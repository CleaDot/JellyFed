using Jellyfin.Plugin.JellyFed.Audit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyFed.Api;

/// <summary>
/// Admin-only JellyFed audit log endpoints.
/// </summary>
[ApiController]
[Route("JellyFed/logs")]
[AllowAnonymous]
[ServiceFilter(typeof(AdminAccessFilter))]
public sealed class AuditLogsController : ControllerBase
{
    private readonly AuditLogService _auditLogService;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditLogsController"/> class.
    /// </summary>
    /// <param name="auditLogService">Audit service.</param>
    public AuditLogsController(AuditLogService auditLogService)
    {
        _auditLogService = auditLogService;
    }

    /// <summary>
    /// Returns counters and peer facets for the logs dashboard.
    /// </summary>
    /// <returns>Overview data.</returns>
    [HttpGet("overview")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<AuditLogOverview> GetOverview()
        => Ok(_auditLogService.GetOverview());

    /// <summary>
    /// Returns a paged feed of audit records.
    /// </summary>
    /// <param name="scope">Logical scope: all/security/peer-connections/peer-access.</param>
    /// <param name="peerId">Optional peer filter.</param>
    /// <param name="limit">Maximum page size.</param>
    /// <param name="beforeId">Pagination cursor.</param>
    /// <returns>Paged audit records.</returns>
    [HttpGet("feed")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<AuditLogFeed> GetFeed(
        [FromQuery] string scope = "all",
        [FromQuery] string? peerId = null,
        [FromQuery] int limit = 100,
        [FromQuery] long? beforeId = null)
        => Ok(_auditLogService.Query(new AuditLogQuery
        {
            Scope = scope,
            PeerId = peerId,
            Limit = limit,
            BeforeId = beforeId
        }));
}
