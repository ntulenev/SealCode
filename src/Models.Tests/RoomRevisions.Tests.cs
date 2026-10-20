using FluentAssertions;

namespace Models.Tests;

public sealed class RoomRevisionsTests
{
    [Theory(DisplayName = "CtorShouldStoreRevisions")]
    [Trait("Category", "Unit")]
    [InlineData(0, 0, 0)]
    [InlineData(1, 2, 3)]
    [InlineData(long.MaxValue, long.MaxValue, long.MaxValue)]
    public void CtorShouldStoreRevisions(long document, long metadata, long presence)
    {
        var result = new RoomRevisions(document, metadata, presence);

        result.DocumentRevision.Should().Be(document);
        result.MetadataRevision.Should().Be(metadata);
        result.PresenceRevision.Should().Be(presence);
    }

    [Theory(DisplayName = "CtorShouldRejectNegativeRevision")]
    [Trait("Category", "Unit")]
    [InlineData(-1, 0, 0, "documentRevision")]
    [InlineData(0, -1, 0, "metadataRevision")]
    [InlineData(0, 0, -1, "presenceRevision")]
    [InlineData(long.MinValue, 0, 0, "documentRevision")]
    [InlineData(0, long.MinValue, 0, "metadataRevision")]
    [InlineData(0, 0, long.MinValue, "presenceRevision")]
    public void CtorShouldRejectNegativeRevision(
        long document,
        long metadata,
        long presence,
        string parameter)
    {
        var action = () => new RoomRevisions(document, metadata, presence);

        action.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(parameter);
    }
}
