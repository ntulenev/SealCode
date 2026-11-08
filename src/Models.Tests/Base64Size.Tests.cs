namespace Models.Tests;

public sealed class Base64SizeTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 4)]
    [InlineData(2, 4)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    public void EncodedLengthAccountsForPadding(int bytes, long characters) => Assert.Equal(characters, Base64Size.EncodedLength(bytes));
}
