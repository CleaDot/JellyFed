using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyFed.Sync;

/// <summary>
/// Scheduled task that synchronizes catalogs from federated peers.
/// </summary>
public class FederationSyncTask : IScheduledTask
{
    private readonly ILogger<FederationSyncTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FederationSyncTask"/> class.
    /// </summary>
    /// <param name="logger">Instance of the <see cref="ILogger{FederationSyncTask}"/> interface.</param>
    public FederationSyncTask(ILogger<FederationSyncTask> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "JellyFed — Sync federated catalogs";

    /// <inheritdoc />
    public string Key => "JellyFedSync";

    /// <inheritdoc />
    public string Description => "Fetches catalogs from all configured peers and generates .strm files.";

    /// <inheritdoc />
    public string Category => "JellyFed";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfo.TriggerInterval,
            IntervalTicks = TimeSpan.FromHours(
                Plugin.Instance?.Configuration.SyncIntervalHours ?? 6).Ticks
        };
    }

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("JellyFed sync started — not yet implemented (Phase 2)");
        progress.Report(100);
        return Task.CompletedTask;
    }
}
