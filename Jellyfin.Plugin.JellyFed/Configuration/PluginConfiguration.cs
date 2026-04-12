using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyFed.Configuration;

/// <summary>
/// JellyFed plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        Peers = [];
        SyncIntervalHours = 6;
        LibraryPath = "/jellyfed-library";
        FederationToken = string.Empty;
    }

    /// <summary>
    /// Gets or sets the list of federated peers.
    /// </summary>
    /// <remarks>
    /// Must have a public setter so that Jellyfin's JSON/XML serializer can populate
    /// the collection on deserialization. CA2227/CA1002 suppressed intentionally.
    /// </remarks>
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Required for Jellyfin plugin config deserialization.")]
    [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "Required for Jellyfin plugin config deserialization.")]
    public List<PeerConfiguration> Peers { get; set; }

    /// <summary>
    /// Gets or sets the sync interval in hours.
    /// </summary>
    public int SyncIntervalHours { get; set; }

    /// <summary>
    /// Gets or sets the local path where .strm files and metadata are written.
    /// </summary>
    public string LibraryPath { get; set; }

    /// <summary>
    /// Gets or sets the federation token exposed by this instance to peers.
    /// </summary>
    public string FederationToken { get; set; }
}
