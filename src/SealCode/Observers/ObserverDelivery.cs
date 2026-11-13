using System.Diagnostics;

using Microsoft.AspNetCore.SignalR;

using Models.Configuration;

using Transport;
using Transport.Models;

using static SealCode.Observers.ObserverEncoding;
using static SealCode.Observers.ObserverSessionErrors;

namespace SealCode.Observers;

/// <summary>Owns bounded guest queues and independent transport workers.</summary>
internal sealed class ObserverDelivery : IDisposable
{
    /// <summary>Creates delivery workers sharing the session gate.</summary>
    /// <param name="gate">The gate shared by all observer components.</param>
    /// <param name="limits">The observer resource budgets.</param>
    /// <param name="timeProvider">The admission and deadline clock.</param>
    /// <param name="hub">The guest transport.</param>
    /// <param name="logger">The privacy-preserving worker logger.</param>
    /// <param name="isCurrent">Checks membership while the gate is held.</param>
    /// <param name="disconnect">Synchronously releases a transport reservation.</param>
    /// <param name="createSnapshot">Captures an immutable guest snapshot.</param>
    public ObserverDelivery(Lock gate, ObserverLimits limits, TimeProvider timeProvider,
        IHubContext<ObserverHub> hub, ILogger<ObserverSessions> logger,
        Func<ObserverGuest, ObserverPresentation?, long, bool> isCurrent,
            Action<string> disconnect,
        Func<ObserverPresentation, ObserverPayload> createSnapshot)
    {
        _gate = gate;
        _limits = limits;
        _timeProvider = timeProvider;
        _hub = hub;
        _logger = logger;
        _isCurrent = isCurrent;
        _disconnect = disconnect;
        _createSnapshot = createSnapshot;
        _snapshots = new(limits.ConcurrentSnapshots);
    }

    /// <inheritdoc />
    public void Dispose() => _snapshots.Dispose();

    /// <summary>Queues a rate-limited snapshot recovery job.</summary>
    /// <param name="guest">The admitted guest whose state is guarded by the gate.</param>
    public void ScheduleSnapshot(ObserverGuest guest)
    {
        if (guest.SnapshotPending)
        {
            return;
        }

        var minute = _timeProvider.GetUtcNow().ToUnixTimeSeconds() / 60;
        if (guest.SnapshotMinute != minute)
        {
            guest.SnapshotMinute = minute;
            guest.SnapshotRequests = 0;
        }
        if (++guest.SnapshotRequests > _limits.SnapshotRequestsPerConnectionPerMinute)
        {
            throw Error("RateLimited");
        }

        guest.SnapshotPending = true;
        guest.NeedsResync = false;
        // Snapshot jobs contain no document data until they acquire the global delivery
        // permit.
        while (guest.Queue.Reader.TryRead(out _))
        { }
        guest.QueuedBytes = 0;
        if (!guest.Queue.Writer.TryWrite(new(guest.Room!, guest.Generation, null, 0)))
        {
            guest.SnapshotPending = false;
            throw Error("Busy");
        }
    }

    /// <summary>Queues an update or a resynchronization marker on overflow.</summary>
    /// <param name="guest">The admitted guest whose state is guarded by the gate.</param>
    /// <param name="payload">The immutable message to deliver.</param>
    /// <param name="bytes">The message size charged against the queue budget.</param>
    public void Enqueue(ObserverGuest guest, ObserverPayload payload, int bytes)
    {
        if (guest.NeedsResync)
        {
            return;
        }

        if (bytes > _limits.DeliveryQueueBytesPerConnection
            || guest.QueuedBytes + bytes > _limits.DeliveryQueueBytesPerConnection
            || !guest.Queue.Writer.TryWrite(new(guest.Room!, guest.Generation, payload,
                bytes)))
        {
            while (guest.Queue.Reader.TryRead(out _))
            { }
            guest.QueuedBytes = 0;
            guest.SnapshotPending = false;
            guest.NeedsResync = true;
            ObserverMetrics.Resyncs.Add(1);
            _ = guest.Queue.Writer.TryWrite(new(guest.Room!, guest.Generation,
                Encode(new ObserverRequiresResync("RequiresResync")), 0));
            return;
        }
        guest.QueuedBytes += bytes;
    }

