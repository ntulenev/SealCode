using ModelContextProtocol.AspNetCore;

using SealCode.Security;

namespace SealCode.Mcp;

/// <summary>
/// Registers and maps the token-protected admin MCP server.
/// </summary>
internal static class AdminMcpExtensions
{
    public static IServiceCollection AddAdminMcp(this IServiceCollection services)
    {
        _ = services.AddMcpServer()
            .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
            .WithTools<AdminMcpTools>();
        return services;
    }

    public static void MapAdminMcp(this WebApplication app)
    {
        app.MapMcp("/mcp")
            .RequireAuthorization(McpTokenAuthenticationHandler.SCHEME_NAME)
            .Add(endpoint =>
            {
                var next = endpoint.RequestDelegate!;
                endpoint.RequestDelegate = async context =>
                {
                    var request = context.Request;
                    if (request.Headers.ContainsKey("Origin")
                        && (!Uri.TryCreate(request.Headers.Origin, UriKind.Absolute, out var origin)
                            || !Uri.TryCreate($"{request.Scheme}://{request.Host}", UriKind.Absolute, out var server)
                            || Uri.Compare(origin, server, UriComponents.SchemeAndServer,
                                UriFormat.UriEscaped, StringComparison.OrdinalIgnoreCase) != 0))
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return;
                    }
                    await next(context).ConfigureAwait(false);
                };
            });
    }
}
