using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

using Abstractions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Models;
using Models.Configuration;

using Moq;

using SealCode.Observers;

namespace SealCode.Tests;

public sealed class ObserverSessionsTests
{
    [Fact]
    public async Task CommittedUpdateSurvivesCheckpointBetweenDeliveryTicks()
    {
        await using var fixture = new Fixture();
        var room = Room();
        var initial = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delivered = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var id = room.RoomId;
        fixture.Registry.Setup(registry => registry.TryGetRoom(id, out room)).Returns(true);
        fixture.Proxy.Setup(proxy => proxy.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns((string method, object?[] args, CancellationToken _) =>
            {
                if (method == "ObserverMessage")
                {
                    var payload = args[0] is JsonElement element ? element : JsonSerializer.Deserialize<JsonElement>((string)args[0]!);
                    if (payload.GetProperty("Type").GetString() == "Snapshot")
                        initial.TrySetResult();
                    if (payload.GetProperty("Type").GetString() == "Update" && payload.GetProperty("DocumentRevision").GetInt64() == 1)
                        delivered.TrySetResult(payload);
                }
                return Task.CompletedTask;
            });
        var token = fixture.Service.CreateLink(room).Split('#')[1];
        await fixture.Service.StartAsync(CancellationToken.None);
        var caller = fixture.Connect(1);
        fixture.Service.Join(caller.Object, token);
        await initial.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _ = room.AppendAsTestParticipant([1], DateTimeOffset.UtcNow);
        fixture.Service.RecordUpdate(id, "AQ==", 1);
        Assert.True(room.CheckpointAsTestParticipant(new(1), [1], new(""), DateTimeOffset.UtcNow));
        Assert.Empty(room.CreateYjsSyncState().Updates);
        var message = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, message.GetProperty("YjsUpdates").GetArrayLength());
        Assert.Equal(1, message.GetProperty("YjsUpdates")[0].GetProperty("Revision").GetInt64());
    }

    [Fact]
    public async Task ConcurrentRoomAdmissionStopsAt20AndDoesNotChangeEditorMembershipOrActivity()
    {
        await using var fixture = new Fixture();
        var room = Room();
        var activity = room.LastActivityUtc;
        var link = fixture.Service.CreateLink(room).Split('#')[1];
        var callers = Enumerable.Range(0, 30).Select(i => fixture.Connect(i)).ToArray();
        var admitted = 0;
        Parallel.ForEach(callers, caller =>
        {
            try
            { fixture.Service.Join(caller.Object, link); Interlocked.Increment(ref admitted); }
            catch (HubException error) { Assert.Equal("ObserverLimitReached", error.Message); }
        });
        Assert.Equal(20, admitted);
        Assert.Equal(20, fixture.Service.Count(room.RoomId).Count);
        Assert.Equal(0, room.ConnectedUserCount);
        Assert.Equal(activity, room.LastActivityUtc);
        Assert.Equal(0, room.DocumentRevision.Value);
    }

    [Fact]
    public async Task ConcurrentTransportAdmissionStopsAt40IncludingUnjoinedAndReleasesExactlyOnce()
    {
        await using var fixture = new Fixture();
        var admitted = new ConcurrentBag<Mock<HubCallerContext>>();
        Parallel.For(0, 80, index =>
        {
            try
            { admitted.Add(fixture.Connect(index)); }
            catch (HubException error) { Assert.Equal("ObserverLimitReached", error.Message); }
        });
        Assert.Equal(40, admitted.Count);
        foreach (var caller in admitted)
        {
            fixture.Service.Disconnect(caller.Object.ConnectionId);
            fixture.Service.Disconnect(caller.Object.ConnectionId);
        }
        for (var i = 100; i < 140; i++)
            _ = fixture.Connect(i);
    }

    [Fact]
    public async Task IdempotentJoinLeaveRotationAndClosedRoomInvalidateCapabilities()
    {
        await using var fixture = new Fixture();
        var room = Room();
        var token = fixture.Service.CreateLink(room).Split('#')[1];
        Assert.DoesNotContain(room.RoomId.Value, token, StringComparison.Ordinal);
        var caller = fixture.Connect(1);
        fixture.Service.Join(caller.Object, token);
        fixture.Service.Join(caller.Object, token);
        Assert.Equal(1, fixture.Service.Count(room.RoomId).Count);
        fixture.Service.Leave(caller.Object.ConnectionId);
        fixture.Service.Leave(caller.Object.ConnectionId);
        Assert.Equal(0, fixture.Service.Count(room.RoomId).Count);
        fixture.Service.Join(caller.Object, token);
        var next = fixture.Service.CreateLink(room).Split('#')[1];
        Assert.NotEqual(next, token);
        Assert.Equal(0, fixture.Service.Count(room.RoomId).Count);
        var replacement = fixture.Connect(2);
        Assert.Throws<HubException>(() => fixture.Service.Join(replacement.Object, token));
        fixture.Service.Join(replacement.Object, next);
        fixture.Service.Disable(room.RoomId);
        Assert.Equal(0, fixture.Service.Count(room.RoomId).Count);
        Assert.Throws<HubException>(() => fixture.Service.Revisions(replacement.Object.ConnectionId));
        room.Close();
        Assert.Throws<Models.Exceptions.RoomNotFoundException>(() => fixture.Service.CreateLink(room));
    }

