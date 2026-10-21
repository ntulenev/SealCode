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
        _journal = new RoomDocumentJournal(_limits, yjsState);
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
        get
        {
            lock (_versionGuard)
            {
                return _journal.State;
            }
        }
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
                return _journal.Bytes;
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
                return _journal.Count;
            }
        }
    }

    /// <summary>
    /// Gets the latest CRDT document revision.
    /// </summary>
    public RoomDocumentRevision DocumentRevision
    {
        get
        {
            lock (_versionGuard)
            {
                return _journal.DocumentRevision;
            }
        }
    }

    /// <summary>
    /// Gets the document revision represented by <see cref="YjsState"/>.
    /// </summary>
    public RoomDocumentRevision YjsCheckpointRevision
    {
        get
        {
            lock (_versionGuard)
            {
                return _journal.YjsCheckpointRevision;
            }
        }
    }

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
    public IReadOnlyDictionary<ConnectionId, RoomUser> ConnectedUsers => _membership.Users;

    /// <summary>
    /// Gets the number of connected users.
    /// </summary>
    public int ConnectedUserCount => _membership.Count;

    /// <summary>
    /// Gets the language revision, independent of document changes.
    /// </summary>
    public long MetadataRevision { get; private set; }

    /// <summary>
    /// Gets the membership revision.
    /// </summary>
    public long PresenceRevision => _membership.PresenceRevision;

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
        => _membership.Snapshot();

    /// <summary>
    /// Checks if a room user name is already in use by another connection.
    /// </summary>
    /// <param name="connectionId">The connection identifier to exclude.</param>
    /// <param name="roomUser">The room user name to check.</param>
    /// <returns>True when the room user name is in use by another connection; otherwise false.</returns>
    public bool IsRoomUserInUse(ConnectionId connectionId, RoomUser roomUser)
        => _membership.IsInUse(connectionId, roomUser);

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
            _membership.Add(connectionId, roomUser, maxUsers);
        }
    }

    /// <summary>
    /// Tries to get a connected room user name.
    /// </summary>
    /// <param name="connectionId">The connection identifier.</param>
    /// <param name="roomUser">The room user name.</param>
    /// <returns>True when found; otherwise false.</returns>
    public bool TryGetRoomUser(ConnectionId connectionId, out RoomUser roomUser) => _membership.TryGet(connectionId, out roomUser);

    /// <summary>
    /// Creates an atomic CRDT synchronization snapshot.
    /// </summary>
    /// <returns>The checkpoint and all updates appended after it.</returns>
    public RoomDocumentSyncState CreateYjsSyncState()
    {
        lock (_versionGuard)
        {
            EnsureOpen();
            return _journal.CreateSnapshot() with
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
    /// Closes the aggregate; retained references cannot accept mutations.
    /// </summary>
    public void Close()
    {
        lock (_versionGuard)
        {
            _closed = true;
            _journal.Clear();
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
    public RoomDocumentAppendResult Append(
        ConnectionId connection,
        byte[] update,
        DateTimeOffset now)
    {
        lock (_versionGuard)
        {
            RequireParticipant(connection);
            var revision = _journal.Append(update);
            Version = Version.Next();
            LastUpdatedUtc = now;
            return new RoomDocumentAppendResult(revision, Version);
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
    public bool Checkpoint(
        ConnectionId connection,
        RoomDocumentRevision revision,
        byte[] state,
        RoomText text,
        DateTimeOffset now)
    {
        lock (_versionGuard)
        {
            RequireParticipant(connection);
            if (!_journal.TryCheckpoint(revision, state, text))
            {
                return false;
            }
            Text = text;
            LastUpdatedUtc = now;
            return true;
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
    public RoomLanguageChangeResult ChangeLanguage(
        ConnectionId connection,
        RoomLanguage language,
        DateTimeOffset now,
        ILanguageValidator validator)
    {
        lock (_versionGuard)
        {
            RequireParticipant(connection);
            ArgumentNullException.ThrowIfNull(validator);
            if (!validator.IsValid(language))
            {
                throw new ArgumentException("Invalid language", nameof(language));
            }
            Language = language;
            MetadataRevision++;
            Version = Version.Next();
            LastUpdatedUtc = now;
            return new RoomLanguageChangeResult(Version, MetadataRevision);
        }
    }

    /// <summary>
    /// Returns revisions without copying document payloads.
    /// </summary>
    /// <returns>The current document, language, and membership revisions.</returns>
    /// <exception cref="RoomNotFoundException">Thrown when the room is closed.</exception>
    public RoomRevisions GetRevisions()
    {
        lock (_versionGuard)
        {
            EnsureOpen();
            return new RoomRevisions(DocumentRevision.Value, MetadataRevision, PresenceRevision);
        }
    }

    /// <summary>
    /// Removes membership and captures the matching presence event atomically.
    /// </summary>
    /// <param name="connection">The participant connection identifier.</param>
    /// <returns>Whether a user was removed, the removed user, remaining users, and presence revision.</returns>
    public RoomLeaveResult Leave(ConnectionId connection)
    {
        lock (_versionGuard)
        {
            var removed = _membership.Remove(connection, out var user);
            return new RoomLeaveResult(removed, user, CreateUsersSnapshot(), PresenceRevision);
        }
    }

    private bool HasUser(ConnectionId connection) => _membership.Contains(connection);

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

    private bool _closed;
    private readonly Lock _versionGuard = new();
    private readonly RoomDocumentJournal _journal;
    private readonly RoomMembership _membership = new();
    private readonly RoomLimits _limits;
}
