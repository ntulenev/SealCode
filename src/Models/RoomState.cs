using System.Collections.Immutable;

using Models.Exceptions;

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
    /// <param name="createdBy">The admin that created the room.</param>
    /// <param name="yjsState">The serialized Yjs document state (full update).</param>
    public RoomState(
        RoomId roomId,
        RoomName name,
        RoomLanguage language,
        RoomText text,
        RoomVersion version,
        DateTimeOffset lastUpdatedUtc,
        AdminUser createdBy,
        byte[]? yjsState = null)
    {
        RoomId = roomId;
        Name = name;
        Language = language;
        Text = text;
        Version = version;
        LastUpdatedUtc = lastUpdatedUtc;
        CreatedBy = createdBy;
        YjsState = yjsState ?? [];
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
    /// Gets the serialized Yjs document state (full update).
    /// </summary>
    public byte[] YjsState { get; private set; }

    /// <summary>
    /// Gets the last updated timestamp in UTC.
    /// </summary>
    public DateTimeOffset LastUpdatedUtc { get; private set; }

    /// <summary>
    /// Gets the admin that created the room.
    /// </summary>
    public AdminUser CreatedBy { get; }

    /// <summary>
    /// Determines whether the provided admin user created this room.
    /// </summary>
    /// <param name="adminUser">The admin user to check.</param>
    /// <returns>True when the admin user created this room; otherwise false.</returns>
    public bool IsCreatedBy(AdminUser adminUser)
        => CreatedBy.Matches(adminUser);

    /// <summary>
    /// Determines whether the provided admin user can delete this room.
    /// </summary>
    /// <param name="adminUser">The current admin user.</param>
    /// <returns>True when the admin user can delete this room; otherwise false.</returns>
    public bool CanDelete(AdminUser adminUser)
        => adminUser.IsSuperAdmin || IsCreatedBy(adminUser);

    /// <summary>
    /// Gets the connected users keyed by connection id.
    /// </summary>
    public IReadOnlyDictionary<ConnectionId, DisplayName> ConnectedUsers => _connectedUsers;

    /// <summary>
    /// Gets the number of connected users.
    /// </summary>
    public int ConnectedUserCount => _connectedUsers.Count;

    /// <summary>
    /// Creates a sorted snapshot of connected user display names.
    /// </summary>
    /// <returns>Connected users sorted by display name (case-insensitive).</returns>
    public string[] CreateUsersSnapshot()
        => [.. _connectedUsers.Values.Select(x => x.Value).OrderBy(name => name, StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Checks if a display name is already in use by another connection.
    /// </summary>
    /// <param name="connectionId">The connection identifier to exclude.</param>
    /// <param name="displayName">The display name to check.</param>
    /// <returns>True when the name is in use by another connection; otherwise false.</returns>
    public bool IsDisplayNameInUse(ConnectionId connectionId, DisplayName displayName)
        => _connectedUsers.Any(entry => entry.Key != connectionId
            && string.Equals(entry.Value.Value, displayName.Value, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Adds or updates a connected user with room capacity and name checks.
    /// </summary>
    /// <param name="connectionId">The connection identifier.</param>
    /// <param name="displayName">The display name.</param>
    /// <param name="maxUsers">The maximum number of users allowed in the room.</param>
    /// <exception cref="AddRoomUserException">Thrown when the room is full or the name is already in use.</exception>
    public void AddUser(ConnectionId connectionId, DisplayName displayName, int maxUsers)
    {
        lock (_addGuard)
        {
            var alreadyInRoom = HasUser(connectionId);
            if (!alreadyInRoom && _connectedUsers.Count >= maxUsers)
            {
                throw new AddRoomUserException($"Room is full (max {maxUsers})");
            }

            if (IsDisplayNameInUse(connectionId, displayName))
            {
                throw new AddRoomUserException("Display name already in use. Choose another name.");
            }

            AddOrUpdateUser(connectionId, displayName);
        }
    }

    private void AddOrUpdateUser(ConnectionId connectionId, DisplayName displayName)
        => ImmutableInterlocked.AddOrUpdate(
            ref _connectedUsers,
            connectionId,
            displayName,
            static (_, value) => value);

    /// <summary>
    /// Tries to get a connected user's display name.
    /// </summary>
    /// <param name="connectionId">The connection identifier.</param>
    /// <param name="displayName">The display name.</param>
    /// <returns>True when found; otherwise false.</returns>
    public bool TryGetDisplayName(ConnectionId connectionId, out DisplayName displayName) => _connectedUsers.TryGetValue(connectionId, out displayName);

    private bool HasUser(ConnectionId connectionId) => _connectedUsers.ContainsKey(connectionId);

    /// <summary>
    /// Removes a connected user.
    /// </summary>
    /// <param name="connectionId">The connection identifier.</param>
    /// <param name="displayName">The removed display name.</param>
    /// <returns>True when removed; otherwise false.</returns>
    public bool RemoveUser(ConnectionId connectionId, out DisplayName displayName)
        => ImmutableInterlocked.TryRemove(ref _connectedUsers, connectionId, out displayName);

    /// <summary>
    /// Updates the room text and bumps the version.
    /// </summary>
    /// <param name="text">The new text.</param>
    /// <param name="updatedUtc">The update timestamp in UTC.</param>
    /// <returns>The new room version.</returns>
    public RoomVersion UpdateText(RoomText text, DateTimeOffset updatedUtc)
    {
        lock (_versionGuard)
        {
            Text = text;
            Version = Version.Next();
            LastUpdatedUtc = updatedUtc;
            return Version;
        }
    }

    /// <summary>
    /// Updates the room language and bumps the version.
    /// </summary>
    /// <param name="language">The new language.</param>
    /// <param name="updatedUtc">The update timestamp in UTC.</param>
    /// <param name="languageValidator">The language validator.</param>
    /// <returns>The new room version.</returns>
    /// <exception cref="ArgumentException">Thrown when the language is invalid.</exception>
    /// <exception cref="ArgumentNullException">Thrown when the validator is null.</exception>
    public RoomVersion UpdateLanguage(RoomLanguage language, DateTimeOffset updatedUtc, ILanguageValidator languageValidator)
    {
        ArgumentNullException.ThrowIfNull(languageValidator);

        if (!languageValidator.IsValid(language))
        {
            throw new ArgumentException("Invalid language", nameof(language));
        }

        lock (_versionGuard)
        {
            Language = language;
            Version = Version.Next();
            LastUpdatedUtc = updatedUtc;
            return Version;
        }
    }

    /// <summary>
    /// Updates the stored Yjs state and text snapshot, bumping the version.
    /// </summary>
    /// <param name="state">The serialized Yjs document state.</param>
    /// <param name="text">The plain text snapshot.</param>
    /// <param name="updatedUtc">The update timestamp in UTC.</param>
    /// <param name="version">The resulting room version.</param>
    /// <returns>True when the state changed and the version was bumped; otherwise false.</returns>
    public bool TryUpdateYjsState(byte[] state, RoomText text, DateTimeOffset updatedUtc, out RoomVersion version)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_versionGuard)
        {
            var sameState = state.AsSpan().SequenceEqual(YjsState);
            var sameText = string.Equals(text.Value, Text.Value, StringComparison.Ordinal);
            if (sameState && sameText)
            {
                version = Version;
                return false;
            }

            YjsState = state;
            Text = text;
            Version = Version.Next();
            LastUpdatedUtc = updatedUtc;
            version = Version;
            return true;
        }
    }

    private readonly Lock _addGuard = new();
    private readonly Lock _versionGuard = new();
    private ImmutableDictionary<ConnectionId, DisplayName> _connectedUsers = [];
}
