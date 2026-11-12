using System.Diagnostics.Metrics;

namespace SealCode.Observers;

/// <summary>
/// Bounded-cardinality viewer resource instruments, without room contents or capabilities.
/// </summary>
internal static class ObserverMetrics
{
    private static readonly Meter _meter = new("SealCode.Observers");

    /// <summary>
    /// Gets the current observer transport count, including unjoined transports.
    /// </summary>
    internal static UpDownCounter<long> Connections { get; } = _meter.CreateUpDownCounter<long>("observer.connections");

    /// <summary>
    /// Gets the rejection count with bounded reason labels.
    /// </summary>
    internal static Counter<long> Rejected { get; } = _meter.CreateCounter<long>("observer.rejected");

    /// <summary>
    /// Gets the count of successfully delivered full snapshots.
    /// </summary>
    internal static Counter<long> Snapshots { get; } = _meter.CreateCounter<long>("observer.snapshots");

    /// <summary>
    /// Gets the count of queue overflows requiring guest recovery.
    /// </summary>
    internal static Counter<long> Resyncs { get; } = _meter.CreateCounter<long>("observer.resyncs");

    /// <summary>
    /// Gets delivered JSON payload bytes, excluding transport framing.
    /// </summary>
    internal static Counter<long> Bytes { get; } = _meter.CreateCounter<long>("observer.payload.bytes");

    /// <summary>
    /// Gets the count of guest deliveries canceled by their send deadline.
    /// </summary>
    internal static Counter<long> SlowConsumers { get; } = _meter.CreateCounter<long>("observer.slow_consumers");

    /// <summary>
    /// Gets full-snapshot construction and transport-write duration in milliseconds.
    /// </summary>
    internal static Histogram<double> SnapshotDuration { get; } = _meter.CreateHistogram<double>("observer.snapshot.duration", "ms");
}
