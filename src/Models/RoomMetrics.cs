using System.Diagnostics.Metrics;

namespace Models;

/// <summary>
/// Owns the room resource instruments and their stable names.
/// </summary>
internal static class RoomMetrics
{
    /// <summary>
    /// Gets the counter of updates rejected by journal resource budgets.
    /// </summary>
    internal static Counter<long> RejectedUpdates => _rejectedUpdates;

    /// <summary>
    /// Gets the counter tracking the decoded bytes retained by room journals.
    /// </summary>
    internal static UpDownCounter<long> JournalSize => _journalSize;

    /// <summary>
    /// Gets the counter tracking the incremental updates retained by room journals.
    /// </summary>
    internal static UpDownCounter<long> JournalEntries => _journalEntries;

    private static readonly Meter _meter = new("SealCode.Rooms");
    private static readonly Counter<long> _rejectedUpdates = _meter.CreateCounter<long>("room.updates.rejected");
    private static readonly UpDownCounter<long> _journalSize = _meter.CreateUpDownCounter<long>("room.journal.bytes");
    private static readonly UpDownCounter<long> _journalEntries = _meter.CreateUpDownCounter<long>("room.journal.count");
}
