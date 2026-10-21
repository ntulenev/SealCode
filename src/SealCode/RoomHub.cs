using Abstractions;

using Microsoft.AspNetCore.SignalR;

using Models;
using Models.Exceptions;

using Transport.Models;

using SealCode.Hub;

using SignalRHub = Microsoft.AspNetCore.SignalR.Hub;

namespace SealCode;

/// <summary>
/// SignalR hub for room interactions.
/// </summary>
#pragma warning disable CA1515 // Need for testing in mocks
public sealed class RoomHub : SignalRHub
#pragma warning restore CA1515
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoomHub"/> class.
    /// </summary>
    /// <param name="roomManager">The room manager.</param>
    /// <param name="sessions">The room participant service.</param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
    public RoomHub(IRoomManager roomManager, IRoomSessionService sessions, ILogger<RoomHub> logger)
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
    [HubMethodName("JoinRoom")]
    public async Task<JoinRoomResult> JoinRoomAsync(string roomId, string displayName)
    {
        var parsedRoomId = ParseRoomIdOrThrow(roomId);
        if (Context.GetActiveRoomId() is string activeRoomId
            && !string.Equals(activeRoomId, parsedRoomId.Value, StringComparison.Ordinal))
        {
            throw HubErrors.Create(HubErrorCode.InvalidInput, "Leave the current room before joining another room", legacyInput: true);
        }
        var wasMember = Context.GetActiveRoomId() is not null;
        var connectionId = new ConnectionId(Context.ConnectionId);
        RoomState room;
        RoomUser roomUser;
        try
        {
            roomUser = new RoomUser(displayName);
            room = _roomManager.RegisterUserInRoom(parsedRoomId, connectionId, roomUser);
        }
        catch (ArgumentException)
        {
            throw HubErrors.Create(HubErrorCode.InvalidInput, "Invalid display name");
        }
        catch (RoomNotFoundException)
        {
            throw HubErrors.Create(HubErrorCode.RoomNotFound, "");
        }
        catch (AddRoomUserException ex)
        {
            throw HubErrors.Create(HubErrorCode.InvalidInput, ex.Message, legacyInput: true);
        }
        displayName = roomUser.Value;
        Context.SetActiveRoom(parsedRoomId.Value, displayName);
        try
        {
            await Groups.AddToGroupAsync(connectionId.Value, parsedRoomId.Value, Context.ConnectionAborted).ConfigureAwait(false);
        }
        catch
        {
            if (!wasMember)
            {
                _ = room.Leave(connectionId);
                Context.ClearActiveRoom();
            }
            throw;
        }

        // Subscribe before taking the snapshot so concurrent updates are either
        // included in it or delivered to the client's synchronization buffer.
        var joinResult = JoinRoomResult.From(room);
        try
        {
            await Clients.GroupExcept(parsedRoomId.Value, connectionId.Value)
                .SendAsync("UserJoined", displayName, joinResult.Users, joinResult.PresenceRevision, CancellationToken.None)
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
    [HubMethodName("UpdateYjs")]
    public async Task UpdateYjsAsync(string roomId, string updateBase64)
    {
        var parsedRoomId = ParseRoomIdOrThrow(roomId);
        var appendResult = HubErrors.Invoke(() => _sessions.Append(parsedRoomId, new(Context.ConnectionId), updateBase64));

        // Once appended, the update belongs to the room even if its author disconnects.
        await Clients.Group(parsedRoomId.Value)
            .SendAsync("YjsUpdated", updateBase64, appendResult.DocumentRevision.Value, appendResult.RoomVersion.Value, appendResult.Author.Value, CancellationToken.None)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the current document revision to an active room participant.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <returns>The latest accepted document revision.</returns>
    [HubMethodName("GetDocumentRevision")]
    public long GetDocumentRevision(string roomId)
        => GetRoomForParticipant(ParseRoomIdOrThrow(roomId), out _).DocumentRevision.Value;

    /// <summary>
    /// Returns all revisions from one atomic snapshot.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <returns>The current document, metadata, and presence revisions.</returns>
    [HubMethodName("GetRoomRevisions")]
    public RoomRevisions GetRoomRevisions(string roomId)
        => GetRoomForParticipant(ParseRoomIdOrThrow(roomId), out _).GetRevisions();

    /// <summary>
    /// Returns an atomic document snapshot without repeating room registration.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <returns>The current checkpoint and update journal.</returns>
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
    [HubMethodName("CheckpointYjs")]
    public async Task<bool> CheckpointYjsAsync(
        string roomId,
        string stateBase64,
        string textSnapshot,
        long basedOnRevision)
    {
        var parsedRoomId = ParseRoomIdOrThrow(roomId);
        var accepted = HubErrors.Invoke(() => _sessions.Checkpoint(parsedRoomId, new(Context.ConnectionId), stateBase64, textSnapshot, basedOnRevision)) == CheckpointOutcome.Accepted;
        if (!accepted)
        {
            _logger.LogDebug("Ignored stale Yjs checkpoint for {RoomId}: based on {BasedOnRevision}",
                parsedRoomId.Value, basedOnRevision);
        }
        if (accepted)
        {
            try
            {
                await Clients.GroupExcept(parsedRoomId.Value, Context.ConnectionId)
                    .SendAsync("YjsCheckpointed", basedOnRevision, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogWarning(ex, "Failed to broadcast Yjs checkpoint for {RoomId}", parsedRoomId.Value);
            }
        }
        return accepted;
    }

    /// <summary>
    /// Sets the room language.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <param name="language">The language to set.</param>
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
            newVersion = changed.RoomVersion.Value;
            metadataRevision = changed.MetadataRevision;
        }
        catch (ArgumentException)
        {
            throw HubErrors.Create(HubErrorCode.InvalidInput, "Invalid language", legacyInput: true);
        }
        await Clients.Group(parsedRoomId.Value)
            .SendAsync("LanguageUpdated", normalized.Value, newVersion, metadataRevision, CancellationToken.None)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Updates the cursor position for the current user.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <param name="position">The cursor position.</param>
    [HubMethodName("UpdateCursor")]
    public async Task UpdateCursorAsync(string roomId, int position)
    {
        var parsedRoomId = ParseRoomIdOrThrow(roomId);
        if (position < 0)
        {
            throw HubErrors.Create(HubErrorCode.InvalidInput, "Invalid cursor position", legacyInput: true);
        }
        await BroadcastPresenceAsync(parsedRoomId, (id, user) => Clients.GroupExcept(id, Context.ConnectionId)
            .SendAsync("CursorUpdated", user.Value, position, Context.ConnectionAborted)).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates the selection state for the current user.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <param name="isMultiLine">True if selection spans multiple lines; otherwise false.</param>
    [HubMethodName("UpdateSelection")]
    public async Task UpdateSelectionAsync(string roomId, bool isMultiLine)
    {
        await BroadcastPresenceAsync(ParseRoomIdOrThrow(roomId), (id, user) => Clients.GroupExcept(id, Context.ConnectionId)
            .SendAsync("UserSelection", user.Value, isMultiLine, Context.ConnectionAborted)).ConfigureAwait(false);
    }

    /// <summary>
    /// Marks a copy-to-clipboard action for the current user.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    [HubMethodName("UpdateCopy")]
    public async Task UpdateCopyAsync(string roomId)
    {
        await BroadcastPresenceAsync(ParseRoomIdOrThrow(roomId), (id, user) => Clients.Group(id)
            .SendAsync("UserCopy", user.Value, Context.ConnectionAborted)).ConfigureAwait(false);
    }

    /// <summary>
    /// Leaves the specified room.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    [HubMethodName("LeaveRoom")]
    public async Task LeaveRoomAsync(string roomId)
        => await RemoveFromRoomAsync(ParseRoomIdOrThrow(roomId).Value, new ConnectionId(Context.ConnectionId), notify: true).ConfigureAwait(false);

    /// <summary>
    /// Handles client disconnection.
    /// </summary>
    /// <param name="exception">The disconnect exception, if any.</param>
    public async override Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.GetActiveRoomId() is string roomId)
        {
            // ConnectionAborted is already canceled during disconnect; use a non-canceled token for cleanup/notifications.
            await RemoveFromRoomAsync(roomId, new ConnectionId(Context.ConnectionId), notify: true).ConfigureAwait(false);
        }
        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }

    private async Task RemoveFromRoomAsync(string roomId, ConnectionId connectionId, bool notify)
    {
        if (!RoomId.TryParse(roomId, out var parsedRoomId))
        {
            return;
        }
        if (Context.GetActiveRoomId() is string activeRoomId
            && string.Equals(activeRoomId, parsedRoomId.Value, StringComparison.Ordinal))
        {
            Context.ClearActiveRoom();
        }
        if (!_roomManager.TryGetRoom(parsedRoomId, out var room))
        {
            return;
        }
        string? displayName = null;
        string[] usersSnapshot = [];
        var result = room.Leave(connectionId);
        if (result.Removed)
        {
            displayName = result.User.Value;
            usersSnapshot = [.. result.Users.Select(x => x.Value)];
        }
        try
        {
            await Groups.RemoveFromGroupAsync(connectionId.Value, parsedRoomId.Value, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Keep cleanup context so disconnect can retry group removal.
            Context.SetActiveRoom(parsedRoomId.Value, displayName);
            throw;
        }
        if (notify && displayName is not null)
        {
            await Clients.Group(parsedRoomId.Value)
                .SendAsync("UserLeft", displayName, usersSnapshot, result.PresenceRevision, CancellationToken.None)
                .ConfigureAwait(false);
            _logger.LogInformation("User left {RoomId} as {RoomUser}", parsedRoomId.Value, displayName);
        }
    }

    private async Task BroadcastPresenceAsync(RoomId id, Func<string, RoomUser, Task> broadcast)
    {
        RoomUser user = default;
        if (HubErrors.Invoke(() => _sessions.TryGetParticipant(id, new(Context.ConnectionId), out _, out user)))
        {
            await broadcast(id.Value, user).ConfigureAwait(false);
        }
    }

    private RoomState GetRoomForParticipant(RoomId roomId, out RoomUser user)
    {
        RoomUser participant = default;
        var room = HubErrors.Invoke(() => _sessions.GetParticipant(roomId, new(Context.ConnectionId), out participant));
        user = participant;
        return room;
    }

    private static RoomId ParseRoomIdOrThrow(string roomId)
        => RoomId.TryParse(roomId, out var parsedRoomId)
            ? parsedRoomId
            : throw HubErrors.Create(HubErrorCode.InvalidInput, "Invalid room id", legacyInput: true);

    private readonly IRoomManager _roomManager;
    private readonly IRoomSessionService _sessions;
    private readonly ILogger<RoomHub> _logger;
}
