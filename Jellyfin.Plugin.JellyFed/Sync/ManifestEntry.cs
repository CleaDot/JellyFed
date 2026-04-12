namespace Jellyfin.Plugin.JellyFed.Sync;

/// <summary>
/// Tracks a single item synced from a peer onto local disk.
/// </summary>
public class ManifestEntry
{
    /// <summary>Gets or sets the local folder path of the item.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Gets or sets the peer name this item came from.</summary>
    public string PeerName { get; set; } = string.Empty;

    /// <summary>Gets or sets the Jellyfin item ID on the remote peer.</summary>
    public string JellyfinId { get; set; } = string.Empty;

    /// <summary>Gets or sets the ISO 8601 date of the last sync.</summary>
    public string SyncedAt { get; set; } = string.Empty;
}