    [Fact]
    public async Task SourceCapAndDisabledFeatureAreEnforcedBeforeJoin()
    {
        await using var fixture = new Fixture();
        for (var i = 0; i < 20; i++)
            _ = fixture.Connect(i, "192.0.2.1");
        Assert.Throws<HubException>(() => fixture.Connect(20, "192.0.2.1"));
        _ = fixture.Connect(21, "192.0.2.2");
        await using var disabled = new Fixture(new ObserverLimits());
        Assert.Throws<HubException>(() => disabled.Connect(1));
        Assert.Throws<HubException>(() => disabled.Service.CreateLink(Room()));
    }

    [Fact]
    public async Task SlowSnapshotDoesNotBlockEditsAndTransportTimeoutReleasesGuestCapacity()
    {
        await using var fixture = new Fixture(new ObserverLimits { Enabled = true, SendTimeoutSeconds = 1 });
        var sending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Proxy.Setup(proxy => proxy.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, object?[] _, CancellationToken cancellation) =>
            {
                sending.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellation);
            });
        var room = Room();
        var token = fixture.Service.CreateLink(room).Split('#')[1];
        var caller = fixture.Connect(1);
        fixture.Service.Join(caller.Object, token);
        await sending.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _ = room.AppendAsTestParticipant([1], DateTimeOffset.UtcNow);
        Assert.Equal(1, room.DocumentRevision.Value);
        await Task.Delay(1500);
        Assert.Equal(0, fixture.Service.Count(room.RoomId).Count);
        caller.Verify(context => context.Abort(), Times.AtLeastOnce);
    }

    private static RoomState Room() => new(RoomId.New(), new("Presentation"), new("sql"), new(""),
        new(1), DateTimeOffset.UtcNow, new AdminUser("Owner"));

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ConcurrentBag<Mock<HubCallerContext>> _callers = [];
        public Mock<ISingleClientProxy> Proxy { get; } = new();
        public ObserverSessions Service { get; }
        public Mock<IRoomRegistry> Registry { get; } = new();
        public Fixture(ObserverLimits? limits = null)
        {
            Proxy.Setup(proxy => proxy.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            var clients = new Mock<IHubClients>();
            clients.Setup(clients => clients.Client(It.IsAny<string>())).Returns(Proxy.Object);
            clients.Setup(clients => clients.Group(It.IsAny<string>())).Returns(Proxy.Object);
            var hub = new Mock<IHubContext<ObserverHub>>();
            hub.SetupGet(hub => hub.Clients).Returns(clients.Object);
            var editors = new Mock<IHubContext<RoomHub>>();
            editors.SetupGet(hub => hub.Clients).Returns(clients.Object);
            Service = new(Options.Create(new ApplicationConfiguration { AdminUsers = [], Languages = [], Observers = limits ?? new ObserverLimits { Enabled = true, NewConnectionsPerSourcePerMinute = 10000 } }),
                Registry.Object, hub.Object, editors.Object, NullLogger<ObserverSessions>.Instance);
        }
        public Mock<HubCallerContext> Connect(int index, string? source = null)
        {
            var caller = new Mock<HubCallerContext>();
            caller.SetupGet(c => c.ConnectionId).Returns("guest-" + index);
            var http = new DefaultHttpContext();
            http.Connection.RemoteIpAddress = IPAddress.Parse(source ?? $"198.51.100.{1 + (index % 200)}");
            var features = new FeatureCollection();
            features.Set<IHttpContextFeature>(new HttpFeature { HttpContext = http });
            caller.SetupGet(c => c.Features).Returns(features);
            Service.Connect(caller.Object);
            _callers.Add(caller);
            return caller;
        }
        public async ValueTask DisposeAsync()
        {
            foreach (var caller in _callers)
                Service.Disconnect(caller.Object.ConnectionId);
            await Service.StopAsync(CancellationToken.None);
            Service.Dispose();
        }
    }
    private sealed class HttpFeature : IHttpContextFeature
    {
        public HttpContext? HttpContext { get; set; }
    }
}
