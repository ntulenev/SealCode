using Abstractions;

using Microsoft.AspNetCore.SignalR;

using Models;
using Models.Exceptions;

using Transport;
using Transport.Models;

using static SealCode.Observers.ObserverEncoding;

namespace SealCode.Observers;

/// <summary>Owns immutable snapshot caching and publication batches.</summary>
internal sealed class ObserverPublication
{
    /// <summary>Creates a publisher sharing the session gate.</summary>
    /// <param name="gate">The gate shared by all observer components.</param>
    /// <param name="registry">The authoritative room registry.</param>
    /// <param name="editors">The participant transport for count notifications.</param>
    /// <param name="enqueue">Enqueues messages while the gate is held.</param>
    /// <param name="connections">Guest reservations guarded by the shared gate.</param>
    public ObserverPublication(Lock gate, IRoomRegistry registry,
        IHubContext<RoomHub> editors, Action<ObserverGuest, ObserverPayload, int> enqueue,
        Dictionary<string, ObserverGuest> connections)
    {
        _gate = gate;
        _registry = registry;
        _editors = editors;
        _enqueue = enqueue;
        _connections = connections;
    }

    /// <summary>Captures or reuses an immutable allow-listed snapshot.</summary>
    /// <param name="room">The authorized presentation to capture.</param>
    /// <returns>The cached or newly encoded allow-listed snapshot.</returns>
    public ObserverPayload CreateSnapshot(ObserverPresentation room)
    {
        var revisions = room.State.GetRevisions();
        // The global snapshot semaphore bounds cache construction. Sharing one immutable
        // payload avoids regenerating the same Base64/JSON for a burst of viewers.
        lock (_gate)
        {
            if (room.SnapshotPayload is not null
                && room.SnapshotDocument == revisions.DocumentRevision
                && room.SnapshotMetadata == revisions.MetadataRevision
                    && room.SnapshotPresence == revisions.PresenceRevision)
            {
                return room.SnapshotPayload;
            }
        }
        // Explicit allow-list: never serialize the room or the editor's
        // identity/capability.
        var sync = room.State.CreateYjsSyncState();
        var payload = Encode(new ObserverSnapshot(
            "Snapshot", room.State.Name.Value, room.State.Kind.ToWireName(),
            sync.Language.Value,
            // Yjs already contains the text; avoid duplicating large snapshots.
            sync.Checkpoint.Length == 0 && sync.Updates.Length == 0 ? sync.Text.Value
                : "",
            sync.Version.Value, sync.MetadataRevision, sync.PresenceRevision,
            [.. sync.Users.Select(user => user.Value)],
            sync.DocumentRevision.Value, sync.CheckpointRevision.Value,
            sync.Checkpoint.Length == 0 ? null : Convert.ToBase64String(sync.Checkpoint),
            [.. sync.Updates.Select(update => new YjsDocumentUpdate(update.Revision.Value,
                Convert.ToBase64String(update.Payload)))]));
        lock (_gate)
        {
            room.SnapshotPayload = payload;
            room.SnapshotDocument = sync.DocumentRevision.Value;
            room.SnapshotMetadata = sync.MetadataRevision;
            room.SnapshotPresence = sync.PresenceRevision;
        }
        return payload;
    }

    /// <summary>Publishes committed updates, metadata and independent guest
    /// counts.</summary>
    /// <param name="room">The presentation whose revisions are published.</param>
    /// <param name="cancellationToken">The publication loop cancellation token.</param>
    /// <returns>A task representing publication and bounded counter delivery.</returns>
    public async Task PublishAsync(ObserverPresentation room,
        CancellationToken cancellationToken)
    {
        // Registry lookups also cover idle deletion, which bypasses RoomKilled
        // notifications.
        if (!_registry.TryGetRoom(room.State.RoomId, out var current)
            || !ReferenceEquals(current, room.State))
        {
            throw new RoomNotFoundException();
        }

        var revisions = room.State.GetRevisions();
        bool hasGuests;
        bool countDirty;
        int count;
        long countRevision;
        lock (_gate)
        {
            if (room.SnapshotDocument != revisions.DocumentRevision
                || room.SnapshotMetadata != revisions.MetadataRevision
                || room.SnapshotPresence != revisions.PresenceRevision || room.Count == 0)
            {
                room.SnapshotPayload = null;
            }

            hasGuests = room.Count > 0;
            countDirty = room.CountDirty;
            room.CountDirty = false;
            count = room.Count;
            countRevision = room.CountRevision;
        }
        if (hasGuests && (revisions.DocumentRevision != room.LastDocument
            || revisions.MetadataRevision != room.LastMetadata
                || revisions.PresenceRevision != room.LastPresence || countDirty))
        {
            var sync = room.State.CreateYjsSyncState();
            YjsDocumentUpdate[] updates;
            bool overflow;
            lock (_gate)
            {
                var pending = new SortedDictionary<long, string>(room.PendingUpdates);
                foreach (var update in sync.Updates)
                {
                    if (update.Revision.Value > room.LastDocument
                        && !pending.ContainsKey(update.Revision.Value))
                    {
                        pending.Add(update.Revision.Value,
                            Convert.ToBase64String(update.Payload));
                    }
                }

                updates = [.. pending.Where(update => update.Key > room.LastDocument
                    && update.Key <= sync.DocumentRevision.Value)
                    .Select(update => new YjsDocumentUpdate(update.Key, update.Value))];
                foreach (var key in room.PendingUpdates.Keys.Where(key
                    => key <= sync.DocumentRevision.Value).ToArray())
                {
                    room.PendingBytes -= room.PendingUpdates[key].Length;
                    _ = room.PendingUpdates.Remove(key);
                }
                overflow = room.PendingOverflow;
                room.PendingOverflow = false;
            }
            var payload = Encode(new ObserverUpdate(
                "Update", sync.DocumentRevision.Value, sync.CheckpointRevision.Value,
                overflow, updates, sync.Language.Value, sync.MetadataRevision,
                sync.PresenceRevision, [.. sync.Users.Select(user => user.Value)],
                count, countRevision));
            var bytes = payload.Bytes;
            lock (_gate)
            {
                foreach (var guest in _connections.Values.Where(guest
                    => ReferenceEquals(guest.Room, room)))
                {
                    _enqueue(guest, payload, bytes);
                }
            }
            lock (_gate)
            {
                room.LastDocument = sync.DocumentRevision.Value;
                room.LastMetadata = sync.MetadataRevision;
                room.LastPresence = sync.PresenceRevision;
            }
        }
        else if (!hasGuests)
        {
            lock (_gate)
            {
                room.PendingUpdates.Clear();
                room.PendingBytes = 0;
                room.PendingOverflow = false;
            }
            lock (_gate)
            {
                room.LastDocument = revisions.DocumentRevision;
                room.LastMetadata = revisions.MetadataRevision;
                room.LastPresence = revisions.PresenceRevision;
            }
        }
        if (countDirty)
        {
            using var timeout
                = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(1));
            try
            {
                await _editors.Clients.Group(room.State.RoomId.Value)
                    .SendAsync(HubContract.Events.ObserverCountChanged, count,
                        countRevision, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            { }
        }
    }
    private readonly Dictionary<string, ObserverGuest> _connections;
    private readonly Lock _gate;
    private readonly IRoomRegistry _registry;
    private readonly IHubContext<RoomHub> _editors;
    private readonly Action<ObserverGuest, ObserverPayload, int> _enqueue;
}
