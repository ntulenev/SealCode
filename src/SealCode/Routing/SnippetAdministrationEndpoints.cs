using SealCode.Hosting;
using SealCode.Security;

using Transport.Models;

namespace SealCode.Routing;

/// <summary>Maps the super-admin database snippet editor.</summary>
internal static class SnippetAdministrationEndpoints
{
    /// <summary>Registers the editor and its shared publication API.</summary>
    public static void MapSnippetAdministrationEndpoints(this WebApplication app)
    {
        _ = app.MapGet("/admin/snippets/manage/access", (HttpContext context,
            SnippetAdministration editor, IAdminUserManager admins) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Json(new { CanEdit = editor.CanEdit(admins) });
        });
        var group = app.MapGroup("/admin/snippets/manage").AddEndpointFilter(async (invocation, next) =>
        {
            var context = invocation.HttpContext;
            context.Response.Headers.CacheControl = "no-store";
            var admins = context.RequestServices.GetRequiredService<IAdminUserManager>();
            var editor = context.RequestServices.GetRequiredService<SnippetAdministration>();
            if (!admins.IsAdmin())
            {
                return Results.Unauthorized();
            }

            if (!editor.Enabled)
            {
                return Results.NotFound();
            }

            if (!editor.CanEdit(admins))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            return await next(invocation).ConfigureAwait(false);
        });
        _ = group.MapGet("", (IWebHostEnvironment env) =>
            Results.File(Path.Combine(env.ContentRootPath, "Resources", "Snippets", "manage.html"), "text/html"));
        _ = group.MapGet("/catalog", async (SnippetAdministration editor, CancellationToken cancellationToken) =>
        {
            var snapshot = (await editor.LoadAsync(cancellationToken).ConfigureAwait(false))!;
            return Results.Json(new SnippetEditorCatalogResponse(snapshot.UpdatedAt,
                [.. snapshot.Library.Categories.Select(SnippetCategoryResponse.From)]));
        });
        _ = group.MapPost("/{language}/{number:int}", async (string language, int number,
            UpdateSnippetRequest request, SnippetAdministration editor, CancellationToken cancellationToken) =>
            await editor.PublishAsync(language, number, request, cancellationToken).ConfigureAwait(false)
                ? Results.Ok()
                : Results.Conflict(new { error = "Catalog changed. Reload before publishing; your draft has not been saved." }));
    }
}
