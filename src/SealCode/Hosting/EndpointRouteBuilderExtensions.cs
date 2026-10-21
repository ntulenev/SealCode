using Abstractions;

using Microsoft.Extensions.Options;

using Models;
using Models.Configuration;

namespace SealCode.Hosting;

/// <summary>
/// Maps the application's HTTP endpoints.
/// </summary>
internal static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the public application endpoints and antiforgery token endpoint.
    /// </summary>
    /// <param name="app">The application whose public HTTP endpoints are mapped.</param>
    public static void MapSealCodeEndpoints(this WebApplication app)
    {
        var applicationVersion = ApplicationVersion.From(typeof(Program).Assembly);
        _ = app.MapGet("/admin/token", (HttpContext context, Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Json(new
            {
                Token = antiforgery.GetAndStoreTokens(context).RequestToken
            });
        });

        _ = app.MapGet("/languages", (ILanguageValidator validator)
            => Results.Json(validator.Languages));

        _ = app.MapGet("/features", (IOptions<ApplicationConfiguration> settings)
            => Results.Json(new
            {
                settings.Value.CollaborativeUndoEnabled,
                MaxYjsMessageBytes = RoomLimits.MaxSignalRMessageBytes,
                settings.Value.Limits
            }));

        _ = app.MapGet("/room/{roomId:ShortGuid}", (RoomId roomId, IRoomRegistry registry, IWebHostEnvironment env) =>
        {
            if (!registry.TryGetRoom(roomId, out _))
            {
                return Results.NotFound("Room not found");
            }

            var path = Path.Combine(env.WebRootPath, "room.html");
            return Results.File(path, "text/html");
        });

        _ = app.MapGet("/health", () => Results.Ok("ok"));

        _ = app.MapGet("/version", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Json(applicationVersion);
        });
    }
}
