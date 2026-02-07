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

builder.Services.AddSingleton<IValidateOptions<ApplicationConfiguration>, ApplicationConfigurationValidator>();

builder.Services.AddSignalR()
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

app.MapPost("/admin/login", async (HttpContext context, IOptions<ApplicationConfiguration> settings) =>
{
    var form = await context.Request.ReadFormAsync();
    var password = form["password"].ToString();

    if (password == settings.Value.AdminPassword)
    {
        context.Response.Cookies.Append(AdminAuth.COOKIENAME, "1", new CookieOptions
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

app.MapGet("/admin", (HttpContext context, IWebHostEnvironment env) =>
{
    if (!AdminAuth.IsAdmin(context))
    {
        return Results.Redirect("/admin/login");
    }

    var path = Path.Combine(env.WebRootPath, "admin.html");
    return Results.File(path, "text/html");
});

app.MapGet("/admin/rooms", (HttpContext context, IRoomRegistry registry) =>
{
    if (!AdminAuth.IsAdmin(context))
    {
        return Results.Unauthorized();
    }

    var rooms = registry.Rooms.Values
        .Select(room =>
        {
            lock (room)
            {
                return new RoomSummaryDto(
                    room.RoomId.Value,
                    room.Name.Value,
                    room.Language.Value,
                    room.ConnectedUsers.Count,
                    room.LastUpdatedUtc
                );
            }
        })
        .OrderBy(room => room.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    return Results.Json(rooms);
});

app.MapPost("/admin/rooms", async (HttpContext context, IRoomRegistry registry) =>
{
    if (!AdminAuth.IsAdmin(context))
    {
        return Results.Unauthorized();
    }

    var payload = await context.Request.ReadFromJsonAsync<CreateRoomRequestDto>();
    if (payload is null || string.IsNullOrWhiteSpace(payload.Name))
    {
        return Results.BadRequest(new { error = "Name is required" });
    }

    var language = new RoomLanguage(payload.Language ?? "csharp");
    var name = RoomName.Create(payload.Name);
    var room = registry.CreateRoom(name, language);
    return Results.Json(new
    {
        RoomId = room.RoomId.Value,
        Name = room.Name.Value,
        Language = room.Language.Value
    });
});

app.MapDelete("/admin/rooms/{roomId}", async (HttpContext context, string roomId, IRoomRegistry registry) =>
{
    if (!AdminAuth.IsAdmin(context))
    {
        return Results.Unauthorized();
    }

    var deleted = await registry.DeleteRoom(new RoomId(roomId), new RoomDeletionReason("Room deleted by admin"));
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
