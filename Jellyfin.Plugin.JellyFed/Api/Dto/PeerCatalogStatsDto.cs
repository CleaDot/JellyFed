namespace Jellyfin.Plugin.JellyFed.Api.Dto;

/// <summary>
/// Stats for a single peer's synced catalog entries.
/// </summary>
public class PeerCatalogStatsDto
{
    /// <summary>Gets or sets the peer name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the number of synced movies.</summary>
    public int MovieCount { get; set; }

    /// <summary>Gets or sets the number of synced series.</summary>
    public int SeriesCount { get; set; }
}
