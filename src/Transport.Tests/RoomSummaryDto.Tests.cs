using FluentAssertions;

using Transport.Models;

namespace Transport.Tests;

public sealed class RoomSummaryDtoTests
{
    [Fact(DisplayName = "CtorShouldSetValues")]
    [Trait("Category", "Unit")]
    public void CtorShouldSetValues()
    {
        var timestamp = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var dto = new RoomSummaryDto("room-1", "Room", "sql", 2, timestamp, "admin");

        dto.RoomId.Should().Be("room-1");
        dto.Name.Should().Be("Room");
        dto.Language.Should().Be("sql");
        dto.UsersCount.Should().Be(2);
        dto.LastUpdatedUtc.Should().Be(timestamp);
        dto.CreatedBy.Should().Be("admin");
    }
}
