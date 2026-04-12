using System.Net.Mime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyFed.Api;

/// <summary>
/// JellyFed federation API endpoints.
/// </summary>
[ApiController]
[Route("JellyFed")]
[Produces(MediaTypeNames.Application.Json)]
public class FederationController : ControllerBase
{
    private readonly ILogger<FederationController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FederationController"/> class.
    /// </summary>
    /// <param name="logger">Instance of the <see cref="ILogger{FederationController}"/> interface.</param>
    public FederationController(ILogger<FederationController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Health check endpoint.
    /// </summary>
    /// <returns>Plugin version and status.</returns>
    [HttpGet("health")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> GetHealth()
    {
        return Ok(new
        {
            version = Plugin.Instance?.Version.ToString() ?? "unknown",
            name = "JellyFed",
            status = "ok"
        });
    }

    /// <summary>
    /// Returns the federated catalog of this instance (stub — Phase 1).
    /// </summary>
    /// <returns>Catalog items.</returns>
    [HttpGet("catalog")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public ActionResult GetCatalog()
    {
        _logger.LogInformation("GET /JellyFed/catalog — not yet implemented");
        return StatusCode(StatusCodes.Status501NotImplemented, "Phase 1 — not yet implemented");
    }

    /// <summary>
    /// Returns the list of known peers (stub — Phase 3).
    /// </summary>
    /// <returns>Peer list.</returns>
    [HttpGet("peers")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public ActionResult GetPeers()
    {
        _logger.LogInformation("GET /JellyFed/peers — not yet implemented");
        return StatusCode(StatusCodes.Status501NotImplemented, "Phase 3 — not yet implemented");
    }
}
