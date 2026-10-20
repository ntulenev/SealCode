using System.Collections.Immutable;
using System.Diagnostics.Metrics;

using Models.Configuration;
using Models.Exceptions;

namespace Models;

/// <summary>
/// Represents the in-memory state of a room.
/// </summary>
public sealed class RoomState
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoomState"/> class.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <param name="name">The room name.</param>
    /// <param name="language">The room language.</param>
    /// <param name="text">The room text.</param>
    /// <param name="version">The room version.</param>
    /// <param name="lastUpdatedUtc">The last updated timestamp in UTC.</param>
    /// <param name="createdBy">The admin that created the room.</param>
    /// <param name="yjsState">The serialized Yjs document state (full update).</param>
    /// <param name="limits">Resource budgets.</param>
    public RoomState(
        RoomId roomId,
        RoomName name,
        RoomLanguage language,
        RoomText text,
        RoomVersion version,
        DateTimeOffset lastUpdatedUtc,
        AdminUser createdBy,
        byte[]? yjsState = null,
        RoomLimits? limits = null)
    {
        _limits = limits ?? new RoomLimits();
        RoomId = roomId;
        Name = name;
        Language = language;
        Text = text;
        Version = version;
        LastUpdatedUtc = lastUpdatedUtc;
        CreatedBy = createdBy;
        YjsState = yjsState is null ? [] : [.. yjsState];
    }

    /// <summary>
    /// Gets the room identifier.
    /// </summary>
    public RoomId RoomId { get; }

    /// <summary>
    /// Gets the room name.
    /// </summary>
    public RoomName Name { get; }

    /// <summary>
    /// Gets the room language.
    /// </summary>
    public RoomLanguage Language { get; private set; }

    /// <summary>
    /// Gets the room text.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when the value is null.</exception>
    public RoomText Text { get; private set; }

    /// <summary>
    /// Gets the room version.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the version is less than 1.</exception>
    public RoomVersion Version { get; private set; }

    /// <summary>
    /// Gets the serialized Yjs document state (full update).
    /// </summary>
    public byte[] YjsState
    {
        get => [.. _yjsState];
        private set => _yjsState = value;
    }

    /// <summary>
    /// Gets the retained journal size.
    /// </summary>
    public long JournalBytes
    {
        get
        {
            lock (_versionGuard)
            {
                return _journalBytes;
            }
        }
    }

    /// <summary>
    /// Gets the retained update count.
    /// </summary>
    public int JournalCount
    {
        get
        {
            lock (_versionGuard)
            {
                return _yjsUpdates.Count;
            }
        }
    }

    /// <summary>
    /// Gets the latest CRDT document revision.
    /// </summary>
    public RoomDocumentRevision DocumentRevision { get; private set; }

    /// <summary>
    /// Gets the document revision represented by <see cref="YjsState"/>.
    /// </summary>
    public RoomDocumentRevision YjsCheckpointRevision { get; private set; }

    /// <summary>
    /// Gets the last updated timestamp in UTC.
    /// </summary>
    public DateTimeOffset LastUpdatedUtc { get; private set; }

    /// <summary>
    /// Gets the admin that created the room.
    /// </summary>
    public AdminUser CreatedBy { get; }

    /// <summary>
    /// Gets the connected users keyed by connection id.
    /// </summary>
    public IReadOnlyDictionary<ConnectionId, RoomUser> ConnectedUsers => _connectedUsers;

    /// <summary>
    /// Gets the number of connected users.
    /// </summary>
    public int ConnectedUserCount => _connectedUsers.Count;

    /// <summary>
    /// Gets the language revision, independent of document changes.
    /// </summary>
    public long MetadataRevision { get; private set; }

    /// <summary>
    /// Gets the membership revision.
    /// </summary>
    public long PresenceRevision { get; private set; }

    /// <summary>
    /// Gets the room resource budgets.
    /// </summary>
    public RoomLimits Limits => _limits;

    /// <summary>
    /// Determines whether the provided admin user created this room.
    /// </summary>
    /// <param name="adminUser">The admin user to check.</param>
    /// <returns>True when the admin user created this room; otherwise false.</returns>
    public bool IsCreatedBy(AdminUser adminUser)
        => CreatedBy.Matches(adminUser);

    /// <summary>
    /// Determines whether the provided admin user can delete this room.
    /// </summary>
    /// <param name="adminUser">The current admin user.</param>
    /// <returns>True when the admin user can delete this room; otherwise false.</returns>
    public bool CanDelete(AdminUser adminUser)
        => adminUser.IsSuperAdmin || IsCreatedBy(adminUser);

    /// <summary>
    /// Creates a sorted snapshot of connected room users.
    /// </summary>
    /// <returns>Connected room users sorted case-insensitively.</returns>
    public RoomUser[] CreateUsersSnapshot()
        => [.. _connectedUsers.Values.OrderBy(name => name.Value, StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Checks if a room user name is already in use by another connection.
    /// </summary>
    /// <param name="connectionId">The connection identifier to exclude.</param>
    /// <param name="roomUser">The room user name to check.</param>
    /// <returns>True when the room user name is in use by another connection; otherwise false.</returns>
    public bool IsRoomUserInUse(ConnectionId connectionId, RoomUser roomUser)
        => _connectedUsers.Any(entry => entry.Key != connectionId
            && string.Equals(entry.Value.Value, roomUser.Value, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Adds or updates a connected room user with room capacity and name checks.
    /// </summary>
    /// <param name="connectionId">The connection identifier.</param>
    /// <param name="roomUser">The room user name.</param>
    /// <param name="maxUsers">The maximum number of users allowed in the room.</param>
    /// <exception cref="AddRoomUserException">Thrown when the room is full or the name is already in use.</exception>
    public void AddUser(ConnectionId connectionId, RoomUser roomUser, int maxUsers)
    {
        lock (_versionGuard)
        {
            EnsureOpen();
            var alreadyInRoom = HasUser(connectionId);
            if (alreadyInRoom)
            {
                if (_connectedUsers[connectionId] != roomUser)
                {
                    throw new AddRoomUserException("Leave the room before changing display name.");
                }

                return;
            }
            if (!alreadyInRoom && _connectedUsers.Count >= maxUsers)
            {
                throw new AddRoomUserException($"Room is full (max {maxUsers})");
            }

            if (IsRoomUserInUse(connectionId, roomUser))
            {
                throw new AddRoomUserException("Display name already in use. Choose another name.");
            }

            AddOrUpdateUser(connectionId, roomUser);
            PresenceRevision++;
        }
    }

    /// <summary>
    /// Tries to get a connected room user name.
    /// </summary>
    /// <param name="connectionId">The connection identifier.</param>
    /// <param name="roomUser">The room user name.</param>
    /// <returns>True when found; otherwise false.</returns>
    public bool TryGetRoomUser(ConnectionId connectionId, out RoomUser roomUser) => _connectedUsers.TryGetValue(connectionId, out roomUser);

    /// <summary>
    /// Removes a connected room user.
    /// </summary>
    /// <param name="connectionId">The connection identifier.</param>
    /// <param name="roomUser">The removed room user name.</param>
    /// <returns>True when removed; otherwise false.</returns>
    public bool RemoveUser(ConnectionId connectionId, out RoomUser roomUser)
    {
        lock (_versionGuard)
        {
            if (!ImmutableInterlocked.TryRemove(ref _connectedUsers, connectionId, out roomUser))
            {
                return false;
            }

            PresenceRevision++;
            return true;
        }
    }

    /// <summary>
    /// Updates the room language and bumps the version.
    /// </summary>
    /// <param name="language">The new language.</param>
    /// <param name="updatedUtc">The update timestamp in UTC.</param>
    /// <param name="languageValidator">The language validator.</param>
    /// <returns>The new room version.</returns>
    /// <exception cref="ArgumentException">Thrown when the language is invalid.</exception>
    /// <exception cref="ArgumentNullException">Thrown when the validator is null.</exception>
    public RoomVersion UpdateLanguage(
        RoomLanguage language,
        DateTimeOffset updatedUtc,
        ILanguageValidator languageValidator)
    {
        ArgumentNullException.ThrowIfNull(languageValidator);

        if (!languageValidator.IsValid(language))
        {
            throw new ArgumentException("Invalid language", nameof(language));
        }

        lock (_versionGuard)
        {
            EnsureOpen();
            Language = language;
            MetadataRevision++;
            Version = Version.Next();
            LastUpdatedUtc = updatedUtc;
            return Version;
        }
    }

    /// <summary>
    /// Appends an opaque incremental Yjs update and bumps both document and room revisions.
    /// </summary>
    /// <param name="update">The serialized incremental Yjs update.</param>
    /// <param name="updatedUtc">The update timestamp in UTC.</param>
    /// <returns>The assigned document revision and resulting room version.</returns>
    /// <exception cref="ArgumentException">Thrown when the update is empty.</exception>
    /// <exception cref="ArgumentNullException">Thrown when the update is null.</exception>
    public RoomDocumentAppendResult AppendYjsUpdate(
        byte[] update,
        DateTimeOffset updatedUtc)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (update.Length == 0)
        {
            throw new ArgumentException("Document update cannot be empty.", nameof(update));
        }

        lock (_versionGuard)
        {
            EnsureOpen();
            if (4L * ((update.Length + 2) / 3) > _limits.MaxEncodedUpdateBytes
                || _journalBytes + update.Length > _limits.MaxJournalBytes
                || _yjsUpdates.Count >= _limits.MaxJournalCount)
            {
                _rejectedUpdates.Add(1);
                throw new RoomLimitException();
            }
            DocumentRevision = DocumentRevision.Next();
            _yjsUpdates.Add(new RoomDocumentUpdate(DocumentRevision, update));
            _journalBytes += update.Length;
            _journalSize.Add(update.Length);
            _journalEntries.Add(1);
            Version = Version.Next();
            LastUpdatedUtc = updatedUtc;
            return new RoomDocumentAppendResult(DocumentRevision, Version);
        }
    }

    /// <summary>
    /// Tries to replace the checkpoint and clear all updates represented by it.
    /// </summary>
    /// <param name="basedOnRevision">The latest revision contained in the checkpoint.</param>
    /// <param name="state">The serialized full Yjs document state.</param>
    /// <param name="text">The corresponding plain text snapshot.</param>
    /// <param name="updatedUtc">The checkpoint timestamp in UTC.</param>
    /// <returns>True when the checkpoint was current and accepted; otherwise false.</returns>
    public bool TryCreateYjsCheckpoint(
        RoomDocumentRevision basedOnRevision,
        byte[] state,
        RoomText text,
        DateTimeOffset updatedUtc)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Length == 0)
        {
            throw new ArgumentException("Document checkpoint cannot be empty.", nameof(state));
        }

        lock (_versionGuard)
        {
            EnsureOpen();
            if (basedOnRevision != DocumentRevision)
            {
                return false;
            }

            if (4L * ((state.Length + 2) / 3) > _limits.MaxEncodedCheckpointBytes
                || text.Value.Length > _limits.MaxSnapshotCharacters)
            {
                throw new RoomLimitException();
            }

            YjsState = [.. state];
            YjsCheckpointRevision = basedOnRevision;
            _journalSize.Add(-_journalBytes);
            _journalEntries.Add(-_yjsUpdates.Count);
            _yjsUpdates.Clear();
            _journalBytes = 0;
            Text = text;
            LastUpdatedUtc = updatedUtc;
            return true;
        }
    }

    /// <summary>
    /// Creates an atomic CRDT synchronization snapshot.
    /// </summary>
    /// <returns>The checkpoint and all updates appended after it.</returns>
    public RoomDocumentSyncState CreateYjsSyncState()
    {
        lock (_versionGuard)
        {
            EnsureOpen();
            return new RoomDocumentSyncState(
                DocumentRevision,
                YjsCheckpointRevision,
                [.. YjsState],
                [.. _yjsUpdates])
            {
                Language = Language,
                Text = Text,
                Version = Version,
                MetadataRevision = MetadataRevision,
                PresenceRevision = PresenceRevision,
                Users = CreateUsersSnapshot()
            };
        }
    }

    /// <summary>
    /// Changes language and captures its exact revision atomically.
    /// </summary>
    /// <param name="language">The new room language.</param>
    /// <param name="now">The update timestamp in UTC.</param>
    /// <param name="validator">The language validator.</param>
    /// <returns>The new room version and language metadata revision.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the validator is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the language is invalid.</exception>
    /// <exception cref="RoomNotFoundException">Thrown when the room is closed.</exception>
    public (RoomVersion Version, long Revision) ChangeLanguage(
        RoomLanguage language,
        DateTimeOffset now,
        ILanguageValidator validator)
    {
        lock (_versionGuard)
        {
            return (UpdateLanguage(language, now, validator), MetadataRevision);
        }
    }

    /// <summary>
    /// Closes the aggregate; retained references cannot accept mutations.
    /// </summary>
    public void Close()
    {
        lock (_versionGuard)
        {
            _closed = true;
            _journalSize.Add(-_journalBytes);
            _journalEntries.Add(-_yjsUpdates.Count);
            _journalBytes = 0;
            _yjsUpdates.Clear();
        }
    }

    /// <summary>
    /// Appends while holding the membership/aggregate lock.
    /// </summary>
    /// <param name="connection">The participant connection identifier.</param>
    /// <param name="update">The serialized incremental CRDT update.</param>
    /// <param name="now">The update timestamp in UTC.</param>
    /// <returns>The assigned document revision and room version.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when the connection is not a participant.</exception>
    /// <exception cref="RoomNotFoundException">Thrown when the room is closed.</exception>
    /// <exception cref="ArgumentNullException">Thrown when the update is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the update is empty.</exception>
    /// <exception cref="RoomLimitException">Thrown when a resource budget is exceeded.</exception>
    public RoomDocumentAppendResult AppendForParticipant(
        ConnectionId connection,
        byte[] update,
        DateTimeOffset now)
    {
        lock (_versionGuard)
        {
            RequireParticipant(connection);
            return AppendYjsUpdate(update, now);
        }
    }

    /// <summary>
    /// Checkpoints while holding the membership/aggregate lock.
    /// </summary>
    /// <param name="connection">The participant connection identifier.</param>
    /// <param name="revision">The document revision represented by the checkpoint.</param>
    /// <param name="state">The serialized checkpoint state.</param>
    /// <param name="text">The corresponding plain text snapshot.</param>
    /// <param name="now">The checkpoint timestamp in UTC.</param>
    /// <returns>True when the checkpoint was accepted; otherwise false.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when the connection is not a participant.</exception>
    /// <exception cref="RoomNotFoundException">Thrown when the room is closed.</exception>
    /// <exception cref="ArgumentNullException">Thrown when the checkpoint is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the checkpoint is empty.</exception>
    /// <exception cref="RoomLimitException">Thrown when a resource budget is exceeded.</exception>
    public bool CheckpointForParticipant(
        ConnectionId connection,
        RoomDocumentRevision revision,
        byte[] state,
        RoomText text,
        DateTimeOffset now)
    {
        lock (_versionGuard)
        {
            RequireParticipant(connection);
            return TryCreateYjsCheckpoint(revision, state, text, now);
        }
    }

    /// <summary>
    /// Changes metadata while holding the membership/aggregate lock.
    /// </summary>
    /// <param name="connection">The participant connection identifier.</param>
    /// <param name="language">The new room language.</param>
    /// <param name="now">The update timestamp in UTC.</param>
    /// <param name="validator">The language validator.</param>
    /// <returns>The new room version and language metadata revision.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when the connection is not a participant.</exception>
    /// <exception cref="RoomNotFoundException">Thrown when the room is closed.</exception>
    /// <exception cref="ArgumentNullException">Thrown when the validator is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the language is invalid.</exception>
    public (RoomVersion Version, long Revision) LanguageForParticipant(
        ConnectionId connection,
        RoomLanguage language,
        DateTimeOffset now,
        ILanguageValidator validator)
    {
        lock (_versionGuard)
        {
            RequireParticipant(connection);
            return ChangeLanguage(language, now, validator);
        }
    }

    /// <summary>
    /// Returns revisions without copying document payloads.
    /// </summary>
    /// <returns>The current document, language, and membership revisions.</returns>
    /// <exception cref="RoomNotFoundException">Thrown when the room is closed.</exception>
    public (long Document, long Metadata, long Presence) GetRevisions()
    {
        lock (_versionGuard)
        {
            EnsureOpen();
            return (DocumentRevision.Value, MetadataRevision, PresenceRevision);
        }
    }

    /// <summary>
    /// Removes membership and captures the matching presence event atomically.
    /// </summary>
    /// <param name="connection">The participant connection identifier.</param>
    /// <returns>Whether a user was removed, the removed user, remaining users, and presence revision.</returns>
    public (bool Removed, RoomUser User, RoomUser[] Users, long Revision) Leave(ConnectionId connection)
    {
        lock (_versionGuard)
        {
            var removed = RemoveUser(connection, out var user);
            return (removed, user, CreateUsersSnapshot(), PresenceRevision);
        }
    }

    private void AddOrUpdateUser(ConnectionId connectionId, RoomUser roomUser)
        => ImmutableInterlocked.AddOrUpdate(
            ref _connectedUsers,
            connectionId,
            roomUser,
            static (_, value) => value);

    private bool HasUser(ConnectionId connectionId) => _connectedUsers.ContainsKey(connectionId);

    private void RequireParticipant(ConnectionId connection)
    {
        EnsureOpen();
        if (!HasUser(connection))
        {
            throw new UnauthorizedAccessException("Join the room before changing it");
        }
    }

    private void EnsureOpen()
    {
        if (_closed)
        {
            throw new RoomNotFoundException();
        }
    }

    private byte[] _yjsState = [];
    private bool _closed;
    private readonly Lock _versionGuard = new();
    private ImmutableDictionary<ConnectionId, RoomUser> _connectedUsers = [];
    private readonly List<RoomDocumentUpdate> _yjsUpdates = [];
    private readonly RoomLimits _limits;
    private long _journalBytes;
    private static readonly Meter _resourceMeter = new("SealCode.Rooms");
    private static readonly Counter<long> _rejectedUpdates = _resourceMeter.CreateCounter<long>("room.updates.rejected");
    private static readonly UpDownCounter<long> _journalSize = _resourceMeter.CreateUpDownCounter<long>("room.journal.bytes");
    private static readonly UpDownCounter<long> _journalEntries = _resourceMeter.CreateUpDownCounter<long>("room.journal.count");
}

