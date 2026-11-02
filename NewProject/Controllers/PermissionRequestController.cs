using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MultipleFormat.Application.Modules.PermissionsRequest;
using MultipleFormat.Application.Modules.PermissionsRequest.DTOs;
using NewProject.Application.Common.CurrentUser;
using NewProject.Common.Results;

namespace MultipleFormat.Web.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/permission-request")]
    public class PermissionRequestController : ControllerBase
    {
        private readonly PermissionRequestService _permissionRequestService;
        private readonly ICurrentUserService _currentUserService;

        public PermissionRequestController(PermissionRequestService permissionRequestService, ICurrentUserService currentUserService)
        {
            _permissionRequestService = permissionRequestService;
            _currentUserService = currentUserService;
        }

        [HttpPost("create-permission-request")]
        public async Task<IActionResult> CreatePermissionRequest([FromBody] PermissionCreateRequestDTO dto)
        {
            var currentUserResult = await _currentUserService.GetCurrentEmployeeAsync(User, HttpContext.RequestAborted);
            if (!currentUserResult.IsSuccess || currentUserResult.Data is not { AccountId: int createdById } currentUser)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "La cuenta del usuario no está configurada." });
            }

            if (currentUserResult.Data.Id <= 0)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Solo los empleados registrados pueden crear solicitudes." });
            }

            if (dto.EmployeeId != 0 && dto.EmployeeId != currentUser.Id)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Solo puede crear solicitudes para sí mismo." });
            }

            dto.EmployeeId = currentUser.Id;

            var result = await _permissionRequestService.CreatePermissionRequest(dto, createdById);
            if (!result.IsSuccess)
            {
                return BadRequest(result);
            }
            return Ok(result);
        }

        [HttpGet("get-permission-data")]
        public async Task<IActionResult> GetPermissionData(CancellationToken cancellationToken)
        {
            ServiceResult<CurrentUserDto> currentUserResult = await _currentUserService.GetCurrentEmployeeAsync(User, cancellationToken);

            if (!currentUserResult.IsSuccess || currentUserResult.Data == null)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = currentUserResult.Message ?? "El usuario no tiene acceso habilitado a la aplicación." });
            }

            int employeeId = currentUserResult.Data.Id!;

            var result = await _permissionRequestService.GetPermissionRequestByEmployeeAsync(employeeId, cancellationToken);
            if (!result.IsSuccess)
            {
                return BadRequest(result);
            }
            return Ok(result);
        }
    }
}
