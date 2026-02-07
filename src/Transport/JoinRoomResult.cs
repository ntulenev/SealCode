namespace Transport;

/// <summary>
/// Represents the result of joining a room.
/// </summary>
/// <param name="Name">The room name.</param>
/// <param name="Language">The room language.</param>
/// <param name="Text">The room text.</param>
/// <param name="Version">The current room version.</param>
/// <param name="Users">The display names of users currently in the room.</param>
/// <param name="CreatedBy">The display name of the room creator.</param>
/// <param name="YjsState">Optional Yjs document state for the room.</param>
public sealed record JoinRoomResult(
    string Name,
    string Language,
    string Text,
    int Version,
    string[] Users,
    string CreatedBy,
    string? YjsState);
