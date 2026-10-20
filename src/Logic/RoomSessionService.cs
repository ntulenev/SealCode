using Abstractions;

using Models;
using Models.Exceptions;

namespace Logic;

/// <summary>Coordinates participant application scenarios independently of SignalR.</summary>
public sealed class RoomSessionService
{
    private readonly IRoomManager _rooms;
    private readonly ILanguageValidator _languages;
    /// <summary>Creates the room session service.</summary>
    public RoomSessionService(IRoomManager rooms, ILanguageValidator languages)
    {
        ArgumentNullException.ThrowIfNull(rooms);
        ArgumentNullException.ThrowIfNull(languages);
        _rooms = rooms;
        _languages = languages;
    }
    /// <summary>Looks up an active participant.</summary>
    public RoomState GetParticipant(RoomId roomId, ConnectionId connection, out RoomUser user)
    {
        if (!_rooms.TryGetRoom(roomId, out var room))
            throw new RoomNotFoundException();
        if (!room.TryGetRoomUser(connection, out user))
            throw new UnauthorizedAccessException("Join the room before changing it");
        return room;
    }
    /// <summary>Appends an update while membership and closure cannot change.</summary>
    public RoomDocumentAppendResult Append(RoomId roomId, ConnectionId connection, byte[] update)
    {
        var room = GetParticipant(roomId, connection, out _);
        return room.AppendForParticipant(connection, update, DateTimeOffset.UtcNow);
    }
    /// <summary>Creates a participant checkpoint.</summary>
    public bool Checkpoint(RoomId roomId, ConnectionId connection, RoomDocumentRevision revision, byte[] state, RoomText text)
    {
        var room = GetParticipant(roomId, connection, out _);
        return room.CheckpointForParticipant(connection, revision, state, text, DateTimeOffset.UtcNow);
    }
    /// <summary>Changes language and captures the assigned metadata revision.</summary>
    public (RoomVersion Version, long Revision) ChangeLanguage(RoomState room, ConnectionId connection, RoomLanguage language)
    {
        ArgumentNullException.ThrowIfNull(room);
        return room.LanguageForParticipant(connection, language, DateTimeOffset.UtcNow, _languages);
    }
}
