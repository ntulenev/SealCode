using Npgsql;

namespace SealCode.Hosting;

/// <summary>Owns the statistics connection pool independently of the snippet provider.</summary>
internal sealed class UsageStatisticsDataSource : IDisposable
{
    /// <summary>Creates a connection pool using only the dedicated statistics setting.</summary>
    /// <param name="configuration">The configuration containing ConnectionStrings:Statistics.</param>
    /// <exception cref="ArgumentNullException">The configuration is null.</exception>
    /// <exception cref="InvalidOperationException">The dedicated connection string is missing.</exception>
    public UsageStatisticsDataSource(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var connection = configuration.GetConnectionString("Statistics");
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException("Set ConnectionStrings:Statistics and apply the usage statistics migration.");
        }
        Value = NpgsqlDataSource.Create(connection);
    }

    /// <summary>Gets the host-owned statistics connection pool.</summary>
    public NpgsqlDataSource Value { get; }

    /// <inheritdoc />
    public void Dispose() => Value.Dispose();
}
