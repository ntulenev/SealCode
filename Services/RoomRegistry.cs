using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using SealCode.Domain;
using SealCode.Hubs;

namespace SealCode.Services;

public sealed class RoomRegistry
{
    private readonly ConcurrentDictionary<string, RoomState> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<RoomRegistry> _logger;
    private readonly IHubContext<RoomHub> _hub;

    public RoomRegistry(ILogger<RoomRegistry> logger, IHubContext<RoomHub> hub)
    {
        _logger = logger;
        _hub = hub;
    }

    public IReadOnlyDictionary<string, RoomState> Rooms => _rooms;

    public bool TryGetRoom(string roomId, out RoomState room) => _rooms.TryGetValue(roomId, out room!);

    public RoomState CreateRoom(string name, string language)
    {
        var roomId = Guid.NewGuid().ToString("N");
        var room = new RoomState
        {
            RoomId = roomId,
            Name = name.Trim(),
            Language = language,
            Text = "",
            Version = 1,
            LastUpdatedUtc = DateTime.UtcNow
        };

        _rooms[roomId] = room;
        _logger.LogInformation("Room created {RoomId} ({Name})", roomId, room.Name);
        return room;
    }

    public async Task<bool> DeleteRoomAsync(string roomId, string reason)
    {
        if (!_rooms.TryRemove(roomId, out var room))
        {
            return false;
        }

        _logger.LogInformation("Room deleted {RoomId} ({Name})", roomId, room.Name);
        await _hub.Clients.Group(roomId).SendAsync("RoomKilled", reason);
        return true;
    }
}
