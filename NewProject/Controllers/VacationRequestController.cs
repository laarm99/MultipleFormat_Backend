using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using MultipleFormat.Application.Modules.VacationsRequest;
using MultipleFormat.Application.Modules.VacationsRequest.DTOs;
using NewProject.Application.Common.CurrentUser;
using NewProject.Common.Results;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MultipleFormat.Web.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/vacation-request")]
    public class VacationRequestController : ControllerBase
    {
        private readonly VacationRequestService _vacationRequestService;
        private readonly ICurrentUserService _currentUserService;

        public VacationRequestController(VacationRequestService vacationRequestService, ICurrentUserService currentUserService)
        {
            _vacationRequestService = vacationRequestService;
            _currentUserService = currentUserService;
        }

        [HttpPost("create-vacation-request")]
        public async Task<IActionResult> CreateVacationRequest(VacationCreateRequestDTO dto)
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

            ServiceResult<int> result = await _vacationRequestService.CreateVacationRequest(dto, employee.Id, createdById);

            if (!result.IsSuccess)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }

        [HttpGet("get-vacation-data")]
        public async Task<IActionResult> GetVacationData(CancellationToken cancellationToken)
        {
            ServiceResult<CurrentUserDto> currentUserResult = await _currentUserService.GetCurrentEmployeeAsync(User, cancellationToken);

            if (!currentUserResult.IsSuccess || currentUserResult.Data == null)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = currentUserResult.Message ?? "El usuario no tiene acceso habilitado a la aplicación." });
            }

            CurrentUserDto employee = currentUserResult.Data!;

            ServiceResult<List<VacationRequestDTO>> result = await _vacationRequestService.GetVacationRequestsByEmployeeAsync(employee.Id, cancellationToken);

            if (!result.IsSuccess)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }

    }
}
