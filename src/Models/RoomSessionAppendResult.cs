namespace Models;

/// <summary>
/// Describes an accepted participant update and its author captured before decoding.
/// </summary>
/// <param name="DocumentRevision">The assigned document revision.</param>
/// <param name="RoomVersion">The resulting room version.</param>
/// <param name="Author">The participant that submitted the update.</param>
public readonly record struct RoomSessionAppendResult(
    RoomDocumentRevision DocumentRevision,
    RoomVersion RoomVersion,
    RoomUser Author);
