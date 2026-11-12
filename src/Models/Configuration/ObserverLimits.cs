using System.ComponentModel.DataAnnotations;

namespace Models.Configuration;

/// <summary>Independent resource budgets for presentation viewers.</summary>
public sealed class ObserverLimits : IValidatableObject
{
    /// <summary>Enables guest connections after deployment capacity verification.</summary>
    public bool Enabled { get; init; }
    /// <summary>Maximum viewers in one room.</summary>
    [Range(1, 1000)] public int MaxPerRoom { get; init; } = 20;
    /// <summary>Maximum guest connections, including connections not yet joined.</summary>
    [Range(1, 1000)] public int MaxConnections { get; init; } = 40;
    /// <summary>Maximum guest connections from one normalized source.</summary>
    [Range(1, 1000)] public int MaxConnectionsPerSource { get; init; } = 20;
    /// <summary>New guest connections permitted per source bucket per minute.</summary>
    [Range(1, 10000)] public int NewConnectionsPerSourcePerMinute { get; init; } = 30;
    /// <summary>Deadline for presenting a valid capability.</summary>
    [Range(5, 60)] public int UnjoinedTimeoutSeconds { get; init; } = 15;
    /// <summary>Maximum guest invocations per second.</summary>
    [Range(1, 20)] public int InvocationsPerSecond { get; init; } = 5;
    /// <summary>Concurrent full-state deliveries; includes transport backpressure.</summary>
    [Range(1, 4)] public int ConcurrentSnapshots { get; init; } = 2;
    /// <summary>Full synchronization requests per connection per minute.</summary>
    [Range(1, 10)] public int SnapshotRequestsPerConnectionPerMinute { get; init; } = 4;
    /// <summary>Queued live-event byte budget per connection.</summary>
    [Range(8192, 1024 * 1024)] public int DeliveryQueueBytesPerConnection { get; init; } = 1024 * 1024;
    /// <summary>Queued live-event count budget per connection.</summary>
    [Range(1, 256)] public int DeliveryQueueEventsPerConnection { get; init; } = 256;
    /// <summary>Deadline for a transport write.</summary>
    [Range(1, 30)] public int SendTimeoutSeconds { get; init; } = 10;
    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MaxPerRoom > MaxConnections || MaxConnectionsPerSource > MaxConnections)
            yield return new ValidationResult("Observer room/source limits must fit the observer connection budget.");
    }
}
