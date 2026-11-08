using System.Data;

using Abstractions;

using Models.Snippets;

using Npgsql;

namespace Persistence.Snippets;

/// <summary>Reads and atomically updates PostgreSQL snippets without applying migrations.</summary>
public sealed class PostgreSqlSnippetRepository : IEditableSnippetRepository
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
            WITH examples AS (
                SELECT language, number AS question_number, 1 AS number,
                       snippet_title AS title, markdown FROM snippets.questions
                UNION ALL
                SELECT language, question_number, number, title, markdown FROM snippets.question_steps
            )
            SELECT c.language, c.title, q.number, q.topic, q.description,
                   e.number, e.title, e.markdown
            FROM snippets.categories c
            LEFT JOIN snippets.questions q ON q.language = c.language
            LEFT JOIN examples e ON e.language = q.language AND e.question_number = q.number
            ORDER BY c.sort_order, c.language, q.number, e.number
            """, connection, transaction);
        var rows = new List<PostgreSqlSnippetRow>();
        try
        {
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (await reader.IsDBNullAsync(2, cancellationToken).ConfigureAwait(false))
                    {
                        rows.Add(new(reader.GetString(0), reader.GetString(1), null, "", "", 0, "", ""));
                    }
                    else
                    {
                        rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2),
                            reader.GetString(3), reader.GetString(4), reader.GetInt32(5),
                            reader.GetString(6), reader.GetString(7)));
                    }
                }
            }
            var library = new SnippetLibrary(rows.GroupBy(row => row.Language).Select(category =>
                new SnippetCategory(new(category.Key), category.First().CategoryTitle,
                    category.Where(row => row.QuestionNumber.HasValue).GroupBy(row => row.QuestionNumber!.Value)
                        .Select(question => new SnippetQuestion(question.Key, question.First().Topic,
                            question.First().Description, question.Select(row =>
                                new SnippetStep(row.StepNumber, row.StepTitle, row.Markdown)))))));
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new(version, library);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The PostgreSQL snippet library violates domain invariants.", exception);
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateQuestionAsync(SnippetLanguage language, SnippetQuestion question,
        DateTimeOffset expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language.Value, nameof(language));
        ArgumentNullException.ThrowIfNull(question);
        await using var connection = await _getDataSource().OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        // Lock the version before touching content, so cooperating writers use the same lock order.
        await using var version = new NpgsqlCommand(
            "SELECT updated_at FROM snippets.catalog_state WHERE id = 1 FOR UPDATE", connection, transaction);
        var observed = await version.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (observed is not DateTime timestamp || new DateTimeOffset(timestamp) != expectedVersion)
        {
            return false;
        }
        await using var update = new NpgsqlCommand("""
            UPDATE snippets.questions SET topic = $3, description = $4, snippet_title = $5, markdown = $6
            WHERE language = $1 AND number = $2
            """, connection, transaction);
        _ = update.Parameters.AddWithValue(language.Value);
        _ = update.Parameters.AddWithValue(question.Number);
        _ = update.Parameters.AddWithValue(question.Topic);
        _ = update.Parameters.AddWithValue(question.Description);
        _ = update.Parameters.AddWithValue(question.Steps[0].Title);
        _ = update.Parameters.AddWithValue(question.Markdown);
        if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            return false;
        }
        await using var remove = new NpgsqlCommand(
            "DELETE FROM snippets.question_steps WHERE language = $1 AND question_number = $2", connection, transaction);
        _ = remove.Parameters.AddWithValue(language.Value);
        _ = remove.Parameters.AddWithValue(question.Number);
        _ = await remove.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        foreach (var step in question.Steps.Skip(1))
        {
            await using var insert = new NpgsqlCommand("""
                INSERT INTO snippets.question_steps(language, question_number, number, title, markdown)
                VALUES ($1, $2, $3, $4, $5)
                """, connection, transaction);
            _ = insert.Parameters.AddWithValue(language.Value);
            _ = insert.Parameters.AddWithValue(question.Number);
            _ = insert.Parameters.AddWithValue(step.Number);
            _ = insert.Parameters.AddWithValue(step.Title);
            _ = insert.Parameters.AddWithValue(step.Markdown);
            _ = await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private readonly Func<NpgsqlDataSource> _getDataSource;
}
