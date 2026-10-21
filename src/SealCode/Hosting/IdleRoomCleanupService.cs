using Abstractions;

using Microsoft.Extensions.Options;

using Models;
using Models.Configuration;

namespace SealCode.Hosting;

/// <summary>
/// Expires inactive empty rooms on a one-minute clock, when enabled.
/// </summary>
internal sealed class IdleRoomCleanupService : BackgroundService
{
    /// <summary>
    /// Initializes the idle cleanup worker with the registry, timeout and clock.
    /// </summary>
    /// <param name="rooms">The room registry.</param>
    /// <param name="settings">The validated application settings.</param>
    /// <param name="timeProvider">The activity and scheduling clock.</param>
    /// <param name="logger">The cleanup logger.</param>
    public IdleRoomCleanupService(IRoomRegistry rooms, IOptions<ApplicationConfiguration> settings,
        TimeProvider timeProvider, ILogger<IdleRoomCleanupService> logger)
    {
        ArgumentNullException.ThrowIfNull(rooms);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _rooms = rooms;
        _timeout = TimeSpan.FromMinutes(settings.Value.Limits.RoomIdleTimeoutMinutes);
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_timeout == TimeSpan.Zero)
        {
            return;
        }
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), _timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await CleanupAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }

    /// <summary>
    /// Removes eligible rooms using an atomic final check in the registry.
    /// </summary>
    internal async Task CleanupAsync(CancellationToken cancellationToken)
    {
        if (_timeout == TimeSpan.Zero)
        {
            return;
        }
        var cutoff = _timeProvider.GetUtcNow() - _timeout;
        foreach (var room in _rooms.GetRoomsSnapshot())
        {
            if (room.ConnectedUserCount == 0 && room.LastActivityUtc <= cutoff
                && await _rooms.DeleteRoomAsync(room.RoomId, new RoomDeletionReason("Room expired"), cutoff, cancellationToken).ConfigureAwait(false))
            {
                _logger.LogInformation("Idle room expired {RoomId}", room.RoomId.Value);
            }
        }
    }

    private readonly IRoomRegistry _rooms;
    private readonly TimeSpan _timeout;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IdleRoomCleanupService> _logger;
}
