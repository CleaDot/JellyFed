using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.JellyFed.Api.Dto;
using Jellyfin.Plugin.JellyFed.Audit;
using Jellyfin.Plugin.JellyFed.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyFed.Sync;

/// <summary>
/// Scheduled task that synchronizes catalogs from all configured federated peers.
/// Exposes <see cref="SyncPeerAsync"/> so admin endpoints can re-use the same per-peer
/// pipeline without going through the scheduler queue.
/// </summary>
public class FederationSyncTask : IScheduledTask
{
    /// <summary>
    /// File name of the persisted manifest in the library path.
    /// </summary>
    public const string ManifestFileName = ".jellyfed-manifest.json";

    private readonly ILibraryManager _libraryManager;
    private readonly PeerClient _peerClient;
    private readonly StrmWriter _strmWriter;
    private readonly AuditLogService _auditLogService;
    private readonly ILogger<FederationSyncTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FederationSyncTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="peerClient">HTTP client for remote JellyFed peers.</param>
    /// <param name="strmWriter">Materializer for local .strm/NFO/source files.</param>
    /// <param name="auditLogService">Audit service.</param>
    /// <param name="logger">Logger instance.</param>
    public FederationSyncTask(
        ILibraryManager libraryManager,
        PeerClient peerClient,
        StrmWriter strmWriter,
        AuditLogService auditLogService,
        ILogger<FederationSyncTask> logger)
    {
        _libraryManager = libraryManager;
        _peerClient = peerClient;
        _strmWriter = strmWriter;
        _auditLogService = auditLogService;
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
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromHours(
                Plugin.Instance?.Configuration.SyncIntervalHours ?? 6).Ticks
        };
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null)
        {
            _logger.LogWarning("JellyFed sync: plugin configuration unavailable.");
            return;
        }

        var libraryPath = config.LibraryPath;
        if (string.IsNullOrWhiteSpace(libraryPath))
        {
            _logger.LogWarning("JellyFed sync: LibraryPath is not configured.");
            return;
        }

        Directory.CreateDirectory(libraryPath);

        var manifest = ManifestStore.Load(libraryPath);
        var localTmdbIds = BuildLocalTmdbIds(config);
        var states = PeerStateStore.Load(libraryPath);

        var seenMovieSources = new HashSet<string>(StringComparer.Ordinal);
        var seenSeriesSources = new HashSet<string>(StringComparer.Ordinal);
        var peersEligibleForPrune = new HashSet<string>(
            config.Peers.Where(static peer => !peer.Enabled).Select(static peer => peer.Name),
            StringComparer.OrdinalIgnoreCase);

        int totalPeers = config.Peers.Count;
        int peerIndex = 0;

        foreach (var peer in config.Peers)
        {
            if (!peer.Enabled)
            {
                peerIndex++;
                progress.Report((double)peerIndex / Math.Max(1, totalPeers) * 90);
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var peerResult = await SyncSinglePeerAsync(
                peer,
                config,
                manifest,
                localTmdbIds,
                seenMovieSources,
                seenSeriesSources,
                cancellationToken).ConfigureAwait(false);

            if (peerResult.CanPrune)
            {
                peersEligibleForPrune.Add(peer.Name);
            }

            if (!states.TryGetValue(peer.Name, out var status))
            {
                status = new PeerStatus();
                states[peer.Name] = status;
            }

            if (peerResult.Error is null)
            {
                status.MarkSynced(peerResult.DurationMs);
            }
            else
            {
                status.MarkSyncFailed(peerResult.Error, peerResult.DurationMs);
            }

            peerIndex++;
            progress.Report((double)peerIndex / Math.Max(1, totalPeers) * 90);
        }

        await PruneDeletedAsync(manifest.Movies, seenMovieSources, peersEligibleForPrune, config, "Movie", cancellationToken)
            .ConfigureAwait(false);
        await PruneDeletedAsync(manifest.Series, seenSeriesSources, peersEligibleForPrune, config, "Series", cancellationToken)
            .ConfigureAwait(false);

        ManifestStore.Save(libraryPath, manifest);
        PeerStateStore.Save(libraryPath, states);

        progress.Report(95);
        _logger.LogInformation("JellyFed sync: triggering Jellyfin library scan.");
        _libraryManager.QueueLibraryScan();

        progress.Report(100);
        _logger.LogInformation("JellyFed sync: complete.");
    }

    /// <summary>
    /// Runs the full sync pipeline for a single peer and returns a summary result.
    /// </summary>
    /// <param name="peer">Peer to synchronize.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the sync attempt.</returns>
    public async Task<PeerSyncResult> SyncPeerAsync(
        PeerConfiguration peer,
        CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || string.IsNullOrWhiteSpace(config.LibraryPath))
        {
            return new PeerSyncResult { Error = "Plugin configuration unavailable." };
        }

        Directory.CreateDirectory(config.LibraryPath);
        var manifest = ManifestStore.Load(config.LibraryPath);
        var localTmdbIds = BuildLocalTmdbIds(config);
        var states = PeerStateStore.Load(config.LibraryPath);

        var seenMovieSources = new HashSet<string>(StringComparer.Ordinal);
        var seenSeriesSources = new HashSet<string>(StringComparer.Ordinal);

        var result = await SyncSinglePeerAsync(
            peer,
            config,
            manifest,
            localTmdbIds,
            seenMovieSources,
            seenSeriesSources,
            cancellationToken).ConfigureAwait(false);

        if (result.CanPrune)
        {
            var prunePeers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { peer.Name };
            result.Pruned += await PruneDeletedForPeerAsync(manifest.Movies, seenMovieSources, prunePeers, config, "Movie", cancellationToken)
                .ConfigureAwait(false);
            result.Pruned += await PruneDeletedForPeerAsync(manifest.Series, seenSeriesSources, prunePeers, config, "Series", cancellationToken)
                .ConfigureAwait(false);
        }

        if (!states.TryGetValue(peer.Name, out var status))
        {
            status = new PeerStatus();
            states[peer.Name] = status;
        }

        if (result.Error is null)
        {
            status.MarkSynced(result.DurationMs);
        }
        else
        {
            status.MarkSyncFailed(result.Error, result.DurationMs);
        }

        ManifestStore.Save(config.LibraryPath, manifest);
        PeerStateStore.Save(config.LibraryPath, states);

        _libraryManager.QueueLibraryScan();

        return result;
    }

    private async Task<PeerSyncResult> SyncSinglePeerAsync(
        PeerConfiguration peer,
        PluginConfiguration config,
        Manifest manifest,
        HashSet<string> localTmdbIds,
        HashSet<string> seenMovieSources,
        HashSet<string> seenSeriesSources,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var result = new PeerSyncResult();

        try
        {
            PeerIdentity.EnsurePeerId(peer);
            _logger.LogInformation("JellyFed sync: starting peer {PeerName}", peer.Name);
            _auditLogService.WritePeerEvent(peer, "peer.sync.started", $"Started sync for {peer.Name}.");

            var catalog = await _peerClient.GetCatalogAsync(peer, null, cancellationToken)
                .ConfigureAwait(false);

            if (catalog is null)
            {
                result.Error = "Peer unreachable.";
                _logger.LogWarning("JellyFed sync: could not reach peer {PeerName}, skipping.", peer.Name);
                _auditLogService.WritePeerEvent(
                    peer,
                    "peer.sync.unreachable",
                    $"Skipped sync for {peer.Name} because the peer was unreachable.",
                    AuditLogSeverities.Warning);
                return result;
            }

            result.CanPrune = true;
            var peerSeg = StrmWriter.SanitizePeerFolderSegment(peer.Name);

            foreach (var item in catalog.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var key = ManifestKey(item.TmdbId, peer.Name, item.JellyfinId);
                var source = BuildSource(item, peer.Name);
                var isAnime = CatalogItemClassifier.IsAnime(item);

                if (!string.IsNullOrEmpty(item.TmdbId) && localTmdbIds.Contains(item.TmdbId))
                {
                    if (item.Type == "Movie")
                    {
                        result.SkippedMovies++;
                    }
                    else
                    {
                        result.SkippedSeries++;
                    }

                    continue;
                }

                if (isAnime && !peer.SyncAnime)
                {
                    continue;
                }

                if (item.Type == "Movie" && peer.SyncMovies)
                {
                    var movieTypeRoot = isAnime
                        ? config.GetEffectiveAnimeRoot()
                        : config.GetEffectiveMoviesRoot();
                    if (string.IsNullOrWhiteSpace(movieTypeRoot))
                    {
                        _logger.LogWarning("JellyFed sync: movies root not configured, skipping movie.");
                        continue;
                    }

                    seenMovieSources.Add(SourceKey(key, peer.Name));
                    var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

                    if (!manifest.Movies.TryGetValue(key, out var entry))
                    {
                        entry = new ManifestEntry
                        {
                            PeerName = peer.Name,
                            JellyfinId = item.JellyfinId,
                            SyncedAt = now,
                            Sources = [source]
                        };

                        var movieContentRoot = Path.Combine(movieTypeRoot, peerSeg);
                        Directory.CreateDirectory(movieContentRoot);
                        entry.Path = await _strmWriter.WriteMovieAsync(movieContentRoot, item, peer, entry, key, cancellationToken)
                            .ConfigureAwait(false);

                        manifest.Movies[key] = entry;
                        result.AddedMovies++;
                        continue;
                    }

                    UpsertSource(entry, source);
                    EnsurePrimarySource(entry, keepCurrentIfAvailable: true);
                    entry.SyncedAt = now;

                    if (string.Equals(entry.PeerName, peer.Name, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(entry.JellyfinId, item.JellyfinId, StringComparison.Ordinal))
                    {
                        await _strmWriter.RewriteMoviePrimaryAsync(entry.Path, item, peer, entry, key, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await _strmWriter.RefreshMovieProvenanceAsync(entry.Path, entry, key, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    result.SkippedMovies++;
                }
                else if (item.Type == "Series" && peer.SyncSeries)
                {
                    var seriesTypeRoot = isAnime
                        ? config.GetEffectiveAnimeRoot()
                        : config.GetEffectiveSeriesRoot();
                    if (string.IsNullOrWhiteSpace(seriesTypeRoot))
                    {
                        _logger.LogWarning("JellyFed sync: series root not configured, skipping series.");
                        continue;
                    }

                    seenSeriesSources.Add(SourceKey(key, peer.Name));
                    var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

                    if (!manifest.Series.TryGetValue(key, out var entry))
                    {
                        var seasons = await _peerClient.GetSeasonsAsync(peer, item.JellyfinId, cancellationToken)
                            .ConfigureAwait(false);
                        if (seasons is null)
                        {
                            _logger.LogWarning("JellyFed sync: failed to fetch seasons for {Title}, skipping.", item.Title);
                            continue;
                        }

                        entry = new ManifestEntry
                        {
                            PeerName = peer.Name,
                            JellyfinId = item.JellyfinId,
                            SyncedAt = now,
                            Sources = [source]
                        };

                        var seriesContentRoot = Path.Combine(seriesTypeRoot, peerSeg);
                        Directory.CreateDirectory(seriesContentRoot);
                        entry.Path = await _strmWriter.WriteSeriesAsync(seriesContentRoot, item, seasons, peer, entry, key, cancellationToken)
                            .ConfigureAwait(false);

                        manifest.Series[key] = entry;
                        result.AddedSeries++;
                        continue;
                    }

                    UpsertSource(entry, source);
                    EnsurePrimarySource(entry, keepCurrentIfAvailable: true);
                    entry.SyncedAt = now;

                    if (string.Equals(entry.PeerName, peer.Name, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(entry.JellyfinId, item.JellyfinId, StringComparison.Ordinal))
                    {
                        var seasons = await _peerClient.GetSeasonsAsync(peer, item.JellyfinId, cancellationToken)
                            .ConfigureAwait(false);
                        if (seasons is not null)
                        {
                            await _strmWriter.RewriteSeriesPrimaryAsync(entry.Path, item, seasons, peer, entry, key, cancellationToken)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            _logger.LogWarning("JellyFed sync: failed to refresh seasons for {Title}, keeping previous materialization.", item.Title);
                            await _strmWriter.RefreshSeriesProvenanceAsync(entry.Path, entry, key, cancellationToken)
                                .ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        await _strmWriter.RefreshSeriesProvenanceAsync(entry.Path, entry, key, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    result.SkippedSeries++;
                }
            }

            _logger.LogInformation(
                "JellyFed sync: peer {PeerName} — +{AddedMovies} movies, +{AddedSeries} series, skipped {SkipM}/{SkipS}",
                peer.Name,
                result.AddedMovies,
                result.AddedSeries,
                result.SkippedMovies,
                result.SkippedSeries);
            _auditLogService.WritePeerEvent(
                peer,
                "peer.sync.completed",
                $"Completed sync for {peer.Name}.",
                details: new
                {
                    result.AddedMovies,
                    result.AddedSeries,
                    result.SkippedMovies,
                    result.SkippedSeries,
                    result.Pruned
                });

            // Discovery is suggestion-only in v1. Sync never auto-registers this instance back on the peer.
        }
        catch (OperationCanceledException)
        {
            result.Error = "Sync cancelled.";
            throw;
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            result.Error = ex.Message;
            _auditLogService.WritePeerEvent(
                peer,
                "peer.sync.failed",
                $"Sync failed for {peer.Name}: {ex.Message}",
                AuditLogSeverities.Error,
                new { error = ex.Message });
            _logger.LogError(ex, "JellyFed sync: peer {PeerName} failed.", peer.Name);
        }
#pragma warning restore CA1031

        sw.Stop();
        result.DurationMs = sw.ElapsedMilliseconds;
        return result;
    }

    private async Task<int> PruneDeletedAsync(
        Dictionary<string, ManifestEntry> entries,
        HashSet<string> seenSourceKeys,
        HashSet<string> peersEligibleForPrune,
        PluginConfiguration config,
        string itemType,
        CancellationToken cancellationToken)
    {
        return await PruneEntriesAsync(entries, seenSourceKeys, peersEligibleForPrune, config, itemType, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<int> PruneDeletedForPeerAsync(
        Dictionary<string, ManifestEntry> entries,
        HashSet<string> seenSourceKeys,
        HashSet<string> peersEligibleForPrune,
        PluginConfiguration config,
        string itemType,
        CancellationToken cancellationToken)
    {
        return await PruneEntriesAsync(entries, seenSourceKeys, peersEligibleForPrune, config, itemType, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<int> PruneEntriesAsync(
        Dictionary<string, ManifestEntry> entries,
        HashSet<string> seenSourceKeys,
        HashSet<string> peersEligibleForPrune,
        PluginConfiguration config,
        string itemType,
        CancellationToken cancellationToken)
    {
        var removedEntries = 0;

        foreach (var key in entries.Keys.ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = entries[key];
            bool removedAnySource = false;
            bool removedPrimary = false;

            var sources = entry.Sources.ToList();
            foreach (var source in sources.ToList())
            {
                if (!peersEligibleForPrune.Contains(source.PeerName))
                {
                    continue;
                }

                if (seenSourceKeys.Contains(SourceKey(key, source.PeerName)))
                {
                    continue;
                }

                sources.Remove(source);
                removedAnySource = true;
                removedPrimary |= string.Equals(source.PeerName, entry.PeerName, StringComparison.OrdinalIgnoreCase) &&
                                  string.Equals(source.JellyfinId, entry.JellyfinId, StringComparison.Ordinal);
            }

            if (!removedAnySource)
            {
                continue;
            }

            entry.Sources = sources;

            if (entry.Sources.Count == 0)
            {
                _strmWriter.DeleteItem(entry.Path);
                entries.Remove(key);
                removedEntries++;
                continue;
            }

            if (removedPrimary || string.IsNullOrWhiteSpace(entry.PeerName))
            {
                EnsurePrimarySource(entry, keepCurrentIfAvailable: false);
                TryMoveEntryToPrimaryPeerFolder(entry);
                await RehydrateAfterPrimarySwitchAsync(entry, key, itemType, config, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await RefreshProvenanceAsync(entry, key, itemType, cancellationToken).ConfigureAwait(false);
            }

            entry.SyncedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        }

        return removedEntries;
    }

    private async Task RehydrateAfterPrimarySwitchAsync(
        ManifestEntry entry,
        string itemKey,
        string itemType,
        PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        if (string.Equals(itemType, "Movie", StringComparison.Ordinal))
        {
            var primary = entry.PrimarySource;
            if (!string.IsNullOrWhiteSpace(primary?.StreamUrl))
            {
                await _strmWriter.RewriteMovieStreamAsync(entry.Path, entry, primary.StreamUrl, itemKey, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            await _strmWriter.RefreshMovieProvenanceAsync(entry.Path, entry, itemKey, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var primaryPeer = config.Peers.FirstOrDefault(peer =>
            string.Equals(peer.Name, entry.PeerName, StringComparison.OrdinalIgnoreCase));

        if (primaryPeer is not null)
        {
            var seasons = await _peerClient.GetSeasonsAsync(primaryPeer, entry.JellyfinId, cancellationToken)
                .ConfigureAwait(false);
            if (seasons is not null)
            {
                await _strmWriter.RewriteSeriesEpisodeStreamsAsync(entry.Path, seasons, entry, itemKey, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
        }

        await _strmWriter.RefreshSeriesProvenanceAsync(entry.Path, entry, itemKey, cancellationToken)
            .ConfigureAwait(false);
    }

    private Task RefreshProvenanceAsync(
        ManifestEntry entry,
        string itemKey,
        string itemType,
        CancellationToken cancellationToken)
        => string.Equals(itemType, "Movie", StringComparison.Ordinal)
            ? _strmWriter.RefreshMovieProvenanceAsync(entry.Path, entry, itemKey, cancellationToken)
            : _strmWriter.RefreshSeriesProvenanceAsync(entry.Path, entry, itemKey, cancellationToken);

    private HashSet<string> BuildLocalTmdbIds(PluginConfiguration pluginConfig)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
            IsVirtualItem = false,
            Recursive = true
        });

        foreach (var item in items)
        {
            if (!string.IsNullOrEmpty(item.Path) &&
                FederatedPathHelper.IsUnderFederatedContent(item.Path, pluginConfig))
            {
                continue;
            }

            var tmdbId = item.GetProviderId("Tmdb");
            if (!string.IsNullOrEmpty(tmdbId))
            {
                result.Add(tmdbId);
            }
        }

        return result;
    }

    private static ManifestSource BuildSource(CatalogItemDto item, string peerName)
        => new()
        {
            PeerName = peerName,
            JellyfinId = item.JellyfinId,
            StreamUrl = item.StreamUrl,
            Container = item.Container,
            VideoCodec = item.VideoCodec,
            AudioCodec = item.AudioCodec,
            Width = item.Width,
            Height = item.Height,
            AddedAt = item.AddedAt,
            UpdatedAt = item.UpdatedAt,
            MediaStreams = item.MediaStreams
        };

    private static void UpsertSource(ManifestEntry entry, ManifestSource source)
    {
        var sources = entry.Sources.ToList();

        var existing = sources.FirstOrDefault(s =>
            string.Equals(s.PeerName, source.PeerName, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            sources.Add(source);
            entry.Sources = sources;
            return;
        }

        existing.JellyfinId = source.JellyfinId;
        existing.StreamUrl = source.StreamUrl;
        existing.Container = source.Container;
        existing.VideoCodec = source.VideoCodec;
        existing.AudioCodec = source.AudioCodec;
        existing.Width = source.Width;
        existing.Height = source.Height;
        existing.AddedAt = source.AddedAt;
        existing.UpdatedAt = source.UpdatedAt;
        existing.MediaStreams = source.MediaStreams;
        entry.Sources = sources;
    }

    private static void EnsurePrimarySource(ManifestEntry entry, bool keepCurrentIfAvailable)
    {
        if (entry.Sources.Count == 0)
        {
            return;
        }

        if (keepCurrentIfAvailable)
        {
            var current = entry.Sources.FirstOrDefault(source =>
                string.Equals(source.PeerName, entry.PeerName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(source.JellyfinId, entry.JellyfinId, StringComparison.Ordinal));
            if (current is not null)
            {
                entry.PeerName = current.PeerName;
                entry.JellyfinId = current.JellyfinId;
                return;
            }
        }

        var preferred = entry.Sources
            .OrderByDescending(source => SourcePixelCount(source))
            .ThenByDescending(SourceUpdatedAt)
            .ThenBy(source => source.PeerName, StringComparer.OrdinalIgnoreCase)
            .First();

        entry.PeerName = preferred.PeerName;
        entry.JellyfinId = preferred.JellyfinId;
    }

    private static void TryMoveEntryToPrimaryPeerFolder(ManifestEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Path) || !Directory.Exists(entry.Path))
        {
            return;
        }

        var itemDir = new DirectoryInfo(entry.Path);
        var currentPeerDir = itemDir.Parent;
        var rootDir = currentPeerDir?.Parent;
        if (currentPeerDir is null || rootDir is null)
        {
            return;
        }

        var targetPeerDir = Path.Combine(rootDir.FullName, StrmWriter.SanitizePeerFolderSegment(entry.PeerName));
        if (string.Equals(currentPeerDir.FullName, targetPeerDir, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Directory.CreateDirectory(targetPeerDir);
        var targetPath = Path.Combine(targetPeerDir, itemDir.Name);
        if (Directory.Exists(targetPath))
        {
            return;
        }

        Directory.Move(entry.Path, targetPath);
        entry.Path = targetPath;

        if (!currentPeerDir.EnumerateFileSystemInfos().Any())
        {
            currentPeerDir.Delete();
        }
    }

    private static int SourcePixelCount(ManifestSource source)
        => (source.Width ?? 0) * (source.Height ?? 0);

    private static DateTime SourceUpdatedAt(ManifestSource source)
        => DateTime.TryParse(source.UpdatedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var updatedAt)
            ? updatedAt
            : DateTime.MinValue;

    private static string ManifestKey(string? tmdbId, string peerName, string jellyfinId)
    {
        var p = peerName.Trim();
        return string.IsNullOrEmpty(tmdbId)
            ? $"no-tmdb:{p}:{jellyfinId}"
            : $"tmdb:{tmdbId}";
    }

    private static string SourceKey(string itemKey, string peerName)
        => $"{itemKey}|{peerName.Trim()}";
}
