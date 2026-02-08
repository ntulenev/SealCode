using FluentAssertions;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Models;
using Models.Configuration;

using Moq;

namespace Transport.Tests;

public sealed class RoomHubTests
{
    [Fact(DisplayName = "CtorShouldThrowWhenRegistryIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenRegistryIsNull()
    {
        var settings = Options.Create(new ApplicationConfiguration { AdminUsers = [], MaxUsersPerRoom = 3 });
        var logger = new Mock<ILogger<RoomHub>>(MockBehavior.Strict).Object;

        var action = () => new RoomHub(null!, settings, logger);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "CtorShouldThrowWhenSettingsIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenSettingsIsNull()
    {
        var registry = new Mock<Abstractions.IRoomRegistry>(MockBehavior.Strict).Object;
        var logger = new Mock<ILogger<RoomHub>>(MockBehavior.Strict).Object;

        var action = () => new RoomHub(registry, null!, logger);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "CtorShouldThrowWhenLoggerIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenLoggerIsNull()
    {
        var registry = new Mock<Abstractions.IRoomRegistry>(MockBehavior.Strict).Object;
        var settings = Options.Create(new ApplicationConfiguration { AdminUsers = [], MaxUsersPerRoom = 3 });

        var action = () => new RoomHub(registry, settings, null!);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "JoinRoomAsyncShouldThrowWhenRoomIdIsEmpty")]
    [Trait("Category", "Unit")]
    public async Task JoinRoomAsyncShouldThrowWhenRoomIdIsEmpty()
    {
        var hub = CreateHub(new Mock<Abstractions.IRoomRegistry>(MockBehavior.Strict).Object);

        Func<Task> action = () => hub.JoinRoomAsync(" ", "Alice");

        await action.Should().ThrowAsync<HubException>();
    }

    [Fact(DisplayName = "JoinRoomAsyncShouldThrowWhenRoomIsNotFound")]
    [Trait("Category", "Unit")]
    public async Task JoinRoomAsyncShouldThrowWhenRoomIsNotFound()
    {
        var registry = new Mock<Abstractions.IRoomRegistry>(MockBehavior.Strict);
        registry.Setup(r => r.TryGetRoom(It.IsAny<RoomId>(), out It.Ref<RoomState>.IsAny))
            .Returns(false);
        var hub = CreateHub(registry.Object);

        Func<Task> action = () => hub.JoinRoomAsync("room", "Alice");

        await action.Should().ThrowAsync<HubException>();
    }

    [Fact(DisplayName = "JoinRoomAsyncShouldThrowWhenDisplayNameIsEmpty")]
    [Trait("Category", "Unit")]
    public async Task JoinRoomAsyncShouldThrowWhenDisplayNameIsEmpty()
    {
        var registry = new Mock<Abstractions.IRoomRegistry>(MockBehavior.Strict);
        var room = CreateRoomState();
        registry.Setup(r => r.TryGetRoom(It.IsAny<RoomId>(), out room))
            .Returns(true);
        var hub = CreateHub(registry.Object);

        Func<Task> action = () => hub.JoinRoomAsync("room", " ");

        await action.Should().ThrowAsync<HubException>();
    }

    private static RoomHub CreateHub(Abstractions.IRoomRegistry registry)
    {
        var settings = Options.Create(new ApplicationConfiguration { AdminUsers = [], MaxUsersPerRoom = 3 });
        var logger = new Mock<ILogger<RoomHub>>(MockBehavior.Strict).Object;

        return new RoomHub(registry, settings, logger);
    }

    private static RoomState CreateRoomState() => new(
        new RoomId("room"),
        new RoomName("Room"),
        new RoomLanguage("csharp"),
        new RoomText("text"),
        new RoomVersion(1),
        new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
        new CreatedBy("admin"));
}
