namespace Models;

/// <summary>
/// Represents an atomic snapshot of the room revisions.
/// </summary>
/// <param name="DocumentRevision">The current document revision.</param>
/// <param name="MetadataRevision">The current language metadata revision.</param>
/// <param name="PresenceRevision">The current membership revision.</param>
public readonly record struct RoomRevisions(
    long DocumentRevision,
    long MetadataRevision,
    long PresenceRevision);
