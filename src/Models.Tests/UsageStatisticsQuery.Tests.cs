namespace Models.Tests;

public sealed class UsageStatisticsQueryTests
{
    [Fact]
    public void DefaultsUseSevenDaysAndNormalizeDatesToUtc()
    {
        var defaults = new UsageStatisticsQuery();
        defaults.Validate();
        Assert.Equal(TimeSpan.FromDays(7), defaults.To - defaults.From);
        Assert.Equal(50, defaults.Limit);
        var end = new DateTimeOffset(2020, 1, 31, 12, 0, 0, TimeSpan.FromHours(3));
        var query = new UsageStatisticsQuery(end.AddDays(-31), end) { Limit = 100, Offset = 10_000 };
        query.Validate();
        Assert.Equal(TimeSpan.Zero, query.From.Offset);
        Assert.Equal(end.UtcDateTime, query.To.UtcDateTime);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(32)]
    public void InvalidPeriodsAreRejected(int days)
    {
        var end = new DateTimeOffset(2020, 2, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Throws<ArgumentException>(() => new UsageStatisticsQuery(end.AddDays(-days), end).Validate());
        Assert.Throws<ArgumentException>(() => new UsageStatisticsQuery(to: DateTimeOffset.UtcNow.AddDays(2)).Validate());
    }

    [Theory]
    [InlineData(-1, 50)]
    [InlineData(10001, 50)]
    [InlineData(0, 0)]
    [InlineData(0, 101)]
    public void PaginationCannotExceedLimits(int offset, int limit)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new UsageStatisticsQuery(offset: offset) { Limit = limit }.Validate());

    [Theory]
    [InlineData("event")]
    [InlineData("kind")]
    [InlineData("provider")]
    [InlineData("sort")]
    [InlineData("breakdown")]
    [InlineData("user")]
    [InlineData("search")]
    [InlineData("language")]
    public void UnsupportedOrOversizedFiltersAreRejected(string field)
    {
        var query = new UsageStatisticsQuery();
        query = field switch
        {
            "event" => query with { EventType = "delete" },
            "kind" => query with { Kind = "invalid" },
            "provider" => query with { Provider = "unknown" },
            "sort" => query with { SortBy = "activity; DROP TABLE statistics.events" },
            "breakdown" => query with { BreakdownSortBy = "unknown" },
            "user" => query with { UserId = new string('x', 513) },
            "search" => query with { Search = new string('x', 161) },
            _ => query with { Language = new string('x', 33) }
        };
        Assert.Throws<ArgumentException>(query.Validate);
    }
}
