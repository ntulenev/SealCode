namespace Transport.Models;

/// <summary>
/// Represents the initial join-room payload.
/// </summary>
public sealed class JoinRoomResultDto
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JoinRoomResultDto"/> class.
    /// </summary>
    /// <param name="name">The room name.</param>
    /// <param name="language">The room language.</param>
    /// <param name="text">The room text.</param>
    /// <param name="version">The room version.</param>
    /// <param name="users">The connected users.</param>
    /// <param name="createdBy">The admin that created the room.</param>
    /// <exception cref="ArgumentNullException">Thrown when required values are null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when version is less than 1.</exception>
    public JoinRoomResultDto(string name, string language, string text, int version, DisplayNameDto[] users, string createdBy)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Language = language ?? throw new ArgumentNullException(nameof(language));
        Text = text ?? throw new ArgumentNullException(nameof(text));
        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "Version must be positive.");
        }

        Users = users ?? throw new ArgumentNullException(nameof(users));
        CreatedBy = createdBy ?? throw new ArgumentNullException(nameof(createdBy));
        Version = version;
    }

    /// <summary>
    /// Gets the room name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the room language.
    /// </summary>
    public string Language { get; }

    /// <summary>
    /// Gets the room text.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the room version.
    /// </summary>
    public int Version { get; }

    /// <summary>
    /// Gets the connected users.
    /// </summary>
    public DisplayNameDto[] Users { get; }

    /// <summary>
    /// Gets the admin that created the room.
    /// </summary>
    public string CreatedBy { get; }
}
