using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Jellyfin.Plugin.JellyFed.Api.Dto;
using Jellyfin.Plugin.JellyFed.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyFed.Sync;

/// <summary>
/// Writes .strm, .nfo and artwork files for federated items.
/// </summary>
public class StrmWriter
{
    /// <summary>Well-known sidecar file that records all known upstream sources for an item.</summary>
    public const string SourcesFileName = "sources.json";

    private static readonly JsonSerializerOptions SourcesJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly PeerClient _peerClient;
    private readonly ILogger<StrmWriter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StrmWriter"/> class.
    /// </summary>
    /// <param name="peerClient">Instance of <see cref="PeerClient"/>.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{StrmWriter}"/> interface.</param>
    public StrmWriter(PeerClient peerClient, ILogger<StrmWriter> logger)
    {
        _peerClient = peerClient;
        _logger = logger;
    }

    /// <summary>
    /// Sanitizes a peer name for use as a single filesystem directory segment (per-peer folder).
    /// </summary>
    /// <param name="peerName">Peer display name.</param>
    /// <returns>Safe folder name.</returns>
    public static string SanitizePeerFolderSegment(string peerName)
    {
        if (string.IsNullOrWhiteSpace(peerName))
        {
            return "_peer";
        }

        return SanitizeName(peerName.Trim());
    }

