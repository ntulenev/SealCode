using FluentAssertions;

namespace Models.Tests;

public sealed class RoomDocumentRevisionTests
{
    [Fact(DisplayName = "CtorShouldRejectNegativeRevision")]
    [Trait("Category", "Unit")]
    public void CtorShouldRejectNegativeRevision()
    {
        var action = () => new RoomDocumentRevision(-1);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact(DisplayName = "NextShouldIncrementRevision")]
    [Trait("Category", "Unit")]
    public void NextShouldIncrementRevision()
    {
        var revision = new RoomDocumentRevision(41);

        revision.Next().Value.Should().Be(42);
    }
}
