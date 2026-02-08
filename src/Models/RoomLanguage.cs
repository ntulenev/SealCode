namespace Models;

/// <summary>
/// Represents a room language.
/// </summary>
public readonly record struct RoomLanguage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoomLanguage"/> struct.
    /// </summary>
    /// <param name="value">The language value.</param>
    /// <exception cref="ArgumentNullException">Thrown when the value is null.</exception>
    public RoomLanguage(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var normalized = value.Trim().ToLowerInvariant();

        Value = normalized;
    }

    /// <summary>
    /// Gets the language value.
    /// </summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}
