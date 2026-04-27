namespace Jellyfin.Plugin.JellyFed.Audit;

/// <summary>
/// Canonical audit severity names.
/// </summary>
public static class AuditLogSeverities
{
    /// <summary>
    /// Informational event.
    /// </summary>
    public const string Info = "info";

    /// <summary>
    /// Warning event.
    /// </summary>
    public const string Warning = "warning";

    /// <summary>
    /// Error event.
    /// </summary>
    public const string Error = "error";
}
