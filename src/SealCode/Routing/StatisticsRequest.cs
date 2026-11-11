using Models;

namespace SealCode.Routing;

/// <summary>Optional HTTP query parameters for a bounded statistics report.</summary>
/// <param name="From">Inclusive UTC start; defaults to the last seven days.</param>
/// <param name="To">Exclusive UTC end; defaults to now.</param>
/// <param name="UserId">Exact stored account identifier.</param>
/// <param name="Offset">Offset between zero and 10,000.</param>
/// <param name="Limit">Page size between one and 100.</param>
/// <param name="Search">Literal account or room name search.</param>
/// <param name="EventType">Login or room events.</param>
/// <param name="Kind">Code or whiteboard rooms.</param>
/// <param name="Language">Exact initial editor language.</param>
/// <param name="Provider">Password, google or mcp authentications.</param>
/// <param name="SortBy">Main table sort key.</param>
/// <param name="Descending">Main table sort direction.</param>
/// <param name="BreakdownSortBy">Workspace breakdown sort key.</param>
/// <param name="BreakdownDescending">Workspace breakdown sort direction.</param>
internal sealed record StatisticsRequest(DateTimeOffset? From = null, DateTimeOffset? To = null,
    string? UserId = null, int? Offset = null, int? Limit = null, string? Search = null,
    string? EventType = null, string? Kind = null, string? Language = null, string? Provider = null,
    string? SortBy = null, bool? Descending = null, string? BreakdownSortBy = null, bool? BreakdownDescending = null)
{
    /// <summary>Builds and validates the query used by persistence.</summary>
    /// <returns>A bounded, validated report query.</returns>
    /// <exception cref="ArgumentException">A report limit or filter is invalid.</exception>
    public UsageStatisticsQuery ToQuery()
    {
        var query = new UsageStatisticsQuery(From, To, UserId, Offset ?? 0)
        {
            Limit = Limit ?? 50,
            Search = Search,
            EventType = EventType,
            Kind = Kind,
            Language = Language,
            Provider = Provider,
            SortBy = SortBy ?? "activity",
            Descending = Descending ?? true,
            BreakdownSortBy = BreakdownSortBy ?? "count",
            BreakdownDescending = BreakdownDescending ?? true
        };
        query.Validate();
        return query;
    }
}
