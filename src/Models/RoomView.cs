namespace Models;

/// <summary>
/// Represents a lightweight view of a room for admin listings.
/// </summary>
public sealed record RoomView(
    RoomId RoomId,
    RoomName Name,
    RoomLanguage Language,
    int UsersCount,
    DateTimeOffset LastUpdatedUtc,
    AdminUser CreatedBy,
    bool CanDelete);
