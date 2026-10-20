namespace Models;

/// <summary>
/// Describes a membership removal and its matching presence snapshot.
/// </summary>
public sealed class RoomLeaveResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoomLeaveResult"/> class.
    /// </summary>
    /// <param name="removed">Whether the participant was removed.</param>
    /// <param name="user">The removed user, or the default value when no participant was removed.</param>
    /// <param name="users">The snapshot of remaining users.</param>
    /// <param name="presenceRevision">The membership revision after the removal.</param>
    /// <exception cref="ArgumentNullException">Thrown when the users array is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the removed user is inconsistent with the removal status or a remaining user is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the presence revision is negative, or zero after a removal.</exception>
    public RoomLeaveResult(
        bool removed,
        RoomUser user,
        RoomUser[] users,
        long presenceRevision)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentOutOfRangeException.ThrowIfNegative(presenceRevision);
        if (removed)
        {
            if (string.IsNullOrWhiteSpace(user.Value))
            {
                throw new ArgumentException("A removed participant must have a valid user name.", nameof(user));
            }

            ArgumentOutOfRangeException.ThrowIfZero(presenceRevision);
        }
        else if (user != default)
        {
            throw new ArgumentException("No user can be removed when the participant was not found.", nameof(user));
        }

        var snapshot = users.ToArray();
        if (snapshot.Any(x => string.IsNullOrWhiteSpace(x.Value)))
        {
            throw new ArgumentException("Remaining users must have valid names.", nameof(users));
        }

        Removed = removed;
        User = user;
        _users = snapshot;
        PresenceRevision = presenceRevision;
    }

    /// <summary>
    /// Gets whether the participant was removed.
    /// </summary>
    public bool Removed { get; }

    /// <summary>
    /// Gets the removed user, or the default value when no participant was removed.
    /// </summary>
    public RoomUser User { get; }

    /// <summary>
    /// Gets a copy of the remaining users snapshot.
    /// </summary>
    public RoomUser[] Users => [.. _users];

    /// <summary>
    /// Gets the membership revision after the removal.
    /// </summary>
    public long PresenceRevision { get; }

    private readonly RoomUser[] _users;
}
