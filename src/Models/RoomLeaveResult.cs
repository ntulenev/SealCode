namespace Models;

/// <summary>
/// Describes a membership removal and its matching presence snapshot.
/// </summary>
/// <param name="Removed">Whether the participant was removed.</param>
/// <param name="User">The removed user, or the default value when no participant was removed.</param>
/// <param name="Users">The detached snapshot of remaining users.</param>
/// <param name="PresenceRevision">The membership revision after the removal.</param>
public readonly record struct RoomLeaveResult(
    bool Removed,
    RoomUser User,
    RoomUser[] Users,
    long PresenceRevision);
