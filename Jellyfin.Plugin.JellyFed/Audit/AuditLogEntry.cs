using System;
using System.Text.Json;

namespace Jellyfin.Plugin.JellyFed.Audit;

/// <summary>
/// A single persistent JellyFed audit event.
/// </summary>
public sealed class AuditLogEntry
{
    /// <summary>
    /// Gets or sets the database identifier.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp of the event.
    /// </summary>
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");

    /// <summary>
    /// Gets or sets the broad event category.
    /// </summary>
    public string Category { get; set; } = AuditLogCategories.General;

    /// <summary>
    /// Gets or sets the event type slug.
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the severity level.
    /// </summary>
    public string Severity { get; set; } = AuditLogSeverities.Info;

    /// <summary>
    /// Gets or sets the human-readable message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the stable peer identifier when attribution is confident.
    /// </summary>
    public string? PeerId { get; set; }

    /// <summary>
    /// Gets or sets the peer display name snapshot.
    /// </summary>
    public string? PeerName { get; set; }

    /// <summary>
    /// Gets or sets the peer URL snapshot.
    /// </summary>
    public string? PeerUrl { get; set; }

    /// <summary>
    /// Gets or sets the actor type (system/admin/peer).
    /// </summary>
    public string? ActorType { get; set; }

    /// <summary>
    /// Gets or sets the actor identifier.
    /// </summary>
    public string? ActorId { get; set; }

    /// <summary>
    /// Gets or sets the actor display name.
    /// </summary>
    public string? ActorName { get; set; }

    /// <summary>
    /// Gets or sets the authentication mode used for the request.
    /// </summary>
    public string? AuthMode { get; set; }

    /// <summary>
    /// Gets or sets the HTTP method if applicable.
    /// </summary>
    public string? Method { get; set; }

    /// <summary>
    /// Gets or sets the HTTP path if applicable.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Gets or sets the response status code if applicable.
    /// </summary>
    public int? StatusCode { get; set; }

    /// <summary>
    /// Gets or sets the request IP address if applicable.
    /// </summary>
    public string? RemoteIp { get; set; }

    /// <summary>
    /// Gets or sets additional structured details as JSON.
    /// </summary>
    public string? DetailsJson { get; set; }

    /// <summary>
    /// Serializes a structured details object to JSON.
    /// </summary>
    /// <param name="details">The details object.</param>
    /// <returns>Serialized JSON, or null.</returns>
    public static string? SerializeDetails(object? details)
    {
        if (details is null)
        {
            return null;
        }

        return JsonSerializer.Serialize(details);
    }
}
