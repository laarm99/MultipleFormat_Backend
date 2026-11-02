using Microsoft.EntityFrameworkCore;
using NewProject.Application.Modules.Users.DTOs;
using NewProject.Common.Results;
using NewProject.Infrastructure.Persistence;
using NewProject.Domain.Entities.IDM;
using MultipleFormat.Application.Modules.Supervisors;

namespace NewProject.Application.Modules.Users
{
    public class UserService
    {
        private readonly MultipleFormatDbContext _ctx;

        public UserService(MultipleFormatDbContext ctx)
        {
            _ctx = ctx;
        }

        public async Task<ServiceResult<List<UserDto>>> GetAsync(
            bool includeInactive = true,
            CancellationToken cancellationToken = default)
        {
            // El Id expuesto es MFR_EMPLOYEES.ID; la relación con IDM_ACCOUNT es por DOMAIN_USER_NAME.
            var rows = await (from account in _ctx.IDMAccounts.AsNoTracking()
                              join employee in _ctx.Employees.AsNoTracking()
                                  on account.DomainUserName equals employee.DomainUserName
                              where includeInactive || account.IsActive
                              orderby account.FullName
                              select new { Account = account, EmployeeId = employee.Id, Employee = employee })
                .ToListAsync(cancellationToken);

            var userDtos = new List<UserDto>();

            foreach (var row in rows)
            {
                var account = row.Account;
                var roles = await _ctx.IDMAccountRoles
                    .AsNoTracking()
                    .Where(x => x.AccountId == account.Id)
                    .Join(
                        _ctx.IDMRoles.AsNoTracking(),
                        ar => ar.RoleId,
                        r => r.Id,
                        (ar, r) => r.RoleName)
                    .ToListAsync(cancellationToken);

                userDtos.Add(new UserDto
                {
                    Id = row.EmployeeId,
                    DomainUserName = account.DomainUserName,
                    FullName = account.FullName,
                    Email = account.Email,
                    IsActive = account.IsActive,
                    Roles = roles,
                    CreatedDate = account.CreatedDate,
                    EmployeeNumber = row.Employee.EmployeeNumber,
                    Shift = row.Employee.Shift,
                    Department = row.Employee.Department,
                    Position = row.Employee.Position,
                    HireDate = row.Employee.HireDate,
                    SupervisorName = row.Employee.SupervisorName
                });
            }

            return ServiceResult<List<UserDto>>.Success(userDtos);
        }

        public async Task<ServiceResult<UserDto>> GetByIdAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            var account = await FindAccountByEmployeeIdAsync(id, false, cancellationToken);

            if (account == null)
            {
                return ServiceResult<UserDto>.NotFound("Usuario no encontrado.");
            }

            var roles = await _ctx.IDMAccountRoles
                .AsNoTracking()
                .Where(x => x.AccountId == account.Id)
                .Join(
                    _ctx.IDMRoles.AsNoTracking(),
                    ar => ar.RoleId,
                    r => r.Id,
                    (ar, r) => r.RoleName)
                .ToListAsync(cancellationToken);

            var userDto = new UserDto
            {
                Id = id,
                DomainUserName = account.DomainUserName,
                FullName = account.FullName,
                Email = account.Email,
                IsActive = account.IsActive,
                Roles = roles,
                CreatedDate = account.CreatedDate
            };

            return ServiceResult<UserDto>.Success(userDto);
        }

        public async Task<ServiceResult<UserDto>> UpdateAsync(
            int id,
            UpdateUserRequest request,
            int? currentUserId = null,
            CancellationToken cancellationToken = default)
        {
            var roleName = request.Role.Trim();
            var role = await _ctx.IDMRoles
                .FirstOrDefaultAsync(x => x.IsActive && x.RoleName == roleName, cancellationToken);
            if (role == null)
            {
                return ServiceResult<UserDto>.BadRequest("El rol indicado no existe o está inactivo.");
            }

            var account = await FindAccountByEmployeeIdAsync(id, true, cancellationToken);
            if (account == null)
            {
                return ServiceResult<UserDto>.NotFound("Usuario no encontrado.");
            }

            var accountRoles = await _ctx.IDMAccountRoles
                .Where(x => x.AccountId == account.Id)
                .ToListAsync(cancellationToken);
            _ctx.IDMAccountRoles.RemoveRange(accountRoles.Where(x => x.RoleId != role.Id));
            if (!accountRoles.Any(x => x.RoleId == role.Id))
            {
                _ctx.IDMAccountRoles.Add(new IDMAccountRole
                {
                    AccountId = account.Id,
                    RoleId = role.Id
                });
            }

            account.UpdatedDate = DateTime.UtcNow;
            account.UpdatedBy = currentUserId;

            await _ctx.SaveChangesAsync(cancellationToken);
            return await GetByIdAsync(id, cancellationToken);
        }

