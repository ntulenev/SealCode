using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;

using Transport.Models;

namespace SealCode.Tests;

public sealed partial class SealCodeApiTests
{
    [Theory]
    [InlineData("code")]
    [InlineData("whiteboard")]
    public async Task GuestHubProvidesSnapshotsUpdatesRecoveryAndSeparatePresenceWithoutEditorCapabilities(string kind)
    {
        using var factory = ObserverFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var cookie = await LoginAsAdminAsync(client);
        var id = await CreateRoomAsync(client, cookie, kind);
        using var link = await SecureJsonAsync(client, $"/room/{id}/observer-link", new { });
        var url = (await link.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("Url").GetString()!;
        Assert.DoesNotContain(id, url, StringComparison.Ordinal);
        await using var editor = await CreateConnectionAsync(factory, client.BaseAddress!, cookie);
        await editor.InvokeAsync<JoinRoomResult>("JoinRoom", id, "Presenter");
        await using var guest = ObserverConnection(factory, client.BaseAddress!);
        var snapshots = new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true };
        var messages = System.Threading.Channels.Channel.CreateUnbounded<JsonElement>(snapshots);
        guest.On<JsonElement>("ObserverMessage", message => messages.Writer.TryWrite(message));
        await guest.StartAsync();
        await guest.InvokeAsync("JoinObserver", url.Split('#')[1]);
        var initial = await ReadObserverMessageAsync(messages.Reader, "Snapshot");
        Assert.Equal(kind, initial.GetProperty("Kind").GetString());
        Assert.False(initial.TryGetProperty("RoomId", out _));
        Assert.False(initial.TryGetProperty("CreatedBy", out _));
        Assert.DoesNotContain(id, initial.GetRawText(), StringComparison.Ordinal);
        var revisions = await guest.InvokeAsync<JsonElement>("GetObserverRevisions");
        Assert.Equal(1, revisions.GetProperty("ObserverCount").GetInt32());
        var editorState = await editor.InvokeAsync<JoinRoomResult>("SyncYjs", id);
        Assert.Equal(["Presenter"], editorState.Users);
        Assert.Equal(1, editorState.ObserverCount);
        await Assert.ThrowsAsync<HubException>(() => guest.InvokeAsync("UpdateYjs", id, "AQ=="));
        await editor.InvokeAsync("UpdateYjs", id, "AQ==");
        var update = await ReadObserverMessageAsync(messages.Reader, "Update", message => message.GetProperty("DocumentRevision").GetInt64() == 1);
        Assert.Equal("AQ==", update.GetProperty("YjsUpdates")[0].GetProperty("Update").GetString());
        await guest.InvokeAsync("SyncObserver");
        var recovered = await ReadObserverMessageAsync(messages.Reader, "Snapshot");
        Assert.Equal(1, recovered.GetProperty("DocumentRevision").GetInt64());
        await guest.InvokeAsync("LeaveObserver");
        await Assert.ThrowsAsync<HubException>(() => guest.InvokeAsync<JsonElement>("GetObserverRevisions"));
        await guest.InvokeAsync("JoinObserver", url.Split('#')[1]);
        _ = await ReadObserverMessageAsync(messages.Reader, "Snapshot");
        await guest.StopAsync();
        var after = await editor.InvokeAsync<JoinRoomResult>("SyncYjs", id);
        Assert.Equal(0, after.ObserverCount);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task GuestLinkCommandsRequireIdentityOwnershipAnExistingRoomAndAntiforgery(string method)
    {
        using var factory = ObserverFactory();
        using var owner = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var cookie = await LoginAsAdminAsync(owner);
        var id = await CreateRoomAsync(owner, cookie);
        var url = $"/room/{id}/observer-link";
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var unauthorized = await GuestLinkCommandAsync(anonymous, method, url);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        using var other = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        _ = await LoginAsync(other, "Other", "pass3");
        using var forbidden = await GuestLinkCommandAsync(other, method, url);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var missing = await GuestLinkCommandAsync(owner, method, $"/room/{Models.RoomId.New().Value}/observer-link");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var allowed = await GuestLinkCommandAsync(owner, method, url);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal("no-store", allowed.Headers.CacheControl!.ToString());
        using var root = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        _ = await LoginAsync(root, "Root", "pass2");
        using var superAdmin = await GuestLinkCommandAsync(root, method, url);
        Assert.Equal(HttpStatusCode.OK, superAdmin.StatusCode);
        if (method != "GET")
        {
            owner.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            using var insecure = method == "POST" ? await owner.PostAsJsonAsync(url, new { }) : await owner.DeleteAsync(url);
            Assert.Equal(HttpStatusCode.BadRequest, insecure.StatusCode);
        }
    }

    [Fact]
    public async Task DisabledGuestAccessIsVisibleInLimitsAndCannotCreateALink()
    {
        using var factory = ObserverFactory(enabled: false);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var cookie = await LoginAsAdminAsync(client);
        var id = await CreateRoomAsync(client, cookie);
        var config = await client.GetFromJsonAsync<JsonElement>("/admin/limits/config");
        Assert.False(config.GetProperty("Observers").GetProperty("Enabled").GetBoolean());
        Assert.Equal(20, config.GetProperty("Observers").GetProperty("MaxPerRoom").GetInt32());
        Assert.Equal(40, config.GetProperty("Observers").GetProperty("MaxConnections").GetInt32());
        using var info = await client.GetAsync($"/room/{id}/observer-link");
        Assert.False((await info.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("Enabled").GetBoolean());
        using var create = await SecureJsonAsync(client, $"/room/{id}/observer-link", new { });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, create.StatusCode);
        using var watch = await client.GetAsync("/watch");
        Assert.Equal("no-referrer", watch.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("no-store", watch.Headers.CacheControl!.ToString());
    }

    private static Task<HttpResponseMessage> GuestLinkCommandAsync(HttpClient client, string method, string url)
        => method switch
        {
            "GET" => client.GetAsync(url),
            "POST" => SecureJsonAsync(client, url, new { }),
            _ => SecureDeleteAsync(client, url)
        };

    private static async Task<JsonElement> ReadObserverMessageAsync(
        System.Threading.Channels.ChannelReader<JsonElement> reader, string type, Func<JsonElement, bool>? predicate = null)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var message = await reader.ReadAsync(deadline.Token);
            if (message.GetProperty("Type").GetString() == type && (predicate is null || predicate(message)))
            {
                return message;
            }
        }
    }

