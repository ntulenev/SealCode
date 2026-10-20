using FluentAssertions;

namespace Models.Tests;

public sealed class RoomLanguageChangeResultTests
{
    [Theory(DisplayName = "CtorShouldStoreAssignedRevisions")]
    [Trait("Category", "Unit")]
    [InlineData(1, 1)]
    [InlineData(int.MaxValue, long.MaxValue)]
    public void CtorShouldStoreAssignedRevisions(int version, long revision)
    {
        var result = new RoomLanguageChangeResult(new RoomVersion(version), revision);

        result.RoomVersion.Value.Should().Be(version);
        result.MetadataRevision.Should().Be(revision);
    }

    [Fact(DisplayName = "CtorShouldRejectDefaultRoomVersion")]
    [Trait("Category", "Unit")]
    public void CtorShouldRejectDefaultRoomVersion()
    {
        var action = () => new RoomLanguageChangeResult(default, 1);

        action.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("roomVersion");
    }

    [Theory(DisplayName = "CtorShouldRejectNonPositiveMetadataRevision")]
    [Trait("Category", "Unit")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    public void CtorShouldRejectNonPositiveMetadataRevision(long revision)
    {
        var action = () => new RoomLanguageChangeResult(new RoomVersion(1), revision);

        action.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("metadataRevision");
    }
}
