namespace Transport.Models;

/// <summary>
/// Represents a summary of a room for listings.
/// </summary>
public sealed class RoomSummary
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoomSummary"/> class.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <param name="name">The room name.</param>
    /// <param name="language">The room language.</param>
    /// <param name="usersCount">The number of connected users.</param>
    /// <param name="lastUpdatedUtc">The last updated timestamp.</param>
    /// <param name="createdBy">The admin that created the room.</param>
    /// <param name="canDelete">Whether the current admin can delete this room.</param>
    public RoomSummary(string roomId, string name, string language, int usersCount, DateTimeOffset lastUpdatedUtc, string createdBy, bool canDelete)
    {

        RoomId = roomId;
        Name = name;
        Language = language;
        UsersCount = usersCount;
        LastUpdatedUtc = lastUpdatedUtc;
        CreatedBy = createdBy;
        CanDelete = canDelete;
    }

    /// <summary>
    /// Gets the room identifier.
    /// </summary>
    public string RoomId { get; }

    /// <summary>
    /// Gets the room name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the room language.
    /// </summary>
    public string Language { get; }

    /// <summary>
    /// Gets the number of connected users.
    /// </summary>
    public int UsersCount { get; }

    /// <summary>
    /// Gets the last updated timestamp in UTC.
    /// </summary>
    public DateTimeOffset LastUpdatedUtc { get; }

    /// <summary>
    /// Gets the admin that created the room.
    /// </summary>
    public string CreatedBy { get; }

    /// <summary>
    /// Gets whether the current admin can delete this room.
    /// </summary>
    public bool CanDelete { get; }
}
