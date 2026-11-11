using System.Data;
using System.Text.Json;

using Abstractions;

using Models;

using Npgsql;

namespace Persistence.Statistics;

/// <summary>Stores usage events and computes bounded reports in PostgreSQL.</summary>
public sealed class PostgreSqlUsageStatisticsRepository : IUsageStatisticsRepository
{
    /// <inheritdoc />
    public bool Enabled => true;

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
    public async Task<JsonElement> ReadAsync(UsageStatisticsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        var userSort = query.SortBy switch
        {
            "user" => "u.\"DisplayName\"",
            "logins" => "u.\"Logins\"",
            "rooms" => "u.\"Rooms\"",
            "firstActivity" => "u.\"FirstActivityUtc\"",
            _ => "u.\"LastActivityUtc\""
        };
        var eventSort = query.SortBy switch
        {
            "type" => "event_type",
            "provider" => "provider",
            "room" => "room_name",
            "kind" => "room_kind",
            "language" => "language",
            _ => "occurred_at"
        };
        var breakdownSort = query.BreakdownSortBy switch
        {
            "kind" => "b.\"Kind\"",
            "language" => "b.\"Language\"",
            _ => "b.\"Count\""
        };
        var direction = query.Descending ? "DESC" : "ASC";
        var breakdownDirection = query.BreakdownDescending ? "DESC" : "ASC";
        await using var connection = await _getDataSource().OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        // Only fixed SQL identifiers from the switches above are interpolated. Every filter is a parameter.
#pragma warning disable CA2100
        await using var command = new NpgsqlCommand($"""
            WITH filtered AS MATERIALIZED (
                SELECT e.*, a.display_name FROM statistics.events e
                JOIN statistics.admin_accounts a ON a.id = e.user_id
                WHERE e.occurred_at >= $3 AND e.occurred_at < $4
                  AND ($1::text IS NULL OR e.user_id = $1)
                  AND ($5 = '' OR strpos(lower(a.display_name || ' ' || a.id || ' ' || coalesce(e.room_name, '')), lower($5)) > 0)
                  AND ($6::text IS NULL OR e.event_type = $6)
                  AND ($7::text IS NULL OR e.room_kind = $7)
                  AND ($8::text IS NULL OR e.language = $8)
                  AND ($9::text IS NULL OR e.provider = $9)
            ), user_totals AS (
                SELECT user_id AS "Id", display_name AS "DisplayName",
                    count(*) FILTER (WHERE event_type = 'login') AS "Logins",
                    count(*) FILTER (WHERE event_type = 'room') AS "Rooms",
                    min(occurred_at) AS "FirstActivityUtc", max(occurred_at) AS "LastActivityUtc"
                FROM filtered GROUP BY user_id, display_name
            ), users AS (
                SELECT * FROM user_totals u WHERE $1::text IS NULL
                ORDER BY {userSort} {direction} NULLS LAST, u."Id" ASC
                LIMIT $10 OFFSET $2
            ), breakdown_totals AS (
                SELECT room_kind AS "Kind", language AS "Language", count(*) AS "Count"
                FROM filtered WHERE event_type = 'room' GROUP BY room_kind, language
            ), breakdown AS (
                SELECT * FROM breakdown_totals b
                ORDER BY {breakdownSort} {breakdownDirection} NULLS LAST, b."Kind", b."Language" LIMIT 100
            ), events AS (
                SELECT id AS "Id", occurred_at AS "OccurredAtUtc", event_type AS "Type",
                    provider AS "Provider", room_id AS "RoomId", room_name AS "RoomName",
                    room_kind AS "Kind", language AS "Language"
                FROM filtered WHERE $1::text IS NOT NULL
                ORDER BY {eventSort} {direction} NULLS LAST, id {direction}
                LIMIT $10 OFFSET $2
            )
            SELECT json_build_object(
                'FromUtc', $3::timestamptz, 'ToUtc', $4::timestamptz, 'Offset', $2::int, 'Limit', $10::int,
                'Logins', count(*) FILTER (WHERE event_type = 'login'),
                'Rooms', count(*) FILTER (WHERE event_type = 'room'),
                'UsersCount', count(DISTINCT user_id), 'EventsCount', count(*),
                'FirstActivityUtc', min(occurred_at), 'LastActivityUtc', max(occurred_at),
                'BreakdownCount', (SELECT count(*) FROM breakdown_totals),
                'Breakdown', (SELECT coalesce(json_agg(b), '[]') FROM breakdown b),
                'Users', (SELECT coalesce(json_agg(u), '[]') FROM users u),
                'Events', (SELECT coalesce(json_agg(e), '[]') FROM events e))::text
            FROM filtered
            """, connection) { CommandTimeout = 15 };
#pragma warning restore CA2100
        AddText(command, query.UserId);
        _ = command.Parameters.AddWithValue(query.Offset);
        _ = command.Parameters.AddWithValue(query.From.ToUniversalTime());
        _ = command.Parameters.AddWithValue(query.To.ToUniversalTime());
        AddText(command, query.Search?.Trim() ?? "");
        AddText(command, query.EventType);
        AddText(command, query.Kind);
        AddText(command, query.Language);
        AddText(command, query.Provider);
        _ = command.Parameters.AddWithValue(query.Limit);
        var json = (string)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static void AddText(NpgsqlCommand command, string? value)
        => _ = command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = (object?)value ?? DBNull.Value });

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
