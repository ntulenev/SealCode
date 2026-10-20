using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Antiforgery;

namespace SealCode.Security;

/// <summary>
/// Protects admin HTTP mutations with bounded login limits and antiforgery validation.
/// </summary>
internal sealed class AdminHttpSecurity : IDisposable
{

    /// <summary>
    /// Validates an admin mutation before passing the request to the next handler.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="next">The next request handler.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public static async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var request = context.Request;
        if (request.Path.StartsWithSegments("/admin", StringComparison.OrdinalIgnoreCase)
            && (HttpMethods.IsPost(request.Method) || HttpMethods.IsDelete(request.Method)))
        {
            try
            {
                await context.RequestServices.GetRequiredService<IAntiforgery>()
                    .ValidateRequestAsync(context)
                    .ConfigureAwait(false);
            }
            catch (AntiforgeryValidationException) { context.Response.StatusCode = 400; return; }
        }
        await next(context).ConfigureAwait(false);
    }

    /// <summary>Applies login limits to every route spelling matched by the endpoint.</summary>
    /// <param name="invocation">The endpoint invocation.</param>
    /// <param name="next">The next endpoint filter.</param>
    /// <returns>The endpoint result or a limit rejection.</returns>
    public async ValueTask<object?> FilterLoginAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var context = invocation.HttpContext;
        var request = context.Request;
        var source = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        using var sourceLease = _sources[(uint)StringComparer.Ordinal.GetHashCode(source) % 256]
            .AttemptAcquire();
        if (!sourceLease.IsAcquired)
        {
            return Results.StatusCode(429);
        }
        if (!request.HasFormContentType)
        {
            return Results.BadRequest();
        }
        var form = await request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
        var name = form["name"].ToString();
        if (name.Length > 64 || form["password"].ToString().Length > 1024)
        {
            return Results.BadRequest();
        }
        using var nameLease = _names[(uint)StringComparer.OrdinalIgnoreCase.GetHashCode(name.Trim()) % 256]
            .AttemptAcquire();
        if (!nameLease.IsAcquired)
        {
            return Results.StatusCode(429);
        }
        return await next(invocation).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var limiter in _sources.Concat(_names))
        {
            limiter.Dispose();
        }
    }
    private static FixedWindowRateLimiter[] CreateLimiters(int permits)
        => [.. Enumerable.Range(0, 256).Select(_ => new FixedWindowRateLimiter(new()
    {
        PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
    }))];
    // Hash buckets bound limiter state for arbitrary names/addresses.
    private readonly FixedWindowRateLimiter[] _sources = CreateLimiters(30);
    private readonly FixedWindowRateLimiter[] _names = CreateLimiters(10);
}
