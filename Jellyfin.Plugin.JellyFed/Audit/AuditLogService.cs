using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JellyFed.Configuration;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.JellyFed.Audit;

/// <summary>
/// High-level audit helpers for JellyFed events.
/// </summary>
public sealed class AuditLogService
{
    private readonly AuditLogStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditLogService"/> class.
    /// </summary>
    /// <param name="store">Persistent store.</param>
    public AuditLogService(AuditLogStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Resolves a presented federation token to the strongest available peer attribution.
    /// </summary>
    /// <param name="token">Presented token.</param>
    /// <returns>Resolved identity, or null when invalid.</returns>
    public FederationRequestIdentity? ResolveFederationToken(string? token)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var trimmed = token.Trim();
        var perPeer = config.Peers.FirstOrDefault(p =>
            p.Enabled &&
            !string.IsNullOrWhiteSpace(p.AccessToken) &&
            string.Equals(trimmed, p.AccessToken, StringComparison.Ordinal));

        if (perPeer is not null)
        {
            PeerIdentity.EnsurePeerId(perPeer);
            return new FederationRequestIdentity
            {
                AuthMode = "peer-access-token",
                PeerId = perPeer.PeerId,
                PeerName = perPeer.Name,
                PeerUrl = perPeer.Url,
                PresentedToken = trimmed
            };
        }

        if (string.Equals(trimmed, config.FederationToken, StringComparison.Ordinal))
        {
            return new FederationRequestIdentity
            {
                AuthMode = "global-federation-token",
                PresentedToken = trimmed
            };
        }

        return null;
    }

    /// <summary>
    /// Writes a prebuilt audit event.
    /// </summary>
    /// <param name="entry">Entry to persist.</param>
    public void Write(AuditLogEntry entry)
        => _store.Write(entry);

    /// <summary>
    /// Writes a request-scoped audit event using the current attribution when available.
    /// </summary>
    /// <param name="httpContext">HTTP context.</param>
    /// <param name="category">Event category.</param>
    /// <param name="eventType">Event type.</param>
    /// <param name="message">Human message.</param>
    /// <param name="severity">Severity.</param>
    /// <param name="statusCode">Status code.</param>
    /// <param name="details">Optional structured details.</param>
    public void WriteRequestEvent(
        HttpContext httpContext,
        string category,
        string eventType,
        string message,
        string severity = AuditLogSeverities.Info,
        int? statusCode = null,
        object? details = null)
    {
        var identity = FederationRequestIdentityAccessor.Get(httpContext);
        Write(new AuditLogEntry
        {
            Category = category,
            EventType = eventType,
            Severity = severity,
            Message = message,
            PeerId = identity?.PeerId,
            PeerName = identity?.PeerName,
            PeerUrl = identity?.PeerUrl,
            ActorType = identity?.IsPeerAttributed == true ? "peer" : "external-peer",
            ActorId = identity?.PeerId,
            ActorName = identity?.PeerName,
            AuthMode = identity?.AuthMode,
            Method = httpContext.Request.Method,
            Path = httpContext.Request.Path.Value,
            StatusCode = statusCode,
            RemoteIp = httpContext.Connection.RemoteIpAddress?.ToString(),
            DetailsJson = AuditLogEntry.SerializeDetails(details)
        });
    }

    /// <summary>
    /// Writes a peer lifecycle or connectivity event.
    /// </summary>
    /// <param name="peer">Peer snapshot.</param>
    /// <param name="eventType">Event type.</param>
    /// <param name="message">Message.</param>
    /// <param name="severity">Severity.</param>
    /// <param name="details">Additional details.</param>
    public void WritePeerEvent(
        PeerConfiguration peer,
        string eventType,
        string message,
        string severity = AuditLogSeverities.Info,
        object? details = null)
    {
        ArgumentNullException.ThrowIfNull(peer);
        PeerIdentity.EnsurePeerId(peer);

        Write(new AuditLogEntry
        {
            Category = AuditLogCategories.PeerConnection,
            EventType = eventType,
            Severity = severity,
            Message = message,
            PeerId = peer.PeerId,
            PeerName = peer.Name,
            PeerUrl = peer.Url,
            ActorType = "system",
            AuthMode = null,
            DetailsJson = AuditLogEntry.SerializeDetails(details)
        });
    }

    /// <summary>
    /// Writes a security event.
    /// </summary>
    /// <param name="eventType">Event type.</param>
    /// <param name="message">Message.</param>
    /// <param name="httpContext">Optional request context.</param>
    /// <param name="severity">Severity.</param>
    /// <param name="details">Structured details.</param>
    public void WriteSecurityEvent(
        string eventType,
        string message,
        HttpContext? httpContext = null,
        string severity = AuditLogSeverities.Warning,
        object? details = null)
    {
        var identity = httpContext is null ? null : FederationRequestIdentityAccessor.Get(httpContext);
        Write(new AuditLogEntry
        {
            Category = AuditLogCategories.Security,
            EventType = eventType,
            Severity = severity,
            Message = message,
            PeerId = identity?.PeerId,
            PeerName = identity?.PeerName,
            PeerUrl = identity?.PeerUrl,
            ActorType = identity?.IsPeerAttributed == true ? "peer" : "unknown",
            ActorId = identity?.PeerId,
            ActorName = identity?.PeerName,
            AuthMode = identity?.AuthMode,
            Method = httpContext?.Request.Method,
            Path = httpContext?.Request.Path.Value,
            StatusCode = null,
            RemoteIp = httpContext?.Connection.RemoteIpAddress?.ToString(),
            DetailsJson = AuditLogEntry.SerializeDetails(details)
        });
    }

    /// <summary>
    /// Queries audit records.
    /// </summary>
    /// <param name="query">Query parameters.</param>
    /// <returns>Paged results.</returns>
    public AuditLogFeed Query(AuditLogQuery query)
        => _store.Query(query);

    /// <summary>
    /// Gets logs overview data.
    /// </summary>
    /// <returns>Overview.</returns>
    public AuditLogOverview GetOverview()
        => _store.GetOverview(GetConfiguredPeers());

    private static List<PeerConfiguration> GetConfiguredPeers()
        => Plugin.Instance?.Configuration?.Peers ?? [];
}
