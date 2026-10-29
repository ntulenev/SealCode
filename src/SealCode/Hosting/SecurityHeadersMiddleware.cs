using Microsoft.Extensions.Options;

using Models.Configuration;

namespace SealCode.Hosting;

/// <summary>
/// Applies the SecurityHeadersMiddleware policy to a request.
/// </summary>
internal sealed class SecurityHeadersMiddleware
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SecurityHeadersMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next request handler in the pipeline.</param>
    /// <param name="settings">The validated application configuration.</param>
    public SecurityHeadersMiddleware(RequestDelegate next, IOptions<ApplicationConfiguration> settings)
    {
        _next = next;
        _settings = settings.Value;
    }

    /// <summary>
    /// Processes the current request.
    /// </summary>
    /// <param name="context">The current request context.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var googleLogin = !string.IsNullOrWhiteSpace(_settings.GoogleSignIn.ClientId)
            && (string.Equals(context.Request.Path.Value, "/admin/login", StringComparison.OrdinalIgnoreCase)
                || string.Equals(context.Request.Path.Value, "/admin-login.html", StringComparison.OrdinalIgnoreCase));
        context.Response.Headers.ContentSecurityPolicy = googleLogin
            ? "default-src 'self'; script-src 'self' https://accounts.google.com/gsi/client; worker-src 'self'; style-src 'self' 'unsafe-inline' https://accounts.google.com/gsi/style; font-src 'self' data:; img-src 'self' data:; connect-src 'self' https://accounts.google.com/gsi/; frame-src https://accounts.google.com/gsi/; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'"
            : "default-src 'self'; script-src 'self'; worker-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self' data:; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        await _next(context).ConfigureAwait(false);
    }

    private readonly RequestDelegate _next;
    private readonly ApplicationConfiguration _settings;
}
