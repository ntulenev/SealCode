namespace Transport.Models;

/// <summary>
/// Represents a display name for transport payloads.
/// </summary>
public readonly record struct DisplayNameDto(string Value)
{
    /// <summary>
    /// Creates a <see cref="DisplayNameDto"/> from a string.
    /// </summary>
    /// <param name="value">The display name.</param>
    /// <returns>The display name DTO.</returns>
    /// <exception cref="ArgumentException">Thrown when the value is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when the value is null.</exception>
    public static DisplayNameDto From(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Display name is required", nameof(value));
        }

        return new DisplayNameDto(value);
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
