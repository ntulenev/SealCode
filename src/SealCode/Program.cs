using SealCode;
using SealCode.Hosting;
using SealCode.Routing;
using SealCode.Security;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSealCodeConfiguration(builder.Configuration);
builder.Services.AddSealCodeSecurity(builder.Environment);
builder.Services.AddSealCodeRooms();
builder.Services.AddSealCodeStatistics();
builder.Services.AddSealCodeSignalR();
builder.Services.AddSealCodeMcp();
builder.Services.AddSealCodeSnippets(builder.Environment);

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
app.Use(AdminHttpSecurity.InvokeAsync);
app.MapSealCodeEndpoints();
app.MapAdminEndpoints();
app.MapStatisticsEndpoints();
app.MapSnippetEndpoints();
app.MapSnippetAdministrationEndpoints();
app.MapAdminMcpEndpoints();
app.MapHub<RoomHub>("/roomHub");
app.Run();