        public async Task<ServiceResult<UserDto>> UpdateRolesAsync(
            int id,
            UpdateUserRolesRequest request,
            int? currentUserId = null,
            CancellationToken cancellationToken = default)
        {
            // Validar que la lista no esté vacía
            if (request.Roles == null || request.Roles.Count == 0)
            {
                return ServiceResult<UserDto>.BadRequest("El usuario debe tener al menos un rol.");
            }

            // Validar que todos los roles existan y estén activos
            var roleNames = request.Roles.Select(r => r.Trim()).Distinct().ToList();
            var roles = await _ctx.IDMRoles
                .Where(x => x.IsActive && roleNames.Contains(x.RoleName))
                .ToListAsync(cancellationToken);

            if (roles.Count != roleNames.Count)
            {
                return ServiceResult<UserDto>.BadRequest("Uno o más roles indicados no existen o están inactivos.");
            }

            // Verificar que la cuenta existe
            var account = await FindAccountByEmployeeIdAsync(id, true, cancellationToken);
            if (account == null)
            {
                return ServiceResult<UserDto>.NotFound("Usuario no encontrado.");
            }

            // Sincronizar por diferencias: la llave es (AccountId, RoleId) y no se puede borrar y reinsertar el mismo par.
            var existingAccountRoles = await _ctx.IDMAccountRoles
                .Where(x => x.AccountId == account.Id)
                .ToListAsync(cancellationToken);
            var targetRoleIds = roles.Select(r => r.Id).ToHashSet();

            _ctx.IDMAccountRoles.RemoveRange(
                existingAccountRoles.Where(x => !targetRoleIds.Contains(x.RoleId)));

            var existingRoleIds = existingAccountRoles.Select(x => x.RoleId).ToHashSet();
            foreach (var role in roles.Where(r => !existingRoleIds.Contains(r.Id)))
            {
                _ctx.IDMAccountRoles.Add(new IDMAccountRole
                {
                    AccountId = account.Id,
                    RoleId = role.Id
                });
            }

            account.UpdatedDate = DateTime.UtcNow;
            account.UpdatedBy = currentUserId;

            await _ctx.SaveChangesAsync(cancellationToken);
            return await GetByIdAsync(id, cancellationToken);
        }

        public async Task<ServiceResult<UserDto>> ToggleActiveAsync(
            int id,
            bool isActive,
            int? currentUserId = null,
            CancellationToken cancellationToken = default)
        {
            var account = await FindAccountByEmployeeIdAsync(id, true, cancellationToken);

            if (account == null)
            {
                return ServiceResult<UserDto>.NotFound("Usuario no encontrado.");
            }

            account.IsActive = isActive;
            account.UpdatedDate = DateTime.UtcNow;
            account.UpdatedBy = currentUserId;

            await _ctx.SaveChangesAsync(cancellationToken);

            return await GetByIdAsync(id, cancellationToken);
        }

        private async Task<IDMAccount?> FindAccountByEmployeeIdAsync(
            int employeeId,
            bool track,
            CancellationToken cancellationToken)
        {
            var domainUserName = await _ctx.Employees
                .AsNoTracking()
                .Where(x => x.Id == employeeId)
                .Select(x => x.DomainUserName)
                .FirstOrDefaultAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(domainUserName))
            {
                return null;
            }

            var query = track ? _ctx.IDMAccounts : _ctx.IDMAccounts.AsNoTracking();
            return await query.FirstOrDefaultAsync(x => x.DomainUserName == domainUserName, cancellationToken);
        }
            }
        }
