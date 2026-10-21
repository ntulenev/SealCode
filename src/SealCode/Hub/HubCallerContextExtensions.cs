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
    /// <param name="context">The hub connection context that stores active room membership.</param>
    /// <returns>The active room identifier, or null when the connection has not joined a room.</returns>
    public static string? GetActiveRoomId(this HubCallerContext context)
        => context.Items.TryGetValue(ROOM_ID_KEY, out var value) ? value as string : null;

    /// <summary>
    /// Remembers the active room and display name for disconnect cleanup.
    /// </summary>
    /// <param name="context">The hub connection context that stores active room membership.</param>
    /// <param name="roomId">The active room identifier.</param>
    /// <param name="displayName">The active participant display name, if known.</param>
    public static void SetActiveRoom(this HubCallerContext context, string roomId, string? displayName)
    {
        context.Items[ROOM_ID_KEY] = roomId;
        context.Items[DISPLAY_NAME_KEY] = displayName;
    }

    /// <summary>
    /// Clears the active room after leaving or rolling back a join.
    /// </summary>
    /// <param name="context">The hub connection context that stores active room membership.</param>
    public static void ClearActiveRoom(this HubCallerContext context)
    {
        _ = context.Items.Remove(ROOM_ID_KEY);
        _ = context.Items.Remove(DISPLAY_NAME_KEY);
    }

    private const string ROOM_ID_KEY = "roomId";
    private const string DISPLAY_NAME_KEY = "displayName";
}
