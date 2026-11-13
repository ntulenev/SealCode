using System.Security.Cryptography;
using System.Text;

using Abstractions;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

using Models;
using Models.Configuration;
using Models.Exceptions;

using SealCode.Hosting;

using Transport.Models;


using static SealCode.Observers.ObserverSessionErrors;

namespace SealCode.Observers;
/// <summary>
/// Owns independent guest admission, capabilities and bounded transport workers.
/// The editor's mutation and delivery paths never wait for these workers.
/// </summary>
#pragma warning disable CA1515 // Public dependency of the integration-tested public hub.
public sealed class ObserverSessions : BackgroundService, IObserverUpdates
#pragma warning restore CA1515
{
    /// <summary>
    /// Creates one process-wide viewer service.
    /// </summary>
    /// <param name="settings">The validated application configuration.</param>
    /// <param name="registry">The authoritative in-memory room registry.</param>
    /// <param name="hub">The read-only transport used for observer delivery.</param>
    /// <param name="editors">The existing transport used for participant counter
    /// notifications.</param>
    /// <param name="logger">The logger, which must not record capabilities or document
    /// contents.</param>
    /// <param name="timeProvider">The clock used by admission, rate windows and
    /// deadlines; null uses the system clock.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required dependency is
    /// null.</exception>
    public ObserverSessions(IOptions<ApplicationConfiguration> settings,
        IRoomRegistry registry,
        IHubContext<ObserverHub> hub, IHubContext<RoomHub> editors,
            ILogger<ObserverSessions> logger,
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
        _timeProvider = timeProvider ?? TimeProvider.System;
        _publication = new ObserverPublication(_gate, _registry, _editors, Enqueue,
            _connections);
        _delivery = new ObserverDelivery(_gate, _limits, _timeProvider, _hub, _logger,
            IsCurrent, Disconnect, _publication.CreateSnapshot);
    }

    /// <summary>
    /// Creates or rotates a capability, retaining its admission hash and an encrypted
    /// owner copy.
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
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            presentation.TokenHash = Hash(token);
            presentation.ProtectedToken = _linkProtector.Protect(token);
            _tokens.Add(presentation.TokenHash, presentation);
            return "/watch#" + token;
        }
    }

    /// <summary>
    /// Invalidates the token before terminating existing subscriptions.
    /// </summary>
    /// <param name="roomId">The room whose guest capability and sessions are
    /// revoked.</param>
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
    /// Recovers the current guest URL for the owner without rotating its capability or
    /// sessions.
    /// </summary>
    /// <param name="roomId">The room whose guest access is inspected.</param>
    /// <returns>The relative guest URL when a capability exists; otherwise,
    /// null.</returns>
    public string? GetLink(RoomId roomId)
    {
        lock (_gate)
        {
            return _rooms.TryGetValue(roomId.Value, out var room)
                && room.ProtectedToken is not null
                ? "/watch#" + _linkProtector.Unprotect(room.ProtectedToken) : null;
        }
    }

    /// <summary>
    /// Returns additive, revisioned viewer presence to an editor.
    /// </summary>
    /// <param name="roomId">The room whose observer presence is read.</param>
    /// <returns>The observer connection count and its independent monotonic
    /// revision.</returns>
    public (int Count, long Revision) Count(RoomId roomId)
    {
        lock (_gate)
        {
            return _rooms.TryGetValue(roomId.Value, out var room) ? (room.Count,
                room.CountRevision) : (0, 0);
        }
    }

    /// <summary>
    /// Records a committed immutable update before editor broadcast; never waits for
    /// guest I/O.
    /// </summary>
    /// <param name="roomId">The room containing the committed edit.</param>
    /// <param name="update">The immutable, accepted Base64 Yjs update.</param>
    /// <param name="revision">The server-assigned document revision of this
    /// update.</param>
    /// <exception cref="ArgumentNullException">Thrown when the update is
    /// null.</exception>
    public void RecordUpdate(RoomId roomId, string update, long revision)
    {
        ArgumentNullException.ThrowIfNull(update);
        lock (_gate)
        {
            if (!_rooms.TryGetValue(roomId.Value, out var room) || room.Count == 0
                || revision <= room.LastDocument)
            {
                return;
            }
            // Retain a bounded reference to the existing Base64 argument, so a checkpoint
            // cannot prune changes between delivery ticks. No document data is copied.
            if (update.Length > _limits.DeliveryQueueBytesPerConnection
                || room.PendingBytes + update.Length
                    > _limits.DeliveryQueueBytesPerConnection
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
    /// <exception cref="ArgumentNullException">Thrown when the context is
    /// null.</exception>
    /// <exception cref="HubException">Thrown when guest access is disabled or admission
    /// capacity is exhausted.</exception>
    public void Connect(HubCallerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var source
            = ConnectionSource.From(context.GetHttpContext()?.Connection.RemoteIpAddress);
        ObserverGuest guest;
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
            guest.Pump = Task.Run(() => _delivery.DeliverAsync(guest));
            _ = _workers.RemoveWhere(task => task.IsCompleted);
            _ = _workers.Add(guest.Pump);
        }
        _ = _delivery.ExpireUnjoinedAsync(guest);
    }

    /// <summary>
    /// Joins using a capability, never a client-supplied room identifier.
    /// </summary>
    /// <param name="context">The admitted observer connection.</param>
    /// <param name="token">The opaque read-only capability; it must not be
    /// logged.</param>
    /// <exception cref="ArgumentNullException">Thrown when the context is
    /// null.</exception>
    /// <exception cref="HubException">Thrown when the capability, membership or room
    /// capacity check fails.</exception>
    public void Join(HubCallerContext context, string token)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (_gate)
        {
            var guest = GetConnection(context.ConnectionId);
            if (token is null || token.Length != 43 || !_tokens.TryGetValue(Hash(token),
                out var room))
            {
                throw Error("ObserverUnavailable");
            }

            EnsureOpen(room);
            if (guest.Room is not null)
            {
                if (ReferenceEquals(guest.Room, room)
                    && guest.Generation == room.Generation)
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
            {
                _delivery.ScheduleSnapshot(guest);
            }
            catch
            {
                LeaveCore(guest);
                throw;
            }
        }
    }

    /// <summary>
    /// Rate-limited recovery of the current authorized room.
    /// </summary>
    /// <param name="connectionId">The joined observer connection requesting
    /// recovery.</param>
    /// <exception cref="HubException">Thrown when authorization or the synchronization
    /// rate limit fails.</exception>
    public void Sync(string connectionId)
    {
        lock (_gate)
        {
            var guest = RequireJoined(connectionId);
            _delivery.ScheduleSnapshot(guest);
        }
    }

    /// <summary>
    /// Reads only revisions and the viewer count, without disclosing room capabilities.
    /// </summary>
    /// <param name="connectionId">The joined observer connection whose room is
    /// read.</param>
    /// <returns>The authorized room revisions and observer counter, without editor
    /// capabilities.</returns>
    /// <exception cref="HubException">Thrown when the connection is not authorized to
    /// observe an open room.</exception>
    public object Revisions(string connectionId)
    {
        lock (_gate)
        {
            var guest = RequireJoined(connectionId);
            var room = guest.Room!;
            return new ObserverRevisions(room.State.GetRevisions(), room.Count,
                room.CountRevision);
        }
    }

    /// <summary>
    /// Enforces a bounded per-connection invocation window.
    /// </summary>
    /// <param name="connectionId">The admitted connection invoking a guest
    /// method.</param>
    /// <exception cref="HubException">Thrown when admission is absent or the invocation
    /// rate is exceeded.</exception>
    public void CheckInvocation(string connectionId)
    {
        lock (_gate)
        {
            var guest = GetConnection(connectionId);
            var second = _timeProvider.GetTimestamp() / _timeProvider.TimestampFrequency;
            if (guest.InvocationSecond != second)
            {
                guest.InvocationSecond = second;
                guest.Invocations = 0;
            }
            if (++guest.Invocations > _limits.InvocationsPerSecond)
            {
                throw Error("RateLimited");
            }
        }
    }

    /// <summary>
    /// Leaves without freeing a still-connected transport slot.
    /// </summary>
    /// <param name="connectionId">The connection leaving its current observer
    /// membership.</param>
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
    /// <param name="connectionId">The connection whose transport reservations are
    /// released.</param>
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

    /// <inheritdoc />
    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_limits.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500),
            _timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                ObserverPresentation[] rooms;
                lock (_gate)
                {
                    rooms = [.. _rooms.Values];
                }

                foreach (var room in rooms)
                {
                    try
                    {
                        await _publication.PublishAsync(room, stoppingToken)
                            .ConfigureAwait(false);
                    }
                    catch (RoomNotFoundException)
                    {
                        lock (_gate)
                        {
                            Revoke(room);
                            _ = _rooms.Remove(room.State.RoomId.Value);
                        }
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException
                        and not OperationCanceledException)
                    {
                        lock (_gate)
                        {
                            room.CountDirty = true;
                        }

                        _logger.LogDebug("Observer publication deferred ({FailureType})",
                            ex.GetType().Name);
                    }
                }
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        { }
        finally
        {
            ObserverGuest[] guests;
            lock (_gate)
            {
                guests = [.. _connections.Values];
            }

            foreach (var guest in guests)
            {
                guest.Context.Abort();
                Disconnect(guest.Context.ConnectionId);
            }
            await Task.WhenAll(guests.Select(guest => guest.Pump)).ConfigureAwait(false);
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
        _delivery.Dispose();
    }

    private static string Hash(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private ObserverGuest GetConnection(string id) => _connections.TryGetValue(id,
        out var guest) ? guest : throw Error("ObserverUnavailable");
    private ObserverGuest RequireJoined(string id)
    {
        var guest = GetConnection(id);
        if (!IsCurrent(guest, guest.Room, guest.Generation))
        {
            throw Error("ObserverUnavailable");
        }

        EnsureOpen(guest.Room!);
        return guest;
    }
    private static void EnsureOpen(ObserverPresentation room)
    {
        try
        {
            _ = room.State.GetRevisions();
        }
        catch (RoomNotFoundException)
        {
            throw Error("ObserverUnavailable");
        }
    }
    private bool IsCurrent(ObserverGuest guest, ObserverPresentation? room,
        long generation) => room is not null
        && ReferenceEquals(guest.Room, room) && room.TokenHash is not null
            && room.Generation == generation
        && _connections.ContainsKey(guest.Context.ConnectionId);
    private ObserverPresentation GetPresentation(RoomState state)
    {
        if (!_rooms.TryGetValue(state.RoomId.Value, out var room))
        {
            room = new(state);
            _rooms.Add(state.RoomId.Value, room);
        }
        return room;
    }
    private void Revoke(ObserverPresentation room)
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
        foreach (var guest in _connections.Values.Where(guest
            => ReferenceEquals(guest.Room, room)).ToArray())
        {
            // Abort also releases transport buffers and interrupts an in-flight snapshot.
            guest.Context.Abort();
            Disconnect(guest.Context.ConnectionId);
        }
    }
    private static void LeaveCore(ObserverGuest guest)
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

    private void Enqueue(ObserverGuest guest, ObserverPayload payload, int bytes)
        => _delivery.Enqueue(guest, payload, bytes);

    private readonly Lock _gate = new();
    private readonly IDataProtector _linkProtector = new EphemeralDataProtectionProvider()
        .CreateProtector("SealCode.ObserverLinks");
    private readonly Dictionary<string, ObserverGuest> _connections
        = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ObserverPresentation> _rooms
        = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ObserverPresentation> _tokens
        = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _sources = new(StringComparer.Ordinal);
    private readonly HashSet<Task> _workers = [];
    private readonly (long Minute, int Count)[] _sourceWindows = new (long, int)[256];
    private readonly ObserverLimits _limits;
    private readonly IRoomRegistry _registry;
    private readonly IHubContext<ObserverHub> _hub;
    private readonly IHubContext<RoomHub> _editors;
    private readonly ILogger<ObserverSessions> _logger;
    private readonly ObserverDelivery _delivery;
    private readonly ObserverPublication _publication;
    private readonly TimeProvider _timeProvider;

}
