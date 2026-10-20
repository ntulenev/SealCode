namespace Models;

/// <summary>
/// Represents an atomic snapshot of the room revisions.
/// </summary>
public sealed class RoomRevisions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoomRevisions"/> class.
    /// </summary>
    /// <param name="documentRevision">The current document revision.</param>
    /// <param name="metadataRevision">The current language metadata revision.</param>
    /// <param name="presenceRevision">The current membership revision.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a revision is negative.</exception>
    public RoomRevisions(
        long documentRevision,
        long metadataRevision,
        long presenceRevision)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(documentRevision);
        ArgumentOutOfRangeException.ThrowIfNegative(metadataRevision);
        ArgumentOutOfRangeException.ThrowIfNegative(presenceRevision);

        DocumentRevision = documentRevision;
        MetadataRevision = metadataRevision;
        PresenceRevision = presenceRevision;
    }

    /// <summary>
    /// Gets the current document revision.
    /// </summary>
    public long DocumentRevision { get; }

    /// <summary>
    /// Gets the current language metadata revision.
    /// </summary>
    public long MetadataRevision { get; }

    /// <summary>
    /// Gets the current membership revision.
    /// </summary>
    public long PresenceRevision { get; }
}
