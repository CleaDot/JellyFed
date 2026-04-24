using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyFed.Api.Dto;

/// <summary>
/// Discovery payload shared with direct peers.
/// Only direct peers are shared; discovered suggestions are never relayed recursively.
/// </summary>
public class DiscoveryResponseDto
{
    /// <summary>Gets or sets this instance's own announcement.</summary>
    public DiscoveryPeerDto Self { get; set; } = new();

    /// <summary>Gets the discoverable direct peers this instance is willing to suggest.</summary>
    public IReadOnlyList<DiscoveryPeerDto> DirectPeers { get; init; } = [];
}
