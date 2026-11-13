using FluentAssertions;

namespace Models.Tests;

public sealed class RoomViewTests
{
    [Fact(DisplayName = "CtorShouldSetValues")]
    [Trait("Category", "Unit")]
    public void CtorShouldSetValues()
    {
        var roomId = RoomId.New();
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
            RoomId.New(),
            new RoomName("Room"),
            new RoomLanguage("csharp"),
            new RoomText("code"),
            new RoomVersion(1),
            new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero),
            new AdminUser("Owner"));
        room.AddUser(new ConnectionId("conn-1"), new RoomUser("Alice"), 5);
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
    [Fact]
    public void FromCapturesMutatedRoom()
    {
        var room = new RoomState(RoomId.New(), new RoomName("Room"),
            new RoomLanguage("csharp"), new RoomText("code"), new RoomVersion(1),
            DateTimeOffset.UnixEpoch, new AdminUser("Owner"));
        var connection = new ConnectionId("conn");
        room.AddUser(connection, new RoomUser("Alice"), 5);
        var now = DateTimeOffset.UnixEpoch.AddHours(1);
        room.Append(connection, [1], now);
        room.ChangeLanguage(connection, new RoomLanguage("sql"), now,
            new TestLanguageValidator());
        var view = RoomView.From(room, room.CreatedBy);
        view.Language.Should().Be(room.Language);
        view.LastUpdatedUtc.Should().Be(now);
        view.UsersCount.Should().Be(1);
        view.CanDelete.Should().BeTrue();
    }

    private sealed class TestLanguageValidator : ILanguageValidator
    {
        public IReadOnlyList<string> Languages => ["csharp", "sql"];
        public bool IsValid(RoomLanguage language) => true;
    }
}

