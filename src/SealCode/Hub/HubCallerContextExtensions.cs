using Microsoft.AspNetCore.SignalR;

namespace SealCode.Hub;

/// <summary>
/// Owns the per-connection active room state.
/// </summary>
internal static class HubCallerContextExtensions
{
    /// <summary>
    /// Gets the active room identifier, if any.
    /// </summary>
    public static string? GetActiveRoomId(this HubCallerContext context)
        => context.Items.TryGetValue(ROOM_ID_KEY, out var value) ? value as string : null;

    /// <summary>
    /// Remembers the active room and display name for disconnect cleanup.
    /// </summary>
    public static void SetActiveRoom(this HubCallerContext context, string roomId, string? displayName)
    {
        context.Items[ROOM_ID_KEY] = roomId;
        context.Items[DISPLAY_NAME_KEY] = displayName;
    }

    /// <summary>
    /// Clears the active room after leaving or rolling back a join.
    /// </summary>
    public static void ClearActiveRoom(this HubCallerContext context)
    {
        _ = context.Items.Remove(ROOM_ID_KEY);
        _ = context.Items.Remove(DISPLAY_NAME_KEY);
    }

    private const string ROOM_ID_KEY = "roomId";
    private const string DISPLAY_NAME_KEY = "displayName";
}
