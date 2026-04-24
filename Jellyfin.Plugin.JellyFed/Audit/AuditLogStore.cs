using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.JellyFed.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyFed.Audit;

/// <summary>
/// SQLite-backed persistent audit store.
/// </summary>
public sealed class AuditLogStore
{
    private const string DatabaseFileName = ".jellyfed-audit.sqlite3";

    private readonly object _gate = new();
    private readonly ILogger<AuditLogStore> _logger;
    private string? _initializedPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditLogStore"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public AuditLogStore(ILogger<AuditLogStore> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Persists a single audit event.
    /// </summary>
    /// <param name="entry">The entry to write.</param>
    public void Write(AuditLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var databasePath = TryGetDatabasePath();
        if (databasePath is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(entry.CreatedAt))
        {
            entry.CreatedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        }

        lock (_gate)
        {
            try
            {
                using var connection = OpenConnection(databasePath);
                using var command = connection.CreateCommand();
                command.CommandText = @"
INSERT INTO audit_logs (
    created_at,
    category,
    event_type,
    severity,
    message,
    peer_id,
    peer_name,
    peer_url,
    actor_type,
    actor_id,
    actor_name,
    auth_mode,
    method,
    path,
    status_code,
    remote_ip,
    details_json)
VALUES (
    $createdAt,
    $category,
    $eventType,
    $severity,
    $message,
    $peerId,
    $peerName,
    $peerUrl,
    $actorType,
    $actorId,
    $actorName,
    $authMode,
    $method,
    $path,
    $statusCode,
    $remoteIp,
    $detailsJson);";

                command.Parameters.AddWithValue("$createdAt", entry.CreatedAt);
                command.Parameters.AddWithValue("$category", entry.Category);
                command.Parameters.AddWithValue("$eventType", entry.EventType);
                command.Parameters.AddWithValue("$severity", entry.Severity);
                command.Parameters.AddWithValue("$message", entry.Message);
                AddNullable(command, "$peerId", entry.PeerId);
                AddNullable(command, "$peerName", entry.PeerName);
                AddNullable(command, "$peerUrl", entry.PeerUrl);
                AddNullable(command, "$actorType", entry.ActorType);
                AddNullable(command, "$actorId", entry.ActorId);
                AddNullable(command, "$actorName", entry.ActorName);
                AddNullable(command, "$authMode", entry.AuthMode);
                AddNullable(command, "$method", entry.Method);
                AddNullable(command, "$path", entry.Path);
                AddNullable(command, "$remoteIp", entry.RemoteIp);
                AddNullable(command, "$detailsJson", entry.DetailsJson);
                if (entry.StatusCode.HasValue)
                {
                    command.Parameters.AddWithValue("$statusCode", entry.StatusCode.Value);
                }
                else
                {
                    command.Parameters.AddWithValue("$statusCode", DBNull.Value);
                }

                command.ExecuteNonQuery();
            }
#pragma warning disable CA1031 // Audit writes must never crash the plugin.
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "JellyFed audit: failed to write audit event {EventType}", entry.EventType);
            }
#pragma warning restore CA1031
        }
    }

    /// <summary>
    /// Queries persisted audit records.
    /// </summary>
    /// <param name="query">The query parameters.</param>
    /// <returns>Paged results.</returns>
    public AuditLogFeed Query(AuditLogQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var databasePath = TryGetDatabasePath();
        if (databasePath is null || !File.Exists(databasePath))
        {
            return new AuditLogFeed();
        }

        var limit = Math.Clamp(query.Limit, 1, 200);
        var items = new List<AuditLogEntry>(limit + 1);

        lock (_gate)
        {
            using var connection = OpenConnection(databasePath);
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    id,
    created_at,
    category,
    event_type,
    severity,
    message,
    peer_id,
    peer_name,
    peer_url,
    actor_type,
    actor_id,
    actor_name,
    auth_mode,
    method,
    path,
    status_code,
    remote_ip,
    details_json
FROM audit_logs
WHERE 1 = 1";

            AppendScopeFilter(command, query.Scope);

            if (!string.IsNullOrWhiteSpace(query.PeerId))
            {
                command.CommandText += " AND peer_id = $peerId";
                command.Parameters.AddWithValue("$peerId", query.PeerId.Trim());
            }

            if (query.BeforeId.HasValue)
            {
                command.CommandText += " AND id < $beforeId";
                command.Parameters.AddWithValue("$beforeId", query.BeforeId.Value);
            }

            command.CommandText += " ORDER BY id DESC LIMIT $limit";
            command.Parameters.AddWithValue("$limit", limit + 1);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                items.Add(ReadEntry(reader));
            }
        }

        var hasMore = items.Count > limit;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new AuditLogFeed
        {
            Items = items,
            HasMore = hasMore,
            NextBeforeId = hasMore && items.Count > 0 ? items[^1].Id : null
        };
    }

    /// <summary>
    /// Returns summary counters and peer facets for the logs UI.
    /// </summary>
    /// <param name="configuredPeers">Current configured peers.</param>
    /// <returns>Overview data.</returns>
    public AuditLogOverview GetOverview(IReadOnlyList<PeerConfiguration> configuredPeers)
    {
        var databasePath = TryGetDatabasePath();
        var overview = new AuditLogOverview
        {
            Peers = BuildPeerFacets(configuredPeers, [])
        };

        if (databasePath is null || !File.Exists(databasePath))
        {
            return overview;
        }

        lock (_gate)
        {
            using var connection = OpenConnection(databasePath);
            using (var summary = connection.CreateCommand())
            {
                summary.CommandText = @"
SELECT
    COUNT(*) AS total_count,
    COALESCE(SUM(CASE WHEN category = 'security' THEN 1 ELSE 0 END), 0) AS security_count,
    COALESCE(SUM(CASE WHEN category = 'peer-connection' THEN 1 ELSE 0 END), 0) AS peer_connection_count,
    COALESCE(SUM(CASE WHEN category = 'peer-access' THEN 1 ELSE 0 END), 0) AS peer_access_count,
    COALESCE(SUM(CASE WHEN created_at >= $since THEN 1 ELSE 0 END), 0) AS last_24h_count,
    MAX(created_at) AS last_event_at
FROM audit_logs;";
                summary.Parameters.AddWithValue("$since", DateTime.UtcNow.AddHours(-24).ToString("O", CultureInfo.InvariantCulture));

                using var reader = summary.ExecuteReader();
                if (reader.Read())
                {
                    overview.TotalCount = reader.GetInt32(0);
                    overview.SecurityCount = reader.GetInt32(1);
                    overview.PeerConnectionCount = reader.GetInt32(2);
                    overview.PeerAccessCount = reader.GetInt32(3);
                    overview.Last24HoursCount = reader.GetInt32(4);
                    overview.LastEventAt = reader.IsDBNull(5) ? null : reader.GetString(5);
                }
            }

            using var peers = connection.CreateCommand();
            peers.CommandText = @"
SELECT peer_id, peer_name, peer_url, MAX(id) AS last_id
FROM audit_logs
WHERE peer_name IS NOT NULL OR peer_id IS NOT NULL
GROUP BY peer_id, peer_name, peer_url
ORDER BY last_id DESC
LIMIT 100;";

            var historicalPeers = new List<AuditLogPeerFacet>();
            using (var reader = peers.ExecuteReader())
            {
                while (reader.Read())
                {
                    historicalPeers.Add(new AuditLogPeerFacet
                    {
                        PeerId = reader.IsDBNull(0) ? null : reader.GetString(0),
                        Name = reader.IsDBNull(1) ? "(unknown peer)" : reader.GetString(1),
                        Url = reader.IsDBNull(2) ? null : reader.GetString(2)
                    });
                }
            }

            overview.Peers = BuildPeerFacets(configuredPeers, historicalPeers);
        }

        return overview;
    }

    private static void AddNullable(SqliteCommand command, string name, string? value)
    {
        command.Parameters.AddWithValue(name, string.IsNullOrWhiteSpace(value) ? DBNull.Value : value);
    }

    private static AuditLogEntry ReadEntry(SqliteDataReader reader)
        => new()
        {
            Id = reader.GetInt64(0),
            CreatedAt = reader.GetString(1),
            Category = reader.GetString(2),
            EventType = reader.GetString(3),
            Severity = reader.GetString(4),
            Message = reader.GetString(5),
            PeerId = reader.IsDBNull(6) ? null : reader.GetString(6),
            PeerName = reader.IsDBNull(7) ? null : reader.GetString(7),
            PeerUrl = reader.IsDBNull(8) ? null : reader.GetString(8),
            ActorType = reader.IsDBNull(9) ? null : reader.GetString(9),
            ActorId = reader.IsDBNull(10) ? null : reader.GetString(10),
            ActorName = reader.IsDBNull(11) ? null : reader.GetString(11),
            AuthMode = reader.IsDBNull(12) ? null : reader.GetString(12),
            Method = reader.IsDBNull(13) ? null : reader.GetString(13),
            Path = reader.IsDBNull(14) ? null : reader.GetString(14),
            StatusCode = reader.IsDBNull(15) ? null : reader.GetInt32(15),
            RemoteIp = reader.IsDBNull(16) ? null : reader.GetString(16),
            DetailsJson = reader.IsDBNull(17) ? null : reader.GetString(17)
        };

    private static void AppendScopeFilter(SqliteCommand command, string scope)
    {
        var normalized = (scope ?? string.Empty).Trim().ToLowerInvariant();
        var category = normalized switch
        {
            "security" => AuditLogCategories.Security,
            "peer-connections" => AuditLogCategories.PeerConnection,
            "peer-access" => AuditLogCategories.PeerAccess,
            _ => null
        };

        if (category is not null)
        {
            command.CommandText += " AND category = $scopeCategory";
            command.Parameters.AddWithValue("$scopeCategory", category);
        }
    }

    private static IReadOnlyList<AuditLogPeerFacet> BuildPeerFacets(
        IReadOnlyList<PeerConfiguration> configuredPeers,
        IReadOnlyList<AuditLogPeerFacet> historicalPeers)
    {
        var merged = new Dictionary<string, AuditLogPeerFacet>(StringComparer.Ordinal);

        foreach (var peer in historicalPeers)
        {
            if (string.IsNullOrWhiteSpace(peer.PeerId))
            {
                continue;
            }

            merged[peer.PeerId] = peer;
        }

        foreach (var peer in configuredPeers)
        {
            if (string.IsNullOrWhiteSpace(peer.PeerId))
            {
                continue;
            }

            merged[peer.PeerId] = new AuditLogPeerFacet
            {
                PeerId = peer.PeerId,
                Name = peer.Name,
                Url = peer.Url
            };
        }

        return [.. merged.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)];
    }

    private SqliteConnection OpenConnection(string databasePath)
    {
        EnsureInitialized(databasePath);
        var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        return connection;
    }

    private void EnsureInitialized(string databasePath)
    {
        if (string.Equals(_initializedPath, databasePath, StringComparison.Ordinal) && File.Exists(databasePath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(databasePath) ?? ".");

        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE IF NOT EXISTS audit_logs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    created_at TEXT NOT NULL,
    category TEXT NOT NULL,
    event_type TEXT NOT NULL,
    severity TEXT NOT NULL,
    message TEXT NOT NULL,
    peer_id TEXT NULL,
    peer_name TEXT NULL,
    peer_url TEXT NULL,
    actor_type TEXT NULL,
    actor_id TEXT NULL,
    actor_name TEXT NULL,
    auth_mode TEXT NULL,
    method TEXT NULL,
    path TEXT NULL,
    status_code INTEGER NULL,
    remote_ip TEXT NULL,
    details_json TEXT NULL
);
CREATE INDEX IF NOT EXISTS idx_audit_logs_created_at ON audit_logs(created_at DESC);
CREATE INDEX IF NOT EXISTS idx_audit_logs_category_created_at ON audit_logs(category, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_audit_logs_peer_id_created_at ON audit_logs(peer_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_audit_logs_path_created_at ON audit_logs(path, created_at DESC);
PRAGMA user_version = 1;";
        command.ExecuteNonQuery();
        _initializedPath = databasePath;
    }

    private static string? TryGetDatabasePath()
    {
        var libraryPath = Plugin.Instance?.Configuration.LibraryPath;
        if (string.IsNullOrWhiteSpace(libraryPath))
        {
            return null;
        }

        return Path.Combine(libraryPath, DatabaseFileName);
    }
}
