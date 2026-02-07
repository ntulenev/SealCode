using Abstractions;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Models;
using Models.Configuration;

namespace Transport;

public sealed class RoomHub : Hub
{
    public RoomHub(IRoomRegistry registry, IOptions<ApplicationConfiguration> settings, ILogger<RoomHub> logger)
    {
        _registry = registry;
        _settings = settings;
        _logger = logger;
    }

#pragma warning disable IDE1006 // Naming Styles
    public async Task<JoinRoomResult> JoinRoom(string roomId, string displayName)
#pragma warning restore IDE1006 // Naming Styles
    {
        if (!_registry.TryGetRoom(new RoomId(roomId), out var room))
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

            room.ConnectedUsers[connectionId] = new DisplayName(displayName);
            usersSnapshot = [.. room.ConnectedUsers.Values.Select(x => x.Value).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)];
            roomName = room.Name.Value;
            language = room.Language.Value;
            text = room.Text.Value;
            version = room.Version.Value;
        }

        Context.Items["roomId"] = roomId;
        Context.Items["displayName"] = displayName;

        await Groups.AddToGroupAsync(connectionId, roomId);
        await Clients.GroupExcept(roomId, connectionId)
            .SendAsync("UserJoined", displayName, usersSnapshot);

#pragma warning disable CA1873 // Avoid potentially expensive logging
        _logger.LogInformation("User joined {RoomId} ({Name}) as {DisplayName}", roomId, roomName, displayName);
#pragma warning restore CA1873 // Avoid potentially expensive logging

        return new JoinRoomResult(roomName, language, text, version, usersSnapshot);
    }

#pragma warning disable IDE1006 // Naming Styles
#pragma warning disable IDE0060 // Remove unused parameter
    public async Task UpdateText(string roomId, string newText, int clientVersion)
#pragma warning restore IDE0060 // Remove unused parameter
#pragma warning restore IDE1006 // Naming Styles
    {
        if (!_registry.TryGetRoom(new RoomId(roomId), out var room))
        {
            throw new HubException("Room not found");
        }

        string author;
        int newVersion;
        lock (room)
        {
            var text = newText ?? string.Empty;
            room.Text = new(text);
            room.Version = room.Version.Next();
            room.LastUpdatedUtc = DateTime.UtcNow;
            newVersion = room.Version.Value;
            author = room.ConnectedUsers.TryGetValue(Context.ConnectionId, out var name)
                ? name.Value

                : "unknown";
        }

        await Clients.GroupExcept(roomId, Context.ConnectionId)
            .SendAsync("TextUpdated", newText ?? string.Empty, newVersion, author);
    }

#pragma warning disable IDE1006 // Naming Styles
    public async Task SetLanguage(string roomId, string language)
#pragma warning restore IDE1006 // Naming Styles
    {
        if (!_registry.TryGetRoom(new RoomId(roomId), out var room))
        {
            throw new HubException("Room not found");
        }

        language = (language ?? "").Trim().ToLowerInvariant();
#pragma warning disable IDE0078 // Use pattern matching
        if (language != "csharp" && language != "sql")
        {
            throw new HubException("Invalid language");
        }
#pragma warning restore IDE0078 // Use pattern matching

        int newVersion;
        lock (room)
        {
            room.Language = new RoomLanguage(language);
            room.Version = room.Version.Next();
            room.LastUpdatedUtc = DateTime.UtcNow;
            newVersion = room.Version.Value;
        }

        await Clients.Group(roomId).SendAsync("LanguageUpdated", language, newVersion);
    }

#pragma warning disable IDE1006 // Naming Styles
    public async Task UpdateCursor(string roomId, int position)
#pragma warning restore IDE1006 // Naming Styles
    {
        if (!_registry.TryGetRoom(new RoomId(roomId), out var room))
        {
            throw new HubException("Room not found");
        }

        string? author = null;
        lock (room)
        {
            if (room.ConnectedUsers.TryGetValue(Context.ConnectionId, out var name))
            {
                author = name.Value;
            }
        }

        if (author is null)
        {
            return;
        }

        await Clients.GroupExcept(roomId, Context.ConnectionId)
            .SendAsync("CursorUpdated", author, position);
    }

#pragma warning disable IDE1006 // Naming Styles
    public async Task UpdateSelection(string roomId, bool isMultiLine)
#pragma warning restore IDE1006 // Naming Styles
    {
        if (!_registry.TryGetRoom(new RoomId(roomId), out var room))
        {
            throw new HubException("Room not found");
        }

        string? author = null;
        lock (room)
        {
            if (room.ConnectedUsers.TryGetValue(Context.ConnectionId, out var name))
            {
                author = name.Value;
            }
        }

        if (author is null)
        {
            return;
        }

        await Clients.Group(roomId).SendAsync("UserSelection", author, isMultiLine);
    }

#pragma warning disable IDE1006 // Naming Styles
    public async Task LeaveRoom(string roomId) => await RemoveFromRoomAsync(roomId, Context.ConnectionId, notify: true);

    public async override Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue("roomId", out var roomIdObj) && roomIdObj is string roomId)
        {
            await RemoveFromRoomAsync(roomId, Context.ConnectionId, notify: true);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task RemoveFromRoomAsync(string roomId, string connectionId, bool notify)
    {
        if (!_registry.TryGetRoom(new RoomId(roomId), out var room))
        {
            return;
        }

        string? displayName = null;
        string[] usersSnapshot = [];

        lock (room)
        {
            if (room.ConnectedUsers.TryGetValue(connectionId, out var name))
            {
                displayName = name.Value;
                _ = room.ConnectedUsers.Remove(connectionId);
                usersSnapshot = [.. room.ConnectedUsers.Values.Select(x => x.Value).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)];
            }
        }

        await Groups.RemoveFromGroupAsync(connectionId, roomId);

        if (notify && displayName is not null)
        {
            await Clients.Group(roomId).SendAsync("UserLeft", displayName, usersSnapshot);
#pragma warning disable CA1873 // Avoid potentially expensive logging
            _logger.LogInformation("User left {RoomId} as {DisplayName}", roomId, displayName);
#pragma warning restore CA1873 // Avoid potentially expensive logging
        }
    }

    private readonly IRoomRegistry _registry;
    private readonly IOptions<ApplicationConfiguration> _settings;
    private readonly ILogger<RoomHub> _logger;
}
