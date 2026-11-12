using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Time.Testing;

using Models;
using Models.Configuration;

using Moq;

namespace SealCode.Tests;

public sealed partial class ObserverSessionsTests
{
    [Fact]
    public async Task ConnectionsWithoutHttpContextShareTheUnknownSourceBudget()
    {
        await using var fixture = new Fixture(new ObserverLimits { Enabled = true, MaxConnectionsPerSource = 1 });
        _ = fixture.Connect(1, includeHttpContext: false);
        Assert.Throws<HubException>(() => fixture.Connect(2, includeHttpContext: false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullSnapshotsDoNotDuplicatePlainTextAlreadyCarriedByCrdt(bool checkpoint)
    {
        await using var fixture = new Fixture();
        var room = new RoomState(RoomId.New(), new("Presentation"), new("sql"), new("seed"),
            new(1), DateTimeOffset.UtcNow, new("Owner"));
        if (checkpoint)
        {
            Assert.True(room.CheckpointAsTestParticipant(new(0), [1], new("seed"), DateTimeOffset.UtcNow));
        }

        var frames = Capture(fixture);
        var caller = fixture.Connect(1);
        fixture.Service.Join(caller.Object, fixture.Service.CreateLink(room).Split('#')[1]);
        await WaitUntilAsync(() => frames.Any(frame => frame.GetProperty("Type").GetString() == "Snapshot"));
        var message = frames.First(frame => frame.GetProperty("Type").GetString() == "Snapshot");
        Assert.Equal(checkpoint ? "" : "seed", message.GetProperty("Text").GetString());
        Assert.Equal(checkpoint ? "AQ==" : null, message.GetProperty("YjsState").GetString());
    }

    [Fact]
    public async Task ARejectedRejoinRollsBackItsRoomReservation()
    {
        await using var fixture = new Fixture(new ObserverLimits
        { Enabled = true, SnapshotRequestsPerConnectionPerMinute = 1 });
        var frames = Capture(fixture);
        var room = Room();
        var token = fixture.Service.CreateLink(room).Split('#')[1];
        var caller = fixture.Connect(1);
        fixture.Service.Join(caller.Object, token);
        await WaitUntilAsync(() => frames.Count >= 2);
        await Task.Delay(10);
        fixture.Service.Leave(caller.Object.ConnectionId);
        Assert.Equal("RateLimited", Assert.Throws<HubException>(() => fixture.Service.Join(caller.Object, token)).Message);
        Assert.Equal(0, fixture.Service.Count(room.RoomId).Count);
        Assert.Throws<HubException>(() => fixture.Service.Revisions(caller.Object.ConnectionId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PresenceNotificationFailuresDoNotTerminateDocumentDelivery(bool canceled)
    {
        var clock = new FakeTimeProvider();
        await using var fixture = new Fixture(timeProvider: clock);
        var room = Room();
        var id = room.RoomId;
        fixture.Registry.Setup(registry => registry.TryGetRoom(id, out room)).Returns(true);
        var attempts = 0;
        fixture.Proxy.Setup(proxy => proxy.SendCoreAsync("ObserverCountChanged", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                attempts++;
                return attempts == 1 ? Task.FromException(canceled ? new OperationCanceledException() : new IOException("test transport failure")) : Task.CompletedTask;
            });
        var caller = fixture.Connect(1);
        fixture.Service.Join(caller.Object, fixture.Service.CreateLink(room).Split('#')[1]);
        await fixture.Service.StartAsync(CancellationToken.None);
        await PublishUntilAsync(clock, () => attempts > 0);
        if (!canceled)
        {
            await PublishUntilAsync(clock, () => attempts > 1);
        }

        Assert.Equal(1, fixture.Service.Count(id).Count);
        Assert.NotNull(fixture.Service.Revisions(caller.Object.ConnectionId));
        await fixture.Service.StopAsync(CancellationToken.None);
        Assert.Equal(0, fixture.Service.Count(id).Count);
        caller.Verify(context => context.Abort(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task UnexpectedPublicationCancellationPropagatesAndReleasesActiveMembership()
    {
        var clock = new FakeTimeProvider();
        await using var fixture = new Fixture(timeProvider: clock);
        var room = Room();
        var id = room.RoomId;
        fixture.Registry.Setup(registry => registry.TryGetRoom(id, out room)).Throws(new OperationCanceledException("simulated external cancellation"));
        var caller = fixture.Connect(1);
        fixture.Service.Join(caller.Object, fixture.Service.CreateLink(room).Split('#')[1]);
        await fixture.Service.StartAsync(CancellationToken.None);
        await PublishUntilAsync(clock, () => fixture.Service.ExecuteTask!.IsCompleted);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.ExecuteTask!);
        Assert.Equal(0, fixture.Service.Count(id).Count);
        caller.Verify(context => context.Abort(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task RecoveryReplacesQueuedLiveEventsWithOneCurrentSnapshot()
    {
        var clock = new FakeTimeProvider();
        await using var fixture = new Fixture(timeProvider: clock);
        var room = Room();
        var id = room.RoomId;
        fixture.Registry.Setup(registry => registry.TryGetRoom(id, out room)).Returns(true);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var frames = Capture(fixture, async message =>
        {
            if (message.GetProperty("Type").GetString() == "Update")
            {
                entered.TrySetResult();
                await release.Task;
            }
        });
        var caller = fixture.Connect(1);
        fixture.Service.Join(caller.Object, fixture.Service.CreateLink(room).Split('#')[1]);
        await WaitUntilAsync(() => frames.Count >= 2);
        await fixture.Service.StartAsync(CancellationToken.None);
        _ = room.AppendAsTestParticipant([1], DateTimeOffset.UtcNow);
        fixture.Service.RecordUpdate(id, "AQ==", 1);
        await PublishUntilAsync(clock, () => entered.Task.IsCompleted);
        _ = room.AppendAsTestParticipant([2], DateTimeOffset.UtcNow);
        fixture.Service.RecordUpdate(id, "Ag==", 2);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        await Task.Delay(30);
        fixture.Service.Sync(caller.Object.ConnectionId);
        release.TrySetResult();
        await WaitUntilAsync(() => frames.Count(frame => frame.GetProperty("Type").GetString() == "Snapshot") == 2);
        var snapshot = frames.Last(frame => frame.GetProperty("Type").GetString() == "Snapshot");
        Assert.Equal(2, snapshot.GetProperty("DocumentRevision").GetInt64());
        Assert.Equal(2, snapshot.GetProperty("YjsUpdates").GetArrayLength());
    }

    [Fact]
    public async Task QuietTicksDoNotRebroadcastAndMetadataChangesAreDeliveredWithoutDocumentWrites()
    {
        var clock = new FakeTimeProvider();
        await using var fixture = new Fixture(timeProvider: clock);
        var frames = Capture(fixture);
        var room = Room();
        var id = room.RoomId;
        fixture.Registry.Setup(registry => registry.TryGetRoom(id, out room)).Returns(true);
        var caller = fixture.Connect(1);
        fixture.Service.Join(caller.Object, fixture.Service.CreateLink(room).Split('#')[1]);
        await WaitUntilAsync(() => frames.Count >= 2);
        await fixture.Service.StartAsync(CancellationToken.None);
        await PublishUntilAsync(clock, () => frames.Any(frame => frame.GetProperty("Type").GetString() == "Update"));
        var count = frames.Count;
        clock.Advance(TimeSpan.FromMilliseconds(500));
        await Task.Delay(30);
        Assert.Equal(count, frames.Count);
        var validator = new Mock<ILanguageValidator>();
        validator.Setup(validator => validator.IsValid(It.IsAny<RoomLanguage>())).Returns(true);
        _ = room.ChangeLanguageAsTestParticipant(new("csharp"), DateTimeOffset.UtcNow, validator.Object);
        await PublishUntilAsync(clock, () => frames.Any(frame => frame.TryGetProperty("Language", out var language) && language.GetString() == "csharp"));
        room.AddUser(new("bob"), new("Bob"), 5);
        await PublishUntilAsync(clock, () => frames.Last().TryGetProperty("Users", out var users) && users.GetArrayLength() == 2);
        Assert.Equal(0, room.DocumentRevision.Value);
        room.Close();
        Assert.Equal("ObserverUnavailable", Assert.Throws<HubException>(() => fixture.Service.Revisions(caller.Object.ConnectionId)).Message);
    }

    [Theory]
    [InlineData(20, 40, 20, true)]
    [InlineData(40, 40, 40, true)]
    [InlineData(41, 40, 20, false)]
    [InlineData(20, 40, 41, false)]
    [InlineData(0, 40, 20, false)]
    public void ObserverBudgetsMustFitBothTheProcessAndTheConfiguredRanges(int room, int process, int source, bool valid)
    {
        var limits = new ObserverLimits { MaxPerRoom = room, MaxConnections = process, MaxConnectionsPerSource = source };
        Assert.Equal(valid, Validator.TryValidateObject(limits, new(limits), [], true));
    }

    [Fact]
    public async Task UnknownInvalidAndForeignCapabilitiesNeverGrantMembership()
    {
        await using var fixture = new Fixture();
        var first = Room();
        var second = Room();
        Assert.Equal((0, 0L), fixture.Service.Count(first.RoomId));
        fixture.Service.Disable(first.RoomId);
        fixture.Service.Leave("missing");
        fixture.Service.Disconnect("missing");
        Assert.Throws<HubException>(() => fixture.Service.CheckInvocation("missing"));
        Assert.Throws<HubException>(() => fixture.Service.Sync("missing"));
        fixture.Service.RecordUpdate(first.RoomId, "AQ==", 1);
        var firstToken = fixture.Service.CreateLink(first).Split('#')[1];
        var secondToken = fixture.Service.CreateLink(second).Split('#')[1];
        fixture.Service.RecordUpdate(first.RoomId, "AQ==", 1); // No viewers: retain no observer data.
        var caller = fixture.Connect(1);
        foreach (var token in new[] { null, "bad", new string('z', 43) })
        {
            Assert.Throws<HubException>(() => fixture.Service.Join(caller.Object, token!));
        }

        Assert.Throws<HubException>(() => fixture.Service.Revisions(caller.Object.ConnectionId));
        fixture.Service.Join(caller.Object, firstToken);
        Assert.Throws<HubException>(() => fixture.Service.Join(caller.Object, secondToken));
        fixture.Service.Leave(caller.Object.ConnectionId);
        fixture.Service.Join(caller.Object, secondToken);
        Assert.Equal(0, fixture.Service.Count(first.RoomId).Count);
        Assert.Equal(1, fixture.Service.Count(second.RoomId).Count);
    }

    [Fact]
    public async Task InvocationAndSourceAdmissionWindowsResetWithoutBorrowingEditorCapacity()
    {
        var clock = new FakeTimeProvider();
        await using var fixture = new Fixture(new ObserverLimits
        { Enabled = true, InvocationsPerSecond = 1, NewConnectionsPerSourcePerMinute = 1 }, clock);
        var room = Room();
        var token = fixture.Service.CreateLink(room).Split('#')[1];
        var caller = fixture.Connect(1, "192.0.2.9");
        fixture.Service.Join(caller.Object, token);
        fixture.Service.CheckInvocation(caller.Object.ConnectionId);
        Assert.Throws<HubException>(() => fixture.Service.CheckInvocation(caller.Object.ConnectionId));
        clock.Advance(TimeSpan.FromSeconds(1));
        fixture.Service.CheckInvocation(caller.Object.ConnectionId);
        Assert.Throws<HubException>(() => fixture.Connect(2, "192.0.2.9"));
        clock.Advance(TimeSpan.FromMinutes(1));
        _ = fixture.Connect(3, "192.0.2.9");
        Assert.Equal(1, fixture.Service.Count(room.RoomId).Count);
    }

    [Fact]
    public async Task SnapshotRequestsAreIdempotentWhilePendingAndRateLimitedAfterCompletion()
    {
        var clock = new FakeTimeProvider();
        await using var fixture = new Fixture(new ObserverLimits
        { Enabled = true, SnapshotRequestsPerConnectionPerMinute = 1 }, clock);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var frames = Capture(fixture, async message =>
        {
            if (message.GetProperty("Type").GetString() == "Snapshot")
            {
                entered.TrySetResult();
                await release.Task;
            }
        });
        var room = Room();
        var caller = fixture.Connect(1);
        fixture.Service.Join(caller.Object, fixture.Service.CreateLink(room).Split('#')[1]);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Service.Sync(caller.Object.ConnectionId);
        fixture.Service.Sync(caller.Object.ConnectionId);
        release.SetResult();
        await WaitUntilAsync(() => frames.Count >= 2);
        await WaitUntilAsync(() =>
        {
            try
            { fixture.Service.Sync(caller.Object.ConnectionId); return false; }
            catch (HubException error) { Assert.Equal("RateLimited", error.Message); return true; }
        });
        clock.Advance(TimeSpan.FromMinutes(1));
        fixture.Service.Sync(caller.Object.ConnectionId);
        await WaitUntilAsync(() => frames.Count(frame => frame.GetProperty("Type").GetString() == "Snapshot") == 2);
    }

    [Fact]
    public async Task CachedSnapshotsStillReflectLanguagePresenceAndDocumentChanges()
    {
        await using var fixture = new Fixture();
        var room = Room();
        _ = RoomStateBuilder.Participant(room);
        var frames = Capture(fixture);
        var caller = fixture.Connect(1);
        fixture.Service.Join(caller.Object, fixture.Service.CreateLink(room).Split('#')[1]);
        await WaitUntilAsync(() => frames.Count >= 2);
        await RequestSnapshotAsync(fixture, caller.Object.ConnectionId, frames, 2);
        var validator = new Mock<ILanguageValidator>();
        validator.Setup(validator => validator.IsValid(It.IsAny<RoomLanguage>())).Returns(true);
        _ = room.ChangeLanguageAsTestParticipant(new("csharp"), DateTimeOffset.UtcNow, validator.Object);
        await RequestSnapshotAsync(fixture, caller.Object.ConnectionId, frames, 3);
        room.AddUser(new("another-editor"), new("Alex"), 5);
        await RequestSnapshotAsync(fixture, caller.Object.ConnectionId, frames, 4);
        var state = frames.Last(frame => frame.GetProperty("Type").GetString() == "Snapshot");
        Assert.Equal("csharp", state.GetProperty("Language").GetString());
        Assert.Contains("Alex", state.GetProperty("Users").EnumerateArray().Select(user => user.GetString()));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("closed")]
    [InlineData("replaced")]
    public async Task RegistryDeletionClosureAndReplacementReleaseViewersAndInvalidateTheLink(string reason)
    {
        var clock = new FakeTimeProvider();
        await using var fixture = new Fixture(new ObserverLimits
        { Enabled = true, MaxPerRoom = 1, MaxConnections = 1, MaxConnectionsPerSource = 1 }, clock);
        var room = Room();
        var id = room.RoomId;
        fixture.Registry.Setup(registry => registry.TryGetRoom(id, out room)).Returns(true);
        var token = fixture.Service.CreateLink(room).Split('#')[1];
        var caller = fixture.Connect(1);
        fixture.Service.Join(caller.Object, token);
        await fixture.Service.StartAsync(CancellationToken.None);
        if (reason == "closed")
        {
            room.Close();
        }
        else if (reason == "missing")
        {
            fixture.Registry.Setup(registry => registry.TryGetRoom(id, out room)).Returns(false);
        }
        else
        {
            var replacement = Room();
            fixture.Registry.Setup(registry => registry.TryGetRoom(id, out replacement)).Returns(true);
        }
        await PublishUntilAsync(clock, () => fixture.Service.Count(id).Count == 0);
        caller.Verify(context => context.Abort(), Times.AtLeastOnce);
        var next = fixture.Connect(2);
        Assert.Throws<HubException>(() => fixture.Service.Join(next.Object, token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaptureOverflowRecoversFromTheAuthoritativeJournalWithoutEvictingEdits(bool bytes)
    {
        var clock = new FakeTimeProvider();
        await using var fixture = new Fixture(new ObserverLimits
        { Enabled = true, DeliveryQueueBytesPerConnection = 8192, DeliveryQueueEventsPerConnection = 2 }, clock);
        var room = Room();
        var id = room.RoomId;
        fixture.Registry.Setup(registry => registry.TryGetRoom(id, out room)).Returns(true);
        var frames = Capture(fixture);
        var caller = fixture.Connect(1);
        fixture.Service.Join(caller.Object, fixture.Service.CreateLink(room).Split('#')[1]);
        await WaitUntilAsync(() => frames.Count >= 2);
        var total = bytes ? 2 : 3;
        var update = bytes ? new string('A', 5000) : "AQ==";
        for (var revision = 1; revision <= total; revision++)
        {
            _ = room.AppendAsTestParticipant(Convert.FromBase64String(update), DateTimeOffset.UtcNow);
            fixture.Service.RecordUpdate(id, update, revision);
            if (revision == 1)
            {
                fixture.Service.RecordUpdate(id, update, revision); // Duplicate capture is harmless.
            }
        }
        await fixture.Service.StartAsync(CancellationToken.None);
        await PublishUntilAsync(clock, () => frames.Any(frame => frame.GetProperty("Type").GetString() == (bytes ? "RequiresResync" : "Update")));
        Assert.Equal(total, room.CreateYjsSyncState().Updates.Length);
        fixture.Service.RecordUpdate(id, "AQ==", 1); // Already published revisions are ignored.
        await RequestSnapshotAsync(fixture, caller.Object.ConnectionId, frames, 2);
        var recovered = frames.Last(frame => frame.GetProperty("Type").GetString() == "Snapshot");
        Assert.Equal(total, recovered.GetProperty("DocumentRevision").GetInt64());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SlowDeliveryQueueIsBoundedByBothBytesAndEventCount(bool byteBudget)
    {
        var clock = new FakeTimeProvider();
        await using var fixture = new Fixture(new ObserverLimits
        { Enabled = true, DeliveryQueueBytesPerConnection = 8192, DeliveryQueueEventsPerConnection = byteBudget ? 256 : 2 }, clock);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var frames = Capture(fixture, async message =>
        {
            if (message.GetProperty("Type").GetString() == "Update")
            {
                entered.TrySetResult();
                await release.Task;
            }
        });
        var room = Room();
        var id = room.RoomId;
        fixture.Registry.Setup(registry => registry.TryGetRoom(id, out room)).Returns(true);
        var caller = fixture.Connect(1);
        fixture.Service.Join(caller.Object, fixture.Service.CreateLink(room).Split('#')[1]);
        await WaitUntilAsync(() => frames.Count >= 2);
        await fixture.Service.StartAsync(CancellationToken.None);
        var payload = byteBudget ? new string('A', 3200) : "AQ==";
        for (var revision = 1; revision <= 6; revision++)
        {
            _ = room.AppendAsTestParticipant(Convert.FromBase64String(payload), DateTimeOffset.UtcNow);
            fixture.Service.RecordUpdate(id, payload, revision);
            clock.Advance(TimeSpan.FromMilliseconds(500));
            await Task.Delay(30);
        }
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        release.TrySetResult();
        await WaitUntilAsync(() => frames.Any(frame => frame.GetProperty("Type").GetString() == "RequiresResync"));
        Assert.Equal(6, room.DocumentRevision.Value);
        Assert.Equal(6, room.CreateYjsSyncState().Updates.Length);
    }

    private static ConcurrentQueue<JsonElement> Capture(Fixture fixture, Func<JsonElement, Task>? send = null)
    {
        var messages = new ConcurrentQueue<JsonElement>();
        fixture.Proxy.Setup(proxy => proxy.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(async (string method, object?[] args, CancellationToken cancellation) =>
            {
                if (method != "ObserverMessage")
                {
                    return;
                }

                var message = (JsonElement)args[0]!;
                messages.Enqueue(message);
                if (send is not null)
                {
                    await send(message).WaitAsync(cancellation);
                }
            });
        return messages;
    }

    private static async Task RequestSnapshotAsync(Fixture fixture, string connectionId, ConcurrentQueue<JsonElement> frames, int total)
    {
        await Task.Delay(10);
        fixture.Service.Sync(connectionId);
        await WaitUntilAsync(() => frames.Count(frame => frame.GetProperty("Type").GetString() == "Snapshot") >= total);
        await Task.Delay(10);
    }

    private static async Task WaitUntilAsync(Func<bool> ready)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!ready())
        {
            await Task.Delay(2, deadline.Token);
        }
    }

    private static async Task PublishUntilAsync(FakeTimeProvider clock, Func<bool> ready)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!ready())
        {
            clock.Advance(TimeSpan.FromMilliseconds(500));
            await Task.Delay(10, deadline.Token);
        }
    }
}
