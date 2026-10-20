namespace Models;

/// <summary>
/// Describes the revisions assigned after changing the room language.
/// </summary>
/// <param name="RoomVersion">The resulting room version.</param>
/// <param name="MetadataRevision">The assigned language metadata revision.</param>
public readonly record struct RoomLanguageChangeResult(
    RoomVersion RoomVersion,
    long MetadataRevision);
