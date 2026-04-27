namespace Jellyfin.Plugin.JellyFed.Audit;

/// <summary>
/// A peer facet extracted from current config or audit history.
/// </summary>
public sealed class AuditLogPeerFacet
{
    /// <summary>
    /// Gets or sets the stable peer id.
    /// </summary>
    public string? PeerId { get; set; }

    /// <summary>
    /// Gets or sets the best-known peer name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the best-known peer URL.
    /// </summary>
    public string? Url { get; set; }
}
