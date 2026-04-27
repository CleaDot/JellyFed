using System;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.JellyFed.Api;

/// <summary>
/// Restricts an endpoint to authenticated Jellyfin administrators.
/// </summary>
public sealed class AdminAccessFilter : IAsyncActionFilter
{
    private readonly IAuthorizationContext _authorizationContext;
    private readonly IUserManager _userManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdminAccessFilter"/> class.
    /// </summary>
    /// <param name="authorizationContext">Jellyfin authorization context.</param>
    /// <param name="userManager">Jellyfin user manager.</param>
    public AdminAccessFilter(
        IAuthorizationContext authorizationContext,
        IUserManager userManager)
    {
        _authorizationContext = authorizationContext;
        _userManager = userManager;
    }

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var auth = await _authorizationContext.GetAuthorizationInfo(context.HttpContext).ConfigureAwait(false);
        if (auth is null || !auth.IsAuthenticated)
        {
            context.Result = new UnauthorizedObjectResult("Admin authentication required.");
            return;
        }

        var user = auth.User;
        if (user is null && auth.UserId != Guid.Empty)
        {
            user = _userManager.GetUserById(auth.UserId);
        }

        if (user is null)
        {
            context.Result = new UnauthorizedObjectResult("Authenticated user could not be resolved.");
            return;
        }

        var remoteIp = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        var userDto = _userManager.GetUserDto(user, remoteIp);
        if (!(userDto.Policy?.IsAdministrator ?? false))
        {
            context.Result = new ForbidResult();
            return;
        }

        await next().ConfigureAwait(false);
    }
}
