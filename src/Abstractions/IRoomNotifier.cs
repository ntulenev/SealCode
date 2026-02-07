using Models;

namespace Abstractions;

/// <summary>
/// Notifies clients about room events.
/// </summary>
public interface IRoomNotifier
{
    /// <summary>
    /// Notifies that a room was killed.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <param name="reason">The deletion reason.</param>
    Task RoomKilledAsync(RoomId roomId, RoomDeletionReason reason, CancellationToken cancellationToken);
}
