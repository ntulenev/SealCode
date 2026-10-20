using System.ComponentModel.DataAnnotations;

namespace Models.Configuration;

/// <summary>
/// Defines validated per-process and per-room resource budgets.
/// </summary>
public sealed class RoomLimits : IValidatableObject
{
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
    /// Gets the maximum number of concurrent anonymous hub connections.
    /// </summary>
    [Range(1, 10000)]
    public int MaxConnections { get; init; } = 160;

    /// <summary>
    /// Gets the maximum number of invocations per connection per second.
    /// </summary>
    [Range(10, 1000)]
    public int InvocationsPerSecond { get; init; } = 100;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Worst-case escaped text plus Base64 journal/checkpoint and entry framing.
        var syncBudget = MaxEncodedCheckpointBytes + (4L * ((MaxJournalBytes + 2) / 3))
            + (6L * MaxSnapshotCharacters) + (80L * MaxJournalCount) + 4096;
        if (syncBudget > 16 * 1024 * 1024)
        {
            yield return new ValidationResult("Combined limits must fit a 16 MiB synchronization response.");
        }
    }
}
