using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Models.Configuration;

using Moq;

namespace SealCode.Tests;

public sealed class RoomResourceFilterTests
{
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
