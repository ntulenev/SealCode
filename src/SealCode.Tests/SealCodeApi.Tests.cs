using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;

using FluentAssertions;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Models;

using Transport.Models;

namespace SealCode.Tests;

public sealed class SealCodeApiTests(SealCodeApiTests.SealCodeAppFactory readFactory)
    : IClassFixture<SealCodeApiTests.SealCodeAppFactory>
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConnectionCapacityRejectionClosesClientAndReleasesPermit()
    {
        using var original = CreateFactory();
        using var factory = original.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Limits:MaxConnections"] = "1" })));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var cookie = await LoginAsAdminAsync(client);
        var roomId = await CreateRoomAsync(client, cookie);
        await using var first = await CreateConnectionAsync(factory, client.BaseAddress!, cookie);
        _ = await first.InvokeAsync<JoinRoomResult>("JoinRoom", roomId, "Alice");
        await using var rejected = new HubConnectionBuilder()
            .WithUrl(new Uri(client.BaseAddress!, "/roomHub"), options =>
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler())
            .Build();
        var closed = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        rejected.Closed += error =>
        {
            closed.TrySetResult(error);
            return Task.CompletedTask;
        };
        var rejection = await Record.ExceptionAsync(() => rejected.StartAsync());
        rejection ??= await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        rejection.Should().NotBeNull();
        rejection!.Message.Should().Contain("RateLimited");
        rejected.State.Should().Be(HubConnectionState.Disconnected);
        await FluentActions.Awaiting(() => rejected.InvokeAsync<RoomRevisions>("GetRoomRevisions", roomId))
            .Should().ThrowAsync<InvalidOperationException>();

        await first.StopAsync();
        await using var replacement = await CreateConnectionAsync(factory, client.BaseAddress!, cookie);
        var joined = await replacement.InvokeAsync<JoinRoomResult>("JoinRoom", roomId, "Bob");
        joined.Users.Should().ContainSingle().Which.Should().Be("Bob");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnlyTrustedProxiesCanChangeLoginSource(bool trusted)
    {
        using var original = CreateFactory();
        using var factory = original.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["TrustedProxies:0"] = trusted ? "127.0.0.1" : "127.0.0.2" }));
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(new LoopbackStartupFilter()));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.1");
        for (var index = 0; index < 30; index++)
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["name"] = $"proxy-user-{index}",
                ["password"] = "wrong"
            });
            using var response = await SecurePostAsync(client, "/admin/login", content);
            response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        }
        var firstBucket = (uint)StringComparer.Ordinal.GetHashCode("198.51.100.1") % 256;
        var other = Enumerable.Range(2, 200).Select(index => $"198.51.100.{index}").First(address =>
            (uint)StringComparer.Ordinal.GetHashCode(address) % 256 != firstBucket);
        client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", other);
        using var lastContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["name"] = "proxy-final-user",
            ["password"] = "wrong"
        });
        using var last = await SecurePostAsync(client, "/admin/login", lastContent);
        last.StatusCode.Should().Be(trusted ? HttpStatusCode.Redirect : HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task AdminValidationDefaultsAndMissingRoomResponsesAreExplicit()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var unauthorized = await client.GetAsync("/admin");
        unauthorized.StatusCode.Should().Be(HttpStatusCode.Redirect);
        unauthorized.Headers.Location!.OriginalString.Should().Be("/admin/login");
        _ = await LoginAsAdminAsync(client);
        using var invalid = await SecureJsonAsync(client, "/admin/rooms", new { Name = "" });
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await invalid.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("title").GetString().Should().Be("InvalidInput");
        using var created = await SecureJsonAsync(client, "/admin/rooms", new { Name = "Default language" });
        var payload = await created.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("Language").GetString().Should().Be("csharp");
        using var missing = await SecureDeleteAsync(client, $"/admin/rooms/{RoomId.New().Value}");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var own = await SecureDeleteAsync(client, $"/admin/rooms/{payload.GetProperty("RoomId").GetString()}");
        own.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdminLimitsRequireLoginAndExposeOnlyEffectiveLimits()
    {
        using var source = CreateFactory();
        using var factory = source.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["MaxUsersPerRoom"] = "2",
                    ["Limits:MaxRooms"] = "7",
                    ["Limits:MaxConnections"] = "12",
                    ["Limits:MaxConnectionsPerSource"] = "3",
                    ["Limits:RoomIdleTimeoutMinutes"] = "45"
                })));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var denied = await client.GetAsync("/admin/limits/config");
        denied.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var loginRedirect = await client.GetAsync("/admin/limits");
        loginRedirect.StatusCode.Should().Be(HttpStatusCode.Redirect);
        loginRedirect.Headers.Location!.OriginalString.Should().Be("/admin/login");

        _ = await LoginAsAdminAsync(client);
        using var page = await client.GetAsync("/admin/limits");
        page.StatusCode.Should().Be(HttpStatusCode.OK);
        using var response = await client.GetAsync("/admin/limits/config");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo("MaxUsersPerRoom", "Limits");
        payload.GetProperty("MaxUsersPerRoom").GetInt32().Should().Be(2);
        var limits = payload.GetProperty("Limits");
        limits.GetProperty("MaxRooms").GetInt32().Should().Be(7);
        limits.GetProperty("MaxConnections").GetInt32().Should().Be(12);
        limits.GetProperty("MaxConnectionsPerSource").GetInt32().Should().Be(3);
        limits.GetProperty("RoomIdleTimeoutMinutes").GetInt32().Should().Be(45);
        var expected = factory.Services.GetRequiredService<IOptions<Models.Configuration.ApplicationConfiguration>>().Value.Limits;
        limits.GetRawText().Should().Be(JsonSerializer.Serialize(expected));
    }

    [Fact]
    public async Task AllAdminsSeeAllRoomsWithRoleSpecificDeleteRights()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var owner = await LoginAsAdminAsync(client);
        var ownId = await CreateRoomAsync(client, owner);
        var root = await LoginAsync(client, "Root", "pass2");
        var otherId = await CreateRoomAsync(client, root);
        var rootRooms = await client.GetFromJsonAsync<JsonElement[]>("/admin/rooms");
        rootRooms.Should().HaveCount(2);
        rootRooms!.All(room => room.GetProperty("CanDelete").GetBoolean()).Should().BeTrue();
        var rootOwnRooms = await client.GetFromJsonAsync<JsonElement[]>("/admin/rooms?mine=true");
        rootOwnRooms.Should().ContainSingle().Which.GetProperty("RoomId").GetString().Should().Be(otherId);
        var rootUser = await client.GetFromJsonAsync<JsonElement>("/admin/me");
        rootUser.GetProperty("DisplayName").GetString().Should().Be("Root");
        var rootPermission = await client.GetFromJsonAsync<JsonElement>($"/room/{otherId}/permissions");
        rootPermission.GetProperty("CanDelete").GetBoolean().Should().BeTrue();
        owner = await LoginAsAdminAsync(client);
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", $"{owner.Name}={owner.Value}");
        var rooms = await client.GetFromJsonAsync<JsonElement[]>("/admin/rooms");
        rooms.Should().HaveCount(2);
        rooms!.Single(room => room.GetProperty("RoomId").GetString() == ownId).GetProperty("CanDelete").GetBoolean().Should().BeTrue();
        rooms.Single(room => room.GetProperty("RoomId").GetString() == otherId).GetProperty("CanDelete").GetBoolean().Should().BeFalse();
        var ownRooms = await client.GetFromJsonAsync<JsonElement[]>("/admin/rooms?mine=true");
        ownRooms.Should().ContainSingle().Which.GetProperty("RoomId").GetString().Should().Be(ownId);
        var allRooms = await client.GetFromJsonAsync<JsonElement[]>("/admin/rooms?mine=false");
        allRooms.Should().HaveCount(2);
        using var currentUser = await client.GetAsync("/admin/me");
        currentUser.Headers.CacheControl!.NoStore.Should().BeTrue();
        var user = await currentUser.Content.ReadFromJsonAsync<JsonElement>();
        user.GetProperty("DisplayName").GetString().Should().Be("Admin");
        var ownPermission = await client.GetFromJsonAsync<JsonElement>($"/room/{ownId}/permissions");
        ownPermission.GetProperty("CanDelete").GetBoolean().Should().BeTrue();
        var otherPermission = await client.GetFromJsonAsync<JsonElement>($"/room/{otherId}/permissions");
        otherPermission.GetProperty("CanDelete").GetBoolean().Should().BeFalse();
        using var missingPermission = await client.GetAsync($"/admin/rooms/{RoomId.New().Value}");
        missingPermission.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task StaticFilesRevalidateEntryPointsAndCacheHashedAssets()
    {
        var root = Path.Combine(Path.GetTempPath(), $"sealcode-static-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "room.html"), "room");
            await File.WriteAllTextAsync(Path.Combine(root, "room-ABCDEFGH.html"), "room");
            await File.WriteAllTextAsync(Path.Combine(root, "chunk-ABCDEFGH.js"), "export {};");
            using var baseFactory = CreateFactory();
            using var factory = baseFactory.WithWebHostBuilder(builder => builder.UseWebRoot(root));
            using var client = factory.CreateClient();
            using var entry = await client.GetAsync("/room.html");
            using var chunk = await client.GetAsync("/chunk-ABCDEFGH.js");
            entry.StatusCode.Should().Be(HttpStatusCode.OK);
            entry.Headers.CacheControl!.NoCache.Should().BeTrue();
            chunk.StatusCode.Should().Be(HttpStatusCode.OK);
            chunk.Headers.CacheControl!.ToString().Should().Contain("immutable");
            chunk.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromDays(365));
            using var hashedHtml = await client.GetAsync("/room-ABCDEFGH.html");
            hashedHtml.StatusCode.Should().Be(HttpStatusCode.OK);
            hashedHtml.Headers.CacheControl!.NoCache.Should().BeTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RoutedHtmlPagesAlwaysRevalidate()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var login = await client.GetAsync("/admin/login");
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        login.Headers.CacheControl!.NoCache.Should().BeTrue();
        var cookie = await LoginAsAdminAsync(client);
        var roomId = await CreateRoomAsync(client, cookie);
        foreach (var path in new[] { "/admin", $"/room/{roomId}" })
        {
            using var response = await client.GetAsync(path);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Headers.CacheControl!.NoCache.Should().BeTrue();
        }
    }

    [Fact]
    public async Task GetVersionShouldReturnRunningAssemblyMetadataWithoutCaching()
    {
        using var client = readFactory.CreateClient();
        var expected = typeof(RoomHub).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

        var response = await client.GetAsync("/version");
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        payload.GetProperty("InformationalVersion").GetString().Should().Be(expected);
        payload.GetProperty("Version").GetString().Should().Be(expected.Split('+', 2)[0]);
        payload.GetProperty("Commit").GetString().Should().MatchRegex("^[0-9a-f]{40}$");
        expected.Should().EndWith(payload.GetProperty("Commit").GetString());
    }

    [Fact(DisplayName = "GET /health returns ok")]
    [Trait("Category", "Integration")]
    public async Task GetHealthReturnsOk()
    {
        using var client = readFactory.CreateClient();

        var response = await client.GetAsync("/health");
        var payload = await response.Content.ReadFromJsonAsync<string>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        payload.Should().Be("ok");
    }

    [Fact(DisplayName = "GET /languages returns configured languages")]
    [Trait("Category", "Integration")]
    public async Task GetLanguagesReturnsConfiguredLanguages()
    {
        using var client = readFactory.CreateClient();

        var response = await client.GetAsync("/languages");
        var payload = await response.Content.ReadFromJsonAsync<string[]>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        payload.Should().NotBeNull();
        payload!.Should().Contain(["csharp", "sql"]);
    }

    [Fact(DisplayName = "GET /features returns collaborative undo flag")]
    [Trait("Category", "Integration")]
    public async Task GetFeaturesReturnsCollaborativeUndoFlag()
    {
        using var client = readFactory.CreateClient();

        var response = await client.GetAsync("/features");
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        payload.GetProperty("CollaborativeUndoEnabled").GetBoolean().Should().BeTrue();
        payload.GetProperty("Limits").GetProperty("CheckpointEveryRevisions").GetInt32().Should().Be(64);
        payload.GetProperty("Limits").GetProperty("CheckpointEveryBase64Bytes").GetInt32().Should().Be(256 * 1024);
        var advertisedLimit = payload.GetProperty("MaxYjsMessageBytes").GetInt64();
        advertisedLimit.Should().Be(16 * 1024 * 1024);
        advertisedLimit.Should().Be(readFactory.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.SignalR.HubOptions>>()
            .Value.MaximumReceiveMessageSize);
    }

    [Fact(DisplayName = "GET /admin/rooms returns unauthorized without cookie")]
    [Trait("Category", "Integration")]
    public async Task GetAdminRoomsWithoutCookieReturnsUnauthorized()
    {
        using var client = readFactory.CreateClient();

        var response = await client.GetAsync("/admin/rooms");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var roomPermission = await client.GetAsync($"/admin/rooms/{RoomId.New().Value}");
        roomPermission.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var currentUser = await client.GetAsync("/admin/me");
        currentUser.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var ownRooms = await client.GetAsync("/admin/rooms?mine=true");
        ownRooms.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GuestRoomPermissionsReturnFalseWithoutBrowserErrors()
    {
        using var factory = CreateFactory();
        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var owner = await LoginAsAdminAsync(admin);
        var roomId = await CreateRoomAsync(admin, owner);
        using var guest = factory.CreateClient();
        using var response = await guest.GetAsync($"/room/{roomId}/permissions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var permission = await response.Content.ReadFromJsonAsync<JsonElement>();
        permission.GetProperty("CanDelete").GetBoolean().Should().BeFalse();
    }

    [Fact(DisplayName = "POST /admin/rooms returns unauthorized without cookie")]
    [Trait("Category", "Integration")]
    public async Task CreateRoomWithoutCookieReturnsUnauthorized()
    {
        using var client = readFactory.CreateClient();

        var response = await SecureJsonAsync(client, "/admin/rooms", new
        {
            Name = "Room",
            Language = "csharp"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "POST /admin/login sets cookie and allows admin room creation")]
    [Trait("Category", "Integration")]
    public async Task LoginThenCreateRoomReturnsRoomDetails()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["name"] = "Admin",
            ["password"] = "pass1"
        });

        var loginResponse = await SecurePostAsync(client, "/admin/login", loginContent);

        loginResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
        loginResponse.Headers.TryGetValues("Set-Cookie", out var setCookies).Should().BeTrue();
        var setCookie = setCookies!.First();
        var cookie = setCookie.Split(';', 2)[0];

        client.DefaultRequestHeaders.Add("Cookie", cookie);

        var createResponse = await SecureJsonAsync(client, "/admin/rooms", new
        {
            Name = "Room",
            Language = "sql"
        });
        var payload = await createResponse.Content.ReadFromJsonAsync<JsonElement>();

        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        payload.GetProperty("RoomId").GetString().Should().NotBeNullOrWhiteSpace();
        payload.GetProperty("Name").GetString().Should().Be("Room");
        payload.GetProperty("Language").GetString().Should().Be("sql");
        payload.GetProperty("CreatedBy").GetString().Should().Be("Admin");
    }

    [Fact(DisplayName = "GET /room/{id} returns not found for missing room")]
    [Trait("Category", "Integration")]
    public async Task GetRoomReturnsNotFoundForMissingRoom()
    {
        using var client = readFactory.CreateClient();

        var missingRoomId = RoomId.New().Value;
        var response = await client.GetAsync($"/room/{missingRoomId}");
        var payload = await response.Content.ReadFromJsonAsync<string>();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        payload.Should().Be("Room not found");
    }

    [Fact(DisplayName = "SignalR /roomHub allows joining a room")]
    [Trait("Category", "Integration")]
    public async Task JoinRoomOverSignalRReturnsRoomState()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var cookie = await LoginAsAdminAsync(client);
        var roomId = await CreateRoomAsync(client, cookie);

        var cookieContainer = new CookieContainer();
        cookieContainer.Add(client.BaseAddress!, cookie);

        var hubUrl = new Uri(client.BaseAddress!, "/roomHub");
        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Cookies = cookieContainer;
            })
            .Build();

        await connection.StartAsync();

        var result = await connection.InvokeAsync<JoinRoomResult>("JoinRoom", roomId, "Alice");

        result.Name.Should().Be("Room");
        result.Language.Should().Be("sql");
        result.Text.Should().Be(string.Empty);
        result.Version.Should().BeGreaterThanOrEqualTo(1);
        result.Users.Should().ContainSingle().Which.Should().Be("Alice");
        result.CreatedBy.Should().Be("Admin");
        result.DocumentRevision.Should().Be(0);
        result.CheckpointRevision.Should().Be(0);
        result.YjsState.Should().BeNull();
        result.YjsUpdates.Should().BeEmpty();

        await connection.DisposeAsync();
    }

    [Theory]
    [InlineData("UserJoined")]
    [InlineData("YjsUpdated")]
    [InlineData("YjsCheckpointed")]
    [InlineData("LanguageUpdated")]
    [InlineData("CursorUpdated")]
    [InlineData("UserSelection")]
    [InlineData("UserCopy")]
    [InlineData("UserLeft")]
    [Trait("Category", "Integration")]
    public async Task SignalRRoomHubBroadcastsRoomEvents(string eventName)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var cookie = await LoginAsAdminAsync(client);
        var roomId = await CreateRoomAsync(client, cookie);

        await using var alice = await CreateConnectionAsync(factory, client.BaseAddress!, cookie);
        await using var bob = await CreateConnectionAsync(factory, client.BaseAddress!, cookie);

        var bobJoined = new TaskCompletionSource<(string name, string[] users)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var bobJoinedSubscription = bob.On<string, string[], long>("UserJoined", (name, users, revision) =>
            bobJoined.TrySetResult((name, users)));

        var bobJoin = await bob.InvokeAsync<JoinRoomResult>("JoinRoom", roomId, "Bob");
        bobJoin.Users.Should().ContainSingle().Which.Should().Be("Bob");

        var aliceJoin = await alice.InvokeAsync<JoinRoomResult>("JoinRoom", roomId, "Alice");
        aliceJoin.Users.Should().Contain(["Alice", "Bob"]);

        var joinedPayload = await bobJoined.Task.WaitAsync(TimeSpan.FromSeconds(5));
        joinedPayload.name.Should().Be("Alice");
        joinedPayload.users.Should().Contain(["Alice", "Bob"]);

        if (eventName is "YjsUpdated" or "YjsCheckpointed")
        {
            var yjsUpdated = new TaskCompletionSource<(string update, long documentRevision, int roomVersion, string author)>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (bob.On<string, long, int, string>("YjsUpdated", (update, documentRevision, roomVersion, author) =>
                yjsUpdated.TrySetResult((update, documentRevision, roomVersion, author))))
            {
                var update = Convert.ToBase64String(new byte[] { 1, 2, 3 });
                await alice.InvokeAsync("UpdateYjs", roomId, update);

                var payload = await yjsUpdated.Task.WaitAsync(TimeSpan.FromSeconds(5));
                payload.update.Should().Be(update);
                payload.author.Should().Be("Alice");
                payload.documentRevision.Should().Be(1);
                payload.roomVersion.Should().BeGreaterThanOrEqualTo(2);

                await using var charlie = await CreateConnectionAsync(factory, client.BaseAddress!, cookie);
                var charlieJoin = await charlie.InvokeAsync<JoinRoomResult>("JoinRoom", roomId, "Charlie");
                charlieJoin.DocumentRevision.Should().Be(1);
                charlieJoin.CheckpointRevision.Should().Be(0);
                charlieJoin.YjsState.Should().BeNull();
                charlieJoin.YjsUpdates.Should().ContainSingle();
                charlieJoin.YjsUpdates[0].Update.Should().Be(update);

                var sync = await alice.InvokeAsync<JoinRoomResult>("SyncYjs", roomId);
                sync.Should().BeEquivalentTo(charlieJoin);
                (await alice.InvokeAsync<long>("GetDocumentRevision", roomId)).Should().Be(1);

                var checkpointed = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var subscription = bob.On<long>("YjsCheckpointed", revision => checkpointed.TrySetResult(revision));
                var state = Convert.ToBase64String(new byte[] { 5, 6, 7 });
                var accepted = await alice.InvokeAsync<bool>("CheckpointYjs", roomId, state, "snapshot", payload.documentRevision);
                accepted.Should().BeTrue();
                (await checkpointed.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(payload.documentRevision);

                var charlieJoinAfterCheckpoint = await charlie.InvokeAsync<JoinRoomResult>("JoinRoom", roomId, "Charlie");
                charlieJoinAfterCheckpoint.DocumentRevision.Should().Be(1);
                charlieJoinAfterCheckpoint.CheckpointRevision.Should().Be(1);
                charlieJoinAfterCheckpoint.YjsState.Should().Be(state);
                charlieJoinAfterCheckpoint.YjsUpdates.Should().BeEmpty();
            }
        }
        if (eventName == "LanguageUpdated")
        {
            var languageUpdated = new TaskCompletionSource<(string language, int version)>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (bob.On<string, int, long>("LanguageUpdated", (language, version, revision) =>
                languageUpdated.TrySetResult((language, version))))
            {
                await alice.InvokeAsync("SetLanguage", roomId, "sql");
                var payload = await languageUpdated.Task.WaitAsync(TimeSpan.FromSeconds(5));
                payload.language.Should().Be("sql");
                payload.version.Should().BeGreaterThanOrEqualTo(2);
            }
        }
        if (eventName == "CursorUpdated")
        {
            var cursorUpdated = new TaskCompletionSource<(string author, int position)>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (bob.On<string, int>("CursorUpdated", (author, position) =>
                cursorUpdated.TrySetResult((author, position))))
            {
                await alice.InvokeAsync("UpdateCursor", roomId, 42);
                var payload = await cursorUpdated.Task.WaitAsync(TimeSpan.FromSeconds(5));
                payload.author.Should().Be("Alice");
                payload.position.Should().Be(42);
            }
        }
        if (eventName == "UserSelection")
        {
            var selectionUpdated = new TaskCompletionSource<(string author, bool isMultiLine)>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (bob.On<string, bool>("UserSelection", (author, isMultiLine) =>
                selectionUpdated.TrySetResult((author, isMultiLine))))
            {
                await alice.InvokeAsync("UpdateSelection", roomId, true);
                var payload = await selectionUpdated.Task.WaitAsync(TimeSpan.FromSeconds(5));
                payload.author.Should().Be("Alice");
                payload.isMultiLine.Should().BeTrue();
            }
        }
        if (eventName == "UserCopy")
        {
            var copyUpdated = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (bob.On<string>("UserCopy", author => copyUpdated.TrySetResult(author)))
            {
                await alice.InvokeAsync("UpdateCopy", roomId);
                var author = await copyUpdated.Task.WaitAsync(TimeSpan.FromSeconds(5));
                author.Should().Be("Alice");
            }
        }
        if (eventName == "UserLeft")
        {
            var leftUpdated = new TaskCompletionSource<(string author, string[] users)>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (bob.On<string, string[], long>("UserLeft", (author, users, revision) =>
                leftUpdated.TrySetResult((author, users))))
            {
                await alice.InvokeAsync("LeaveRoom", roomId);
                var payload = await leftUpdated.Task.WaitAsync(TimeSpan.FromSeconds(5));
                payload.author.Should().Be("Alice");
            }
        }

    }

    [Fact(DisplayName = "DELETE /admin/rooms forbids non-super admins deleting others rooms")]
    [Trait("Category", "Integration")]
    public async Task DeleteRoomForOtherAdminReturnsForbidden()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });

        var rootCookie = await LoginAsync(client, "Root", "pass2");
        var roomId = await CreateRoomAsync(client, rootCookie);

        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", $"{rootCookie.Name}={rootCookie.Value}");

        var roomsResponse = await client.GetAsync("/admin/rooms");
        roomsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var roomsPayload = await roomsResponse.Content.ReadFromJsonAsync<JsonElement[]>();
        roomsPayload.Should().NotBeNull();
        var createdRoom = roomsPayload!.First(room => room.GetProperty("RoomId").GetString() == roomId);
        createdRoom.GetProperty("CreatedBy").GetString().Should().Be("Root");

        var adminCookie = await LoginAsync(client, "Admin", "pass1");
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", $"{adminCookie.Name}={adminCookie.Value}");

        var forbiddenResponse = await SecureDeleteAsync(client, $"/admin/rooms/{roomId}");

        forbiddenResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", $"{rootCookie.Name}={rootCookie.Value}");

        var deleteResponse = await SecureDeleteAsync(client, $"/admin/rooms/{roomId}");

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthShouldPreserveSecurityHeaders()
    {
        using var client = readFactory.CreateClient();
        using var response = await client.GetAsync("/health");

        response.Headers.GetValues("Content-Security-Policy").Should().Equal("default-src 'self'; script-src 'self'; worker-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self' data:; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'");
        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
    }

    [Fact]
    public async Task InvalidRoomJsonShouldReturnInvalidInputProblem()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        _ = await LoginAsAdminAsync(client);
        using var content = new StringContent("{invalid", System.Text.Encoding.UTF8, "application/json");
        using var response = await SecurePostAsync(client, "/admin/rooms", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().Should().Be("InvalidInput");
    }

    [Fact]
    public async Task RoomCapacityShouldReturnLimitExceededProblem()
    {
        using var originalFactory = CreateFactory();
        using var factory = originalFactory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Limits:MaxRooms"] = "1" })));
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var cookie = await LoginAsAdminAsync(client);
        _ = await CreateRoomAsync(client, cookie);
        using var response = await SecureJsonAsync(client, "/admin/rooms", new { Name = "Second", Language = "sql" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().Should().Be("LimitExceeded");
    }

    [Fact]
    public async Task RootShouldServeIndexHtml()
    {
        using var client = readFactory.CreateClient();
        using var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await response.Content.ReadAsStringAsync()).Should().Contain("<!doctype html>");
    }

    [Fact]
    public void InvalidTrustedProxyShouldFailStartupWithValidationMessage()
    {
        using var originalFactory = CreateFactory();
        using var factory = originalFactory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["TrustedProxies:0"] = "not-an-ip" })));

        var action = () => factory.CreateClient();

        action.Should().Throw<OptionsValidationException>().WithMessage("*TrustedProxies contains an invalid IP address: not-an-ip*");
    }

    private static SealCodeAppFactory CreateFactory() => new();

    [Fact]
    public async Task ConnectionShouldBelongToOnlyOneRoomAndCleanUpOnDisconnect()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var cookie = await LoginAsAdminAsync(client);
        var roomA = await CreateRoomAsync(client, cookie);
        var roomB = await CreateRoomAsync(client, cookie);
        await using var alice = await CreateConnectionAsync(factory, client.BaseAddress!, cookie);
        await using var inspector = await CreateConnectionAsync(factory, client.BaseAddress!, cookie);
        _ = await alice.InvokeAsync<JoinRoomResult>("JoinRoom", roomA, "Alice");
        var repeatedJoin = await alice.InvokeAsync<JoinRoomResult>("JoinRoom", roomA, "Alice");
        repeatedJoin.Users.Should().Equal("Alice");

        Func<Task> joinOther = () => alice.InvokeAsync<JoinRoomResult>("JoinRoom", roomB, "Alice");
        await joinOther.Should().ThrowAsync<Microsoft.AspNetCore.SignalR.HubException>().WithMessage("*Leave the current room*");
        var otherRoom = await inspector.InvokeAsync<JoinRoomResult>("JoinRoom", roomB, "Inspector");
        otherRoom.Users.Should().Equal("Inspector");
        await inspector.InvokeAsync("LeaveRoom", roomB);

        var left = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var leftSubscription = inspector.On<string, string[], long>("UserLeft", (name, _, revision) =>
        {
            if (name == "Alice")
            {
                left.TrySetResult();
            }
        });
        _ = await inspector.InvokeAsync<JoinRoomResult>("JoinRoom", roomA, "Inspector");
        await alice.StopAsync();
        await left.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var afterDisconnect = await inspector.InvokeAsync<JoinRoomResult>("JoinRoom", roomA, "Inspector");
        afterDisconnect.Users.Should().Equal("Inspector");

        // An explicit leave also allows a subsequent join on the same connection.
        await inspector.InvokeAsync("LeaveRoom", roomA);
        var nextRoom = await inspector.InvokeAsync<JoinRoomResult>("JoinRoom", roomB, "Inspector");
        nextRoom.Users.Should().Equal("Inspector");
        // The disconnected participant's name is available again.
        await using var replacement = await CreateConnectionAsync(factory, client.BaseAddress!, cookie);
        var replacementJoin = await replacement.InvokeAsync<JoinRoomResult>("JoinRoom", roomA, "Alice");
        replacementJoin.Users.Should().Equal("Alice");
    }

    [Theory]
    [InlineData("before-join")]
    [InlineData("after-leave")]
    [InlineData("other-room")]
    public async Task NonParticipantsShouldNotMutateRoom(string scenario)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var cookie = await LoginAsAdminAsync(client);
        var targetRoom = await CreateRoomAsync(client, cookie);
        await using var connection = await CreateConnectionAsync(factory, client.BaseAddress!, cookie);
        if (scenario == "after-leave")
        {
            _ = await connection.InvokeAsync<JoinRoomResult>("JoinRoom", targetRoom, "Alice");
            await connection.InvokeAsync("LeaveRoom", targetRoom);
        }
        else if (scenario == "other-room")
        {
            var otherRoom = await CreateRoomAsync(client, cookie);
            _ = await connection.InvokeAsync<JoinRoomResult>("JoinRoom", otherRoom, "Alice");
        }

        var bytes = Convert.ToBase64String([1, 2]);
        Func<Task> update = () => connection.InvokeAsync("UpdateYjs", targetRoom, bytes);
        Func<Task> checkpoint = () => connection.InvokeAsync<bool>("CheckpointYjs", targetRoom, bytes, "forged", 0L);
        Func<Task> language = () => connection.InvokeAsync("SetLanguage", targetRoom, "csharp");
        Func<Task> sync = () => connection.InvokeAsync<JoinRoomResult>("SyncYjs", targetRoom);
        Func<Task> revision = () => connection.InvokeAsync<long>("GetDocumentRevision", targetRoom);

        await update.Should().ThrowAsync<Microsoft.AspNetCore.SignalR.HubException>().WithMessage("*Join the room*");
        await checkpoint.Should().ThrowAsync<Microsoft.AspNetCore.SignalR.HubException>().WithMessage("*Join the room*");
        await language.Should().ThrowAsync<Microsoft.AspNetCore.SignalR.HubException>().WithMessage("*Join the room*");
        await sync.Should().ThrowAsync<Microsoft.AspNetCore.SignalR.HubException>().WithMessage("*Join the room*");
        await revision.Should().ThrowAsync<Microsoft.AspNetCore.SignalR.HubException>().WithMessage("*Join the room*");
        await using var inspector = await CreateConnectionAsync(factory, client.BaseAddress!, cookie);
        var state = await inspector.InvokeAsync<JoinRoomResult>("JoinRoom", targetRoom, "Inspector");
        state.DocumentRevision.Should().Be(0);
        state.YjsState.Should().BeNull();
        state.YjsUpdates.Should().BeEmpty();
        state.Language.Should().Be("sql");
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData("<img src=x onerror=alert(1)>")]
    public async Task CreateRoomShouldRejectUnconfiguredLanguage(string language)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        _ = await LoginAsAdminAsync(client);

        var response = await SecureJsonAsync(client, "/admin/rooms", new
        {
            Name = "Invalid language",
            Language = language
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var rooms = await client.GetFromJsonAsync<RoomSummary[]>("/admin/rooms");
        rooms.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Root")]
    public async Task ForgedAdminCookieShouldNotAuthenticate(string name)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"admin_auth={name}");

        var response = await client.GetAsync("/admin/rooms");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var createResponse = await SecureJsonAsync(client, "/admin/rooms", new
        {
            Name = "Forged",
            Language = "sql"
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TamperedAdminTicketShouldNotAuthenticate()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
        var cookie = await LoginAsAdminAsync(client);
        var value = cookie.Value.ToCharArray();
        var middle = value.Length / 2;
        value[middle] = value[middle] == 'A' ? 'B' : 'A';
        client.DefaultRequestHeaders.Add("Cookie", $"{cookie.Name}={new string(value)}");

        var response = await client.GetAsync("/admin/rooms");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExpiredAdminTicketShouldNotAuthenticate()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
        var cookie = await LoginAsAdminAsync(client);
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var ticket = options.TicketDataFormat.Unprotect(cookie.Value)!;
        ticket.Properties.ExpiresUtc = DateTimeOffset.UnixEpoch;
        var expiredCookie = options.TicketDataFormat.Protect(ticket);
        client.DefaultRequestHeaders.Add("Cookie", $"{cookie.Name}={expiredCookie}");

        (await client.GetAsync("/admin/rooms")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LogoutShouldRemoveAuthenticatedSessionCookie()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        _ = await LoginAsAdminAsync(client);
        (await client.GetAsync("/admin/rooms")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await SecurePostAsync(client, "/admin/logout", null)).StatusCode.Should().Be(HttpStatusCode.Redirect);

        (await client.GetAsync("/admin/rooms")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AdminMutationsRequireValidAntiforgeryToken()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new()
        {
            AllowAutoRedirect = false
        });
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["name"] = "Admin", ["password"] = "pass1" });
        (await client.PostAsync("/admin/login", form)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _ = await LoginAsAdminAsync(client);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        (await client.PostAsJsonAsync("/admin/rooms", new
        {
            Name = "blocked"
        })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", "tampered");
        (await client.PostAsync("/admin/logout", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync("/admin/rooms")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/admin/login")]
    [InlineData("/admin/login/")]
    [InlineData("/ADMIN/LOGIN")]
    public async Task LoginFloodIsControlled(string path)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new()
        {
            AllowAutoRedirect = false
        });
        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["name"] = "unknown", ["password"] = "incorrect" });
            (await SecurePostAsync(client, path, form)).StatusCode.Should().Be(HttpStatusCode.Redirect);
        }
        using var last = new FormUrlEncodedContent(new Dictionary<string, string> { ["name"] = "UNKNOWN", ["password"] = "incorrect" });
        (await SecurePostAsync(client, path, last)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task LoginTrailingSlashRejectsLongName()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["name"] = new string('a', 65), ["password"] = "incorrect" });
        (await SecurePostAsync(client, "/admin/login/", form)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("/admin/login")]
    [InlineData("/admin/login/")]
    public async Task LoginRejectsJsonWithValidAntiforgeryToken(string path)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        await RefreshTokenAsync(client);
        using var response = await client.PostAsJsonAsync(path, new { name = "Admin", password = "pass1" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("/admin/login")]
    [InlineData("/admin/login/")]
    public async Task LoginRejectsOversizedPassword(string path)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["name"] = "Admin",
            ["password"] = new string('a', 1025)
        });
        (await SecurePostAsync(client, path, form)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("/admin/login")]
    [InlineData("/admin/login/")]
    public async Task LoginRequiresAntiforgeryToken(string path)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["name"] = "Admin",
            ["password"] = "pass1"
        });
        (await client.PostAsync(path, form)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task HubJournalLimitIsStableAndCheckpointRestoresCapacity()
    {
        using var originalFactory = CreateFactory();
        using var factory = originalFactory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Limits:MaxJournalCount"] = "4", ["Limits:CheckpointEveryRevisions"] = "1" })));
        using var client = factory.CreateClient(new()
        {
            AllowAutoRedirect = false
        });
        var cookie = await LoginAsAdminAsync(client);
        var roomId = await CreateRoomAsync(client, cookie);
        await using var connection = await CreateConnectionAsync(factory, client.BaseAddress!, cookie);
        _ = await connection.InvokeAsync<JoinRoomResult>("JoinRoom", roomId, "Alice");
        for (var i = 0; i < 4; i++)
        {
            await connection.InvokeAsync("UpdateYjs", roomId, "AQ==");
        }
        var refusal = (await FluentActions.Awaiting(() => connection.InvokeAsync("UpdateYjs", roomId, "Ag=="))
            .Should().ThrowAsync<Microsoft.AspNetCore.SignalR.HubException>()).Which;
        HubErrorAssertions.AssertHubError(refusal, SealCode.Hub.HubErrorCode.LimitExceeded);
        (await connection.InvokeAsync<long>("GetDocumentRevision", roomId)).Should().Be(4);
        (await connection.InvokeAsync<bool>("CheckpointYjs", roomId, "AQ==", "", 4L)).Should().BeTrue();
        await connection.InvokeAsync("UpdateYjs", roomId, "Ag==");
        (await connection.InvokeAsync<long>("GetDocumentRevision", roomId)).Should().Be(5);
    }

    private static async Task RefreshTokenAsync(HttpClient client)
    {
        var tokenResponse = await client.GetAsync("/admin/token");
        var response = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        if (tokenResponse.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            var existing = client.DefaultRequestHeaders.TryGetValues("Cookie", out var values) ? string.Join("; ", values) : "";
            var retained = existing.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Where(value => !value.StartsWith(".AspNetCore.Antiforgery", StringComparison.Ordinal));
            client.DefaultRequestHeaders.Remove("Cookie");
            client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", retained.Concat(cookies.Select(cookie => cookie.Split(';')[0]))));
        }
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", response.GetProperty("Token").GetString());
    }

    private static async Task<HttpResponseMessage> SecurePostAsync(
        HttpClient client,
        string url,
        HttpContent? content)
    {
        await RefreshTokenAsync(client);
        return await client.PostAsync(url, content);
    }

    private static async Task<HttpResponseMessage> SecureJsonAsync<T>(
        HttpClient client,
        string url,
        T content)
    {
        await RefreshTokenAsync(client);
        return await client.PostAsJsonAsync(url, content);
    }

    private static async Task<HttpResponseMessage> SecureDeleteAsync(
        HttpClient client,
        string url)
    {
        await RefreshTokenAsync(client);
        return await client.DeleteAsync(url);
    }

    private static Task<Cookie> LoginAsAdminAsync(HttpClient client)
        => LoginAsync(client, "Admin", "pass1");

    private static async Task<Cookie> LoginAsync(
        HttpClient client,
        string name,
        string password)
    {
        using var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["name"] = name,
            ["password"] = password
        });

        var loginResponse = await SecurePostAsync(client, "/admin/login", loginContent);

        loginResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
        loginResponse.Headers.TryGetValues("Set-Cookie", out var setCookies).Should().BeTrue();

        var rawCookie = setCookies!.First().Split(';', 2)[0];
        var parts = rawCookie.Split('=', 2);
        return new Cookie(parts[0], parts.Length > 1 ? parts[1] : string.Empty);
    }

    private static async Task<string> CreateRoomAsync(HttpClient client, Cookie cookie)
    {
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", $"{cookie.Name}={cookie.Value}");

        var response = await SecureJsonAsync(client, "/admin/rooms", new
        {
            Name = "Room",
            Language = "sql"
        });
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return payload.GetProperty("RoomId").GetString()!;
    }

    private static async Task<HubConnection> CreateConnectionAsync(
        WebApplicationFactory<Program> factory,
        Uri baseAddress,
        Cookie cookie)
    {
        var cookieContainer = new CookieContainer();
        cookieContainer.Add(baseAddress, cookie);

        var hubUrl = new Uri(baseAddress, "/roomHub");
        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Cookies = cookieContainer;
            })
            .Build();

        await connection.StartAsync();
        return connection;
    }

    private sealed class LoopbackStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.Use((context, proceed) =>
                {
                    context.Connection.RemoteIpAddress = IPAddress.Loopback;
                    return proceed(context);
                });
                next(app);
            };
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "xUnit requires a publicly constructible fixture.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515", Justification = "xUnit requires a publicly constructible fixture.")]
    public sealed class SealCodeAppFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                // Keep local Google credentials and domain arrays out of the default test host.
                var settingsWithoutGoogle = config.Build().AsEnumerable()
                    .Where(setting => !setting.Key.StartsWith("GoogleSignIn", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                config.Sources.Clear();
                config.AddInMemoryCollection(settingsWithoutGoogle);
                var settings = new Dictionary<string, string?>
                {
                    ["AdminUsers:0:Name"] = "Admin",
                    ["AdminUsers:0:Password"] = "pass1",
                    ["AdminUsers:0:PasswordHash"] = null,
                    ["AdminUsers:0:IsSuperAdmin"] = "false",
                    ["AdminUsers:1:Name"] = "Root",
                    ["AdminUsers:1:Password"] = "pass2",
                    ["AdminUsers:1:PasswordHash"] = null,
                    ["AdminUsers:1:IsSuperAdmin"] = "true",
                    ["Languages:0"] = "csharp",
                    ["Languages:1"] = "sql",
                    ["MaxUsersPerRoom"] = "3"
                };

                config.AddInMemoryCollection(settings);
            });
        }
    }
}
