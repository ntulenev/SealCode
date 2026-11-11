using System.ComponentModel;
using System.Text.Json;

using Abstractions;

using ModelContextProtocol;
using ModelContextProtocol.Server;

using Models;

using SealCode.Security;

namespace SealCode.Mcp;

/// <summary>Exposes bounded usage reports exclusively to authenticated super administrators.</summary>
[McpServerToolType]
internal sealed class StatisticsMcpTools
{
    /// <summary>Reads a filtered usage summary and one bounded page of users or account events.</summary>
    /// <param name="statistics">The configured statistics repository.</param>
    /// <param name="admins">The current MCP identity provider.</param>
    /// <param name="from">Inclusive UTC start, defaulting to the last seven days.</param>
    /// <param name="to">Exclusive UTC end, defaulting to now.</param>
    /// <param name="userId">Exact stored account identifier, or null for a user list.</param>
    /// <param name="offset">Page offset, at most 10,000.</param>
    /// <param name="limit">Page size between one and 100.</param>
    /// <param name="search">Literal account or room name search.</param>
    /// <param name="eventType">Optional login or room filter.</param>
    /// <param name="kind">Optional code or whiteboard filter.</param>
    /// <param name="language">Optional exact initial editor language.</param>
    /// <param name="provider">Optional password, google or mcp filter.</param>
    /// <param name="sortBy">Main table sort key.</param>
    /// <param name="descending">Whether to sort descending.</param>
    /// <param name="cancellationToken">Cancels the bounded database read.</param>
    /// <returns>Filtered totals, workspace breakdown and a page of users or events.</returns>
    /// <exception cref="McpException">The caller is forbidden, statistics are disabled or the query is invalid.</exception>
    [McpServerTool(Name = "get_usage_statistics", ReadOnly = true, UseStructuredContent = true),
     Description("Super admin only. Read usage totals, room-kind/language breakdown and ONE page of users (or events for userId). UTC [from,to), maximum 31 days per call; defaults to last 7 days. Page limit 1–100 (default 50), offset 0–10000; use UsersCount/EventsCount for paging. Filters: search is literal account/room text; eventType login/room; kind code/whiteboard; provider password/google/mcp; language is the initial editor code, e.g. csharp. Use Users.Id as userId for account history. No all-history export. Database reads time out after 15 seconds. Statistics must be configured.")]
    public static async Task<JsonElement> ReadAsync(IUsageStatisticsRepository statistics, IAdminUserManager admins,
        DateTimeOffset? from = null, DateTimeOffset? to = null, string? userId = null,
        int offset = 0, int limit = 50, string? search = null, string? eventType = null,
        string? kind = null, string? language = null, string? provider = null,
        [Description("Users: activity, user, logins, rooms, firstActivity. Events: time, type, provider, room, kind, language.")] string sortBy = "activity",
        bool descending = true, CancellationToken cancellationToken = default)
    {
        if (!admins.TryGetAdminUser(out var admin) || !admin.IsSuperAdmin)
        {
            throw new McpException("Super administrator authentication required.");
        }
        if (!statistics.Enabled)
        {
            throw new McpException("Statistics are disabled. Configure ConnectionStrings:Statistics.");
        }
        var query = new UsageStatisticsQuery(from, to, userId, offset)
        {
            Limit = limit,
            Search = search,
            EventType = eventType,
            Kind = kind,
            Language = language,
            Provider = provider,
            SortBy = sortBy,
            Descending = descending
        };
        try
        {
            query.Validate();
        }
        catch (ArgumentException)
        {
            throw new McpException("Invalid statistics query. Use a nonempty UTC period of at most 31 days, limit 1–100, offset 0–10000, and documented filters/sort keys.");
        }
        return await statistics.ReadAsync(query, cancellationToken).ConfigureAwait(false);
    }
}
