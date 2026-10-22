using System.ComponentModel.DataAnnotations;

namespace Models.Configuration;

/// <summary>
/// Defines validated per-process and per-room resource budgets.
/// </summary>
public sealed class RoomLimits : IValidatableObject
{
    /// <summary>
    /// Maximum size in bytes of a SignalR message.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006", Justification = "Named public contract required by the hosting configuration.")]
    public const int MaxSignalRMessageBytes = 16 * 1024 * 1024;

    /// <summary>
    /// Gets the maximum update size in Base64 characters.
    /// </summary>
    [Range(1024, 12 * 1024 * 1024)]
    public int MaxEncodedUpdateBytes { get; init; } = 4 * 1024 * 1024;

    /// <summary>
    /// Gets the maximum checkpoint size in Base64 characters.
    /// </summary>
    [Range(1024, 12 * 1024 * 1024)]
    public int MaxEncodedCheckpointBytes { get; init; } = 4 * 1024 * 1024;

    /// <summary>
    /// Gets the maximum number of retained decoded journal bytes.
    /// </summary>
    [Range(1024, 64 * 1024 * 1024)]
    public int MaxJournalBytes { get; init; } = 4 * 1024 * 1024;

    /// <summary>
    /// Gets the maximum number of retained journal updates.
    /// </summary>
    [Range(1, 10000)]
    public int MaxJournalCount { get; init; } = 2048;

    /// <summary>
    /// Gets the checkpoint threshold in document revisions.
    /// </summary>
    [Range(1, 10000)]
    public int CheckpointEveryRevisions { get; init; } = 64;

    /// <summary>
    /// Gets the checkpoint threshold in Base64 characters.
    /// </summary>
    [Range(1024, 4 * 1024 * 1024)]
    public int CheckpointEveryBase64Bytes { get; init; } = 256 * 1024;

    /// <summary>
    /// Gets the maximum number of UTF-16 characters in a text snapshot.
    /// </summary>
    [Range(500000, 4 * 1024 * 1024)]
    public int MaxSnapshotCharacters { get; init; } = 1024 * 1024;

    /// <summary>
    /// Gets the maximum number of rooms per process.
    /// </summary>
    [Range(1, 1000)]
    public int MaxRooms { get; init; } = 32;

    /// <summary>
    /// Gets the empty room idle timeout in minutes; 0 disables automatic deletion.
    /// </summary>
    [Range(0, 525600)]
    public int RoomIdleTimeoutMinutes { get; init; }

    /// <summary>
    /// Gets the maximum number of concurrent anonymous hub connections.
    /// </summary>
    [Range(1, 10000)]
    public int MaxConnections { get; init; } = 160;

    /// <summary>
    /// Gets the maximum concurrent connections from one normalized source.
    /// </summary>
    [Range(1, 10000)]
    public int MaxConnectionsPerSource
    {
        get => _maxConnectionsPerSource ?? Math.Min(20, MaxConnections);
        init => _maxConnectionsPerSource = value;
    }

    private readonly int? _maxConnectionsPerSource;

    /// <summary>
    /// Gets the deadline in seconds for a connection to join a room.
    /// </summary>
    [Range(5, 3600)]
    public int UnjoinedConnectionTimeoutSeconds { get; init; } = 60;

    /// <summary>
    /// Gets the maximum number of invocations per connection per second.
    /// </summary>
    [Range(10, 1000)]
    public int InvocationsPerSecond { get; init; } = 100;

    /// <summary>
    /// Gets the maximum new connections per source bucket per minute.
    /// </summary>
    [Range(1, 10000)]
    public int NewConnectionsPerSourcePerMinute { get; init; } = 30;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MaxConnectionsPerSource > MaxConnections)
        {
            yield return new ValidationResult("The source connection limit must not exceed the total connection limit.");
        }

        if (4L * CheckpointEveryRevisions > MaxJournalCount
            || 3L * CheckpointEveryBase64Bytes > MaxJournalBytes)
        {
            yield return new ValidationResult("Checkpoint thresholds must leave journal headroom.");
        }

        // Worst-case escaped text plus Base64 journal/checkpoint and entry framing.
        var syncBudget = MaxEncodedCheckpointBytes + Base64Size.EncodedLength(MaxJournalBytes)
            + (6L * MaxSnapshotCharacters) + (80L * MaxJournalCount) + 4096;
        if (syncBudget > 16 * 1024 * 1024)
        {
            yield return new ValidationResult("Combined limits must fit a 16 MiB synchronization response.");
        }
    }
}
