using System.Net;

using Abstractions;

using Microsoft.AspNetCore.Http;

using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using SealCode.Hub;

using Models.Configuration;

using Moq;

namespace SealCode.Tests;

public sealed class RoomResourceFilterTests
{
    [Fact]
    public async Task DisconnectReleasesConnectionCapacity()
    {
        using var filter = new RoomResourceFilter(Options.Create(new ApplicationConfiguration
        {
            AdminUsers = [],
            Languages = [],
            Limits = new RoomLimits { MaxConnections = 1 }
        }));
        using var services = new ServiceCollection().BuildServiceProvider();
        using var hub = new RoomHub(new Mock<IRoomManager>().Object,
            new Mock<IRoomSessionService>().Object,
            new Mock<ILogger<RoomHub>>().Object);
        var callers = Enumerable.Range(0, 3).Select(_ => new Mock<HubCallerContext>()).ToArray();
        foreach (var caller in callers)
        {
            caller.SetupGet(c => c.Features).Returns(new FeatureCollection());
            caller.SetupGet(c => c.Items).Returns(new Dictionary<object, object?>());
        }
        var contexts = callers.Select(c => new HubLifetimeContext(c.Object, services, hub)).ToArray();
        var accepted = 0;
        Task Next(HubLifetimeContext context)
        {
            accepted++;
            return Task.CompletedTask;
        }

        await filter.OnConnectedAsync(contexts[0], Next);
        var error = await Assert.ThrowsAsync<HubException>(() => filter.OnConnectedAsync(contexts[1], Next));
        Assert.Contains("RateLimited", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, accepted);
        callers[1].Verify(c => c.Abort(), Times.Once);
        await filter.OnDisconnectedAsync(contexts[0], null, (_, _) => Task.CompletedTask);
        await filter.OnConnectedAsync(contexts[2], Next);
        Assert.Equal(2, accepted);
        callers[2].Verify(c => c.Abort(), Times.Never);
        await filter.OnDisconnectedAsync(contexts[2], null, (_, _) => Task.CompletedTask);
    }

    [Fact]
    public async Task ConfiguredSourceLimitRejectsThirdConnection()
    {
        using var filter = new RoomResourceFilter(Options.Create(new ApplicationConfiguration
        {
            AdminUsers = [],
            Languages = [],
            Limits = new RoomLimits { NewConnectionsPerSourcePerMinute = 2 }
        }));
        using var services = new ServiceCollection().BuildServiceProvider();
        using var hub = new RoomHub(new Mock<IRoomManager>().Object,
            new Mock<IRoomSessionService>().Object,
            new Mock<ILogger<RoomHub>>().Object);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var caller = new Mock<HubCallerContext>();
            caller.SetupGet(c => c.Features).Returns(new FeatureCollection());
            caller.SetupGet(c => c.Items).Returns(new Dictionary<object, object?>());
            var context = new HubLifetimeContext(caller.Object, services, hub);
            if (attempt < 2)
            {
                await filter.OnConnectedAsync(context, _ => Task.CompletedTask);
                await filter.OnDisconnectedAsync(context, null, (_, _) => Task.CompletedTask);
            }
            else
            {
                var error = await Assert.ThrowsAsync<HubException>(() => filter.OnConnectedAsync(context, _ => Task.CompletedTask));
                Assert.Contains("RateLimited", error.Message, StringComparison.Ordinal);
                caller.Verify(c => c.Abort(), Times.Once);
            }
        }
    }
    [Fact]
    public async Task SourceCapacityIsReleasedWithoutLeakingGlobalPermits()
    {
        var clock = new FakeTimeProvider();
        using var filter = new RoomResourceFilter(Options.Create(new ApplicationConfiguration
        {
            AdminUsers = [],
            Languages = [],
            Limits = new RoomLimits { MaxConnections = 2, MaxConnectionsPerSource = 1 }
        }), clock);
        using var services = new ServiceCollection().BuildServiceProvider();
        using var hub = new RoomHub(Mock.Of<IRoomManager>(), Mock.Of<IRoomSessionService>(), Mock.Of<ILogger<RoomHub>>());
        var first = Caller("192.0.2.1");
        var same = Caller("192.0.2.1");
        var other = Caller("192.0.2.2");
        HubLifetimeContext Context(Mock<HubCallerContext> caller)
        {
            return new(caller.Object, services, hub);
        }
        await filter.OnConnectedAsync(Context(first), _ => Task.CompletedTask);
        await Assert.ThrowsAsync<HubException>(() => filter.OnConnectedAsync(Context(same), _ => Task.CompletedTask));
        await filter.OnConnectedAsync(Context(other), _ => Task.CompletedTask);
        Assert.Equal(2, filter.TrackedSourceCount);
        await filter.OnDisconnectedAsync(Context(first), null, (_, _) => Task.CompletedTask);
        Assert.Equal(1, filter.TrackedSourceCount);
        await filter.OnConnectedAsync(Context(same), _ => Task.CompletedTask);
        await filter.OnDisconnectedAsync(Context(same), null, (_, _) => Task.CompletedTask);
        await filter.OnDisconnectedAsync(Context(other), null, (_, _) => Task.CompletedTask);
        Assert.Equal(0, filter.TrackedSourceCount);
    }

    [Fact]
    public async Task DeadlineAbortsOnlyUnjoinedConnectionsAndReleaseDisposesTimer()
    {
        var clock = new FakeTimeProvider();
        using var filter = new RoomResourceFilter(Options.Create(new ApplicationConfiguration
        {
            AdminUsers = [],
            Languages = [],
            Limits = new RoomLimits { UnjoinedConnectionTimeoutSeconds = 5 }
        }), clock);
        using var services = new ServiceCollection().BuildServiceProvider();
        using var hub = new RoomHub(Mock.Of<IRoomManager>(), Mock.Of<IRoomSessionService>(), Mock.Of<ILogger<RoomHub>>());
        var unjoined = Caller("192.0.2.1");
        var joined = Caller("192.0.2.2");
        var released = Caller("192.0.2.3");
        HubLifetimeContext Context(Mock<HubCallerContext> caller)
        {
            return new(caller.Object, services, hub);
        }
        foreach (var caller in new[] { unjoined, joined, released })
        {
            await filter.OnConnectedAsync(Context(caller), _ => Task.CompletedTask);
        }
        joined.Object.SetActiveRoom("room", "Alice");
        await filter.OnDisconnectedAsync(Context(released), null, (_, _) => Task.CompletedTask);
        clock.Advance(TimeSpan.FromSeconds(5));
        unjoined.Verify(c => c.Abort(), Times.Once);
        joined.Verify(c => c.Abort(), Times.Never);
        released.Verify(c => c.Abort(), Times.Never);
        await filter.OnDisconnectedAsync(Context(unjoined), null, (_, _) => Task.CompletedTask);
        await filter.OnDisconnectedAsync(Context(joined), null, (_, _) => Task.CompletedTask);
    }

    private static Mock<HubCallerContext> Caller(string address)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(address);
        var features = new FeatureCollection();
        features.Set<IHttpContextFeature>(new HttpFeature { HttpContext = http });
        var caller = new Mock<HubCallerContext>();
        caller.SetupGet(c => c.Features).Returns(features);
        caller.SetupGet(c => c.Items).Returns(new Dictionary<object, object?>());
        return caller;
    }

    private sealed class HttpFeature : IHttpContextFeature
    {
        public HttpContext? HttpContext { get; set; }
    }

}
