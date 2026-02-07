using Abstractions;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Models;
using Models.Configuration;

namespace Transport;

/// <summary>
/// SignalR hub for room interactions.
/// </summary>
public sealed class RoomHub : Hub
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoomHub"/> class.
    /// </summary>
    /// <param name="registry">The room registry.</param>
    /// <param name="settings">The application settings.</param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
    public RoomHub(IRoomRegistry registry, IOptions<ApplicationConfiguration> settings, ILogger<RoomHub> logger)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _registry = registry;
        _settings = settings;
        _logger = logger;
    }

#pragma warning disable IDE1006 // Naming Styles
    /// <summary>
    /// Joins the specified room with a display name.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <param name="displayName">The user display name.</param>
    /// <returns>The join result including the initial room state.</returns>
    /// <exception cref="HubException">Thrown when the room or inputs are invalid.</exception>
    public async Task<JoinRoomResult> JoinRoom(string roomId, string displayName)
#pragma warning restore IDE1006 // Naming Styles
    {
        if (string.IsNullOrWhiteSpace(roomId))
        {
            throw new HubException("Room id required");
        }

        if (!_registry.TryGetRoom(new RoomId(roomId), out var room))
        {
            throw new HubException("Room not found");
        }

        displayName = (displayName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new HubException("Display name required");
        }

        var connectionId = new ConnectionId(Context.ConnectionId);
        var maxUsers = Math.Clamp(_settings.Value.MaxUsersPerRoom, 1, 5);

        string[] usersSnapshot;
        string roomName;
        string language;
        string text;
        int version;

        try
        {
            room.AddUser(connectionId, new DisplayName(displayName), maxUsers);
        }
        catch (AddRoomUserException ex)
        {
            throw new HubException(ex.Message);
        }

        usersSnapshot = [.. room.ConnectedUsers.Values.Select(x => x.Value).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)];
        roomName = room.Name.Value;
        language = room.Language.Value;
        text = room.Text.Value;
        version = room.Version.Value;

        Context.Items["roomId"] = roomId;
        Context.Items["displayName"] = displayName;

        await Groups.AddToGroupAsync(connectionId.Value, roomId);
        await Clients.GroupExcept(roomId, connectionId.Value)
            .SendAsync("UserJoined", displayName, usersSnapshot);

        _logger.LogInformation("User joined {RoomId} ({Name}) as {DisplayName}", roomId, roomName, displayName);

        return new JoinRoomResult(roomName, language, text, version, usersSnapshot);
    }

#pragma warning disable IDE1006 // Naming Styles
#pragma warning disable IDE0060 // Remove unused parameter
    /// <summary>
    /// Updates the room text.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <param name="newText">The new text.</param>
    /// <param name="clientVersion">The client version.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="HubException">Thrown when inputs are invalid or the room is not found.</exception>
    public async Task UpdateText(string roomId, string newText, int clientVersion)
#pragma warning restore IDE0060 // Remove unused parameter
#pragma warning restore IDE1006 // Naming Styles
    {
        if (string.IsNullOrWhiteSpace(roomId))
        {
            throw new HubException("Room id required");
        }

        if (clientVersion < 0)
        {
            throw new HubException("Invalid client version");
        }

        if (!_registry.TryGetRoom(new RoomId(roomId), out var room))
        {
            throw new HubException("Room not found");
        }

        string author;
        int newVersion;
        var text = newText ?? string.Empty;
        newVersion = room.UpdateText(new(text), DateTimeOffset.UtcNow).Value;
        author = room.TryGetDisplayName(new ConnectionId(Context.ConnectionId), out var name)
            ? name.Value

            : "unknown";

        await Clients.GroupExcept(roomId, Context.ConnectionId)
            .SendAsync("TextUpdated", newText ?? string.Empty, newVersion, author);
    }

#pragma warning disable IDE1006 // Naming Styles
    /// <summary>
    /// Sets the room language.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <param name="language">The language to set.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="HubException">Thrown when inputs are invalid or the room is not found.</exception>
    public async Task SetLanguage(string roomId, string language)
