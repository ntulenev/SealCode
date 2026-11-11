using System.Data;
using System.Text.Json;

using Abstractions;

using Models;

using Npgsql;

namespace Persistence.Statistics;

/// <summary>Stores usage events and computes bounded reports in PostgreSQL.</summary>
public sealed class PostgreSqlUsageStatisticsRepository : IUsageStatisticsRepository
{
    /// <summary>Uses a deferred host-owned connection pool.</summary>
    /// <param name="getDataSource">Resolves the host-owned statistics connection pool.</param>
    /// <exception cref="ArgumentNullException">The resolver is null.</exception>
    public PostgreSqlUsageStatisticsRepository(Func<NpgsqlDataSource> getDataSource)
    {
        ArgumentNullException.ThrowIfNull(getDataSource);
        _getDataSource = getDataSource;
    }

    /// <inheritdoc />
    public Task RecordLoginAsync(AdminUser user, string provider, CancellationToken cancellationToken)
        => RecordAsync(user, provider, null, cancellationToken);

    /// <inheritdoc />
    public Task RecordRoomAsync(RoomState room, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(room);
        return RecordAsync(room.CreatedBy, null, room, cancellationToken);
    }

    private async Task RecordAsync(AdminUser user, string? provider, RoomState? room, CancellationToken cancellationToken)
    {
        await using var connection = await _getDataSource().OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var account = new NpgsqlCommand("""
            INSERT INTO statistics.admin_accounts(id, display_name) VALUES ($1, $2)
            ON CONFLICT (id) DO UPDATE SET display_name = EXCLUDED.display_name
            """, connection, transaction);
        _ = account.Parameters.AddWithValue(Key(user.Name));
        _ = account.Parameters.AddWithValue(user.DisplayName);
        _ = await account.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand("""
            INSERT INTO statistics.events(user_id, event_type, provider, room_id, room_name, room_kind, language)
            VALUES ($1, $2, $3, $4, $5, $6, $7)
            """, connection, transaction);
        _ = command.Parameters.AddWithValue(Key(user.Name));
        _ = command.Parameters.AddWithValue(room is null ? "login" : "room");
        foreach (var value in new[] { provider, room?.RoomId.Value.ToString(), room?.Name.Value,
            room is null ? null : room.Kind == RoomKind.Whiteboard ? "whiteboard" : "code", room?.Language.Value })
        {
            _ = command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = (object?)value ?? DBNull.Value });
        }
        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<JsonElement> ReadAsync(string? userId, int offset, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        await using var connection = await _getDataSource().OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand("""
            WITH filtered AS MATERIALIZED (
                SELECT * FROM statistics.events WHERE ($1::text IS NULL OR user_id = $1)
            ), users AS (
                SELECT a.id AS "Id", a.display_name AS "DisplayName",
                    count(*) FILTER (WHERE e.event_type = 'login') AS "Logins",
                    count(*) FILTER (WHERE e.event_type = 'room') AS "Rooms",
                    min(e.occurred_at) AS "FirstActivityUtc", max(e.occurred_at) AS "LastActivityUtc"
                FROM statistics.admin_accounts a JOIN filtered e ON e.user_id = a.id
                GROUP BY a.id, a.display_name ORDER BY max(e.occurred_at) DESC, a.id
                LIMIT 50 OFFSET $2
            ), breakdown AS (
                SELECT room_kind AS "Kind", language AS "Language", count(*) AS "Count"
                FROM filtered WHERE event_type = 'room' GROUP BY room_kind, language
                ORDER BY count(*) DESC, room_kind, language
            ), events AS (
                SELECT id AS "Id", occurred_at AS "OccurredAtUtc", event_type AS "Type",
                    provider AS "Provider", room_id AS "RoomId", room_name AS "RoomName",
                    room_kind AS "Kind", language AS "Language"
                FROM filtered WHERE $1::text IS NOT NULL ORDER BY occurred_at DESC, id DESC
                LIMIT 50 OFFSET $2
            )
            SELECT json_build_object(
                'Logins', count(*) FILTER (WHERE event_type = 'login'),
                'Rooms', count(*) FILTER (WHERE event_type = 'room'),
                'UsersCount', count(DISTINCT user_id), 'EventsCount', count(*),
                'FirstActivityUtc', min(occurred_at), 'LastActivityUtc', max(occurred_at),
                'Breakdown', (SELECT coalesce(json_agg(b), '[]') FROM breakdown b),
                'Users', (SELECT coalesce(json_agg(u), '[]') FROM users u),
                'Events', (SELECT coalesce(json_agg(e), '[]') FROM events e))::text
            FROM filtered
            """, connection);
        _ = command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = (object?)userId ?? DBNull.Value });
        _ = command.Parameters.AddWithValue(offset);
        var json = (string)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    /// <inheritdoc />
    public async Task<int> DeleteBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken)
    {
        await using var connection = await _getDataSource().OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        // Serialize pruning with account upserts so a concurrent event cannot lose its parent account.
        await using var accountLock = new NpgsqlCommand("LOCK TABLE statistics.admin_accounts IN SHARE ROW EXCLUSIVE MODE", connection, transaction);
        _ = await accountLock.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand("DELETE FROM statistics.events WHERE occurred_at < $1", connection, transaction);
        _ = command.Parameters.AddWithValue(before.ToUniversalTime());
        var deleted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await using var cleanup = new NpgsqlCommand("""
            DELETE FROM statistics.admin_accounts a
            WHERE NOT EXISTS (SELECT 1 FROM statistics.events e WHERE e.user_id = a.id)
            """, connection, transaction);
        _ = await cleanup.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return deleted;
    }

    private static string Key(string name)
        => name.StartsWith("google:", StringComparison.Ordinal) ? name : name.ToUpperInvariant();

    private readonly Func<NpgsqlDataSource> _getDataSource;
}
