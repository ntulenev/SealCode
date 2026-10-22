using System.Net;
using System.Threading.RateLimiting;

using Abstractions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

using Models.Configuration;

using Moq;

using SealCode.Security;

namespace SealCode.Tests;

public sealed class RateLimiterFactoryTests
{
    [Fact]
    public async Task SourceLoginBudgetIsSharedAcrossNamesAndManuallyReplenishes()
    {
        var limiters = new List<FixedWindowRateLimiter>();
        using var security = new AdminHttpSecurity((permits, _) => CreateLimiter(permits, limiters));
        for (var index = 0; index < 30; index++)
        {
            Assert.Null(await Login(security, $"Name-{index}"));
        }
        var refusal = await Login(security, "another-name");
        Assert.Equal(429, Assert.IsType<IStatusCodeHttpResult>(refusal, exactMatch: false).StatusCode);
        foreach (var limiter in limiters)
        {
            _ = limiter.TryReplenish();
        }
        Assert.Null(await Login(security, "after-reset"));
    }

    [Fact]
    public async Task OneNameBudgetDoesNotConsumeAnotherNameBudget()
    {
        using var security = new AdminHttpSecurity((permits, _) => CreateLimiter(permits, []));
        const string name = "Alice";
        var other = Enumerable.Range(0, 256).Select(index => $"Other-{index}").First(candidate =>
            (uint)StringComparer.OrdinalIgnoreCase.GetHashCode(candidate) % 256
            != (uint)StringComparer.OrdinalIgnoreCase.GetHashCode(name) % 256);
        for (var index = 0; index < 10; index++)
        {
            Assert.Null(await Login(security, name));
        }
        Assert.Equal(429, Assert.IsType<IStatusCodeHttpResult>(await Login(security, " alice "), exactMatch: false).StatusCode);
        Assert.Null(await Login(security, other));
    }

    [Fact]
    public async Task InvocationBudgetRejectsThenAcceptsAfterManualReplenishment()
    {
        var limiters = new List<FixedWindowRateLimiter>();
        using var filter = new RoomResourceFilter(Options.Create(new ApplicationConfiguration
        {
            AdminUsers = [],
            Languages = [],
            Limits = new RoomLimits { InvocationsPerSecond = 10 }
        }), limiterFactory: (permits, _) => CreateLimiter(permits, limiters));
        using var services = new ServiceCollection().BuildServiceProvider();
        var caller = new Mock<HubCallerContext>();
        caller.SetupGet(context => context.Features).Returns(new FeatureCollection());
        caller.SetupGet(context => context.Items).Returns(new Dictionary<object, object?>());
        using var hub = new RoomHub(Mock.Of<IRoomManager>(), Mock.Of<IRoomSessionService>(), NullLogger<RoomHub>.Instance);
        var lifetime = new HubLifetimeContext(caller.Object, services, hub);
        await filter.OnConnectedAsync(lifetime, _ => Task.CompletedTask);
        var invocation = new HubInvocationContext(caller.Object, services, hub,
            typeof(RoomHub).GetMethod(nameof(RoomHub.UpdateCopyAsync))!, ["room"]);
        for (var index = 0; index < 10; index++)
        {
            _ = await filter.InvokeMethodAsync(invocation, _ => ValueTask.FromResult<object?>(null));
        }
        var error = await Assert.ThrowsAsync<HubException>(async () =>
            await filter.InvokeMethodAsync(invocation, _ => ValueTask.FromResult<object?>(null)));
        Assert.StartsWith("RateLimited:", error.Message, StringComparison.Ordinal);
        _ = limiters[^1].TryReplenish();
        _ = await filter.InvokeMethodAsync(invocation, _ => ValueTask.FromResult<object?>(null));
        await filter.OnDisconnectedAsync(lifetime, null, (_, _) => Task.CompletedTask);
    }

    private static FixedWindowRateLimiter CreateLimiter(int permits, List<FixedWindowRateLimiter> limiters)
    {
        var limiter = new FixedWindowRateLimiter(new()
        {
            PermitLimit = permits,
            Window = TimeSpan.FromTicks(1),
            AutoReplenishment = false,
            QueueLimit = 0
        });
        limiters.Add(limiter);
        return limiter;
    }

    private static ValueTask<object?> Login(AdminHttpSecurity security, string name)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["name"] = name,
            ["password"] = "wrong"
        });
        return security.FilterLoginAsync(new DefaultEndpointFilterInvocationContext(context),
            _ => ValueTask.FromResult<object?>(null));
    }
}
