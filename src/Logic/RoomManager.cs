using Abstractions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Models;
using Models.Configuration;
using Models.Exceptions;

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
    /// <param name="settings">The application settings.</param>
    /// <param name="notifier">Room closure transport.</param>
    /// <param name="logger">Operational diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
    public RoomManager(
        IRoomRegistry registry,
        IOptions<ApplicationConfiguration> settings,
        IRoomNotifier notifier,
        ILogger<RoomManager> logger)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(logger);

        _registry = registry;
        _notifier = notifier;
        _logger = logger;
        _maxUsersPerRoom = settings.Value.MaxUsersPerRoom;
    }

    /// <inheritdoc />
    public bool TryGetRoom(RoomId roomId, out RoomState room)
        => _registry.TryGetRoom(roomId, out room);

    /// <inheritdoc />
    public RoomState RegisterUserInRoom(
        RoomId roomId,
        ConnectionId connectionId,
        RoomUser roomUser)
    {
        if (!_registry.TryGetRoom(roomId, out var room))
        {
            throw new RoomNotFoundException();
        }

        room.AddUser(connectionId, roomUser, _maxUsersPerRoom);
        return room;
    }

    /// <inheritdoc />
    public RoomView[] GetRoomsSnapshot(AdminUser adminUser)
        => [.. _registry.GetRoomsSnapshot()
            .Select(room => RoomView.From(room, adminUser))
            .OrderBy(room => room.Name.Value, StringComparer.OrdinalIgnoreCase)];

    /// <inheritdoc />
    public RoomState CreateRoom(
        RoomName name,
        RoomLanguage language,
        AdminUser adminUser)
        => _registry.CreateRoom(name, language, adminUser);

    /// <inheritdoc />
    public async Task<RoomDeletionResult> DeleteRoomAsync(
        RoomId roomId,
        AdminUser adminUser,
        CancellationToken cancellationToken)
    {
        if (!_registry.TryGetRoom(roomId, out var room))
        {
            return RoomDeletionResult.NotFound;
        }

        if (!room.CanDelete(adminUser))
        {
            return RoomDeletionResult.Forbidden;
        }

        var reason = new RoomDeletionReason("Room deleted by admin");
        var deleted = await _registry.DeleteRoomAsync(
            roomId,
            reason,
            cancellationToken).ConfigureAwait(false);
        if (deleted)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await _notifier.RoomKilledAsync(roomId, reason, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogWarning(ex, "Room closure notification failed for {RoomId}", roomId.Value);
            }
        }
        return deleted ? RoomDeletionResult.Deleted : RoomDeletionResult.NotFound;
    }

    private readonly IRoomNotifier _notifier;
    private readonly ILogger<RoomManager> _logger;
    private readonly IRoomRegistry _registry;
    private readonly int _maxUsersPerRoom;
}

