using System.Data;
using ExitPass.CentralPms.Application.OperatorConsole;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.OperatorConsole;

/// <summary>
/// PostgreSQL-backed read-only repository for Operator Console access evaluation inputs.
/// </summary>
public sealed class OperatorConsoleAccessEvaluationReadRepository : IOperatorConsoleAccessEvaluationReadRepository
{
    private readonly string _connectionString;

    /// <summary>
    /// Creates an Operator Console access evaluation read repository.
    /// </summary>
    public OperatorConsoleAccessEvaluationReadRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    /// <inheritdoc />
    public async Task<OperatorConsoleAccessEvaluationReadContext> LoadAsync(
        OperatorConsoleAccessEvaluationReadRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var hrIdentityMapping = await ReadHrIdentityMappingAsync(connection, request, cancellationToken);
        var deviceBinding = await ReadDeviceBindingAsync(connection, request.OperatorDeviceBindingId, cancellationToken);
        var deviceAssignment = await ReadDeviceAssignmentAsync(connection, request, cancellationToken);
        var activeShift = await ReadShiftAsync(connection, request, cancellationToken);
        var latestShiftVersion = activeShift is null
            ? null
            : await ReadLatestShiftVersionAsync(connection, activeShift.OperatorShiftId, cancellationToken);
        var latestShiftRevocation = activeShift is null
            ? null
            : await ReadLatestShiftRevocationAsync(connection, activeShift.OperatorShiftId, cancellationToken);
        var activeShiftTakeover = activeShift is null
            ? null
            : await ReadActiveShiftTakeoverAsync(connection, activeShift.OperatorShiftId, request.EvaluatedAt, cancellationToken);

        return new OperatorConsoleAccessEvaluationReadContext(
            request,
            hrIdentityMapping,
            deviceBinding,
            deviceAssignment,
            activeShift,
            latestShiftVersion,
            latestShiftRevocation,
            activeShiftTakeover,
            StatutoryEntitlementFingerprint: null);
    }

