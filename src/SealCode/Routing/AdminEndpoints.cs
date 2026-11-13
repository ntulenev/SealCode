using Abstractions;

using Microsoft.Extensions.Options;

using Models;
using Models.Configuration;

using SealCode.Hosting;
using SealCode.Security;

using Transport.Models;

namespace SealCode.Routing;

/// <summary>
/// Registers admin authentication and room management endpoints.
/// </summary>
internal static class AdminEndpoints
{
    /// <summary>
    /// Maps the admin routes for the application.
    /// </summary>
    /// <param name="app">The application to configure.</param>
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("").RequireAdmin();
        var uncachedApi = app.MapGroup("").RequireAdmin(noStore: true);
        var pages = app.MapGroup("").RequireAdmin(redirect: true);

        _ = app.MapGet("/admin/login", (IWebHostEnvironment env, HttpContext context) =>
        {
            var path = Path.Combine(env.WebRootPath, "admin-login.html");
            StaticAssetCaching.Apply(context.Response, path);
            return Results.File(path, "text/html");
        });

        _ = app.MapPost("/admin/login",
                    async (IAdminUserManager adminUserManager,
                           CancellationToken cancellationToken) =>
        {
            return await adminUserManager.TrySetCurrentUserAsync(cancellationToken).ConfigureAwait(false)
                ? Results.Redirect("/admin")
                : Results.Redirect("/admin/login?error=1");
        }).AddEndpointFilter(app.Services.GetRequiredService<AdminHttpSecurity>().FilterLoginAsync);

        _ = app.MapGet("/admin/google-config", (IOptions<ApplicationConfiguration> settings, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Json(new { settings.Value.GoogleSignIn.ClientId });
        });

        _ = app.MapPost("/admin/google-login",
            async (GoogleLoginRequest request,
                   IOptions<ApplicationConfiguration> settings,
                   IGoogleIdentityVerifier verifier,
                   IAdminUserManager admins) =>
        {
            var clientId = settings.Value.GoogleSignIn.ClientId;
            if (string.IsNullOrWhiteSpace(clientId)
                || string.IsNullOrWhiteSpace(request.Credential)
                || request.Credential.Length > 8192)
            {
                return Results.Unauthorized();
            }

            var identity = await verifier.VerifyAsync(request.Credential, clientId).ConfigureAwait(false);
            return identity is not null && await admins.TrySetGoogleCurrentUserAsync(identity).ConfigureAwait(false)
                ? Results.Ok()
                : Results.Unauthorized();
        }).AddEndpointFilter(app.Services.GetRequiredService<AdminHttpSecurity>().FilterGoogleLoginAsync);

        _ = app.MapPost("/admin/logout", async (IAdminUserManager adminUserManager) =>
        {
            await adminUserManager.ClearCurrentAdminUserAsync().ConfigureAwait(false);
            return Results.Redirect("/admin/login");
        });

        _ = pages.MapGet("/admin", (IWebHostEnvironment env, HttpContext context) =>
        {

            var path = Path.Combine(env.WebRootPath, "admin.html");
            StaticAssetCaching.Apply(context.Response, path);
            return Results.File(path, "text/html");
        });

        _ = pages.MapGet("/admin/limits", (IWebHostEnvironment env, HttpContext context) =>
        {

            var path = Path.Combine(env.WebRootPath, "admin-limits.html");
            StaticAssetCaching.Apply(context.Response, path);
            return Results.File(path, "text/html");
        });

        _ = uncachedApi.MapGet("/admin/limits/config", (IOptions<ApplicationConfiguration> settings,
            HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";

            return Results.Json(new { settings.Value.MaxUsersPerRoom, settings.Value.Limits, settings.Value.Observers });
        });

        _ = uncachedApi.MapGet("/admin/me", (IUsageStatisticsRepository statistics, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var adminUser = context.GetRequiredAdmin();
            return Results.Json(new { adminUser.DisplayName, adminUser.IsSuperAdmin, StatisticsEnabled = statistics.Enabled });
        });

        _ = uncachedApi.MapGet("/admin/rooms", (IRoomManager roomManager,
            HttpContext context, bool mine = false) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var adminUser = context.GetRequiredAdmin();

            var rooms = roomManager.GetRoomsSnapshot(adminUser)
                .Where(room => !mine || room.CreatedBy.Matches(adminUser))
                .Select(room => new RoomSummary(
                    room.RoomId.Value,
                    room.Name.Value,
                    room.Language.Value,
                    room.UsersCount,
                    room.LastUpdatedUtc,
                    room.CreatedBy.DisplayName,
                    room.CanDelete)
                { Kind = room.Kind.ToWireName() })
                .ToArray();

            return Results.Json(rooms);
        });

        _ = api.MapGet("/admin/rooms/{roomId:ShortGuid}",
                    (RoomId roomId,
                     IRoomManager roomManager,
                     HttpContext context) =>
        {
            var adminUser = context.GetRequiredAdmin();

            if (!roomManager.TryGetRoom(roomId, out var room))
            {
                return Results.NotFound();
            }

            context.Response.Headers.CacheControl = "no-store";
            return Results.Json(new { CanDelete = room.CanDelete(adminUser) });
        });

        _ = api.MapPost("/admin/rooms",
                    async (HttpContext context,
                           TrackedRoomCreator creator,
                           ILanguageValidator languageValidator,
                           CancellationToken cancellationToken) =>
        {
            var adminUser = context.GetRequiredAdmin();

            var payload = await context.Request.ReadFromJsonAsync<CreateRoomRequest>(cancellationToken).ConfigureAwait(false);
            if (payload is null || string.IsNullOrWhiteSpace(payload.Name))
            {
                return Results.BadRequest(new
                {
                    error = "Name is required"
                });
            }

            if (!RoomKindWireNames.TryParseWireName(payload.Kind, out var kind))
            {
                return Results.BadRequest(new { error = "Invalid room kind" });
            }
            var language = new RoomLanguage(kind == RoomKind.Whiteboard ? "plaintext" : payload.Language ?? languageValidator.DefaultLanguage);
            if (kind == RoomKind.Code && !languageValidator.IsValid(language))
            {
                return Results.BadRequest(new
                {
                    error = "Invalid language"
                });
            }
            var name = new RoomName(payload.Name);
            var room = await creator.CreateAsync(name, language, adminUser, kind, cancellationToken).ConfigureAwait(false);
            return Results.Json(new
            {
                RoomId = room.RoomId.Value,
                Name = room.Name.Value,
                Language = room.Language.Value,
                Kind = room.Kind.ToWireName(),
                CreatedBy = room.CreatedBy.DisplayName
            });
        });

        _ = api.MapDelete("/admin/rooms/{roomId:ShortGuid}",
                    async (RoomId roomId,
                           IRoomManager roomManager,
                           HttpContext context,
                           CancellationToken cancellationToken) =>
        {
            var adminUser = context.GetRequiredAdmin();

            var result = await roomManager.DeleteRoomAsync(roomId, adminUser, cancellationToken).ConfigureAwait(false);
            return result switch
            {
                RoomDeletionResult.Deleted => Results.Ok(),
                RoomDeletionResult.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
                RoomDeletionResult.NotFound => Results.NotFound(),
                _ => throw new InvalidOperationException("Unknown room deletion result.")
            };
        });

    }
}
