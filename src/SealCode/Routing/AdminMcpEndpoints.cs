using SealCode.Security;

namespace SealCode.Routing;

/// <summary>
/// Registers the token-protected admin MCP endpoint.
/// </summary>
internal static class AdminMcpEndpoints
{
    /// <summary>
    /// Maps the admin MCP transport independently of browser authentication.
    /// </summary>
    /// <param name="app">The application to configure.</param>
    public static void MapAdminMcpEndpoints(this WebApplication app)
    {
        _ = app.MapMcp("/mcp")
            .RequireAuthorization(McpTokenAuthenticationHandler.SCHEME_NAME);
    }
}
