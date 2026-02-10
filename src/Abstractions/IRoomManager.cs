using Models;

namespace Abstractions;

/// <summary>
/// Defines higher-level room management operations.
/// </summary>
public interface IRoomManager
{
    /// <summary>
    /// Gets room snapshots for the specified admin user.
    /// </summary>
    /// <param name="adminUser">The current admin user.</param>
    /// <returns>The room views.</returns>
    RoomView[] GetRoomsSnapshot(AdminUser adminUser);

    /// <summary>
    /// Creates a new room with the specified name and language.
    /// </summary>
    /// <param name="name">The room name.</param>
    /// <param name="language">The room language.</param>
    /// <param name="adminUser">The admin creating the room.</param>
    /// <returns>The created room state.</returns>
    RoomState CreateRoom(RoomName name, RoomLanguage language, AdminUser adminUser);

    /// <summary>
    /// Deletes a room with admin authorization checks.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <param name="adminUser">The current admin user.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The deletion result.</returns>
    Task<RoomDeletionResult> DeleteRoomAsync(RoomId roomId, AdminUser adminUser, CancellationToken cancellationToken);
}
