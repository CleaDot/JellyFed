namespace Jellyfin.Plugin.JellyFed.Audit;

/// <summary>
/// Attribution data for a JellyFed request authenticated by federation credentials.
/// </summary>
public sealed class FederationRequestIdentity
{
    /// <summary>
    /// Gets or sets the authentication mode.
    /// </summary>
    public string AuthMode { get; set; } = "unknown";

    /// <summary>
    /// Gets or sets the stable peer identifier when known.
    /// </summary>
    public string? PeerId { get; set; }

    /// <summary>
    /// Gets or sets the peer display name when known.
    /// </summary>
    public string? PeerName { get; set; }

    /// <summary>
    /// Gets or sets the peer URL when known.
    /// </summary>
    public string? PeerUrl { get; set; }

    /// <summary>
    /// Gets or sets the presented token.
    /// </summary>
    public string? PresentedToken { get; set; }

    /// <summary>
    /// Gets a value indicating whether the request is attributable to a specific peer.
    /// </summary>
    public bool IsPeerAttributed => !string.IsNullOrWhiteSpace(PeerId);
}
