using System.Data;
using ExitPass.CentralPms.Application.OperatorConsole;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.OperatorConsole;

/// <summary>
/// PostgreSQL-backed read repository for fiscal status view-audit report rows.
/// </summary>
public sealed class OperatorConsoleFiscalStatusViewAuditReportRepository :
    IOperatorConsoleFiscalStatusViewAuditReportRepository
{
    private const string FiscalStatusViewActionCode = OperatorConsoleActionCodes.ViewFiscalIssuanceStatus;
    private const string FiscalIssuanceReferenceTargetEntityType = "FISCAL_ISSUANCE_REFERENCE";

    private readonly string _connectionString;

    /// <summary>
    /// Creates a fiscal status view-audit report repository.
    /// </summary>
    public OperatorConsoleFiscalStatusViewAuditReportRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    /// <inheritdoc />
    public async Task<OperatorConsoleFiscalStatusViewAuditReportResult> ListAsync(
        OperatorConsoleFiscalStatusViewAuditReportQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Invariant: this report reads safe Operator Console action-log metadata only. It must not join raw
        // fiscal payloads, POS Server payloads, payment provider payloads, statutory evidence, or customer PII.
        const string sql = """
            WITH source AS (
                SELECT
                    log.operator_action_log_id,
                    log.performed_at,
                    log.action_reason_code,
                    COALESCE(
                        notes.payload ->> 'FiscalStatusViewResultClass',
                        CASE
                            WHEN log.action_status::text = 'DENIED' THEN 'DENIED'
                            WHEN log.action_status::text = 'FAILED' THEN 'FAILED_SAFELY'
                            ELSE 'SUCCEEDED'
                        END) AS result_class,
                    log.operator_user_id,
                    log.site_id,
                    NULLIF(notes.payload ->> 'SiteGroupId', '')::uuid AS site_group_id,
                    log.target_entity_id AS fiscal_issuance_reference_id,
                    log.correlation_id,
                    COALESCE(
                        notes.payload ->> 'FiscalStatusViewSafeErrorPosture',
                        notes.payload ->> 'FiscalStatusViewSafeErrorCode',
                        notes.payload ->> 'Decision') AS safe_denial_or_error_posture,
                    notes.payload ->> 'FiscalStatusViewSourceModule' AS source_module
                FROM operations.operator_action_logs AS log
                CROSS JOIN LATERAL (
                    SELECT COALESCE(NULLIF(log.action_notes, ''), '{}')::jsonb AS payload
                ) AS notes
                WHERE log.action_reason_code = @action_reason_code
                  AND log.target_entity_type = @target_entity_type
                  AND log.target_entity_id IS NOT NULL
            )
            SELECT
                operator_action_log_id,
                performed_at,
                action_reason_code,
                result_class,
                operator_user_id,
                site_id,
                site_group_id,
                fiscal_issuance_reference_id,
                correlation_id,
                safe_denial_or_error_posture,
                source_module,
                COUNT(*) OVER() AS total_count
            FROM source
            WHERE (@from IS NULL OR performed_at >= @from)
              AND (@to IS NULL OR performed_at <= @to)
              AND (@site_id IS NULL OR site_id = @site_id)
              AND (@site_group_id IS NULL OR site_group_id = @site_group_id)
              AND (@operator_user_id IS NULL OR operator_user_id = @operator_user_id)
              AND (@fiscal_issuance_reference_id IS NULL OR fiscal_issuance_reference_id = @fiscal_issuance_reference_id)
              AND (@result_class IS NULL OR result_class = @result_class)
              AND (@correlation_id_filter IS NULL OR correlation_id = @correlation_id_filter)
            ORDER BY performed_at DESC, operator_action_log_id DESC
            LIMIT @limit
            OFFSET @offset;
            """;

        var items = new List<OperatorConsoleFiscalStatusViewAuditReportItemResult>();
        var totalCount = 0;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        AddParameters(command, query);

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadItem(reader));
            totalCount = reader.GetInt32(reader.GetOrdinal("total_count"));
        }

        return new OperatorConsoleFiscalStatusViewAuditReportResult(
            items,
            totalCount,
            query.Limit,
            query.Offset,
            query.CorrelationId);
    }

    private static void AddParameters(
        NpgsqlCommand command,
        OperatorConsoleFiscalStatusViewAuditReportQuery query)
    {
        command.Parameters.Add("action_reason_code", NpgsqlDbType.Varchar).Value = FiscalStatusViewActionCode;
        command.Parameters.Add("target_entity_type", NpgsqlDbType.Varchar).Value = FiscalIssuanceReferenceTargetEntityType;
        command.Parameters.Add("from", NpgsqlDbType.TimestampTz).Value = DbValue(query.From);
        command.Parameters.Add("to", NpgsqlDbType.TimestampTz).Value = DbValue(query.To);
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = DbValue(query.SiteId);
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = DbValue(query.SiteGroupId);
        command.Parameters.Add("operator_user_id", NpgsqlDbType.Uuid).Value = DbValue(query.OperatorUserId);
        command.Parameters.Add("fiscal_issuance_reference_id", NpgsqlDbType.Uuid).Value = DbValue(query.FiscalIssuanceReferenceId);
        command.Parameters.Add("result_class", NpgsqlDbType.Text).Value = DbValue(query.ResultClass);
        command.Parameters.Add("correlation_id_filter", NpgsqlDbType.Uuid).Value = DbValue(query.CorrelationIdFilter);
        command.Parameters.Add("limit", NpgsqlDbType.Integer).Value = query.Limit;
        command.Parameters.Add("offset", NpgsqlDbType.Integer).Value = query.Offset;
    }

    private static OperatorConsoleFiscalStatusViewAuditReportItemResult ReadItem(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(reader.GetOrdinal("operator_action_log_id")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("performed_at")),
            reader.GetString(reader.GetOrdinal("action_reason_code")),
            reader.GetString(reader.GetOrdinal("result_class")),
            reader.GetGuid(reader.GetOrdinal("operator_user_id")),
            GetNullableGuid(reader, "site_id"),
            GetNullableGuid(reader, "site_group_id"),
            reader.GetGuid(reader.GetOrdinal("fiscal_issuance_reference_id")),
            reader.GetGuid(reader.GetOrdinal("correlation_id")),
            GetNullableString(reader, "safe_denial_or_error_posture"),
            GetNullableString(reader, "source_module"));

    private static object DbValue(Guid? value) => value.HasValue ? value.Value : DBNull.Value;

    private static object DbValue(DateTimeOffset? value) => value.HasValue ? value.Value : DBNull.Value;

    private static object DbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static Guid? GetNullableGuid(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static string? GetNullableString(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
}
