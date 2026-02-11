using Abstractions;

using FluentAssertions;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

using Models;
using Models.Exceptions;

using Moq;

namespace SealCode.Tests;

public sealed class RoomHubTests
{
    [Fact(DisplayName = "CtorShouldThrowWhenRoomManagerIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenRoomManagerIsNull()
    {
        var validator = new Mock<ILanguageValidator>(MockBehavior.Strict).Object;
        var logger = new Mock<ILogger<RoomHub>>(MockBehavior.Strict).Object;

        var action = () => new RoomHub(null!, validator, logger);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "CtorShouldThrowWhenLanguageValidatorIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenLanguageValidatorIsNull()
    {
        var roomManager = new Mock<IRoomManager>(MockBehavior.Strict).Object;
        var logger = new Mock<ILogger<RoomHub>>().Object;

        var action = () => new RoomHub(roomManager, null!, logger);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "CtorShouldThrowWhenLoggerIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenLoggerIsNull()
    {
        var roomManager = new Mock<IRoomManager>(MockBehavior.Strict).Object;
        var validator = new Mock<ILanguageValidator>(MockBehavior.Strict).Object;

        var action = () => new RoomHub(roomManager, validator, null!);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "JoinRoomAsyncShouldThrowWhenRoomIdIsEmpty")]
    [Trait("Category", "Unit")]
    public async Task JoinRoomAsyncShouldThrowWhenRoomIdIsEmpty()
    {
        using var hub = CreateHub();

        Func<Task> action = () => hub.JoinRoomAsync(" ", "Alice");

        await action.Should().ThrowAsync<HubException>();
    }

    [Fact(DisplayName = "JoinRoomAsyncShouldThrowWhenRoomIsNotFound")]
    [Trait("Category", "Unit")]
    public async Task JoinRoomAsyncShouldThrowWhenRoomIsNotFound()
    {
        var roomManager = new Mock<IRoomManager>(MockBehavior.Strict);
        roomManager.Setup(m => m.RegisterUserInRoom(
                It.IsAny<RoomId>(),
                It.IsAny<ConnectionId>(),
                It.IsAny<DisplayName>()))
            .Throws(new RoomNotFoundException());
        using var hub = CreateHub(roomManager.Object);

        Func<Task> action = () => hub.JoinRoomAsync("room", "Alice");

        await action.Should().ThrowAsync<HubException>();
        roomManager.VerifyAll();
    }

    [Fact(DisplayName = "JoinRoomAsyncShouldThrowWhenDisplayNameIsEmpty")]
    [Trait("Category", "Unit")]
    public async Task JoinRoomAsyncShouldThrowWhenDisplayNameIsEmpty()
    {
        using var hub = CreateHub();

        Func<Task> action = () => hub.JoinRoomAsync("room", " ");

        await action.Should().ThrowAsync<HubException>();
    }

    private static RoomHub CreateHub(IRoomManager? roomManager = null)
    {
        roomManager ??= new Mock<IRoomManager>(MockBehavior.Strict).Object;
        var validator = new Mock<ILanguageValidator>(MockBehavior.Strict).Object;
        var logger = new Mock<ILogger<RoomHub>>().Object;
        var context = new Mock<HubCallerContext>(MockBehavior.Strict);
        context.SetupGet(c => c.ConnectionId).Returns("conn-1");
        context.SetupGet(c => c.ConnectionAborted).Returns(CancellationToken.None);
        context.SetupGet(c => c.Items).Returns(new Dictionary<object, object?>());

        var hub = new RoomHub(roomManager, validator, logger)
        {
            Context = context.Object
        };

        return hub;
    }
}
