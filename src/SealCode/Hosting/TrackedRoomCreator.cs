using Abstractions;

using Models;

namespace SealCode.Hosting;

/// <summary>Creates rooms through either transport with durable usage accounting.</summary>
internal sealed class TrackedRoomCreator
{
    /// <summary>Captures the shared room manager, registry and statistics repository.</summary>
    /// <param name="rooms">The room manager used by both transports.</param>
    /// <param name="registry">The registry used to roll back unrecorded rooms.</param>
    /// <param name="statistics">The durable activity repository.</param>
    /// <exception cref="ArgumentNullException">A required dependency is null.</exception>
    public TrackedRoomCreator(IRoomManager rooms, IRoomRegistry registry, IUsageStatisticsRepository statistics)
    {
        ArgumentNullException.ThrowIfNull(rooms);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(statistics);
        _rooms = rooms;
        _registry = registry;
        _statistics = statistics;
    }

    /// <summary>Records creation before returning the room to its creator.</summary>
    /// <param name="name">The requested room name.</param>
    /// <param name="language">The initial editor language.</param>
    /// <param name="user">The authenticated creator.</param>
    /// <param name="kind">The code or whiteboard workspace kind.</param>
    /// <param name="cancellationToken">Cancels persistence before commit, but never rollback.</param>
    /// <returns>The created room after its usage event has been committed.</returns>
    public async Task<RoomState> CreateAsync(RoomName name, RoomLanguage language, AdminUser user,
        RoomKind kind, CancellationToken cancellationToken)
    {
        var room = _rooms.CreateRoom(name, language, user, kind);
        try
        {
            await _statistics.RecordRoomAsync(room, cancellationToken).ConfigureAwait(false);
            return room;
        }
        catch
        {
            _ = await _registry.DeleteRoomAsync(room.RoomId, new RoomDeletionReason("Usage recording failed"), CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private readonly IRoomManager _rooms;
    private readonly IRoomRegistry _registry;
    private readonly IUsageStatisticsRepository _statistics;
}
