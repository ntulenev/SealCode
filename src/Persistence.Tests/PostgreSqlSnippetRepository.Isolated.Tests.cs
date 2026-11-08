using System.Diagnostics.CodeAnalysis;

using Npgsql;

using Persistence.Snippets;

namespace Persistence.Tests;

public sealed class PostgreSqlSnippetRepositoryIsolatedTests
{
    [IsolatedPostgreSqlFact]
    public async Task UpgradePreservesExistingMarkdownAndLoadsNumberedFollowUpsAsync()
    {
        await WithDatabaseAsync(async source =>
        {
            await SeedAsync(source);
            await ExecuteAsync(source, await File.ReadAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "Migrations", "002_question_steps.sql")));
            var repository = new PostgreSqlSnippetRepository(source);
            var original = (await repository.LoadIfChangedAsync(null, CancellationToken.None))!;
            var first = original.Library.Categories[1].Questions[0];
            Assert.Equal("  SELECT 1;\n", Assert.Single(first.Steps).Markdown);

            await ExecuteAsync(source, """
                INSERT INTO snippets.question_steps(language,question_number,number,title,markdown) VALUES
                    ('sql',1,3,'What if we filter?','SELECT 3;'),
                    ('sql',1,2,'What if we change it?','SELECT 2;');
                """);
            var changed = await repository.LoadIfChangedAsync(original.UpdatedAt, CancellationToken.None);
            Assert.NotNull(changed);
            var question = changed.Library.Categories[1].Questions[0];
            Assert.Equal([1, 2, 3], question.Steps.Select(step => step.Number));
            Assert.Equal("What if we change it?", question.Steps[1].Title);
            Assert.Equal("SELECT 2;", question.Steps[1].Markdown);
            Assert.Equal("  SELECT 1;\n", question.Markdown);
            Assert.Single(first.Steps);
            Assert.Single(changed.Library.Categories[1].Questions[1].Steps);
            Assert.Null(await repository.LoadIfChangedAsync(changed.UpdatedAt, CancellationToken.None));

            await ExecuteAsync(source, "UPDATE snippets.question_steps SET markdown = 'Changed follow-up' WHERE language = 'sql' AND question_number = 1 AND number = 2");
            var updated = await repository.LoadIfChangedAsync(changed.UpdatedAt, CancellationToken.None);
            Assert.NotNull(updated);
            Assert.Equal("Changed follow-up", updated.Library.Categories[1].Questions[0].Steps[1].Markdown);
            await ExecuteAsync(source, "DELETE FROM snippets.questions WHERE language = 'sql' AND number = 1");
            await using var count = source.CreateCommand("SELECT count(*) FROM snippets.question_steps");
            Assert.Equal(0L, await count.ExecuteScalarAsync());
        }, applyStepMigration: false);
    }

    [IsolatedPostgreSqlFact]
    public async Task IncompleteProgressionsRejectTheWholeDatabaseSnapshotAsync()
    {
        await WithDatabaseAsync(async source =>
        {
            await SeedAsync(source);
            await ExecuteAsync(source, "INSERT INTO snippets.question_steps(language,question_number,number,title,markdown) VALUES ('sql',1,3,'Missing step 2','SELECT 3;')");
            await Assert.ThrowsAsync<InvalidDataException>(() => new PostgreSqlSnippetRepository(source).LoadAsync(CancellationToken.None));
        });
    }

    [Fact]
    public void ConstructorRejectsNullDataSource()
    {
        Assert.Throws<ArgumentNullException>(() => new PostgreSqlSnippetRepository((NpgsqlDataSource)null!));
        Assert.Throws<ArgumentNullException>(() => new PostgreSqlSnippetRepository((Func<NpgsqlDataSource>)null!));
    }

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
    public async Task EmptyCategoryLoadsAndMissingTimestampFailsWithoutReturningPartialLibraryAsync()
    {
        await WithDatabaseAsync(async source =>
        {
            var repository = new PostgreSqlSnippetRepository(source);
            await SeedAsync(source);
            await ExecuteAsync(source, "INSERT INTO snippets.categories(language,title,sort_order) VALUES ('csharp','C#',20)");
            var loaded = await repository.LoadAsync(CancellationToken.None);
            Assert.Empty(loaded.Categories.Single(category => category.Language.Value == "csharp").Questions);

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
                "TRUNCATE snippets.questions CASCADE",
                "TRUNCATE snippets.categories CASCADE",
                "INSERT INTO snippets.question_steps(language,question_number,number,title,markdown) VALUES ('sql',1,2,'Follow-up','SELECT 2;')",
                "UPDATE snippets.question_steps SET title = title",
                "DELETE FROM snippets.question_steps",
                "TRUNCATE snippets.question_steps"
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

    [IsolatedPostgreSqlFact]
    public async Task QuestionPublicationIsAtomicAndRejectsStaleVersionsAsync()
    {
        await WithDatabaseAsync(async source =>
        {
            await SeedAsync(source);
            var repository = new PostgreSqlSnippetRepository(source);
            var original = (await repository.LoadIfChangedAsync(null, CancellationToken.None))!;
            var question = new Models.Snippets.SnippetQuestion(1, "Edited topic", "Edited description",
                [new Models.Snippets.SnippetStep(1, "First", "  SELECT 'quoted';\n"),
                 new Models.Snippets.SnippetStep(2, "Next", "SELECT 20;")]);
            Assert.True(await repository.UpdateQuestionAsync(new("sql"), question, original.UpdatedAt, CancellationToken.None));
            var updated = (await repository.LoadIfChangedAsync(original.UpdatedAt, CancellationToken.None))!;
            var saved = updated.Library.Categories[1].Questions[0];
            Assert.Equal("Edited topic", saved.Topic);
            Assert.Equal("  SELECT 'quoted';\n", saved.Markdown);
            Assert.Equal("SELECT 20;", saved.Steps[1].Markdown);
            Assert.False(await repository.UpdateQuestionAsync(new("sql"), question, original.UpdatedAt, CancellationToken.None));
            Assert.Null(await repository.LoadIfChangedAsync(updated.UpdatedAt, CancellationToken.None));
            // A failing follow-up must roll back the first-example update and trigger revision too.
            await ExecuteAsync(source, "ALTER TABLE snippets.question_steps ADD CONSTRAINT test_reject CHECK (markdown <> 'reject')");
            var invalid = new Models.Snippets.SnippetQuestion(1, "Must roll back", "Coverage",
                [new Models.Snippets.SnippetStep(1, "First", "changed"),
                 new Models.Snippets.SnippetStep(2, "Next", "reject")]);
            await Assert.ThrowsAsync<PostgresException>(() => repository.UpdateQuestionAsync(
                new("sql"), invalid, updated.UpdatedAt, CancellationToken.None));
            Assert.Null(await repository.LoadIfChangedAsync(updated.UpdatedAt, CancellationToken.None));
            var single = new Models.Snippets.SnippetQuestion(1, "Single", "Coverage", "SELECT 99;");
            Assert.True(await repository.UpdateQuestionAsync(new("sql"), single, updated.UpdatedAt, CancellationToken.None));
            var final = await repository.LoadAsync(CancellationToken.None);
            Assert.Single(final.Categories[1].Questions[0].Steps);
            Assert.Equal("SELECT 2;", final.Categories[1].Questions[1].Markdown);
        });
    }

    [IsolatedPostgreSqlFact]
    public async Task MissingQuestionsAndCanceledWritesDoNotAdvanceTheRevisionAndConcurrentWritersHaveOneWinnerAsync()
    {
        await WithDatabaseAsync(async source =>
        {
            await SeedAsync(source);
            var repository = new PostgreSqlSnippetRepository(source);
            var original = (await repository.LoadIfChangedAsync(null, CancellationToken.None))!;
            Assert.False(await repository.UpdateQuestionAsync(new("sql"), new(999, "Missing", "Coverage", "Source"),
                original.UpdatedAt, CancellationToken.None));
            Assert.False(await repository.UpdateQuestionAsync(new("missing"), new(1, "Missing", "Coverage", "Source"),
                original.UpdatedAt, CancellationToken.None));
            Assert.Null(await repository.LoadIfChangedAsync(original.UpdatedAt, CancellationToken.None));
            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.UpdateQuestionAsync(
                new("sql"), new(1, "Canceled", "Coverage", "Source"), original.UpdatedAt, canceled.Token));
            Assert.Null(await repository.LoadIfChangedAsync(original.UpdatedAt, CancellationToken.None));
            var results = await Task.WhenAll(
                repository.UpdateQuestionAsync(new("sql"), new(1, "Writer one", "Coverage", "First"), original.UpdatedAt, CancellationToken.None),
                repository.UpdateQuestionAsync(new("sql"), new(1, "Writer two", "Coverage", "Second"), original.UpdatedAt, CancellationToken.None));
            Assert.Single(results, saved => saved);
            var changed = await repository.LoadAsync(CancellationToken.None);
            Assert.Equal(results[0] ? "First" : "Second", changed.Categories[1].Questions[0].Markdown);
            await ExecuteAsync(source, "DELETE FROM snippets.catalog_state");
            Assert.False(await repository.UpdateQuestionAsync(new("sql"), new(1, "Must not write", "Coverage", "Source"),
                original.UpdatedAt, CancellationToken.None));
            await using var persisted = source.CreateCommand("SELECT markdown FROM snippets.questions WHERE language = 'sql' AND number = 1");
            Assert.Equal(results[0] ? "First" : "Second", await persisted.ExecuteScalarAsync());
        });
    }

    [Fact]
    public async Task UpdateValidatesArgumentsBeforeConnectingAsync()
    {
        var repository = new PostgreSqlSnippetRepository(() => throw new InvalidOperationException("Must not connect"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.UpdateQuestionAsync(default,
            new(1, "Topic", "Coverage", "Source"), DateTimeOffset.UtcNow, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.UpdateQuestionAsync(new("sql"),
            null!, DateTimeOffset.UtcNow, CancellationToken.None));
    }

    [IsolatedPostgreSqlFact]
    public async Task CategoryAndQuestionLifecyclePreservesChildrenAndRejectsConflictsAsync()
    {
        await WithDatabaseAsync(async source =>
        {
            var repository = new PostgreSqlSnippetRepository(source);
            async Task<Abstractions.VersionedSnippetLibrary> SnapshotAsync()
            {
                return (await repository.LoadIfChangedAsync(null, CancellationToken.None))!;
            }
            var snapshot = await SnapshotAsync();
            var sql = new Models.Snippets.SnippetCategory(new("sql"), "SQL questions", [], 10);
            Assert.True(await repository.SaveCategoryAsync(null, sql, snapshot.UpdatedAt, CancellationToken.None));
            snapshot = await SnapshotAsync();
            Assert.Empty(Assert.Single(snapshot.Library.Categories).Questions);
            Assert.Equal(10, snapshot.Library.Categories[0].SortOrder);
            Assert.False(await repository.SaveCategoryAsync(null, sql, snapshot.UpdatedAt, CancellationToken.None));
            Assert.Null(await repository.LoadIfChangedAsync(snapshot.UpdatedAt, CancellationToken.None));
            var question = new Models.Snippets.SnippetQuestion(1, "Topic", "Coverage",
                [new Models.Snippets.SnippetStep(1, "Original", "SELECT 1;"), new Models.Snippets.SnippetStep(2, "Next", "SELECT 2;")]);
            Assert.True(await repository.CreateQuestionAsync(new("sql"), question, snapshot.UpdatedAt, CancellationToken.None));
            snapshot = await SnapshotAsync();
            Assert.False(await repository.CreateQuestionAsync(new("sql"), question, snapshot.UpdatedAt, CancellationToken.None));
            Assert.False(await repository.CreateQuestionAsync(new("missing"), question, snapshot.UpdatedAt, CancellationToken.None));
            Assert.Null(await repository.LoadIfChangedAsync(snapshot.UpdatedAt, CancellationToken.None));
            Assert.True(await repository.SaveCategoryAsync(new("sql"), new(new("csharp"), "Renamed", [], -10),
                snapshot.UpdatedAt, CancellationToken.None));
            snapshot = await SnapshotAsync();
            var renamed = Assert.Single(snapshot.Library.Categories);
            Assert.Equal("csharp", renamed.Language.Value);
            Assert.Equal("Renamed", renamed.Title);
            Assert.Equal(2, Assert.Single(renamed.Questions).Steps.Count);
            Assert.True(await repository.SaveCategoryAsync(null, new(new("general"), "General", [], -20),
                snapshot.UpdatedAt, CancellationToken.None));
            snapshot = await SnapshotAsync();
            Assert.Equal(["general", "csharp"], snapshot.Library.Categories.Select(category => category.Language.Value));
            var moved = new Models.Snippets.SnippetQuestion(7, "Moved topic", "Coverage", question.Steps);
            Assert.True(await repository.RelocateQuestionAsync(new("csharp"), 1, new("general"), moved,
                snapshot.UpdatedAt, CancellationToken.None));
            snapshot = await SnapshotAsync();
            Assert.Empty(snapshot.Library.Categories[1].Questions);
            Assert.Equal(7, Assert.Single(snapshot.Library.Categories[0].Questions).Number);
            Assert.Equal(2, snapshot.Library.Categories[0].Questions[0].Steps.Count);
            Assert.False(await repository.SaveCategoryAsync(new("csharp"), new(new("general"), "Collision", []), snapshot.UpdatedAt, CancellationToken.None));
            Assert.False(await repository.SaveCategoryAsync(new("missing"), sql, snapshot.UpdatedAt, CancellationToken.None));
            Assert.False(await repository.RelocateQuestionAsync(new("general"), 7, new("missing"), moved, snapshot.UpdatedAt, CancellationToken.None));
            Assert.True(await repository.CreateQuestionAsync(new("general"), new(8, "Other", "Coverage", "Other source"), snapshot.UpdatedAt, CancellationToken.None));
            snapshot = await SnapshotAsync();
            Assert.False(await repository.RelocateQuestionAsync(new("general"), 7, new("general"), new(8, "Collision", "Coverage", "Source"), snapshot.UpdatedAt, CancellationToken.None));
            Assert.False(await repository.DeleteQuestionAsync(new("general"), 99, snapshot.UpdatedAt, CancellationToken.None));
            Assert.False(await repository.DeleteCategoryAsync(new("missing"), snapshot.UpdatedAt, CancellationToken.None));
            Assert.Null(await repository.LoadIfChangedAsync(snapshot.UpdatedAt, CancellationToken.None));
            var stale = snapshot.UpdatedAt;
            Assert.True(await repository.DeleteQuestionAsync(new("general"), 7, snapshot.UpdatedAt, CancellationToken.None));
            Assert.False(await repository.DeleteCategoryAsync(new("general"), stale, CancellationToken.None));
            snapshot = await SnapshotAsync();
            Assert.Equal(8, Assert.Single(snapshot.Library.Categories[0].Questions).Number);
            Assert.True(await repository.DeleteCategoryAsync(new("general"), snapshot.UpdatedAt, CancellationToken.None));
            snapshot = await SnapshotAsync();
            Assert.Equal("csharp", Assert.Single(snapshot.Library.Categories).Language.Value);
            await using var count = source.CreateCommand("SELECT count(*) FROM snippets.question_steps");
            Assert.Equal(0L, await count.ExecuteScalarAsync());
            Assert.True(await repository.DeleteCategoryAsync(new("csharp"), snapshot.UpdatedAt, CancellationToken.None));
            Assert.Empty((await SnapshotAsync()).Library.Categories);
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

    private static async Task WithDatabaseAsync(Func<NpgsqlDataSource, Task> test, bool applyStepMigration = true)
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
            foreach (var path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Migrations"), "*.sql").Order(StringComparer.Ordinal))
            {
                if (applyStepMigration || Path.GetFileName(path) == "001_create_snippets.sql")
                {
                    await ExecuteAsync(source, await File.ReadAllTextAsync(path));
                }
            }
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
