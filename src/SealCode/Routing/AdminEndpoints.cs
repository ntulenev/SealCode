using Abstractions;

using Microsoft.Extensions.Options;

using Models;
using Models.Configuration;

using SealCode.Security;
using SealCode.Hosting;

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

        _ = app.MapGet("/admin", (IWebHostEnvironment env, IAdminUserManager adminUserManager, HttpContext context) =>
        {
            if (!adminUserManager.IsAdmin())
            {
                return Results.Redirect("/admin/login");
            }

            var path = Path.Combine(env.WebRootPath, "admin.html");
            StaticAssetCaching.Apply(context.Response, path);
            return Results.File(path, "text/html");
        });

        _ = app.MapGet("/admin/limits", (IWebHostEnvironment env, IAdminUserManager adminUserManager, HttpContext context) =>
        {
            if (!adminUserManager.IsAdmin())
            {
                return Results.Redirect("/admin/login");
            }

            var path = Path.Combine(env.WebRootPath, "admin-limits.html");
            StaticAssetCaching.Apply(context.Response, path);
            return Results.File(path, "text/html");
        });

        _ = app.MapGet("/admin/limits/config", (IOptions<ApplicationConfiguration> settings,
            IAdminUserManager adminUserManager, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!adminUserManager.IsAdmin())
            {
                return Results.Unauthorized();
            }

            return Results.Json(new { settings.Value.MaxUsersPerRoom, settings.Value.Limits });
        });

        _ = app.MapGet("/admin/me", (IAdminUserManager adminUserManager, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return adminUserManager.TryGetAdminUser(out var adminUser)
                ? Results.Json(new { adminUser.DisplayName })
                : Results.Unauthorized();
        });

        _ = app.MapGet("/admin/rooms", (IRoomManager roomManager, IAdminUserManager adminUserManager,
            HttpContext context, bool mine = false) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!adminUserManager.TryGetAdminUser(out var adminUser))
            {
                return Results.Unauthorized();
            }

            var rooms = roomManager.GetRoomsSnapshot(adminUser)
                .Where(room => !mine || room.CreatedBy.Matches(adminUser))
                .Select(room => new RoomSummary(
                    room.RoomId.Value,
                    room.Name.Value,
                    room.Language.Value,
                    room.UsersCount,
                    room.LastUpdatedUtc,
                    room.CreatedBy.DisplayName,
                    room.CanDelete))
                .ToArray();

            return Results.Json(rooms);
        });

        _ = app.MapGet("/admin/rooms/{roomId:ShortGuid}",
                    (RoomId roomId,
                     IRoomManager roomManager,
                     IAdminUserManager adminUserManager,
                     HttpContext context) =>
        {
            if (!adminUserManager.TryGetAdminUser(out var adminUser))
            {
                return Results.Unauthorized();
            }

            if (!roomManager.TryGetRoom(roomId, out var room))
            {
                return Results.NotFound();
            }

            context.Response.Headers.CacheControl = "no-store";
            return Results.Json(new { CanDelete = room.CanDelete(adminUser) });
        });

        _ = app.MapPost("/admin/rooms",
                    async (HttpContext context,
                           IRoomManager roomManager,
                           ILanguageValidator languageValidator,
                           IAdminUserManager adminUserManager,
                           CancellationToken cancellationToken) =>
        {
            if (!adminUserManager.TryGetAdminUser(out var adminUser))
            {
                return Results.Unauthorized();
            }

            var payload = await context.Request.ReadFromJsonAsync<CreateRoomRequest>(cancellationToken).ConfigureAwait(false);
            if (payload is null || string.IsNullOrWhiteSpace(payload.Name))
            {
                return Results.BadRequest(new
                {
                    error = "Name is required"
                });
            }

            var language = new RoomLanguage(payload.Language ?? "csharp");
            if (!languageValidator.IsValid(language))
            {
                return Results.BadRequest(new
                {
                    error = "Invalid language"
                });
            }
            var name = new RoomName(payload.Name);
            var room = roomManager.CreateRoom(name, language, adminUser);
            return Results.Json(new
            {
                RoomId = room.RoomId.Value,
                Name = room.Name.Value,
                Language = room.Language.Value,
                CreatedBy = room.CreatedBy.DisplayName
            });
        });

        _ = app.MapDelete("/admin/rooms/{roomId:ShortGuid}",
                    async (RoomId roomId,
                           IRoomManager roomManager,
                           IAdminUserManager adminUserManager,
                           CancellationToken cancellationToken) =>
        {
            if (!adminUserManager.TryGetAdminUser(out var adminUser))
            {
                return Results.Unauthorized();
            }

            var result = await roomManager.DeleteRoomAsync(roomId, adminUser, cancellationToken).ConfigureAwait(false);
            return result switch
            {
                RoomDeletionResult.Deleted => Results.Ok(),
                RoomDeletionResult.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
                RoomDeletionResult.NotFound => Results.NotFound(),
                _ => throw new NotImplementedException()
            };
        });

    }
}

internal sealed record GoogleLoginRequest(string Credential);
