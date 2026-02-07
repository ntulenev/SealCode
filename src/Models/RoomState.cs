namespace Models;

/// <summary>
/// Represents the in-memory state of a room.
/// </summary>
public sealed class RoomState
{
    /// <summary>
    /// Gets or initializes the room identifier.
    /// </summary>
    public RoomId RoomId { get; set; }

    /// <summary>
    /// Gets or sets the room name.
    /// </summary>
    public RoomName Name { get; set; }

    /// <summary>
    /// Gets or sets the room language.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the language is invalid.</exception>
    public RoomLanguage Language { get; set; }

    /// <summary>
    /// Gets or sets the room text.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when the value is null.</exception>
    public RoomText Text { get; set; }

    /// <summary>
    /// Gets or sets the room version.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the version is less than 1.</exception>
    public RoomVersion Version { get; set; }

    /// <summary>
    /// Gets or sets the last updated timestamp in UTC.
    /// </summary>
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets the connected users keyed by connection id.
    /// </summary>
    public Dictionary<string, DisplayName> ConnectedUsers { get; } = new(StringComparer.Ordinal);
}
