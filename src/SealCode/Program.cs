using Microsoft.AspNetCore.Http.Json;
using Models.Configuration;

using Abstractions;
using Logic;
using Models;

using SealCode;

using SealCode.Security;

using Transport.Models;
using Transport.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<ApplicationConfiguration>()
    .Bind(builder.Configuration)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<ILanguageValidator, ConfigurationLanguageValidator>();

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
builder.Services.AddSingleton<IRoomManager, RoomManager>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped(sp =>
{
    var accessor = sp.GetRequiredService<IHttpContextAccessor>();
    return accessor.HttpContext ?? throw new InvalidOperationException("HttpContext is not available.");
});
builder.Services.AddScoped<IAdminUserManager, AdminUserManager>();

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
            async (IAdminUserManager adminUserManager,
                   CancellationToken cancellationToken) =>
{
    return await adminUserManager.TrySetCurrentUserAsync(cancellationToken).ConfigureAwait(false)
        ? Results.Redirect("/admin")
        : Results.Redirect("/admin/login?error=1");
});

app.MapPost("/admin/logout", (IAdminUserManager adminUserManager) =>
{
    adminUserManager.ClearCurrentAdminUser();
    return Results.Redirect("/admin/login");
});

app.MapGet("/admin", (IWebHostEnvironment env, IAdminUserManager adminUserManager) =>
{
    if (!adminUserManager.IsAdmin())
    {
        return Results.Redirect("/admin/login");
    }

    var path = Path.Combine(env.WebRootPath, "admin.html");
    return Results.File(path, "text/html");
});

app.MapGet("/admin/rooms", (IRoomManager roomManager, IAdminUserManager adminUserManager) =>
{
    if (!adminUserManager.TryGetAdminUser(out var adminUser))
    {
        return Results.Unauthorized();
    }

    var rooms = roomManager.GetRoomsSnapshot(adminUser)
        .Select(room => new RoomSummaryDto(
            room.RoomId.Value,
            room.Name.Value,
            room.Language.Value,
            room.UsersCount,
            room.LastUpdatedUtc,
            room.CreatedBy.Name,
            room.CanDelete))
        .ToArray();

    return Results.Json(rooms);
});

app.MapGet("/languages", (ILanguageValidator validator)
    => Results.Json(validator.Languages));

app.MapPost("/admin/rooms",
            async (HttpContext context,
                   IRoomManager roomManager,
                   IAdminUserManager adminUserManager,
                   CancellationToken cancellationToken) =>
{
    if (!adminUserManager.TryGetAdminUser(out var adminUser))
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
    var room = roomManager.CreateRoom(name, language, adminUser);
    return Results.Json(new
    {
        RoomId = room.RoomId.Value,
        Name = room.Name.Value,
        Language = room.Language.Value,
        CreatedBy = room.CreatedBy.Name
    });
});

app.MapDelete("/admin/rooms/{roomId}",
            async (string roomId,
                   IRoomManager roomManager,
                   IAdminUserManager adminUserManager,
                   CancellationToken cancellationToken) =>
{
    if (!adminUserManager.TryGetAdminUser(out var adminUser))
    {
        return Results.Unauthorized();
    }

    var result = await roomManager.DeleteRoomAsync(new RoomId(roomId), adminUser, cancellationToken).ConfigureAwait(false);
    return result switch
    {
        RoomDeletionResult.Deleted => Results.Ok(),
        RoomDeletionResult.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
        RoomDeletionResult.NotFound => Results.NotFound(),
        _ => throw new NotImplementedException()
    };
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
