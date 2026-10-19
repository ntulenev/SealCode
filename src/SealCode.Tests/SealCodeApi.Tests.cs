using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Models;

using Transport.Models;

namespace SealCode.Tests;

public sealed class SealCodeApiTests
{
    [Fact(DisplayName = "GET /health returns ok")]
    [Trait("Category", "Integration")]
    public async Task GetHealthReturnsOk()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");
        var payload = await response.Content.ReadFromJsonAsync<string>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        payload.Should().Be("ok");
    }

    [Fact(DisplayName = "GET /languages returns configured languages")]
    [Trait("Category", "Integration")]
    public async Task GetLanguagesReturnsConfiguredLanguages()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

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
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/features");
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        payload.GetProperty("CollaborativeUndoEnabled").GetBoolean().Should().BeTrue();
        var advertisedLimit = payload.GetProperty("MaxYjsMessageBytes").GetInt64();
        advertisedLimit.Should().Be(16 * 1024 * 1024);
        advertisedLimit.Should().Be(factory.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.SignalR.HubOptions>>()
            .Value.MaximumReceiveMessageSize);
    }

    [Fact(DisplayName = "GET /admin/rooms returns unauthorized without cookie")]
    [Trait("Category", "Integration")]
    public async Task GetAdminRoomsWithoutCookieReturnsUnauthorized()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/admin/rooms");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "POST /admin/rooms returns unauthorized without cookie")]
    [Trait("Category", "Integration")]
    public async Task CreateRoomWithoutCookieReturnsUnauthorized()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/admin/rooms", new { Name = "Room", Language = "csharp" });

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

        var loginResponse = await client.PostAsync("/admin/login", loginContent);

        loginResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
        loginResponse.Headers.TryGetValues("Set-Cookie", out var setCookies).Should().BeTrue();
        var setCookie = setCookies!.First();
        var cookie = setCookie.Split(';', 2)[0];

        client.DefaultRequestHeaders.Add("Cookie", cookie);

        var createResponse = await client.PostAsJsonAsync("/admin/rooms", new { Name = "Room", Language = "sql" });
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
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

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

    [Fact(DisplayName = "SignalR /roomHub broadcasts room events")]
    [Trait("Category", "Integration")]
    public async Task SignalRRoomHubBroadcastsRoomEvents()
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
        using var bobJoinedSubscription = bob.On<string, string[]>("UserJoined", (name, users) =>
            bobJoined.TrySetResult((name, users)));

        var bobJoin = await bob.InvokeAsync<JoinRoomResult>("JoinRoom", roomId, "Bob");
        bobJoin.Users.Should().ContainSingle().Which.Should().Be("Bob");

        var aliceJoin = await alice.InvokeAsync<JoinRoomResult>("JoinRoom", roomId, "Alice");
        aliceJoin.Users.Should().Contain(["Alice", "Bob"]);

        var joinedPayload = await AwaitWithTimeout(bobJoined.Task);
        joinedPayload.name.Should().Be("Alice");
        joinedPayload.users.Should().Contain(["Alice", "Bob"]);

        var yjsUpdated = new TaskCompletionSource<(string update, long documentRevision, int roomVersion, string author)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (bob.On<string, long, int, string>("YjsUpdated", (update, documentRevision, roomVersion, author) =>
            yjsUpdated.TrySetResult((update, documentRevision, roomVersion, author))))
        {
            var update = Convert.ToBase64String(new byte[] { 1, 2, 3 });
            await alice.InvokeAsync("UpdateYjs", roomId, update);

            var payload = await AwaitWithTimeout(yjsUpdated.Task);
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

            var state = Convert.ToBase64String(new byte[] { 5, 6, 7 });
            var accepted = await alice.InvokeAsync<bool>("CheckpointYjs", roomId, state, "snapshot", payload.documentRevision);
            accepted.Should().BeTrue();

            var charlieJoinAfterCheckpoint = await charlie.InvokeAsync<JoinRoomResult>("JoinRoom", roomId, "Charlie");
            charlieJoinAfterCheckpoint.DocumentRevision.Should().Be(1);
            charlieJoinAfterCheckpoint.CheckpointRevision.Should().Be(1);
            charlieJoinAfterCheckpoint.YjsState.Should().Be(state);
            charlieJoinAfterCheckpoint.YjsUpdates.Should().BeEmpty();
        }

        var languageUpdated = new TaskCompletionSource<(string language, int version)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (bob.On<string, int>("LanguageUpdated", (language, version) =>
            languageUpdated.TrySetResult((language, version))))
        {
            await alice.InvokeAsync("SetLanguage", roomId, "sql");
            var payload = await AwaitWithTimeout(languageUpdated.Task);
            payload.language.Should().Be("sql");
            payload.version.Should().BeGreaterThanOrEqualTo(2);
        }

        var cursorUpdated = new TaskCompletionSource<(string author, int position)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (bob.On<string, int>("CursorUpdated", (author, position) =>
            cursorUpdated.TrySetResult((author, position))))
        {
            await alice.InvokeAsync("UpdateCursor", roomId, 42);
            var payload = await AwaitWithTimeout(cursorUpdated.Task);
            payload.author.Should().Be("Alice");
            payload.position.Should().Be(42);
        }

        var selectionUpdated = new TaskCompletionSource<(string author, bool isMultiLine)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (bob.On<string, bool>("UserSelection", (author, isMultiLine) =>
            selectionUpdated.TrySetResult((author, isMultiLine))))
        {
            await alice.InvokeAsync("UpdateSelection", roomId, true);
            var payload = await AwaitWithTimeout(selectionUpdated.Task);
            payload.author.Should().Be("Alice");
            payload.isMultiLine.Should().BeTrue();
        }

        var copyUpdated = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (bob.On<string>("UserCopy", author => copyUpdated.TrySetResult(author)))
        {
            await alice.InvokeAsync("UpdateCopy", roomId);
            var author = await AwaitWithTimeout(copyUpdated.Task);
            author.Should().Be("Alice");
        }

        var leftUpdated = new TaskCompletionSource<(string author, string[] users)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (bob.On<string, string[]>("UserLeft", (author, users) =>
            leftUpdated.TrySetResult((author, users))))
        {
            await alice.InvokeAsync("LeaveRoom", roomId);
            var payload = await AwaitWithTimeout(leftUpdated.Task);
            payload.author.Should().Be("Alice");
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

        var forbiddenResponse = await client.DeleteAsync($"/admin/rooms/{roomId}");

        forbiddenResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", $"{rootCookie.Name}={rootCookie.Value}");

        var deleteResponse = await client.DeleteAsync($"/admin/rooms/{roomId}");

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
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
        using var leftSubscription = inspector.On<string, string[]>("UserLeft", (name, _) =>
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

        await update.Should().ThrowAsync<Microsoft.AspNetCore.SignalR.HubException>().WithMessage("*Join the room*");
        await checkpoint.Should().ThrowAsync<Microsoft.AspNetCore.SignalR.HubException>().WithMessage("*Join the room*");
        await language.Should().ThrowAsync<Microsoft.AspNetCore.SignalR.HubException>().WithMessage("*Join the room*");
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

        var response = await client.PostAsJsonAsync("/admin/rooms", new { Name = "Invalid language", Language = language });

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
        var createResponse = await client.PostAsJsonAsync("/admin/rooms", new { Name = "Forged", Language = "sql" });
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
        ticket.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
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

        (await client.PostAsync("/admin/logout", null)).StatusCode.Should().Be(HttpStatusCode.Redirect);

        (await client.GetAsync("/admin/rooms")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static Task<Cookie> LoginAsAdminAsync(HttpClient client)
        => LoginAsync(client, "Admin", "pass1");

    private static async Task<Cookie> LoginAsync(HttpClient client, string name, string password)
    {
        using var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["name"] = name,
            ["password"] = password
        });

        var loginResponse = await client.PostAsync("/admin/login", loginContent);

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

        var response = await client.PostAsJsonAsync("/admin/rooms", new { Name = "Room", Language = "sql" });
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return payload.GetProperty("RoomId").GetString()!;
    }

    private static async Task<HubConnection> CreateConnectionAsync(WebApplicationFactory<Program> factory, Uri baseAddress, Cookie cookie)
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

    private static async Task<T> AwaitWithTimeout<T>(Task<T> task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
        completed.Should().BeSameAs(task);
        return await task;
    }

    private sealed class SealCodeAppFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var settings = new Dictionary<string, string?>
                {
                    ["AdminUsers:0:Name"] = "Admin",
                    ["AdminUsers:0:Password"] = "pass1",
                    ["AdminUsers:0:IsSuperAdmin"] = "false",
                    ["AdminUsers:1:Name"] = "Root",
                    ["AdminUsers:1:Password"] = "pass2",
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
