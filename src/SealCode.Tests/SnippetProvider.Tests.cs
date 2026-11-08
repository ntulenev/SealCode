using Abstractions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Models.Configuration;

using Moq;

using Persistence.Snippets;

using SealCode.Hosting;

namespace SealCode.Tests;

public sealed class SnippetProviderTests
{
    [Theory]
    [InlineData("File", true, false)]
    [InlineData("PostgreSql", true, true)]
    [InlineData("PostgreSql", false, false)]
    public void SelectsProviderWithoutConnectingAndDisabledLibraryNeedsNoConnection(
        string name, bool enabled, bool postgres)
    {
        using var provider = Services(name, enabled, postgres ? "Host=localhost;Database=snippets" : null);
        var repository = provider.GetRequiredService<ISnippetRepository>();
        if (postgres)
        {
            Assert.IsType<PostgreSqlSnippetRepository>(repository);
        }
        else
        {
            Assert.IsType<FileSnippetRepository>(repository);
        }
    }

    [Fact]
    public async Task EnabledPostgreSqlDefersMissingConnectionFailureUntilTheReadAsync()
    {
        using var provider = Services("PostgreSql", true, null);
        var repository = provider.GetRequiredService<ISnippetRepository>();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.LoadAsync(CancellationToken.None));
        Assert.Contains("ConnectionStrings:Snippets", error.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider Services(string name, bool enabled, string? connection)
    {
        var services = new ServiceCollection();
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(value => value.ContentRootPath).Returns(Path.GetTempPath());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:Snippets"] = connection }).Build());
        services.AddSingleton(Options.Create(new ApplicationConfiguration
        {
            AdminUsers = [],
            Languages = ["csharp"],
            Snippets = new() { Enabled = enabled, Provider = name }
        }));
        services.AddSealCodeSnippets(environment.Object);
        return services.BuildServiceProvider();
    }
}
