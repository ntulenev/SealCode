using System.Globalization;

using Abstractions;

using SealCode.Security;

using Transport.Models;

namespace SealCode.Routing;

/// <summary>
/// Maps authenticated snippet pages and transport contracts.
/// </summary>
internal static class SnippetEndpoints
{
    /// <summary>
    /// Registers the private question library endpoints.
    /// </summary>
    /// <param name="app">The application to configure.</param>
    /// <exception cref="ArgumentNullException">Thrown when the application is null.</exception>
    public static void MapSnippetEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        _ = app.MapGet("/admin/snippets/access", (HttpContext context, ISnippetCatalog catalog, IAdminUserManager admins) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var allowed = admins.TryGetAdminUser(out var admin) && catalog.CanAccess(admin);
            var available = catalog.Available;
            return Results.Json(new SnippetAccessResponse(allowed && available, allowed && !available));
        });

        var api = app.MapGroup("").RequireAdmin(noStore: true);
        var pages = app.MapGroup("").RequireAdmin(redirect: true, noStore: true);
        _ = api.MapGet("/admin/snippets/catalog", (HttpContext context, ISnippetCatalog catalog) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var admin = context.GetRequiredAdmin();
            if (!catalog.Enabled)
            {
                return Results.NotFound();
            }
            if (!catalog.CanAccess(admin))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            if (!catalog.Available)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
            var snapshot = catalog.GetCatalogSnapshot(admin);
            context.Response.Headers["X-Snippets-Synced-At"] = snapshot.LastSyncedAt.ToString("O", CultureInfo.InvariantCulture);
            return Results.Json(snapshot.Library.Categories.Select(SnippetCategoryResponse.From).ToArray());
        });

        _ = pages.MapGet("/admin/snippets", (HttpContext context, ISnippetCatalog catalog, IWebHostEnvironment env) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var admin = context.GetRequiredAdmin();
            return !catalog.Enabled ? Results.NotFound()
                : !catalog.CanAccess(admin) ? Results.StatusCode(StatusCodes.Status403Forbidden)
                : !catalog.Available ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable)
                : Results.File(Path.Combine(env.ContentRootPath, "Resources", "Snippets", "library.html"), "text/html");
        });
    }
}
