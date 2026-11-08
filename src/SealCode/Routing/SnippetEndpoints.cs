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
            return Results.Json(new SnippetAccessResponse(admins.TryGetAdminUser(out var admin) && catalog.CanAccess(admin)));
        });

        _ = app.MapGet("/admin/snippets/catalog", (HttpContext context, ISnippetCatalog catalog, IAdminUserManager admins) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!admins.TryGetAdminUser(out var admin))
            {
                return Results.Unauthorized();
            }
            return !catalog.Enabled ? Results.NotFound()
                : !catalog.CanAccess(admin) ? Results.StatusCode(StatusCodes.Status403Forbidden)
                : Results.Json(catalog.GetSnapshot(admin).Categories.Select(SnippetCategoryResponse.From).ToArray());
        });

        _ = app.MapGet("/admin/snippets", (HttpContext context, ISnippetCatalog catalog, IAdminUserManager admins, IWebHostEnvironment env) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!admins.TryGetAdminUser(out var admin))
            {
                return Results.Redirect("/admin/login");
            }
            return !catalog.Enabled ? Results.NotFound()
                : !catalog.CanAccess(admin) ? Results.StatusCode(StatusCodes.Status403Forbidden)
                : Results.File(Path.Combine(env.ContentRootPath, "Resources", "Snippets", "library.html"), "text/html");
        });
    }
}
