namespace Jellyfin.Plugin.JellyFed.Api.Dto;

/// <summary>
/// Status and metadata for a single federated peer.
/// </summary>
public class PeerDto
{
    /// <summary>Gets or sets the peer display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the peer base URL.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the peer is enabled in config.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets a value indicating whether the peer was reachable at last heartbeat.</summary>
    public bool Online { get; set; }

    /// <summary>Gets or sets the last time the peer was seen online (ISO 8601), or null.</summary>
    public string? LastSeen { get; set; }

    /// <summary>Gets or sets the JellyFed version running on the peer.</summary>
    public string? Version { get; set; }

    /// <summary>Gets or sets the number of movies in the peer's catalog.</summary>
    public int MovieCount { get; set; }

    /// <summary>Gets or sets the number of series in the peer's catalog.</summary>
    public int SeriesCount { get; set; }
}
