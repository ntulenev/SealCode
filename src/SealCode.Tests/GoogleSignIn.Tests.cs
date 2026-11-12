using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Abstractions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using Models;
using Models.Configuration;

using SealCode.Hosting;
using SealCode.Security;

namespace SealCode.Tests;

public sealed class GoogleSignInTests
{
    [Fact]
    public async Task VerifiedGoogleAccountCanSignInWithoutAStatisticsDatabase()
    {
        using var source = new SealCodeApiTests.SealCodeAppFactory();
        using var configured = Configure(source, new("google-without-database", "person@example.org", "example.org", true));
        using var factory = configured.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:SealCode"] = "" }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IUsageStatisticsRepository>();
                services.AddSealCodeStatistics();
            });
        });
        using var client = factory.CreateClient();
        await SetAntiforgeryAsync(client);
        using var login = await client.PostAsJsonAsync("/admin/google-login", new { Credential = "valid" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        AddResponseCookie(client, login, "admin_auth");
        var user = await client.GetFromJsonAsync<JsonElement>("/admin/me");
        Assert.Equal("person@example.org", user.GetProperty("DisplayName").GetString());
        Assert.False(user.GetProperty("StatisticsEnabled").GetBoolean());
        Assert.IsType<DisabledUsageStatisticsRepository>(factory.Services.GetRequiredService<IUsageStatisticsRepository>());
    }

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
        var statistics = (TestUsageStatisticsRepository)factory.Services.GetRequiredService<IUsageStatisticsRepository>();
        Assert.Equal("google", Assert.Single(statistics.Logins).Provider);
        Assert.Equal("google:google-subject-1", statistics.Logins[0].User.Name);
        AddResponseCookie(client, login, "admin_auth");
        await SetAntiforgeryAsync(client);

        using var adminPage = await client.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.OK, adminPage.StatusCode);
        var currentUser = await client.GetFromJsonAsync<JsonElement>("/admin/me");
        Assert.Equal("person@example.org", currentUser.GetProperty("DisplayName").GetString());
        var otherRoom = factory.Services.GetRequiredService<IRoomManager>()
            .CreateRoom(new RoomName("Someone else's room"), new RoomLanguage("sql"),
                new AdminUser("google:GOOGLE-subject-1", displayName: "person@example.org"));
        var emptyRooms = await client.GetFromJsonAsync<JsonElement[]>("/admin/rooms?mine=true");
        Assert.Empty(emptyRooms!);
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
        var ownRooms = await client.GetFromJsonAsync<JsonElement[]>("/admin/rooms?mine=true");
        Assert.Equal(room.GetProperty("RoomId").GetString(),
            Assert.Single(ownRooms!).GetProperty("RoomId").GetString());
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

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("unlisted.org", false)]
    [InlineData(null, true)]
    public async Task ExplicitEmailGetsOrdinaryAdminSessionAndRemovalRevokesAccess(string? hostedDomain, bool emailsOnly)
    {
        using var source = new SealCodeApiTests.SealCodeAppFactory();
        using var factory = Configure(source, new("individual-account", "User123@gmail.com", hostedDomain, true), emailsOnly);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SetAntiforgeryAsync(client);
        using var login = await client.PostAsJsonAsync("/admin/google-login", new { Credential = "valid" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        AddResponseCookie(client, login, "admin_auth");
        var user = await client.GetFromJsonAsync<JsonElement>("/admin/me");
        Assert.Equal("User123@gmail.com", user.GetProperty("DisplayName").GetString());
        var otherRoom = factory.Services.GetRequiredService<IRoomManager>()
            .CreateRoom(new RoomName("Other owner's room"), new RoomLanguage("sql"), new AdminUser("other"));
        await SetAntiforgeryAsync(client);
        using var deletion = await client.DeleteAsync($"/admin/rooms/{otherRoom.RoomId.Value}");
        Assert.Equal(HttpStatusCode.Forbidden, deletion.StatusCode);
        using var mcp = await client.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        Assert.Equal(HttpStatusCode.Unauthorized, mcp.StatusCode);

        factory.Services.GetRequiredService<IOptions<ApplicationConfiguration>>().Value.GoogleSignIn.AllowedEmails[0]
            = "replacement@gmail.com";
        using var revoked = await client.GetAsync("/admin/me");
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }

    [Theory]
    [InlineData("other@gmail.com", null, true)]
    [InlineData("user123@gmail.com.evil.org", null, true)]
    [InlineData("prefixuser123@gmail.com", null, true)]
    [InlineData("user123@gmail.com", null, false)]
    [InlineData("user123@gmail.com", "example.org", false)]
    public async Task EmailExceptionsDoNotAdmitOtherOrUnverifiedAccounts(string email, string? hostedDomain, bool verified)
    {
        using var source = new SealCodeApiTests.SealCodeAppFactory();
        using var factory = Configure(source, new("individual-account", email, hostedDomain, verified));
        using var client = factory.CreateClient();
        await SetAntiforgeryAsync(client);
        using var login = await client.PostAsJsonAsync("/admin/google-login", new { Credential = "valid" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.DoesNotContain(login.Headers.TryGetValues("Set-Cookie", out var values) ? values : [],
            value => value.StartsWith("admin_auth=", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    [InlineData("overlong")]
    public void EmailExceptionsStillRequireValidSubject(string? subject)
    {
        var config = new GoogleSignInConfiguration { AllowedEmails = ["user123@gmail.com"] };
        var identity = new GoogleIdentity(subject == "overlong" ? new string('a', 256) : subject!, "user123@gmail.com", null, true);
        Assert.False(GoogleSignInAccess.IsAllowed(identity, config));
        Assert.False(GoogleSignInAccess.IsAllowed(null, config));
    }

    private static WebApplicationFactory<Program> Configure(
        SealCodeApiTests.SealCodeAppFactory source,
        GoogleIdentity identity,
        bool emailsOnly = false)
        => source.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var settings = new Dictionary<string, string?>
                {
                    ["GoogleSignIn:ClientId"] = ClientId,
                    ["GoogleSignIn:AllowedEmails:0"] = "user123@gmail.com"
                };
                if (!emailsOnly)
                {
                    settings["GoogleSignIn:AllowedHostedDomains:0"] = "example.org";
                }
                config.AddInMemoryCollection(settings);
            });
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
            => Task.FromResult(credential == "valid" && clientId == ClientId ? identity : null);
    }
}
