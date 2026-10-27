using Microsoft.EntityFrameworkCore;
using MultipleFormat.Application.Modules.FormRequests.DTOs;
using MultipleFormat.Domain.Common.Enums;
using NewProject.Application.Common.CurrentUser;
using NewProject.Common.Results;
using NewProject.Infrastructure.Persistence;

namespace MultipleFormat.Application.Modules.FormRequests
{
    public class FormRequestService
    {
        private readonly MultipleFormatDbContext _ctx;
        private readonly NewProject.Application.Common.Storage.AttachmentStorage _attachments;

        public FormRequestService(
            MultipleFormatDbContext ctx,
            NewProject.Application.Common.Storage.AttachmentStorage attachments)
        {
            _ctx = ctx;
            _attachments = attachments;
        }

        public async Task<ServiceResult<PagedFormRequestResultDto>> GetVisibleAsync(
            CurrentUserDto currentUser,
            FormRequestFilterDto? filter = null,
            CancellationToken cancellationToken = default)
        {
            var roleNames = await GetRoleNamesAsync(currentUser, cancellationToken);

            if (filter != null && (filter.Page < 1 || filter.PageSize < 1 || filter.PageSize > 100))
            {
                return ServiceResult<PagedFormRequestResultDto>.BadRequest(
                    "Page debe ser mayor o igual a 1 y PageSize debe estar entre 1 y 100.");
            }

            if (filter?.StartDate.HasValue == true &&
                filter.EndDate.HasValue &&
                filter.StartDate.Value.Date > filter.EndDate.Value.Date)
            {
                return ServiceResult<PagedFormRequestResultDto>.BadRequest(
                    "La fecha inicial no puede ser posterior a la fecha final.");
            }

            var isAdmin = roleNames.Any(x => x == nameof(Roles.Admin));
            var isSupervisor = roleNames.Any(x => x == nameof(Roles.Supervisor));
            var isGeneralUser = roleNames.Any(x => x == nameof(Roles.GeneralUser));

            if (!isAdmin && !isSupervisor && !isGeneralUser)
            {
                return ServiceResult<PagedFormRequestResultDto>.NotFound("El usuario no tiene un rol habilitado.");
            }

            var query = from formRequest in _ctx.FormRequests.AsNoTracking()
                        join employee in _ctx.Employees.AsNoTracking()
                            on formRequest.EmployeeId equals employee.Id
                        select new
                        {
                            FormRequest = formRequest,
                            Employee = employee
                        };

            if (!isAdmin)
            {
                if (isSupervisor && currentUser.SupervisorId.HasValue)
                {
                    var supervisorId = currentUser.SupervisorId.Value;
                    var ownEmployeeId = isGeneralUser ? currentUser.Id : 0;
                    // Multirol: sus subordinados (sin cambio de depto/turno) y, si ademas es General, sus propias solicitudes.
                    query = query.Where(x =>
                        (x.Employee.SupervisorId == supervisorId &&
                         x.FormRequest.FormType != FormType.DepartmentShiftChange) ||
                        (ownEmployeeId > 0 && x.FormRequest.EmployeeId == ownEmployeeId));
                }
                else if (isGeneralUser && currentUser.Id > 0)
                {
                    query = query.Where(x => x.FormRequest.EmployeeId == currentUser.Id);
                }
                else
                {
                    return ServiceResult<PagedFormRequestResultDto>.Success(new PagedFormRequestResultDto
                    {
                        Page = filter?.Page ?? 1,
                        PageSize = filter?.PageSize ?? 25
                    });
                }
            }

            if (filter?.FormType is FormType formType)
            {
                query = query.Where(x => x.FormRequest.FormType == formType);
            }

            if (filter?.Status is RequestStatus status)
            {
                query = query.Where(x => x.FormRequest.Status == status);
            }

            if (filter?.StartDate is DateTime startDate)
            {
                query = query.Where(x => x.FormRequest.CreatedDate >= startDate);
            }

            if (filter?.EndDate is DateTime endDate)
            {
                var exclusiveEndDate = endDate.Date.AddDays(1);
                query = query.Where(x => x.FormRequest.CreatedDate < exclusiveEndDate);
            }

            if (!string.IsNullOrWhiteSpace(filter?.Search))
            {
                var search = filter.Search.Trim();
                query = query.Where(x =>
                    x.FormRequest.Id.ToString().Contains(search) ||
                    x.Employee.FullName.Contains(search) ||
                    x.Employee.EmployeeNumber.Contains(search));
            }

            var page = filter?.Page ?? 1;
            var pageSize = filter?.PageSize ?? 25;
            var totalCount = await query.CountAsync(cancellationToken);
            var totalPages = totalCount == 0
                ? 0
                : (int)Math.Ceiling(totalCount / (double)pageSize);

            var requests = await query
                .OrderByDescending(x => x.FormRequest.CreatedDate)
                .ThenByDescending(x => x.FormRequest.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new FormRequestSummaryDto
                {
                    Id = x.FormRequest.Id,
                    EmployeeId = x.FormRequest.EmployeeId,
                    EmployeeNumber = x.Employee.EmployeeNumber,
                    EmployeeFullName = x.Employee.FullName,
                    FormType = x.FormRequest.FormType,
                    Status = x.FormRequest.Status,
                    CreatedDate = x.FormRequest.CreatedDate,
                    SubmittedDate = x.FormRequest.SubmittedDate,
                    Comments = x.FormRequest.Comments,
                    Approver = x.Employee.SupervisorName
                })
                .ToListAsync(cancellationToken);

            return ServiceResult<PagedFormRequestResultDto>.Success(new PagedFormRequestResultDto
            {
                Items = requests,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = totalPages
            });
        }

