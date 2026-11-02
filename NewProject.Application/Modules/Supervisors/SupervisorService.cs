using Microsoft.EntityFrameworkCore;
using MultipleFormat.Application.Modules.Supervisors.DTOs;
using MultipleFormat.Domain.Entities;
using NewProject.Common.Results;
using NewProject.Domain.Entities.IDM;
using NewProject.Infrastructure.Persistence;
using System.Globalization;
using System.Net.Mail;
using System.Text;

namespace MultipleFormat.Application.Modules.Supervisors
{
    public class SupervisorService
    {
        private readonly MultipleFormatDbContext _ctx;

        public SupervisorService(MultipleFormatDbContext ctx)
        {
            _ctx = ctx;
        }

        public async Task<ServiceResult<List<SupervisorDto>>> GetAsync(
            bool includeInactive = true,
            CancellationToken cancellationToken = default)
        {
            var supervisors = await _ctx.Supervisors
                .AsNoTracking()
                .Where(x => includeInactive || x.IsActive)
                .OrderBy(x => x.FullName)
                .Select(x => new SupervisorDto
                {
                    Id = x.Id,
                    FullName = x.FullName,
                    DomainUserName = x.DomainUserName,
                    Email = x.Email,
                    IsActive = x.IsActive,
                    EmployeeNumber = _ctx.Employees.Where(e => e.DomainUserName == x.DomainUserName).Select(e => e.EmployeeNumber).FirstOrDefault(),
                    Shift = _ctx.Employees.Where(e => e.DomainUserName == x.DomainUserName).Select(e => e.Shift).FirstOrDefault(),
                    Department = _ctx.Employees.Where(e => e.DomainUserName == x.DomainUserName).Select(e => e.Department).FirstOrDefault(),
                    Position = _ctx.Employees.Where(e => e.DomainUserName == x.DomainUserName).Select(e => e.Position).FirstOrDefault(),
                    HireDate = _ctx.Employees.Where(e => e.DomainUserName == x.DomainUserName).Select(e => e.HireDate).FirstOrDefault(),
                    SupervisorName = _ctx.Employees.Where(e => e.DomainUserName == x.DomainUserName).Select(e => e.SupervisorName).FirstOrDefault()
                })
                .ToListAsync(cancellationToken);

            return ServiceResult<List<SupervisorDto>>.Success(supervisors);
        }

        public async Task<ServiceResult<SupervisorDto>> GetByIdAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            var supervisor = await _ctx.Supervisors
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new SupervisorDto
                {
                    Id = x.Id,
                    FullName = x.FullName,
                    DomainUserName = x.DomainUserName,
                    Email = x.Email,
                    IsActive = x.IsActive
                })
                .FirstOrDefaultAsync(cancellationToken);

