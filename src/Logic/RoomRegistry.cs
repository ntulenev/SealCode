using System.Collections.Immutable;

using Abstractions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Models;
using Models.Configuration;
using Models.Exceptions;

namespace Logic;

/// <summary>
/// Provides in-memory storage and management for rooms.
/// </summary>
public sealed class RoomRegistry : IRoomRegistry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoomRegistry"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="settings">Resource budgets.</param>
    /// <param name="timeProvider">The clock used for room creation and activity.</param>
    /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
    public RoomRegistry(
        ILogger<RoomRegistry> logger,
        IOptions<ApplicationConfiguration> settings,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        ArgumentNullException.ThrowIfNull(settings);

        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _limits = settings.Value.Limits;
    }

    /// <inheritdoc />
    public bool TryGetRoom(RoomId roomId, out RoomState room) => _rooms.TryGetValue(roomId, out room!);

    /// <inheritdoc />
    public RoomState CreateRoom(
        RoomName name,
        RoomLanguage language,
        AdminUser createdBy)
    {
        lock (_capacityGuard)
        {
            if (_rooms.Count >= _limits.MaxRooms)
            {
                throw new RoomLimitException();
            }

            var room = new RoomState(
                RoomId.New(),
                name,
                language,
                new RoomText(string.Empty),
                new RoomVersion(1),
                _timeProvider.GetUtcNow(),
                createdBy, limits: _limits, timeProvider: _timeProvider);

            _ = ImmutableInterlocked.TryAdd(ref _rooms, room.RoomId, room);

#pragma warning disable CA1873 // Avoid potentially expensive logging
            _logger.LogInformation("Room created {RoomId} ({Name}) by {CreatedBy}", room.RoomId.Value, room.Name.Value, room.CreatedBy.Name);
#pragma warning restore CA1873 // Avoid potentially expensive logging

            return room;
        }
    }

    /// <inheritdoc />
    public IEnumerable<RoomState> GetRoomsSnapshot() => _rooms.Values;

    /// <inheritdoc />
    public Task<bool> DeleteRoomAsync(
        RoomId roomId,
        RoomDeletionReason reason,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!ImmutableInterlocked.TryRemove(ref _rooms, roomId, out var room))
        {
            return Task.FromResult(false);
        }

#pragma warning disable CA1873 // Avoid potentially expensive logging
        _logger.LogInformation("Room deleted {RoomId} ({Name}): {Reason}", room.RoomId.Value, room.Name.Value, reason.Value);
#pragma warning restore CA1873 // Avoid potentially expensive logging

        room.Close();
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> DeleteRoomAsync(RoomId roomId, RoomDeletionReason reason, DateTimeOffset idleCutoff, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_rooms.TryGetValue(roomId, out var room) || !room.TryCloseIfIdle(idleCutoff))
        {
            return Task.FromResult(false);
        }
        // Once closed, finish removal even if shutdown cancellation arrives during the commit.
        return DeleteRoomAsync(roomId, reason, CancellationToken.None);
    }

    private readonly Lock _capacityGuard = new();
    private readonly RoomLimits _limits;
    private readonly TimeProvider _timeProvider;
    private ImmutableDictionary<RoomId, RoomState> _rooms = [];
    private readonly ILogger<RoomRegistry> _logger;
}
