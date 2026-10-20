using System.Reflection;

using Abstractions;

using Logic;

using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

using Models;
using Models.Configuration;

using SealCode;
using SealCode.Routing;
using SealCode.Security;

using Transport.Serialization;

var builder = WebApplication.CreateBuilder(args);
const int maxYjsMessageBytes = 16 * 1024 * 1024;

builder.Services.AddOptions<ApplicationConfiguration>()
    .Bind(builder.Configuration)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<IValidateOptions<ApplicationConfiguration>, ApplicationConfigurationValidator>();
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
{
    var proxies = builder.Configuration.GetSection("TrustedProxies").Get<string[]>() ?? [];
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();
    if (proxies.Length > 0)
    {
        options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
        foreach (var proxy in proxies)
            options.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
    }
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.AddSingleton<AdminHttpSecurity>();
builder.Services.AddSingleton<RoomResourceFilter>();
builder.Services.AddSingleton<ILanguageValidator, ConfigurationLanguageValidator>();

builder.Services.AddSignalR(options =>
    {
        options.AddFilter<RoomResourceFilter>();
        options.MaximumReceiveMessageSize = maxYjsMessageBytes;
        options.KeepAliveInterval = TimeSpan.FromSeconds(5);
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(15);
    })
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNamingPolicy = null;
        options.PayloadSerializerOptions.Converters.Add(new ShortGuidJsonConverter());
    });

builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
    options.SerializerOptions.Converters.Add(new ShortGuidJsonConverter());
});
builder.Services.Configure<RouteOptions>(options => options.ConstraintMap["ShortGuid"] = typeof(ShortGuidRouteConstraint));

builder.Services.AddSingleton<IRoomRegistry, RoomRegistry>();
builder.Services.AddSingleton<IRoomNotifier, SignalRRoomNotifier>();
builder.Services.AddSingleton<IRoomManager, RoomManager>();
builder.Services.AddSingleton<RoomSessionService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped(sp =>
{
    var accessor = sp.GetRequiredService<IHttpContextAccessor>();
    return accessor.HttpContext ?? throw new InvalidOperationException("HttpContext is not available.");
});
builder.Services.AddScoped<IAdminUserManager, AdminUserManager>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "admin_auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.SlidingExpiration = false;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    });

var app = builder.Build();
var informationalVersion = typeof(Program).Assembly
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "development";
var versionParts = informationalVersion.Split('+', 2);
var applicationVersion = new
{
    Version = versionParts[0],
    Commit = versionParts.Length > 1 ? versionParts[1].Split('.').Last() : null,
    InformationalVersion = informationalVersion
};

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    _ = app.UseHsts();
    _ = app.UseHttpsRedirection();
}
app.Use(async (context, next) =>
{
    try
    { await next(context).ConfigureAwait(false); }
    catch (Exception ex) when (ex is ArgumentException or System.Text.Json.JsonException or BadHttpRequestException or Models.Exceptions.RoomLimitException)
    {
        var limit = ex is Models.Exceptions.RoomLimitException;
        await Results.Problem(statusCode: limit ? 409 : 400, title: limit ? "LimitExceeded" : "InvalidInput")
            .ExecuteAsync(context).ConfigureAwait(false);
    }
});
app.Use(async (context, next) =>
{
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; worker-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self' data:; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    if (context.Request.Path.StartsWithSegments("/roomHub", StringComparison.Ordinal)
        && context.Request.Headers.Upgrade.ToString().Equals("websocket", StringComparison.OrdinalIgnoreCase)
        && context.Request.Headers.TryGetValue("Origin", out var origin)
        && (!Uri.TryCreate(origin.ToString(), UriKind.Absolute, out var uri)
            || uri.GetLeftPart(UriPartial.Authority) != $"{context.Request.Scheme}://{context.Request.Host}"))
    {
        context.Response.StatusCode = 403;
        return;
    }
    await next(context).ConfigureAwait(false);
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.Use((context, next) => app.Services.GetRequiredService<AdminHttpSecurity>().InvokeAsync(context, next));
app.MapGet("/admin/token", (HttpContext context, Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery) =>
{
    context.Response.Headers.CacheControl = "no-store";
    return Results.Json(new { Token = antiforgery.GetAndStoreTokens(context).RequestToken });
});

app.MapGet("/", (IWebHostEnvironment env) =>
{
    var path = Path.Combine(env.WebRootPath, "index.html");
    return Results.File(path, "text/html");
});

app.MapGet("/languages", (ILanguageValidator validator)
    => Results.Json(validator.Languages));

app.MapGet("/features", (IOptions<ApplicationConfiguration> settings)
    => Results.Json(new
    {
        settings.Value.CollaborativeUndoEnabled,
        MaxYjsMessageBytes = maxYjsMessageBytes,
        settings.Value.Limits
    }));

app.MapGet("/room/{roomId:ShortGuid}", (RoomId roomId, IRoomRegistry registry, IWebHostEnvironment env) =>
{
    if (!registry.TryGetRoom(roomId, out _))
    {
        return Results.NotFound("Room not found");
    }

    var path = Path.Combine(env.WebRootPath, "room.html");
    return Results.File(path, "text/html");
});

app.MapGet("/health", () => Results.Ok("ok"));

app.MapGet("/version", (HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    return Results.Json(applicationVersion);
});

app.MapAdminEndpoints();

app.MapHub<RoomHub>("/roomHub");

app.Run();
