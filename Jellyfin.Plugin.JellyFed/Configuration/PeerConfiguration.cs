namespace Jellyfin.Plugin.JellyFed.Configuration;

/// <summary>
/// Configuration for a federated peer instance.
/// </summary>
public class PeerConfiguration
{
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
}
