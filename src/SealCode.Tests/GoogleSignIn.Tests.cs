using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Abstractions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Models;

using SealCode.Security;

namespace SealCode.Tests;

public sealed class GoogleSignInTests
{
    private const string ClientId = "example-client.apps.googleusercontent.com";

    [Fact]
    public async Task MalformedGoogleTokenIsRejected()
    {
        var verifier = new GoogleIdentityVerifier();
        Assert.Null(await verifier.VerifyAsync("not-a-jwt", ClientId));
    }

    [Fact]
    public async Task GoogleSignInIsDisabledByDefault()
    {
        using var factory = new SealCodeApiTests.SealCodeAppFactory();
        using var client = factory.CreateClient();
        var configuration = await client.GetFromJsonAsync<JsonElement>("/admin/google-config");
        Assert.True(string.IsNullOrEmpty(configuration.GetProperty("ClientId").GetString()));
        await SetAntiforgeryAsync(client);
        using var response = await client.PostAsJsonAsync("/admin/google-login", new { Credential = "anything" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task VerifiedHostedAccountGetsOrdinaryAdminSessionWithoutMcpAccess()
    {
        using var source = new SealCodeApiTests.SealCodeAppFactory();
        using var factory = Configure(source, new("google-subject-1", "person@example.org", "example.org", true));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var loginPage = await client.GetAsync("/admin/login");
        Assert.Contains("admin-login-main.js", await loginPage.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("https://accounts.google.com/gsi/client",
            loginPage.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        var configuration = await client.GetFromJsonAsync<JsonElement>("/admin/google-config");
        Assert.Equal(ClientId, configuration.GetProperty("ClientId").GetString());
        using var missingCsrf = await client.PostAsJsonAsync("/admin/google-login", new { Credential = "valid" });
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);

        await SetAntiforgeryAsync(client);
        using var login = await client.PostAsJsonAsync("/admin/google-login", new { Credential = "valid" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        AddResponseCookie(client, login, "admin_auth");
        await SetAntiforgeryAsync(client);

        using var adminPage = await client.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.OK, adminPage.StatusCode);
        var otherRoom = factory.Services.GetRequiredService<IRoomManager>()
            .CreateRoom(new RoomName("Someone else's room"), new RoomLanguage("sql"), new AdminUser("Other"));
        using var rooms = await client.GetAsync("/admin/rooms");
        Assert.Equal(HttpStatusCode.OK, rooms.StatusCode);
        var roomList = await rooms.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.False(roomList!.Single(room => room.GetProperty("RoomId").GetString() == otherRoom.RoomId.Value)
            .GetProperty("CanDelete").GetBoolean());
        using var forbidden = await client.DeleteAsync($"/admin/rooms/{otherRoom.RoomId.Value}");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var mcp = await client.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        Assert.Equal(HttpStatusCode.Unauthorized, mcp.StatusCode);

        await SetAntiforgeryAsync(client);
        using var created = await client.PostAsJsonAsync("/admin/rooms", new { Name = "Owned", Language = "sql" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var room = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("person@example.org", room.GetProperty("CreatedBy").GetString());
        using var deleted = await client.DeleteAsync($"/admin/rooms/{room.GetProperty("RoomId").GetString()}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
    }

    [Theory]
    [InlineData("person@example.org", "another.org", true)]
    [InlineData("person@example.org", "", true)]
    [InlineData("person@example.org", "example.org", false)]
    public async Task UntrustedGoogleClaimsCannotCreateAdminSession(string email, string hostedDomain, bool verified)
    {
        using var source = new SealCodeApiTests.SealCodeAppFactory();
        using var factory = Configure(source, new("google-subject-2", email, hostedDomain, verified));
        using var client = factory.CreateClient();
        await SetAntiforgeryAsync(client);
        using var login = await client.PostAsJsonAsync("/admin/google-login", new { Credential = "valid" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.DoesNotContain(login.Headers.TryGetValues("Set-Cookie", out var values) ? values : [],
            value => value.StartsWith("admin_auth=", StringComparison.Ordinal));
    }

    private static WebApplicationFactory<Program> Configure(
        SealCodeApiTests.SealCodeAppFactory source,
        GoogleIdentity identity)
        => source.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["GoogleSignIn:ClientId"] = ClientId,
                    ["GoogleSignIn:AllowedHostedDomains:0"] = "example.org"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IGoogleIdentityVerifier>();
                services.AddSingleton<IGoogleIdentityVerifier>(new FixedGoogleIdentityVerifier(identity));
            });
        });

    private static async Task SetAntiforgeryAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/admin/token");
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (response.Headers.TryGetValues("Set-Cookie", out _))
        {
            AddResponseCookie(client, response, ".AspNetCore.Antiforgery");
        }
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", payload.GetProperty("Token").GetString());
    }

    private static void AddResponseCookie(HttpClient client, HttpResponseMessage response, string prefix)
    {
        var cookie = response.Headers.GetValues("Set-Cookie")
            .Select(value => value.Split(';')[0])
            .First(value => value.StartsWith(prefix, StringComparison.Ordinal));
        var previous = client.DefaultRequestHeaders.TryGetValues("Cookie", out var values)
            ? string.Join("; ", values) : "";
        var retained = previous.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(value => !value.StartsWith(prefix, StringComparison.Ordinal));
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", retained.Append(cookie)));
    }

    private sealed class FixedGoogleIdentityVerifier(GoogleIdentity identity) : IGoogleIdentityVerifier
    {
        public Task<GoogleIdentity?> VerifyAsync(string credential, string clientId)
            => Task.FromResult<GoogleIdentity?>(credential == "valid" && clientId == ClientId ? identity : null);
    }
}
