using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using MultipleFormat.Application.Modules.TimesAdjustmentRequest;
using MultipleFormat.Application.Modules.TimesAdjustmentRequest.DTOs;
using NewProject.Application.Common.CurrentUser;
using NewProject.Common.Results;
using System.Threading;
using System.Threading.Tasks;

namespace MultipleFormat.Web.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/time-adjustment-request")]
    public class TimeAdjustmentRequestController : ControllerBase
    {
        private readonly TimeAdjustmentRequestService _timeAdjustmentRequestService;
        private readonly ICurrentUserService _currentUserService;

        public TimeAdjustmentRequestController(TimeAdjustmentRequestService timeAdjustmentRequestService, ICurrentUserService currentUserService)
        {
            _timeAdjustmentRequestService = timeAdjustmentRequestService;
            _currentUserService = currentUserService;
        }

        [HttpPost("create-time-adjustment-request")]
        public async Task<IActionResult> CreateTimeAdjustmentRequest(TimeAdjustmentCreateRequestDTO dto)
        {
            ServiceResult<CurrentUserDto> currentUserResult = await _currentUserService.GetCurrentEmployeeAsync(User, HttpContext.RequestAborted);

            if (!currentUserResult.IsSuccess || currentUserResult.Data == null)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = currentUserResult.Message ?? "El usuario no tiene acceso habilitado a la aplicación." });
            }

            CurrentUserDto employee = currentUserResult.Data!;
            if (employee.Id <= 0)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Solo los empleados registrados pueden crear solicitudes." });
            }

            if (employee.AccountId is not int createdById)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "La cuenta del usuario no está configurada." });
            }

            ServiceResult<int> result = await _timeAdjustmentRequestService.CreateTimeAdjustmentRequest(dto, employee.Id, createdById);

            if (!result.IsSuccess)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }

        [HttpGet("get-time-adjustmen")]
        public async Task<IActionResult> GetTimeAdjustent(CancellationToken cancellationToken)
        {
            ServiceResult<CurrentUserDto> currentUserResult = await _currentUserService.GetCurrentEmployeeAsync(User, cancellationToken);

            if (!currentUserResult.IsSuccess || currentUserResult.Data == null)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = currentUserResult.Message ?? "El usuario no tiene acceso habilitado a la aplicación." });
            }

            CurrentUserDto employee = currentUserResult.Data!;

            ServiceResult<List<TimeAdjustmentRequestDto>> result = await _timeAdjustmentRequestService.GetByEmployeeAsync(employee.Id, cancellationToken);

            if (!result.IsSuccess)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }
    }
}
