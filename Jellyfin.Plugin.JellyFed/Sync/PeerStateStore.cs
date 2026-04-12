using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Jellyfin.Plugin.JellyFed.Sync;

/// <summary>
/// Persists peer online/offline states to disk between restarts.
/// Stored at {libraryPath}/.jellyfed-peers.json.
/// </summary>
public static class PeerStateStore
{
    private const string FileName = ".jellyfed-peers.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Loads the peer state map from disk.
    /// </summary>
    /// <param name="libraryPath">The JellyFed library root path.</param>
    /// <returns>A dictionary of peer name → PeerStatus.</returns>
    public static Dictionary<string, PeerStatus> Load(string libraryPath)
    {
        var path = Path.Combine(libraryPath, FileName);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, PeerStatus>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Saves the peer state map to disk.
    /// </summary>
    /// <param name="libraryPath">The JellyFed library root path.</param>
    /// <param name="states">The current peer states.</param>
    public static void Save(string libraryPath, Dictionary<string, PeerStatus> states)
    {
        Directory.CreateDirectory(libraryPath);
        var path = Path.Combine(libraryPath, FileName);
        File.WriteAllText(path, JsonSerializer.Serialize(states, JsonOptions));
    }
}
