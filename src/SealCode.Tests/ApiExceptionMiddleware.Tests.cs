using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Models.Exceptions;

using SealCode.Hosting;

namespace SealCode.Tests;

public sealed class ApiExceptionMiddlewareTests
{
    [Theory]
    [InlineData("argument", 400)]
    [InlineData("json", 400)]
    [InlineData("http", 400)]
    [InlineData("limit", 409)]
    public async Task LogsSafeDetailsAndPreservesProblem(string kind, int status)
    {
        using var provider = new AuditLogProvider();
        using var logs = LoggerFactory.Create(builder => builder.AddProvider(provider));
        using var services = new ServiceCollection().AddLogging().AddProblemDetails()
            .BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = "POST";
        context.Request.Path = "/rooms/private-id";
        context.Response.Body = new MemoryStream();
        context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask,
            RoutePatternFactory.Parse("/rooms/{id}"), 0, null, "test"));
        Exception failure = kind switch
        {
            "argument" => new ArgumentException("private-message", nameof(kind)),
            "json" => new System.Text.Json.JsonException("private-message"),
            "http" => new BadHttpRequestException("private-message"),
            _ => new RoomLimitException()
        };
        var middleware = new ApiExceptionMiddleware(_ => throw failure,
            logs.CreateLogger<ApiExceptionMiddleware>());
        await middleware.InvokeAsync(context);
        Assert.Equal(status, context.Response.StatusCode);
        var entry = Assert.Single(provider.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("POST /rooms/{id}", entry.Text, StringComparison.Ordinal);
        Assert.Contains(failure.GetType().Name, entry.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("private", entry.Text, StringComparison.Ordinal);
        if (kind == "argument")
        {
            Assert.Contains("kind", entry.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task StartedResponseRethrowsOriginalException()
    {
        using var logs = LoggerFactory.Create(_ => { });
        var context = new DefaultHttpContext();
        var feature = new Moq.Mock<IHttpResponseFeature>();
        feature.SetupGet(value => value.HasStarted).Returns(true);
        context.Features.Set(feature.Object);
        var failure = new ArgumentException("private-message");
        var middleware = new ApiExceptionMiddleware(_ => throw failure,
            logs.CreateLogger<ApiExceptionMiddleware>());
        Assert.Same(failure, await Assert.ThrowsAsync<ArgumentException>(
            () => middleware.InvokeAsync(context)));
    }
}
