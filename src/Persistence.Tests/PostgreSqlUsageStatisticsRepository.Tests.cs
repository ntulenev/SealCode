using Models;

using Npgsql;

using Persistence.Statistics;

namespace Persistence.Tests;

public sealed class PostgreSqlUsageStatisticsRepositoryTests
{
    [StatisticsDatabaseFact]
    public async Task BoundedReportsApplyFiltersSortingAndPaginationBeforeReturningRowsAsync()
    {
        await WithDatabaseAsync(async source =>
        {
            var repository = new PostgreSqlUsageStatisticsRepository(() => source);
            await repository.RecordLoginAsync(new AdminUser("Alpha"), "password", CancellationToken.None);
            await repository.RecordLoginAsync(new AdminUser("Alpha"), "google", CancellationToken.None);
            await repository.RecordLoginAsync(new AdminUser("Beta"), "mcp", CancellationToken.None);
            await repository.RecordLoginAsync(new AdminUser("Outside"), "password", CancellationToken.None);
            foreach (var (owner, name, language, kind) in new[]
            {
                ("Alpha", "C# interview", "csharp", RoomKind.Code),
                ("Alpha", "Design canvas", "plaintext", RoomKind.Whiteboard),
                ("Beta", "SQL interview", "sql", RoomKind.Code)
            })
            {
                await repository.RecordRoomAsync(new RoomState(RoomId.New(), new RoomName(name), new RoomLanguage(language),
                    new RoomText(""), new RoomVersion(1), DateTimeOffset.UtcNow, new AdminUser(owner), kind: kind), CancellationToken.None);
            }
            await ExecuteAsync(source, """
                UPDATE statistics.events SET occurred_at = '2020-01-02T00:00:00Z';
                UPDATE statistics.events SET occurred_at = '2020-01-01T23:59:59Z' WHERE provider = 'password' AND user_id = 'ALPHA';
                UPDATE statistics.events SET occurred_at = '2020-01-04T00:00:00Z' WHERE user_id = 'OUTSIDE';
                """);
            var query = new UsageStatisticsQuery(new DateTimeOffset(2020, 1, 2, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2020, 1, 4, 0, 0, 0, TimeSpan.Zero))
            { SortBy = "rooms", Limit = 1 };
            var report = await repository.ReadAsync(query, CancellationToken.None);
            Assert.Equal(2, report.GetProperty("Logins").GetInt32());
            Assert.Equal(3, report.GetProperty("Rooms").GetInt32());
            Assert.Equal(2, report.GetProperty("UsersCount").GetInt32());
            Assert.Equal("ALPHA", Assert.Single(report.GetProperty("Users").EnumerateArray()).GetProperty("Id").GetString());
            var second = await repository.ReadAsync(query with { Offset = 1 }, CancellationToken.None);
            Assert.Equal("BETA", Assert.Single(second.GetProperty("Users").EnumerateArray()).GetProperty("Id").GetString());
            var ascending = await repository.ReadAsync(query with { Descending = false }, CancellationToken.None);
            Assert.Equal("BETA", ascending.GetProperty("Users")[0].GetProperty("Id").GetString());
            var search = await repository.ReadAsync(query with { Search = "sQl" }, CancellationToken.None);
            Assert.Equal(1, search.GetProperty("Rooms").GetInt32());
            Assert.Equal(0, search.GetProperty("Logins").GetInt32());
            foreach (var filtered in new[] { query with { Kind = "whiteboard" }, query with { Language = "csharp" } })
            {
                Assert.Equal(1, (await repository.ReadAsync(filtered, CancellationToken.None)).GetProperty("Rooms").GetInt32());
            }
            var authentication = await repository.ReadAsync(query with { Provider = "mcp" }, CancellationToken.None);
            Assert.Equal(1, authentication.GetProperty("Logins").GetInt32());
            Assert.Equal(0, authentication.GetProperty("Rooms").GetInt32());
            var events = await repository.ReadAsync(query with { UserId = "ALPHA", EventType = "room", SortBy = "room", Descending = false, Limit = 100 }, CancellationToken.None);
            Assert.Equal(["C# interview", "Design canvas"], events.GetProperty("Events").EnumerateArray().Select(value => value.GetProperty("RoomName").GetString()));
            var kinds = await repository.ReadAsync(query with { BreakdownSortBy = "kind", BreakdownDescending = true }, CancellationToken.None);
            Assert.Equal("whiteboard", kinds.GetProperty("Breakdown")[0].GetProperty("Kind").GetString());
        });
    }

