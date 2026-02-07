namespace Models;

/// <summary>
/// Represents a display name.
/// </summary>
public readonly record struct DisplayName
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DisplayName"/> struct.
    /// </summary>
    /// <param name="value">The display name value.</param>
    /// <exception cref="ArgumentException">Thrown when the value is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when the value is null.</exception>
    public DisplayName(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("Display name is required", nameof(value));
        }

        Value = trimmed;
    }

    /// <summary>
    /// Gets the display name value.
    /// </summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}
