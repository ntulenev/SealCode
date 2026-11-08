using System.Diagnostics.CodeAnalysis;

using Npgsql;

using Persistence.Snippets;

namespace Persistence.Tests;

public sealed class PostgreSqlSnippetRepositoryIsolatedTests
{
    [Fact]
    public void ConstructorRejectsNullDataSource()
        => Assert.Throws<ArgumentNullException>(() => new PostgreSqlSnippetRepository(null!));

    [IsolatedPostgreSqlFact]
    public async Task EmptyLibraryMappingOrderingAndChangedTimestampAreSupportedAsync()
    {
        await WithDatabaseAsync(async source =>
        {
            var repository = new PostgreSqlSnippetRepository(source);
            var empty = await repository.LoadIfChangedAsync(null, CancellationToken.None);
            Assert.NotNull(empty);
            Assert.Empty(empty.Library.Categories);
            await SeedAsync(source);
            var loaded = await repository.LoadIfChangedAsync(empty.UpdatedAt, CancellationToken.None);
            Assert.NotNull(loaded);
            Assert.True(loaded.UpdatedAt > empty.UpdatedAt);
            Assert.Equal(["general", "sql"], loaded.Library.Categories.Select(value => value.Language.Value));
            var category = loaded.Library.Categories[1];
            Assert.Equal("SQL", category.Title);
            Assert.Equal([1, 2], category.Questions.Select(value => value.Number));
            var question = category.Questions[0];
            Assert.Equal("Topic", question.Topic);
            Assert.Equal("Coverage", question.Description);
            Assert.Equal("  SELECT 1;\n", question.Markdown);
            Assert.Null(await repository.LoadIfChangedAsync(loaded.UpdatedAt, CancellationToken.None));

            await ExecuteAsync(source, "UPDATE snippets.questions SET markdown = 'Changed' WHERE language = 'sql' AND number = 1");
            var changed = await repository.LoadIfChangedAsync(loaded.UpdatedAt, CancellationToken.None);
            Assert.NotNull(changed);
            Assert.Equal("Changed", changed.Library.Categories[1].Questions[0].Markdown);
            Assert.Equal("  SELECT 1;\n", question.Markdown);
            Assert.Equal(2, (await repository.LoadAsync(CancellationToken.None)).Categories.Count);
        });
    }

    [IsolatedPostgreSqlFact]
    public async Task InvalidCategoryAndMissingTimestampFailWithoutReturningPartialLibraryAsync()
    {
        await WithDatabaseAsync(async source =>
        {
            var repository = new PostgreSqlSnippetRepository(source);
            await SeedAsync(source);
            await ExecuteAsync(source, "INSERT INTO snippets.categories(language,title,sort_order) VALUES ('csharp','C#',20)");
            var failure = await Assert.ThrowsAsync<InvalidDataException>(
                () => repository.LoadAsync(CancellationToken.None));
            Assert.IsType<ArgumentException>(failure.InnerException);

            await ExecuteAsync(source, "DELETE FROM snippets.catalog_state");
            await Assert.ThrowsAsync<InvalidDataException>(() => repository.LoadAsync(CancellationToken.None));
            var trigger = await Assert.ThrowsAsync<PostgresException>(
                () => ExecuteAsync(source, "UPDATE snippets.questions SET topic = topic"));
            Assert.Equal("P0001", trigger.SqlState);
        });
    }

    [IsolatedPostgreSqlFact]
    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "All statements are fixed SQL literals declared in this test; no user input is accepted.")]
    public async Task ManualEditsAdvanceTimestampAndRollbacksDoNotPublishChangesAsync()
    {
        await WithDatabaseAsync(async source =>
        {
            await SeedAsync(source);
            var repository = new PostgreSqlSnippetRepository(source);
            var original = (await repository.LoadIfChangedAsync(null, CancellationToken.None))!;
            foreach (var sql in new[]
            {
                "INSERT INTO snippets.categories(language,title) VALUES ('csharp','C#')",
                "INSERT INTO snippets.questions(language,number,topic,description,markdown) VALUES ('sql',3,'Topic','Coverage','Question')",
                "UPDATE snippets.categories SET title = title",
                "UPDATE snippets.questions SET topic = topic",
                "DELETE FROM snippets.questions",
                "DELETE FROM snippets.categories",
                "TRUNCATE snippets.questions",
                "TRUNCATE snippets.categories CASCADE"
            })
            {
                await using var connection = await source.OpenConnectionAsync();
                await using var transaction = await connection.BeginTransactionAsync();
                await using var command = new NpgsqlCommand(sql, connection, transaction);
                _ = await command.ExecuteNonQueryAsync();
                await using var timestamp = new NpgsqlCommand(
                    "SELECT updated_at FROM snippets.catalog_state WHERE id = 1", connection, transaction);
                var updatedAt = new DateTimeOffset((DateTime)(await timestamp.ExecuteScalarAsync())!);
                Assert.True(updatedAt > original.UpdatedAt, sql);
                await transaction.RollbackAsync();
                Assert.Null(await repository.LoadIfChangedAsync(original.UpdatedAt, CancellationToken.None));
            }
        });
    }

    private static Task SeedAsync(NpgsqlDataSource source) => ExecuteAsync(source, """
        INSERT INTO snippets.categories(language,title,sort_order) VALUES ('sql','SQL',10),('general','General',0);
        INSERT INTO snippets.questions(language,number,topic,description,markdown) VALUES
            ('sql',2,'Second','More coverage','SELECT 2;'),
            ('sql',1,'Topic','Coverage',E'  SELECT 1;\n'),
            ('general',1,'General topic','General coverage','Question');
        """);

    private static async Task ExecuteAsync(NpgsqlDataSource source, string sql)
    {
        await using var command = source.CreateCommand(sql);
        _ = await command.ExecuteNonQueryAsync();
    }

    private static async Task WithDatabaseAsync(Func<NpgsqlDataSource, Task> test)
    {
        // This separate opt-in connection must point to a disposable PostgreSQL test server.
        var settings = new NpgsqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("SEALCODE_SNIPPETS_ISOLATED_TEST_CONNECTION"));
        await using var admin = NpgsqlDataSource.Create(settings.ConnectionString);
        var database = "sealcode_test_" + Guid.NewGuid().ToString("N");
        await ExecuteAsync(admin, $"CREATE DATABASE {database}");
        try
        {
            settings.Database = database;
            await using var source = NpgsqlDataSource.Create(settings.ConnectionString);
            var migration = await File.ReadAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "Migrations", "001_create_snippets.sql"));
            await ExecuteAsync(source, migration);
            await test(source);
        }
        finally
        {
            await ExecuteAsync(admin, $"DROP DATABASE {database} WITH (FORCE)");
        }
    }

    private sealed class IsolatedPostgreSqlFactAttribute : FactAttribute
    {
        public IsolatedPostgreSqlFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SEALCODE_SNIPPETS_ISOLATED_TEST_CONNECTION")))
            {
                Skip = "Set SEALCODE_SNIPPETS_ISOLATED_TEST_CONNECTION to a disposable server with CREATE DATABASE permission.";
            }
        }
    }
}
