using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyFed.Sync;

/// <summary>
/// Scheduled task that synchronizes catalogs from all configured federated peers.
/// </summary>
public class FederationSyncTask : IScheduledTask
{
    private const string ManifestFileName = ".jellyfed-manifest.json";

    private readonly ILibraryManager _libraryManager;
    private readonly PeerClient _peerClient;
    private readonly StrmWriter _strmWriter;
    private readonly ILogger<FederationSyncTask> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="FederationSyncTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of <see cref="ILibraryManager"/>.</param>
    /// <param name="peerClient">Instance of <see cref="PeerClient"/>.</param>
    /// <param name="strmWriter">Instance of <see cref="StrmWriter"/>.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{FederationSyncTask}"/> interface.</param>
    public FederationSyncTask(
        ILibraryManager libraryManager,
        PeerClient peerClient,
        StrmWriter strmWriter,
        ILogger<FederationSyncTask> logger)
    {
        _libraryManager = libraryManager;
        _peerClient = peerClient;
        _strmWriter = strmWriter;
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

        Directory.CreateDirectory(Path.Combine(libraryPath, "Films"));
        Directory.CreateDirectory(Path.Combine(libraryPath, "Series"));

        var manifest = LoadManifest(libraryPath);
        var seenMovieKeys = new HashSet<string>(StringComparer.Ordinal);
        var seenSeriesKeys = new HashSet<string>(StringComparer.Ordinal);

        var enabledPeers = config.Peers;
        int totalPeers = enabledPeers.Count;
        int peerIndex = 0;

        foreach (var peer in enabledPeers)
        {
            if (!peer.Enabled)
            {
                peerIndex++;
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogInformation("JellyFed sync: starting peer {PeerName}", peer.Name);

            var catalog = await _peerClient.GetCatalogAsync(peer, null, cancellationToken)
                .ConfigureAwait(false);

            if (catalog is null)
            {
                _logger.LogWarning("JellyFed sync: could not reach peer {PeerName}, skipping.", peer.Name);
                peerIndex++;
                progress.Report((double)peerIndex / totalPeers * 100);
                continue;
            }

            int addedMovies = 0, addedSeries = 0, skippedMovies = 0, skippedSeries = 0;

            foreach (var item in catalog.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var key = ManifestKey(item.TmdbId, peer.Name, item.JellyfinId);

                if (item.Type == "Movie" && peer.SyncMovies)
                {
                    seenMovieKeys.Add(key);
                    if (manifest.Movies.ContainsKey(key))
                    {
                        skippedMovies++;
                        continue;
                    }

                    var folderPath = await _strmWriter.WriteMovieAsync(libraryPath, item, peer, cancellationToken)
                        .ConfigureAwait(false);

                    manifest.Movies[key] = new ManifestEntry
                    {
                        Path = folderPath,
                        PeerName = peer.Name,
                        JellyfinId = item.JellyfinId,
                        SyncedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                    };
                    addedMovies++;
                }
                else if (item.Type == "Series" && peer.SyncSeries)
                {
                    seenSeriesKeys.Add(key);
                    if (manifest.Series.ContainsKey(key))
                    {
                        skippedSeries++;
                        continue;
                    }

                    var seasons = await _peerClient.GetSeasonsAsync(peer, item.JellyfinId, cancellationToken)
                        .ConfigureAwait(false);

                    if (seasons is null)
                    {
                        _logger.LogWarning("JellyFed sync: failed to fetch seasons for {Title}, skipping.", item.Title);
                        continue;
                    }

                    var folderPath = await _strmWriter.WriteSeriesAsync(libraryPath, item, seasons, peer, cancellationToken)
                        .ConfigureAwait(false);

                    manifest.Series[key] = new ManifestEntry
                    {
                        Path = folderPath,
                        PeerName = peer.Name,
                        JellyfinId = item.JellyfinId,
                        SyncedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                    };
                    addedSeries++;
                }
            }

            _logger.LogInformation("JellyFed sync: peer {PeerName} — +{AddedMovies} movies, +{AddedSeries} series, skipped {SkipM}/{SkipS}", peer.Name, addedMovies, addedSeries, skippedMovies, skippedSeries);

            peerIndex++;
            progress.Report((double)peerIndex / totalPeers * 90);
        }

        // Remove items that disappeared from their peer's catalog.
        PruneDeleted(manifest.Movies, seenMovieKeys);
        PruneDeleted(manifest.Series, seenSeriesKeys);

        SaveManifest(libraryPath, manifest);

        progress.Report(95);
        _logger.LogInformation("JellyFed sync: triggering Jellyfin library scan.");
        _libraryManager.QueueLibraryScan();

        progress.Report(100);
        _logger.LogInformation("JellyFed sync: complete.");
    }

    private void PruneDeleted(Dictionary<string, ManifestEntry> entries, HashSet<string> seenKeys)
    {
        var toRemove = new List<string>();
        foreach (var (key, entry) in entries)
        {
            if (!seenKeys.Contains(key))
            {
                _strmWriter.DeleteItem(entry.Path);
                toRemove.Add(key);
            }
        }

        foreach (var key in toRemove)
        {
            entries.Remove(key);
        }
    }

    private static string ManifestKey(string? tmdbId, string peerName, string jellyfinId)
        => string.IsNullOrEmpty(tmdbId)
            ? $"no-tmdb:{peerName}:{jellyfinId}"
            : $"tmdb:{tmdbId}";

    private static Manifest LoadManifest(string libraryPath)
    {
        var path = Path.Combine(libraryPath, ManifestFileName);
        if (!File.Exists(path))
        {
            return new Manifest();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Manifest>(json, JsonOptions) ?? new Manifest();
        }
        catch
        {
            return new Manifest();
        }
    }

    private static void SaveManifest(string libraryPath, Manifest manifest)
    {
        var path = Path.Combine(libraryPath, ManifestFileName);
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        File.WriteAllText(path, json);
    }
}
