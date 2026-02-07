namespace Models;

/// <summary>
/// Represents a room language.
/// </summary>
public readonly record struct RoomLanguage
{
    /// <summary>
    /// The C# language option.
    /// </summary>
#pragma warning disable IDE1006 // Naming Styles
    public static readonly RoomLanguage CSharp = new("csharp");
#pragma warning restore IDE1006 // Naming Styles

    /// <summary>
    /// The SQL language option.
    /// </summary>
#pragma warning disable IDE1006 // Naming Styles
    public static readonly RoomLanguage Sql = new("sql");
#pragma warning restore IDE1006 // Naming Styles

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
        Value = normalized switch
        {
            "csharp" => CSharp.Value,
            "sql" => Sql.Value,
            _ => throw new ArgumentException("Invalid language", nameof(value))
        };
    }

    /// <summary>
    /// Gets the language value.
    /// </summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}
