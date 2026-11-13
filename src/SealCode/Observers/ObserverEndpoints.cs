using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;

using Models;
using Models.Configuration;

using SealCode.Routing;

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
        _ = routes.RequireAdmin().RequireRoomOwner();
        _ = routes.MapGet("", (RoomId roomId,
            ObserverSessions observers, IOptions<ApplicationConfiguration> settings) =>
        {
            var url = observers.GetLink(roomId);
            return Results.Json(new { settings.Value.Observers.Enabled, settings.Value.Observers.MaxPerRoom, HasLink = url is not null, Url = url, ObserverCount = observers.Count(roomId).Count });
        });
        _ = routes.MapPost("", (HttpContext context,
            ObserverSessions observers, IOptions<ApplicationConfiguration> settings) =>
        {
            if (!settings.Value.Observers.Enabled)
            {
                return Results.StatusCode(503);
            }

            return Results.Json(new { Url = observers.CreateLink(context.GetOwnedRoom()) });
        });
        _ = routes.MapDelete("", (RoomId roomId, ObserverSessions observers) =>
        {
            observers.Disable(roomId);
            return Results.Ok();
        });
    }
}
