using Npgsql;

using Persistence.Snippets;

namespace Persistence.Tests;

public sealed class PostgreSqlSnippetRepositoryTests
{
    [PostgreSqlFact]
    public async Task ReadsPopulatedDatabaseAndSkipsUnchangedSnapshotWithoutWritesAsync()
    {
        var connection = Environment.GetEnvironmentVariable("SEALCODE_SNIPPETS_TEST_CONNECTION")!;
        await using var source = NpgsqlDataSource.Create(connection);
        var repository = new PostgreSqlSnippetRepository(source);

        var first = await repository.LoadIfChangedAsync(null, CancellationToken.None);
        Assert.NotNull(first);
        Assert.NotEmpty(first.Library.Categories);
        Assert.All(first.Library.Categories, category => Assert.NotEmpty(category.Questions));
        Assert.Null(await repository.LoadIfChangedAsync(first.UpdatedAt, CancellationToken.None));
        var library = await repository.LoadAsync(CancellationToken.None);
        Assert.Equal(first.Library.Categories.Select(category => category.Language),
            library.Categories.Select(category => category.Language));

        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => repository.LoadIfChangedAsync(null, canceled.Token));
    }

    private sealed class PostgreSqlFactAttribute : FactAttribute
    {
        public PostgreSqlFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SEALCODE_SNIPPETS_TEST_CONNECTION")))
            {
                Skip = "Set SEALCODE_SNIPPETS_TEST_CONNECTION to a migrated, populated PostgreSQL database (read-only test).";
            }
        }
    }
}
