using Abstractions;

using FluentAssertions;
using Microsoft.Extensions.Options;

using Moq;

using Models;
using Models.Configuration;
using Models.Exceptions;

namespace Logic.Tests;

public sealed class RoomManagerTests
{
    [Fact(DisplayName = "CtorShouldThrowWhenRegistryIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenRegistryIsNull()
    {
        var action = () => new RoomManager(null!, CreateSettings(3));

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "CtorShouldThrowWhenSettingsIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenSettingsIsNull()
    {
        var registry = new Mock<IRoomRegistry>(MockBehavior.Strict);

        var action = () => new RoomManager(registry.Object, null!);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "RegisterUserInRoomShouldThrowWhenRoomIsMissing")]
    [Trait("Category", "Unit")]
    public void RegisterUserInRoomShouldThrowWhenRoomIsMissing()
    {
        var registry = new Mock<IRoomRegistry>(MockBehavior.Strict);
        registry.Setup(r => r.TryGetRoom(It.IsAny<RoomId>(), out It.Ref<RoomState>.IsAny))
            .Returns(false);
        var manager = new RoomManager(registry.Object, CreateSettings(3));
        var missingRoomId = RoomId.New();

        var action = () => manager.RegisterUserInRoom(
            missingRoomId,
            new ConnectionId("conn-1"),
            new RoomUser("Alice"));

        action.Should().Throw<RoomNotFoundException>();
        registry.VerifyAll();
    }

    [Fact(DisplayName = "TryGetRoomShouldDelegateToRegistry")]
    [Trait("Category", "Unit")]
    public void TryGetRoomShouldDelegateToRegistry()
    {
        var registry = new Mock<IRoomRegistry>(MockBehavior.Strict);
        var room = CreateRoomState(RoomId.New(), "Room", "Admin");
        registry.Setup(r => r.TryGetRoom(room.RoomId, out room))
            .Returns(true);
        var manager = new RoomManager(registry.Object, CreateSettings(3));

        var found = manager.TryGetRoom(room.RoomId, out var actualRoom);

        found.Should().BeTrue();
        actualRoom.Should().BeSameAs(room);
        registry.VerifyAll();
    }

    [Fact(DisplayName = "RegisterUserInRoomShouldAddUserAndReturnRoom")]
    [Trait("Category", "Unit")]
    public void RegisterUserInRoomShouldAddUserAndReturnRoom()
    {
        var registry = new Mock<IRoomRegistry>(MockBehavior.Strict);
        var room = CreateRoomState(RoomId.New(), "Room", "Admin");
        registry.Setup(r => r.TryGetRoom(room.RoomId, out room))
            .Returns(true);
        var manager = new RoomManager(registry.Object, CreateSettings(3));
        var connectionId = new ConnectionId("conn-1");
        var displayName = new RoomUser("Alice");

        var result = manager.RegisterUserInRoom(room.RoomId, connectionId, displayName);

        result.Should().BeSameAs(room);
        room.ConnectedUsers.Should().ContainKey(connectionId);
        room.ConnectedUsers[connectionId].Should().Be(displayName);
        registry.VerifyAll();
    }

    [Fact(DisplayName = "RegisterUserInRoomShouldClampMaxUsersToAtLeastOne")]
    [Trait("Category", "Unit")]
    public void RegisterUserInRoomShouldClampMaxUsersToAtLeastOne()
    {
        var registry = new Mock<IRoomRegistry>(MockBehavior.Strict);
        var room = CreateRoomState(RoomId.New(), "Room", "Admin");
        registry.Setup(r => r.TryGetRoom(room.RoomId, out room))
            .Returns(true);
        var manager = new RoomManager(registry.Object, CreateSettings(0));

        manager.RegisterUserInRoom(room.RoomId, new ConnectionId("conn-1"), new RoomUser("Alice"));

        var action = () => manager.RegisterUserInRoom(
            room.RoomId,
            new ConnectionId("conn-2"),
            new RoomUser("Bob"));

        action.Should().Throw<AddRoomUserException>()
            .WithMessage("Room is full (max 1)");
        registry.VerifyAll();
    }

    [Fact(DisplayName = "GetRoomsSnapshotShouldReturnOrderedViewsWithCanDelete")]
    [Trait("Category", "Unit")]
    public void GetRoomsSnapshotShouldReturnOrderedViewsWithCanDelete()
    {
        var registry = new Mock<IRoomRegistry>(MockBehavior.Strict);
        var adminRoom = CreateRoomState(RoomId.New(), "Zulu", "Admin");
        var otherRoom = CreateRoomState(RoomId.New(), "Alpha", "Root");
        registry.Setup(r => r.GetRoomsSnapshot())
            .Returns([adminRoom, otherRoom]);
        var manager = new RoomManager(registry.Object, CreateSettings(3));

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
        var created = CreateRoomState(RoomId.New(), "Room", "Admin");
        registry.Setup(r => r.CreateRoom(name, language, admin))
            .Returns(created);
        var manager = new RoomManager(registry.Object, CreateSettings(3));

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
        var manager = new RoomManager(registry.Object, CreateSettings(3));

        var result = await manager.DeleteRoomAsync(RoomId.New(), new AdminUser("Admin"), CancellationToken.None);

        result.Should().Be(RoomDeletionResult.NotFound);
        registry.VerifyAll();
    }

    [Fact(DisplayName = "DeleteRoomAsyncShouldReturnForbiddenWhenNotAllowed")]
    [Trait("Category", "Unit")]
    public async Task DeleteRoomAsyncShouldReturnForbiddenWhenNotAllowed()
    {
        var registry = new Mock<IRoomRegistry>(MockBehavior.Strict);
        var room = CreateRoomState(RoomId.New(), "Room", "Root");
        registry.Setup(r => r.TryGetRoom(room.RoomId, out room))
            .Returns(true);
        var manager = new RoomManager(registry.Object, CreateSettings(3));

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
        var room = CreateRoomState(RoomId.New(), "Room", "Admin");
        registry.Setup(r => r.TryGetRoom(room.RoomId, out room))
            .Returns(true);
        registry.Setup(r => r.DeleteRoomAsync(
                room.RoomId,
                It.Is<RoomDeletionReason>(reason => reason.Value == "Room deleted by admin"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var manager = new RoomManager(registry.Object, CreateSettings(3));

        var result = await manager.DeleteRoomAsync(room.RoomId, new AdminUser("Admin"), CancellationToken.None);

        result.Should().Be(RoomDeletionResult.Deleted);
        registry.VerifyAll();
    }

    [Fact(DisplayName = "DeleteRoomAsyncShouldReturnNotFoundWhenDeleteFails")]
    [Trait("Category", "Unit")]
    public async Task DeleteRoomAsyncShouldReturnNotFoundWhenDeleteFails()
    {
        var registry = new Mock<IRoomRegistry>(MockBehavior.Strict);
        var room = CreateRoomState(RoomId.New(), "Room", "Admin");
        registry.Setup(r => r.TryGetRoom(room.RoomId, out room))
            .Returns(true);
        registry.Setup(r => r.DeleteRoomAsync(
                room.RoomId,
                It.IsAny<RoomDeletionReason>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var manager = new RoomManager(registry.Object, CreateSettings(3));

        var result = await manager.DeleteRoomAsync(room.RoomId, new AdminUser("Admin"), CancellationToken.None);

        result.Should().Be(RoomDeletionResult.NotFound);
        registry.VerifyAll();
    }

    private static RoomState CreateRoomState(RoomId id, string name, string createdBy)
        => new(
            id,
            new RoomName(name),
            new RoomLanguage("csharp"),
            new RoomText(string.Empty),
            new RoomVersion(1),
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new AdminUser(createdBy));

    private static IOptions<ApplicationConfiguration> CreateSettings(int maxUsersPerRoom)
        => Options.Create(new ApplicationConfiguration
        {
            AdminUsers =
            [
                new AdminUserConfiguration
                {
                    Name = "admin",
                    Password = "admin",
                    IsSuperAdmin = true
                }
            ],
            Languages = ["csharp"],
            MaxUsersPerRoom = maxUsersPerRoom
        });
}

