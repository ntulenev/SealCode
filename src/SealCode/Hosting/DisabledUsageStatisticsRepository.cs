using System.Text.Json;

using Abstractions;

using Models;

namespace SealCode.Hosting;

/// <summary>Leaves authentication and rooms available when statistics have no configured database.</summary>
internal sealed class DisabledUsageStatisticsRepository : IUsageStatisticsRepository
{
    /// <inheritdoc />
    public bool Enabled => false;

    /// <inheritdoc />
    public Task RecordLoginAsync(AdminUser user, string provider, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RecordRoomAsync(RoomState room, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(room);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Statistics are not configured.</exception>
    public Task<JsonElement> ReadAsync(string? userId, int offset, CancellationToken cancellationToken)
        => throw new InvalidOperationException("Statistics are disabled.");

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Statistics are not configured.</exception>
    public Task<int> DeleteBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken)
        => throw new InvalidOperationException("Statistics are disabled.");
}
