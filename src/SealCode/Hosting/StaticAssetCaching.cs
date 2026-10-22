using System.Text.RegularExpressions;

using Microsoft.AspNetCore.StaticFiles;

namespace SealCode.Hosting;

/// <summary>
/// Caches content-addressed assets and revalidates stable entry points.
/// </summary>
internal static partial class StaticAssetCaching
{
    /// <summary>
    /// Sets the cache policy for a static response.
    /// </summary>
    /// <param name="context">The static file response context.</param>
    public static void Apply(StaticFileResponseContext context)
    {
        context.Context.Response.Headers.CacheControl = HashedFileName().IsMatch(context.File.Name)
            ? "public, max-age=31536000, immutable" : "no-cache";
    }

    [GeneratedRegex(@"-[A-Z0-9]{8}\.", RegexOptions.CultureInvariant)]
    private static partial Regex HashedFileName();
}
