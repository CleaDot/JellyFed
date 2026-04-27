using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyFed.Audit;

/// <summary>
/// Summary counters and peer facets for the logs UI.
/// </summary>
public sealed class AuditLogOverview
{
    /// <summary>
    /// Gets or sets the total number of stored audit records.
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Gets or sets the number of security records.
    /// </summary>
    public int SecurityCount { get; set; }

    /// <summary>
    /// Gets or sets the number of peer-connection records.
    /// </summary>
    public int PeerConnectionCount { get; set; }

    /// <summary>
    /// Gets or sets the number of peer-access records.
    /// </summary>
    public int PeerAccessCount { get; set; }

    /// <summary>
    /// Gets or sets the number of records written in the last 24 hours.
    /// </summary>
    public int Last24HoursCount { get; set; }

    /// <summary>
    /// Gets or sets the last event timestamp.
    /// </summary>
    public string? LastEventAt { get; set; }

    /// <summary>
    /// Gets or sets the peer facets offered to the UI filter.
    /// </summary>
    public IReadOnlyList<AuditLogPeerFacet> Peers { get; set; } = [];
}
