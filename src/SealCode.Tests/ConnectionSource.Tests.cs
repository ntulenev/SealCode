using System.Net;

using SealCode.Hosting;

namespace SealCode.Tests;

public sealed class ConnectionSourceTests
{
    [Theory]
    [InlineData(null, "unknown")]
    [InlineData("192.0.2.1", "192.0.2.1")]
    [InlineData("::ffff:192.0.2.1", "192.0.2.1")]
    [InlineData("2001:db8:1:2::1", "2001:db8:1:2::")]
    [InlineData("2001:db8:1:2:ffff::2", "2001:db8:1:2::")]
    [InlineData("2001:db8:1:3::1", "2001:db8:1:3::")]
    public void NormalizesSources(string? address, string expected)
    {
        Assert.Equal(expected, ConnectionSource.From(address is null ? null : IPAddress.Parse(address)));
    }
}
