using Microsoft.AspNetCore.SignalR;
using Abstractions;
using Models;
using System.Threading;

namespace Transport;

/// <summary>
/// SignalR-based notifier for room events.
/// </summary>
public sealed class SignalRRoomNotifier : IRoomNotifier
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SignalRRoomNotifier"/> class.
    /// </summary>
    /// <param name="hub">The hub context.</param>
    public SignalRRoomNotifier(IHubContext<RoomHub> hub)
    {
        ArgumentNullException.ThrowIfNull(hub);
        _hub = hub;
    }

    /// <inheritdoc />
    public Task RoomKilledAsync(RoomId roomId, RoomDeletionReason reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(roomId.Value))
        {
            throw new ArgumentException("Room id is required", nameof(roomId));
        }

        if (string.IsNullOrWhiteSpace(reason.Value))
        {
            throw new ArgumentException("Reason is required", nameof(reason));
        }

        return _hub.Clients.Group(roomId.Value).SendAsync("RoomKilled", reason.Value, cancellationToken);
    }

    private readonly IHubContext<RoomHub> _hub;
}
