using System;

namespace Jellyfin.Plugin.JellyFed.Configuration;

/// <summary>
/// Helpers for stable peer identity management.
/// </summary>
public static class PeerIdentity
{
    /// <summary>
    /// Ensures the peer has a stable identifier.
    /// </summary>
    /// <param name="peer">Peer configuration.</param>
    /// <returns>True when the peer id had to be created.</returns>
    public static bool EnsurePeerId(PeerConfiguration peer)
    {
        ArgumentNullException.ThrowIfNull(peer);

        if (!string.IsNullOrWhiteSpace(peer.PeerId))
        {
            return false;
        }

        peer.PeerId = Guid.NewGuid().ToString("N");
        return true;
    }
}
