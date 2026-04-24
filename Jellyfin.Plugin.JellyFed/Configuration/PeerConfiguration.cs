namespace Jellyfin.Plugin.JellyFed.Configuration;

/// <summary>
/// Configuration for a federated peer instance.
/// </summary>
public class PeerConfiguration
{
    /// <summary>
    /// Gets or sets the stable JellyFed peer identifier.
    /// </summary>
    public string PeerId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name for this peer.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the base URL of the peer Jellyfin instance.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the federation API token for this peer.
    /// </summary>
    public string FederationToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the shareable federation token originally provided by this peer.
    /// This stays stable for discovery suggestions even if <see cref="FederationToken"/>
    /// is later replaced by a revocable per-peer access token.
    /// </summary>
    public string DiscoveryToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether sync is enabled for this peer.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether movies should be synced from this peer.
    /// </summary>
    public bool SyncMovies { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether series should be synced from this peer.
    /// </summary>
    public bool SyncSeries { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether anime-classified items (movies or series
    /// whose genres include an anime tag) should be synced from this peer.
    /// </summary>
    public bool SyncAnime { get; set; } = true;

    /// <summary>
    /// Gets or sets the access token issued by this instance to this peer.
    /// The peer must present this token (not the global FederationToken) when
    /// querying our catalog. Null until an explicit token exchange has happened.
    /// Removing the peer revokes this token immediately.
    /// </summary>
    public string? AccessToken { get; set; }
}
