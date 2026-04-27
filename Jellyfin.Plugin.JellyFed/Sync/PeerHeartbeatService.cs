using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyFed.Audit;
using Jellyfin.Plugin.JellyFed.Configuration;
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

    private readonly PeerClient _peerClient;
    private readonly AuditLogService _auditLogService;
    private readonly ILogger<PeerHeartbeatService> _logger;
    private Timer? _timer;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PeerHeartbeatService"/> class.
    /// </summary>
    /// <param name="peerClient">Federation HTTP client with route-version fallback support.</param>
    /// <param name="auditLogService">Audit service.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{PeerHeartbeatService}"/> interface.</param>
    public PeerHeartbeatService(PeerClient peerClient, AuditLogService auditLogService, ILogger<PeerHeartbeatService> logger)
    {
        _peerClient = peerClient;
        _auditLogService = auditLogService;
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

            PeerIdentity.EnsurePeerId(peer);
            var wasOnline = status.Online;
            var previousVersion = status.Version;

            try
            {
                var info = await _peerClient
                    .GetSystemInfoAsync(peer.Url, peer.FederationToken, cancellationToken)
                    .ConfigureAwait(false);

                if (info is not null)
                {
                    status.MarkOnline(info.Version, status.MovieCount, status.SeriesCount);

                    var discovery = await _peerClient.GetDiscoveryAsync(peer, cancellationToken)
                        .ConfigureAwait(false);
                    if (discovery?.Self is not null)
                    {
                        status.Discoverable = discovery.Self.Discoverable;
                        if (!string.IsNullOrWhiteSpace(discovery.Self.Version))
                        {
                            status.Version = discovery.Self.Version;
                        }
                    }

                    _logger.LogDebug(
                        "JellyFed heartbeat: {PeerName} online (v{Version}, route={Route}).",
                        peer.Name,
                        status.Version,
                        info.PreferredRoutePrefix);

                    if (!wasOnline || !string.Equals(previousVersion, status.Version, StringComparison.Ordinal))
                    {
                        _auditLogService.WritePeerEvent(
                            peer,
                            "peer.heartbeat.online",
                            $"Peer {peer.Name} is reachable.",
                            details: new { version = status.Version, wasOnline, route = info.PreferredRoutePrefix, discoverable = status.Discoverable });
                    }
                }
                else
                {
                    status.MarkOffline();
                    if (wasOnline)
                    {
                        _auditLogService.WritePeerEvent(
                            peer,
                            "peer.heartbeat.offline",
                            $"Peer {peer.Name} is no longer reachable.",
                            AuditLogSeverities.Warning);
                    }
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
            {
                status.MarkOffline();
                _logger.LogDebug("JellyFed heartbeat: {PeerName} unreachable — {Message}", peer.Name, ex.Message);
                if (wasOnline)
                {
                    _auditLogService.WritePeerEvent(
                        peer,
                        "peer.heartbeat.offline",
                        $"Peer {peer.Name} became unreachable.",
                        AuditLogSeverities.Warning,
                        new { error = ex.Message });
                }
            }

            changed = true;
        }

        if (changed)
        {
            PeerStateStore.Save(config.LibraryPath, states);
        }
    }
}
