using Abstractions;

using Models;

using SealCode.Security;

namespace SealCode.Routing;

/// <summary>Shares administrator and room ownership checks between endpoints.</summary>
internal static class AdminEndpointFilters
{
    /// <summary>Requires an administrator and retains the resolved identity.</summary>
    /// <param name="group">The protected routes.</param>
    /// <param name="redirect">Whether unauthenticated requests redirect to login.</param>
    /// <param name="noStore">Whether all responses prohibit caching.</param>
    /// <returns>The protected route group.</returns>
    public static RouteGroupBuilder RequireAdmin(this RouteGroupBuilder group,
        bool redirect = false, bool noStore = false)
        => group.AddEndpointFilter(async (invocation, next) =>
        {
            var context = invocation.HttpContext;
            if (noStore)
            {
                context.Response.Headers.CacheControl = "no-store";
            }
            var admins = context.RequestServices.GetRequiredService<IAdminUserManager>();
            if (!admins.TryGetAdminUser(out var admin))
            {
                return redirect ? Results.Redirect("/admin/login") : Results.Unauthorized();
            }
            context.Items[_adminKey] = admin;
            return await next(invocation).ConfigureAwait(false);
        });

    /// <summary>Requires ownership of the routed room after administrator resolution.</summary>
    /// <param name="group">The administrator-protected routes.</param>
    /// <returns>The protected route group.</returns>
    public static RouteGroupBuilder RequireRoomOwner(this RouteGroupBuilder group)
        => group.AddEndpointFilter(async (invocation, next) =>
        {
            var context = invocation.HttpContext;
            var rooms = context.RequestServices.GetRequiredService<IRoomManager>();
            if (!RoomId.TryParse(context.Request.RouteValues["roomId"]?.ToString(), out var id)
                || !rooms.TryGetRoom(id, out var room))
            {
                return Results.NotFound();
            }
            if (!room.CanDelete(context.GetRequiredAdmin()))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            context.Items[_roomKey] = room;
            return await next(invocation).ConfigureAwait(false);
        });

    /// <summary>Gets the administrator resolved by the group filter.</summary>
    /// <param name="context">The filtered request context.</param>
    /// <returns>The authenticated administrator.</returns>
    public static AdminUser GetRequiredAdmin(this HttpContext context)
        => (AdminUser)context.Items[_adminKey]!;

    /// <summary>Gets the room resolved by the ownership filter.</summary>
    /// <param name="context">The filtered request context.</param>
    /// <returns>The owned room.</returns>
    public static RoomState GetOwnedRoom(this HttpContext context)
        => (RoomState)context.Items[_roomKey]!;

    private static readonly object _adminKey = new();
    private static readonly object _roomKey = new();
}
