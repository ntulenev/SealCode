namespace Transport.Models;

/// <summary>
/// Represents an admin room listing with its invitation path for MCP clients.
/// </summary>
/// <param name="Room">The room summary and the current admin's deletion permission.</param>
/// <param name="InvitationPath">The invitation path relative to the application URL.</param>
/// <param name="InvitationUrl">The absolute invitation URL ready to share with participants.</param>
public sealed record McpRoomSummary(RoomSummary Room, string InvitationPath, Uri InvitationUrl);
