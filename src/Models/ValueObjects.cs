namespace Models;

/// <summary>
/// Represents a room identifier.
/// </summary>
public readonly record struct RoomId(string Value)
{
    /// <summary>
    /// Creates a new room identifier.
    /// </summary>
    /// <returns>A new room identifier.</returns>
    public static RoomId New() => new(Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Creates a room identifier from a string.
    /// </summary>
    /// <param name="value">The identifier value.</param>
    /// <returns>The room identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when the value is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when the value is null.</exception>
    public static RoomId From(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Room id is required", nameof(value));
        }

        return new RoomId(value.Trim());
    }

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

/// <summary>
/// Represents a room name.
/// </summary>
public readonly record struct RoomName(string Value)
{
    /// <summary>
    /// Creates a room name.
    /// </summary>
    /// <param name="value">The name value.</param>
    /// <returns>The room name.</returns>
    /// <exception cref="ArgumentException">Thrown when the value is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when the value is null.</exception>
    public static RoomName Create(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("Room name is required", nameof(value));
        }

        if (trimmed.Length > 20)
        {
            trimmed = $"{trimmed[..20]}...";
        }

        return new RoomName(trimmed);
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>
/// Represents a room language.
/// </summary>
public readonly record struct RoomLanguage(string Value)
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
    /// Creates a room language from a string.
    /// </summary>
    /// <param name="value">The language value.</param>
    /// <returns>The room language.</returns>
    /// <exception cref="ArgumentException">Thrown when the language is invalid.</exception>
    /// <exception cref="ArgumentNullException">Thrown when the value is null.</exception>
    public static RoomLanguage From(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "csharp" => CSharp,
            "sql" => Sql,
            _ => throw new ArgumentException("Invalid language", nameof(value))
        };
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>
/// Represents a display name.
/// </summary>
public readonly record struct DisplayName(string Value)
{
    /// <summary>
    /// Creates a display name from a string.
    /// </summary>
    /// <param name="value">The display name value.</param>
    /// <returns>The display name.</returns>
    /// <exception cref="ArgumentException">Thrown when the value is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when the value is null.</exception>
    public static DisplayName From(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("Display name is required", nameof(value));
        }

        return new DisplayName(trimmed);
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>
/// Represents a room deletion reason.
/// </summary>
public readonly record struct RoomDeletionReason(string Value)
{
    /// <summary>
    /// Creates a deletion reason.
    /// </summary>
    /// <param name="value">The reason value.</param>
    /// <returns>The deletion reason.</returns>
    /// <exception cref="ArgumentException">Thrown when the value is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when the value is null.</exception>
    public static RoomDeletionReason From(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("Reason is required", nameof(value));
        }

        return new RoomDeletionReason(trimmed);
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
