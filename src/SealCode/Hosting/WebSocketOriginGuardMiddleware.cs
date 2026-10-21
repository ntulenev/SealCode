namespace SealCode.Hosting;

/// <summary>
/// Applies the WebSocketOriginGuardMiddleware policy to a request.
/// </summary>
internal sealed class WebSocketOriginGuardMiddleware(RequestDelegate next)
{
    /// <summary>
    /// Processes the current request.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/roomHub", StringComparison.Ordinal)
            && context.Request.Headers.Upgrade.ToString().Equals("websocket", StringComparison.OrdinalIgnoreCase)
            && context.Request.Headers.TryGetValue("Origin", out var origin)
            && (!Uri.TryCreate(origin.ToString(), UriKind.Absolute, out var uri)
                || uri.GetLeftPart(UriPartial.Authority) != $"{context.Request.Scheme}://{context.Request.Host}"))
        {
            context.Response.StatusCode = 403;
            return;
        }
        await _next(context).ConfigureAwait(false);
    }

    private readonly RequestDelegate _next = next;
}
