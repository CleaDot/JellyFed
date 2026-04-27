namespace Jellyfin.Plugin.JellyFed.Configuration;

/// <summary>
/// Persisted admin-visible suggestion discovered through a direct peer.
/// Suggestions are metadata only until an admin manually adds them as direct peers.
/// </summary>
public class DiscoveredPeerConfiguration
{
    /// <summary>Gets or sets the suggested peer display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the suggested peer base URL.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Gets or sets the token an admin can use when manually adding the peer.</summary>
    public string FederationToken { get; set; } = string.Empty;

    /// <summary>Gets or sets the direct peer through which this suggestion was discovered.</summary>
    public string SourcePeerName { get; set; } = string.Empty;

    /// <summary>Gets or sets the discovered peer's advertised JellyFed version.</summary>
    public string? Version { get; set; }

    /// <summary>Gets or sets the conceptual hop count from this instance.</summary>
    public int HopCount { get; set; } = 2;

    /// <summary>Gets or sets the ISO 8601 timestamp when this suggestion was last refreshed.</summary>
    public string? LastDiscoveredAt { get; set; }
}
