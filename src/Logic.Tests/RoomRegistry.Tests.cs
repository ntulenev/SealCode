using Abstractions;

using FluentAssertions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Models;
using Models.Configuration;
using Models.Exceptions;

using Moq;

namespace Logic.Tests;

public sealed class RoomRegistryTests
{
    [Fact(DisplayName = "CtorShouldThrowWhenLoggerIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenLoggerIsNull()
    {
        var action = () => new RoomRegistry(null!, CreateSettings());

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "CreateRoomShouldAddRoom")]
    [Trait("Category", "Unit")]
    public void CreateRoomShouldAddRoom()
    {
        var registry = CreateRegistry();

        var room = registry.CreateRoom(new RoomName("Room"), new RoomLanguage("csharp"), new AdminUser("admin"));

        room.RoomId.Value.Should().NotBeNullOrWhiteSpace();
        room.Name.Value.Should().Be("Room");
        room.Language.Value.Should().Be("csharp");
        room.Text.Value.Should().Be(string.Empty);
        room.Version.Value.Should().Be(1);
        room.CreatedBy.Name.Should().Be("admin");

        registry.TryGetRoom(room.RoomId, out var stored).Should().BeTrue();
        stored.RoomId.Should().Be(room.RoomId);
    }

    [Fact(DisplayName = "GetRoomsSnapshotShouldReturnRooms")]
    [Trait("Category", "Unit")]
    public void GetRoomsSnapshotShouldReturnRooms()
    {
        var registry = CreateRegistry();

        var first = registry.CreateRoom(new RoomName("Room A"), new RoomLanguage("csharp"), new AdminUser("admin"));
        var second = registry.CreateRoom(new RoomName("Room B"), new RoomLanguage("sql"), new AdminUser("admin"));

        var rooms = registry.GetRoomsSnapshot().ToList();

        rooms.Should().ContainSingle(r => r.RoomId == first.RoomId);
        rooms.Should().ContainSingle(r => r.RoomId == second.RoomId);
    }

    [Fact(DisplayName = "DeleteRoomAsyncShouldReturnFalseWhenRoomIsMissing")]
    [Trait("Category", "Unit")]
    public async Task DeleteRoomAsyncShouldReturnFalseWhenRoomIsMissing()
    {
        var registry = CreateRegistry();
        var missingRoomId = RoomId.New();

        using var cts = new CancellationTokenSource();

        var result = await registry.DeleteRoomAsync(missingRoomId, new RoomDeletionReason("cleanup"), cts.Token);

        result.Should().BeFalse();
    }

    [Fact(DisplayName = "DeleteRoomAsyncShouldRemoveRoom")]
    [Trait("Category", "Unit")]
    public async Task DeleteRoomAsyncShouldRemoveRoom()
    {
        var registry = CreateRegistry();
        var room = registry.CreateRoom(new RoomName("Room"), new RoomLanguage("csharp"), new AdminUser("admin"));
        var reason = new RoomDeletionReason("cleanup");
        using var cts = new CancellationTokenSource();

        var result = await registry.DeleteRoomAsync(room.RoomId, reason, cts.Token);

        result.Should().BeTrue();

        registry.TryGetRoom(room.RoomId, out _).Should().BeFalse();
    }

    [Fact(DisplayName = "DeleteRoomAsyncShouldThrowWhenCanceled")]
    [Trait("Category", "Unit")]
    public async Task DeleteRoomAsyncShouldThrowWhenCanceled()
    {
        var registry = CreateRegistry();
        var roomId = RoomId.New();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> action = () => registry.DeleteRoomAsync(roomId, new RoomDeletionReason("cleanup"), cts.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task NotificationFailureAfterCommitStillReturnsSuccessAndClosesOldReference()
    {
        var notifier = new Mock<IRoomNotifier>();
        using var request = new CancellationTokenSource();
        notifier.Setup(n => n.RoomKilledAsync(It.IsAny<RoomId>(), It.IsAny<RoomDeletionReason>(), It.IsAny<CancellationToken>()))
            .Callback(() => request.Cancel()).ThrowsAsync(new IOException("delivery failed"));
        var registry = CreateRegistry();
        var room = registry.CreateRoom(new("Room"), new("csharp"), new("admin"));
        _ = RoomStateBuilder.Participant(room);
        var manager = new RoomManager(
            registry,
            CreateSettings(),
            notifier.Object,
            new Mock<ILogger<RoomManager>>().Object);
        Assert.Equal(RoomDeletionResult.Deleted, await manager.DeleteRoomAsync(room.RoomId, new("admin"), request.Token));
        Assert.False(registry.TryGetRoom(room.RoomId, out _));
        Assert.Throws<RoomNotFoundException>(() => room.AppendAsTestParticipant([1], DateTimeOffset.UtcNow));
        Assert.Throws<RoomNotFoundException>(() => room.AddUser(new("connection"), new("Alice"), 5));
        Assert.False(await registry.DeleteRoomAsync(room.RoomId, new("closed"), CancellationToken.None));
    }

    private static RoomRegistry CreateRegistry()
    {
        var logger = new Mock<ILogger<RoomRegistry>>().Object;

        return new RoomRegistry(logger, CreateSettings());
    }

    private static IOptions<ApplicationConfiguration> CreateSettings()
        => Options.Create(new ApplicationConfiguration
        {
            AdminUsers = [],
            Languages = ["csharp"],
            MaxUsersPerRoom = 5
        });

}
