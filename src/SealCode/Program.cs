using SealCode;
using SealCode.Hosting;
using SealCode.Routing;
using SealCode.Security;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSealCodeConfiguration(builder.Configuration);
builder.Services.AddSealCodeSecurity(builder.Environment);
builder.Services.AddSealCodeRooms();
builder.Services.AddSealCodeSignalR();
builder.Services.AddSealCodeMcp();

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
app.UseMiddleware<McpOriginGuardMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = StaticAssetCaching.Apply });
app.UseAuthentication();
app.UseAuthorization();
app.Use((context, next) => AdminHttpSecurity.InvokeAsync(context, next));
app.MapSealCodeEndpoints();
app.MapAdminEndpoints();
app.MapAdminMcpEndpoints();
app.MapHub<RoomHub>("/roomHub");
app.Run();
