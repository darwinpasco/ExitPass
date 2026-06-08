using System.Data;
using ExitPass.CentralPms.Application.OperatorConsole;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.OperatorConsole;

/// <summary>
/// PostgreSQL-backed read-only repository for Operator Console access readiness.
///
/// Design references:
/// docs/operator-console/OperatorConsole_Access_Readiness_API_Backend_Design_v1.md,
/// docs/operator-console/OperatorConsole_Device_Enrollment_Readiness_Design_v1.md, and
/// docs/operator-console/OperatorConsole_Shift_Site_Validation_Workflow_Design_v1.md.
/// Invariant: production controlled actions must be backed by real operator, device, shift,
/// site, workflow-state, and audit readiness instead of local/dev fallback headers.
/// </summary>
public sealed class OperatorConsoleAccessReadinessRepository : IOperatorConsoleAccessReadinessRepository
{
    private static readonly string[] RequiredTableNames =
    [
        "hr_identity_mappings",
        "operator_device_bindings",
        "operator_device_assignment_history",
        "operator_shifts",
        "operator_access_evaluations",
        "operator_access_evaluation_reasons"
    ];

    private readonly string _connectionString;
    private readonly SemaphoreSlim _capabilityLock = new(1, 1);
    private OperatorConsoleAccessReadinessRepositoryCapabilities? _capabilities;

    /// <summary>Creates an Operator Console access readiness repository.</summary>
    public OperatorConsoleAccessReadinessRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    /// <inheritdoc />
    public async Task<OperatorConsoleAccessReadinessRepositoryResult> LoadAsync(
        OperatorConsoleAccessReadinessCommand command,
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var capabilities = await GetCapabilitiesAsync(connection, cancellationToken);
        if (!capabilities.HasReadinessTables)
        {
            return Empty(capabilities);
        }

        var operatorReasons = await ReadOperatorReasonsAsync(connection, command, evaluatedAt, cancellationToken);
        var deviceReasons = await ReadDeviceReasonsAsync(connection, command, evaluatedAt, cancellationToken);
        var shiftReasons = await ReadShiftReasonsAsync(connection, command, evaluatedAt, cancellationToken);

        return new OperatorConsoleAccessReadinessRepositoryResult(
            capabilities,
            operatorReasons,
            deviceReasons.Reasons,
            shiftReasons.Reasons,
            BuildSiteReasons(deviceReasons, shiftReasons));
    }

    private async Task<OperatorConsoleAccessReadinessRepositoryCapabilities> GetCapabilitiesAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        if (_capabilities is not null)
        {
            return _capabilities;
        }

