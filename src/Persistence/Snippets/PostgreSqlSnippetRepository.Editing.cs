using Models.Snippets;

using Npgsql;

namespace Persistence.Snippets;

public sealed partial class PostgreSqlSnippetRepository
{
    /// <inheritdoc />
    public Task<bool> SaveCategoryAsync(SnippetLanguage? originalLanguage, SnippetCategory category,
        DateTimeOffset expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(category);
        return MutateAsync(expectedVersion, async (connection, transaction) =>
        {
            await using var command = originalLanguage is null ? new NpgsqlCommand("""
                INSERT INTO snippets.categories(language, title, sort_order) VALUES ($1, $2, $3)
                ON CONFLICT DO NOTHING
                """, connection, transaction) : new NpgsqlCommand("""
                UPDATE snippets.categories SET language = $1, title = $2, sort_order = $3
                WHERE language = $4 AND ($1 = $4 OR NOT EXISTS (
                    SELECT 1 FROM snippets.categories WHERE language = $1))
                """, connection, transaction);
            _ = command.Parameters.AddWithValue(category.Language.Value);
            _ = command.Parameters.AddWithValue(category.Title);
            _ = command.Parameters.AddWithValue(category.SortOrder);
            if (originalLanguage is { } original)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(original.Value, nameof(originalLanguage));
                _ = command.Parameters.AddWithValue(original.Value);
            }
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DeleteCategoryAsync(SnippetLanguage language, DateTimeOffset expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language.Value, nameof(language));
        return MutateAsync(expectedVersion, async (connection, transaction) =>
        {
            await using var command = new NpgsqlCommand(
                "DELETE FROM snippets.categories WHERE language = $1", connection, transaction);
            _ = command.Parameters.AddWithValue(language.Value);
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> CreateQuestionAsync(SnippetLanguage language, SnippetQuestion question,
        DateTimeOffset expectedVersion, CancellationToken cancellationToken)
        => SaveQuestionAsync(null, null, language, question, expectedVersion, cancellationToken);

    /// <inheritdoc />
    public Task<bool> RelocateQuestionAsync(SnippetLanguage originalLanguage, int originalNumber,
        SnippetLanguage language, SnippetQuestion question, DateTimeOffset expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalLanguage.Value, nameof(originalLanguage));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(originalNumber);
        return SaveQuestionAsync(originalLanguage, originalNumber, language, question,
            expectedVersion, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DeleteQuestionAsync(SnippetLanguage language, int number,
        DateTimeOffset expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language.Value, nameof(language));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);
        return MutateAsync(expectedVersion, async (connection, transaction) =>
        {
            await using var command = new NpgsqlCommand(
                "DELETE FROM snippets.questions WHERE language = $1 AND number = $2", connection, transaction);
            _ = command.Parameters.AddWithValue(language.Value);
            _ = command.Parameters.AddWithValue(number);
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
        }, cancellationToken);
    }

    private Task<bool> SaveQuestionAsync(SnippetLanguage? originalLanguage, int? originalNumber,
        SnippetLanguage language, SnippetQuestion question, DateTimeOffset expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language.Value, nameof(language));
        ArgumentNullException.ThrowIfNull(question);
        return MutateAsync(expectedVersion, async (connection, transaction) =>
        {
            await using var command = originalNumber is null ? new NpgsqlCommand("""
                INSERT INTO snippets.questions(language, number, topic, description, snippet_title, markdown)
                SELECT $1, $2, $3, $4, $5, $6
                WHERE EXISTS (SELECT 1 FROM snippets.categories WHERE language = $1)
                ON CONFLICT DO NOTHING
                """, connection, transaction) : new NpgsqlCommand("""
                UPDATE snippets.questions SET language = $1, number = $2, topic = $3,
                    description = $4, snippet_title = $5, markdown = $6
                WHERE language = $7 AND number = $8
                    AND EXISTS (SELECT 1 FROM snippets.categories WHERE language = $1)
                    AND (($1 = $7 AND $2 = $8) OR NOT EXISTS (
                        SELECT 1 FROM snippets.questions WHERE language = $1 AND number = $2))
                """, connection, transaction);
            _ = command.Parameters.AddWithValue(language.Value);
            _ = command.Parameters.AddWithValue(question.Number);
            _ = command.Parameters.AddWithValue(question.Topic);
            _ = command.Parameters.AddWithValue(question.Description);
            _ = command.Parameters.AddWithValue(question.Steps[0].Title);
            _ = command.Parameters.AddWithValue(question.Markdown);
            if (originalNumber is not null)
            {
                _ = command.Parameters.AddWithValue(originalLanguage!.Value.Value);
                _ = command.Parameters.AddWithValue(originalNumber.Value);
            }
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
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
            return true;
        }, cancellationToken);
    }

    private async Task<bool> MutateAsync(DateTimeOffset expectedVersion,
        Func<NpgsqlConnection, NpgsqlTransaction, Task<bool>> write, CancellationToken cancellationToken)
    {
        await using var connection = await _getDataSource().OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        // Every writer locks the catalog revision before touching content.
        await using var version = new NpgsqlCommand(
            "SELECT updated_at FROM snippets.catalog_state WHERE id = 1 FOR UPDATE", connection, transaction);
        var observed = await version.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (observed is not DateTime timestamp || new DateTimeOffset(timestamp) != expectedVersion
            || !await write(connection, transaction).ConfigureAwait(false))
        {
            return false;
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
