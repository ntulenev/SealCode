using Abstractions;

using FluentAssertions;

using Moq;

using Models;

namespace Logic.Tests;

public sealed class RoomManagerTests
{
    [Fact(DisplayName = "CtorShouldThrowWhenRegistryIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenRegistryIsNull()
    {
        var action = () => new RoomManager(null!);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "GetRoomsSnapshotShouldReturnOrderedViewsWithCanDelete")]
    [Trait("Category", "Unit")]
    public void GetRoomsSnapshotShouldReturnOrderedViewsWithCanDelete()
    {
        var registry = new Mock<IRoomRegistry>(MockBehavior.Strict);
        var adminRoom = CreateRoomState("room-1", "Zulu", "Admin");
        var otherRoom = CreateRoomState("room-2", "Alpha", "Root");
        registry.Setup(r => r.GetRoomsSnapshot())
            .Returns([adminRoom, otherRoom]);
        var manager = new RoomManager(registry.Object);

        var views = manager.GetRoomsSnapshot(new AdminUser("Admin"));

        views.Should().HaveCount(2);
        views[0].Name.Value.Should().Be("Alpha");
        views[0].CanDelete.Should().BeFalse();
        views[1].Name.Value.Should().Be("Zulu");
        views[1].CanDelete.Should().BeTrue();
        registry.VerifyAll();
    }

    [Fact(DisplayName = "CreateRoomShouldDelegateToRegistry")]
    [Trait("Category", "Unit")]
    public void CreateRoomShouldDelegateToRegistry()
    {
        var registry = new Mock<IRoomRegistry>(MockBehavior.Strict);
        var admin = new AdminUser("Admin");
        var name = new RoomName("Room");
        var language = new RoomLanguage("csharp");
        var created = CreateRoomState("room-1", "Room", "Admin");
        registry.Setup(r => r.CreateRoom(name, language, admin))
            .Returns(created);
        var manager = new RoomManager(registry.Object);

        var result = manager.CreateRoom(name, language, admin);

        result.Should().BeSameAs(created);
        registry.VerifyAll();
    }

    [Fact(DisplayName = "DeleteRoomAsyncShouldReturnNotFoundWhenMissing")]
    [Trait("Category", "Unit")]
    public async Task DeleteRoomAsyncShouldReturnNotFoundWhenMissing()
    {
        var registry = new Mock<IRoomRegistry>(MockBehavior.Strict);
        registry.Setup(r => r.TryGetRoom(It.IsAny<RoomId>(), out It.Ref<RoomState>.IsAny))
            .Returns(false);
        var manager = new RoomManager(registry.Object);

        var result = await manager.DeleteRoomAsync(new RoomId("missing"), new AdminUser("Admin"), CancellationToken.None);

        result.Should().Be(RoomDeletionResult.NotFound);
        registry.VerifyAll();
    }

    [Fact(DisplayName = "DeleteRoomAsyncShouldReturnForbiddenWhenNotAllowed")]
    [Trait("Category", "Unit")]
    public async Task DeleteRoomAsyncShouldReturnForbiddenWhenNotAllowed()
    {
        var registry = new Mock<IRoomRegistry>(MockBehavior.Strict);
        var room = CreateRoomState("room-1", "Room", "Root");
        registry.Setup(r => r.TryGetRoom(room.RoomId, out room))
            .Returns(true);
        var manager = new RoomManager(registry.Object);

        var result = await manager.DeleteRoomAsync(room.RoomId, new AdminUser("Admin"), CancellationToken.None);

        result.Should().Be(RoomDeletionResult.Forbidden);
        registry.VerifyAll();
        registry.Verify(r => r.DeleteRoomAsync(It.IsAny<RoomId>(), It.IsAny<RoomDeletionReason>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "DeleteRoomAsyncShouldReturnDeletedWhenSuccessful")]
    [Trait("Category", "Unit")]
    public async Task DeleteRoomAsyncShouldReturnDeletedWhenSuccessful()
    {
        var registry = new Mock<IRoomRegistry>(MockBehavior.Strict);
        var room = CreateRoomState("room-1", "Room", "Admin");
        registry.Setup(r => r.TryGetRoom(room.RoomId, out room))
            .Returns(true);
        registry.Setup(r => r.DeleteRoomAsync(
                room.RoomId,
                It.Is<RoomDeletionReason>(reason => reason.Value == "Room deleted by admin"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var manager = new RoomManager(registry.Object);

        var result = await manager.DeleteRoomAsync(room.RoomId, new AdminUser("Admin"), CancellationToken.None);

        result.Should().Be(RoomDeletionResult.Deleted);
        registry.VerifyAll();
    }

    [Fact(DisplayName = "DeleteRoomAsyncShouldReturnNotFoundWhenDeleteFails")]
    [Trait("Category", "Unit")]
    public async Task DeleteRoomAsyncShouldReturnNotFoundWhenDeleteFails()
    {
        var registry = new Mock<IRoomRegistry>(MockBehavior.Strict);
        var room = CreateRoomState("room-1", "Room", "Admin");
        registry.Setup(r => r.TryGetRoom(room.RoomId, out room))
            .Returns(true);
        registry.Setup(r => r.DeleteRoomAsync(
                room.RoomId,
                It.IsAny<RoomDeletionReason>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var manager = new RoomManager(registry.Object);

        var result = await manager.DeleteRoomAsync(room.RoomId, new AdminUser("Admin"), CancellationToken.None);

        result.Should().Be(RoomDeletionResult.NotFound);
        registry.VerifyAll();
    }

    private static RoomState CreateRoomState(string id, string name, string createdBy)
        => new(
            new RoomId(id),
            new RoomName(name),
            new RoomLanguage("csharp"),
            new RoomText(string.Empty),
            new RoomVersion(1),
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new AdminUser(createdBy));
}