    [Fact]
    public async Task InvalidQueryIsRejectedBeforeAConnectionIsOpenedAsync()
    {
        var repository = new PostgreSqlUsageStatisticsRepository(() => throw new InvalidOperationException("Must not connect"));
        _ = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.ReadAsync(new UsageStatisticsQuery { Limit = 101 }, CancellationToken.None));
    }

    [StatisticsDatabaseFact]
    public async Task AccountsEventsReportsPagingAndStrictRetentionWorkAsync()
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SEALCODE_STATISTICS_ISOLATED_TEST_CONNECTION"));
        await using var admin = NpgsqlDataSource.Create(settings.ConnectionString);
        var database = "sealcode_statistics_test_" + Guid.NewGuid().ToString("N");
        await ExecuteAsync(admin, $"CREATE DATABASE {database}");
        try
        {
            settings.Database = database;
            await using var source = NpgsqlDataSource.Create(settings.ConnectionString);
            var migration = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "StatisticsMigrations", "001_usage_statistics.sql"));
            await ExecuteAsync(source, migration);
            await ExecuteAsync(source, migration);
            var repository = new PostgreSqlUsageStatisticsRepository(() => source);
            var empty = await repository.ReadAsync(new UsageStatisticsQuery(userId: null, offset: 0), CancellationToken.None);
            Assert.Equal(0, empty.GetProperty("Logins").GetInt32());
            await Task.WhenAll(Enumerable.Range(0, 12).Select(index => repository.RecordLoginAsync(
                new AdminUser(index % 2 == 0 ? "Admin" : "admin"), "password", CancellationToken.None)));
            await repository.RecordLoginAsync(new AdminUser("google:Case", displayName: "user@example.org"), "google", CancellationToken.None);
            await repository.RecordLoginAsync(new AdminUser("google:case", displayName: "other@example.org"), "google", CancellationToken.None);
            foreach (var kind in new[] { RoomKind.Code, RoomKind.Whiteboard })
            {
                var room = new RoomState(RoomId.New(), new RoomName("<script>room</script>"), new RoomLanguage("csharp"),
                    new RoomText(""), new RoomVersion(1), DateTimeOffset.UtcNow, new AdminUser("ADMIN"), kind: kind);
                await repository.RecordRoomAsync(room, CancellationToken.None);
            }
            var report = await repository.ReadAsync(new UsageStatisticsQuery(userId: null, offset: 0), CancellationToken.None);
            Assert.Equal(14, report.GetProperty("Logins").GetInt32());
            Assert.Equal(2, report.GetProperty("Rooms").GetInt32());
            Assert.Equal(3, report.GetProperty("UsersCount").GetInt32());
            Assert.Equal(2, report.GetProperty("Breakdown").GetArrayLength());
            var detail = await repository.ReadAsync(new UsageStatisticsQuery(userId: "ADMIN", offset: 0), CancellationToken.None);
            Assert.Equal(14, detail.GetProperty("EventsCount").GetInt32());
            Assert.Equal(12, detail.GetProperty("Logins").GetInt32());
            Assert.Empty((await repository.ReadAsync(new UsageStatisticsQuery(userId: "ADMIN", offset: 50), CancellationToken.None)).GetProperty("Events").EnumerateArray());
            Assert.Empty((await repository.ReadAsync(new UsageStatisticsQuery(userId: null, offset: 50), CancellationToken.None)).GetProperty("Users").EnumerateArray());
            Assert.Equal(0, (await repository.ReadAsync(new UsageStatisticsQuery(userId: "missing", offset: 0), CancellationToken.None)).GetProperty("EventsCount").GetInt32());
            await ExecuteAsync(source, "UPDATE statistics.events SET occurred_at = '2020-01-01T00:00:00Z'; UPDATE statistics.events SET occurred_at = '2020-01-02T00:00:00Z' WHERE event_type = 'room';");
            var cutoff = new DateTimeOffset(2020, 1, 2, 0, 0, 0, TimeSpan.Zero);
            Assert.Equal(14, await repository.DeleteBeforeAsync(cutoff, CancellationToken.None));
            var retained = await repository.ReadAsync(new UsageStatisticsQuery(cutoff.AddDays(-2), cutoff.AddDays(1)), CancellationToken.None);
            Assert.Equal(0, retained.GetProperty("Logins").GetInt32());
            Assert.Equal(2, retained.GetProperty("Rooms").GetInt32());
            Assert.Equal(1, retained.GetProperty("UsersCount").GetInt32());
            Assert.Equal(2, await repository.DeleteBeforeAsync(cutoff.AddDays(1), CancellationToken.None));
            Assert.Equal(0, (await repository.ReadAsync(new UsageStatisticsQuery(userId: null, offset: 0), CancellationToken.None)).GetProperty("EventsCount").GetInt32());
            await using var accounts = source.CreateCommand("SELECT count(*) FROM statistics.admin_accounts");
            Assert.Equal(0L, await accounts.ExecuteScalarAsync());
        }
        finally
        {
            await ExecuteAsync(admin, $"DROP DATABASE {database} WITH (FORCE)");
        }
    }

    [StatisticsDatabaseFact]
    public async Task MetadataPagingRollbackCancellationAndRecreatedAccountsWorkAsync()
    {
        await WithDatabaseAsync(async source =>
        {
            var repository = new PostgreSqlUsageStatisticsRepository(() => source);
            var user = new AdminUser("History");
            for (var index = 0; index < 55; index++)
            {
                await repository.RecordLoginAsync(user, "password", CancellationToken.None);
            }
            var room = new RoomState(RoomId.New(), new RoomName("<script>room</script>"), new RoomLanguage("csharp"),
                new RoomText("Private code must not be stored"), new RoomVersion(1), DateTimeOffset.UtcNow, user);
            await repository.RecordRoomAsync(room, CancellationToken.None);
            await ExecuteAsync(source, "UPDATE statistics.events SET occurred_at = '2020-01-01T00:00:00Z'");
            // A fresh repository reads durable history; timestamp ties use descending event identifiers.
            var reopened = new PostgreSqlUsageStatisticsRepository(() => source);
            var first = (await reopened.ReadAsync(new UsageStatisticsQuery(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2020, 1, 2, 0, 0, 0, TimeSpan.Zero), "HISTORY"), CancellationToken.None)).GetProperty("Events");
            var second = (await reopened.ReadAsync(new UsageStatisticsQuery(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2020, 1, 2, 0, 0, 0, TimeSpan.Zero), "HISTORY", 50), CancellationToken.None)).GetProperty("Events");
            Assert.Equal(50, first.GetArrayLength());
            Assert.Equal(6, second.GetArrayLength());
            Assert.Equal(56, first[0].GetProperty("Id").GetInt32());
            Assert.Equal(6, second[0].GetProperty("Id").GetInt32());
            Assert.Equal(room.RoomId.Value, first[0].GetProperty("RoomId").GetString());
            Assert.Equal("<script>room</script>", first[0].GetProperty("RoomName").GetString());
            Assert.Equal("csharp", first[0].GetProperty("Language").GetString());
            Assert.DoesNotContain("Private code", first.GetRawText(), StringComparison.Ordinal);
            Assert.Equal(0, (await reopened.ReadAsync(new UsageStatisticsQuery(userId: "' OR true --", offset: 0), CancellationToken.None)).GetProperty("EventsCount").GetInt32());

            await ExecuteAsync(source, "ALTER TABLE statistics.events ADD CONSTRAINT reject_test_provider CHECK (provider IS DISTINCT FROM 'reject')");
            _ = await Assert.ThrowsAsync<PostgresException>(() => repository.RecordLoginAsync(new AdminUser("Rejected"), "reject", CancellationToken.None));
            await using var orphan = source.CreateCommand("SELECT count(*) FROM statistics.admin_accounts WHERE id = 'REJECTED'");
            Assert.Equal(0L, await orphan.ExecuteScalarAsync());
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.RecordLoginAsync(new AdminUser("Canceled"), "password", cancellation.Token));
            Assert.Equal(56, await repository.DeleteBeforeAsync(DateTimeOffset.UtcNow, CancellationToken.None));
            await repository.RecordLoginAsync(user, "password", CancellationToken.None);
            var recreated = await repository.ReadAsync(new UsageStatisticsQuery(userId: "HISTORY", offset: 0), CancellationToken.None);
            Assert.Equal(1, recreated.GetProperty("Logins").GetInt32());
            Assert.Equal(1, recreated.GetProperty("UsersCount").GetInt32());
            _ = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.ReadAsync(new UsageStatisticsQuery(userId: null, offset: -1), CancellationToken.None));
        });
    }

    [StatisticsDatabaseFact]
    public async Task UserPagesHaveNoDuplicatesAndContainTheRemainingAccountsAsync()
    {
        await WithDatabaseAsync(async source =>
        {
            var repository = new PostgreSqlUsageStatisticsRepository(() => source);
            await Task.WhenAll(Enumerable.Range(0, 55).Select(index => repository.RecordLoginAsync(
                new AdminUser($"User-{index:D2}"), "password", CancellationToken.None)));
            await ExecuteAsync(source, "UPDATE statistics.events SET occurred_at = '2020-01-01T00:00:00Z'");
            var first = await repository.ReadAsync(new UsageStatisticsQuery(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2020, 1, 2, 0, 0, 0, TimeSpan.Zero)), CancellationToken.None);
            var second = await repository.ReadAsync(new UsageStatisticsQuery(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2020, 1, 2, 0, 0, 0, TimeSpan.Zero), offset: 50), CancellationToken.None);
            Assert.Equal(55, first.GetProperty("UsersCount").GetInt32());
            Assert.Equal(50, first.GetProperty("Users").GetArrayLength());
            Assert.Equal(5, second.GetProperty("Users").GetArrayLength());
            var identifiers = first.GetProperty("Users").EnumerateArray().Concat(second.GetProperty("Users").EnumerateArray())
                .Select(user => user.GetProperty("Id").GetString()).ToArray();
            Assert.Equal(55, identifiers.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal("USER-00", identifiers[0]);
            Assert.Equal("USER-54", identifiers[^1]);
        });
    }

    [StatisticsDatabaseFact]
    public async Task ConcurrentPruningAndAccountUpsertsPreserveNewEventsAsync()
    {
        await WithDatabaseAsync(async source =>
        {
            var repository = new PostgreSqlUsageStatisticsRepository(() => source);
            var writes = Enumerable.Range(0, 20).Select(_ => repository.RecordLoginAsync(new AdminUser("Concurrent"), "mcp", CancellationToken.None));
            await Task.WhenAll(writes.Append(repository.DeleteBeforeAsync(DateTimeOffset.UnixEpoch, CancellationToken.None)));
            var report = await repository.ReadAsync(new UsageStatisticsQuery(userId: "CONCURRENT", offset: 0), CancellationToken.None);
            Assert.Equal(20, report.GetProperty("Logins").GetInt32());
            Assert.Equal(1, report.GetProperty("UsersCount").GetInt32());
        });
    }

    [Fact]
    public void RepositoryRequiresAConnectionPoolResolver()
        => Assert.Throws<ArgumentNullException>(() => new PostgreSqlUsageStatisticsRepository(null!));

    private static async Task WithDatabaseAsync(Func<NpgsqlDataSource, Task> test)
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SEALCODE_STATISTICS_ISOLATED_TEST_CONNECTION"));
        await using var admin = NpgsqlDataSource.Create(settings.ConnectionString);
        var database = "sealcode_statistics_test_" + Guid.NewGuid().ToString("N");
        await ExecuteAsync(admin, $"CREATE DATABASE {database}");
        try
        {
            settings.Database = database;
            await using var source = NpgsqlDataSource.Create(settings.ConnectionString);
            await ExecuteAsync(source, await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "StatisticsMigrations", "001_usage_statistics.sql")));
            await test(source);
        }
        finally
        {
            await ExecuteAsync(admin, $"DROP DATABASE {database} WITH (FORCE)");
        }
    }

    private static async Task ExecuteAsync(NpgsqlDataSource source, string sql)
    {
        await using var command = source.CreateCommand(sql);
        _ = await command.ExecuteNonQueryAsync();
    }

    private sealed class StatisticsDatabaseFactAttribute : FactAttribute
    {
        public StatisticsDatabaseFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SEALCODE_STATISTICS_ISOLATED_TEST_CONNECTION")))
            {
                Skip = "Set SEALCODE_STATISTICS_ISOLATED_TEST_CONNECTION to a disposable PostgreSQL test server.";
            }
        }
    }
}
