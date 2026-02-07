using System.Collections.Concurrent;

using Abstractions;

using Microsoft.Extensions.Logging;

using Models;

namespace Logic;

/// <summary>
/// Provides in-memory storage and management for rooms.
/// </summary>
public sealed class RoomRegistry : IRoomRegistry
{
    private readonly ConcurrentDictionary<RoomId, RoomState> _rooms = new();
    private readonly ILogger<RoomRegistry> _logger;
    private readonly IRoomNotifier _notifier;

    /// <summary>
    /// Initializes a new instance of the <see cref="RoomRegistry"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="notifier">The room notifier.</param>
    public RoomRegistry(ILogger<RoomRegistry> logger, IRoomNotifier notifier)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(notifier);

        _logger = logger;
        _notifier = notifier;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<RoomId, RoomState> Rooms => _rooms;

    /// <inheritdoc />
    public bool TryGetRoom(RoomId roomId, out RoomState room)
    {
        if (string.IsNullOrWhiteSpace(roomId.Value))
        {
            throw new ArgumentException("Room id is required", nameof(roomId));
        }

        return _rooms.TryGetValue(roomId, out room!);
    }

    /// <inheritdoc />
    public RoomState CreateRoom(RoomName name, RoomLanguage language)
    {
        if (string.IsNullOrWhiteSpace(name.Value))
        {
            throw new ArgumentException("Room name is required", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(language.Value))
        {
            throw new ArgumentException("Room language is required", nameof(language));
        }

        var room = new RoomState
        {
            RoomId = RoomId.New(),
            Name = name,
            Language = language,
            Text = RoomText.From(string.Empty),
            Version = RoomVersion.From(1),
            LastUpdatedUtc = DateTime.UtcNow
        };

        _rooms[room.RoomId] = room;
#pragma warning disable CA1873 // Avoid potentially expensive logging
        _logger.LogInformation("Room created {RoomId} ({Name})", room.RoomId.Value, room.Name.Value);
#pragma warning restore CA1873 // Avoid potentially expensive logging
        return room;
    }

    /// <inheritdoc />
#pragma warning disable IDE1006 // Naming Styles
    public async Task<bool> DeleteRoom(RoomId roomId, RoomDeletionReason reason)
#pragma warning restore IDE1006 // Naming Styles
    {
        if (string.IsNullOrWhiteSpace(roomId.Value))
        {
            throw new ArgumentException("Room id is required", nameof(roomId));
        }

        if (string.IsNullOrWhiteSpace(reason.Value))
        {
            throw new ArgumentException("Reason is required", nameof(reason));
        }

        if (!_rooms.TryRemove(roomId, out var room))
        {
            return false;
        }

#pragma warning disable CA1873 // Avoid potentially expensive logging
        _logger.LogInformation("Room deleted {RoomId} ({Name})", room.RoomId.Value, room.Name.Value);
#pragma warning restore CA1873 // Avoid potentially expensive logging
        await _notifier.RoomKilledAsync(roomId, reason);
        return true;
    }
}
