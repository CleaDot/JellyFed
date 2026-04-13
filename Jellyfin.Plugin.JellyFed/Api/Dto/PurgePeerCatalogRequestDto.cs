namespace Jellyfin.Plugin.JellyFed.Api.Dto;

/// <summary>
/// Request body for purging all synced items from a specific peer.
/// </summary>
public class PurgePeerCatalogRequestDto
{
    /// <summary>Gets or sets the name of the peer whose catalog should be purged.</summary>
    public string PeerName { get; set; } = string.Empty;
}
