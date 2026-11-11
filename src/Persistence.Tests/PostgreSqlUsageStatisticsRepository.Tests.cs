using Models;

using Npgsql;

using Persistence.Statistics;

namespace Persistence.Tests;

public sealed class PostgreSqlUsageStatisticsRepositoryTests
{
    [StatisticsDatabaseFact]
    public async Task AccountsEventsReportsPagingAndStrictRetentionWorkAsync()
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SEALCODE_SNIPPETS_ISOLATED_TEST_CONNECTION"));
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
            var empty = await repository.ReadAsync(null, 0, CancellationToken.None);
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
            var report = await repository.ReadAsync(null, 0, CancellationToken.None);
            Assert.Equal(14, report.GetProperty("Logins").GetInt32());
            Assert.Equal(2, report.GetProperty("Rooms").GetInt32());
            Assert.Equal(3, report.GetProperty("UsersCount").GetInt32());
            Assert.Equal(2, report.GetProperty("Breakdown").GetArrayLength());
            var detail = await repository.ReadAsync("ADMIN", 0, CancellationToken.None);
            Assert.Equal(14, detail.GetProperty("EventsCount").GetInt32());
            Assert.Equal(12, detail.GetProperty("Logins").GetInt32());
            Assert.Empty((await repository.ReadAsync("ADMIN", 50, CancellationToken.None)).GetProperty("Events").EnumerateArray());
            Assert.Empty((await repository.ReadAsync(null, 50, CancellationToken.None)).GetProperty("Users").EnumerateArray());
            Assert.Equal(0, (await repository.ReadAsync("missing", 0, CancellationToken.None)).GetProperty("EventsCount").GetInt32());
            await ExecuteAsync(source, "UPDATE statistics.events SET occurred_at = '2020-01-01T00:00:00Z'; UPDATE statistics.events SET occurred_at = '2020-01-02T00:00:00Z' WHERE event_type = 'room';");
            var cutoff = new DateTimeOffset(2020, 1, 2, 0, 0, 0, TimeSpan.Zero);
            Assert.Equal(14, await repository.DeleteBeforeAsync(cutoff, CancellationToken.None));
            var retained = await repository.ReadAsync(null, 0, CancellationToken.None);
            Assert.Equal(0, retained.GetProperty("Logins").GetInt32());
            Assert.Equal(2, retained.GetProperty("Rooms").GetInt32());
            Assert.Equal(1, retained.GetProperty("UsersCount").GetInt32());
            Assert.Equal(2, await repository.DeleteBeforeAsync(cutoff.AddDays(1), CancellationToken.None));
            Assert.Equal(0, (await repository.ReadAsync(null, 0, CancellationToken.None)).GetProperty("EventsCount").GetInt32());
            await using var accounts = source.CreateCommand("SELECT count(*) FROM statistics.admin_accounts");
            Assert.Equal(0L, await accounts.ExecuteScalarAsync());
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
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SEALCODE_SNIPPETS_ISOLATED_TEST_CONNECTION")))
            {
                Skip = "Set SEALCODE_SNIPPETS_ISOLATED_TEST_CONNECTION to a disposable PostgreSQL test server.";
            }
        }
    }
}
