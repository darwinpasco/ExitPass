using System.Data;
using ExitPass.CentralPms.Application.VendorSessions;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.VendorSessions;

/// <summary>
/// PostgreSQL repository for vendor session projection snapshots.
/// </summary>
public sealed class PostgresVendorSessionProjectionRepository : IVendorSessionProjectionRepository
{
    private static readonly Guid CentralPmsServiceIdentityId =
        Guid.Parse("12000000-0000-0000-0000-000000000001");

    private readonly string _connectionString;

    /// <summary>
    /// Creates a PostgreSQL projection repository.
    /// </summary>
    public PostgresVendorSessionProjectionRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    /// <inheritdoc />
    public async Task<VendorSessionProjection> UpsertAsync(
        VendorSessionProjection projection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projection);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return await UpsertAsync(connection, transaction: null, projection, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VendorSessionProjection>> UpsertBatchAsync(
        IReadOnlyList<VendorSessionProjection> projections,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projections);

        if (projections.Count == 0)
        {
            return [];
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var results = new List<VendorSessionProjection>(projections.Count);

        foreach (var projection in projections)
        {
            ArgumentNullException.ThrowIfNull(projection);
            results.Add(await UpsertAsync(connection, transaction, projection, cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return results;
    }

    private static async Task<VendorSessionProjection> UpsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        VendorSessionProjection projection,
        CancellationToken cancellationToken)
    {

        const string sql = """
            INSERT INTO sessions.vendor_session_projections (
                vendor_session_projection_id,
                vendor_system_id,
                site_id,
                site_group_id,
                parking_lot_index_code,
                parking_lot_name,
                passageway_index_code,
                passageway_name,
                lane_index_code,
                lane_name,
                lane_direction,
                vendor_record_guid,
                card_num,
                plate_license,
                enter_time,
                exit_time,
                allow_type,
                allow_result,
                image_url,
                source_api,
                source_payload_hash,
                source_payload_reference,
                source_event_at,
                stable_identity_type,
                stable_identity_key,
                first_seen_at,
                last_seen_at,
                last_refreshed_at,
                projection_status,
                correlation_id,
                created_at,
                created_by_service_identity_id,
                updated_at,
                updated_by_service_identity_id,
                row_version
            )
            VALUES (
                @vendor_session_projection_id,
                @vendor_system_id,
                @site_id,
                @site_group_id,
                @parking_lot_index_code,
                @parking_lot_name,
                @passageway_index_code,
                @passageway_name,
                @lane_index_code,
                @lane_name,
                @lane_direction,
                @vendor_record_guid,
                @card_num,
                @plate_license,
                @enter_time,
                @exit_time,
                @allow_type,
                @allow_result,
                @image_url,
                @source_api,
                @source_payload_hash,
                @source_payload_reference,
                @source_event_at,
                @stable_identity_type,
                @stable_identity_key,
                @first_seen_at,
                @last_seen_at,
                @last_refreshed_at,
                @projection_status,
                @correlation_id,
                @created_at,
                @service_identity_id,
                @updated_at,
                @service_identity_id,
                1
            )
            ON CONFLICT ON CONSTRAINT uq_vendor_session_projections__stable_identity_key
            DO UPDATE SET
                vendor_system_id = COALESCE(EXCLUDED.vendor_system_id, sessions.vendor_session_projections.vendor_system_id),
                site_id = COALESCE(EXCLUDED.site_id, sessions.vendor_session_projections.site_id),
                site_group_id = COALESCE(EXCLUDED.site_group_id, sessions.vendor_session_projections.site_group_id),
                parking_lot_index_code = EXCLUDED.parking_lot_index_code,
                parking_lot_name = EXCLUDED.parking_lot_name,
                passageway_index_code = EXCLUDED.passageway_index_code,
                passageway_name = EXCLUDED.passageway_name,
                lane_index_code = EXCLUDED.lane_index_code,
                lane_name = EXCLUDED.lane_name,
                lane_direction = EXCLUDED.lane_direction,
                vendor_record_guid = COALESCE(EXCLUDED.vendor_record_guid, sessions.vendor_session_projections.vendor_record_guid),
                card_num = COALESCE(EXCLUDED.card_num, sessions.vendor_session_projections.card_num),
                plate_license = COALESCE(EXCLUDED.plate_license, sessions.vendor_session_projections.plate_license),
                enter_time = COALESCE(EXCLUDED.enter_time, sessions.vendor_session_projections.enter_time),
                exit_time = COALESCE(EXCLUDED.exit_time, sessions.vendor_session_projections.exit_time),
                allow_type = EXCLUDED.allow_type,
                allow_result = EXCLUDED.allow_result,
                image_url = EXCLUDED.image_url,
                source_api = EXCLUDED.source_api,
                source_payload_hash = EXCLUDED.source_payload_hash,
                source_payload_reference = EXCLUDED.source_payload_reference,
                source_event_at = EXCLUDED.source_event_at,
                first_seen_at = LEAST(sessions.vendor_session_projections.first_seen_at, EXCLUDED.first_seen_at),
                last_seen_at = GREATEST(sessions.vendor_session_projections.last_seen_at, EXCLUDED.last_seen_at),
                last_refreshed_at = EXCLUDED.last_refreshed_at,
                projection_status = EXCLUDED.projection_status,
                correlation_id = EXCLUDED.correlation_id,
                updated_at = EXCLUDED.updated_at,
                updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
                row_version = sessions.vendor_session_projections.row_version + 1
            RETURNING
                vendor_session_projection_id,
                vendor_system_id,
                site_id,
                site_group_id,
                parking_lot_index_code,
                parking_lot_name,
                passageway_index_code,
                passageway_name,
                lane_index_code,
                lane_name,
                lane_direction,
                vendor_record_guid,
                card_num,
                plate_license,
                enter_time,
                exit_time,
                allow_type,
                allow_result,
                image_url,
                source_api,
                source_payload_hash,
                source_payload_reference,
                source_event_at,
                stable_identity_type,
                stable_identity_key,
                first_seen_at,
                last_seen_at,
                last_refreshed_at,
                projection_status,
                correlation_id,
                created_at,
                updated_at;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddProjectionParameters(command, projection);

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Vendor session projection upsert did not return a row.");
        }

        return MapProjection(reader);
    }

    /// <inheritdoc />
    public async Task<VendorSessionProjectionReadResult?> FindLatestAsync(
        VendorSessionProjectionLookupQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        const string sql = """
            SELECT
                projection.vendor_session_projection_id,
                projection.vendor_system_id,
                projection.site_id,
                projection.site_group_id,
                projection.parking_lot_index_code,
                projection.parking_lot_name,
                projection.passageway_index_code,
                projection.passageway_name,
                projection.lane_index_code,
                projection.lane_name,
                projection.lane_direction,
                projection.vendor_record_guid,
                projection.card_num,
                projection.plate_license,
                projection.enter_time,
                projection.exit_time,
                projection.allow_type,
                projection.allow_result,
                projection.image_url,
                projection.source_api,
                projection.source_payload_hash,
                projection.source_payload_reference,
                projection.source_event_at,
                projection.stable_identity_type,
                projection.stable_identity_key,
                projection.first_seen_at,
                projection.last_seen_at,
                projection.last_refreshed_at,
                projection.projection_status,
                projection.correlation_id,
                projection.created_at,
                projection.updated_at,
                CASE WHEN target.enabled_flag THEN target.last_success_at ELSE NULL END AS target_last_success_at
            FROM sessions.vendor_session_projections projection
            LEFT JOIN sessions.vendor_session_projection_sync_targets target
              ON target.site_id = projection.site_id
             AND target.site_group_id = projection.site_group_id
             AND target.vendor_system_id = projection.vendor_system_id
             AND target.parking_lot_index_code = projection.parking_lot_index_code
            WHERE (@site_id IS NULL OR projection.site_id = @site_id)
              AND (@site_group_id IS NULL OR projection.site_group_id = @site_group_id)
              AND (@parking_lot_index_code IS NULL OR projection.parking_lot_index_code = @parking_lot_index_code)
              AND (
                    (@card_num IS NOT NULL AND projection.card_num = @card_num)
                 OR (@card_num IS NULL AND @plate_license IS NOT NULL AND projection.plate_license = @plate_license)
              )
              AND projection.projection_status <> 'INVALIDATED'
            ORDER BY
                CASE projection.projection_status
                    WHEN 'ACTIVE' THEN 0
                    WHEN 'UNKNOWN' THEN 1
                    WHEN 'STALE' THEN 2
                    WHEN 'EXITED' THEN 3
                    ELSE 4
                END,
                projection.last_refreshed_at DESC,
                projection.enter_time DESC NULLS LAST,
                projection.created_at DESC
            LIMIT 1;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = DbValue(query.SiteId);
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = DbValue(query.SiteGroupId);
        command.Parameters.Add("parking_lot_index_code", NpgsqlDbType.Text).Value = DbValue(query.ParkingLotIndexCode);
        command.Parameters.Add("card_num", NpgsqlDbType.Text).Value = DbValue(query.CardNum);
        command.Parameters.Add("plate_license", NpgsqlDbType.Text).Value = DbValue(query.PlateLicense);

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new VendorSessionProjectionReadResult(
            MapProjection(reader),
            GetNullableTimestamp(reader, "target_last_success_at"));
    }

    private static void AddProjectionParameters(NpgsqlCommand command, VendorSessionProjection projection)
    {
        command.Parameters.Add("vendor_session_projection_id", NpgsqlDbType.Uuid).Value = projection.VendorSessionProjectionId;
        command.Parameters.Add("vendor_system_id", NpgsqlDbType.Uuid).Value = DbValue(projection.VendorSystemId);
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = DbValue(projection.SiteId);
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = DbValue(projection.SiteGroupId);
        command.Parameters.Add("parking_lot_index_code", NpgsqlDbType.Text).Value = DbValue(projection.ParkingLotIndexCode);
        command.Parameters.Add("parking_lot_name", NpgsqlDbType.Text).Value = DbValue(projection.ParkingLotName);
        command.Parameters.Add("passageway_index_code", NpgsqlDbType.Text).Value = DbValue(projection.PassagewayIndexCode);
        command.Parameters.Add("passageway_name", NpgsqlDbType.Text).Value = DbValue(projection.PassagewayName);
        command.Parameters.Add("lane_index_code", NpgsqlDbType.Text).Value = DbValue(projection.LaneIndexCode);
        command.Parameters.Add("lane_name", NpgsqlDbType.Text).Value = DbValue(projection.LaneName);
        command.Parameters.Add("lane_direction", NpgsqlDbType.Text).Value = DbValue(projection.LaneDirection);
        command.Parameters.Add("vendor_record_guid", NpgsqlDbType.Text).Value = DbValue(projection.VendorRecordGuid);
        command.Parameters.Add("card_num", NpgsqlDbType.Text).Value = DbValue(projection.CardNum);
        command.Parameters.Add("plate_license", NpgsqlDbType.Text).Value = DbValue(projection.PlateLicense);
        command.Parameters.Add("enter_time", NpgsqlDbType.TimestampTz).Value = DbTimestampValue(projection.EnterTime);
        command.Parameters.Add("exit_time", NpgsqlDbType.TimestampTz).Value = DbTimestampValue(projection.ExitTime);
        command.Parameters.Add("allow_type", NpgsqlDbType.Text).Value = DbValue(projection.AllowType);
        command.Parameters.Add("allow_result", NpgsqlDbType.Text).Value = DbValue(projection.AllowResult);
        command.Parameters.Add("image_url", NpgsqlDbType.Text).Value = DbValue(projection.ImageUrl);
        command.Parameters.AddWithValue("source_api", projection.SourceApi);
        command.Parameters.AddWithValue("source_payload_hash", projection.SourcePayloadHash);
        command.Parameters.Add("source_payload_reference", NpgsqlDbType.Text).Value = DbValue(projection.SourcePayloadReference);
        command.Parameters.Add("source_event_at", NpgsqlDbType.TimestampTz).Value = DbTimestampValue(projection.SourceEventAt);
        command.Parameters.AddWithValue("stable_identity_type", projection.StableIdentityType);
        command.Parameters.AddWithValue("stable_identity_key", projection.StableIdentityKey);
        command.Parameters.Add("first_seen_at", NpgsqlDbType.TimestampTz).Value = ToUtc(projection.FirstSeenAt);
        command.Parameters.Add("last_seen_at", NpgsqlDbType.TimestampTz).Value = ToUtc(projection.LastSeenAt);
        command.Parameters.Add("last_refreshed_at", NpgsqlDbType.TimestampTz).Value = ToUtc(projection.LastRefreshedAt);
        command.Parameters.AddWithValue("projection_status", ToDatabaseStatus(projection.ProjectionStatus));
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = DbValue(projection.CorrelationId);
        command.Parameters.Add("created_at", NpgsqlDbType.TimestampTz).Value = ToUtc(projection.CreatedAt);
        command.Parameters.Add("updated_at", NpgsqlDbType.TimestampTz).Value = ToUtc(projection.UpdatedAt);
        command.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = CentralPmsServiceIdentityId;
    }

    private static VendorSessionProjection MapProjection(NpgsqlDataReader reader)
    {
        return new VendorSessionProjection(
            reader.GetGuid(reader.GetOrdinal("vendor_session_projection_id")),
            GetNullableGuid(reader, "vendor_system_id"),
            GetNullableGuid(reader, "site_id"),
            GetNullableGuid(reader, "site_group_id"),
            GetNullableString(reader, "parking_lot_index_code"),
            GetNullableString(reader, "parking_lot_name"),
            GetNullableString(reader, "passageway_index_code"),
            GetNullableString(reader, "passageway_name"),
            GetNullableString(reader, "lane_index_code"),
            GetNullableString(reader, "lane_name"),
            GetNullableString(reader, "lane_direction"),
            GetNullableString(reader, "vendor_record_guid"),
            GetNullableString(reader, "card_num"),
            GetNullableString(reader, "plate_license"),
            GetNullableTimestamp(reader, "enter_time"),
            GetNullableTimestamp(reader, "exit_time"),
            GetNullableString(reader, "allow_type"),
            GetNullableString(reader, "allow_result"),
            GetNullableString(reader, "image_url"),
            reader.GetString(reader.GetOrdinal("source_api")),
            reader.GetString(reader.GetOrdinal("source_payload_hash")),
            GetNullableString(reader, "source_payload_reference"),
            GetNullableTimestamp(reader, "source_event_at"),
            reader.GetString(reader.GetOrdinal("stable_identity_type")),
            reader.GetString(reader.GetOrdinal("stable_identity_key")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("first_seen_at")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("last_seen_at")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("last_refreshed_at")),
            FromDatabaseStatus(reader.GetString(reader.GetOrdinal("projection_status"))),
            GetNullableGuid(reader, "correlation_id"),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at")));
    }

    private static string ToDatabaseStatus(VendorSessionProjectionStatus status)
    {
        return status switch
        {
            VendorSessionProjectionStatus.Active => "ACTIVE",
            VendorSessionProjectionStatus.Exited => "EXITED",
            VendorSessionProjectionStatus.Stale => "STALE",
            VendorSessionProjectionStatus.Invalidated => "INVALIDATED",
            _ => "UNKNOWN"
        };
    }

    private static VendorSessionProjectionStatus FromDatabaseStatus(string status)
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

    private static object DbValue(Guid? value) => value.HasValue ? value.Value : DBNull.Value;

    private static object DbTimestampValue(DateTimeOffset? value) =>
        value.HasValue ? ToUtc(value.Value) : DBNull.Value;

    private static DateTimeOffset ToUtc(DateTimeOffset value) => value.ToUniversalTime();

    private static object DbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
}
