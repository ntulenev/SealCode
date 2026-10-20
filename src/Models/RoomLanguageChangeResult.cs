namespace Models;

/// <summary>
/// Describes the revisions assigned after changing the room language.
/// </summary>
public sealed class RoomLanguageChangeResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoomLanguageChangeResult"/> class.
    /// </summary>
    /// <param name="roomVersion">The resulting room version.</param>
    /// <param name="metadataRevision">The assigned language metadata revision.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the room version or metadata revision is not positive.</exception>
    public RoomLanguageChangeResult(RoomVersion roomVersion, long metadataRevision)
    {
        if (roomVersion.Value < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(roomVersion), "Room version must be positive.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(metadataRevision);

        RoomVersion = roomVersion;
        MetadataRevision = metadataRevision;
    }

    /// <summary>
    /// Gets the resulting room version.
    /// </summary>
    public RoomVersion RoomVersion { get; }

    /// <summary>
    /// Gets the assigned language metadata revision.
    /// </summary>
    public long MetadataRevision { get; }
}
