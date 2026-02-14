using FluentAssertions;

using Models;

using Transport.Models;

namespace Transport.Tests;

public sealed class RoomSummaryTests
{
    [Fact(DisplayName = "CtorShouldSetValues")]
    [Trait("Category", "Unit")]
    public void CtorShouldSetValues()
    {
        var timestamp = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var roomId = RoomId.New().Value;
        var dto = new RoomSummary(roomId, "Room", "sql", 2, timestamp, "admin", true);

        dto.RoomId.Should().Be(roomId);
        dto.Name.Should().Be("Room");
        dto.Language.Should().Be("sql");
        dto.UsersCount.Should().Be(2);
        dto.LastUpdatedUtc.Should().Be(timestamp);
        dto.CreatedBy.Should().Be("admin");
        dto.CanDelete.Should().BeTrue();
    }
}
