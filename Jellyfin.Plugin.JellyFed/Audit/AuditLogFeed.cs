using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyFed.Audit;

/// <summary>
/// Paged audit log results.
/// </summary>
public sealed class AuditLogFeed
{
    /// <summary>
    /// Gets or sets the returned items.
    /// </summary>
    public IReadOnlyList<AuditLogEntry> Items { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether more results are available.
    /// </summary>
    public bool HasMore { get; set; }

    /// <summary>
    /// Gets or sets the next cursor for pagination.
    /// </summary>
    public long? NextBeforeId { get; set; }
}