        public async Task<ServiceResult<bool>> CancelAsync(
            int id,
            CurrentUserDto currentUser,
            CancellationToken cancellationToken = default)
        {
            var formRequest = await _ctx.FormRequests.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (formRequest == null || formRequest.EmployeeId != currentUser.Id)
            {
                return ServiceResult<bool>.NotFound("Solicitud no encontrada.");
            }

            if (formRequest.Status != RequestStatus.Submitted)
            {
                return ServiceResult<bool>.BadRequest("Solo se pueden cancelar solicitudes que aún no han sido aprobadas ni rechazadas.");
            }

            formRequest.Status = RequestStatus.Cancelled;
            await _ctx.SaveChangesAsync(cancellationToken);
            return ServiceResult<bool>.Success(true);
        }

        public async Task<ServiceResult<FormRequestDetailDto>> GetVisibleDetailAsync(
            int id,
            CurrentUserDto currentUser,
            CancellationToken cancellationToken = default)
        {
            var roleNames = await GetRoleNamesAsync(currentUser, cancellationToken);
            var isAdmin = roleNames.Any(x => x == nameof(Roles.Admin));
            var isSupervisor = roleNames.Any(x => x == nameof(Roles.Supervisor));
            var isGeneralUser = roleNames.Any(x => x == nameof(Roles.GeneralUser));

            if (!isAdmin && !isSupervisor && !isGeneralUser)
            {
                return ServiceResult<FormRequestDetailDto>.NotFound("El usuario no tiene un rol habilitado.");
            }

            var request = await _ctx.FormRequests
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Join(
                    _ctx.Employees.AsNoTracking(),
                    formRequest => formRequest.EmployeeId,
                    employee => employee.Id,
                    (formRequest, employee) => new { FormRequest = formRequest, Employee = employee })
                .FirstOrDefaultAsync(cancellationToken);

            if (request == null)
            {
                return ServiceResult<FormRequestDetailDto>.NotFound("Formulario no encontrado.");
            }

            var canView = isAdmin ||
                (isSupervisor &&
                 currentUser.SupervisorId.HasValue &&
                 request.Employee.SupervisorId == currentUser.SupervisorId.Value &&
                 request.FormRequest.FormType != FormType.DepartmentShiftChange) ||
                (isGeneralUser && currentUser.Id > 0 && request.FormRequest.EmployeeId == currentUser.Id);

            if (!canView)
            {
                return ServiceResult<FormRequestDetailDto>.NotFound("Formulario no encontrado.");
            }

            ApproverDto? approver = null;
            if (request.FormRequest.ApproverId.HasValue)
            {
                var approverAccount = await _ctx.IDMAccounts
                    .AsNoTracking()
                    .Where(x => x.Id == request.FormRequest.ApproverId.Value && x.IsActive)
                    .Select(x => new { x.Id, x.FullName, x.DomainUserName })
                    .FirstOrDefaultAsync(cancellationToken);

                if (approverAccount != null)
                {
                    var approverEmployee = await _ctx.Employees
                        .AsNoTracking()
                        .Where(x => x.DomainUserName == approverAccount.DomainUserName)
                        .Select(x => new { x.Id, x.Position })
                        .FirstOrDefaultAsync(cancellationToken);

                    approver = new ApproverDto
                    {
                        Id = approverEmployee?.Id ?? 0,
                        Name = approverAccount.FullName,
                        Position = approverEmployee?.Position
                    };
                }
            }

            var detail = new FormRequestDetailDto
            {
                Id = request.FormRequest.Id,
                EmployeeId = request.FormRequest.EmployeeId,
                EmployeeNumber = request.Employee.EmployeeNumber,
                EmployeeFullName = request.Employee.FullName,
                DomainUserName = request.Employee.DomainUserName,
                FormType = request.FormRequest.FormType,
                Status = request.FormRequest.Status,
                CreatedDate = request.FormRequest.CreatedDate,
                SubmittedDate = request.FormRequest.SubmittedDate,
                Approver = approver,
                Comments = request.FormRequest.Comments
            };

            switch (request.FormRequest.FormType)
            {
                case FormType.Vacation:
                    var vacation = await _ctx.VacationRequests
                        .AsNoTracking()
                        .Where(x => x.FormRequestId == id)
                        .Select(x => new
                        {
                            x.VacationDays,
                            x.StartDate,
                            x.EndDate,
                            x.ReturnDate
                        })
                        .FirstOrDefaultAsync(cancellationToken);

                    if (vacation != null)
                    {
                        detail.VacationDays = vacation.VacationDays;
                        detail.StartDate = vacation.StartDate;
                        detail.EndDate = vacation.EndDate;
                        detail.ReturnDate = vacation.ReturnDate;
                    }
                    break;

                case FormType.Permission:
                    var permission = await _ctx.PermissionRequests
                        .AsNoTracking()
                        .Where(x => x.FormRequestId == id)
                        .Select(x => new
                        {
                            x.PermissionType,
                            x.PermissionDays,
                            x.StartDate,
                            x.EndDate,
                            x.ReturnDate,
                            x.SwapDetail
                        })
                        .FirstOrDefaultAsync(cancellationToken);

                    if (permission != null)
                    {
                        detail.PermissionType = permission.PermissionType;
                        detail.PermissionDays = permission.PermissionDays;
                        detail.StartDate = permission.StartDate;
                        detail.EndDate = permission.EndDate;
                        detail.ReturnDate = permission.ReturnDate;
                        detail.SwapDetail = permission.SwapDetail;
                        detail.Attachments = _attachments.List(id)
                            .Select(x => new FormRequestAttachmentDto { Id = x.Id, Name = x.Name, FileName = x.Name })
                            .ToList();
                    }
                    break;

                case FormType.TimeAdjustment:
                    var adjustment = await _ctx.TimeAdjustmentRequests
                        .AsNoTracking()
                        .Where(x => x.FormRequestId == id)
                        .Select(x => new
                        {
                            x.AdjustmentDate,
                            x.MissingPunchType,
                            x.EntryTime,
                            x.ExitTime,
                            x.Justification
                        })
                        .FirstOrDefaultAsync(cancellationToken);

                    if (adjustment != null)
                    {
                        detail.AdjustmentDate = adjustment.AdjustmentDate;
                        detail.MissingPunchType = adjustment.MissingPunchType;
                        detail.EntryTime = adjustment.EntryTime;
                        detail.ExitTime = adjustment.ExitTime;
                        detail.Justification = adjustment.Justification;
                    }
                    break;

                case FormType.DepartmentShiftChange:
                    var change = await _ctx.DepartmentShiftChangeRequests
                        .AsNoTracking()
                        .Where(x => x.FormRequestId == id)
                        .Select(x => new
                        {
                            x.EffectiveDate,
                            x.NewPlant,
                            x.NewShift,
                            x.NewDepartment,
                            x.NewSupervisor
                        })
                        .FirstOrDefaultAsync(cancellationToken);

                    if (change != null)
                    {
                        detail.EffectiveDate = change.EffectiveDate;
                        detail.NewPlant = change.NewPlant;
                        detail.NewShift = change.NewShift;
                        detail.NewDepartment = change.NewDepartment;
                        detail.NewSupervisor = change.NewSupervisor;
                    }
                    break;
            }

            return ServiceResult<FormRequestDetailDto>.Success(detail);
        }

        private async Task<List<string>> GetRoleNamesAsync(
            CurrentUserDto currentUser,
            CancellationToken cancellationToken)
        {
            var userName = currentUser.DomainUserName.Trim().ToUpperInvariant();

            return await (from account in _ctx.IDMAccounts.AsNoTracking()
                          join accountRole in _ctx.IDMAccountRoles.AsNoTracking()
                              on account.Id equals accountRole.AccountId
                          join role in _ctx.IDMRoles.AsNoTracking()
                              on accountRole.RoleId equals role.Id
                          where account.IsActive &&
                                role.IsActive &&
                                account.DomainUserName == userName
                          select role.RoleName)
                .ToListAsync(cancellationToken);
        }
    }
}
