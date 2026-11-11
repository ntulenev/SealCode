using System.Text.Json;

using Models;

namespace Abstractions;

/// <summary>Durable administrator activity, without authentication secrets or room content.</summary>
public interface IUsageStatisticsRepository
{
    /// <summary>Gets whether statistics are enabled by a dedicated database connection.</summary>
    bool Enabled { get; }

    /// <summary>Upserts an account and records a successful authentication.</summary>
    /// <param name="user">The authenticated account, excluding credentials.</param>
    /// <param name="provider">The authentication method: password, google or mcp.</param>
    /// <param name="cancellationToken">Cancels the transaction before commit.</param>
    /// <returns>A task completing after the account and event have been committed.</returns>
    Task RecordLoginAsync(AdminUser user, string provider, CancellationToken cancellationToken);

    /// <summary>Upserts the creator and records a newly created room.</summary>
    /// <param name="room">The newly created room whose metadata is recorded.</param>
    /// <param name="cancellationToken">Cancels the transaction before commit.</param>
    /// <returns>A task completing after the creator and event have been committed.</returns>
    Task RecordRoomAsync(RoomState room, CancellationToken cancellationToken);

    /// <summary>Reads totals, workspace breakdown and a page of accounts or account events.</summary>
    /// <param name="query">A UTC period of at most 31 days, filters, sorting and bounded pagination.</param>
    /// <param name="cancellationToken">Cancels the database read.</param>
    /// <returns>Totals, room breakdown, and the requested page of users or events.</returns>
    /// <exception cref="ArgumentException">The query violates a report limit.</exception>
    Task<JsonElement> ReadAsync(UsageStatisticsQuery query, CancellationToken cancellationToken);

    /// <summary>Deletes events strictly before the UTC cutoff and removes orphaned accounts.</summary>
    /// <param name="before">The exclusive cutoff, converted to UTC before deletion.</param>
    /// <param name="cancellationToken">Cancels the transaction before commit.</param>
    /// <returns>The number of deleted events, excluding deleted account records.</returns>
    Task<int> DeleteBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken);
}
