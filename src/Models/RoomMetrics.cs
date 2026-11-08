using System.Diagnostics.Metrics;

namespace Models;

/// <summary>
/// Owns the room resource instruments and their stable names.
/// </summary>
internal static class RoomMetrics
{
    // Initialize the meter before the instrument property initializers run.
    private static readonly Meter _meter = new("SealCode.Rooms");

    /// <summary>
    /// Gets the counter of updates rejected by journal resource budgets.
    /// </summary>
    internal static Counter<long> RejectedUpdates { get; } = _meter.CreateCounter<long>("room.updates.rejected");

    /// <summary>
    /// Gets the counter tracking the decoded bytes retained by room journals.
    /// </summary>
    internal static UpDownCounter<long> JournalSize { get; } = _meter.CreateUpDownCounter<long>("room.journal.bytes");

    /// <summary>
    /// Gets the counter tracking the incremental updates retained by room journals.
    /// </summary>
    internal static UpDownCounter<long> JournalEntries { get; } = _meter.CreateUpDownCounter<long>("room.journal.count");

    internal static Counter<long> CheckpointAccepted { get; } = _meter.CreateCounter<long>("room.checkpoint.accepted");

    internal static Counter<long> CheckpointStale { get; } = _meter.CreateCounter<long>("room.checkpoint.stale");

    internal static Counter<long> CheckpointRejected { get; } = _meter.CreateCounter<long>("room.checkpoint.rejected");
}
