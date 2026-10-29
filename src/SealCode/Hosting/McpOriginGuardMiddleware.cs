namespace SealCode.Hosting;

/// <summary>
/// Rejects malformed and cross-origin MCP requests before authentication.
/// </summary>
internal sealed class McpOriginGuardMiddleware
{
    /// <summary>
    /// Initializes the MCP origin guard.
    /// </summary>
    /// <param name="next">The next request handler in the pipeline.</param>
    public McpOriginGuardMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Validates the origin of an MCP request, when supplied.
    /// </summary>
    /// <param name="context">The current request context.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (request.Path.StartsWithSegments("/mcp", StringComparison.OrdinalIgnoreCase)
            && request.Headers.TryGetValue("Origin", out var origins)
            && (origins.Count != 1 || !IsAllowedOrigin(origins[0], request)))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await _next(context).ConfigureAwait(false);
    }

    private static bool IsAllowedOrigin(string? value, HttpRequest request)
    {
        // Origin is a scheme/host/port tuple, not a URL with credentials or a path.
        if (!Uri.TryCreate(value, UriKind.Absolute, out var origin)
            || !string.IsNullOrEmpty(origin.UserInfo)
            || !value.StartsWith($"{origin.Scheme}://", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.AsSpan(origin.Scheme.Length + 3).IndexOfAny("/?#\\@") >= 0)
        {
            return false;
        }

        return WebSocketOriginGuardMiddleware.IsSameOrigin(origin, request);
    }

    private readonly RequestDelegate _next;
}
