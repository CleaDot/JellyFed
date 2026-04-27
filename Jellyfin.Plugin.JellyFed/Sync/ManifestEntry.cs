using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.JellyFed.Sync;

/// <summary>
/// Tracks one logical JellyFed item synced onto local disk.
/// <para>
/// <see cref="PeerName"/> and <see cref="JellyfinId"/> represent the current primary source
/// that owns the on-disk <c>.strm</c> URLs, while <see cref="Sources"/> keeps the full upstream
/// provenance set for future source selection.
/// </para>
/// </summary>
public class ManifestEntry
{
    /// <summary>Gets or sets the local folder path of the item.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Gets or sets the peer name of the current primary source.</summary>
    public string PeerName { get; set; } = string.Empty;

    /// <summary>Gets or sets the Jellyfin item ID of the current primary source.</summary>
    public string JellyfinId { get; set; } = string.Empty;

    /// <summary>Gets or sets the ISO 8601 date of the last sync.</summary>
    public string SyncedAt { get; set; } = string.Empty;

    /// <summary>Gets or sets all currently known upstream sources for this logical item.</summary>
    public IReadOnlyList<ManifestSource> Sources { get; set; } = [];

    /// <summary>Gets or sets all currently known per-episode upstream sources for a series item.</summary>
    public IReadOnlyList<SeriesEpisodeSourceGroup> EpisodeSources { get; set; } = [];

    /// <summary>
    /// Gets the current primary source entry, or null if the manifest has not been normalized yet.
    /// </summary>
    public ManifestSource? PrimarySource
        => Sources.FirstOrDefault(s => string.Equals(s.PeerName, PeerName, System.StringComparison.OrdinalIgnoreCase));
}
