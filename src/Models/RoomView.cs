namespace Models;

/// <summary>
/// Represents a lightweight view of a room for admin listings.
/// </summary>
public sealed record RoomView(
    RoomId RoomId,
    RoomName Name,
    RoomLanguage Language,
    int UsersCount,
    DateTimeOffset LastUpdatedUtc,
    AdminUser CreatedBy,
    bool CanDelete)
{
    /// <summary>
    /// Creates a room view for the specified room and admin user.
    /// </summary>
    /// <param name="room">The source room state.</param>
    /// <param name="adminUser">The admin user requesting the view.</param>
    /// <returns>The resulting room view.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="room"/> is null.</exception>
    public static RoomView From(RoomState room, AdminUser adminUser)
    {
        ArgumentNullException.ThrowIfNull(room);

        return new RoomView(
            room.RoomId,
            room.Name,
            room.Language,
            room.ConnectedUserCount,
            room.LastUpdatedUtc,
            room.CreatedBy,
            room.CanDelete(adminUser));
    }
}
