using System;

namespace Jellyfin.Plugin.JellyFed.Sync;

/// <summary>
/// Runtime status of a federated peer, persisted between restarts.
/// </summary>
public class PeerStatus
{
    /// <summary>Gets or sets a value indicating whether the peer was reachable at last check.</summary>
    public bool Online { get; set; }

    /// <summary>Gets or sets the last time the peer was seen online (ISO 8601).</summary>
    public string? LastSeen { get; set; }

    /// <summary>Gets or sets the JellyFed version reported by the peer.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Gets or sets the number of movies in the peer's catalog.</summary>
    public int MovieCount { get; set; }

    /// <summary>Gets or sets the number of series in the peer's catalog.</summary>
    public int SeriesCount { get; set; }

    /// <summary>
    /// Updates this status from a successful health + catalog response.
    /// </summary>
    /// <param name="version">JellyFed version string.</param>
    /// <param name="movieCount">Number of movies.</param>
    /// <param name="seriesCount">Number of series.</param>
    public void MarkOnline(string version, int movieCount, int seriesCount)
    {
        Online = true;
        LastSeen = DateTime.UtcNow.ToString("O");
        Version = version;
        MovieCount = movieCount;
        SeriesCount = seriesCount;
    }

    /// <summary>
    /// Marks the peer as offline without changing catalog size.
    /// </summary>
    public void MarkOffline()
    {
        Online = false;
    }
}
