namespace Models.Tests;

public sealed class RoomKindWireNamesTests
{
    [Theory]
    [InlineData(null, RoomKind.Code)]
    [InlineData("code", RoomKind.Code)]
    [InlineData("whiteboard", RoomKind.Whiteboard)]
    public void ParsesWireName(string? name, RoomKind expected)
    {
        Assert.True(RoomKindWireNames.TryParseWireName(name, out var kind));
        Assert.Equal(expected, kind);
        Assert.Equal(name ?? "code", kind.ToWireName());
    }

    [Theory]
    [InlineData("")]
    [InlineData("CODE")]
    [InlineData("unknown")]
    public void RejectsUnknownName(string name)
        => Assert.False(RoomKindWireNames.TryParseWireName(name, out _));

    [Fact]
    public void RejectsUndefinedKind()
        => Assert.Throws<ArgumentOutOfRangeException>(() => ((RoomKind)42).ToWireName());
}
