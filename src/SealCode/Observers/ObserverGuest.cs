using System.Threading.Channels;

using Microsoft.AspNetCore.SignalR;

namespace SealCode.Observers;

/// <summary>Shared guest state guarded by the observer gate.</summary>
internal sealed class ObserverGuest
{
    /// <summary>Creates bounded delivery state for one admitted guest.</summary>
    /// <param name="context">The admitted transport context.</param>
    /// <param name="source">The source admission bucket.</param>
    /// <param name="queueCapacity">The bounded queue capacity.</param>
    public ObserverGuest(HubCallerContext context, string source, int queueCapacity)
    {
        Context = context;
        Source = source;
        Stop = CancellationTokenSource.CreateLinkedTokenSource(context.ConnectionAborted);
        Queue = Channel.CreateBounded<ObserverDeliveryItem>(
            new BoundedChannelOptions(queueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false
            });
    }

    /// <summary>Gets the admitted transport context.</summary>
    public HubCallerContext Context { get; }
    /// <summary>Gets the admission source bucket.</summary>
    public string Source { get; }
    /// <summary>Gets the lifetime cancellation source.</summary>
    public CancellationTokenSource Stop { get; }
    /// <summary>Gets the bounded delivery queue.</summary>
    public Channel<ObserverDeliveryItem> Queue { get; }
    /// <summary>Gets or sets the active delivery worker.</summary>
    public Task Pump { get; set; } = Task.CompletedTask;
    /// <summary>Gets or sets the authorized presentation.</summary>
    public ObserverPresentation? Room { get; set; }
    /// <summary>Gets or sets the current capability generation.</summary>
    public long Generation { get; set; } = -1;
    /// <summary>Gets or sets the invocation rate window.</summary>
    public long InvocationSecond { get; set; }
    /// <summary>Gets or sets the calls in the current rate window.</summary>
    public int Invocations { get; set; }
    /// <summary>Gets or sets the snapshot rate window.</summary>
    public long SnapshotMinute { get; set; }
    /// <summary>Gets or sets the requests in the snapshot window.</summary>
    public int SnapshotRequests { get; set; }
    /// <summary>Gets or sets whether a snapshot is queued.</summary>
    public bool SnapshotPending { get; set; }
    /// <summary>Gets or sets whether incremental delivery is suspended.</summary>
    public bool NeedsResync { get; set; }
    /// <summary>Gets or sets retained delivery bytes.</summary>
    public int QueuedBytes { get; set; }
}
