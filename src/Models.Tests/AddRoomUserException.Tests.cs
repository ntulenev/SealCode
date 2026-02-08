using FluentAssertions;

namespace Models.Tests;

public sealed class AddRoomUserExceptionTests
{
    [Fact(DisplayName = "CtorShouldSetMessage")]
    [Trait("Category", "Unit")]
    public void CtorShouldSetMessage()
    {
        var exception = new AddRoomUserException("boom");

        exception.Message.Should().Be("boom");
    }

    [Fact(DisplayName = "CtorShouldSetMessageAndInnerException")]
    [Trait("Category", "Unit")]
    public void CtorShouldSetMessageAndInnerException()
    {
        var inner = new InvalidOperationException("inner");

        var exception = new AddRoomUserException("boom", inner);

        exception.Message.Should().Be("boom");
        exception.InnerException.Should().Be(inner);
    }
}
