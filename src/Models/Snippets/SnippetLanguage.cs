namespace Models.Snippets;

/// <summary>
/// Identifies a snippet language or the general computer science category.
/// </summary>
public readonly record struct SnippetLanguage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SnippetLanguage"/> struct.
    /// </summary>
    /// <param name="value">A Monaco language identifier or general.</param>
    /// <exception cref="ArgumentNullException">Thrown when the value is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the value is blank or violates the category key format.</exception>
    public SnippetLanguage(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > 32 || !char.IsAsciiLetterLower(normalized[0])
            || normalized.Any(character => !char.IsAsciiLetterLower(character)
                && !char.IsAsciiDigit(character) && character is not ('_' or '-')))
        {
            throw new ArgumentException("Category keys require 1–32 lowercase letters, digits, underscores or hyphens and must start with a letter.", nameof(value));
        }
        Value = normalized;
    }

    /// <summary>
    /// Gets the normalized language identifier.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Gets whether questions are independent of a programming language.
    /// </summary>
    public bool IsGeneral => string.Equals(Value, "general", StringComparison.Ordinal);

    /// <inheritdoc />
    public override string ToString() => Value;
}
