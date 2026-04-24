using System.Linq;
using Jellyfin.Plugin.JellyFed.Audit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.JellyFed.Api;

/// <summary>
/// Action filter that validates the federation Bearer token.
/// Apply to any endpoint that should only be accessible to registered peers.
/// </summary>
public sealed class FederationAuthFilter : IActionFilter
{
    private readonly AuditLogService _auditLogService;

    /// <summary>
    /// Initializes a new instance of the <see cref="FederationAuthFilter"/> class.
    /// </summary>
    /// <param name="auditLogService">Audit service.</param>
    public FederationAuthFilter(AuditLogService auditLogService)
    {
        _auditLogService = auditLogService;
    }

    /// <inheritdoc />
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var config = Plugin.Instance?.Configuration;

        if (config is null || string.IsNullOrWhiteSpace(config.FederationToken))
        {
            _auditLogService.WriteSecurityEvent(
                "auth.unavailable",
                "Federation request rejected because the local federation token is not configured.",
                context.HttpContext,
                AuditLogSeverities.Error);
            context.Result = new ObjectResult("Federation token not configured on this instance.")
            {
                StatusCode = 503
            };
            return;
        }

        var authHeader = context.HttpContext.Request.Headers.Authorization.ToString();

        if (!authHeader.StartsWith("Bearer ", System.StringComparison.OrdinalIgnoreCase))
        {
            _auditLogService.WriteSecurityEvent(
                "auth.missing-bearer",
                "Federation request rejected because no Bearer token was provided.",
                context.HttpContext);
            context.Result = new UnauthorizedObjectResult("Bearer token required.");
            return;
        }

        var token = authHeader["Bearer ".Length..].Trim();

        var identity = _auditLogService.ResolveFederationToken(token);
        if (identity is not null)
        {
            FederationRequestIdentityAccessor.Set(context.HttpContext, identity);
            return;
        }

        _auditLogService.WriteSecurityEvent(
            "auth.invalid-token",
            "Federation request rejected because the presented token was invalid.",
            context.HttpContext,
            details: new { providedTokenPrefix = token.Length >= 8 ? token[..8] : token });
        context.Result = new UnauthorizedObjectResult("Invalid federation token.");
    }

    /// <inheritdoc />
    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}
