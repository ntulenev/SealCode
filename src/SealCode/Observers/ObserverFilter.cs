using Microsoft.AspNetCore.SignalR;

namespace SealCode.Observers;

/// <summary>Rate limits every observer invocation, including unknown capabilities.</summary>
internal sealed class ObserverFilter(ObserverSessions sessions) : IHubFilter
{
    public ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocation, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        sessions.CheckInvocation(invocation.Context.ConnectionId);
        return next(invocation);
    }
}
