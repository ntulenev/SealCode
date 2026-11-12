using Abstractions;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;

using Models;
using Models.Configuration;

using SealCode.Security;

namespace SealCode.Observers;

/// <summary>
/// Owner-only guest-link management with explicit antiforgery validation.
/// </summary>
internal static class ObserverEndpoints
{
    /// <summary>
    /// Maps owner-authorized guest-link commands and their antiforgery filter.
    /// </summary>
    /// <param name="app">The application receiving the endpoints.</param>
    public static void MapObserverEndpoints(this WebApplication app)
    {
        var routes = app.MapGroup("/room/{roomId:ShortGuid}/observer-link");
        _ = routes.AddEndpointFilter(async (invocation, next) =>
        {
            invocation.HttpContext.Response.Headers.CacheControl = "no-store";
            if (HttpMethods.IsPost(invocation.HttpContext.Request.Method) || HttpMethods.IsDelete(invocation.HttpContext.Request.Method))
            {
                try
                {
                    await invocation.HttpContext.RequestServices.GetRequiredService<IAntiforgery>()
                        .ValidateRequestAsync(invocation.HttpContext).ConfigureAwait(false);
                }
                catch (AntiforgeryValidationException) { return Results.BadRequest(); }
            }
            return await next(invocation).ConfigureAwait(false);
        });
        _ = routes.MapGet("", (RoomId roomId, IRoomManager rooms, IAdminUserManager admins,
            ObserverSessions observers, IOptions<ApplicationConfiguration> settings) =>
        {
            if (!admins.TryGetAdminUser(out var admin))
            {
                return Results.Unauthorized();
            }

            if (!rooms.TryGetRoom(roomId, out var room))
            {
                return Results.NotFound();
            }

            if (!room.CanDelete(admin))
            {
                return Results.StatusCode(403);
            }

            return Results.Json(new { settings.Value.Observers.Enabled, settings.Value.Observers.MaxPerRoom, HasLink = observers.HasLink(roomId), ObserverCount = observers.Count(roomId).Count });
        });
        _ = routes.MapPost("", (RoomId roomId, IRoomManager rooms, IAdminUserManager admins,
            ObserverSessions observers, IOptions<ApplicationConfiguration> settings) =>
        {
            if (!admins.TryGetAdminUser(out var admin))
            {
                return Results.Unauthorized();
            }

            if (!rooms.TryGetRoom(roomId, out var room))
            {
                return Results.NotFound();
            }

            if (!room.CanDelete(admin))
            {
                return Results.StatusCode(403);
            }

            if (!settings.Value.Observers.Enabled)
            {
                return Results.StatusCode(503);
            }

            return Results.Json(new { Url = observers.CreateLink(room) });
        });
        _ = routes.MapDelete("", (RoomId roomId, IRoomManager rooms, IAdminUserManager admins, ObserverSessions observers) =>
        {
            if (!admins.TryGetAdminUser(out var admin))
            {
                return Results.Unauthorized();
            }

            if (!rooms.TryGetRoom(roomId, out var room))
            {
                return Results.NotFound();
            }

            if (!room.CanDelete(admin))
            {
                return Results.StatusCode(403);
            }

            observers.Disable(roomId);
            return Results.Ok();
        });
    }
}
