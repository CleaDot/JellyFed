using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Jellyfin.Plugin.JellyFed;

namespace Jellyfin.Plugin.JellyFed.Sync;

/// <summary>
/// Persisted manifest of all items written by JellyFed.
/// Keyed by logical TMDB identity ("tmdb:12345") or by peer-scoped fallback key
/// ("no-tmdb:{peer}:{id}") when no stable shared identifier exists.
/// </summary>
public class Manifest
{
    /// <summary>Gets or sets the schema version of this persisted document.</summary>
    public int SchemaVersion { get; set; } = FederationProtocol.CurrentSchemaVersion;

    /// <summary>Gets or sets the synced movies: logical key → manifest entry.</summary>
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Required so persisted manifest documents can be migrated and deserialized safely.")]
    public Dictionary<string, ManifestEntry> Movies { get; set; } = [];

    /// <summary>Gets or sets the synced series: logical key → manifest entry.</summary>
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Required so persisted manifest documents can be migrated and deserialized safely.")]
    public Dictionary<string, ManifestEntry> Series { get; set; } = [];
}
