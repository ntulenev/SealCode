using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Antiforgery;

namespace SealCode.Security;

internal sealed class AdminHttpSecurity : IDisposable
{
    // Hash buckets bound limiter state for arbitrary names/addresses.
    private readonly FixedWindowRateLimiter[] _sources = CreateLimiters(30);
    private readonly FixedWindowRateLimiter[] _names = CreateLimiters(10);
    private static FixedWindowRateLimiter[] CreateLimiters(int permits) => [.. Enumerable.Range(0, 256).Select(_ => new FixedWindowRateLimiter(new()
    {
        PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
    }))];

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var request = context.Request;
        if (request.Path.StartsWithSegments("/admin", StringComparison.OrdinalIgnoreCase) && (HttpMethods.IsPost(request.Method) || HttpMethods.IsDelete(request.Method)))
        {
            if (request.Path == "/admin/login")
            {
                var source = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                using var sourceLease = _sources[(uint)StringComparer.Ordinal.GetHashCode(source) % 256].AttemptAcquire();
                if (!sourceLease.IsAcquired)
                { context.Response.StatusCode = 429; return; }
                if (!request.HasFormContentType)
                { context.Response.StatusCode = 400; return; }
                var form = await request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
                var name = form["name"].ToString();
                if (name.Length > 64 || form["password"].ToString().Length > 1024)
                { context.Response.StatusCode = 400; return; }
                using var nameLease = _names[(uint)StringComparer.OrdinalIgnoreCase.GetHashCode(name.Trim()) % 256].AttemptAcquire();
                if (!nameLease.IsAcquired)
                { context.Response.StatusCode = 429; return; }
            }
            try
            { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context).ConfigureAwait(false); }
            catch (AntiforgeryValidationException) { context.Response.StatusCode = 400; return; }
        }
        await next(context).ConfigureAwait(false);
    }

    public void Dispose()
    {
        foreach (var limiter in _sources.Concat(_names))
            limiter.Dispose();
    }
}
