using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace SealCode.Tests;

public sealed class AdminMcpTests
{
    private const string ADMIN_TOKEN = "test-admin-mcp-token-0123456789abcdef";
    private const string ROOT_TOKEN = "test-root-mcp-token-0123456789abcdef";

    [Theory]
    [InlineData(null)]
    [InlineData("Bearer wrong")]
    [InlineData("Basic invalid")]
    [InlineData("Bearer")]
    public async Task RequestsRequireBearerToken(string? authorization)
    {
        using var factory = CreateFactory();
        using var http = factory.CreateClient();
        if (authorization is not null)
        {
            http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authorization);
        }
        using var response = await http.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
    }

    [Fact]
    public async Task TokensAreCaseSensitiveAndCannotBeSuppliedThroughQueryStringsOrRepeatedHeaders()
    {
        using var factory = CreateFactory();
        using var http = CreateHttpClient(factory, ADMIN_TOKEN.ToUpperInvariant());
        using var wrongCase = await http.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongCase.StatusCode);

        http.DefaultRequestHeaders.Remove("Authorization");
        using var query = await http.PostAsJsonAsync($"/mcp?access_token={ADMIN_TOKEN}", new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        Assert.Equal(HttpStatusCode.Unauthorized, query.StatusCode);

        http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", [$"Bearer {ADMIN_TOKEN}", $"Bearer {ROOT_TOKEN}"]);
        using var repeated = await http.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        Assert.Equal(HttpStatusCode.Unauthorized, repeated.StatusCode);
    }

    [Fact]
    public async Task ToolsManageRoomsWithTheSameOwnershipRulesAsTheAdminPanel()
    {
        using var factory = CreateFactory();
        using var adminHttp = CreateHttpClient(factory, ADMIN_TOKEN);
        using var rootHttp = CreateHttpClient(factory, ROOT_TOKEN);
        await using var admin = await ConnectAsync(adminHttp);
        await using var root = await ConnectAsync(rootHttp);

        var tools = await admin.ListToolsAsync();
        Assert.Equal(["create_room", "delete_room", "list_languages", "list_rooms"],
            [.. tools.Select(tool => tool.Name).Order(StringComparer.Ordinal)]);
        foreach (var name in new[] { "create_room", "delete_room", "list_rooms" })
        {
            var schema = tools.Single(tool => tool.Name == name).ProtocolTool.OutputSchema;
            Assert.NotNull(schema);
            Assert.Contains("roomId", schema.Value.GetRawText(), StringComparison.Ordinal);
        }
        var languages = await admin.CallToolAsync("list_languages");
        Assert.Contains("csharp", languages.Content.OfType<TextContentBlock>().Single().Text, StringComparison.Ordinal);

        var created = await admin.CallToolAsync("create_room", new Dictionary<string, object?> { ["name"] = "MCP room" });
        Assert.NotEqual(true, created.IsError);
        var ownId = Payload(created).GetProperty("roomId").GetString()!;
        Assert.Equal("csharp", Payload(created).GetProperty("language").GetString());
        Assert.Equal("Admin", Payload(created).GetProperty("createdBy").GetString());
        Assert.Equal($"/room/{ownId}", Payload(created).GetProperty("invitationPath").GetString());
        Assert.Equal(new Uri(adminHttp.BaseAddress!, $"/room/{ownId}").AbsoluteUri,
            Payload(created).GetProperty("invitationUrl").GetString());

        var rootCreated = await root.CallToolAsync("create_room", new Dictionary<string, object?> { ["name"] = "Root room", ["language"] = "sql" });
        var rootId = Payload(rootCreated).GetProperty("roomId").GetString()!;
        var listing = Payload(await admin.CallToolAsync("list_rooms"));
        Assert.Equal(2, listing.GetArrayLength());
        var own = listing.EnumerateArray().Single(room => room.GetProperty("room").GetProperty("roomId").GetString() == ownId);
        Assert.True(own.GetProperty("room").GetProperty("canDelete").GetBoolean());
        Assert.Equal(0, own.GetProperty("room").GetProperty("usersCount").GetInt32());
        var other = listing.EnumerateArray().Single(room => room.GetProperty("room").GetProperty("roomId").GetString() == rootId);
        Assert.False(other.GetProperty("room").GetProperty("canDelete").GetBoolean());

        var forbidden = await DeleteAsync(admin, rootId);
        Assert.True(forbidden.IsError);
        Assert.Contains("Forbidden", forbidden.Content.OfType<TextContentBlock>().Single().Text, StringComparison.Ordinal);
        Assert.NotEqual(true, (await DeleteAsync(root, ownId)).IsError);
        Assert.True((await DeleteAsync(admin, ownId)).IsError);
        Assert.NotEqual(true, (await DeleteAsync(root, rootId)).IsError);
        Assert.Empty(Payload(await root.CallToolAsync("list_rooms")).EnumerateArray());
    }

    [Theory]
    [InlineData("http://localhost:5454")]
    [InlineData("https://seal-code.com")]
    [InlineData("https://seal-code.com:8443")]
    public async Task InvitationsIncludeTheActualRequestDomainSchemeAndPort(string address)
    {
        using var factory = CreateFactory();
        using var http = CreateHttpClient(factory, ADMIN_TOKEN, new Uri(address));
        await using var client = await ConnectAsync(http);
        var created = Payload(await client.CallToolAsync("create_room", new Dictionary<string, object?>
        {
            ["name"] = "Absolute URL"
        }));
        var path = created.GetProperty("invitationPath").GetString()!;
        var expected = new Uri(http.BaseAddress!, path).AbsoluteUri;
        Assert.Equal(expected, created.GetProperty("invitationUrl").GetString());
        var listed = Assert.Single(Payload(await client.CallToolAsync("list_rooms")).EnumerateArray());
        Assert.Equal(path, listed.GetProperty("invitationPath").GetString());
        Assert.Equal(expected, listed.GetProperty("invitationUrl").GetString());
    }

    [Theory]
    [InlineData(true, "https")]
    [InlineData(false, "http")]
    public async Task InvitationsUseApplicationPathBaseAndOnlyTrustConfiguredProxies(bool trusted, string scheme)
    {
        using var original = CreateFactory();
        using var factory = original.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["TrustedProxies:0"] = trusted ? "127.0.0.1" : "127.0.0.2" }));
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(new ProxyRequestStartupFilter()));
        });
        using var http = CreateHttpClient(factory, ADMIN_TOKEN, new Uri("http://seal-code.com"));
        http.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        http.DefaultRequestHeaders.Add("X-Forwarded-Host", "untrusted.example");
        await using var client = await ConnectAsync(http);
        var created = Payload(await client.CallToolAsync("create_room", new Dictionary<string, object?> { ["name"] = "Proxy URL" }));
        var expected = $"{scheme}://seal-code.com/seal{created.GetProperty("invitationPath").GetString()}";
        Assert.Equal(expected, created.GetProperty("invitationUrl").GetString());
        var listed = Assert.Single(Payload(await client.CallToolAsync("list_rooms")).EnumerateArray());
        Assert.Equal(expected, listed.GetProperty("invitationUrl").GetString());
    }

    [Theory]
    [InlineData("create_room", "name", " ")]
    [InlineData("create_room", "language", "unsupported")]
    [InlineData("delete_room", "roomId", "invalid")]
    public async Task InvalidInputReturnsMcpToolErrors(string tool, string argument, string value)
    {
        using var factory = CreateFactory();
        using var http = CreateHttpClient(factory, ADMIN_TOKEN);
        await using var client = await ConnectAsync(http);
        var arguments = new Dictionary<string, object?> { [argument] = value };
        if (argument == "language")
        {
            arguments["name"] = "Test room";
        }
        var result = await client.CallToolAsync(tool, arguments);
        Assert.True(result.IsError);
        Assert.Empty(Payload(await client.CallToolAsync("list_rooms")).EnumerateArray());
    }

    [Fact]
    public async Task BrowserCookiesCannotAuthenticateOrElevateMcpRequests()
    {
        using var factory = CreateFactory();
        using var http = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = await http.GetFromJsonAsync<JsonElement>("/admin/token");
        http.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token.GetProperty("Token").GetString());
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["name"] = "Root",
            ["password"] = "pass2"
        });
        using var login = await http.PostAsync("/admin/login", form);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        using var browser = await http.GetAsync("/admin/rooms");
        Assert.Equal(HttpStatusCode.OK, browser.StatusCode);
        using var mcp = await http.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        Assert.Equal(HttpStatusCode.Unauthorized, mcp.StatusCode);

        // Refresh antiforgery for the authenticated browser identity.
        token = await http.GetFromJsonAsync<JsonElement>("/admin/token");
        http.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        http.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token.GetProperty("Token").GetString());
        using var browserCreated = await http.PostAsJsonAsync("/admin/rooms", new { Name = "Browser root room", Language = (string?)null });
        Assert.Equal(HttpStatusCode.OK, browserCreated.StatusCode);
        var rootRoom = await browserCreated.Content.ReadFromJsonAsync<JsonElement>();
        var rootId = rootRoom.GetProperty("RoomId").GetString()!;

        // A super-admin cookie must not elevate the regular admin's bearer token.
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ADMIN_TOKEN);
        await using var client = await ConnectAsync(http);
        Assert.True((await DeleteAsync(client, rootId)).IsError);
        var created = Payload(await client.CallToolAsync("create_room", new Dictionary<string, object?>
        {
            ["name"] = "MCP regular room",
            ["language"] = null
        }));
        Assert.Equal("Admin", created.GetProperty("createdBy").GetString());
        Assert.Equal(rootRoom.GetProperty("Language").GetString(), created.GetProperty("language").GetString());
        var ownId = created.GetProperty("roomId").GetString()!;

        // The browser still operates as Root and can delete the room created through MCP.
        var listing = await http.GetFromJsonAsync<JsonElement[]>("/admin/rooms");
        Assert.Equal(2, listing!.Length);
        Assert.All(listing, room => Assert.True(room.GetProperty("CanDelete").GetBoolean()));
        using var removed = await http.DeleteAsync($"/admin/rooms/{ownId}");
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        var remaining = Assert.Single(Payload(await client.CallToolAsync("list_rooms")).EnumerateArray());
        Assert.Equal(rootId, remaining.GetProperty("room").GetProperty("roomId").GetString());
        Assert.False(remaining.GetProperty("room").GetProperty("canDelete").GetBoolean());
    }

    [Theory]
    [InlineData("https://untrusted.example")]
    [InlineData("null")]
    [InlineData("invalid origin")]
    [InlineData("http://user@localhost")]
    [InlineData("http://localhost/path")]
    public async Task UntrustedOriginsAreRejected(string origin)
    {
        using var factory = CreateFactory();
        using var http = CreateHttpClient(factory, ADMIN_TOKEN);
        http.DefaultRequestHeaders.TryAddWithoutValidation("Origin", origin);
        using var response = await http.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        http.DefaultRequestHeaders.Remove("Authorization");
        using var anonymous = await http.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        Assert.Equal(HttpStatusCode.Forbidden, anonymous.StatusCode);
    }

    [Fact]
    public async Task EarlierProtocolClientsCanConnectFromTheSameOrigin()
    {
        using var factory = CreateFactory();
        using var http = CreateHttpClient(factory, ADMIN_TOKEN);
        http.DefaultRequestHeaders.Add("Origin", http.BaseAddress!.GetLeftPart(UriPartial.Authority));
        await using var client = await ConnectAsync(http, "2025-11-25");
        Assert.Equal(4, (await client.ListToolsAsync()).Count);
    }

    [Fact]
    public async Task RoomCapacityLimitsAlsoApplyToMcp()
    {
        using var factory = CreateFactory();
        using var limited = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Limits:MaxRooms"] = "1" })));
        using var http = CreateHttpClient(limited, ADMIN_TOKEN);
        await using var client = await ConnectAsync(http);
        var arguments = new Dictionary<string, object?> { ["name"] = "Capacity test" };
        Assert.NotEqual(true, (await client.CallToolAsync("create_room", arguments)).IsError);
        var rejected = await client.CallToolAsync("create_room", arguments);
        Assert.True(rejected.IsError);
        Assert.Contains("capacity", rejected.Content.OfType<TextContentBlock>().Single().Text, StringComparison.Ordinal);
        Assert.Single(Payload(await client.CallToolAsync("list_rooms")).EnumerateArray());
    }

    [Fact]
    public async Task EmptyTokenDisablesMcpAccessAndBearerDoesNotGrantBrowserAccess()
    {
        using var factory = CreateFactory();
        using var disabled = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["AdminUsers:0:McpToken"] = "" })));
        using var http = CreateHttpClient(disabled, ADMIN_TOKEN);
        using var rejected = await http.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);

        using var enabledHttp = CreateHttpClient(factory, ADMIN_TOKEN);
        using var browser = await enabledHttp.GetAsync("/admin/rooms");
        Assert.Equal(HttpStatusCode.Unauthorized, browser.StatusCode);
    }

    private static Task<CallToolResult> DeleteAsync(McpClient client, string id)
        => client.CallToolAsync("delete_room", new Dictionary<string, object?> { ["roomId"] = id }).AsTask();

    private static JsonElement Payload(CallToolResult result)
        => JsonSerializer.Deserialize<JsonElement>(result.Content.OfType<TextContentBlock>().Single().Text);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "McpClient owns the transport on success; failure disposes it explicitly.")]
    private static async Task<McpClient> ConnectAsync(HttpClient http, string? protocolVersion = null)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "/mcp")
        }, http);
        try
        {
            return await McpClient.CreateAsync(transport, new McpClientOptions { ProtocolVersion = protocolVersion });
        }
        catch
        {
            await transport.DisposeAsync();
            throw;
        }
    }

    private static HttpClient CreateHttpClient(WebApplicationFactory<Program> factory, string token, Uri? baseAddress = null)
    {
        var http = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = baseAddress ?? new Uri("http://localhost")
        });
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static McpAppFactory CreateFactory() => new();

    private sealed class ProxyRequestStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
            => app =>
            {
                app.Use((context, proceed) =>
                {
                    context.Connection.RemoteIpAddress = IPAddress.Loopback;
                    context.Request.PathBase = new PathString("/seal");
                    return proceed(context);
                });
                next(app);
            };
    }

    private sealed class McpAppFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["AdminUsers:0:Name"] = "Admin",
                    ["AdminUsers:0:Password"] = "pass1",
                    ["AdminUsers:0:PasswordHash"] = null,
                    ["AdminUsers:0:IsSuperAdmin"] = "false",
                    ["AdminUsers:0:McpToken"] = ADMIN_TOKEN,
                    ["AdminUsers:1:Name"] = "Root",
                    ["AdminUsers:1:Password"] = "pass2",
                    ["AdminUsers:1:PasswordHash"] = null,
                    ["AdminUsers:1:IsSuperAdmin"] = "true",
                    ["AdminUsers:1:McpToken"] = ROOT_TOKEN,
                    ["Languages:0"] = "csharp",
                    ["Languages:1"] = "sql",
                    ["MaxUsersPerRoom"] = "3"
                }));
        }
    }
}
