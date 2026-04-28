using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyFed.Api.Dto;
using Jellyfin.Plugin.JellyFed.Audit;
using Jellyfin.Plugin.JellyFed.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyFed.Sync;

/// <summary>
/// HTTP client for querying the JellyFed federation API of a remote peer.
/// </summary>
public class PeerClient
{
    private readonly HttpClient _http;
    private readonly AuditLogService _auditLogService;
    private readonly ILogger<PeerClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PeerClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Instance of <see cref="IHttpClientFactory"/>.</param>
    /// <param name="auditLogService">Audit service.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{PeerClient}"/> interface.</param>
    public PeerClient(IHttpClientFactory httpClientFactory, AuditLogService auditLogService, ILogger<PeerClient> logger)
    {
        _http = httpClientFactory.CreateClient("JellyFed");
        _auditLogService = auditLogService;
        _logger = logger;
    }

    /// <summary>
    /// Fetches the full catalog from a peer, optionally filtered by date.
    /// </summary>
    /// <param name="peer">The peer configuration.</param>
    /// <param name="since">Only return items updated after this date (delta sync).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The catalog response, or null on failure.</returns>
    public async Task<CatalogResponseDto?> GetCatalogAsync(
        PeerConfiguration peer,
        DateTime? since,
        CancellationToken cancellationToken)
    {
        var suffix = "/catalog";
        if (since.HasValue)
        {
            suffix += $"?since={Uri.EscapeDataString(since.Value.ToString("O"))}";
        }

        var url = BuildUrl(peer.Url, FederationProtocol.ToPath(suffix));

        try
        {
            var result = await GetJsonAsync<CatalogResponseDto>(
                peer.Url,
                peer.FederationToken,
                FederationProtocol.ToPath(suffix),
                cancellationToken).ConfigureAwait(false);

            if (result is null)
            {
                _auditLogService.WritePeerEvent(
                    peer,
                    "peer.catalog-fetch.failed",
                    $"Catalog fetch failed for {peer.Name}.",
                    AuditLogSeverities.Warning,
                    new { url });
            }

            return result;
        }
        catch (Exception ex)
        {
            _auditLogService.WritePeerEvent(
                peer,
                "peer.catalog-fetch.exception",
                $"Catalog fetch failed for {peer.Name}: {ex.Message}",
                AuditLogSeverities.Warning,
                new { url, error = ex.Message });
            _logger.LogError(ex, "Failed to fetch catalog from peer {PeerName} ({Url})", peer.Name, peer.Url);
            return null;
        }
    }

    /// <summary>
    /// Fetches seasons and episodes for a series from a peer.
    /// </summary>
    /// <param name="peer">The peer configuration.</param>
    /// <param name="seriesId">The Jellyfin series ID on the remote peer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The seasons response, or null on failure.</returns>
    public async Task<SeasonsResponseDto?> GetSeasonsAsync(
        PeerConfiguration peer,
        string seriesId,
        CancellationToken cancellationToken)
    {
        var suffix = $"/catalog/series/{seriesId}/seasons";
        var url = BuildUrl(peer.Url, FederationProtocol.ToPath(suffix));

        try
        {
            var result = await GetJsonAsync<SeasonsResponseDto>(
                peer.Url,
                peer.FederationToken,
                FederationProtocol.ToPath(suffix),
                cancellationToken).ConfigureAwait(false);

            if (result is null)
            {
                _auditLogService.WritePeerEvent(
                    peer,
                    "peer.seasons-fetch.failed",
                    $"Season fetch failed for {peer.Name} / series {seriesId}.",
                    AuditLogSeverities.Warning,
                    new { url, seriesId });
            }

            return result;
        }
        catch (Exception ex)
        {
            _auditLogService.WritePeerEvent(
                peer,
                "peer.seasons-fetch.exception",
                $"Season fetch failed for {peer.Name}: {ex.Message}",
                AuditLogSeverities.Warning,
                new { url, seriesId, error = ex.Message });
            _logger.LogError(ex, "Failed to fetch seasons for series {SeriesId} from {PeerName}", seriesId, peer.Name);
            return null;
        }
    }

