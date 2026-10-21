namespace SealCode.Hosting;

/// <summary>
/// Applies the SecurityHeadersMiddleware policy to a request.
/// </summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    /// <summary>
    /// Processes the current request.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; worker-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self' data:; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        await _next(context).ConfigureAwait(false);
    }

    private readonly RequestDelegate _next = next;
}
