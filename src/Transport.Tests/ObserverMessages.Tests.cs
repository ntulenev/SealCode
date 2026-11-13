using System.Text.Json;

using Models;

using Transport.Models;

namespace Transport.Tests;

public sealed class ObserverMessagesTests
{
    [Fact]
    public void SnapshotPreservesWireJsonIncludingNullState()
        => Assert.Equal(/*lang=json,strict*/ """{"Type":"Snapshot","Name":"Room","Kind":"code","Language":"sql","Text":"text","Version":1,"MetadataRevision":2,"PresenceRevision":3,"Users":["Alice"],"DocumentRevision":4,"CheckpointRevision":0,"YjsState":null,"YjsUpdates":[]}""",
            JsonSerializer.Serialize(new ObserverSnapshot("Snapshot", "Room", "code", "sql",
                "text", 1, 2, 3, ["Alice"], 4, 0, null, [])));

    [Fact]
    public void UpdatePreservesWireJson()
        => Assert.Equal(/*lang=json,strict*/ """{"Type":"Update","DocumentRevision":4,"CheckpointRevision":0,"RequiresResync":false,"YjsUpdates":[{"Revision":4,"Update":"AQ=="}],"Language":"sql","MetadataRevision":2,"PresenceRevision":3,"Users":[],"ObserverCount":1,"ObserverRevision":5}""",
            JsonSerializer.Serialize(new ObserverUpdate("Update", 4, 0, false,
                [new YjsDocumentUpdate(4, "AQ==")], "sql", 2, 3, [], 1, 5)));

    [Fact]
    public void CountPreservesWireJson()
        => Assert.Equal(/*lang=json,strict*/ """{"Type":"Count","ObserverCount":1,"ObserverRevision":5}""",
            JsonSerializer.Serialize(new ObserverCountMessage("Count", 1, 5)));

    [Fact]
    public void ResyncPreservesPropertyPresence()
        => Assert.Equal(/*lang=json,strict*/ """{"Type":"RequiresResync"}""",
            JsonSerializer.Serialize(new ObserverRequiresResync("RequiresResync")));

    [Fact]
    public void RevisionsPreserveWireJson()
        => Assert.Equal(/*lang=json,strict*/ """{"Revisions":{"ObserverCount":0,"ObserverRevision":0,"DocumentRevision":1,"MetadataRevision":2,"PresenceRevision":3},"ObserverCount":4,"ObserverRevision":5}""",
            JsonSerializer.Serialize(new ObserverRevisions(new RoomRevisions(1, 2, 3), 4, 5)));
}