#pragma warning restore IDE1006 // Naming Styles
    {
        if (string.IsNullOrWhiteSpace(roomId))
        {
            throw new HubException("Room id required");
        }

        if (string.IsNullOrWhiteSpace(language))
        {
            throw new HubException("Language required");
        }

        if (!_registry.TryGetRoom(new RoomId(roomId), out var room))
        {
            throw new HubException("Room not found");
        }

        language = (language ?? "").Trim().ToLowerInvariant();
        if (language is not "csharp" and not "sql")
        {
            throw new HubException("Invalid language");
        }


        int newVersion;
        newVersion = room.UpdateLanguage(new RoomLanguage(language), DateTimeOffset.UtcNow).Value;

        await Clients.Group(roomId).SendAsync("LanguageUpdated", language, newVersion);
    }

#pragma warning disable IDE1006 // Naming Styles
    /// <summary>
    /// Updates the cursor position for the current user.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <param name="position">The cursor position.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="HubException">Thrown when inputs are invalid or the room is not found.</exception>
    public async Task UpdateCursor(string roomId, int position)
#pragma warning restore IDE1006 // Naming Styles
    {
        if (string.IsNullOrWhiteSpace(roomId))
        {
            throw new HubException("Room id required");
        }

        if (position < 0)
        {
            throw new HubException("Invalid cursor position");
        }

        if (!_registry.TryGetRoom(new RoomId(roomId), out var room))
        {
            throw new HubException("Room not found");
        }

        string? author = null;
        if (room.TryGetDisplayName(new ConnectionId(Context.ConnectionId), out var name))
        {
            author = name.Value;
        }

        if (author is null)
        {
            return;
        }

        await Clients.GroupExcept(roomId, Context.ConnectionId)
            .SendAsync("CursorUpdated", author, position);
    }

#pragma warning disable IDE1006 // Naming Styles
    /// <summary>
    /// Updates the selection state for the current user.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <param name="isMultiLine">True if selection spans multiple lines; otherwise false.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="HubException">Thrown when inputs are invalid or the room is not found.</exception>
    public async Task UpdateSelection(string roomId, bool isMultiLine)
#pragma warning restore IDE1006 // Naming Styles
    {
        if (string.IsNullOrWhiteSpace(roomId))
        {
            throw new HubException("Room id required");
        }

        if (!_registry.TryGetRoom(new RoomId(roomId), out var room))
        {
            throw new HubException("Room not found");
        }

        string? author = null;
        if (room.TryGetDisplayName(new ConnectionId(Context.ConnectionId), out var name))
        {
            author = name.Value;
        }

        if (author is null)
        {
            return;
        }

        await Clients.Group(roomId).SendAsync("UserSelection", author, isMultiLine);
    }

#pragma warning disable IDE1006 // Naming Styles
    /// <summary>
    /// Leaves the specified room.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="HubException">Thrown when inputs are invalid.</exception>
    public async Task LeaveRoom(string roomId)
    {
        if (string.IsNullOrWhiteSpace(roomId))
        {
            throw new HubException("Room id required");
        }

        await RemoveFromRoomAsync(roomId, new ConnectionId(Context.ConnectionId), notify: true);
    }

    /// <summary>
    /// Handles client disconnection.
    /// </summary>
    /// <param name="exception">The disconnect exception, if any.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async override Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue("roomId", out var roomIdObj) && roomIdObj is string roomId)
        {
            await RemoveFromRoomAsync(roomId, new ConnectionId(Context.ConnectionId), notify: true);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task RemoveFromRoomAsync(string roomId, ConnectionId connectionId, bool notify)
    {
        if (!_registry.TryGetRoom(new RoomId(roomId), out var room))
        {
            return;
        }

        string? displayName = null;
        string[] usersSnapshot = [];

        if (room.RemoveUser(connectionId, out var name))
        {
            displayName = name.Value;
            usersSnapshot = [.. room.ConnectedUsers.Values.Select(x => x.Value).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)];
        }

        await Groups.RemoveFromGroupAsync(connectionId.Value, roomId);

        if (notify && displayName is not null)
        {
            await Clients.Group(roomId).SendAsync("UserLeft", displayName, usersSnapshot);
            _logger.LogInformation("User left {RoomId} as {DisplayName}", roomId, displayName);
        }
    }

    private readonly IRoomRegistry _registry;
    private readonly IOptions<ApplicationConfiguration> _settings;
    private readonly ILogger<RoomHub> _logger;
}