            return supervisor == null
                ? ServiceResult<SupervisorDto>.NotFound("Supervisor no encontrado.")
                : ServiceResult<SupervisorDto>.Success(supervisor);
        }

        public async Task<ServiceResult<SupervisorDto>> UpdateAsync(
            int id,
            UpdateSupervisorRequest request,
            int? currentUserId = null,
            CancellationToken cancellationToken = default)
        {
            var domainUserName = NormalizeDomainUserName(request.DomainUserName);
            if (string.IsNullOrEmpty(domainUserName))
            {
                return ServiceResult<SupervisorDto>.BadRequest("El NetKey es requerido.");
            }

            var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
            if (email != null && !MailAddress.TryCreate(email, out _))
            {
                return ServiceResult<SupervisorDto>.BadRequest("El correo electrónico no es válido.");
            }

            var supervisor = await _ctx.Supervisors
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (supervisor == null)
            {
                return ServiceResult<SupervisorDto>.NotFound("Supervisor no encontrado.");
            }

            if (await _ctx.Supervisors.AnyAsync(
                    x => x.Id != id && x.DomainUserName == domainUserName,
                    cancellationToken))
            {
                return ServiceResult<SupervisorDto>.Conflict("El NetKey ya está asignado a otro supervisor.");
            }

            if (email != null && await _ctx.Supervisors.AnyAsync(
                    x => x.Id != id && x.Email != null && x.Email.ToUpper() == email.ToUpper(),
                    cancellationToken))
            {
                return ServiceResult<SupervisorDto>.Conflict("El correo ya está asignado a otro supervisor.");
            }

            supervisor.DomainUserName = domainUserName;
            supervisor.Email = email;
            supervisor.UpdatedDate = DateTime.UtcNow;
            supervisor.UpdatedBy = currentUserId;
            await _ctx.SaveChangesAsync(cancellationToken);

            return await GetByIdAsync(id, cancellationToken);
        }

        public async Task<ServiceResult<int>> SyncFromEmployeesAsync(CancellationToken cancellationToken = default)
        {
            var names = await _ctx.Employees
                .AsNoTracking()
                .Where(x => x.SupervisorName != null && x.SupervisorName.Trim() != "")
                .Select(x => x.SupervisorName!)
                .Distinct()
                .ToListAsync(cancellationToken);

            var supervisorsByName = await _ctx.Supervisors
                .ToDictionaryAsync(x => x.NormalizedName, cancellationToken);

            var created = 0;
            foreach (var name in names)
            {
                var normalizedName = NormalizeName(name);
                if (string.IsNullOrEmpty(normalizedName) || supervisorsByName.ContainsKey(normalizedName))
                {
                    continue;
                }

                var supervisor = new Supervisor
                {
                    FullName = name.Trim(),
                    NormalizedName = normalizedName,
                    IsActive = true,
                    CreatedDate = DateTime.UtcNow
                };

                _ctx.Supervisors.Add(supervisor);
                supervisorsByName.Add(normalizedName, supervisor);
                created++;
            }

            await _ctx.SaveChangesAsync(cancellationToken);

            var employees = await _ctx.Employees
                .Where(x => x.SupervisorName != null && x.SupervisorName.Trim() != "")
                .ToListAsync(cancellationToken);

            foreach (var employee in employees)
            {
                var normalizedName = NormalizeName(employee.SupervisorName);
                if (!string.IsNullOrEmpty(normalizedName) &&
                    supervisorsByName.TryGetValue(normalizedName, out var supervisor) &&
                    employee.SupervisorId != supervisor.Id)
                {
                    employee.SupervisorId = supervisor.Id;
                }
            }

            await _ctx.SaveChangesAsync(cancellationToken);

            // Si el supervisor también es empleado, completar usuario de dominio y correo.
            var allEmployees = await _ctx.Employees
                .AsNoTracking()
                .OrderBy(x => x.Id)
                .ToListAsync(cancellationToken);
            var employeesByName = new Dictionary<string, Employee>();
            foreach (var emp in allEmployees)
            {
                employeesByName.TryAdd(NormalizeName(emp.FullName), emp);
            }

            var usedUserNames = supervisorsByName.Values
                .Where(x => x.DomainUserName != null)
                .Select(x => x.DomainUserName!)
                .ToHashSet();

            foreach (var supervisor in supervisorsByName.Values.Where(x => x.DomainUserName == null))
            {
                if (employeesByName.TryGetValue(supervisor.NormalizedName, out var match) &&
                    !string.IsNullOrWhiteSpace(match.DomainUserName) &&
                    usedUserNames.Add(match.DomainUserName))
                {
                    supervisor.DomainUserName = match.DomainUserName;
                    supervisor.Email = string.IsNullOrWhiteSpace(match.Email) ? null : match.Email;
                }
            }

            // Rol Supervisor para los que ya tienen cuenta IDM.
            var supervisorRole = await _ctx.IDMRoles
                .FirstOrDefaultAsync(x => x.RoleName == "Supervisor" && x.IsActive, cancellationToken);
            if (supervisorRole != null)
            {
                var userNames = supervisorsByName.Values
                    .Where(x => x.DomainUserName != null)
                    .Select(x => x.DomainUserName!)
                    .ToList();
                var accountIds = await _ctx.IDMAccounts
                    .Where(x => userNames.Contains(x.DomainUserName))
                    .Select(x => x.Id)
                    .ToListAsync(cancellationToken);
                var withRole = (await _ctx.IDMAccountRoles
                    .Where(x => x.RoleId == supervisorRole.Id)
                    .Select(x => x.AccountId)
                    .ToListAsync(cancellationToken)).ToHashSet();

                foreach (var accountId in accountIds.Where(x => !withRole.Contains(x)))
                {
                    _ctx.IDMAccountRoles.Add(new IDMAccountRole
                    {
                        AccountId = accountId,
                        RoleId = supervisorRole.Id
                    });
                }
            }

            await _ctx.SaveChangesAsync(cancellationToken);
            return ServiceResult<int>.Success(created);
        }

        public async Task<ServiceResult<bool>> AssignEmployeeAsync(
            int employeeId,
            int supervisorId,
            CancellationToken cancellationToken = default)
        {
            var employee = await _ctx.Employees
                .FirstOrDefaultAsync(x => x.Id == employeeId, cancellationToken);

            if (employee == null)
            {
                return ServiceResult<bool>.NotFound("Empleado no encontrado.");
            }

            var supervisorExists = await _ctx.Supervisors
                .AnyAsync(x => x.Id == supervisorId && x.IsActive, cancellationToken);

            if (!supervisorExists)
            {
                return ServiceResult<bool>.NotFound("Supervisor no encontrado o inactivo.");
            }

            employee.SupervisorId = supervisorId;
            await _ctx.SaveChangesAsync(cancellationToken);

            return ServiceResult<bool>.Success(true);
        }

        public static string NormalizeDomainUserName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var userName = value.Trim();
            var separatorIndex = userName.LastIndexOf('\\');
            if (separatorIndex >= 0 && separatorIndex < userName.Length - 1)
            {
                userName = userName[(separatorIndex + 1)..];
            }

            return userName.Trim().ToUpperInvariant();
        }

        public static string NormalizeName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = value.Trim().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);
            foreach (var character in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(character);
                }
            }

            return string.Join(' ', builder.ToString()
                .Normalize(NormalizationForm.FormC)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .ToUpperInvariant();
        }

        public async Task<ServiceResult<SupervisorDto>> ToggleActiveAsync(
            int id,
            bool isActive,
            int? currentUserId = null,
            CancellationToken cancellationToken = default)
        {
            var supervisor = await _ctx.Supervisors
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

            if (supervisor == null)
            {
                return ServiceResult<SupervisorDto>.NotFound("Supervisor no encontrado.");
            }

            supervisor.IsActive = isActive;
            supervisor.UpdatedDate = DateTime.UtcNow;
            supervisor.UpdatedBy = currentUserId;

            await _ctx.SaveChangesAsync(cancellationToken);

            return await GetByIdAsync(id, cancellationToken);
        }
    }
}
