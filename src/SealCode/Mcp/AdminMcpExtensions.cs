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
        _ = app.MapMcp("/mcp")
            .RequireAuthorization(McpTokenAuthenticationHandler.SCHEME_NAME);
    }
}
