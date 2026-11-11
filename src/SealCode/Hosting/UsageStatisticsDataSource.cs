using Npgsql;

namespace SealCode.Hosting;

/// <summary>Owns the statistics connection pool independently of the snippet provider.</summary>
internal sealed class UsageStatisticsDataSource : IDisposable
{
    public UsageStatisticsDataSource(IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("Statistics") ?? configuration.GetConnectionString("Snippets");
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException("Set ConnectionStrings:Statistics (or ConnectionStrings:Snippets) and apply the usage statistics migration.");
        }
        Value = NpgsqlDataSource.Create(connection);
    }

    public NpgsqlDataSource Value { get; }

    public void Dispose() => Value.Dispose();
}