        await _capabilityLock.WaitAsync(cancellationToken);
        try
        {
            if (_capabilities is not null)
            {
                return _capabilities;
            }

            const string sql = """
                SELECT table_name
                FROM information_schema.tables
                WHERE table_schema = 'operator_console'
                  AND table_name = ANY(@table_names);
                """;

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.Add("table_names", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = RequiredTableNames;

            var tables = new HashSet<string>(StringComparer.Ordinal);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                tables.Add(reader.GetString("table_name"));
            }

            _capabilities = new OperatorConsoleAccessReadinessRepositoryCapabilities(
                OperatorConsoleSchemaExists: tables.Count > 0,
                HrIdentityMappingsTableExists: tables.Contains("hr_identity_mappings"),
                OperatorDeviceBindingsTableExists: tables.Contains("operator_device_bindings"),
                OperatorDeviceAssignmentHistoryTableExists: tables.Contains("operator_device_assignment_history"),
                OperatorShiftsTableExists: tables.Contains("operator_shifts"),
                OperatorAccessEvaluationsTableExists: tables.Contains("operator_access_evaluations"),
                OperatorAccessEvaluationReasonsTableExists: tables.Contains("operator_access_evaluation_reasons"));

            return _capabilities;
        }
        finally
        {
            _capabilityLock.Release();
        }
    }

    private static async Task<IReadOnlyList<string>> ReadOperatorReasonsAsync(
        NpgsqlConnection connection,
        OperatorConsoleAccessReadinessCommand command,
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken)
    {
        if (!command.OperatorUserId.HasValue || command.OperatorUserId.Value == Guid.Empty)
        {
            return [];
        }

        const string sql = """
            SELECT mapping_status::text, effective_from, effective_to, revoked_at
            FROM operator_console.hr_identity_mappings
            WHERE user_id = @operator_user_id
            ORDER BY
                CASE WHEN mapping_status = 'ACTIVE' THEN 0 ELSE 1 END,
                effective_from DESC
            LIMIT 1;
            """;

        await using var sqlCommand = new NpgsqlCommand(sql, connection);
        sqlCommand.Parameters.Add("operator_user_id", NpgsqlDbType.Uuid).Value = command.OperatorUserId.Value;

        await using var reader = await sqlCommand.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return [OperatorConsoleDenialReasonCatalog.OperatorNotFound];
        }

        var status = reader.GetString("mapping_status");
        var effectiveFrom = reader.GetFieldValue<DateTimeOffset>("effective_from");
        var effectiveTo = GetNullableDateTimeOffset(reader, "effective_to");
        var revokedAt = GetNullableDateTimeOffset(reader, "revoked_at");

        return string.Equals(status, "ACTIVE", StringComparison.Ordinal) &&
            effectiveFrom <= evaluatedAt &&
            (!effectiveTo.HasValue || effectiveTo.Value > evaluatedAt) &&
            !revokedAt.HasValue
            ? []
            : [OperatorConsoleDenialReasonCatalog.OperatorInactive];
    }

    private static async Task<DeviceReadinessFacts> ReadDeviceReasonsAsync(
        NpgsqlConnection connection,
        OperatorConsoleAccessReadinessCommand command,
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken)
    {
        if (!command.OperatorDeviceBindingId.HasValue || command.OperatorDeviceBindingId.Value == Guid.Empty)
        {
            return DeviceReadinessFacts.Empty;
        }

        const string bindingSql = """
            SELECT device_status::text, trust_level::text, site_group_id, site_id, revoked_at, lost_reported_at
            FROM operator_console.operator_device_bindings
            WHERE operator_device_binding_id = @operator_device_binding_id;
            """;

        await using var bindingCommand = new NpgsqlCommand(bindingSql, connection);
        bindingCommand.Parameters.Add("operator_device_binding_id", NpgsqlDbType.Uuid).Value = command.OperatorDeviceBindingId.Value;

        Guid? bindingSiteGroupId;
        Guid? bindingSiteId;
        var reasons = new List<string>();
        await using (var reader = await bindingCommand.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return new DeviceReadinessFacts([OperatorConsoleDenialReasonCatalog.DeviceNotEnrolled], null, null);
            }

            var status = reader.GetString("device_status");
            var trustLevel = reader.GetString("trust_level");
            bindingSiteGroupId = reader.GetGuid("site_group_id");
            bindingSiteId = reader.GetGuid("site_id");
            var revokedAt = GetNullableDateTimeOffset(reader, "revoked_at");
            var lostReportedAt = GetNullableDateTimeOffset(reader, "lost_reported_at");

            if (!string.Equals(status, "ACTIVE", StringComparison.Ordinal) ||
                string.Equals(trustLevel, "UNVERIFIED", StringComparison.Ordinal) ||
                revokedAt.HasValue ||
                lostReportedAt.HasValue)
            {
                reasons.Add(OperatorConsoleDenialReasonCatalog.DeviceNotActive);
            }

            if (command.SiteId.HasValue && bindingSiteId != command.SiteId.Value)
            {
                reasons.Add(OperatorConsoleDenialReasonCatalog.DeviceSiteMismatch);
            }

            if (command.SiteGroupId.HasValue && bindingSiteGroupId != command.SiteGroupId.Value)
            {
                reasons.Add(OperatorConsoleDenialReasonCatalog.DeviceSiteMismatch);
            }
        }

        var assignment = await ReadActiveDeviceAssignmentAsync(
            connection,
            command.OperatorDeviceBindingId.Value,
            evaluatedAt,
            cancellationToken);

        if (assignment is null)
        {
            reasons.Add(OperatorConsoleDenialReasonCatalog.DeviceSiteMismatch);
        }
        else
        {
            if (!string.Equals(assignment.AssignmentStatusCode, "ACTIVE", StringComparison.Ordinal))
            {
                reasons.Add(OperatorConsoleDenialReasonCatalog.DeviceSiteMismatch);
            }

            if (command.SiteId.HasValue && assignment.SiteId != command.SiteId.Value)
            {
                reasons.Add(OperatorConsoleDenialReasonCatalog.DeviceSiteMismatch);
            }

            if (command.SiteGroupId.HasValue && assignment.SiteGroupId != command.SiteGroupId.Value)
            {
                reasons.Add(OperatorConsoleDenialReasonCatalog.DeviceSiteMismatch);
            }
        }

        return new DeviceReadinessFacts(reasons.Distinct(StringComparer.Ordinal).ToArray(), bindingSiteGroupId, bindingSiteId);
    }

    private static async Task<ShiftReadinessFacts> ReadShiftReasonsAsync(
        NpgsqlConnection connection,
        OperatorConsoleAccessReadinessCommand command,
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken)
    {
        if (!command.OperatorShiftId.HasValue || command.OperatorShiftId.Value == Guid.Empty)
        {
            return ShiftReadinessFacts.Empty;
        }

        const string sql = """
            SELECT operator_user_id, site_group_id, site_id, operational_status::text, active_from, active_to, revoked_at
            FROM operator_console.operator_shifts
            WHERE operator_shift_id = @operator_shift_id;
            """;

        await using var sqlCommand = new NpgsqlCommand(sql, connection);
        sqlCommand.Parameters.Add("operator_shift_id", NpgsqlDbType.Uuid).Value = command.OperatorShiftId.Value;

        await using var reader = await sqlCommand.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new ShiftReadinessFacts([OperatorConsoleDenialReasonCatalog.ShiftNotFound], null, null);
        }

        var reasons = new List<string>();
        var operatorUserId = reader.GetGuid("operator_user_id");
        var siteGroupId = reader.GetGuid("site_group_id");
        var siteId = reader.GetGuid("site_id");
        var status = reader.GetString("operational_status");
        var activeFrom = GetNullableDateTimeOffset(reader, "active_from");
        var activeTo = GetNullableDateTimeOffset(reader, "active_to");
        var revokedAt = GetNullableDateTimeOffset(reader, "revoked_at");

        if (!string.Equals(status, "ACTIVE", StringComparison.Ordinal) ||
            !activeFrom.HasValue ||
            activeFrom.Value > evaluatedAt ||
            (activeTo.HasValue && activeTo.Value <= evaluatedAt) ||
            revokedAt.HasValue)
        {
            reasons.Add(OperatorConsoleDenialReasonCatalog.ShiftNotActive);
        }

        if (command.OperatorUserId.HasValue && operatorUserId != command.OperatorUserId.Value)
        {
            reasons.Add(OperatorConsoleDenialReasonCatalog.OperatorSiteNotAllowed);
        }

        if (command.SiteId.HasValue && siteId != command.SiteId.Value)
        {
            reasons.Add(OperatorConsoleDenialReasonCatalog.ShiftSiteMismatch);
        }

        if (command.SiteGroupId.HasValue && siteGroupId != command.SiteGroupId.Value)
        {
            reasons.Add(OperatorConsoleDenialReasonCatalog.ShiftSiteMismatch);
        }

        return new ShiftReadinessFacts(reasons.Distinct(StringComparer.Ordinal).ToArray(), siteGroupId, siteId);
    }

    private static async Task<DeviceAssignmentRow?> ReadActiveDeviceAssignmentAsync(
        NpgsqlConnection connection,
        Guid operatorDeviceBindingId,
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT site_group_id, site_id, assignment_status_code
            FROM operator_console.operator_device_assignment_history
            WHERE operator_device_binding_id = @operator_device_binding_id
              AND effective_from <= @evaluated_at
              AND (effective_to IS NULL OR effective_to > @evaluated_at)
              AND ended_at IS NULL
            ORDER BY effective_from DESC
            LIMIT 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("operator_device_binding_id", NpgsqlDbType.Uuid).Value = operatorDeviceBindingId;
        command.Parameters.Add("evaluated_at", NpgsqlDbType.TimestampTz).Value = evaluatedAt;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new DeviceAssignmentRow(
                reader.GetGuid("site_group_id"),
                reader.GetGuid("site_id"),
                reader.GetString("assignment_status_code"))
            : null;
    }

    private static IReadOnlyList<string> BuildSiteReasons(DeviceReadinessFacts device, ShiftReadinessFacts shift)
    {
        var reasons = new List<string>();
        if (device.Reasons.Contains(OperatorConsoleDenialReasonCatalog.DeviceSiteMismatch, StringComparer.Ordinal))
        {
            reasons.Add(OperatorConsoleDenialReasonCatalog.DeviceSiteMismatch);
        }

        if (shift.Reasons.Contains(OperatorConsoleDenialReasonCatalog.ShiftSiteMismatch, StringComparer.Ordinal) ||
            shift.Reasons.Contains(OperatorConsoleDenialReasonCatalog.OperatorSiteNotAllowed, StringComparer.Ordinal))
        {
            reasons.AddRange(shift.Reasons.Where(reason =>
                reason == OperatorConsoleDenialReasonCatalog.ShiftSiteMismatch ||
                reason == OperatorConsoleDenialReasonCatalog.OperatorSiteNotAllowed));
        }

        return reasons.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static OperatorConsoleAccessReadinessRepositoryResult Empty(
        OperatorConsoleAccessReadinessRepositoryCapabilities capabilities) =>
        new(capabilities, [], [], [], []);

    private static DateTimeOffset? GetNullableDateTimeOffset(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    private sealed record DeviceAssignmentRow(
        Guid SiteGroupId,
        Guid SiteId,
        string AssignmentStatusCode);

    private sealed record DeviceReadinessFacts(
        IReadOnlyList<string> Reasons,
        Guid? SiteGroupId,
        Guid? SiteId)
    {
        public static DeviceReadinessFacts Empty { get; } = new([], null, null);
    }

    private sealed record ShiftReadinessFacts(
        IReadOnlyList<string> Reasons,
        Guid? SiteGroupId,
        Guid? SiteId)
    {
        public static ShiftReadinessFacts Empty { get; } = new([], null, null);
    }
}
