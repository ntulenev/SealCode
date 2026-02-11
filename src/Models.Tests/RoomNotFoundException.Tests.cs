using FluentAssertions;
using Models.Exceptions;

namespace Models.Tests;

public sealed class RoomNotFoundExceptionTests
{
    [Fact(DisplayName = "CtorShouldSetDefaultMessage")]
    [Trait("Category", "Unit")]
    public void CtorShouldSetDefaultMessage()
    {
        var exception = new RoomNotFoundException();

        exception.Message.Should().Be("Room not found");
    }

    [Fact(DisplayName = "CtorShouldSetMessage")]
    [Trait("Category", "Unit")]
    public void CtorShouldSetMessage()
    {
        var exception = new RoomNotFoundException("boom");

        exception.Message.Should().Be("boom");
    }

    [Fact(DisplayName = "CtorShouldSetMessageAndInnerException")]
    [Trait("Category", "Unit")]
    public void CtorShouldSetMessageAndInnerException()
    {
        var inner = new InvalidOperationException("inner");

        var exception = new RoomNotFoundException("boom", inner);

        exception.Message.Should().Be("boom");
        exception.InnerException.Should().Be(inner);
    }
}
