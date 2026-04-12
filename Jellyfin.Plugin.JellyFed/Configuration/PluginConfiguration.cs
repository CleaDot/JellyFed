using System.Collections.ObjectModel;
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
    /// Gets the list of federated peers.
    /// </summary>
    public Collection<PeerConfiguration> Peers { get; init; }

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
