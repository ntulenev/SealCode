using System.ComponentModel;

using Abstractions;

using Microsoft.Extensions.Options;

using ModelContextProtocol;
using ModelContextProtocol.Server;

using Models;
using Models.Configuration;
using Models.Exceptions;

using SealCode.Security;

using Transport.Models;

namespace SealCode.Mcp;

/// <summary>
/// Exposes the admin panel operations through MCP using the same room manager.
/// </summary>
[McpServerToolType]
internal sealed class AdminMcpTools
{
    /// <summary>
    /// Lists room snapshots with permissions evaluated for the current admin.
    /// </summary>
    /// <param name="rooms">The room manager shared with the admin panel.</param>
    /// <param name="admins">The current admin identity provider.</param>
    /// <returns>The visible rooms and invitation paths.</returns>
    [McpServerTool(Name = "list_rooms", ReadOnly = true, UseStructuredContent = true),
     Description("List all rooms, participant counts, owners, deletion permissions and invitation paths.")]
    public static McpRoomSummary[] ListRooms(IRoomManager rooms, IAdminUserManager admins)
    {
        var admin = GetAdmin(admins);
        return [.. rooms.GetRoomsSnapshot(admin).Select(room => new McpRoomSummary(
            new RoomSummary(room.RoomId.Value, room.Name.Value,
                room.Language.Value, room.UsersCount, room.LastUpdatedUtc,
                room.CreatedBy.Name, room.CanDelete),
            InvitationPath(room.RoomId)))];
    }

    /// <summary>
    /// Lists the configured languages available to the admin panel.
    /// </summary>
    /// <param name="settings">The application settings.</param>
    /// <returns>The configured room languages.</returns>
    [McpServerTool(Name = "list_languages", ReadOnly = true, UseStructuredContent = true),
     Description("List the languages available when creating a room.")]
    public static string[] ListLanguages(IOptions<ApplicationConfiguration> settings)
        => settings.Value.Languages;

    /// <summary>
    /// Creates a room using the admin panel's validation and ownership rules.
    /// </summary>
    /// <param name="rooms">The shared room manager.</param>
    /// <param name="admins">The current admin identity provider.</param>
    /// <param name="languages">The configured language validator.</param>
    /// <param name="name">The requested room name.</param>
    /// <param name="language">The optional room language; null defaults to csharp.</param>
    /// <returns>The created room and its invitation path.</returns>
    /// <exception cref="McpException">Thrown when authentication, validation or capacity checks fail.</exception>
    [McpServerTool(Name = "create_room", Destructive = false, UseStructuredContent = true),
     Description("Create a room as the authenticated admin and return its invitation path.")]
    public static McpCreatedRoom CreateRoom(
        IRoomManager rooms,
        IAdminUserManager admins,
        ILanguageValidator languages,
        [Description("Room name, required. Same name rules as the admin panel.")] string name,
        [Description("Language from list_languages; defaults to csharp.")] string? language = "csharp")
    {
        var admin = GetAdmin(admins);
        try
        {
            var roomLanguage = new RoomLanguage(language ?? "csharp");
            if (!languages.IsValid(roomLanguage))
            {
                throw new McpException("Invalid language. Use list_languages for allowed values.");
            }

            var room = rooms.CreateRoom(new RoomName(name), roomLanguage, admin);
            return new McpCreatedRoom(room.RoomId.Value, room.Name.Value,
                room.Language.Value, room.CreatedBy.Name, InvitationPath(room.RoomId));
        }
        catch (ArgumentException)
        {
            throw new McpException("Invalid room name or language.");
        }
        catch (RoomLimitException)
        {
            throw new McpException("Room capacity limit reached.");
        }
    }

    /// <summary>
    /// Deletes a room using the same authorization and notifications as the admin panel.
    /// </summary>
    /// <param name="rooms">The shared room manager.</param>
    /// <param name="admins">The current admin identity provider.</param>
    /// <param name="roomId">The room identifier.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The deleted room identifier and confirmation.</returns>
    /// <exception cref="McpException">Thrown for an invalid, missing or forbidden room.</exception>
    [McpServerTool(Name = "delete_room", Destructive = true, UseStructuredContent = true),
     Description("Delete a room and disconnect participants. Regular admins can delete only their own rooms; super admins can delete any room.")]
    public static async Task<McpDeletedRoom> DeleteRoomAsync(
        IRoomManager rooms,
        IAdminUserManager admins,
        [Description("Room identifier returned by list_rooms or create_room.")] string roomId,
        CancellationToken cancellationToken)
    {
        var admin = GetAdmin(admins);
        if (!RoomId.TryParse(roomId, out var id))
        {
            throw new McpException("Invalid room identifier.");
        }

        var result = await rooms.DeleteRoomAsync(id, admin, cancellationToken).ConfigureAwait(false);
        return result switch
        {
            RoomDeletionResult.Deleted => new McpDeletedRoom(id.Value, true),
            RoomDeletionResult.Forbidden => throw new McpException("Forbidden: this admin cannot delete another admin's room."),
            RoomDeletionResult.NotFound => throw new McpException("Room not found."),
            _ => throw new InvalidOperationException("Unknown room deletion result.")
        };
    }

    private static AdminUser GetAdmin(IAdminUserManager admins)
        => admins.TryGetAdminUser(out var admin)
            ? admin
            : throw new McpException("Admin authentication required.");

    private static string InvitationPath(RoomId id) => $"/room/{id.Value}";
}
