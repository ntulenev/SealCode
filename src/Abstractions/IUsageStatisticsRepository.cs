using System.Text.Json;

using Models;

namespace Abstractions;

/// <summary>Durable administrator activity, without authentication secrets or room content.</summary>
public interface IUsageStatisticsRepository
{
    /// <summary>Upserts an account and records a successful authentication.</summary>
    Task RecordLoginAsync(AdminUser user, string provider, CancellationToken cancellationToken);

    /// <summary>Upserts the creator and records a newly created room.</summary>
    Task RecordRoomAsync(RoomState room, CancellationToken cancellationToken);

    /// <summary>Reads totals, workspace breakdown and a page of accounts or account events.</summary>
    Task<JsonElement> ReadAsync(string? userId, int offset, CancellationToken cancellationToken);

    /// <summary>Deletes events strictly before the UTC cutoff and removes orphaned accounts.</summary>
    Task<int> DeleteBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken);
}
