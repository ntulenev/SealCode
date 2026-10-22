using Models;

namespace TestKit;

internal static class RoomStateBuilder
{
    internal static RoomDocumentAppendResult AppendAsTestParticipant(this RoomState room, byte[] update, DateTimeOffset now)
        => room.Append(Participant(room), update, now);

    internal static bool CheckpointAsTestParticipant(this RoomState room, RoomDocumentRevision revision, byte[] state, RoomText text, DateTimeOffset now)
        => room.Checkpoint(Participant(room), revision, state, text, now);

    internal static RoomVersion ChangeLanguageAsTestParticipant(this RoomState room, RoomLanguage language, DateTimeOffset now, ILanguageValidator validator)
        => room.ChangeLanguage(Participant(room), language, now, validator).RoomVersion;

    internal static ConnectionId Participant(RoomState room)
    {
        if (room.ConnectedUsers.Count > 0)
        {
            return room.ConnectedUsers.Keys.First();
        }
        var connection = new ConnectionId("test-participant");
        room.AddUser(connection, new RoomUser("Test participant"), 5);
        return connection;
    }
}
