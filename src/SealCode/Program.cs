using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

using Models.Configuration;

using Abstractions;
using Logic;
using Models;

using Transport;
using Transport.Admin;

using Transport.Models;
using Transport.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<ApplicationConfiguration>()
    .Bind(builder.Configuration)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSignalR(options => options.MaximumReceiveMessageSize = 1024 * 1024)
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNamingPolicy = null;
        options.PayloadSerializerOptions.Converters.Add(new DisplayNameDtoJsonConverter());
    });

builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
    options.SerializerOptions.Converters.Add(new DisplayNameDtoJsonConverter());
});

builder.Services.AddSingleton<IRoomRegistry, RoomRegistry>();
builder.Services.AddSingleton<IRoomNotifier, SignalRRoomNotifier>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/", (IWebHostEnvironment env) =>
{
    var path = Path.Combine(env.WebRootPath, "index.html");
    return Results.File(path, "text/html");
});

app.MapGet("/admin/login", (IWebHostEnvironment env) =>
{
    var path = Path.Combine(env.WebRootPath, "admin-login.html");
    return Results.File(path, "text/html");
});

app.MapPost("/admin/login",
            async (HttpContext context,
                   IOptions<ApplicationConfiguration> settings,
                   CancellationToken cancellationToken) =>
{
    var form = await context.Request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
    var name = form["name"].ToString().Trim();
    var password = form["password"].ToString();

    var user = settings.Value.AdminUsers.FirstOrDefault(user =>
        string.Equals(user.Name, name, StringComparison.OrdinalIgnoreCase)
        && user.Password == password);

    if (user is not null)
    {
        context.Response.Cookies.Append(AdminAuth.COOKIENAME, user.Name, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax
        });

        return Results.Redirect("/admin");
    }

    return Results.Redirect("/admin/login?error=1");
});

app.MapPost("/admin/logout", (HttpContext context) =>
{
    context.Response.Cookies.Delete(AdminAuth.COOKIENAME);
    return Results.Redirect("/admin/login");
});

app.MapGet("/admin", (HttpContext context, IWebHostEnvironment env, IOptions<ApplicationConfiguration> settings) =>
{
    if (!AdminAuth.IsAdmin(context, settings.Value))
    {
        return Results.Redirect("/admin/login");
    }

    var path = Path.Combine(env.WebRootPath, "admin.html");
    return Results.File(path, "text/html");
});

app.MapGet("/admin/rooms", (HttpContext context, IRoomRegistry registry, IOptions<ApplicationConfiguration> settings) =>
{
    if (!AdminAuth.IsAdmin(context, settings.Value))
    {
        return Results.Unauthorized();
    }

    var rooms = registry.GetRoomsSnapshot()
        .Select(room => new RoomSummaryDto(
            room.RoomId.Value,
            room.Name.Value,
            room.Language.Value,
            room.ConnectedUserCount,
            room.LastUpdatedUtc,
            room.CreatedBy.Value))
        .OrderBy(room => room.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    return Results.Json(rooms);
});

app.MapPost("/admin/rooms",
            async (HttpContext context,
                   IRoomRegistry registry,
                   IOptions<ApplicationConfiguration> settings,
                   CancellationToken cancellationToken) =>
{
    if (!AdminAuth.TryGetAdminName(context, settings.Value, out var adminName))
    {
        return Results.Unauthorized();
    }

    var payload = await context.Request.ReadFromJsonAsync<CreateRoomRequestDto>(cancellationToken).ConfigureAwait(false);
    if (payload is null || string.IsNullOrWhiteSpace(payload.Name))
    {
        return Results.BadRequest(new { error = "Name is required" });
    }

    var language = new RoomLanguage(payload.Language ?? "csharp");
    var name = new RoomName(payload.Name);
    var room = registry.CreateRoom(name, language, new CreatedBy(adminName));
    return Results.Json(new
    {
        RoomId = room.RoomId.Value,
        Name = room.Name.Value,
        Language = room.Language.Value,
        CreatedBy = room.CreatedBy.Value
    });
});

app.MapDelete("/admin/rooms/{roomId}",
            async (HttpContext context,
                   string roomId,
                   IRoomRegistry registry,
                   IOptions<ApplicationConfiguration> settings,
                   CancellationToken cancellationToken) =>
{
    if (!AdminAuth.IsAdmin(context, settings.Value))
    {
        return Results.Unauthorized();
    }

    var deleted = await registry.DeleteRoomAsync(
        new RoomId(roomId),
        new RoomDeletionReason("Room deleted by admin"),
        cancellationToken).ConfigureAwait(false);
    return deleted ? Results.Ok() : Results.NotFound();
});

app.MapGet("/room/{roomId}", (string roomId, IRoomRegistry registry, IWebHostEnvironment env) =>
{
    if (!RoomId.TryParse(roomId, out var parsedRoomId))
    {
        return Results.BadRequest("Invalid room id");
    }

    if (!registry.TryGetRoom(parsedRoomId, out _))
    {
        return Results.NotFound("Room not found");
    }

    var path = Path.Combine(env.WebRootPath, "room.html");
    return Results.File(path, "text/html");
});

app.MapGet("/health", () => Results.Ok("ok"));

app.MapHub<RoomHub>("/roomHub");

app.Run();
