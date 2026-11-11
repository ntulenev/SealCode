namespace Models;

/// <summary>A bounded UTC usage report shared by HTTP, MCP and persistence.</summary>
public sealed record UsageStatisticsQuery
{
    /// <summary>Maximum number of days included in one report.</summary>
    private const int MAX_DAYS = 31;

    /// <summary>Creates a query, defaulting to the last seven days.</summary>
    /// <param name="from">Inclusive start; defaults to seven days before the end.</param>
    /// <param name="to">Exclusive end; defaults to the current UTC time.</param>
    /// <param name="userId">Optional exact stored account identifier.</param>
    /// <param name="offset">Offset into a bounded page of users or events.</param>
    public UsageStatisticsQuery(DateTimeOffset? from = null, DateTimeOffset? to = null,
        string? userId = null, int offset = 0)
    {
        To = (to ?? DateTimeOffset.UtcNow).ToUniversalTime();
        From = (from ?? To.AddDays(-7)).ToUniversalTime();
        UserId = userId;
        Offset = offset;
    }

    /// <summary>Inclusive UTC lower bound.</summary>
    public DateTimeOffset From { get; init; }
    /// <summary>Exclusive UTC upper bound.</summary>
    public DateTimeOffset To { get; init; }
    /// <summary>Exact account identifier, or null for all users.</summary>
    public string? UserId { get; init; }
    /// <summary>Case-insensitive literal search in account identifiers, names and room names.</summary>
    public string? Search { get; init; }
    /// <summary>Optional login or room event filter.</summary>
    public string? EventType { get; init; }
    /// <summary>Optional code or whiteboard workspace filter.</summary>
    public string? Kind { get; init; }
    /// <summary>Optional exact initial editor language.</summary>
    public string? Language { get; init; }
    /// <summary>Optional password, google or mcp authentication method.</summary>
    public string? Provider { get; init; }
    /// <summary>Main table sort key; unknown keys are rejected.</summary>
    public string SortBy { get; init; } = "activity";
    /// <summary>Whether the main table is sorted descending.</summary>
    public bool Descending { get; init; } = true;
    /// <summary>Workspace breakdown sort key: count, kind or language.</summary>
    public string BreakdownSortBy { get; init; } = "count";
    /// <summary>Whether the workspace breakdown is sorted descending.</summary>
    public bool BreakdownDescending { get; init; } = true;
    /// <summary>Page offset, limited to 10,000 rows.</summary>
    public int Offset { get; init; }
    /// <summary>Page size, between 1 and 100 rows.</summary>
    public int Limit { get; init; } = 50;

    /// <summary>Validates date, filter, sorting and pagination limits before any database access.</summary>
    /// <exception cref="ArgumentException">The query is invalid or exceeds a report limit.</exception>
    public void Validate()
    {
        if (From >= To || To - From > TimeSpan.FromDays(MAX_DAYS)
            || To > DateTimeOffset.UtcNow.AddDays(1))
        {
            throw new ArgumentException("Choose a nonempty UTC period of at most 31 days, ending no later than tomorrow.");
        }
        if (Offset is < 0 or > 10_000 || Limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(Offset), "Offset must be 0–10000 and limit 1–100.");
        }
        if (UserId?.Length > 512 || Search?.Length > 160 || Language?.Length > 32
            || EventType is not (null or "login" or "room")
            || Kind is not (null or "code" or "whiteboard")
            || Provider is not (null or "password" or "google" or "mcp")
            || SortBy is not ("activity" or "user" or "logins" or "rooms" or "firstActivity"
                or "time" or "type" or "provider" or "room" or "kind" or "language")
            || BreakdownSortBy is not ("count" or "kind" or "language"))
        {
            throw new ArgumentException("Invalid statistics filter or sort key.");
        }
    }
}
