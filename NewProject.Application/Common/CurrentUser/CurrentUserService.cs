using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MultipleFormat.Domain.Entities;
using NewProject.Common.Results;
using NewProject.Infrastructure.Persistence;

namespace NewProject.Application.Common.CurrentUser
{
    public class CurrentUserService : ICurrentUserService
    {
        private readonly MultipleFormatDbContext _ctx;

        public CurrentUserService(MultipleFormatDbContext ctx)
        {
            _ctx = ctx;
        }

        public async Task<ServiceResult<CurrentUserDto>> GetCurrentEmployeeAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
        {
            var identity = user?.Identity;
            if (identity?.IsAuthenticated != true)
            {
                return ServiceResult<CurrentUserDto>.NotFound("User not authenticated.");
            }

            var userName = NormalizeUserName(identity.Name);
            if (string.IsNullOrEmpty(userName))
            {
                return ServiceResult<CurrentUserDto>.NotFound("Invalid user name.");
            }

            var employee = await _ctx.Set<Employee>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.DomainUserName == userName && x.IsActive, cancellationToken);

            var supervisor = await _ctx.Supervisors
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.DomainUserName == userName && x.IsActive, cancellationToken);

            if (employee == null && supervisor == null)
            {
                return ServiceResult<CurrentUserDto>.NotFound("Employee not found or inactive.");
            }

            var account = await _ctx.IDMAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.DomainUserName == userName && x.IsActive, cancellationToken);

            var dto = new CurrentUserDto
            {
                Id = employee?.Id ?? 0,
                EmployeeNumber = employee?.EmployeeNumber ?? string.Empty,
                DomainUserName = employee?.DomainUserName ?? supervisor!.DomainUserName!,
                FullName = employee?.FullName ?? supervisor!.FullName,
                Email = employee?.Email ?? supervisor!.Email ?? string.Empty,
                IsActive = employee?.IsActive ?? supervisor!.IsActive,
                AccountId = account?.Id,
                SupervisorId = supervisor?.Id,
                IsSupervisor = supervisor != null
            };

            return ServiceResult<CurrentUserDto>.Success(dto);
        }

        public async Task<int?> GetAccountIdAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
        {
            var userName = NormalizeUserName(user?.Identity?.Name);
            if (string.IsNullOrEmpty(userName))
            {
                return null;
            }

            return await _ctx.IDMAccounts
                .AsNoTracking()
                .Where(x => x.DomainUserName == userName && x.IsActive)
                .Select(x => (int?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<bool> IsAdministratorAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
        {
            var userName = NormalizeUserName(user?.Identity?.Name);
            if (string.IsNullOrEmpty(userName))
            {
                return false;
            }

            return await (from account in _ctx.IDMAccounts.AsNoTracking()
                          join accountRole in _ctx.IDMAccountRoles.AsNoTracking()
                              on account.Id equals accountRole.AccountId
                          join role in _ctx.IDMRoles.AsNoTracking()
                              on accountRole.RoleId equals role.Id
                          where account.IsActive && role.IsActive &&
                                account.DomainUserName == userName &&
                                role.RoleName == "Admin"
                          select account.Id).AnyAsync(cancellationToken);
        }

        // Quita el dominio (DOMINIO\usuario) y pasa a mayúsculas.
        private static string NormalizeUserName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            var separator = trimmed.LastIndexOf('\\');
            if (separator >= 0)
            {
                trimmed = trimmed[(separator + 1)..].Trim();
            }

            return trimmed.ToUpperInvariant();
        }
    }
}
