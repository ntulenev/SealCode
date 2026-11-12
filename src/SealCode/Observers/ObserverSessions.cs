using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

using Abstractions;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

using Models;
using Models.Configuration;

using SealCode.Hosting;

using Transport.Models;

namespace SealCode.Observers;

/// <summary>
/// Owns independent guest admission, capabilities and bounded transport workers.
/// The editor's mutation and delivery paths never wait for these workers.
/// </summary>
#pragma warning disable CA1515 // Public dependency of the integration-tested public hub.
public sealed class ObserverSessions : BackgroundService
#pragma warning restore CA1515
{
    private readonly Lock _gate = new();
    private readonly IDataProtector _linkProtector = new EphemeralDataProtectionProvider()
        .CreateProtector("SealCode.ObserverLinks");
    private readonly Dictionary<string, Guest> _connections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Presentation> _rooms = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Presentation> _tokens = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _sources = new(StringComparer.Ordinal);
    private readonly HashSet<Task> _workers = [];
    private readonly (long Minute, int Count)[] _sourceWindows = new (long, int)[256];
    private readonly ObserverLimits _limits;
    private readonly IRoomRegistry _registry;
    private readonly IHubContext<ObserverHub> _hub;
    private readonly IHubContext<RoomHub> _editors;
    private readonly ILogger<ObserverSessions> _logger;
    private readonly SemaphoreSlim _snapshots;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates one process-wide viewer service.
    /// </summary>
    /// <param name="settings">The validated application configuration.</param>
    /// <param name="registry">The authoritative in-memory room registry.</param>
    /// <param name="hub">The read-only transport used for observer delivery.</param>
    /// <param name="editors">The existing transport used for participant counter notifications.</param>
    /// <param name="logger">The logger, which must not record capabilities or document contents.</param>
    /// <param name="timeProvider">The clock used by admission, rate windows and deadlines; null uses the system clock.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
    public ObserverSessions(IOptions<ApplicationConfiguration> settings, IRoomRegistry registry,
        IHubContext<ObserverHub> hub, IHubContext<RoomHub> editors, ILogger<ObserverSessions> logger,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(editors);
        ArgumentNullException.ThrowIfNull(logger);
        _limits = settings.Value.Observers;
        _registry = registry;
        _hub = hub;
        _editors = editors;
        _logger = logger;
        _snapshots = new(_limits.ConcurrentSnapshots);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Creates or rotates a capability, retaining its admission hash and an encrypted owner copy.
    /// </summary>
    /// <param name="room">The existing room to present.</param>
    /// <returns>The relative guest URL containing a newly generated capability.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the room is null.</exception>
    /// <exception cref="HubException">Thrown when guest access is disabled.</exception>
    public string CreateLink(RoomState room)
    {
        ArgumentNullException.ThrowIfNull(room);
        lock (_gate)
        {
            if (!_limits.Enabled)
            {
                throw Error("ObserverUnavailable");
            }

            _ = room.GetRevisions(); // Reject a concurrently closed aggregate.
            var presentation = GetPresentation(room);
            Revoke(presentation);
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            presentation.TokenHash = Hash(token);
            presentation.ProtectedToken = _linkProtector.Protect(token);
            _tokens.Add(presentation.TokenHash, presentation);
            return "/watch#" + token;
        }
    }

    /// <summary>
    /// Invalidates the token before terminating existing subscriptions.
    /// </summary>
    /// <param name="roomId">The room whose guest capability and sessions are revoked.</param>
    public void Disable(RoomId roomId)
    {
        lock (_gate)
        {
            if (_rooms.TryGetValue(roomId.Value, out var room))
            {
                Revoke(room);
            }
        }
    }

    /// <summary>
    /// Recovers the current guest URL for the owner without rotating its capability or sessions.
    /// </summary>
    /// <param name="roomId">The room whose guest access is inspected.</param>
    /// <returns>The relative guest URL when a capability exists; otherwise, null.</returns>
    public string? GetLink(RoomId roomId)
    {
        lock (_gate)
        {
            return _rooms.TryGetValue(roomId.Value, out var room) && room.ProtectedToken is not null
                ? "/watch#" + _linkProtector.Unprotect(room.ProtectedToken) : null;
        }
    }

    /// <summary>
    /// Returns additive, revisioned viewer presence to an editor.
    /// </summary>
    /// <param name="roomId">The room whose observer presence is read.</param>
    /// <returns>The observer connection count and its independent monotonic revision.</returns>
    public (int Count, long Revision) Count(RoomId roomId)
    {
        lock (_gate)
        {
            return _rooms.TryGetValue(roomId.Value, out var room) ? (room.Count, room.CountRevision) : (0, 0);
        }
    }

    /// <summary>
    /// Records a committed immutable update before editor broadcast; never waits for guest I/O.
    /// </summary>
    /// <param name="roomId">The room containing the committed edit.</param>
    /// <param name="update">The immutable, accepted Base64 Yjs update.</param>
    /// <param name="revision">The server-assigned document revision of this update.</param>
    /// <exception cref="ArgumentNullException">Thrown when the update is null.</exception>
    public void RecordUpdate(RoomId roomId, string update, long revision)
    {
        ArgumentNullException.ThrowIfNull(update);
        lock (_gate)
        {
            if (!_rooms.TryGetValue(roomId.Value, out var room) || room.Count == 0 || revision <= room.LastDocument)
            {
                return;
            }
            // Retain a bounded reference to the existing Base64 argument, so a checkpoint
            // cannot prune changes between delivery ticks. No document data is copied.
            if (update.Length > _limits.DeliveryQueueBytesPerConnection
                || room.PendingBytes + update.Length > _limits.DeliveryQueueBytesPerConnection
                || room.PendingUpdates.Count >= _limits.DeliveryQueueEventsPerConnection)
            {
                room.PendingUpdates.Clear();
                room.PendingBytes = 0;
                room.PendingOverflow = true;
                return;
            }
            if (room.PendingUpdates.TryAdd(revision, update))
            {
                room.PendingBytes += update.Length;
            }
        }
    }

    /// <summary>
    /// Reserves a transport slot before any guest can join.
    /// </summary>
    /// <param name="context">The newly connected observer transport.</param>
    /// <exception cref="ArgumentNullException">Thrown when the context is null.</exception>
    /// <exception cref="HubException">Thrown when guest access is disabled or admission capacity is exhausted.</exception>
    public void Connect(HubCallerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var source = ConnectionSource.From(context.GetHttpContext()?.Connection.RemoteIpAddress);
        Guest guest;
        lock (_gate)
        {
            if (!_limits.Enabled)
            {
                throw Error("ObserverUnavailable");
            }

            var bucket = (uint)StringComparer.Ordinal.GetHashCode(source) % 256;
            var minute = _timeProvider.GetUtcNow().ToUnixTimeSeconds() / 60;
            var window = _sourceWindows[bucket];
            if (window.Minute != minute)
            {
                window = (minute, 0);
            }

            _sourceWindows[bucket] = (minute, window.Count + 1);
            if (window.Count >= _limits.NewConnectionsPerSourcePerMinute
                || _connections.Count >= _limits.MaxConnections
                || _sources.GetValueOrDefault(source) >= _limits.MaxConnectionsPerSource)
            {
                throw Error("ObserverLimitReached");
            }

            guest = new(context, source, _limits.DeliveryQueueEventsPerConnection);
            _connections.Add(context.ConnectionId, guest);
            ObserverMetrics.Connections.Add(1);
            _sources[source] = _sources.GetValueOrDefault(source) + 1;
            guest.Pump = Task.Run(() => DeliverAsync(guest));
            _ = _workers.RemoveWhere(task => task.IsCompleted);
            _ = _workers.Add(guest.Pump);
        }
        _ = ExpireUnjoinedAsync(guest);
    }

    /// <summary>
    /// Joins using a capability, never a client-supplied room identifier.
    /// </summary>
    /// <param name="context">The admitted observer connection.</param>
    /// <param name="token">The opaque read-only capability; it must not be logged.</param>
    /// <exception cref="ArgumentNullException">Thrown when the context is null.</exception>
    /// <exception cref="HubException">Thrown when the capability, membership or room capacity check fails.</exception>
    public void Join(HubCallerContext context, string token)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (_gate)
        {
            var guest = GetConnection(context.ConnectionId);
            if (token is null || token.Length != 43 || !_tokens.TryGetValue(Hash(token), out var room))
            {
                throw Error("ObserverUnavailable");
            }

            EnsureOpen(room);
            if (guest.Room is not null)
            {
                if (ReferenceEquals(guest.Room, room) && guest.Generation == room.Generation)
                {
                    return;
                }

                throw Error("Forbidden");
            }
            if (room.Count >= _limits.MaxPerRoom)
            {
                throw Error("ObserverLimitReached");
            }

            guest.Room = room;
            guest.Generation = room.Generation;
            room.Count++;
            room.CountRevision++;
            room.CountDirty = true;
            try
            { ScheduleSnapshot(guest); }
            catch { LeaveCore(guest); throw; }
        }
    }

    /// <summary>
    /// Rate-limited recovery of the current authorized room.
    /// </summary>
    /// <param name="connectionId">The joined observer connection requesting recovery.</param>
    /// <exception cref="HubException">Thrown when authorization or the synchronization rate limit fails.</exception>
    public void Sync(string connectionId)
    {
        lock (_gate)
        {
            var guest = RequireJoined(connectionId);
            ScheduleSnapshot(guest);
        }
    }

    /// <summary>
    /// Reads only revisions and the viewer count, without disclosing room capabilities.
    /// </summary>
    /// <param name="connectionId">The joined observer connection whose room is read.</param>
    /// <returns>The authorized room revisions and observer counter, without editor capabilities.</returns>
    /// <exception cref="HubException">Thrown when the connection is not authorized to observe an open room.</exception>
    public object Revisions(string connectionId)
    {
        lock (_gate)
        {
            var guest = RequireJoined(connectionId);
            var room = guest.Room!;
            return new { Revisions = room.State.GetRevisions(), ObserverCount = room.Count, ObserverRevision = room.CountRevision };
        }
    }

    /// <summary>
    /// Enforces a bounded per-connection invocation window.
    /// </summary>
    /// <param name="connectionId">The admitted connection invoking a guest method.</param>
    /// <exception cref="HubException">Thrown when admission is absent or the invocation rate is exceeded.</exception>
    public void CheckInvocation(string connectionId)
    {
        lock (_gate)
        {
            var guest = GetConnection(connectionId);
            var second = _timeProvider.GetTimestamp() / _timeProvider.TimestampFrequency;
            if (guest.InvocationSecond != second)
            { guest.InvocationSecond = second; guest.Invocations = 0; }
            if (++guest.Invocations > _limits.InvocationsPerSecond)
            {
                throw Error("RateLimited");
            }
        }
    }

    /// <summary>
    /// Leaves without freeing a still-connected transport slot.
    /// </summary>
    /// <param name="connectionId">The connection leaving its current observer membership.</param>
    public void Leave(string connectionId)
    {
        lock (_gate)
        {
            if (_connections.TryGetValue(connectionId, out var guest))
            {
                LeaveCore(guest);
            }
        }
    }

    /// <summary>
    /// Releases each source, room and process reservation exactly once.
    /// </summary>
    /// <param name="connectionId">The connection whose transport reservations are released.</param>
    public void Disconnect(string connectionId)
    {
        lock (_gate)
        {
            if (!_connections.Remove(connectionId, out var guest))
            {
                return;
            }

            ObserverMetrics.Connections.Add(-1);
            LeaveCore(guest);
            if (--_sources[guest.Source] == 0)
            {
                _ = _sources.Remove(guest.Source);
            }

            guest.Stop.Cancel();
            _ = guest.Queue.Writer.TryComplete();
        }
    }

    private static HubException Error(string code)
    {
        ObserverMetrics.Rejected.Add(1, new KeyValuePair<string, object?>("reason", code));
        return new(code);
    }
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private Guest GetConnection(string id) => _connections.TryGetValue(id, out var guest) ? guest : throw Error("ObserverUnavailable");
    private Guest RequireJoined(string id)
    {
        var guest = GetConnection(id);
        if (!IsCurrent(guest, guest.Room, guest.Generation))
        {
            throw Error("ObserverUnavailable");
        }

        EnsureOpen(guest.Room!);
        return guest;
    }
    private static void EnsureOpen(Presentation room)
    {
        try
        { _ = room.State.GetRevisions(); }
        catch (Models.Exceptions.RoomNotFoundException) { throw Error("ObserverUnavailable"); }
    }
    private bool IsCurrent(Guest guest, Presentation? room, long generation) => room is not null
        && ReferenceEquals(guest.Room, room) && room.TokenHash is not null && room.Generation == generation
        && _connections.ContainsKey(guest.Context.ConnectionId);
    private Presentation GetPresentation(RoomState state)
    {
        if (!_rooms.TryGetValue(state.RoomId.Value, out var room))
        {
            room = new(state);
            _rooms.Add(state.RoomId.Value, room);
        }
        return room;
    }
    private void Revoke(Presentation room)
    {
        if (room.TokenHash is not null)
        {
            _ = _tokens.Remove(room.TokenHash);
        }

        room.TokenHash = null;
        room.ProtectedToken = null;
        room.SnapshotPayload = null;
        room.PendingUpdates.Clear();
        room.PendingBytes = 0;
        room.Generation++;
        foreach (var guest in _connections.Values.Where(guest => ReferenceEquals(guest.Room, room)).ToArray())
        {
            // Abort also releases transport buffers and interrupts an in-flight snapshot.
            guest.Context.Abort();
            Disconnect(guest.Context.ConnectionId);
        }
    }
    private static void LeaveCore(Guest guest)
    {
        if (guest.Room is { } room)
        {
            room.Count--;
            room.CountRevision++;
            room.CountDirty = true;
            guest.Room = null;
        }
        guest.Generation = -1;
        guest.SnapshotPending = false;
        guest.NeedsResync = false;
        while (guest.Queue.Reader.TryRead(out _))
        { }
        guest.QueuedBytes = 0;
    }
    private void ScheduleSnapshot(Guest guest)
    {
        if (guest.SnapshotPending)
        {
            return;
        }

        var minute = _timeProvider.GetUtcNow().ToUnixTimeSeconds() / 60;
        if (guest.SnapshotMinute != minute)
        { guest.SnapshotMinute = minute; guest.SnapshotRequests = 0; }
        if (++guest.SnapshotRequests > _limits.SnapshotRequestsPerConnectionPerMinute)
        {
            throw Error("RateLimited");
        }

        guest.SnapshotPending = true;
        guest.NeedsResync = false;
        // Snapshot jobs contain no document data until they acquire the global delivery permit.
        while (guest.Queue.Reader.TryRead(out _))
        { }
        guest.QueuedBytes = 0;
        if (!guest.Queue.Writer.TryWrite(new(guest.Room!, guest.Generation, null, 0)))
        {
            guest.SnapshotPending = false;
            throw Error("Busy");
        }
    }
    private void Enqueue(Guest guest, ObserverPayload payload, int bytes)
    {
        if (guest.NeedsResync)
        {
            return;
        }

        if (bytes > _limits.DeliveryQueueBytesPerConnection
            || guest.QueuedBytes + bytes > _limits.DeliveryQueueBytesPerConnection
            || !guest.Queue.Writer.TryWrite(new(guest.Room!, guest.Generation, payload, bytes)))
        {
            while (guest.Queue.Reader.TryRead(out _))
            { }
            guest.QueuedBytes = 0;
            guest.SnapshotPending = false;
            guest.NeedsResync = true;
            ObserverMetrics.Resyncs.Add(1);
            _ = guest.Queue.Writer.TryWrite(new(guest.Room!, guest.Generation, Encode(new { Type = "RequiresResync" }), 0));
            return;
        }
        guest.QueuedBytes += bytes;
    }

    private async Task ExpireUnjoinedAsync(Guest guest)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(_limits.UnjoinedTimeoutSeconds), _timeProvider, guest.Stop.Token).ConfigureAwait(false);
            lock (_gate)
            {
                if (guest.Room is null)
                { guest.Context.Abort(); Disconnect(guest.Context.ConnectionId); }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task DeliverAsync(Guest guest)
    {
        try
        {
            await foreach (var delivery in guest.Queue.Reader.ReadAllAsync(guest.Stop.Token).ConfigureAwait(false))
            {
                lock (_gate)
                {
                    guest.QueuedBytes = Math.Max(0, guest.QueuedBytes - delivery.Bytes);
                    if (!IsCurrent(guest, delivery.Room, delivery.Generation))
                    {
                        continue;
                    }
                }
                if (delivery.Payload is null)
                {
                    // Includes serialization and the entire backpressured transport write.
                    await _snapshots.WaitAsync(guest.Stop.Token).ConfigureAwait(false);
                    var started = Stopwatch.GetTimestamp();
                    try
                    {
                        ObserverPayload payload;
                        lock (_gate)
                        {
                            if (!IsCurrent(guest, delivery.Room, delivery.Generation))
                            {
                                continue;
                            }
                        }
                        payload = CreateSnapshot(delivery.Room);
                        await SendAsync(guest, delivery, payload).ConfigureAwait(false);
                        ObserverPayload counter;
                        lock (_gate)
                        {
                            counter = Encode(new { Type = "Count", ObserverCount = delivery.Room.Count, ObserverRevision = delivery.Room.CountRevision });
                        }

                        await SendAsync(guest, delivery, counter).ConfigureAwait(false);
                        ObserverMetrics.Snapshots.Add(1);
                    }
                    finally
                    {
                        _ = _snapshots.Release();
                        ObserverMetrics.SnapshotDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                        lock (_gate)
                        {
                            guest.SnapshotPending = false;
                        }
                    }
                }
                else
                {
                    await SendAsync(guest, delivery, delivery.Payload).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Do not log capability arguments or document payloads.
            _logger.LogDebug("Observer delivery ended ({FailureType})", ex.GetType().Name);
        }
        finally
        {
            guest.Context.Abort();
            Disconnect(guest.Context.ConnectionId);
            guest.Stop.Dispose();
        }
    }
    private async Task SendAsync(Guest guest, Delivery delivery, ObserverPayload payload)
    {
        lock (_gate)
        {
            if (!IsCurrent(guest, delivery.Room, delivery.Generation))
            {
                return;
            }
        }

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(_limits.SendTimeoutSeconds), _timeProvider);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(guest.Stop.Token, deadline.Token);
        try
        {
            await _hub.Clients.Client(guest.Context.ConnectionId).SendAsync("ObserverMessage", payload.Value, timeout.Token).ConfigureAwait(false);
            ObserverMetrics.Bytes.Add(payload.Bytes);
        }
        catch (OperationCanceledException) when (!guest.Stop.IsCancellationRequested)
        {
            ObserverMetrics.SlowConsumers.Add(1);
            throw;
        }
    }
    private ObserverPayload CreateSnapshot(Presentation room)
    {
        var revisions = room.State.GetRevisions();
        // The global snapshot semaphore bounds cache construction. Sharing one immutable
        // payload avoids regenerating the same Base64/JSON for a burst of viewers.
        lock (_gate)
        {
            if (room.SnapshotPayload is not null && room.SnapshotDocument == revisions.DocumentRevision
                && room.SnapshotMetadata == revisions.MetadataRevision && room.SnapshotPresence == revisions.PresenceRevision)
            {
                return room.SnapshotPayload;
            }
        }
        // Explicit allow-list: never serialize the room or the editor's identity/capability.
        var sync = room.State.CreateYjsSyncState();
        var payload = Encode(new
        {
            Type = "Snapshot",
            Name = room.State.Name.Value,
            Kind = room.State.Kind == RoomKind.Whiteboard ? "whiteboard" : "code",
            Language = sync.Language.Value,
            // Yjs already contains the text. Repeating a large plain-text snapshot
            // doubles serialization/transport allocations on every presentation join.
            Text = sync.Checkpoint.Length == 0 && sync.Updates.Length == 0 ? sync.Text.Value : "",
            Version = sync.Version.Value,
            sync.MetadataRevision,
            sync.PresenceRevision,
            Users = sync.Users.Select(user => user.Value).ToArray(),
            DocumentRevision = sync.DocumentRevision.Value,
            CheckpointRevision = sync.CheckpointRevision.Value,
            YjsState = sync.Checkpoint.Length == 0 ? null : Convert.ToBase64String(sync.Checkpoint),
            YjsUpdates = sync.Updates.Select(update => new YjsDocumentUpdate(update.Revision.Value, Convert.ToBase64String(update.Payload))).ToArray()
        });
        lock (_gate)
        {
            room.SnapshotPayload = payload;
            room.SnapshotDocument = sync.DocumentRevision.Value;
            room.SnapshotMetadata = sync.MetadataRevision;
            room.SnapshotPresence = sync.PresenceRevision;
        }
        return payload;
    }

    /// <inheritdoc />
    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_limits.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500), _timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                Presentation[] rooms;
                lock (_gate)
                {
                    rooms = [.. _rooms.Values];
                }

                foreach (var room in rooms)
                {
                    try
                    { await PublishAsync(room, stoppingToken).ConfigureAwait(false); }
                    catch (Models.Exceptions.RoomNotFoundException)
                    {
                        lock (_gate)
                        { Revoke(room); _ = _rooms.Remove(room.State.RoomId.Value); }
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
                    {
                        lock (_gate)
                        {
                            room.CountDirty = true;
                        }

                        _logger.LogDebug("Observer publication deferred ({FailureType})", ex.GetType().Name);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            Guest[] guests;
            lock (_gate)
            {
                guests = [.. _connections.Values];
            }

            foreach (var guest in guests)
            { guest.Context.Abort(); Disconnect(guest.Context.ConnectionId); }
            await Task.WhenAll(guests.Select(guest => guest.Pump)).ConfigureAwait(false);
        }
    }
    private async Task PublishAsync(Presentation room, CancellationToken cancellationToken)
    {
        // Registry lookups also cover idle deletion, which bypasses RoomKilled notifications.
        if (!_registry.TryGetRoom(room.State.RoomId, out var current) || !ReferenceEquals(current, room.State))
        {
            throw new Models.Exceptions.RoomNotFoundException();
        }

        var revisions = room.State.GetRevisions();
        bool hasGuests;
        bool countDirty;
        int count;
        long countRevision;
        lock (_gate)
        {
            if (room.SnapshotDocument != revisions.DocumentRevision || room.SnapshotMetadata != revisions.MetadataRevision
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
            || revisions.MetadataRevision != room.LastMetadata || revisions.PresenceRevision != room.LastPresence || countDirty))
        {
            var sync = room.State.CreateYjsSyncState();
            YjsDocumentUpdate[] updates;
            bool overflow;
            lock (_gate)
            {
                var pending = new SortedDictionary<long, string>(room.PendingUpdates);
                foreach (var update in sync.Updates)
                {
                    if (update.Revision.Value > room.LastDocument && !pending.ContainsKey(update.Revision.Value))
                    {
                        pending.Add(update.Revision.Value, Convert.ToBase64String(update.Payload));
                    }
                }

                updates = [.. pending.Where(update => update.Key > room.LastDocument && update.Key <= sync.DocumentRevision.Value)
                    .Select(update => new YjsDocumentUpdate(update.Key, update.Value))];
                foreach (var key in room.PendingUpdates.Keys.Where(key => key <= sync.DocumentRevision.Value).ToArray())
                {
                    room.PendingBytes -= room.PendingUpdates[key].Length;
                    _ = room.PendingUpdates.Remove(key);
                }
                overflow = room.PendingOverflow;
                room.PendingOverflow = false;
            }
            var payload = Encode(new
            {
                Type = "Update",
                DocumentRevision = sync.DocumentRevision.Value,
                CheckpointRevision = sync.CheckpointRevision.Value,
                RequiresResync = overflow,
                YjsUpdates = updates,
                Language = sync.Language.Value,
                sync.MetadataRevision,
                sync.PresenceRevision,
                Users = sync.Users.Select(user => user.Value).ToArray(),
                ObserverCount = count,
                ObserverRevision = countRevision
            });
            var bytes = payload.Bytes;
            lock (_gate)
            {
                foreach (var guest in _connections.Values.Where(guest => ReferenceEquals(guest.Room, room)))
                {
                    Enqueue(guest, payload, bytes);
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
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(1));
            try
            {
                await _editors.Clients.Group(room.State.RoomId.Value)
                    .SendAsync("ObserverCountChanged", count, countRevision, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        }
    }

    /// <inheritdoc />
    public async override Task StopAsync(CancellationToken cancellationToken)
    {
        Task[] workers;
        lock (_gate)
        {
            foreach (var guest in _connections.Values.ToArray())
            {
                guest.Context.Abort();
                Disconnect(guest.Context.ConnectionId);
            }
            workers = [.. _workers];
        }
        await Task.WhenAll(workers).WaitAsync(cancellationToken).ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        base.Dispose();
        _snapshots.Dispose();
    }

    private sealed class Presentation
    {
        public Presentation(RoomState state)
        {
            State = state;
        }

        public RoomState State { get; }
        public string? TokenHash { get; set; }
        public string? ProtectedToken { get; set; }
        public long Generation { get; set; }
        public int Count { get; set; }
        public long CountRevision { get; set; }
        public bool CountDirty { get; set; }
        public long LastDocument { get; set; }
        public long LastMetadata { get; set; }
        public long LastPresence { get; set; }
        public ObserverPayload? SnapshotPayload { get; set; }
        public long SnapshotDocument { get; set; }
        public long SnapshotMetadata { get; set; }
        public long SnapshotPresence { get; set; }
        public SortedDictionary<long, string> PendingUpdates { get; } = [];
        public int PendingBytes { get; set; }
        public bool PendingOverflow { get; set; }
    }
    private sealed class Guest
    {
        public Guest(HubCallerContext context, string source, int queueCapacity)
        {
            Context = context;
            Source = source;
            Stop = CancellationTokenSource.CreateLinkedTokenSource(context.ConnectionAborted);
            Queue = Channel.CreateBounded<Delivery>(new BoundedChannelOptions(queueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false
            });
        }

        public HubCallerContext Context { get; }
        public string Source { get; }
        public CancellationTokenSource Stop { get; }
        public Channel<Delivery> Queue { get; }
        public Task Pump { get; set; } = Task.CompletedTask;
        public Presentation? Room { get; set; }
        public long Generation { get; set; } = -1;
        public long InvocationSecond { get; set; }
        public int Invocations { get; set; }
        public long SnapshotMinute { get; set; }
        public int SnapshotRequests { get; set; }
        public bool SnapshotPending { get; set; }
        public bool NeedsResync { get; set; }
        public int QueuedBytes { get; set; }
    }
    private static ObserverPayload Encode(object value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        using var document = JsonDocument.Parse(bytes);
        return new(document.RootElement.Clone(), bytes.Length);
    }
    private sealed record ObserverPayload(JsonElement Value, int Bytes);
    private sealed record Delivery(Presentation Room, long Generation, ObserverPayload? Payload, int Bytes);
}
