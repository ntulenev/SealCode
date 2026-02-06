using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using SealCode.Domain;
using SealCode.Hubs;
using SealCode.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AppSettings>(builder.Configuration);

builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNamingPolicy = null;
    });
builder.Services.AddSingleton<RoomRegistry>();

builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

const string AdminCookie = "admin_auth";

bool IsAdmin(HttpContext context)
{
    return context.Request.Cookies.TryGetValue(AdminCookie, out var value) && value == "1";
}

IResult RequireAdmin(HttpContext context)
{
    return Results.Redirect("/admin/login");
}

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

app.MapPost("/admin/login", async (HttpContext context, IOptions<AppSettings> settings) =>
{
    var form = await context.Request.ReadFormAsync();
    var password = form["password"].ToString();

    if (password == settings.Value.AdminPassword)
    {
        context.Response.Cookies.Append(AdminCookie, "1", new CookieOptions
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
    context.Response.Cookies.Delete(AdminCookie);
    return Results.Redirect("/admin/login");
});

app.MapGet("/admin", (HttpContext context, IWebHostEnvironment env) =>
{
    if (!IsAdmin(context))
    {
        return RequireAdmin(context);
    }

    var path = Path.Combine(env.WebRootPath, "admin.html");
    return Results.File(path, "text/html");
});

app.MapGet("/admin/rooms", (HttpContext context, RoomRegistry registry) =>
{
    if (!IsAdmin(context))
    {
        return Results.Unauthorized();
    }

    var rooms = registry.Rooms.Values
        .Select(room =>
        {
            lock (room)
            {
                return new RoomSummary(
                    room.RoomId,
                    room.Name,
                    room.Language,
                    room.ConnectedUsers.Count,
                    room.LastUpdatedUtc
                );
            }
        })
        .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    return Results.Json(rooms);
});

app.MapPost("/admin/rooms", async (HttpContext context, RoomRegistry registry) =>
{
    if (!IsAdmin(context))
    {
        return Results.Unauthorized();
    }

    var payload = await context.Request.ReadFromJsonAsync<CreateRoomRequest>();
    if (payload is null || string.IsNullOrWhiteSpace(payload.Name))
    {
        return Results.BadRequest(new { error = "Name is required" });
    }

    var language = (payload.Language ?? "csharp").Trim().ToLowerInvariant();
    if (language != "csharp" && language != "sql")
    {
        return Results.BadRequest(new { error = "Invalid language" });
    }

    var room = registry.CreateRoom(payload.Name.Trim(), language);
    return Results.Json(new
    {
        room.RoomId,
        room.Name,
        room.Language
    });
});

app.MapDelete("/admin/rooms/{roomId}", async (HttpContext context, string roomId, RoomRegistry registry) =>
{
    if (!IsAdmin(context))
    {
        return Results.Unauthorized();
    }

    var deleted = await registry.DeleteRoomAsync(roomId, "Room deleted by admin");
    return deleted ? Results.Ok() : Results.NotFound();
});

app.MapGet("/room/{roomId}", (string roomId, RoomRegistry registry, IWebHostEnvironment env) =>
{
    if (!registry.TryGetRoom(roomId, out _))
    {
        return Results.NotFound("Room not found");
    }

    var path = Path.Combine(env.WebRootPath, "room.html");
    return Results.File(path, "text/html");
});

app.MapGet("/health", () => Results.Ok("ok"));

app.MapHub<RoomHub>("/roomHub");

app.Run();

record CreateRoomRequest(string Name, string? Language);
record RoomSummary(string RoomId, string Name, string Language, int UsersCount, DateTime LastUpdatedUtc);
