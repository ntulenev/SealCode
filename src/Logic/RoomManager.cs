using Abstractions;

using Models;

namespace Logic;

/// <summary>
/// Default implementation of <see cref="IRoomManager"/>.
/// </summary>
public sealed class RoomManager : IRoomManager
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoomManager"/> class.
    /// </summary>
    /// <param name="registry">The room registry.</param>
    public RoomManager(IRoomRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    /// <inheritdoc />
    public RoomView[] GetRoomsSnapshot(AdminUser adminUser)
        => [.. _registry.GetRoomsSnapshot()
            .Select(room => new RoomView(
                room.RoomId,
                room.Name,
                room.Language,
                room.ConnectedUserCount,
                room.LastUpdatedUtc,
                room.CreatedBy,
                room.CanDelete(adminUser)))
            .OrderBy(room => room.Name.Value, StringComparer.OrdinalIgnoreCase)];

    /// <inheritdoc />
    public RoomState CreateRoom(RoomName name, RoomLanguage language, AdminUser adminUser)
        => _registry.CreateRoom(name, language, adminUser);

    /// <inheritdoc />
    public async Task<RoomDeletionResult> DeleteRoomAsync(RoomId roomId, AdminUser adminUser, CancellationToken cancellationToken)
    {
        if (!_registry.TryGetRoom(roomId, out var room))
        {
            return RoomDeletionResult.NotFound;
        }

        if (!room.CanDelete(adminUser))
        {
            return RoomDeletionResult.Forbidden;
        }

        var deleted = await _registry.DeleteRoomAsync(
            roomId,
            new RoomDeletionReason("Room deleted by admin"),
            cancellationToken).ConfigureAwait(false);
        return deleted ? RoomDeletionResult.Deleted : RoomDeletionResult.NotFound;
    }

    private readonly IRoomRegistry _registry;
}
