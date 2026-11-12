namespace SealCode.Observers;

/// <summary>Capability-only, read-only transport. No editor methods are exposed.</summary>
#pragma warning disable CA1515 // Public hub is exercised by integration tests.
public sealed class ObserverHub(ObserverSessions sessions) : Microsoft.AspNetCore.SignalR.Hub
{
    /// <summary>Reserves a guest slot and schedules an initial snapshot.</summary>
    public void JoinObserver(string token) => sessions.Join(Context, token);
    /// <summary>Schedules recovery without accepting any client document data.</summary>
    public void SyncObserver() => sessions.Sync(Context.ConnectionId);
    /// <summary>Returns revisions only for the capability bound to this connection.</summary>
    public object GetObserverRevisions() => sessions.Revisions(Context.ConnectionId);
    /// <summary>Releases guest membership without disconnecting the transport.</summary>
    public void LeaveObserver() => sessions.Leave(Context.ConnectionId);
    /// <inheritdoc />
    public override Task OnConnectedAsync()
    {
        sessions.Connect(Context);
        return base.OnConnectedAsync();
    }
    /// <inheritdoc />
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        sessions.Disconnect(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
