using System.Diagnostics.Metrics;

namespace Models;

/// <summary>
/// Owns the room resource instruments and their stable names.
/// </summary>
internal static class RoomMetrics
{
    private static readonly Meter _meter = new("SealCode.Rooms");
    internal static Counter<long> RejectedUpdates { get; } = _meter.CreateCounter<long>("room.updates.rejected");
    internal static UpDownCounter<long> JournalSize { get; } = _meter.CreateUpDownCounter<long>("room.journal.bytes");
    internal static UpDownCounter<long> JournalEntries { get; } = _meter.CreateUpDownCounter<long>("room.journal.count");
}
