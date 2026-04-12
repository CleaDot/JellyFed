using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyFed.Sync;

/// <summary>
/// Background service that periodically pings all configured peers
/// and updates their online/offline status in <see cref="PeerStateStore"/>.
/// </summary>
public class PeerHeartbeatService : IHostedService, IDisposable
{
    private const int HeartbeatIntervalMinutes = 5;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PeerHeartbeatService> _logger;
    private Timer? _timer;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PeerHeartbeatService"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Instance of <see cref="IHttpClientFactory"/>.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{PeerHeartbeatService}"/> interface.</param>
    public PeerHeartbeatService(IHttpClientFactory httpClientFactory, ILogger<PeerHeartbeatService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("JellyFed heartbeat service started (interval: {Minutes} min).", HeartbeatIntervalMinutes);
        _timer = new Timer(
            _ => _ = PingAllPeersAsync(CancellationToken.None),
            null,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(HeartbeatIntervalMinutes));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("JellyFed heartbeat service stopping.");
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases managed resources.
    /// </summary>
    /// <param name="disposing">Whether managed resources should be released.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _timer?.Dispose();
        }

        _disposed = true;
    }

    private async Task PingAllPeersAsync(CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || string.IsNullOrWhiteSpace(config.LibraryPath))
        {
            return;
        }

        var states = PeerStateStore.Load(config.LibraryPath);
        var http = _httpClientFactory.CreateClient("JellyFed");
        bool changed = false;

        foreach (var peer in config.Peers)
        {
            if (!peer.Enabled)
            {
                continue;
            }

            if (!states.TryGetValue(peer.Name, out var status))
            {
                status = new PeerStatus();
                states[peer.Name] = status;
            }

            try
            {
                var url = peer.Url.TrimEnd('/') + "/JellyFed/health";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", peer.FederationToken);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(10));

                using var response = await http.SendAsync(request, cts.Token).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    var health = await response.Content
                        .ReadFromJsonAsync<HealthResponse>(cts.Token)
                        .ConfigureAwait(false);

                    status.MarkOnline(health?.Version ?? string.Empty, status.MovieCount, status.SeriesCount);
                    _logger.LogDebug("JellyFed heartbeat: {PeerName} online (v{Version}).", peer.Name, health?.Version);
                }
                else
                {
                    status.MarkOffline();
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or TaskCanceledException)
            {
                status.MarkOffline();
                _logger.LogDebug("JellyFed heartbeat: {PeerName} unreachable — {Message}", peer.Name, ex.Message);
            }

            changed = true;
        }

        if (changed)
        {
            PeerStateStore.Save(config.LibraryPath, states);
        }
    }

    private sealed class HealthResponse
    {
        public string? Version { get; set; }
    }
}
