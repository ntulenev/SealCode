using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
        using var hub = new RoomHub(new Mock<Abstractions.IRoomManager>().Object,
            new Mock<Abstractions.IRoomSessionService>().Object,
            new Mock<Microsoft.Extensions.Logging.ILogger<RoomHub>>().Object);
        var callers = Enumerable.Range(0, 3).Select(_ => new Mock<HubCallerContext>()).ToArray();
        foreach (var caller in callers)
        {
            caller.SetupGet(c => c.Features).Returns(new Microsoft.AspNetCore.Http.Features.FeatureCollection());
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
        using var hub = new RoomHub(new Mock<Abstractions.IRoomManager>().Object,
            new Mock<Abstractions.IRoomSessionService>().Object,
            new Mock<Microsoft.Extensions.Logging.ILogger<RoomHub>>().Object);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var caller = new Mock<HubCallerContext>();
            caller.SetupGet(c => c.Features).Returns(new Microsoft.AspNetCore.Http.Features.FeatureCollection());
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
}
