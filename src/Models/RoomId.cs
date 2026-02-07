namespace Models;

/// <summary>
/// Represents a room identifier.
/// </summary>
public readonly record struct RoomId
{
    /// <summary>
    /// Creates a new room identifier.
    /// </summary>
    /// <returns>A new room identifier.</returns>
    public static RoomId New() => new(Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes a new instance of the <see cref="RoomId"/> struct.
    /// </summary>
    /// <param name="value">The identifier value.</param>
    /// <exception cref="ArgumentException">Thrown when the value is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when the value is null.</exception>
    public RoomId(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Room id is required", nameof(value));
        }

        Value = value.Trim();
    }

    /// <summary>
    /// Gets the identifier value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Tries to parse a room identifier.
    /// </summary>
    /// <param name="value">The identifier value.</param>
    /// <param name="roomId">The parsed identifier.</param>
    /// <returns>True when parsed; otherwise false.</returns>
    public static bool TryParse(string? value, out RoomId roomId)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            roomId = default;
            return false;
        }

        roomId = new RoomId(value.Trim());
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