    /// <summary>
    /// Pings a peer and returns its JellyFed system information when reachable.
    /// </summary>
    /// <param name="url">Peer base URL.</param>
    /// <param name="federationToken">Federation token to present in the Bearer header.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The remote system info, or <see langword="null"/> when unreachable.</returns>
    public async Task<FederationSystemInfoDto?> GetSystemInfoAsync(
        string url,
        string? federationToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        try
        {
            var info = await GetJsonAsync<FederationSystemInfoDto>(
                url,
                federationToken,
                FederationProtocol.ToPath("system/info"),
                cts.Token).ConfigureAwait(false);

            if (info is not null)
            {
                NormalizeSystemInfo(info);
                return info;
            }

            return null;
        }
#pragma warning disable CA1031 // Handshake is best-effort; any failure is reported as unreachable.
        catch
        {
            return null;
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Pings a peer's health/system-info endpoint with the provided URL + token.
    /// Used by the admin UI when adding or editing a peer, so misconfiguration shows up immediately.
    /// </summary>
    /// <param name="url">Peer base URL.</param>
    /// <param name="federationToken">Federation token to present in the Bearer header.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tuple (reachable, reportedVersion); reportedVersion is null when unreachable.</returns>
    public async Task<(bool Reachable, string? Version)> HealthCheckAsync(
        string url,
        string federationToken,
        CancellationToken cancellationToken)
    {
        var info = await GetSystemInfoAsync(url, federationToken, cancellationToken).ConfigureAwait(false);
        return (info is not null, info?.Version);
    }

    /// <summary>
    /// Fetches the discovery payload advertised by a direct peer.
    /// The payload contains the peer itself plus any direct peers it is willing to suggest.
    /// </summary>
    /// <param name="peer">The direct peer to query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The discovery payload, or null on failure.</returns>
    public async Task<DiscoveryResponseDto?> GetDiscoveryAsync(
        PeerConfiguration peer,
        CancellationToken cancellationToken)
    {
        var url = BuildUrl(peer.Url, FederationProtocol.ToPath("/discovery"));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", peer.FederationToken);

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<DiscoveryResponseDto>(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to fetch discovery payload from peer {PeerName} ({Url})", peer.Name, url);
            return null;
        }
    }

    /// <summary>
    /// Downloads a remote image to a local file path.
    /// </summary>
    /// <param name="imageUrl">The remote image URL.</param>
    /// <param name="localPath">The local destination path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if downloaded successfully.</returns>
    public async Task<bool> DownloadImageAsync(
        string imageUrl,
        string localPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await _http.GetByteArrayAsync(new Uri(imageUrl), cancellationToken)
                .ConfigureAwait(false);
            await System.IO.File.WriteAllBytesAsync(localPath, bytes, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download image {Url}", imageUrl);
            return false;
        }
    }

    /// <summary>
    /// Announces this instance to a peer so it can register us as a peer in return.
    /// If the peer responds with a per-peer <c>accessToken</c>, that token replaces the
    /// peer's global <c>FederationToken</c> in the local config so that future API calls
    /// use the revocable per-peer credential instead of the shared global token.
    /// </summary>
    /// <param name="peer">The peer to notify.</param>
    /// <param name="selfName">Friendly name of this instance.</param>
    /// <param name="selfUrl">URL of this instance reachable by the peer.</param>
    /// <param name="selfToken">Federation token of this instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task RegisterOnPeerAsync(
        PeerConfiguration peer,
        string selfName,
        string selfUrl,
        string selfToken,
        CancellationToken cancellationToken)
    {
        PeerIdentity.EnsurePeerId(peer);
        var url = BuildUrl(peer.Url, FederationProtocol.ToPath("/peer/register"));

        try
        {
            var payload = new RegisterPeerRequestDto
            {
                Name = selfName,
                Url = selfUrl,
                FederationToken = selfToken
            };

            var result = await PostJsonAsync<RegisterPeerResponseDto>(
                peer.Url,
                null,
                FederationProtocol.ToPath("/peer/register"),
                payload,
                cancellationToken).ConfigureAwait(false);

            if (result is null)
            {
                return;
            }

            _logger.LogInformation("JellyFed: registered on peer {PeerName}.", peer.Name);

            if (string.IsNullOrEmpty(result.AccessToken) ||
                !string.Equals(result.Status, "ok", StringComparison.Ordinal))
            {
                return;
            }

            var config = Plugin.Instance?.Configuration;
            if (config is null)
            {
                return;
            }

            var localPeer = config.Peers.FirstOrDefault(p =>
                string.Equals(p.Url, peer.Url, StringComparison.OrdinalIgnoreCase));

            if (localPeer is not null &&
                !string.Equals(localPeer.FederationToken, result.AccessToken, StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(localPeer.DiscoveryToken))
                {
                    localPeer.DiscoveryToken = localPeer.FederationToken;
                }

                localPeer.FederationToken = result.AccessToken;
                Plugin.Instance!.SaveConfiguration();
                _logger.LogInformation(
                    "JellyFed: stored per-peer access token from {PeerName} — future API calls use revocable token.",
                    peer.Name);
                _auditLogService.WritePeerEvent(
                    localPeer,
                    "peer.access-token-received",
                    $"Stored a revocable access token issued by {peer.Name}.",
                    details: new { peer.Url });
            }
        }
        catch (Exception ex)
        {
            _auditLogService.WritePeerEvent(
                peer,
                "peer.registration-outbound.exception",
                $"Could not auto-register on peer {peer.Name}: {ex.Message}",
                AuditLogSeverities.Warning,
                new { url, error = ex.Message });
            _logger.LogDebug(ex, "JellyFed: could not register on peer {PeerName} (non-fatal)", peer.Name);
        }
    }

    private async Task<T?> GetJsonAsync<T>(
        string baseUrl,
        string? bearerToken,
        string path,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUrl(baseUrl, path));
        ApplyBearer(request, bearerToken);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return default;
        }

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<TResponse?> PostJsonAsync<TResponse>(
        string baseUrl,
        string? bearerToken,
        string path,
        object payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl(baseUrl, path))
        {
            Content = JsonContent.Create(payload)
        };
        ApplyBearer(request, bearerToken);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return default;
        }

        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken)
            .ConfigureAwait(false);
    }

    private static void NormalizeSystemInfo(FederationSystemInfoDto info)
    {
        info.Name = string.IsNullOrWhiteSpace(info.Name) ? "JellyFed" : info.Name;
        info.Capabilities ??= [];
    }

    private static void ApplyBearer(HttpRequestMessage request, string? bearerToken)
    {
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }
    }

    private static string BuildUrl(string baseUrl, string path)
        => baseUrl.TrimEnd('/') + path;
}