    /// <summary>Expires admitted transports that never join a presentation.</summary>
    /// <param name="guest">The admitted guest whose state is guarded by the gate.</param>
    /// <returns>A task representing the independent guest worker.</returns>
    public async Task ExpireUnjoinedAsync(ObserverGuest guest)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(_limits.UnjoinedTimeoutSeconds),
                _timeProvider, guest.Stop.Token).ConfigureAwait(false);
            lock (_gate)
            {
                if (guest.Room is null)
                {
                    guest.Context.Abort();
                    _disconnect(guest.Context.ConnectionId);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Pumps guest messages independently of editor delivery.</summary>
    /// <param name="guest">The admitted guest whose state is guarded by the gate.</param>
    /// <returns>A task representing the independent guest worker.</returns>
    public async Task DeliverAsync(ObserverGuest guest)
    {
        try
        {
            await foreach (var delivery in guest.Queue.Reader
                .ReadAllAsync(guest.Stop.Token).ConfigureAwait(false))
            {
                lock (_gate)
                {
                    guest.QueuedBytes = Math.Max(0, guest.QueuedBytes - delivery.Bytes);
                    if (!_isCurrent(guest, delivery.Room, delivery.Generation))
                    {
                        continue;
                    }
                }
                if (delivery.Payload is null)
                {
                    // Includes serialization and the entire backpressured transport
                    // write.
                    await _snapshots.WaitAsync(guest.Stop.Token).ConfigureAwait(false);
                    var started = Stopwatch.GetTimestamp();
                    try
                    {
                        ObserverPayload payload;
                        lock (_gate)
                        {
                            if (!_isCurrent(guest, delivery.Room, delivery.Generation))
                            {
                                continue;
                            }
                        }
                        payload = _createSnapshot(delivery.Room);
                        await SendAsync(guest, delivery, payload).ConfigureAwait(false);
                        ObserverPayload counter;
                        lock (_gate)
                        {
                            counter = Encode(new ObserverCountMessage("Count",
                                delivery.Room.Count, delivery.Room.CountRevision));
                        }

                        await SendAsync(guest, delivery, counter).ConfigureAwait(false);
                        ObserverMetrics.Snapshots.Add(1);
                    }
                    finally
                    {
                        _ = _snapshots.Release();
                        ObserverMetrics.SnapshotDuration.Record(
                            Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                        lock (_gate)
                        {
                            guest.SnapshotPending = false;
                        }
                    }
                }
                else
                {
                    await SendAsync(guest, delivery, delivery.Payload)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Do not log capability arguments or document payloads.
            _logger.LogDebug("Observer delivery ended ({FailureType})",
                ex.GetType().Name);
        }
        finally
        {
            guest.Context.Abort();
            _disconnect(guest.Context.ConnectionId);
            guest.Stop.Dispose();
        }
    }

    private async Task SendAsync(ObserverGuest guest, ObserverDeliveryItem delivery,
        ObserverPayload payload)
    {
        lock (_gate)
        {
            if (!_isCurrent(guest, delivery.Room, delivery.Generation))
            {
                return;
            }
        }

        using var deadline
            = new CancellationTokenSource(
                TimeSpan.FromSeconds(_limits.SendTimeoutSeconds), _timeProvider);
        using var timeout
            = CancellationTokenSource.CreateLinkedTokenSource(guest.Stop.Token,
            deadline.Token);
        try
        {
            await _hub.Clients.Client(guest.Context.ConnectionId)
                .SendAsync(HubContract.ObserverEvents.ObserverMessage, payload.Value,
                timeout.Token).ConfigureAwait(false);
            ObserverMetrics.Bytes.Add(payload.Bytes);
        }
        catch (OperationCanceledException) when (!guest.Stop.IsCancellationRequested)
        {
            ObserverMetrics.SlowConsumers.Add(1);
            throw;
        }
    }

    private readonly Lock _gate;
    private readonly ObserverLimits _limits;
    private readonly TimeProvider _timeProvider;
    private readonly IHubContext<ObserverHub> _hub;
    private readonly ILogger<ObserverSessions> _logger;
    private readonly SemaphoreSlim _snapshots;
    private readonly Func<ObserverGuest, ObserverPresentation?, long, bool> _isCurrent;
    private readonly Action<string> _disconnect;
    private readonly Func<ObserverPresentation, ObserverPayload> _createSnapshot;
}
