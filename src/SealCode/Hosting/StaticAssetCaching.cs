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
        => Apply(context.Context.Response, context.File.Name);

    /// <summary>
    /// Sets the shared cache policy for a file response.
    /// </summary>
    /// <param name="response">The outgoing response.</param>
    /// <param name="path">The file path.</param>
    public static void Apply(HttpResponse response, string path)
    {
        response.Headers.CacheControl = !string.Equals(Path.GetExtension(path), ".html", StringComparison.OrdinalIgnoreCase)
            && HashedFileName().IsMatch(Path.GetFileName(path))
            ? "public, max-age=31536000, immutable" : "no-cache";
    }

    [GeneratedRegex(@"-[A-Z0-9]{8}\.", RegexOptions.CultureInvariant)]
    private static partial Regex HashedFileName();
}
