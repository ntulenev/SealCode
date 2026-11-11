using System.Text.Json;

using Abstractions;

using Models;

namespace SealCode.Tests;

internal sealed class TestUsageStatisticsRepository : IUsageStatisticsRepository
{
    public bool Enabled => true;

    public List<(AdminUser User, string Provider)> Logins { get; } = [];
    public List<RoomState> Rooms { get; } = [];
    public DateTimeOffset? Cutoff { get; private set; }
    public bool FailWrites { get; set; }
    public UsageStatisticsQuery? LastQuery { get; private set; }

    public Task RecordLoginAsync(AdminUser user, string provider, CancellationToken cancellationToken)
    {
        if (FailWrites)
        {
            throw new InvalidOperationException("Test write failure");
        }
        lock (Logins)
        {
            Logins.Add((user, provider));
        }
        return Task.CompletedTask;
    }

    public Task RecordRoomAsync(RoomState room, CancellationToken cancellationToken)
    {
        if (FailWrites)
        {
            throw new InvalidOperationException("Test write failure");
        }
        lock (Rooms)
        {
            Rooms.Add(room);
        }
        return Task.CompletedTask;
    }

    public Task<JsonElement> ReadAsync(UsageStatisticsQuery query, CancellationToken cancellationToken)
    {
        LastQuery = query;
        return Task.FromResult(JsonSerializer.SerializeToElement(new { Logins = Logins.Count, Rooms = Rooms.Count, query.UserId, query.Offset, query.From, query.To, query.Search, query.SortBy, query.Limit }));
    }

    public Task<int> DeleteBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken)
    {
        Cutoff = before;
        return Task.FromResult(0);
    }
}
