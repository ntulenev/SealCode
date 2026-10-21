using SealCode;
using SealCode.Hosting;
using SealCode.Routing;
using SealCode.Security;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSealCodeConfiguration(builder.Configuration);
builder.Services.AddSealCodeSecurity(builder.Environment);
builder.Services.AddSealCodeRooms();
builder.Services.AddSealCodeSignalR();

var app = builder.Build();
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    _ = app.UseHsts();
    _ = app.UseHttpsRedirection();
}
app.UseMiddleware<ApiExceptionMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<WebSocketOriginGuardMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.Use((context, next) => AdminHttpSecurity.InvokeAsync(context, next));
app.MapSealCodeEndpoints();
app.MapAdminEndpoints();
app.MapHub<RoomHub>("/roomHub");
app.Run();
