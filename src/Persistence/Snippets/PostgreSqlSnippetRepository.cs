using System.Data;

using Abstractions;

using Models.Snippets;

using Npgsql;

namespace Persistence.Snippets;

/// <summary>Reads PostgreSQL snapshots without writing data or applying migrations.</summary>
public sealed class PostgreSqlSnippetRepository : IVersionedSnippetRepository
{
    /// <summary>Creates a repository backed by the host-owned connection pool.</summary>
    /// <param name="dataSource">The shared PostgreSQL data source.</param>
    /// <exception cref="ArgumentNullException">Thrown when the data source is null.</exception>
    public PostgreSqlSnippetRepository(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _getDataSource = () => dataSource;
    }

    /// <summary>Defers connection-pool configuration until a catalog read can handle failures.</summary>
    /// <param name="getDataSource">Resolves the host-owned PostgreSQL connection pool.</param>
    /// <exception cref="ArgumentNullException">Thrown when the resolver is null.</exception>
    public PostgreSqlSnippetRepository(Func<NpgsqlDataSource> getDataSource)
    {
        ArgumentNullException.ThrowIfNull(getDataSource);
        _getDataSource = getDataSource;
    }

    /// <inheritdoc />
    public async Task<SnippetLibrary> LoadAsync(CancellationToken cancellationToken)
        => (await LoadIfChangedAsync(null, cancellationToken).ConfigureAwait(false))!.Library;

    /// <inheritdoc />
    /// <exception cref="InvalidDataException">Thrown when persisted data is invalid.</exception>
    public async Task<VersionedSnippetLibrary?> LoadIfChangedAsync(
        DateTimeOffset? updatedAt, CancellationToken cancellationToken)
    {
        await using var connection = await _getDataSource().OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken).ConfigureAwait(false);
        await using var versionCommand = new NpgsqlCommand(
            "SELECT updated_at FROM snippets.catalog_state WHERE id = 1", connection, transaction);
        var value = await versionCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (value is not DateTime timestamp)
        {
            throw new InvalidDataException("Snippet catalog timestamp is missing. Apply the database migration.");
        }
        var version = new DateTimeOffset(timestamp);
        if (version == updatedAt)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        await using var command = new NpgsqlCommand("""
            SELECT c.language, c.title, q.number, q.topic, q.description, q.markdown
            FROM snippets.categories c
            LEFT JOIN snippets.questions q ON q.language = c.language
            ORDER BY c.sort_order, c.language, q.number
            """, connection, transaction);
        var categories = new List<SnippetCategory>();
        try
        {
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                string? language = null;
                string? title = null;
                var questions = new List<SnippetQuestion>();
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var nextLanguage = reader.GetString(0);
                    if (language != nextLanguage)
                    {
                        if (language is not null)
                        {
                            categories.Add(new(new(language), title!, questions));
                        }
                        language = nextLanguage;
                        title = reader.GetString(1);
                        questions = [];
                    }
                    if (!await reader.IsDBNullAsync(2, cancellationToken).ConfigureAwait(false))
                    {
                        questions.Add(new(reader.GetInt32(2), reader.GetString(3), reader.GetString(4), reader.GetString(5)));
                    }
                }
                if (language is not null)
                {
                    categories.Add(new(new(language), title!, questions));
                }
            }
            var library = new SnippetLibrary(categories);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new(version, library);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The PostgreSQL snippet library violates domain invariants.", exception);
        }
    }

    private readonly Func<NpgsqlDataSource> _getDataSource;
}
