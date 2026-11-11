using Abstractions;

using Models;

namespace SealCode.Hosting;

/// <summary>Creates rooms through either transport with durable usage accounting.</summary>
internal sealed class TrackedRoomCreator(IRoomManager rooms, IRoomRegistry registry, IUsageStatisticsRepository statistics)
{
    /// <summary>Records creation before returning the room to its creator.</summary>
    public async Task<RoomState> CreateAsync(RoomName name, RoomLanguage language, AdminUser user,
        RoomKind kind, CancellationToken cancellationToken)
    {
        var room = rooms.CreateRoom(name, language, user, kind);
        try
        {
            await statistics.RecordRoomAsync(room, cancellationToken).ConfigureAwait(false);
            return room;
        }
        catch
        {
            _ = await registry.DeleteRoomAsync(room.RoomId, new RoomDeletionReason("Usage recording failed"), CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }
}
