using Abstractions;

using SealCode.Security;

namespace SealCode.Routing;

/// <summary>Super-administrator-only usage reports and retention controls.</summary>
internal static class StatisticsEndpoints
{
    public static void MapStatisticsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/admin/statistics").AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            var admins = context.HttpContext.RequestServices.GetRequiredService<IAdminUserManager>();
            if (!admins.TryGetAdminUser(out var user))
            {
                return Results.Unauthorized();
            }
            return user.IsSuperAdmin ? await next(context).ConfigureAwait(false) : Results.StatusCode(StatusCodes.Status403Forbidden);
        });
        _ = group.MapGet("", (IWebHostEnvironment environment) =>
            Results.File(Path.Combine(environment.ContentRootPath, "Resources", "Statistics", "index.html"), "text/html"));
        _ = group.MapGet("/data", async (IUsageStatisticsRepository repository, CancellationToken cancellationToken,
            string? userId = null, int offset = 0) =>
        {
            if (offset < 0 || offset > 1_000_000 || userId?.Length > 512)
            {
                return Results.BadRequest(new { error = "Invalid user or page offset." });
            }
            return Results.Json(await repository.ReadAsync(userId, offset, cancellationToken).ConfigureAwait(false));
        });
        _ = group.MapDelete("/data", async (DateTimeOffset before, IUsageStatisticsRepository repository,
            CancellationToken cancellationToken) =>
        {
            if (before == default || before > DateTimeOffset.UtcNow)
            {
                return Results.BadRequest(new { error = "Choose a cutoff in the past." });
            }
            var deleted = await repository.DeleteBeforeAsync(before.ToUniversalTime(), cancellationToken).ConfigureAwait(false);
            return Results.Json(new { DeletedEvents = deleted });
        });
    }
}
