namespace Models;

/// <summary>
/// Represents the in-memory state of a room.
/// </summary>
public sealed class RoomState
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoomState"/> class.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <param name="name">The room name.</param>
    /// <param name="language">The room language.</param>
    /// <param name="text">The room text.</param>
    /// <param name="version">The room version.</param>
    /// <param name="lastUpdatedUtc">The last updated timestamp in UTC.</param>
    public RoomState(
        RoomId roomId,
        RoomName name,
        RoomLanguage language,
        RoomText text,
        RoomVersion version,
        DateTimeOffset lastUpdatedUtc)
    {
        RoomId = roomId;
        Name = name;
        Language = language;
        Text = text;
        Version = version;
        LastUpdatedUtc = lastUpdatedUtc;
    }

    /// <summary>
    /// Gets the room identifier.
    /// </summary>
    public RoomId RoomId { get; }

    /// <summary>
    /// Gets the room name.
    /// </summary>
    public RoomName Name { get; }

    /// <summary>
    /// Gets the room language.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the language is invalid.</exception>
    public RoomLanguage Language { get; private set; }

    /// <summary>
    /// Gets the room text.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when the value is null.</exception>
    public RoomText Text { get; private set; }

    /// <summary>
    /// Gets the room version.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the version is less than 1.</exception>
    public RoomVersion Version { get; private set; }

    /// <summary>
    /// Gets the last updated timestamp in UTC.
    /// </summary>
    public DateTimeOffset LastUpdatedUtc { get; private set; }

    /// <summary>
    /// Gets the connected users keyed by connection id.
    /// </summary>
    public IReadOnlyDictionary<ConnectionId, DisplayName> ConnectedUsers => _connectedUsers;

    /// <summary>
    /// Gets the number of connected users.
    /// </summary>
    public int ConnectedUserCount => _connectedUsers.Count;

    /// <summary>
    /// Adds or updates a connected user.
    /// </summary>
    /// <param name="connectionId">The connection identifier.</param>
    /// <param name="displayName">The display name.</param>
    public void AddOrUpdateUser(ConnectionId connectionId, DisplayName displayName) => _connectedUsers[connectionId] = displayName;

    /// <summary>
    /// Tries to get a connected user's display name.
    /// </summary>
    /// <param name="connectionId">The connection identifier.</param>
    /// <param name="displayName">The display name.</param>
    /// <returns>True when found; otherwise false.</returns>
    public bool TryGetDisplayName(ConnectionId connectionId, out DisplayName displayName) => _connectedUsers.TryGetValue(connectionId, out displayName);

    /// <summary>
    /// Checks if a user is already connected.
    /// </summary>
    /// <param name="connectionId">The connection identifier.</param>
    /// <returns>True when connected; otherwise false.</returns>
    public bool HasUser(ConnectionId connectionId) => _connectedUsers.ContainsKey(connectionId);

    /// <summary>
    /// Removes a connected user.
    /// </summary>
    /// <param name="connectionId">The connection identifier.</param>
    /// <param name="displayName">The removed display name.</param>
    /// <returns>True when removed; otherwise false.</returns>
    public bool RemoveUser(ConnectionId connectionId, out DisplayName displayName) => _connectedUsers.Remove(connectionId, out displayName);

    /// <summary>
    /// Updates the room text and bumps the version.
    /// </summary>
    /// <param name="text">The new text.</param>
    /// <param name="updatedUtc">The update timestamp in UTC.</param>
    /// <returns>The new room version.</returns>
    public RoomVersion UpdateText(RoomText text, DateTimeOffset updatedUtc)
    {
        Text = text;
        Version = Version.Next();
        LastUpdatedUtc = updatedUtc;
        return Version;
    }

    /// <summary>
    /// Updates the room language and bumps the version.
    /// </summary>
    /// <param name="language">The new language.</param>
    /// <param name="updatedUtc">The update timestamp in UTC.</param>
    /// <returns>The new room version.</returns>
    public RoomVersion UpdateLanguage(RoomLanguage language, DateTimeOffset updatedUtc)
    {
        Language = language;
        Version = Version.Next();
        LastUpdatedUtc = updatedUtc;
        return Version;
    }

    private readonly Dictionary<ConnectionId, DisplayName> _connectedUsers = [];
}
