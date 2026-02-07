namespace Transport.Models;

/// <summary>
/// Represents a create-room request payload.
/// </summary>
public sealed class CreateRoomRequestDto
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CreateRoomRequestDto"/> class.
    /// </summary>
    /// <param name="name">The room name.</param>
    /// <param name="language">The optional language.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is null.</exception>
    public CreateRoomRequestDto(string name, string? language)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required", nameof(name));
        }

        Name = name;
        Language = language;
    }

    /// <summary>
    /// Gets the room name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the optional room language.
    /// </summary>
    public string? Language { get; }
}
