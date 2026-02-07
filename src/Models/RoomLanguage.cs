using System.Collections.Frozen;

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
    /// <exception cref="ArgumentException">Thrown when the language is invalid.</exception>
    /// <exception cref="ArgumentNullException">Thrown when the value is null.</exception>
    public RoomLanguage(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var normalized = value.Trim().ToLowerInvariant();

        if (!_validLanguages.Contains(normalized))
        {
            throw new ArgumentException("Invalid language", nameof(value));
        }

        Value = normalized;
    }

    /// <summary>
    /// Gets the language value.
    /// </summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
    private static readonly FrozenSet<string> _validLanguages = (new[] { "csharp", "sql" }).ToFrozenSet();
}
