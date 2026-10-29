namespace Transport.Models;

/// <summary>
/// Represents a successful room deletion returned to an MCP client.
/// </summary>
/// <param name="RoomId">The deleted room identifier.</param>
/// <param name="Deleted">Whether the room was deleted.</param>
public sealed record McpDeletedRoom(string RoomId, bool Deleted);
