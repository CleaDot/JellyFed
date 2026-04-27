namespace Jellyfin.Plugin.JellyFed.Api.Dto;

/// <summary>
/// Discovery announcement shared between direct peers.
/// </summary>
public class DiscoveryPeerDto
{
    /// <summary>Gets or sets the peer display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the peer base URL.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Gets or sets the token to use when manually adding the peer.</summary>
    public string FederationToken { get; set; } = string.Empty;

    /// <summary>Gets or sets the advertised JellyFed version.</summary>
    public string? Version { get; set; }

    /// <summary>Gets or sets a value indicating whether this instance permits second-hop discovery.</summary>
    public bool Discoverable { get; set; }
}
