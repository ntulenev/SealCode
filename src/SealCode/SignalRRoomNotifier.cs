using Abstractions;

using Microsoft.AspNetCore.SignalR;

using Models;

namespace SealCode;

/// <summary>
/// SignalR-based notifier for room events.
/// </summary>
#pragma warning disable CA1515 // Need for testing in mocks
public sealed class SignalRRoomNotifier : IRoomNotifier
#pragma warning restore CA1515 
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
