using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using SealCode.Domain;
using SealCode.Services;

namespace SealCode.Hubs;

public sealed class RoomHub : Hub
{
    private readonly RoomRegistry _registry;
    private readonly IOptions<AppSettings> _settings;
    private readonly ILogger<RoomHub> _logger;

    public RoomHub(RoomRegistry registry, IOptions<AppSettings> settings, ILogger<RoomHub> logger)
    {
        _registry = registry;
        _settings = settings;
        _logger = logger;
    }

    public async Task<JoinRoomResult> JoinRoom(string roomId, string displayName)
    {
        if (!_registry.TryGetRoom(roomId, out var room))
        {
            throw new HubException("Room not found");
        }

        displayName = (displayName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new HubException("Display name required");
        }

        var connectionId = Context.ConnectionId;
        var maxUsers = Math.Clamp(_settings.Value.MaxUsersPerRoom, 1, 5);

        string[] usersSnapshot;
        string roomName;
        string language;
        string text;
        int version;

        lock (room)
        {
            var alreadyInRoom = room.ConnectedUsers.ContainsKey(connectionId);
            if (!alreadyInRoom && room.ConnectedUsers.Count >= maxUsers)
            {
                throw new HubException($"Room is full (max {maxUsers})");
            }

            room.ConnectedUsers[connectionId] = displayName;
            usersSnapshot = room.ConnectedUsers.Values.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
            roomName = room.Name;
            language = room.Language;
            text = room.Text;
            version = room.Version;
        }

        Context.Items["roomId"] = roomId;
        Context.Items["displayName"] = displayName;

        await Groups.AddToGroupAsync(connectionId, roomId);
        await Clients.GroupExcept(roomId, connectionId)
            .SendAsync("UserJoined", displayName, usersSnapshot);

        _logger.LogInformation("User joined {RoomId} ({Name}) as {DisplayName}", roomId, roomName, displayName);

        return new JoinRoomResult(roomName, language, text, version, usersSnapshot);
    }

    public async Task UpdateText(string roomId, string newText, int clientVersion)
    {
        if (!_registry.TryGetRoom(roomId, out var room))
        {
            throw new HubException("Room not found");
        }

        string author;
        int newVersion;
        lock (room)
        {
            room.Text = newText ?? string.Empty;
            room.Version++;
            room.LastUpdatedUtc = DateTime.UtcNow;
            newVersion = room.Version;
            author = room.ConnectedUsers.TryGetValue(Context.ConnectionId, out var name)
                ? name
                : "unknown";
        }

        await Clients.GroupExcept(roomId, Context.ConnectionId)
            .SendAsync("TextUpdated", newText ?? string.Empty, newVersion, author);
    }

    public async Task SetLanguage(string roomId, string language)
    {
        if (!_registry.TryGetRoom(roomId, out var room))
        {
            throw new HubException("Room not found");
        }

        language = (language ?? "").Trim().ToLowerInvariant();
        if (language != "csharp" && language != "sql")
        {
            throw new HubException("Invalid language");
        }

        int newVersion;
        lock (room)
        {
            room.Language = language;
            room.Version++;
            room.LastUpdatedUtc = DateTime.UtcNow;
            newVersion = room.Version;
        }

        await Clients.Group(roomId).SendAsync("LanguageUpdated", language, newVersion);
    }

    public async Task UpdateCursor(string roomId, int position)
    {
        if (!_registry.TryGetRoom(roomId, out var room))
        {
            throw new HubException("Room not found");
        }

        string? author = null;
        lock (room)
        {
            if (room.ConnectedUsers.TryGetValue(Context.ConnectionId, out var name))
            {
                author = name;
            }
        }

        if (author is null)
        {
            return;
        }

        await Clients.GroupExcept(roomId, Context.ConnectionId)
            .SendAsync("CursorUpdated", author, position);
    }

    public async Task LeaveRoom(string roomId)
    {
        await RemoveFromRoomAsync(roomId, Context.ConnectionId, notify: true);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue("roomId", out var roomIdObj) && roomIdObj is string roomId)
        {
            await RemoveFromRoomAsync(roomId, Context.ConnectionId, notify: true);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task RemoveFromRoomAsync(string roomId, string connectionId, bool notify)
    {
        if (!_registry.TryGetRoom(roomId, out var room))
        {
            return;
        }

        string? displayName = null;
        string[] usersSnapshot = Array.Empty<string>();

        lock (room)
        {
            if (room.ConnectedUsers.TryGetValue(connectionId, out var name))
            {
                displayName = name;
                room.ConnectedUsers.Remove(connectionId);
                usersSnapshot = room.ConnectedUsers.Values.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
            }
        }

        await Groups.RemoveFromGroupAsync(connectionId, roomId);

        if (notify && displayName is not null)
        {
            await Clients.Group(roomId).SendAsync("UserLeft", displayName, usersSnapshot);
            _logger.LogInformation("User left {RoomId} as {DisplayName}", roomId, displayName);
        }
    }

    public sealed record JoinRoomResult(string Name, string Language, string Text, int Version, string[] Users);
}