    private static async Task<OperatorHrIdentityMappingReadModel?> ReadHrIdentityMappingAsync(
        NpgsqlConnection connection,
        OperatorConsoleAccessEvaluationReadRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                hr_identity_mapping_id,
                user_id,
                hr_provider_code,
                mapping_status::text AS mapping_status,
                effective_from,
                effective_to,
                revoked_at,
                revocation_reason_code
            FROM operator_console.hr_identity_mappings
            WHERE user_id = @user_id
              AND effective_from <= @evaluated_at
              AND (effective_to IS NULL OR effective_to > @evaluated_at)
            ORDER BY
                CASE WHEN mapping_status = 'ACTIVE' THEN 0 ELSE 1 END,
                effective_from DESC,
                hr_identity_mapping_id
            LIMIT 1;
            """;

        await using var command = CreateCommand(sql, connection);
        command.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = request.UserId;
        command.Parameters.Add("evaluated_at", NpgsqlDbType.TimestampTz).Value = request.EvaluatedAt;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new OperatorHrIdentityMappingReadModel(
            reader.GetGuid("hr_identity_mapping_id"),
            reader.GetGuid("user_id"),
            reader.GetString("hr_provider_code"),
            reader.GetString("mapping_status"),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("effective_from")),
            GetNullableDateTimeOffset(reader, "effective_to"),
            GetNullableDateTimeOffset(reader, "revoked_at"),
            GetNullableString(reader, "revocation_reason_code"));
    }

    private static async Task<OperatorDeviceBindingReadModel?> ReadDeviceBindingAsync(
        NpgsqlConnection connection,
        Guid? operatorDeviceBindingId,
        CancellationToken cancellationToken)
    {
        if (!operatorDeviceBindingId.HasValue)
        {
            return null;
        }

        const string sql = """
            SELECT
                operator_device_binding_id,
                device_binding_code,
                device_name,
                site_group_id,
                site_id,
                service_identity_id,
                device_status::text AS device_status,
                trust_level::text AS trust_level,
                binding_source,
                last_seen_at,
                revoked_at,
                revocation_reason_code
            FROM operator_console.operator_device_bindings
            WHERE operator_device_binding_id = @operator_device_binding_id;
            """;

        await using var command = CreateCommand(sql, connection);
        command.Parameters.Add("operator_device_binding_id", NpgsqlDbType.Uuid).Value = operatorDeviceBindingId.Value;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new OperatorDeviceBindingReadModel(
            reader.GetGuid("operator_device_binding_id"),
            reader.GetString("device_binding_code"),
            reader.GetString("device_name"),
            reader.GetGuid("site_group_id"),
            reader.GetGuid("site_id"),
            GetNullableGuid(reader, "service_identity_id"),
            reader.GetString("device_status"),
            reader.GetString("trust_level"),
            reader.GetString("binding_source"),
            GetNullableDateTimeOffset(reader, "last_seen_at"),
            GetNullableDateTimeOffset(reader, "revoked_at"),
            GetNullableString(reader, "revocation_reason_code"));
    }

    private static async Task<OperatorDeviceAssignmentReadModel?> ReadDeviceAssignmentAsync(
        NpgsqlConnection connection,
        OperatorConsoleAccessEvaluationReadRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.OperatorDeviceBindingId.HasValue)
        {
            return null;
        }

        const string sql = """
            SELECT
                operator_device_assignment_history_id,
                operator_device_binding_id,
                site_group_id,
                site_id,
                assignment_status_code,
                assignment_source_code,
                effective_from,
                effective_to,
                ended_at
            FROM operator_console.operator_device_assignment_history
            WHERE operator_device_binding_id = @operator_device_binding_id
              AND (@site_id IS NULL OR site_id = @site_id)
              AND effective_from <= @evaluated_at
              AND (effective_to IS NULL OR effective_to > @evaluated_at)
            ORDER BY effective_from DESC, operator_device_assignment_history_id
            LIMIT 1;
            """;

        await using var command = CreateCommand(sql, connection);
        command.Parameters.Add("operator_device_binding_id", NpgsqlDbType.Uuid).Value = request.OperatorDeviceBindingId.Value;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = DbValue(request.SiteId);
        command.Parameters.Add("evaluated_at", NpgsqlDbType.TimestampTz).Value = request.EvaluatedAt;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new OperatorDeviceAssignmentReadModel(
            reader.GetGuid("operator_device_assignment_history_id"),
            reader.GetGuid("operator_device_binding_id"),
            reader.GetGuid("site_group_id"),
            reader.GetGuid("site_id"),
            reader.GetString("assignment_status_code"),
            reader.GetString("assignment_source_code"),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("effective_from")),
            GetNullableDateTimeOffset(reader, "effective_to"),
            GetNullableDateTimeOffset(reader, "ended_at"));
    }

    private static async Task<OperatorShiftReadModel?> ReadShiftAsync(
        NpgsqlConnection connection,
        OperatorConsoleAccessEvaluationReadRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                operator_shift_id,
                hr_identity_mapping_id,
                operator_user_id,
                site_group_id,
                site_id,
                hr_provider_code,
                operational_status::text AS operational_status,
                scheduled_start_at,
                scheduled_end_at,
                active_from,
                active_to,
                revoked_at,
                revocation_reason_code,
                current_takeover_id
            FROM operator_console.operator_shifts
            WHERE (
                    @operator_shift_id IS NOT NULL
                AND operator_shift_id = @operator_shift_id
            )
               OR (
                    @operator_shift_id IS NULL
                AND operator_user_id = @user_id
                AND (@site_id IS NULL OR site_id = @site_id)
                AND active_from <= @evaluated_at
                AND (active_to IS NULL OR active_to > @evaluated_at)
            )
            ORDER BY
                CASE WHEN operational_status = 'ACTIVE' THEN 0 ELSE 1 END,
                active_from DESC NULLS LAST,
                scheduled_start_at DESC,
                operator_shift_id
            LIMIT 1;
            """;

        await using var command = CreateCommand(sql, connection);
        command.Parameters.Add("operator_shift_id", NpgsqlDbType.Uuid).Value = DbValue(request.OperatorShiftId);
        command.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = request.UserId;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = DbValue(request.SiteId);
        command.Parameters.Add("evaluated_at", NpgsqlDbType.TimestampTz).Value = request.EvaluatedAt;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new OperatorShiftReadModel(
            reader.GetGuid("operator_shift_id"),
            reader.GetGuid("hr_identity_mapping_id"),
            reader.GetGuid("operator_user_id"),
            reader.GetGuid("site_group_id"),
            reader.GetGuid("site_id"),
            reader.GetString("hr_provider_code"),
            reader.GetString("operational_status"),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("scheduled_start_at")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("scheduled_end_at")),
            GetNullableDateTimeOffset(reader, "active_from"),
            GetNullableDateTimeOffset(reader, "active_to"),
            GetNullableDateTimeOffset(reader, "revoked_at"),
            GetNullableString(reader, "revocation_reason_code"),
            GetNullableGuid(reader, "current_takeover_id"));
    }

    private static async Task<OperatorShiftVersionReadModel?> ReadLatestShiftVersionAsync(
        NpgsqlConnection connection,
        Guid operatorShiftId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                operator_shift_version_id,
                operator_shift_id,
                hr_provider_code,
                import_status_code,
                source_system_code,
                scheduled_start_at,
                scheduled_end_at,
                imported_at
            FROM operator_console.operator_shift_versions
            WHERE operator_shift_id = @operator_shift_id
            ORDER BY imported_at DESC, operator_shift_version_id
            LIMIT 1;
            """;

        await using var command = CreateCommand(sql, connection);
        command.Parameters.Add("operator_shift_id", NpgsqlDbType.Uuid).Value = operatorShiftId;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new OperatorShiftVersionReadModel(
            reader.GetGuid("operator_shift_version_id"),
            reader.GetGuid("operator_shift_id"),
            reader.GetString("hr_provider_code"),
            reader.GetString("import_status_code"),
            reader.GetString("source_system_code"),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("scheduled_start_at")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("scheduled_end_at")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("imported_at")));
    }

    private static async Task<OperatorShiftRevocationReadModel?> ReadLatestShiftRevocationAsync(
        NpgsqlConnection connection,
        Guid operatorShiftId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                shift_revocation_id,
                operator_shift_id,
                revocation_status::text AS revocation_status,
                reason_code,
                revoked_operator_user_id,
                site_id,
                requested_at,
                approved_at,
                effective_at
            FROM operator_console.shift_revocations
            WHERE operator_shift_id = @operator_shift_id
            ORDER BY requested_at DESC, shift_revocation_id
            LIMIT 1;
            """;

        await using var command = CreateCommand(sql, connection);
        command.Parameters.Add("operator_shift_id", NpgsqlDbType.Uuid).Value = operatorShiftId;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new OperatorShiftRevocationReadModel(
            reader.GetGuid("shift_revocation_id"),
            reader.GetGuid("operator_shift_id"),
            reader.GetString("revocation_status"),
            reader.GetString("reason_code"),
            reader.GetGuid("revoked_operator_user_id"),
            reader.GetGuid("site_id"),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("requested_at")),
            GetNullableDateTimeOffset(reader, "approved_at"),
            GetNullableDateTimeOffset(reader, "effective_at"));
    }

    private static async Task<OperatorShiftTakeoverReadModel?> ReadActiveShiftTakeoverAsync(
        NpgsqlConnection connection,
        Guid operatorShiftId,
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                shift_takeover_id,
                operator_shift_id,
                original_operator_user_id,
                takeover_operator_user_id,
                takeover_status::text AS takeover_status,
                reason_code,
                site_id,
                requested_at,
                approved_at,
                active_from,
                active_to,
                ended_at
            FROM operator_console.shift_takeovers
            WHERE operator_shift_id = @operator_shift_id
              AND takeover_status = 'ACTIVE'
              AND (active_from IS NULL OR active_from <= @evaluated_at)
              AND (active_to IS NULL OR active_to > @evaluated_at)
            ORDER BY active_from DESC NULLS LAST, requested_at DESC, shift_takeover_id
            LIMIT 1;
            """;

        await using var command = CreateCommand(sql, connection);
        command.Parameters.Add("operator_shift_id", NpgsqlDbType.Uuid).Value = operatorShiftId;
        command.Parameters.Add("evaluated_at", NpgsqlDbType.TimestampTz).Value = evaluatedAt;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new OperatorShiftTakeoverReadModel(
            reader.GetGuid("shift_takeover_id"),
            reader.GetGuid("operator_shift_id"),
            reader.GetGuid("original_operator_user_id"),
            reader.GetGuid("takeover_operator_user_id"),
            reader.GetString("takeover_status"),
            reader.GetString("reason_code"),
            reader.GetGuid("site_id"),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("requested_at")),
            GetNullableDateTimeOffset(reader, "approved_at"),
            GetNullableDateTimeOffset(reader, "active_from"),
            GetNullableDateTimeOffset(reader, "active_to"),
            GetNullableDateTimeOffset(reader, "ended_at"));
    }

    private static NpgsqlCommand CreateCommand(string sql, NpgsqlConnection connection) =>
        new(sql, connection)
        {
            CommandTimeout = 30
        };

    private static object DbValue(Guid? value) => value.HasValue ? value.Value : DBNull.Value;

    private static string? GetNullableString(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static Guid? GetNullableGuid(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static DateTimeOffset? GetNullableDateTimeOffset(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }
}
