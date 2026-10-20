using FluentAssertions;

namespace Models.Tests;

public sealed class RoomLeaveResultTests
{
    [Theory(DisplayName = "CtorShouldStoreSuccessfulRemoval")]
    [Trait("Category", "Unit")]
    [InlineData(1)]
    [InlineData(long.MaxValue)]
    public void CtorShouldStoreSuccessfulRemoval(long revision)
    {
        var user = new RoomUser("Alice");
        RoomUser[] users = [new("Bob")];

        var result = new RoomLeaveResult(true, user, users, revision);

        result.Removed.Should().BeTrue();
        result.User.Should().Be(user);
        result.Users.Should().Equal(users);
        result.PresenceRevision.Should().Be(revision);
    }

    [Theory(DisplayName = "CtorShouldAllowMissingParticipant")]
    [Trait("Category", "Unit")]
    [InlineData(0)]
    [InlineData(3)]
    public void CtorShouldAllowMissingParticipant(long revision)
    {
        var result = new RoomLeaveResult(false, default, [], revision);

        result.Removed.Should().BeFalse();
        result.User.Should().Be(default(RoomUser));
        result.Users.Should().BeEmpty();
        result.PresenceRevision.Should().Be(revision);
    }

    [Fact(DisplayName = "CtorShouldAllowEmptyRoomAfterRemoval")]
    [Trait("Category", "Unit")]
    public void CtorShouldAllowEmptyRoomAfterRemoval()
    {
        var result = new RoomLeaveResult(true, new RoomUser("Alice"), [], 2);

        result.Users.Should().BeEmpty();
    }

    [Fact(DisplayName = "CtorShouldAllowRemainingUserWithSameName")]
    [Trait("Category", "Unit")]
    public void CtorShouldAllowRemainingUserWithSameName()
    {
        var user = new RoomUser("Alice");

        var result = new RoomLeaveResult(true, user, [user], 3);

        result.Users.Should().Equal(user);
    }

    [Fact(DisplayName = "CtorShouldRejectNullUsers")]
    [Trait("Category", "Unit")]
    public void CtorShouldRejectNullUsers()
    {
        var action = () => new RoomLeaveResult(false, default, null!, 0);

        action.Should().Throw<ArgumentNullException>().WithParameterName("users");
    }

    [Theory(DisplayName = "CtorShouldRejectInconsistentRemovedUser")]
    [Trait("Category", "Unit")]
    [InlineData(true)]
    [InlineData(false)]
    public void CtorShouldRejectInconsistentRemovedUser(bool removed)
    {
        var user = removed ? default : new RoomUser("Alice");
        var action = () => new RoomLeaveResult(removed, user, [], 2);

        action.Should().Throw<ArgumentException>().WithParameterName("user");
    }

    [Fact(DisplayName = "CtorShouldRejectDefaultRemainingUser")]
    [Trait("Category", "Unit")]
    public void CtorShouldRejectDefaultRemainingUser()
    {
        var action = () => new RoomLeaveResult(false, default, [default], 1);

        action.Should().Throw<ArgumentException>().WithParameterName("users");
    }

    [Theory(DisplayName = "CtorShouldRejectInvalidPresenceRevision")]
    [Trait("Category", "Unit")]
    [InlineData(false, -1)]
    [InlineData(true, -1)]
    [InlineData(true, 0)]
    [InlineData(false, long.MinValue)]
    public void CtorShouldRejectInvalidPresenceRevision(bool removed, long revision)
    {
        var user = removed ? new RoomUser("Alice") : default;
        var action = () => new RoomLeaveResult(removed, user, [], revision);

        action.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("presenceRevision");
    }

    [Fact(DisplayName = "UsersShouldBeDetachedFromInputAndReturnedArrays")]
    [Trait("Category", "Unit")]
    public void UsersShouldBeDetachedFromInputAndReturnedArrays()
    {
        var original = new RoomUser("Bob");
        RoomUser[] users = [original];
        var result = new RoomLeaveResult(true, new RoomUser("Alice"), users, 3);

        users[0] = default;
        var snapshot = result.Users;
        snapshot[0] = default;

        result.Users.Should().Equal(original);
    }
}
