using Models;

namespace Abstractions;

/// <summary>
/// Defines access to room lifecycle and state management.
/// </summary>
public interface IRoomRegistry
{
    /// <summary>
    /// Gets all rooms indexed by room identifier.
    /// </summary>
    IReadOnlyDictionary<RoomId, RoomState> Rooms { get; }

    /// <summary>
    /// Creates a new room with the specified name and language.
    /// </summary>
    /// <param name="name">The room name.</param>
    /// <param name="language">The room language.</param>
    /// <returns>The created room state.</returns>
    RoomState CreateRoom(RoomName name, RoomLanguage language);

    /// <summary>
    /// Tries to get a room by its identifier.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <param name="room">The room state when found.</param>
    /// <returns>True when found; otherwise false.</returns>
    bool TryGetRoom(RoomId roomId, out RoomState room);

    /// <summary>
    /// Deletes a room by identifier with a reason.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <param name="reason">The deletion reason.</param>
    /// <returns>True when removed; otherwise false.</returns>
    Task<bool> DeleteRoom(RoomId roomId, RoomDeletionReason reason);
}
