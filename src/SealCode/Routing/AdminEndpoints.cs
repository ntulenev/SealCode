using Abstractions;

using Models;

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
        _ = app.MapGet("/admin/login", (IWebHostEnvironment env) =>
        {
            var path = Path.Combine(env.WebRootPath, "admin-login.html");
            return Results.File(path, "text/html");
        });

        _ = app.MapPost("/admin/login",
                    async (IAdminUserManager adminUserManager,
                           CancellationToken cancellationToken) =>
        {
            return await adminUserManager.TrySetCurrentUserAsync(cancellationToken).ConfigureAwait(false)
                ? Results.Redirect("/admin")
                : Results.Redirect("/admin/login?error=1");
        });

        _ = app.MapPost("/admin/logout", async (IAdminUserManager adminUserManager) =>
        {
            await adminUserManager.ClearCurrentAdminUserAsync().ConfigureAwait(false);
            return Results.Redirect("/admin/login");
        });

        _ = app.MapGet("/admin", (IWebHostEnvironment env, IAdminUserManager adminUserManager) =>
        {
            if (!adminUserManager.IsAdmin())
            {
                return Results.Redirect("/admin/login");
            }

            var path = Path.Combine(env.WebRootPath, "admin.html");
            return Results.File(path, "text/html");
        });

        _ = app.MapGet("/admin/rooms", (IRoomManager roomManager, IAdminUserManager adminUserManager) =>
        {
            if (!adminUserManager.TryGetAdminUser(out var adminUser))
            {
                return Results.Unauthorized();
            }

            var rooms = roomManager.GetRoomsSnapshot(adminUser)
                .Select(room => new RoomSummary(
                    room.RoomId.Value,
                    room.Name.Value,
                    room.Language.Value,
                    room.UsersCount,
                    room.LastUpdatedUtc,
                    room.CreatedBy.Name,
                    room.CanDelete))
                .ToArray();

            return Results.Json(rooms);
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
                CreatedBy = room.CreatedBy.Name
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
