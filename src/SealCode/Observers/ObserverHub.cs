namespace SealCode.Observers;

/// <summary>
/// Capability-only, read-only transport. No editor methods are exposed.
/// </summary>
#pragma warning disable CA1515 // Public hub is exercised by integration tests.
public sealed class ObserverHub : Microsoft.AspNetCore.SignalR.Hub
#pragma warning restore CA1515
{
    /// <summary>
    /// Initializes the transport with the shared observer session service.
    /// </summary>
    /// <param name="sessions">The process-wide observer session service.</param>
    /// <exception cref="ArgumentNullException">Thrown when the session service is null.</exception>
    public ObserverHub(ObserverSessions sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        _sessions = sessions;
    }

    /// <summary>
    /// Reserves a guest slot and schedules an initial snapshot.
    /// </summary>
    /// <param name="token">The opaque read-only capability from the guest link.</param>
    public void JoinObserver(string token) => _sessions.Join(Context, token);
    /// <summary>
    /// Schedules recovery without accepting any client document data.
    /// </summary>
    public void SyncObserver() => _sessions.Sync(Context.ConnectionId);
    /// <summary>
    /// Returns revisions only for the capability bound to this connection.
    /// </summary>
    /// <returns>The document revisions and independent observer count.</returns>
    public object GetObserverRevisions() => _sessions.Revisions(Context.ConnectionId);
    /// <summary>
    /// Releases guest membership without disconnecting the transport.
    /// </summary>
    public void LeaveObserver() => _sessions.Leave(Context.ConnectionId);
    /// <inheritdoc />
    public override Task OnConnectedAsync()
    {
        _sessions.Connect(Context);
        return base.OnConnectedAsync();
    }
    /// <inheritdoc />
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _sessions.Disconnect(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    private readonly ObserverSessions _sessions;
}