    /// <summary>
    /// Writes a new movie item: folder, .strm, .nfo, poster, backdrop and sources sidecar.
    /// </summary>
    /// <param name="contentRoot">Peer-scoped root where the movie folder should be created.</param>
    /// <param name="item">Catalog snapshot of the movie.</param>
    /// <param name="peer">Peer that supplied the primary source.</param>
    /// <param name="entry">Logical manifest entry for the movie.</param>
    /// <param name="itemKey">Logical manifest key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The folder path written on disk.</returns>
    public async Task<string> WriteMovieAsync(
        string contentRoot,
        CatalogItemDto item,
        PeerConfiguration peer,
        ManifestEntry entry,
        string itemKey,
        CancellationToken cancellationToken)
    {
        var folderName = SanitizeName($"{item.Title} ({item.Year})");
        var folderPath = Path.Combine(contentRoot, folderName);
        Directory.CreateDirectory(folderPath);

        await WriteMovieFilesAsync(folderPath, item, peer, entry, itemKey, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation("Wrote movie: {Title} ({Year}) → {Path}", item.Title, item.Year, folderPath);
        return folderPath;
    }

    /// <summary>
    /// Rewrites the primary movie files (.strm, .nfo, artwork, sources sidecar).
    /// </summary>
    /// <param name="folderPath">Existing movie folder path.</param>
    /// <param name="item">Fresh catalog snapshot of the movie.</param>
    /// <param name="peer">Peer that owns the primary source.</param>
    /// <param name="entry">Logical manifest entry for the movie.</param>
    /// <param name="itemKey">Logical manifest key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task RewriteMoviePrimaryAsync(
        string folderPath,
        CatalogItemDto item,
        PeerConfiguration peer,
        ManifestEntry entry,
        string itemKey,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folderPath);
        await WriteMovieFilesAsync(folderPath, item, peer, entry, itemKey, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Refreshes only the provenance markers (peer/tag/studio) and sources sidecar of an existing movie.
    /// </summary>
    /// <param name="folderPath">Existing movie folder path.</param>
    /// <param name="entry">Logical manifest entry for the movie.</param>
    /// <param name="itemKey">Logical manifest key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task RefreshMovieProvenanceAsync(
        string folderPath,
        ManifestEntry entry,
        string itemKey,
        CancellationToken cancellationToken)
    {
        var folderName = Path.GetFileName(folderPath);
        var nfoPath = Path.Combine(folderPath, $"{folderName}.nfo");
        await RewriteNfoProvenanceAsync(nfoPath, entry, cancellationToken).ConfigureAwait(false);
        await WriteSourcesFileAsync(folderPath, entry, itemKey, "Movie", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Rewrites only the movie .strm URL, keeping the existing NFO metadata in place.
    /// Used when the primary source changes and JellyFed has a stream URL but not a full
    /// fresh metadata snapshot yet.
    /// </summary>
    /// <param name="folderPath">Existing movie folder path.</param>
    /// <param name="entry">Logical manifest entry for the movie.</param>
    /// <param name="streamUrl">New primary stream URL.</param>
    /// <param name="itemKey">Logical manifest key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "folderPath comes from persisted manifest data owned by JellyFed.")]
    public async Task RewriteMovieStreamAsync(
        string folderPath,
        ManifestEntry entry,
        string streamUrl,
        string itemKey,
        CancellationToken cancellationToken)
    {
        var folderName = Path.GetFileName(folderPath);
        var strmPath = Path.Combine(folderPath, $"{folderName}.strm");
        await File.WriteAllTextAsync(strmPath, streamUrl, Encoding.UTF8, cancellationToken)
            .ConfigureAwait(false);

        await RefreshMovieProvenanceAsync(folderPath, entry, itemKey, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes a new series item: folder, tvshow.nfo, poster and all seasons/episodes.
    /// </summary>
    /// <param name="contentRoot">Peer-scoped root where the series folder should be created.</param>
    /// <param name="item">Catalog snapshot of the series.</param>
    /// <param name="seasons">Fetched seasons and episodes for the primary source.</param>
    /// <param name="peer">Peer that supplied the primary source.</param>
    /// <param name="entry">Logical manifest entry for the series.</param>
    /// <param name="itemKey">Logical manifest key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The folder path written on disk.</returns>
    public async Task<string> WriteSeriesAsync(
        string contentRoot,
        CatalogItemDto item,
        SeasonsResponseDto seasons,
        PeerConfiguration peer,
        ManifestEntry entry,
        string itemKey,
        CancellationToken cancellationToken)
    {
        var folderName = SanitizeName($"{item.Title} ({item.Year})");
        var folderPath = Path.Combine(contentRoot, folderName);
        Directory.CreateDirectory(folderPath);

        await WriteSeriesFilesAsync(folderPath, item, seasons, peer, entry, itemKey, true, cancellationToken)
            .ConfigureAwait(false);

        var epCount = seasons.Seasons.Sum(s => s.Episodes.Count);
        _logger.LogInformation(
            "Wrote series: {Title} — {SeasonCount} seasons, {EpCount} episodes → {Path}",
            item.Title,
            seasons.Seasons.Count,
            epCount,
            folderPath);

        return folderPath;
    }

    /// <summary>
    /// Rewrites the primary files for a series. Existing season folders are removed first so
    /// the materialized episode list always matches the current primary source.
    /// </summary>
    /// <param name="folderPath">Existing series folder path.</param>
    /// <param name="item">Fresh catalog snapshot of the series.</param>
    /// <param name="seasons">Fetched seasons and episodes for the primary source.</param>
    /// <param name="peer">Peer that owns the primary source.</param>
    /// <param name="entry">Logical manifest entry for the series.</param>
    /// <param name="itemKey">Logical manifest key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task RewriteSeriesPrimaryAsync(
        string folderPath,
        CatalogItemDto item,
        SeasonsResponseDto seasons,
        PeerConfiguration peer,
        ManifestEntry entry,
        string itemKey,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folderPath);
        await WriteSeriesFilesAsync(folderPath, item, seasons, peer, entry, itemKey, true, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Refreshes only the provenance markers (peer/tag/studio) and sources sidecar of an existing series.
    /// </summary>
    /// <param name="folderPath">Existing series folder path.</param>
    /// <param name="entry">Logical manifest entry for the series.</param>
    /// <param name="itemKey">Logical manifest key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task RefreshSeriesProvenanceAsync(
        string folderPath,
        ManifestEntry entry,
        string itemKey,
        CancellationToken cancellationToken)
    {
        var tvshowNfoPath = Path.Combine(folderPath, "tvshow.nfo");
        await RewriteNfoProvenanceAsync(tvshowNfoPath, entry, cancellationToken).ConfigureAwait(false);

        foreach (var episodeNfo in Directory.EnumerateFiles(folderPath, "*.nfo", SearchOption.AllDirectories)
                     .Where(static path => !string.Equals(Path.GetFileName(path), "tvshow.nfo", StringComparison.OrdinalIgnoreCase)))
        {
            await RewriteNfoProvenanceAsync(episodeNfo, entry, cancellationToken).ConfigureAwait(false);
        }

        await WriteSourcesFileAsync(folderPath, entry, itemKey, "Series", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Rewrites only the episode materialization for a series, keeping the existing tvshow
    /// metadata file in place. Used for primary-source failover groundwork.
    /// </summary>
    /// <param name="folderPath">Existing series folder path.</param>
    /// <param name="seasons">Fetched seasons and episodes for the promoted primary source.</param>
    /// <param name="entry">Logical manifest entry for the series.</param>
    /// <param name="itemKey">Logical manifest key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "folderPath comes from persisted manifest data owned by JellyFed.")]
    public async Task RewriteSeriesEpisodeStreamsAsync(
        string folderPath,
        SeasonsResponseDto seasons,
        ManifestEntry entry,
        string itemKey,
        CancellationToken cancellationToken)
    {
        foreach (var seasonDir in Directory.EnumerateDirectories(folderPath, "Season *", SearchOption.TopDirectoryOnly))
        {
            Directory.Delete(seasonDir, true);
        }

        foreach (var season in seasons.Seasons)
        {
            var seasonNum = season.SeasonNumber ?? 0;
            var seasonFolder = Path.Combine(folderPath, $"Season {seasonNum:D2}");
            Directory.CreateDirectory(seasonFolder);

            foreach (var ep in season.Episodes)
            {
                var epNum = ep.EpisodeNumber ?? 0;
                var epName = SanitizeName($"S{seasonNum:D2}E{epNum:D2} - {ep.Title}");

                var strmPath = Path.Combine(seasonFolder, $"{epName}.strm");
                await File.WriteAllTextAsync(strmPath, ep.StreamUrl ?? string.Empty, Encoding.UTF8, cancellationToken)
                    .ConfigureAwait(false);

                var epNfoPath = Path.Combine(seasonFolder, $"{epName}.nfo");
                await File.WriteAllTextAsync(epNfoPath, BuildEpisodeNfo(ep, seasonNum, entry), Encoding.UTF8, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        await RefreshSeriesProvenanceAsync(folderPath, entry, itemKey, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes a previously synced item folder.
    /// </summary>
    /// <param name="folderPath">The item folder to remove.</param>
    public void DeleteItem(string folderPath)
    {
        if (Directory.Exists(folderPath))
        {
            Directory.Delete(folderPath, true);
            _logger.LogInformation("Deleted item: {Path}", folderPath);
        }
    }

    private async Task WriteMovieFilesAsync(
        string folderPath,
        CatalogItemDto item,
        PeerConfiguration peer,
        ManifestEntry entry,
        string itemKey,
        CancellationToken cancellationToken)
    {
        var folderName = Path.GetFileName(folderPath);
        var strmPath = Path.Combine(folderPath, $"{folderName}.strm");
        await File.WriteAllTextAsync(strmPath, item.StreamUrl ?? string.Empty, Encoding.UTF8, cancellationToken)
            .ConfigureAwait(false);

        var nfoPath = Path.Combine(folderPath, $"{folderName}.nfo");
        await File.WriteAllTextAsync(nfoPath, BuildMovieNfo(item, entry), Encoding.UTF8, cancellationToken)
            .ConfigureAwait(false);

        await DownloadArtworkAsync(item.PosterUrl, Path.Combine(folderPath, "poster.jpg"), cancellationToken)
            .ConfigureAwait(false);
        await DownloadArtworkAsync(item.BackdropUrl, Path.Combine(folderPath, "fanart.jpg"), cancellationToken)
            .ConfigureAwait(false);

        await WriteSourcesFileAsync(folderPath, entry, itemKey, "Movie", cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteSeriesFilesAsync(
        string folderPath,
        CatalogItemDto item,
        SeasonsResponseDto seasons,
        PeerConfiguration peer,
        ManifestEntry entry,
        string itemKey,
        bool replaceSeasonFolders,
        CancellationToken cancellationToken)
    {
        if (replaceSeasonFolders)
        {
            foreach (var seasonDir in Directory.EnumerateDirectories(folderPath, "Season *", SearchOption.TopDirectoryOnly))
            {
                Directory.Delete(seasonDir, true);
            }
        }

        var nfoPath = Path.Combine(folderPath, "tvshow.nfo");
        await File.WriteAllTextAsync(nfoPath, BuildSeriesNfo(item, entry), Encoding.UTF8, cancellationToken)
            .ConfigureAwait(false);

        await DownloadArtworkAsync(item.PosterUrl, Path.Combine(folderPath, "poster.jpg"), cancellationToken)
            .ConfigureAwait(false);
        await DownloadArtworkAsync(item.BackdropUrl, Path.Combine(folderPath, "fanart.jpg"), cancellationToken)
            .ConfigureAwait(false);

        foreach (var season in seasons.Seasons)
        {
            var seasonNum = season.SeasonNumber ?? 0;
            var seasonFolder = Path.Combine(folderPath, $"Season {seasonNum:D2}");
            Directory.CreateDirectory(seasonFolder);

            foreach (var ep in season.Episodes)
            {
                var epNum = ep.EpisodeNumber ?? 0;
                var epName = SanitizeName($"S{seasonNum:D2}E{epNum:D2} - {ep.Title}");

                var strmPath = Path.Combine(seasonFolder, $"{epName}.strm");
                await File.WriteAllTextAsync(strmPath, ep.StreamUrl ?? string.Empty, Encoding.UTF8, cancellationToken)
                    .ConfigureAwait(false);

                var epNfoPath = Path.Combine(seasonFolder, $"{epName}.nfo");
                await File.WriteAllTextAsync(epNfoPath, BuildEpisodeNfo(ep, seasonNum, entry), Encoding.UTF8, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        await WriteSourcesFileAsync(folderPath, entry, itemKey, "Series", cancellationToken).ConfigureAwait(false);
    }

    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "folderPath comes from persisted manifest data owned by JellyFed.")]
    private async Task WriteSourcesFileAsync(
        string folderPath,
        ManifestEntry entry,
        string itemKey,
        string itemType,
        CancellationToken cancellationToken)
    {
        var sourcesFile = new SourcesFile
        {
            ItemKey = itemKey,
            ItemType = itemType,
            PrimaryPeerName = entry.PeerName,
            PrimaryJellyfinId = entry.JellyfinId,
            Path = entry.Path,
            SyncedAt = entry.SyncedAt,
            Sources = entry.Sources
                .OrderByDescending(source => string.Equals(source.PeerName, entry.PeerName, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(SourcePixelCount)
                .ThenBy(source => source.PeerName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            EpisodeSources = entry.EpisodeSources
                .Select(group => new SeriesEpisodeSourceGroup
                {
                    SeasonNumber = group.SeasonNumber,
                    EpisodeNumber = group.EpisodeNumber,
                    Title = group.Title,
                    Sources = group.Sources
                        .OrderByDescending(source => string.Equals(source.PeerName, entry.PeerName, StringComparison.OrdinalIgnoreCase))
                        .ThenByDescending(SourcePixelCount)
                        .ThenBy(source => source.PeerName, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                })
                .OrderBy(group => group.SeasonNumber)
                .ThenBy(group => group.EpisodeNumber)
                .ToList()
        };

        var json = JsonSerializer.Serialize(sourcesFile, SourcesJsonOptions);
        await File.WriteAllTextAsync(Path.Combine(folderPath, SourcesFileName), json, Encoding.UTF8, cancellationToken)
            .ConfigureAwait(false);
    }

    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "nfoPath comes from persisted manifest data owned by JellyFed.")]
    private async Task RewriteNfoProvenanceAsync(
        string nfoPath,
        ManifestEntry entry,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(nfoPath))
        {
            return;
        }

        var xml = await File.ReadAllTextAsync(nfoPath, cancellationToken).ConfigureAwait(false);
        var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var root = doc.Root;
        if (root is null)
        {
            return;
        }

        ApplyProvenance(root, entry);
        await File.WriteAllTextAsync(nfoPath, doc.ToString(), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
    }

    private async Task DownloadArtworkAsync(string? url, string localPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        await _peerClient.DownloadImageAsync(url, localPath, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildMovieNfo(CatalogItemDto item, ManifestEntry entry)
    {
        var movieEl = new XElement(
            "movie",
            new XElement("title", item.Title),
            new XElement("originaltitle", item.OriginalTitle ?? item.Title),
            new XElement("year", item.Year),
            new XElement("plot", item.Overview ?? string.Empty),
            new XElement("runtime", item.RuntimeMinutes),
            new XElement("rating", item.VoteAverage?.ToString("F1", CultureInfo.InvariantCulture)),
            item.Genres.Select(g => new XElement("genre", g)),
            BuildUniqueIds(item));

        AddProvenance(movieEl, entry);

        var fileInfo = BuildFileInfo(item.VideoCodec, item.Width, item.Height, item.MediaStreams, item.AudioCodec);
        if (fileInfo is not null)
        {
            movieEl.Add(fileInfo);
        }

        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), movieEl).ToString();
    }

    private static string BuildSeriesNfo(CatalogItemDto item, ManifestEntry entry)
    {
        var tvshowEl = new XElement(
            "tvshow",
            new XElement("title", item.Title),
            new XElement("originaltitle", item.OriginalTitle ?? item.Title),
            new XElement("year", item.Year),
            new XElement("plot", item.Overview ?? string.Empty),
            new XElement("rating", item.VoteAverage?.ToString("F1", CultureInfo.InvariantCulture)),
            item.Genres.Select(g => new XElement("genre", g)),
            BuildUniqueIds(item));

        AddProvenance(tvshowEl, entry);

        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), tvshowEl).ToString();
    }

    private static string BuildEpisodeNfo(EpisodeDto ep, int seasonNumber, ManifestEntry entry)
    {
        var epEl = new XElement(
            "episodedetails",
            new XElement("title", ep.Title),
            new XElement("season", seasonNumber),
            new XElement("episode", ep.EpisodeNumber),
            new XElement("plot", ep.Overview ?? string.Empty),
            new XElement("aired", ep.AirDate ?? string.Empty),
            new XElement("runtime", ep.RuntimeMinutes));

        AddProvenance(epEl, entry);

        var fileInfo = BuildFileInfo(ep.VideoCodec, ep.Width, ep.Height, ep.MediaStreams, ep.AudioCodec);
        if (fileInfo is not null)
        {
            epEl.Add(fileInfo);
        }

        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), epEl).ToString();
    }

    private static void AddProvenance(XElement root, ManifestEntry entry)
    {
        ApplyProvenance(root, entry);
    }

    private static void ApplyProvenance(XElement root, ManifestEntry entry)
    {
        root.Elements("jellyfed_peer").Remove();
        root.Elements("jellyfed_id").Remove();
        root.Elements("jellyfed_source_count").Remove();

        foreach (var studio in root.Elements("studio")
                     .Where(static element => element.Value.StartsWith("JellyFed:", StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            studio.Remove();
        }

        foreach (var tag in root.Elements("tag")
                     .Where(static element =>
                         string.Equals(element.Value, "JellyFed", StringComparison.OrdinalIgnoreCase) ||
                         element.Value.StartsWith("JellyFed:", StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            tag.Remove();
        }

        root.Add(new XElement("jellyfed_peer", entry.PeerName));
        root.Add(new XElement("jellyfed_id", entry.JellyfinId));
        root.Add(new XElement("jellyfed_source_count", entry.Sources.Count));
        root.Add(new XElement("tag", "JellyFed"));
        root.Add(new XElement("tag", $"JellyFed:primary:{entry.PeerName}"));

        if (entry.Sources.Count > 1)
        {
            root.Add(new XElement("tag", "JellyFed:multi-source"));
        }

        foreach (var source in entry.Sources
                     .OrderByDescending(source => string.Equals(source.PeerName, entry.PeerName, StringComparison.OrdinalIgnoreCase))
                     .ThenByDescending(SourcePixelCount)
                     .ThenBy(source => source.PeerName, StringComparer.OrdinalIgnoreCase))
        {
            root.Add(new XElement("studio", $"JellyFed:{source.PeerName}"));
            root.Add(new XElement("tag", $"JellyFed:source:{source.PeerName}"));
        }
    }

    private static XElement? BuildFileInfo(
        string? videoCodec,
        int? width,
        int? height,
        IReadOnlyList<MediaStreamInfoDto> mediaStreams,
        string? fallbackAudioCodec)
    {
        if (string.IsNullOrEmpty(videoCodec) && mediaStreams.Count == 0)
        {
            return null;
        }

        var videoEl = new XElement("video");
        if (!string.IsNullOrEmpty(videoCodec))
        {
            videoEl.Add(new XElement("codec", videoCodec));
        }

        if (width.HasValue)
        {
            videoEl.Add(new XElement("width", width.Value));
        }

        if (height.HasValue)
        {
            videoEl.Add(new XElement("height", height.Value));
        }

        var streamdetails = new XElement("streamdetails", videoEl);

        bool hasAudio = false;
        foreach (var s in mediaStreams)
        {
            if (s.Type == "Audio")
            {
                hasAudio = true;
                var audioEl = new XElement("audio");
                if (!string.IsNullOrEmpty(s.Codec))
                {
                    audioEl.Add(new XElement("codec", s.Codec));
                }

                if (!string.IsNullOrEmpty(s.Language))
                {
                    audioEl.Add(new XElement("language", s.Language));
                }

                if (!string.IsNullOrEmpty(s.Title))
                {
                    audioEl.Add(new XElement("title", s.Title));
                }

                streamdetails.Add(audioEl);
            }
            else if (s.Type == "Subtitle")
            {
                var subEl = new XElement("subtitle");
                if (!string.IsNullOrEmpty(s.Language))
                {
                    subEl.Add(new XElement("language", s.Language));
                }

                if (!string.IsNullOrEmpty(s.Title))
                {
                    subEl.Add(new XElement("title", s.Title));
                }

                streamdetails.Add(subEl);
            }
        }

        if (!hasAudio && !string.IsNullOrEmpty(fallbackAudioCodec))
        {
            streamdetails.Add(new XElement("audio", new XElement("codec", fallbackAudioCodec)));
        }

        return new XElement("fileinfo", streamdetails);
    }

    private static XElement[] BuildUniqueIds(CatalogItemDto item)
    {
        if (!string.IsNullOrEmpty(item.TmdbId) && !string.IsNullOrEmpty(item.ImdbId))
        {
            return
            [
                new XElement("uniqueid", new XAttribute("type", "tmdb"), new XAttribute("default", "true"), item.TmdbId),
                new XElement("uniqueid", new XAttribute("type", "imdb"), item.ImdbId)
            ];
        }

        if (!string.IsNullOrEmpty(item.TmdbId))
        {
            return [new XElement("uniqueid", new XAttribute("type", "tmdb"), new XAttribute("default", "true"), item.TmdbId)];
        }

        if (!string.IsNullOrEmpty(item.ImdbId))
        {
            return [new XElement("uniqueid", new XAttribute("type", "imdb"), new XAttribute("default", "true"), item.ImdbId)];
        }

        return [];
    }

    private static int SourcePixelCount(ManifestSource source)
        => (source.Width ?? 0) * (source.Height ?? 0);

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        return sb.ToString();
    }
}
