using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.JellyFed.Api.Dto;

/// <summary>
/// Manifest statistics grouped by peer.
/// </summary>
public class ManifestStatsDto
{
    /// <summary>Gets or sets the per-peer catalog stats.</summary>
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "DTO — needs public setter for serialization.")]
    [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "DTO — simple list is fine here.")]
    public List<PeerCatalogStatsDto> Peers { get; set; } = [];
}
