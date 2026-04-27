namespace Jellyfin.Plugin.JellyFed.Api.Dto;

/// <summary>
/// Request body used to promote one upstream source as the active source for a federated item.
/// </summary>
public class SelectPrimarySourceRequestDto
{
    /// <summary>
    /// Gets or sets the logical item type: Movie or Series.
    /// </summary>
    public string ItemType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the logical manifest key.
    /// </summary>
    public string ItemKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the peer name to promote.
    /// </summary>
    public string PeerName { get; set; } = string.Empty;
}
