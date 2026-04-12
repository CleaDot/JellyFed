using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyFed.Sync;

/// <summary>
/// Persisted manifest of all items written by JellyFed.
/// Keyed by TMDB ID ("tmdb:12345") or "no-tmdb:{peer}:{id}" when unavailable.
/// </summary>
public class Manifest
{
    /// <summary>Gets the synced movies: dedup key → ManifestEntry.</summary>
    public Dictionary<string, ManifestEntry> Movies { get; init; } = [];

    /// <summary>Gets the synced series: dedup key → ManifestEntry.</summary>
    public Dictionary<string, ManifestEntry> Series { get; init; } = [];
}
