using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyFed.Sync;

/// <summary>
/// Sidecar persisted next to a federated item so future source-selection work can read
/// provenance without reopening the global manifest.
/// </summary>
public class SourcesFile
{
    /// <summary>Gets or sets the sidecar schema version.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Gets or sets the logical manifest key.</summary>
    public string ItemKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the Jellyfin item type: Movie or Series.</summary>
    public string ItemType { get; set; } = string.Empty;

    /// <summary>Gets or sets the current primary peer name.</summary>
    public string PrimaryPeerName { get; set; } = string.Empty;

    /// <summary>Gets or sets the current primary remote Jellyfin ID.</summary>
    public string PrimaryJellyfinId { get; set; } = string.Empty;

    /// <summary>Gets or sets the on-disk folder path for the logical item.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Gets or sets the last time JellyFed refreshed the logical item.</summary>
    public string SyncedAt { get; set; } = string.Empty;

    /// <summary>Gets or sets all currently known sources for the logical item.</summary>
    public IReadOnlyList<ManifestSource> Sources { get; set; } = [];

    /// <summary>Gets or sets all currently known per-episode sources for a series item.</summary>
    public IReadOnlyList<SeriesEpisodeSourceGroup> EpisodeSources { get; set; } = [];
}
