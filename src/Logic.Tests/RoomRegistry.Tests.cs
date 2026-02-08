using Abstractions;

using FluentAssertions;

using Microsoft.Extensions.Logging;

using Moq;

using Models;

namespace Logic.Tests;

public sealed class RoomRegistryTests
{
    [Fact(DisplayName = "CtorShouldThrowWhenLoggerIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenLoggerIsNull()
    {
        var notifier = new Mock<IRoomNotifier>(MockBehavior.Strict).Object;

        var action = () => new RoomRegistry(null!, notifier);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "CtorShouldThrowWhenNotifierIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenNotifierIsNull()
    {
        var logger = new Mock<ILogger<RoomRegistry>>(MockBehavior.Strict).Object;

        var action = () => new RoomRegistry(logger, null!);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "CreateRoomShouldAddRoom")]
    [Trait("Category", "Unit")]
    public void CreateRoomShouldAddRoom()
    {
        var registry = CreateRegistry();

        var room = registry.CreateRoom(new RoomName("Room"), new RoomLanguage("csharp"), new CreatedBy("admin"));

        room.RoomId.Value.Should().NotBeNullOrWhiteSpace();
        room.Name.Value.Should().Be("Room");
        room.Language.Value.Should().Be("csharp");
        room.Text.Value.Should().Be(string.Empty);
        room.Version.Value.Should().Be(1);
        room.CreatedBy.Value.Should().Be("admin");

        registry.TryGetRoom(room.RoomId, out var stored).Should().BeTrue();
        stored.RoomId.Should().Be(room.RoomId);
    }

    [Fact(DisplayName = "GetRoomsSnapshotShouldReturnRooms")]
    [Trait("Category", "Unit")]
    public void GetRoomsSnapshotShouldReturnRooms()
    {
        var registry = CreateRegistry();

        var first = registry.CreateRoom(new RoomName("Room A"), new RoomLanguage("csharp"), new CreatedBy("admin"));
        var second = registry.CreateRoom(new RoomName("Room B"), new RoomLanguage("sql"), new CreatedBy("admin"));

        var rooms = registry.GetRoomsSnapshot().ToList();

        rooms.Should().ContainSingle(r => r.RoomId == first.RoomId);
        rooms.Should().ContainSingle(r => r.RoomId == second.RoomId);
    }

    [Fact(DisplayName = "DeleteRoomAsyncShouldReturnFalseWhenRoomIsMissing")]
    [Trait("Category", "Unit")]
    public async Task DeleteRoomAsyncShouldReturnFalseWhenRoomIsMissing()
    {
        var notifier = new Mock<IRoomNotifier>(MockBehavior.Strict);
        var registry = CreateRegistry(notifier.Object);
        var notifierCalls = 0;
        using var cts = new CancellationTokenSource();

        notifier.Setup(n => n.RoomKilledAsync(
                It.IsAny<RoomId>(),
                It.IsAny<RoomDeletionReason>(),
                It.Is<CancellationToken>(token => token == cts.Token)))
            .Callback(() => notifierCalls++)
            .Returns(Task.CompletedTask);

        var result = await registry.DeleteRoomAsync(new RoomId("missing"), new RoomDeletionReason("cleanup"), cts.Token);

        result.Should().BeFalse();
        notifierCalls.Should().Be(0);
    }

    [Fact(DisplayName = "DeleteRoomAsyncShouldRemoveRoomAndNotify")]
    [Trait("Category", "Unit")]
    public async Task DeleteRoomAsyncShouldRemoveRoomAndNotify()
    {
        var notifier = new Mock<IRoomNotifier>(MockBehavior.Strict);
        var registry = CreateRegistry(notifier.Object);
        var room = registry.CreateRoom(new RoomName("Room"), new RoomLanguage("csharp"), new CreatedBy("admin"));
        var reason = new RoomDeletionReason("cleanup");
        using var cts = new CancellationTokenSource();
        var notifierCalls = 0;

        notifier.Setup(n => n.RoomKilledAsync(
                It.Is<RoomId>(id => id == room.RoomId),
                It.Is<RoomDeletionReason>(r => r == reason),
                It.Is<CancellationToken>(token => token == cts.Token)))
            .Callback(() => notifierCalls++)
            .Returns(Task.CompletedTask);

        var result = await registry.DeleteRoomAsync(room.RoomId, reason, cts.Token);

        result.Should().BeTrue();
        notifierCalls.Should().Be(1);
        registry.TryGetRoom(room.RoomId, out _).Should().BeFalse();
    }

    [Fact(DisplayName = "DeleteRoomAsyncShouldThrowWhenCanceled")]
    [Trait("Category", "Unit")]
    public async Task DeleteRoomAsyncShouldThrowWhenCanceled()
    {
        var registry = CreateRegistry();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> action = () => registry.DeleteRoomAsync(new RoomId("room"), new RoomDeletionReason("cleanup"), cts.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private static RoomRegistry CreateRegistry(IRoomNotifier? notifier = null)
    {
        var logger = new Mock<ILogger<RoomRegistry>>(MockBehavior.Strict).Object;
        var roomNotifier = notifier ?? new Mock<IRoomNotifier>(MockBehavior.Strict).Object;

        return new RoomRegistry(logger, roomNotifier);
    }
}
