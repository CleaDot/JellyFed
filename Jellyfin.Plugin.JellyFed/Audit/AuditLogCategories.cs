namespace Jellyfin.Plugin.JellyFed.Audit;

/// <summary>
/// Canonical audit log category names.
/// </summary>
public static class AuditLogCategories
{
    /// <summary>
    /// General operational events.
    /// </summary>
    public const string General = "general";

    /// <summary>
    /// Security-sensitive events.
    /// </summary>
    public const string Security = "security";

    /// <summary>
    /// Peer connectivity, sync and registration events.
    /// </summary>
    public const string PeerConnection = "peer-connection";

    /// <summary>
    /// Requests made by remote peers against this instance.
    /// </summary>
    public const string PeerAccess = "peer-access";
}
