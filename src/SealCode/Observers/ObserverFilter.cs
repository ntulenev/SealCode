using Microsoft.AspNetCore.SignalR;

namespace SealCode.Observers;

/// <summary>
/// Rate limits every observer invocation, including unknown capabilities.
/// </summary>
internal sealed class ObserverFilter : IHubFilter
{
    /// <summary>
    /// Initializes the filter with the shared per-connection limiter.
    /// </summary>
    /// <param name="sessions">The process-wide observer session service.</param>
    public ObserverFilter(ObserverSessions sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        _sessions = sessions;
    }

    /// <inheritdoc />
    public ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocation, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        _sessions.CheckInvocation(invocation.Context.ConnectionId);
        return next(invocation);
    }

    private readonly ObserverSessions _sessions;
}
