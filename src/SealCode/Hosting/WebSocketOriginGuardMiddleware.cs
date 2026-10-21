namespace SealCode.Hosting;

/// <summary>
/// Applies the WebSocketOriginGuardMiddleware policy to a request.
/// </summary>
internal sealed class WebSocketOriginGuardMiddleware
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebSocketOriginGuardMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next request handler in the pipeline.</param>
    public WebSocketOriginGuardMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Processes the current request.
    /// </summary>
    /// <param name="context">The current request context.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/roomHub", StringComparison.Ordinal)
            && context.Request.Headers.Upgrade.ToString().Equals("websocket", StringComparison.OrdinalIgnoreCase)
            && context.Request.Headers.TryGetValue("Origin", out var origin)
            && (!Uri.TryCreate(origin.ToString(), UriKind.Absolute, out var uri)
                || !IsSameOrigin(uri, context.Request)))
        {
            context.Response.StatusCode = 403;
            return;
        }
        await _next(context).ConfigureAwait(false);
    }

    internal static bool IsSameOrigin(Uri origin, HttpRequest request)
    {
        var port = request.Host.Port
            ?? (request.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80);
        return origin.Scheme.Equals(request.Scheme, StringComparison.OrdinalIgnoreCase)
            && origin.Host.Equals(request.Host.Host, StringComparison.OrdinalIgnoreCase)
            && origin.Port == port;
    }

    private readonly RequestDelegate _next;
}
