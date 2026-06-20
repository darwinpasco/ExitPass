using ExitPass.CentralPms.Application.VendorSessions;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.VendorSessions;

/// <summary>
/// PostgreSQL repository for site-scoped vendor session projection sync targets.
/// </summary>
public sealed class PostgresVendorSessionProjectionSyncTargetRepository : IVendorSessionProjectionSyncTargetRepository
{
    private readonly string _connectionString;

    /// <summary>
    /// Creates a PostgreSQL sync target repository.
    /// </summary>
    public PostgresVendorSessionProjectionSyncTargetRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VendorSessionProjectionSyncTarget>> ListDueTargetsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                projection_sync_target_id,
                site_id,
                site_group_id,
                vendor_system_id,
                parking_lot_index_code,
                parking_lot_name,
                enabled_flag,
                poll_interval_seconds,
                lookback_window_minutes,
                page_size,
                last_success_at,
                last_failure_at,
                last_attempt_at,
                health_status,
                failure_count,
                last_error_code,
                last_error_message,
                created_at,
                updated_at
            FROM sessions.vendor_session_projection_sync_targets
            WHERE enabled_flag = TRUE
              AND (
                    last_attempt_at IS NULL
                 OR last_attempt_at <= @now - (poll_interval_seconds * interval '1 second')
              )
            ORDER BY last_attempt_at NULLS FIRST, site_id, parking_lot_index_code;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("now", NpgsqlDbType.TimestampTz).Value = now;

        var targets = new List<VendorSessionProjectionSyncTarget>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            targets.Add(MapTarget(reader));
        }

        return targets;
    }

    /// <inheritdoc />
    public async Task<VendorSessionProjectionSyncTarget?> FindEnabledTargetAsync(
        Guid? siteId,
        string? parkingLotIndexCode,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                projection_sync_target_id,
                site_id,
                site_group_id,
                vendor_system_id,
                parking_lot_index_code,
                parking_lot_name,
                enabled_flag,
                poll_interval_seconds,
                lookback_window_minutes,
                page_size,
                last_success_at,
                last_failure_at,
                last_attempt_at,
                health_status,
                failure_count,
                last_error_code,
                last_error_message,
                created_at,
                updated_at
            FROM sessions.vendor_session_projection_sync_targets
            WHERE enabled_flag = TRUE
              AND (@site_id IS NULL OR site_id = @site_id)
              AND (@parking_lot_index_code IS NULL OR parking_lot_index_code = @parking_lot_index_code)
            ORDER BY site_id, parking_lot_index_code
            LIMIT 2;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = DbValue(siteId);
        command.Parameters.Add("parking_lot_index_code", NpgsqlDbType.Text).Value = DbValue(parkingLotIndexCode);

        var targets = new List<VendorSessionProjectionSyncTarget>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            targets.Add(MapTarget(reader));
        }

        if (targets.Count > 1)
        {
            throw new InvalidOperationException("VENDOR_SESSION_PROJECTION_SYNC_TARGET_AMBIGUOUS");
        }

        return targets.SingleOrDefault();
    }

    /// <inheritdoc />
    public async Task UpdateHealthAsync(
        VendorSessionProjectionSyncTargetHealthUpdate update,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE sessions.vendor_session_projection_sync_targets
            SET
                last_attempt_at = @attempted_at,
                last_success_at = CASE WHEN @succeeded THEN @attempted_at ELSE last_success_at END,
                last_failure_at = CASE WHEN @succeeded THEN last_failure_at ELSE @attempted_at END,
                failure_count = CASE WHEN @succeeded THEN 0 ELSE failure_count + 1 END,
                health_status = CASE
                    WHEN @succeeded THEN 'HEALTHY'
                    WHEN failure_count + 1 >= @failing_failure_count_threshold THEN 'FAILING'
                    ELSE 'DEGRADED'
                END,
                last_error_code = CASE WHEN @succeeded THEN NULL ELSE @last_error_code END,
                last_error_message = CASE WHEN @succeeded THEN NULL ELSE @last_error_message END,
                updated_at = @attempted_at,
                correlation_id = @correlation_id,
                row_version = row_version + 1
            WHERE projection_sync_target_id = @projection_sync_target_id;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("projection_sync_target_id", NpgsqlDbType.Uuid).Value = update.ProjectionSyncTargetId;
        command.Parameters.Add("attempted_at", NpgsqlDbType.TimestampTz).Value = update.AttemptedAt;
        command.Parameters.AddWithValue("succeeded", update.Succeeded);
        command.Parameters.Add("last_error_code", NpgsqlDbType.Text).Value = DbValue(update.ErrorCode);
        command.Parameters.Add("last_error_message", NpgsqlDbType.Text).Value = DbValue(update.ErrorMessage);
        command.Parameters.AddWithValue("failing_failure_count_threshold", Math.Max(1, update.FailingFailureCountThreshold));
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = update.CorrelationId;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static VendorSessionProjectionSyncTarget MapTarget(NpgsqlDataReader reader)
    {
        return new VendorSessionProjectionSyncTarget(
            reader.GetGuid(reader.GetOrdinal("projection_sync_target_id")),
            reader.GetGuid(reader.GetOrdinal("site_id")),
            reader.GetGuid(reader.GetOrdinal("site_group_id")),
            reader.GetGuid(reader.GetOrdinal("vendor_system_id")),
            reader.GetString(reader.GetOrdinal("parking_lot_index_code")),
            GetNullableString(reader, "parking_lot_name"),
            reader.GetBoolean(reader.GetOrdinal("enabled_flag")),
            reader.GetInt32(reader.GetOrdinal("poll_interval_seconds")),
            reader.GetInt32(reader.GetOrdinal("lookback_window_minutes")),
            reader.GetInt32(reader.GetOrdinal("page_size")),
            GetNullableTimestamp(reader, "last_success_at"),
            GetNullableTimestamp(reader, "last_failure_at"),
            GetNullableTimestamp(reader, "last_attempt_at"),
            FromDatabaseHealthStatus(reader.GetString(reader.GetOrdinal("health_status"))),
            reader.GetInt32(reader.GetOrdinal("failure_count")),
            GetNullableString(reader, "last_error_code"),
            GetNullableString(reader, "last_error_message"),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at")));
    }

    private static VendorSessionProjectionHealthStatus FromDatabaseHealthStatus(string status)
    {
        return status.ToUpperInvariant() switch
        {
            "HEALTHY" => VendorSessionProjectionHealthStatus.Healthy,
            "DEGRADED" => VendorSessionProjectionHealthStatus.Degraded,
            "FAILING" => VendorSessionProjectionHealthStatus.Failing,
            "DISABLED" => VendorSessionProjectionHealthStatus.Disabled,
            _ => VendorSessionProjectionHealthStatus.Unknown
        };
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

    private static object DbValue(Guid? value) => value.HasValue ? value.Value : DBNull.Value;

    private static object DbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
}
