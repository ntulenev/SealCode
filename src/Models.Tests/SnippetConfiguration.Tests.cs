using Models.Configuration;

namespace Models.Tests;

public sealed class SnippetConfigurationTests
{
    [Theory]
    [InlineData("File", false)]
    [InlineData("PostgreSql", true)]
    [InlineData("postgresql", true)]
    public void ProviderDeterminesDatabaseUsage(string provider, bool expected)
        => Assert.Equal(expected, new SnippetConfiguration { Provider = provider }.UsesPostgreSql);
}
