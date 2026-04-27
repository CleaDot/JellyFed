using System.Collections.Generic;
using Jellyfin.Plugin.JellyFed.Api.Dto;

namespace Jellyfin.Plugin.JellyFed.Sync;

/// <summary>
/// Tracks one concrete upstream source for a logical JellyFed item.
/// </summary>
public class ManifestSource
{
    /// <summary>Gets or sets the peer name that owns this source.</summary>
    public string PeerName { get; set; } = string.Empty;

    /// <summary>Gets or sets the Jellyfin item ID on the remote peer.</summary>
    public string JellyfinId { get; set; } = string.Empty;

    /// <summary>Gets or sets the direct stream URL when applicable (movies / episodes fallback groundwork).</summary>
    public string? StreamUrl { get; set; }

    /// <summary>Gets or sets the container format.</summary>
    public string? Container { get; set; }

    /// <summary>Gets or sets the video codec.</summary>
    public string? VideoCodec { get; set; }

    /// <summary>Gets or sets the audio codec.</summary>
    public string? AudioCodec { get; set; }

    /// <summary>Gets or sets the width in pixels.</summary>
    public int? Width { get; set; }

    /// <summary>Gets or sets the height in pixels.</summary>
    public int? Height { get; set; }

    /// <summary>Gets or sets when the source item was created on the peer.</summary>
    public string? AddedAt { get; set; }

    /// <summary>Gets or sets when the source item was updated on the peer.</summary>
    public string? UpdatedAt { get; set; }

    /// <summary>Gets or sets all audio and subtitle tracks known for this source.</summary>
    public IReadOnlyList<MediaStreamInfoDto> MediaStreams { get; set; } = [];
}