    private static HubConnection ObserverConnection(WebApplicationFactory<Program> factory, Uri address)
        => new HubConnectionBuilder().WithUrl(new Uri(address, "/observerHub"), options => options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler()).Build();

    private static WebApplicationFactory<Program> ObserverFactory(bool enabled = true)
        => new ObserverAppFactory(enabled);

    private sealed class ObserverAppFactory : WebApplicationFactory<Program>
    {
        private readonly bool _enabled;

        public ObserverAppFactory(bool enabled)
        {
            _enabled = enabled;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.Sources.Clear();
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AdminUsers:0:Name"] = "Admin",
                    ["AdminUsers:0:Password"] = "pass1",
                    ["AdminUsers:1:Name"] = "Root",
                    ["AdminUsers:1:Password"] = "pass2",
                    ["AdminUsers:1:IsSuperAdmin"] = "true",
                    ["AdminUsers:2:Name"] = "Other",
                    ["AdminUsers:2:Password"] = "pass3",
                    ["Languages:0"] = "sql",
                    ["MaxUsersPerRoom"] = "5",
                    ["Observers:Enabled"] = _enabled.ToString(),
                    ["Observers:InvocationsPerSecond"] = "20",
                    ["Logging:LogLevel:Default"] = "Warning"
                });
            });
        }
    }
}
