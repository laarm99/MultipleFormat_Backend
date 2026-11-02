using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MultipleFormat.Domain.Common.Enums;
using MultipleFormat.Domain.Entities;
using NewProject.Domain.Entities.IDM;
using NewProject.Infrastructure.Persistence;
using MultipleFormat.Web.Controllers.DTOs;
using NewProject.Application.Common.CurrentUser;
using NewProject.Common.Results;
using MultipleFormat.Application.Modules.Employees;
using System.Security.Principal;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

[Authorize]
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private static readonly string DefaultRoleName =
        nameof(Roles.GeneralUser);
    private static readonly string SupervisorRoleName =
        nameof(Roles.Supervisor);
    private readonly MultipleFormatDbContext _ctx;
    private readonly ICurrentUserService _currentUserService;

    public AuthController(
        MultipleFormatDbContext ctx,
        ICurrentUserService currentUserService)
    {
        _ctx = ctx;
        _currentUserService = currentUserService;
    }

    [AllowAnonymous]
    [HttpGet("login")]
    public async Task<IActionResult> Login(CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Unauthorized();
        }

        ServiceResult<CurrentUserDto> currentUserResult = await _currentUserService.GetCurrentEmployeeAsync(User, cancellationToken);

        if (!currentUserResult.IsSuccess || currentUserResult.Data == null)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    message = "El usuario no está registrado en el sistema. Por favor, contacte a recursos humanos/nóminas para darse de alta."
                });
        }

        CurrentUserDto user = currentUserResult.Data;
        string userName = user.DomainUserName;

        // Buscar también cuentas inactivas para no duplicarlas.
        IDMAccount account = await _ctx.IDMAccounts
            .FirstOrDefaultAsync(x => x.DomainUserName == userName, cancellationToken);

        if (account != null)
        {
            return await BuildAccountResponseAsync(account, cancellationToken);
        }

        // Empleado => GeneralUser; supervisor => Supervisor (puede ser ambos).
        List<string> roleNames = new List<string>();
        if (user.Id > 0)
        {
            roleNames.Add(DefaultRoleName);
        }

        if (user.IsSupervisor)
        {
            roleNames.Add(SupervisorRoleName);
        }

        List<IDMRole> roles = await _ctx.IDMRoles
            .Where(x => roleNames.Contains(x.RoleName) && x.IsActive)
            .ToListAsync(cancellationToken);

        if (roles.Count == 0)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new
                {
                    message =
                        "El acceso no está disponible por un problema de configuración."
                });
        }

        IDMAccount newAccount = new IDMAccount
        {
            DomainUserName = userName,
            FullName = user.FullName,
            Email = user.Email,
            IsActive = true,
            CreatedDate = DateTime.UtcNow
        };

        _ctx.IDMAccounts.Add(newAccount);
        foreach (IDMRole role in roles)
        {
            _ctx.IDMAccountRoles.Add(new IDMAccountRole { Account = newAccount, Role = role });
        }

        try
        {
            await _ctx.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Posible creación concurrente: recuperar la cuenta existente.
            IDMAccount existing = await _ctx.IDMAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.DomainUserName == userName, cancellationToken);

            if (existing != null)
            {
                return await BuildAccountResponseAsync(existing, cancellationToken);
            }

            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Error creando la cuenta." });
        }

        return await BuildAccountResponseAsync(newAccount, cancellationToken);
    }

    private async Task<IActionResult> BuildAccountResponseAsync(IDMAccount account, CancellationToken cancellationToken)
    {
        if (!account.IsActive)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new { message = "El usuario no tiene acceso habilitado a la aplicación." });
        }

        List<string> roles = await (from accountRole in _ctx.IDMAccountRoles.AsNoTracking()
                                    join role in _ctx.IDMRoles.AsNoTracking()
                                        on accountRole.RoleId equals role.Id
                                    where accountRole.AccountId == account.Id && role.IsActive
                                    select role.RoleName)
            .ToListAsync(cancellationToken);

        return Ok(new IdmAccountDto
        {
            DomainUserName = account.DomainUserName,
            FullName = account.FullName,
            Email = account.Email,
            IsActive = account.IsActive,
            Roles = roles,
            CreatedDate = account.CreatedDate
        });
    }
}