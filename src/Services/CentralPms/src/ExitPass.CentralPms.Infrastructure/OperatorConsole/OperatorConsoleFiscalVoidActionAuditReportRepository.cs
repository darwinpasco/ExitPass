using System.Data;
using ExitPass.CentralPms.Application.OperatorConsole;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.OperatorConsole;

/// <summary>
/// PostgreSQL-backed read repository for fiscal void action audit review rows.
/// </summary>
public sealed class OperatorConsoleFiscalVoidActionAuditReportRepository :
    IOperatorConsoleFiscalVoidActionAuditReportRepository
{
    private const string FiscalVoidActionCode = OperatorConsoleActionCodes.VoidFiscalDocument;
    private const string FiscalIssuanceReferenceTargetEntityType = "FISCAL_ISSUANCE_REFERENCE";

    private readonly string _connectionString;

    /// <summary>
    /// Creates a fiscal void action audit review repository.
    /// </summary>
    public OperatorConsoleFiscalVoidActionAuditReportRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    /// <inheritdoc />
    public async Task<OperatorConsoleFiscalVoidActionAuditReportResult> ListAsync(
        OperatorConsoleFiscalVoidActionAuditReportQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Invariant: this report reads safe Operator Console action-log metadata and fiscal issuance reference
        // metadata only. It must not join raw fiscal payloads, POS Server payloads, payment provider payloads,
        // statutory evidence, raw payment callbacks, secrets, stack traces, or customer PII.
        const string sql = """
            WITH source AS (
                SELECT
                    log.operator_action_log_id,
                    log.performed_at,
                    log.action_reason_code,
                    COALESCE(
                        notes.payload ->> 'FiscalVoidResultClass',
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
                    ref.fiscal_document_number,
                    ref.pos_server_fiscal_document_id,
                    notes.payload ->> 'FiscalVoidReasonCode' AS reason_code,
                    notes.payload ->> 'FiscalVoidReasonText' AS reason_text,
                    log.correlation_id,
                    notes.payload ->> 'FiscalVoidOperatorActionRequestId' AS operator_action_request_id,
                    notes.payload ->> 'FiscalVoidPosServerResultClassification' AS pos_server_result_classification,
                    COALESCE(
                        notes.payload ->> 'FiscalVoidSafeErrorPosture',
                        notes.payload ->> 'FiscalStatusViewSafeErrorPosture',
                        notes.payload ->> 'FiscalVoidSafeErrorCode',
                        notes.payload ->> 'FiscalStatusViewSafeErrorCode',
                        notes.payload ->> 'Decision') AS safe_denial_or_error_posture,
                    COALESCE(
                        notes.payload ->> 'FiscalVoidSourceModule',
                        notes.payload ->> 'FiscalStatusViewSourceModule') AS source_module,
                    notes.payload ->> 'PaymentFinalityChanged' AS payment_finality_changed,
                    notes.payload ->> 'ExitAuthorizationIssued' AS exit_authorization_issued,
                    notes.payload ->> 'GateBehaviorTriggered' AS gate_behavior_triggered,
                    notes.payload ->> 'RefundOrReversalCreated' AS refund_or_reversal_created,
                    notes.payload ->> 'HikCentralCalled' AS hik_central_called,
                    notes.payload ->> 'PaymentProviderCalled' AS payment_provider_called,
                    notes.payload ->> 'RenderingGenerated' AS rendering_generated,
                    notes.payload ->> 'ReplacementFiscalDocumentCreated' AS replacement_fiscal_document_created,
                    notes.payload ->> 'NewFiscalNumberAllocated' AS new_fiscal_number_allocated,
                    notes.payload ->> 'FiscalSequenceChangedByCentralPms' AS fiscal_sequence_changed_by_central_pms
                FROM operations.operator_action_logs AS log
                CROSS JOIN LATERAL (
                    SELECT COALESCE(NULLIF(log.action_notes, ''), '{}')::jsonb AS payload
                ) AS notes
                LEFT JOIN core.fiscal_issuance_references AS ref
                    ON ref.fiscal_issuance_reference_id = log.target_entity_id
                   AND ref.is_active = true
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
                fiscal_document_number,
                pos_server_fiscal_document_id,
                reason_code,
                reason_text,
                correlation_id,
                operator_action_request_id,
                pos_server_result_classification,
                safe_denial_or_error_posture,
                source_module,
                payment_finality_changed,
                exit_authorization_issued,
                gate_behavior_triggered,
                refund_or_reversal_created,
                hik_central_called,
                payment_provider_called,
                rendering_generated,
                replacement_fiscal_document_created,
                new_fiscal_number_allocated,
                fiscal_sequence_changed_by_central_pms,
                COUNT(*) OVER() AS total_count
            FROM source
            WHERE (@from IS NULL OR performed_at >= @from)
              AND (@to IS NULL OR performed_at <= @to)
              AND (@site_id IS NULL OR site_id = @site_id)
              AND (@site_group_id IS NULL OR site_group_id = @site_group_id)
              AND (@operator_user_id IS NULL OR operator_user_id = @operator_user_id)
              AND (@fiscal_issuance_reference_id IS NULL OR fiscal_issuance_reference_id = @fiscal_issuance_reference_id)
              AND (@fiscal_document_number IS NULL OR fiscal_document_number = @fiscal_document_number)
              AND (@result_class IS NULL OR result_class = @result_class)
              AND (@correlation_id_filter IS NULL OR correlation_id = @correlation_id_filter)
            ORDER BY performed_at DESC, operator_action_log_id DESC
            LIMIT @limit
            OFFSET @offset;
            """;

        var items = new List<OperatorConsoleFiscalVoidActionAuditReportItemResult>();
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

        return new OperatorConsoleFiscalVoidActionAuditReportResult(
            items,
            totalCount,
            query.Limit,
            query.Offset,
            query.CorrelationId);
    }

    private static void AddParameters(
        NpgsqlCommand command,
        OperatorConsoleFiscalVoidActionAuditReportQuery query)
    {
        command.Parameters.Add("action_reason_code", NpgsqlDbType.Varchar).Value = FiscalVoidActionCode;
        command.Parameters.Add("target_entity_type", NpgsqlDbType.Varchar).Value = FiscalIssuanceReferenceTargetEntityType;
        command.Parameters.Add("from", NpgsqlDbType.TimestampTz).Value = DbValue(query.From);
        command.Parameters.Add("to", NpgsqlDbType.TimestampTz).Value = DbValue(query.To);
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = DbValue(query.SiteId);
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = DbValue(query.SiteGroupId);
        command.Parameters.Add("operator_user_id", NpgsqlDbType.Uuid).Value = DbValue(query.OperatorUserId);
        command.Parameters.Add("fiscal_issuance_reference_id", NpgsqlDbType.Uuid).Value = DbValue(query.FiscalIssuanceReferenceId);
        command.Parameters.Add("fiscal_document_number", NpgsqlDbType.Text).Value = DbValue(query.FiscalDocumentNumber);
        command.Parameters.Add("result_class", NpgsqlDbType.Text).Value = DbValue(query.ResultClass);
        command.Parameters.Add("correlation_id_filter", NpgsqlDbType.Uuid).Value = DbValue(query.CorrelationIdFilter);
        command.Parameters.Add("limit", NpgsqlDbType.Integer).Value = query.Limit;
        command.Parameters.Add("offset", NpgsqlDbType.Integer).Value = query.Offset;
    }

    private static OperatorConsoleFiscalVoidActionAuditReportItemResult ReadItem(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(reader.GetOrdinal("operator_action_log_id")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("performed_at")),
            reader.GetString(reader.GetOrdinal("action_reason_code")),
            reader.GetString(reader.GetOrdinal("result_class")),
            reader.GetGuid(reader.GetOrdinal("operator_user_id")),
            GetNullableGuid(reader, "site_id"),
            GetNullableGuid(reader, "site_group_id"),
            reader.GetGuid(reader.GetOrdinal("fiscal_issuance_reference_id")),
            GetNullableString(reader, "fiscal_document_number"),
            GetNullableGuid(reader, "pos_server_fiscal_document_id"),
            GetNullableString(reader, "reason_code"),
            GetNullableString(reader, "reason_text"),
            reader.GetGuid(reader.GetOrdinal("correlation_id")),
            GetNullableString(reader, "operator_action_request_id"),
            GetNullableString(reader, "pos_server_result_classification"),
            GetNullableString(reader, "safe_denial_or_error_posture"),
            GetNullableString(reader, "source_module"),
            GetNullableBool(reader, "payment_finality_changed"),
            GetNullableBool(reader, "exit_authorization_issued"),
            GetNullableBool(reader, "gate_behavior_triggered"),
            GetNullableBool(reader, "refund_or_reversal_created"),
            GetNullableBool(reader, "hik_central_called"),
            GetNullableBool(reader, "payment_provider_called"),
            GetNullableBool(reader, "rendering_generated"),
            GetNullableBool(reader, "replacement_fiscal_document_created"),
            GetNullableBool(reader, "new_fiscal_number_allocated"),
            GetNullableBool(reader, "fiscal_sequence_changed_by_central_pms"));

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

    private static bool? GetNullableBool(NpgsqlDataReader reader, string columnName)
    {
        var value = GetNullableString(reader, columnName);
        return bool.TryParse(value, out var parsed) ? parsed : null;
    }
}
