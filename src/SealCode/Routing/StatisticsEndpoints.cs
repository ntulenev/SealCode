using Abstractions;

using SealCode.Security;

namespace SealCode.Routing;

/// <summary>Super-administrator-only usage reports and retention controls.</summary>
internal static class StatisticsEndpoints
{
    /// <summary>Maps protected reports and retention operations.</summary>
    /// <param name="app">The application receiving statistics endpoints.</param>
    /// <exception cref="ArgumentNullException">The application is null.</exception>
    public static void MapStatisticsEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/admin/statistics").AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            var admins = context.HttpContext.RequestServices.GetRequiredService<IAdminUserManager>();
            if (!admins.TryGetAdminUser(out var user))
            {
                return Results.Unauthorized();
            }
            if (!user.IsSuperAdmin)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            var statistics = context.HttpContext.RequestServices.GetRequiredService<IUsageStatisticsRepository>();
            return statistics.Enabled
                ? await next(context).ConfigureAwait(false)
                : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Statistics are disabled");
        });
        _ = group.MapGet("", (IWebHostEnvironment environment) =>
            Results.File(Path.Combine(environment.ContentRootPath, "Resources", "Statistics", "index.html"), "text/html"));
        _ = group.MapGet("/data", async ([AsParameters] StatisticsRequest request,
            IUsageStatisticsRepository repository, CancellationToken cancellationToken)
            => Results.Json(await repository.ReadAsync(request.ToQuery(), cancellationToken).ConfigureAwait(false)));
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
