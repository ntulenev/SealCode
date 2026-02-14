using FluentAssertions;

using Moq;

using Microsoft.AspNetCore.SignalR;

using Models;

namespace SealCode.Tests;

public sealed class SignalRRoomNotifierTests
{
    [Fact(DisplayName = "CtorShouldThrowWhenHubIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenHubIsNull()
    {
        var action = () => new SignalRRoomNotifier(null!);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "RoomKilledAsyncShouldThrowWhenRoomIdIsEmpty")]
    [Trait("Category", "Unit")]
    public void RoomKilledAsyncShouldThrowWhenRoomIdIsEmpty()
    {
        var notifier = new SignalRRoomNotifier(new Mock<IHubContext<RoomHub>>(MockBehavior.Strict).Object);

        Func<Task> action = () => notifier.RoomKilledAsync(default, new RoomDeletionReason("cleanup"), CancellationToken.None);

        action.Should().ThrowAsync<ArgumentException>();
    }

    [Fact(DisplayName = "RoomKilledAsyncShouldThrowWhenReasonIsEmpty")]
    [Trait("Category", "Unit")]
    public void RoomKilledAsyncShouldThrowWhenReasonIsEmpty()
    {
        var notifier = new SignalRRoomNotifier(new Mock<IHubContext<RoomHub>>(MockBehavior.Strict).Object);

        Func<Task> action = () => notifier.RoomKilledAsync(RoomId.New(), default, CancellationToken.None);

        action.Should().ThrowAsync<ArgumentException>();
    }

    [Fact(DisplayName = "RoomKilledAsyncShouldNotifyGroup")]
    [Trait("Category", "Unit")]
    public async Task RoomKilledAsyncShouldNotifyGroup()
    {
        var roomId = RoomId.New();
        var reason = new RoomDeletionReason("cleanup");
        using var cts = new CancellationTokenSource();
        var proxyCalls = 0;

        var proxy = new Mock<IClientProxy>(MockBehavior.Strict);
        proxy.Setup(p => p.SendCoreAsync(
                "RoomKilled",
                It.Is<object?[]>(args => args.Length == 1 && string.Equals(args[0] as string, reason.Value, StringComparison.Ordinal)),
                It.Is<CancellationToken>(token => token == cts.Token)))
            .Callback(() => proxyCalls++)
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubClients>(MockBehavior.Strict);
        clients.Setup(c => c.Group(It.Is<string>(group => group == roomId.Value)))
            .Returns(proxy.Object);

        var hub = new Mock<IHubContext<RoomHub>>(MockBehavior.Strict);
        hub.SetupGet(h => h.Clients).Returns(clients.Object);

        var notifier = new SignalRRoomNotifier(hub.Object);

        await notifier.RoomKilledAsync(roomId, reason, cts.Token);

        proxyCalls.Should().Be(1);
    }
}
