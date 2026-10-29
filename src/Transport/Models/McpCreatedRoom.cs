namespace Transport.Models;

/// <summary>
/// Represents a newly created room returned to an MCP client.
/// </summary>
/// <param name="RoomId">The room identifier.</param>
/// <param name="Name">The normalized room name.</param>
/// <param name="Language">The normalized room language.</param>
/// <param name="CreatedBy">The display name of the admin who created the room.</param>
/// <param name="InvitationPath">The invitation path relative to the application URL.</param>
public sealed record McpCreatedRoom(
    string RoomId,
    string Name,
    string Language,
    string CreatedBy,
    string InvitationPath);
