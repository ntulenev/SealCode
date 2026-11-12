using System.Diagnostics.Metrics;

namespace SealCode.Observers;

/// <summary>Bounded-cardinality viewer resource instruments, without room contents or capabilities.</summary>
internal static class ObserverMetrics
{
    private static readonly Meter _meter = new("SealCode.Observers");
    internal static UpDownCounter<long> Connections { get; } = _meter.CreateUpDownCounter<long>("observer.connections");
    internal static Counter<long> Rejected { get; } = _meter.CreateCounter<long>("observer.rejected");
    internal static Counter<long> Snapshots { get; } = _meter.CreateCounter<long>("observer.snapshots");
    internal static Counter<long> Resyncs { get; } = _meter.CreateCounter<long>("observer.resyncs");
    internal static Counter<long> Bytes { get; } = _meter.CreateCounter<long>("observer.payload.bytes");
    internal static Counter<long> SlowConsumers { get; } = _meter.CreateCounter<long>("observer.slow_consumers");
    internal static Histogram<double> SnapshotDuration { get; } = _meter.CreateHistogram<double>("observer.snapshot.duration", "ms");
}
