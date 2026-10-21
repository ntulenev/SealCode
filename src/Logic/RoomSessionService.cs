using Abstractions;

using Models;
using Models.Exceptions;

namespace Logic;

/// <summary>
/// Coordinates participant application scenarios independently of SignalR.
/// </summary>
public sealed class RoomSessionService : IRoomSessionService
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoomSessionService"/> class.
    /// </summary>
    /// <param name="rooms">The room manager.</param>
    /// <param name="languages">The language validator.</param>
    /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
    public RoomSessionService(IRoomManager rooms, ILanguageValidator languages)
    {
        ArgumentNullException.ThrowIfNull(rooms);
        ArgumentNullException.ThrowIfNull(languages);

        _rooms = rooms;
        _languages = languages;
    }

    /// <inheritdoc />
    public RoomState GetParticipant(
        RoomId roomId,
        ConnectionId connection,
        out RoomUser user)
    {
        if (!TryGetParticipant(roomId, connection, out var room, out user))
        {
            throw new UnauthorizedAccessException("Join the room before changing it");
        }
        return room;
    }

    /// <inheritdoc />
    public bool TryGetParticipant(RoomId roomId, ConnectionId connection, out RoomState room, out RoomUser user)
    {
        if (!_rooms.TryGetRoom(roomId, out room))
        {
            throw new RoomNotFoundException();
        }
        return room.TryGetRoomUser(connection, out user);
    }

    /// <inheritdoc />
    public RoomSessionAppendResult Append(RoomId roomId, ConnectionId connection, string updateBase64)
    {
        if (string.IsNullOrWhiteSpace(updateBase64))
        {
            throw new ArgumentException("Update payload required");
        }
        var room = GetParticipant(roomId, connection, out var author);
        if (updateBase64.Length > room.Limits.MaxEncodedUpdateBytes)
        {
            throw new RoomLimitException("Update size");
        }
        var update = Decode(updateBase64);
        room = GetParticipant(roomId, connection, out _);
        var result = room.AppendForParticipant(connection, update, DateTimeOffset.UtcNow);
        return new RoomSessionAppendResult(result.DocumentRevision, result.RoomVersion, author);
    }

    /// <inheritdoc />
    public CheckpointOutcome Checkpoint(RoomId roomId, ConnectionId connection, string stateBase64, string? textSnapshot, long basedOnRevision)
    {
        RoomDocumentRevision revision;
        try
        {
            revision = new(basedOnRevision);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException("Invalid document revision");
        }
        if (string.IsNullOrWhiteSpace(stateBase64))
        {
            throw new ArgumentException("State payload required");
        }
        var room = GetParticipant(roomId, connection, out _);
        if (stateBase64.Length > room.Limits.MaxEncodedCheckpointBytes
            || (textSnapshot?.Length ?? 0) > room.Limits.MaxSnapshotCharacters)
        {
            throw new RoomLimitException("Checkpoint size");
        }
        if (revision != room.DocumentRevision)
        {
            return CheckpointOutcome.Stale;
        }
        var state = Decode(stateBase64);
        room = GetParticipant(roomId, connection, out _);
        return room.CheckpointForParticipant(connection, revision, state, new RoomText(textSnapshot ?? string.Empty), DateTimeOffset.UtcNow)
            ? CheckpointOutcome.Accepted : CheckpointOutcome.Stale;
    }

    private static byte[] Decode(string payload)
    {
        try
        {
            return Convert.FromBase64String(payload);
        }
        catch (FormatException)
        {
            throw new ArgumentException("Invalid Yjs payload");
        }
    }

    /// <inheritdoc />
    public RoomLanguageChangeResult ChangeLanguage(
        RoomState room,
        ConnectionId connection,
        RoomLanguage language)
    {
        ArgumentNullException.ThrowIfNull(room);
        return room.LanguageForParticipant(connection, language, DateTimeOffset.UtcNow, _languages);
    }

    private readonly IRoomManager _rooms;
    private readonly ILanguageValidator _languages;
}
