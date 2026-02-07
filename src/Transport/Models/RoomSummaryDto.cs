namespace Transport.Models;

/// <summary>
/// Represents a summary of a room for listings.
/// </summary>
public sealed class RoomSummaryDto
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoomSummaryDto"/> class.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <param name="name">The room name.</param>
    /// <param name="language">The room language.</param>
    /// <param name="usersCount">The number of connected users.</param>
    /// <param name="lastUpdatedUtc">The last updated timestamp.</param>
    public RoomSummaryDto(string roomId, string name, string language, int usersCount, DateTimeOffset lastUpdatedUtc)
    {

        RoomId = roomId;
        Name = name;
        Language = language;
        UsersCount = usersCount;
        LastUpdatedUtc = lastUpdatedUtc;
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
}
