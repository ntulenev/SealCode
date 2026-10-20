using System.ComponentModel.DataAnnotations;

namespace Models.Configuration;

/// <summary>Finite per-process and per-room resource budgets. Sizes refer to Base64 characters on the wire.</summary>
public sealed class RoomLimits : IValidatableObject
{
    /// <summary>Maximum encoded update size.</summary>
    [Range(1024, 12 * 1024 * 1024)]
    public int MaxEncodedUpdateBytes { get; init; } = 4 * 1024 * 1024;
    /// <summary>Maximum encoded checkpoint size.</summary>
    [Range(1024, 12 * 1024 * 1024)]
    public int MaxEncodedCheckpointBytes { get; init; } = 4 * 1024 * 1024;
    /// <summary>Maximum retained decoded journal bytes.</summary>
    [Range(1024, 64 * 1024 * 1024)]
    public int MaxJournalBytes { get; init; } = 4 * 1024 * 1024;
    /// <summary>Maximum retained updates.</summary>
    [Range(1, 10000)]
    public int MaxJournalCount { get; init; } = 2048;
    /// <summary>Maximum snapshot text characters.</summary>
    [Range(500000, 4 * 1024 * 1024)]
    public int MaxSnapshotCharacters { get; init; } = 1024 * 1024;
    /// <summary>Maximum rooms per process.</summary>
    [Range(1, 1000)]
    public int MaxRooms { get; init; } = 32;
    /// <summary>Maximum concurrent anonymous hub connections.</summary>
    [Range(1, 10000)]
    public int MaxConnections { get; init; } = 160;
    /// <summary>Maximum invocations per connection per second.</summary>
    [Range(10, 1000)]
    public int InvocationsPerSecond { get; init; } = 100;
    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Worst-case escaped text plus Base64 journal/checkpoint and entry framing.
        var syncBudget = MaxEncodedCheckpointBytes + (4L * ((MaxJournalBytes + 2) / 3))
            + (6L * MaxSnapshotCharacters) + (80L * MaxJournalCount) + 4096;
        if (syncBudget > 16 * 1024 * 1024)
            yield return new ValidationResult("Combined limits must fit a 16 MiB synchronization response.");
    }
}
