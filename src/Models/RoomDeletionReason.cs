namespace Models;

/// <summary>
/// Represents a room deletion reason.
/// </summary>
public readonly record struct RoomDeletionReason
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoomDeletionReason"/> struct.
    /// </summary>
    /// <param name="value">The reason value.</param>
    /// <exception cref="ArgumentException">Thrown when the value is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when the value is null.</exception>
    public RoomDeletionReason(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("Reason is required", nameof(value));
        }

        Value = trimmed;
    }

    /// <summary>
    /// Gets the reason value.
    /// </summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}
