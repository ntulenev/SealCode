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
}
