namespace Jellyfin.Plugin.JellyFed.Api.Dto;

/// <summary>
/// Admin-visible discovery suggestion for a peer that is not configured directly yet.
/// </summary>
public class DiscoveredPeerDto
{
    /// <summary>Gets or sets the suggested peer display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the suggested peer base URL.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Gets or sets the token to use if the admin manually adds this peer.</summary>
    public string FederationToken { get; set; } = string.Empty;

    /// <summary>Gets or sets the direct peer through which this suggestion was discovered.</summary>
    public string SourcePeerName { get; set; } = string.Empty;

    /// <summary>Gets or sets the remote JellyFed version, when known.</summary>
    public string? Version { get; set; }

    /// <summary>Gets or sets the conceptual hop count from this instance.</summary>
    public int HopCount { get; set; }

    /// <summary>Gets or sets the ISO 8601 timestamp when this suggestion was last refreshed.</summary>
    public string? LastDiscoveredAt { get; set; }
}
