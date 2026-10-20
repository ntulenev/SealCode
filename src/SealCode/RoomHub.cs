using Abstractions;

using Microsoft.AspNetCore.SignalR;

using Models;
using Models.Exceptions;

using Transport.Models;

namespace SealCode;

/// <summary>
/// SignalR hub for room interactions.
/// </summary>
#pragma warning disable CA1515 // Need for testing in mocks
public sealed class RoomHub : Hub
#pragma warning restore CA1515 
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoomHub"/> class.
    /// </summary>
    /// <param name="roomManager">The room manager.</param>
    /// <param name="sessions">The room participant service.</param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
    public RoomHub(
        IRoomManager roomManager,
        IRoomSessionService sessions,
        ILogger<RoomHub> logger)
    {
        ArgumentNullException.ThrowIfNull(roomManager);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(logger);

        _roomManager = roomManager;
        _sessions = sessions;
        _logger = logger;
    }

    /// <summary>
    /// Joins the specified room with a display name.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <param name="displayName">The user display name.</param>
    /// <returns>The join result including the initial room state.</returns>
    /// <exception cref="HubException">Thrown when the room or inputs are invalid.</exception>
    [HubMethodName("JoinRoom")]
    public async Task<JoinRoomResult> JoinRoomAsync(string roomId, string displayName)
    {
        var parsedRoomId = ParseRoomIdOrThrow(roomId);
        if (Context.Items.TryGetValue("roomId", out var activeRoom)
            && activeRoom is string activeRoomId
            && !string.Equals(activeRoomId, parsedRoomId.Value, StringComparison.Ordinal))
        {
            throw new HubException("Leave the current room before joining another room");
        }

        var wasMember = Context.Items.ContainsKey("roomId");
        var connectionId = new ConnectionId(Context.ConnectionId);
        RoomState room;
        RoomUser roomUser;

        try
        {
            roomUser = new RoomUser(displayName);
            room = _roomManager.RegisterUserInRoom(
                parsedRoomId,
                connectionId,
                roomUser);
        }
        catch (ArgumentException)
        {
            throw new HubException("InvalidInput: Invalid display name");
        }
        catch (RoomNotFoundException ex)
        {
            throw new HubException(ex.Message);
        }
        catch (AddRoomUserException ex)
        {
            throw new HubException(ex.Message);
        }

        displayName = roomUser.Value;

        Context.Items["roomId"] = parsedRoomId.Value;
        Context.Items["displayName"] = displayName;

        var cancellationToken = Context.ConnectionAborted;

        try
        {
            await Groups.AddToGroupAsync(connectionId.Value, parsedRoomId.Value, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (!wasMember)
            {
                _ = room.RemoveUser(connectionId, out _);
                _ = Context.Items.Remove("roomId");
                _ = Context.Items.Remove("displayName");
            }
            throw;
        }

        // Subscribe before taking the snapshot so concurrent updates are either
        // included in it or delivered to the client's synchronization buffer.
        var joinResult = JoinRoomResult.From(room);
        try
        {
            await Clients.GroupExcept(parsedRoomId.Value, connectionId.Value)
                .SendAsync(
                    "UserJoined",
                    displayName,
                    joinResult.Users,
                    joinResult.PresenceRevision,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning(ex, "Presence notification failed for {RoomId}", parsedRoomId.Value);
        }

        _logger.LogInformation("User joined {RoomId} ({Name}) as {RoomUser}", parsedRoomId.Value, room.Name.Value, displayName);

        return joinResult;
    }

    /// <summary>
    /// Appends and broadcasts an incremental Yjs update.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <param name="updateBase64">The incremental Yjs update (base64).</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="HubException">Thrown when inputs are invalid or the room is not found.</exception>
    [HubMethodName("UpdateYjs")]
    public async Task UpdateYjsAsync(string roomId, string updateBase64)
    {
        var parsedRoomId = ParseRoomIdOrThrow(roomId);

        if (string.IsNullOrWhiteSpace(updateBase64))
        {
            throw new HubException("Update payload required");
        }

        var room = GetRoomForParticipant(parsedRoomId, out var user);

        if (updateBase64.Length > room.Limits.MaxEncodedUpdateBytes)
        {
            throw new HubException("LimitExceeded: Update size");
        }

        byte[] update;
        try
        {
            update = Convert.FromBase64String(updateBase64);
        }
        catch (FormatException)
        {
            throw new HubException("Invalid Yjs payload");
        }

        var author = user.Value;

        var appendResult = _sessions.Append(parsedRoomId, new(Context.ConnectionId), update);

        // Once appended, the update belongs to the room even if its author disconnects.
        var cancellationToken = CancellationToken.None;
        await Clients.Group(parsedRoomId.Value)
            .SendAsync(
                "YjsUpdated",
                updateBase64,
                appendResult.DocumentRevision.Value,
                appendResult.RoomVersion.Value,
                author,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the current document revision to an active room participant.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <returns>The latest accepted document revision.</returns>
    /// <exception cref="HubException">The caller has not joined the room or the room does not exist.</exception>
    [HubMethodName("GetDocumentRevision")]
    public long GetDocumentRevision(string roomId)
        => GetRoomForParticipant(ParseRoomIdOrThrow(roomId), out _).DocumentRevision.Value;

    /// <summary>
    /// Returns all revisions from one atomic snapshot.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <returns>The current document, metadata, and presence revisions.</returns>
    /// <exception cref="HubException">Thrown when the room or participant is not found.</exception>
    [HubMethodName("GetRoomRevisions")]
    public object GetRoomRevisions(string roomId)
    {
        var (document, metadata, presence) = GetRoomForParticipant(ParseRoomIdOrThrow(roomId), out _).GetRevisions();
        return new
        {
            DocumentRevision = document,
            MetadataRevision = metadata,
            PresenceRevision = presence
        };
    }

    /// <summary>
    /// Returns an atomic document snapshot without repeating room registration.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <returns>The current checkpoint and update journal.</returns>
    /// <exception cref="HubException">The caller has not joined the room or the room does not exist.</exception>
    [HubMethodName("SyncYjs")]
    public JoinRoomResult SyncYjs(string roomId)
        => JoinRoomResult.From(GetRoomForParticipant(ParseRoomIdOrThrow(roomId), out _));

    /// <summary>
    /// Stores a compact Yjs checkpoint when it represents the latest document revision.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <param name="stateBase64">The full Yjs document state (base64).</param>
    /// <param name="textSnapshot">The corresponding plain text snapshot.</param>
    /// <param name="basedOnRevision">The latest document revision contained in the checkpoint.</param>
    /// <returns>True when the checkpoint was accepted; otherwise false.</returns>
    /// <exception cref="HubException">Thrown when inputs are invalid or the room is not found.</exception>
    [HubMethodName("CheckpointYjs")]
    public Task<bool> CheckpointYjsAsync(
        string roomId,
        string stateBase64,
        string textSnapshot,
        long basedOnRevision)
    {
        var parsedRoomId = ParseRoomIdOrThrow(roomId);
        RoomDocumentRevision revision;
        try
        {
            revision = new RoomDocumentRevision(basedOnRevision);
        }
        catch (ArgumentException)
        {
            throw new HubException("Invalid document revision");
        }

        if (string.IsNullOrWhiteSpace(stateBase64))
        {
            throw new HubException("State payload required");
        }

        var room = GetRoomForParticipant(parsedRoomId, out _);

        if (stateBase64.Length > room.Limits.MaxEncodedCheckpointBytes
            || (textSnapshot?.Length ?? 0) > room.Limits.MaxSnapshotCharacters)
        {
            throw new HubException("LimitExceeded: Checkpoint size");
        }

        byte[] state;
        try
        {
            state = Convert.FromBase64String(stateBase64);
        }
        catch (FormatException)
        {
            throw new HubException("Invalid Yjs payload");
        }

        var accepted = _sessions.Checkpoint(
            parsedRoomId,
            new ConnectionId(Context.ConnectionId),
            revision,
            state,
            new RoomText(textSnapshot ?? string.Empty));

        if (!accepted)
        {
            _logger.LogDebug(
                "Ignored stale Yjs checkpoint for {RoomId}: based on {BasedOnRevision}, current {DocumentRevision}",
                parsedRoomId.Value,
                basedOnRevision,
                room.DocumentRevision.Value);
        }

        return Task.FromResult(accepted);
    }

    /// <summary>
    /// Sets the room language.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <param name="language">The language to set.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="HubException">Thrown when inputs are invalid or the room is not found.</exception>
    [HubMethodName("SetLanguage")]
    public async Task SetLanguageAsync(string roomId, string language)
    {
        var parsedRoomId = ParseRoomIdOrThrow(roomId);

        var room = GetRoomForParticipant(parsedRoomId, out _);

        int newVersion;
        long metadataRevision;
        RoomLanguage normalized;
        try
        {
            normalized = new RoomLanguage(language);
            var changed = _sessions.ChangeLanguage(room, new(Context.ConnectionId), normalized);
            newVersion = changed.Version.Value;
            metadataRevision = changed.Revision;
        }
        catch (ArgumentException)
        {
            throw new HubException("Invalid language");
        }

        await Clients.Group(parsedRoomId.Value)
            .SendAsync(
                "LanguageUpdated",
                normalized.Value,
                newVersion,
                metadataRevision,
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Updates the cursor position for the current user.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <param name="position">The cursor position.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="HubException">Thrown when inputs are invalid or the room is not found.</exception>
    [HubMethodName("UpdateCursor")]
    public async Task UpdateCursorAsync(string roomId, int position)
    {
        var parsedRoomId = ParseRoomIdOrThrow(roomId);

        if (position < 0)
        {
            throw new HubException("Invalid cursor position");
        }

        if (!_roomManager.TryGetRoom(parsedRoomId, out var room))
        {
            throw new HubException("Room not found");
        }

        if (!room.TryGetRoomUser(new ConnectionId(Context.ConnectionId), out var user))
        {
            return;
        }
        var author = user.Value;

        var cancellationToken = Context.ConnectionAborted;
        await Clients.GroupExcept(parsedRoomId.Value, Context.ConnectionId)
            .SendAsync("CursorUpdated", author, position, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Updates the selection state for the current user.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <param name="isMultiLine">True if selection spans multiple lines; otherwise false.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="HubException">Thrown when inputs are invalid or the room is not found.</exception>
    [HubMethodName("UpdateSelection")]
    public async Task UpdateSelectionAsync(string roomId, bool isMultiLine)
    {
        var parsedRoomId = ParseRoomIdOrThrow(roomId);

        if (!_roomManager.TryGetRoom(parsedRoomId, out var room))
        {
            throw new HubException("Room not found");
        }

        if (!room.TryGetRoomUser(new ConnectionId(Context.ConnectionId), out var user))
        {
            return;
        }
        var author = user.Value;

        var cancellationToken = Context.ConnectionAborted;
        await Clients.Group(parsedRoomId.Value).SendAsync("UserSelection", author, isMultiLine, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Marks a copy-to-clipboard action for the current user.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="HubException">Thrown when inputs are invalid or the room is not found.</exception>
    [HubMethodName("UpdateCopy")]
    public async Task UpdateCopyAsync(string roomId)
    {
        var parsedRoomId = ParseRoomIdOrThrow(roomId);

        if (!_roomManager.TryGetRoom(parsedRoomId, out var room))
        {
            throw new HubException("Room not found");
        }

        if (!room.TryGetRoomUser(new ConnectionId(Context.ConnectionId), out var user))
        {
            return;
        }

        var cancellationToken = Context.ConnectionAborted;
        await Clients.Group(parsedRoomId.Value).SendAsync("UserCopy", user.Value, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Leaves the specified room.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="HubException">Thrown when inputs are invalid.</exception>
    [HubMethodName("LeaveRoom")]
    public async Task LeaveRoomAsync(string roomId)
    {
        var parsedRoomId = ParseRoomIdOrThrow(roomId);

        await RemoveFromRoomAsync(parsedRoomId.Value, new ConnectionId(Context.ConnectionId), notify: true).ConfigureAwait(false);
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
            // ConnectionAborted is already canceled during disconnect; use a non-canceled token for cleanup/notifications.
            await RemoveFromRoomAsync(roomId, new ConnectionId(Context.ConnectionId), notify: true).ConfigureAwait(false);
        }

        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }

    private async Task RemoveFromRoomAsync(
        string roomId,
        ConnectionId connectionId,
        bool notify)
    {
        if (!RoomId.TryParse(roomId, out var parsedRoomId))
        {
            return;
        }

        if (Context.Items.TryGetValue("roomId", out var activeRoom)
            && activeRoom is string activeRoomId
            && string.Equals(activeRoomId, parsedRoomId.Value, StringComparison.Ordinal))
        {
            _ = Context.Items.Remove("roomId");
            _ = Context.Items.Remove("displayName");
        }

        if (!_roomManager.TryGetRoom(parsedRoomId, out var room))
        {
            return;
        }

        string? displayName = null;
        string[] usersSnapshot = [];

        var (removed, leftUser, leftUsers, leftRevision) = room.Leave(connectionId);
        if (removed)
        {
            displayName = leftUser.Value;
            usersSnapshot = [.. leftUsers.Select(x => x.Value)];
        }

        try
        {
            await Groups.RemoveFromGroupAsync(connectionId.Value, parsedRoomId.Value, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Keep cleanup context so disconnect can retry group removal.
            Context.Items["roomId"] = parsedRoomId.Value;
            throw;
        }

        if (notify && displayName is not null)
        {
            await Clients.Group(parsedRoomId.Value)
                .SendAsync(
                    "UserLeft",
                    displayName,
                    usersSnapshot,
                    leftRevision,
                    CancellationToken.None)
                .ConfigureAwait(false);
            _logger.LogInformation("User left {RoomId} as {RoomUser}", parsedRoomId.Value, displayName);
        }
    }

    private RoomState GetRoomForParticipant(RoomId roomId, out RoomUser user)
    {
        try
        {
            return _sessions.GetParticipant(roomId, new(Context.ConnectionId), out user);
        }
        catch (RoomNotFoundException ex) { throw new HubException(ex.Message); }
        catch (UnauthorizedAccessException ex) { throw new HubException(ex.Message); }
    }

    private static RoomId ParseRoomIdOrThrow(string roomId)
    {
        if (!RoomId.TryParse(roomId, out var parsedRoomId))
        {
            throw new HubException("Invalid room id");
        }

        return parsedRoomId;
    }

    private readonly IRoomManager _roomManager;
    private readonly IRoomSessionService _sessions;
    private readonly ILogger<RoomHub> _logger;
}

