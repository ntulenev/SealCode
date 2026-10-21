using System.Collections.Immutable;

using Models.Exceptions;

namespace Models;

/// <summary>
/// Owns participants and their revision under the room lock.
/// </summary>
internal sealed class RoomMembership
{
    internal long PresenceRevision { get; private set; }
    internal IReadOnlyDictionary<ConnectionId, RoomUser> Users => _users;
    internal int Count => _users.Count;
    internal bool Contains(ConnectionId connection) => _users.ContainsKey(connection);
    internal bool TryGet(ConnectionId connection, out RoomUser user) => _users.TryGetValue(connection, out user);
    internal RoomUser[] Snapshot() => [.. _users.Values.OrderBy(user => user.Value, StringComparer.OrdinalIgnoreCase)];
    internal bool IsInUse(ConnectionId connection, RoomUser user)
        => _users.Any(entry => entry.Key != connection && string.Equals(entry.Value.Value, user.Value, StringComparison.OrdinalIgnoreCase));

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
