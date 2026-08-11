using ExitPass.CentralPms.Application.VendorSessions;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.VendorSessions;

/// <summary>
/// PostgreSQL read repository for operator-facing vendor session projection health.
/// </summary>
public sealed class PostgresVendorSessionProjectionHealthReadRepository
    : IVendorSessionProjectionHealthReadRepository
{
    private readonly string _connectionString;

    /// <summary>
    /// Creates a PostgreSQL projection health read repository.
    /// </summary>
    public PostgresVendorSessionProjectionHealthReadRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VendorSessionProjectionHealthTargetReadModel>> ListTargetsAsync(
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH projection_rollup AS (
                SELECT
                    site_id,
                    vendor_system_id,
                    parking_lot_index_code,
                    max(last_refreshed_at) AS latest_projection_last_refreshed_at,
                    count(*) AS total_projection_count,
                    count(*) FILTER (WHERE projection_status = 'ACTIVE') AS active_projection_count,
                    count(*) FILTER (WHERE projection_status = 'EXITED') AS exited_projection_count,
                    count(*) FILTER (WHERE card_num IS NOT NULL) AS card_num_projection_count,
                    count(*) FILTER (WHERE plate_license IS NOT NULL) AS plate_license_projection_count
                FROM sessions.vendor_session_projections
                WHERE projection_status <> 'INVALIDATED'
                GROUP BY site_id, vendor_system_id, parking_lot_index_code
            )
            SELECT
                target.projection_sync_target_id,
                target.site_id,
                target.site_group_id,
                target.vendor_system_id,
                target.parking_lot_index_code,
                target.parking_lot_name,
                target.enabled_flag,
                target.poll_interval_seconds,
                target.lookback_window_minutes,
                target.page_size,
                target.last_success_at,
                target.last_failure_at,
                target.last_attempt_at,
                target.health_status,
                target.failure_count,
                target.last_error_code,
                target.last_error_message,
                target.last_lock_contention_at,
                target.lock_contention_count,
                COALESCE(rollup.latest_projection_last_refreshed_at, NULL) AS latest_projection_last_refreshed_at,
                COALESCE(rollup.total_projection_count, 0) AS total_projection_count,
                COALESCE(rollup.active_projection_count, 0) AS active_projection_count,
                COALESCE(rollup.exited_projection_count, 0) AS exited_projection_count,
                COALESCE(rollup.card_num_projection_count, 0) AS card_num_projection_count,
                COALESCE(rollup.plate_license_projection_count, 0) AS plate_license_projection_count
            FROM sessions.vendor_session_projection_sync_targets target
            LEFT JOIN projection_rollup rollup
              ON rollup.site_id = target.site_id
             AND rollup.vendor_system_id = target.vendor_system_id
             AND rollup.parking_lot_index_code = target.parking_lot_index_code
            ORDER BY target.enabled_flag DESC, target.health_status, target.site_id, target.parking_lot_index_code;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);

        var targets = new List<VendorSessionProjectionHealthTargetReadModel>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            targets.Add(MapTarget(reader));
        }

        return targets;
    }

    /// <inheritdoc />
    public async Task<VendorSessionProjectionHealthTargetReadModel?> GetTargetAsync(
        Guid projectionSyncTargetId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH projection_rollup AS (
                SELECT
                    projection.site_id,
                    projection.vendor_system_id,
                    projection.parking_lot_index_code,
                    max(projection.last_refreshed_at) AS latest_projection_last_refreshed_at,
                    count(*) AS total_projection_count,
                    count(*) FILTER (WHERE projection.projection_status = 'ACTIVE') AS active_projection_count,
                    count(*) FILTER (WHERE projection.projection_status = 'EXITED') AS exited_projection_count,
                    count(*) FILTER (WHERE projection.card_num IS NOT NULL) AS card_num_projection_count,
                    count(*) FILTER (WHERE projection.plate_license IS NOT NULL) AS plate_license_projection_count
                FROM sessions.vendor_session_projections projection
                JOIN sessions.vendor_session_projection_sync_targets target
                  ON target.site_id = projection.site_id
                 AND target.vendor_system_id = projection.vendor_system_id
                 AND target.parking_lot_index_code = projection.parking_lot_index_code
                WHERE target.projection_sync_target_id = @projection_sync_target_id
                  AND projection.projection_status <> 'INVALIDATED'
                GROUP BY projection.site_id, projection.vendor_system_id, projection.parking_lot_index_code
            )
            SELECT
                target.projection_sync_target_id,
                target.site_id,
                target.site_group_id,
                target.vendor_system_id,
                target.parking_lot_index_code,
                target.parking_lot_name,
                target.enabled_flag,
                target.poll_interval_seconds,
                target.lookback_window_minutes,
                target.page_size,
                target.last_success_at,
                target.last_failure_at,
                target.last_attempt_at,
                target.health_status,
                target.failure_count,
                target.last_error_code,
                target.last_error_message,
                target.last_lock_contention_at,
                target.lock_contention_count,
                COALESCE(rollup.latest_projection_last_refreshed_at, NULL) AS latest_projection_last_refreshed_at,
                COALESCE(rollup.total_projection_count, 0) AS total_projection_count,
                COALESCE(rollup.active_projection_count, 0) AS active_projection_count,
                COALESCE(rollup.exited_projection_count, 0) AS exited_projection_count,
                COALESCE(rollup.card_num_projection_count, 0) AS card_num_projection_count,
                COALESCE(rollup.plate_license_projection_count, 0) AS plate_license_projection_count
            FROM sessions.vendor_session_projection_sync_targets target
            LEFT JOIN projection_rollup rollup
              ON rollup.site_id = target.site_id
             AND rollup.vendor_system_id = target.vendor_system_id
             AND rollup.parking_lot_index_code = target.parking_lot_index_code
            WHERE target.projection_sync_target_id = @projection_sync_target_id;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("projection_sync_target_id", NpgsqlDbType.Uuid).Value = projectionSyncTargetId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapTarget(reader) : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VendorSessionProjectionHealthLatestRecord>> ListLatestRecordsAsync(
        Guid projectionSyncTargetId,
        int limit,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                projection.vendor_session_projection_id,
                projection.vendor_record_guid,
                projection.card_num,
                projection.plate_license,
                projection.enter_time,
                projection.exit_time,
                projection.projection_status,
                projection.last_refreshed_at,
                projection.source_event_at,
                projection.correlation_id
            FROM sessions.vendor_session_projections projection
            JOIN sessions.vendor_session_projection_sync_targets target
              ON target.site_id = projection.site_id
             AND target.vendor_system_id = projection.vendor_system_id
             AND target.parking_lot_index_code = projection.parking_lot_index_code
            WHERE target.projection_sync_target_id = @projection_sync_target_id
              AND projection.projection_status <> 'INVALIDATED'
            ORDER BY projection.last_refreshed_at DESC, projection.enter_time DESC NULLS LAST
            LIMIT @limit;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("projection_sync_target_id", NpgsqlDbType.Uuid).Value = projectionSyncTargetId;
        command.Parameters.Add("limit", NpgsqlDbType.Integer).Value = Math.Clamp(limit, 1, 100);

        var records = new List<VendorSessionProjectionHealthLatestRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new VendorSessionProjectionHealthLatestRecord(
                reader.GetGuid(reader.GetOrdinal("vendor_session_projection_id")),
                GetNullableString(reader, "vendor_record_guid"),
                GetNullableString(reader, "card_num"),
                GetNullableString(reader, "plate_license"),
                GetNullableTimestamp(reader, "enter_time"),
                GetNullableTimestamp(reader, "exit_time"),
                FromProjectionStatus(reader.GetString(reader.GetOrdinal("projection_status"))),
                reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("last_refreshed_at")),
                GetNullableTimestamp(reader, "source_event_at"),
                GetNullableGuid(reader, "correlation_id")));
        }

        return records;
    }

    private static VendorSessionProjectionHealthTargetReadModel MapTarget(NpgsqlDataReader reader)
    {
        return new VendorSessionProjectionHealthTargetReadModel(
            reader.GetGuid(reader.GetOrdinal("projection_sync_target_id")),
            reader.GetGuid(reader.GetOrdinal("site_id")),
            reader.GetGuid(reader.GetOrdinal("site_group_id")),
            reader.GetGuid(reader.GetOrdinal("vendor_system_id")),
            reader.GetString(reader.GetOrdinal("parking_lot_index_code")),
            GetNullableString(reader, "parking_lot_name"),
            reader.GetBoolean(reader.GetOrdinal("enabled_flag")),
            FromHealthStatus(reader.GetString(reader.GetOrdinal("health_status"))),
            GetNullableTimestamp(reader, "last_attempt_at"),
            GetNullableTimestamp(reader, "last_success_at"),
            GetNullableTimestamp(reader, "last_failure_at"),
            reader.GetInt32(reader.GetOrdinal("failure_count")),
            GetNullableString(reader, "last_error_code"),
            GetNullableString(reader, "last_error_message"),
            GetNullableTimestamp(reader, "last_lock_contention_at"),
            reader.GetInt32(reader.GetOrdinal("lock_contention_count")),
            reader.GetInt32(reader.GetOrdinal("poll_interval_seconds")),
            reader.GetInt32(reader.GetOrdinal("lookback_window_minutes")),
            reader.GetInt32(reader.GetOrdinal("page_size")),
            GetNullableTimestamp(reader, "latest_projection_last_refreshed_at"),
            reader.GetInt64(reader.GetOrdinal("total_projection_count")),
            reader.GetInt64(reader.GetOrdinal("active_projection_count")),
            reader.GetInt64(reader.GetOrdinal("exited_projection_count")),
            reader.GetInt64(reader.GetOrdinal("card_num_projection_count")),
            reader.GetInt64(reader.GetOrdinal("plate_license_projection_count")));
    }

    private static VendorSessionProjectionHealthStatus FromHealthStatus(string status)
    {
        return status.ToUpperInvariant() switch
        {
            "HEALTHY" => VendorSessionProjectionHealthStatus.Healthy,
            "DEGRADED" => VendorSessionProjectionHealthStatus.Degraded,
            "FAILING" => VendorSessionProjectionHealthStatus.Failing,
            "DISABLED" => VendorSessionProjectionHealthStatus.Disabled,
            "DEFERRED" => VendorSessionProjectionHealthStatus.Deferred,
            _ => VendorSessionProjectionHealthStatus.Unknown
        };
    }

    private static VendorSessionProjectionStatus FromProjectionStatus(string status)
    {
        return status.ToUpperInvariant() switch
        {
            "ACTIVE" => VendorSessionProjectionStatus.Active,
            "EXITED" => VendorSessionProjectionStatus.Exited,
            "STALE" => VendorSessionProjectionStatus.Stale,
            "INVALIDATED" => VendorSessionProjectionStatus.Invalidated,
            _ => VendorSessionProjectionStatus.Unknown
        };
    }

    private static Guid? GetNullableGuid(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static DateTimeOffset? GetNullableTimestamp(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    private static string? GetNullableString(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
}
