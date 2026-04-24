using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.JellyFed.Audit;

/// <summary>
/// Stores and retrieves request attribution from <see cref="HttpContext.Items"/>.
/// </summary>
public static class FederationRequestIdentityAccessor
{
    private const string ContextKey = "JellyFed.FederationRequestIdentity";

    /// <summary>
    /// Saves the current request identity.
    /// </summary>
    /// <param name="httpContext">HTTP context.</param>
    /// <param name="identity">Identity to store.</param>
    public static void Set(HttpContext httpContext, FederationRequestIdentity identity)
        => httpContext.Items[ContextKey] = identity;

    /// <summary>
    /// Reads the current request identity.
    /// </summary>
    /// <param name="httpContext">HTTP context.</param>
    /// <returns>The stored identity, or null.</returns>
    public static FederationRequestIdentity? Get(HttpContext httpContext)
        => httpContext.Items.TryGetValue(ContextKey, out var value)
            ? value as FederationRequestIdentity
            : null;
}
