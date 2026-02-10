namespace Models;

/// <summary>
/// Represents the result of an admin room deletion attempt.
/// </summary>
public enum RoomDeletionResult
{
    /// <summary>
    /// The room was deleted successfully.
    /// </summary>
    Deleted,

    /// <summary>
    /// The room could not be found.
    /// </summary>
    NotFound,

    /// <summary>
    /// The admin user is not allowed to delete the room.
    /// </summary>
    Forbidden
}
