using System.Collections.Immutable;

using Models.Exceptions;

namespace Models;

/// <summary>
/// Owns participants and their revision under the room lock.
/// </summary>
internal sealed class RoomMembership
{
    /// <summary>
    /// Gets the revision of the latest membership change.
    /// </summary>
    internal long PresenceRevision { get; private set; }

    /// <summary>
    /// Gets the immutable participant snapshot keyed by connection identifier.
    /// </summary>
    internal IReadOnlyDictionary<ConnectionId, RoomUser> Users => _users;

    /// <summary>
    /// Gets the number of connected participants.
    /// </summary>
    internal int Count => _users.Count;

    /// <summary>
    /// Determines whether the connection belongs to the room.
    /// </summary>
    /// <param name="connection">The participant connection identifier.</param>
    /// <returns>True when the connection is a participant; otherwise false.</returns>
    internal bool Contains(ConnectionId connection) => _users.ContainsKey(connection);

    /// <summary>
    /// Tries to find the participant associated with a connection.
    /// </summary>
    /// <param name="connection">The participant connection identifier.</param>
    /// <param name="user">The participant name, or its default value when the connection is not found.</param>
    /// <returns>True when the participant was found; otherwise false.</returns>
    internal bool TryGet(ConnectionId connection, out RoomUser user) => _users.TryGetValue(connection, out user);

    /// <summary>
    /// Creates a participant snapshot sorted case-insensitively by display name.
    /// </summary>
    /// <returns>The participants sorted case-insensitively by display name.</returns>
    internal RoomUser[] Snapshot() => [.. _users.Values.OrderBy(user => user.Value, StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Determines whether another connection already uses the display name.
    /// </summary>
    /// <param name="connection">The participant connection identifier.</param>
    /// <param name="user">The display name to check for another connection.</param>
    /// <returns>True when another connection uses the display name; otherwise false.</returns>
    internal bool IsInUse(ConnectionId connection, RoomUser user)
        => _users.Any(entry => entry.Key != connection && string.Equals(entry.Value.Value, user.Value, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Adds a participant after capacity and display-name checks, retaining idempotent joins.
    /// </summary>
    /// <param name="connection">The participant connection identifier.</param>
    /// <param name="user">The display name to register for the connection.</param>
    /// <param name="maxUsers">The maximum number of room participants.</param>
    /// <exception cref="AddRoomUserException">Thrown when the room is full, the display name is taken, or an existing connection attempts to rename itself.</exception>
    internal void Add(ConnectionId connection, RoomUser user, int maxUsers)
    {
        if (_users.TryGetValue(connection, out var existing))
        {
            if (existing != user)
            {
                throw new AddRoomUserException("Leave the room before changing display name.");
            }
            return;
        }
        if (Count >= maxUsers)
        {
            throw new AddRoomUserException($"Room is full (max {maxUsers})");
        }
        if (IsInUse(connection, user))
        {
            throw new AddRoomUserException("Display name already in use. Choose another name.");
        }
        _users = _users.Add(connection, user);
        PresenceRevision++;
    }

    /// <summary>
    /// Removes a participant and advances the presence revision when found.
    /// </summary>
    /// <param name="connection">The participant connection identifier.</param>
    /// <param name="user">The participant name, or its default value when the connection is not found.</param>
    /// <returns>True when a participant was removed; otherwise false.</returns>
    internal bool Remove(ConnectionId connection, out RoomUser user)
    {
        if (!_users.TryGetValue(connection, out user))
        {
            return false;
        }
        _users = _users.Remove(connection);
        PresenceRevision++;
        return true;
    }

    private volatile ImmutableDictionary<ConnectionId, RoomUser> _users = [];
}
