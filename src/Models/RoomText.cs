namespace Models;

/// <summary>
/// Represents the room text content.
/// </summary>
public readonly record struct RoomText
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoomText"/> struct.
    /// </summary>
    /// <param name="value">The text value.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is null.</exception>
    public RoomText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    /// <summary>
    /// Gets the text value.
    /// </summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}
