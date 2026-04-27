using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyFed.Sync;

/// <summary>
/// Tracks all known upstream sources for one logical episode of a federated series.
/// </summary>
public class SeriesEpisodeSourceGroup
{
    /// <summary>Gets or sets the season number.</summary>
    public int SeasonNumber { get; set; }

    /// <summary>Gets or sets the episode number.</summary>
    public int EpisodeNumber { get; set; }

    /// <summary>Gets or sets the episode title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets all currently known upstream sources for this episode.</summary>
    public IReadOnlyList<ManifestSource> Sources { get; set; } = [];
}
