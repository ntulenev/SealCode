using FluentAssertions;

namespace Models.Tests;

public sealed class RoomViewTests
{
    [Fact(DisplayName = "CtorShouldSetValues")]
    [Trait("Category", "Unit")]
    public void CtorShouldSetValues()
    {
        var roomId = new RoomId("room-1");
        var name = new RoomName("Room");
        var language = new RoomLanguage("csharp");
        var createdBy = new AdminUser("Admin");
        var updatedUtc = new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero);

        var view = new RoomView(roomId, name, language, 3, updatedUtc, createdBy, true);

        view.RoomId.Should().Be(roomId);
        view.Name.Should().Be(name);
        view.Language.Should().Be(language);
        view.UsersCount.Should().Be(3);
        view.LastUpdatedUtc.Should().Be(updatedUtc);
        view.CreatedBy.Should().Be(createdBy);
        view.CanDelete.Should().BeTrue();
    }

    [Fact(DisplayName = "FromShouldMapRoomAndAdminUser")]
    [Trait("Category", "Unit")]
    public void FromShouldMapRoomAndAdminUser()
    {
        var room = new RoomState(
            new RoomId("room-1"),
            new RoomName("Room"),
            new RoomLanguage("csharp"),
            new RoomText("code"),
            new RoomVersion(1),
            new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero),
            new AdminUser("Owner"));
        room.AddUser(new ConnectionId("conn-1"), new DisplayName("Alice"), 5);
        var adminUser = new AdminUser("Admin");

        var view = RoomView.From(room, adminUser);

        view.RoomId.Should().Be(room.RoomId);
        view.Name.Should().Be(room.Name);
        view.Language.Should().Be(room.Language);
        view.UsersCount.Should().Be(1);
        view.LastUpdatedUtc.Should().Be(room.LastUpdatedUtc);
        view.CreatedBy.Should().Be(room.CreatedBy);
        view.CanDelete.Should().BeFalse();
    }
}
