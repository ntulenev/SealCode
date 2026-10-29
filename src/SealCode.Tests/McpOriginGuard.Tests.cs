using Microsoft.AspNetCore.Http;

using SealCode.Hosting;

namespace SealCode.Tests;

public sealed class McpOriginGuardTests
{
    [Theory]
    [InlineData(null, "example.com", "https", 200)]
    [InlineData("https://EXAMPLE.com", "example.com", "https", 200)]
    [InlineData("https://example.com:443", "example.com", "https", 200)]
    [InlineData("https://example.com", "example.com:443", "https", 200)]
    [InlineData("http://[::1]:80", "[::1]", "http", 200)]
    [InlineData("https://example.com/", "example.com", "https", 403)]
    [InlineData("https://example.com/path", "example.com", "https", 403)]
    [InlineData("https://example.com?query", "example.com", "https", 403)]
    [InlineData("https://example.com#fragment", "example.com", "https", 403)]
    [InlineData("https://user@example.com", "example.com", "https", 403)]
    [InlineData("https://@example.com", "example.com", "https", 403)]
    [InlineData(" https://example.com", "example.com", "https", 403)]
    [InlineData("https://example.com https://other.com", "example.com", "https", 403)]
    [InlineData("https://other.com", "example.com", "https", 403)]
    [InlineData("https://example.com:444", "example.com", "https", 403)]
    [InlineData("http://example.com", "example.com", "https", 403)]
    [InlineData("null", "example.com", "https", 403)]
    [InlineData("", "example.com", "https", 403)]
    public async Task OnlyValidSameOriginsReachTheNextHandler(string? origin, string host, string scheme, int expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/MCP/";
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString(host);
        if (origin is not null)
        {
            context.Request.Headers.Origin = origin;
        }
        var reachedNext = false;
        var middleware = new McpOriginGuardMiddleware(_ =>
        {
            reachedNext = true;
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(context);
        Assert.Equal(expected, context.Response.StatusCode);
        Assert.Equal(expected == 200, reachedNext);
    }

    [Fact]
    public async Task RepeatedOriginsAreRejectedOnlyOnMcpPaths()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/mcp";
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("example.com");
        context.Request.Headers.Origin = new Microsoft.Extensions.Primitives.StringValues(
            ["https://example.com", "https://other.com"]);
        var middleware = new McpOriginGuardMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context);
        Assert.Equal(403, context.Response.StatusCode);
        context.Request.Path = "/admin/rooms";
        context.Response.StatusCode = 200;
        await middleware.InvokeAsync(context);
        Assert.Equal(200, context.Response.StatusCode);
    }
}
