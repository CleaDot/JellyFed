namespace Jellyfin.Plugin.JellyFed.Audit;

/// <summary>
/// Query parameters for browsing audit events.
/// </summary>
public sealed class AuditLogQuery
{
    /// <summary>
    /// Gets or sets the logical scope: all / security / peer-connections / peer-access.
    /// </summary>
    public string Scope { get; set; } = "all";

    /// <summary>
    /// Gets or sets an optional peer id filter.
    /// </summary>
    public string? PeerId { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of items to return.
    /// </summary>
    public int Limit { get; set; } = 100;

    /// <summary>
    /// Gets or sets the exclusive upper-bound id for pagination.
    /// </summary>
    public long? BeforeId { get; set; }
}
