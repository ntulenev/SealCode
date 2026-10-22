using Microsoft.AspNetCore.Http;

using SealCode.Hosting;

namespace SealCode.Tests;

public sealed class WebSocketOriginGuardTests
{
    [Theory]
    [InlineData("https://example.com", "EXAMPLE.com", "https", 200)]
    [InlineData("https://example.com", "example.com:443", "https", 200)]
    [InlineData("https://other.com", "example.com", "https", 403)]
    [InlineData("https://example.com:444", "example.com", "https", 403)]
    [InlineData("http://example.com", "example.com", "https", 403)]
    [InlineData("invalid", "example.com", "https", 403)]
    [InlineData(null, "example.com", "https", 200)]
    public async Task EnforcesEffectiveOrigin(string? origin, string host, string scheme, int expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/roomHub";
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString(host);
        context.Request.Headers.Upgrade = "websocket";
        if (origin is not null)
        {
            context.Request.Headers.Origin = origin;
        }
        var middleware = new WebSocketOriginGuardMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context);
        Assert.Equal(expected, context.Response.StatusCode);
        context.Request.Headers.Upgrade = "other";
        context.Response.StatusCode = 200;
        await middleware.InvokeAsync(context);
        Assert.Equal(200, context.Response.StatusCode);
    }
}
