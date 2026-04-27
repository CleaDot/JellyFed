using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyFed.Api.Dto;

/// <summary>
/// Response envelope for GET /JellyFed/v1/peers.
/// </summary>
public class PeersResponseDto
{
    /// <summary>Gets the list of configured peers with their current status.</summary>
    public IReadOnlyList<PeerDto> Peers { get; init; } = [];

    /// <summary>Gets or sets a value indicating whether this instance can be suggested by direct peers.</summary>
    public bool SelfDiscoverable { get; set; }
}
