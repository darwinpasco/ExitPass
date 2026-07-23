using System.Data;
using System.Text.Json;
using ExitPass.CentralPms.Application.OperatorConsole;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.OperatorConsole;

/// <summary>
/// PostgreSQL-backed read-only repository for Operator Console statutory discount validation drafts.
///
/// ExitPass v1.2 Invariants Enforced:
/// - Reads stored validation, policy, tariff, and application metadata only.
/// - Does not resolve policies, apply discounts, mutate tariff/payment/gate/coupon/provider/reconciliation state,
///   or upload evidence.
/// </summary>
public sealed class OperatorConsoleStatutoryDiscountReadRepository : IOperatorConsoleStatutoryDiscountReadRepository
{
    private readonly string _connectionString;

    /// <summary>
    /// Creates an Operator Console statutory discount read repository.
    /// </summary>
    public OperatorConsoleStatutoryDiscountReadRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    /// <inheritdoc />
    public async Task<OperatorConsoleStatutoryDiscountDraftQueueResult> ListDraftsAsync(
        OperatorConsoleStatutoryDiscountDraftQueueQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        const string sql = """
            SELECT
                sdv.statutory_discount_validation_id,
                sdv.parking_session_id,
                COALESCE(ps.ticket_number_masked, ps.vendor_session_ref) AS ticket_reference,
                ps.plate_number_masked,
                ps.site_id,
                s.site_name,
                sdv.entitlement_type::text,
                sdv.validation_status::text,
                sdv.evidence_required,
                sdv.evidence_captured AS evidence_required_satisfied,
                COALESCE(evidence_summary.evidence_count, 0)::int AS evidence_count,
                evidence_summary.latest_evidence_status,
                sdv.policy_resolution_basis::text,
                p.policy_code,
                p.policy_name,
                ROUND(COALESCE(sdv.gross_amount_at_validation, active_tariff.gross_amount) * 100)::bigint AS original_amount_minor_units,
                COALESCE(
                    latest_application.final_payable_amount_minor_units,
                    ROUND(COALESCE(sdv.net_amount_after_discount, active_tariff.net_amount) * 100)::bigint
                ) AS payable_amount_minor_units,
                COALESCE(sdv.currency_code, latest_application.currency_code, active_tariff.currency_code) AS currency_code,
                sdv.requested_at,
                sdv.requested_by_user_id,
                COALESCE(sdv.failure_reason_code, sdv.decision_reason_code) AS blocked_reason,
                COUNT(*) OVER() AS total_count
            FROM discounts.statutory_discount_validations AS sdv
            JOIN core.parking_sessions AS ps
              ON ps.parking_session_id = sdv.parking_session_id
            LEFT JOIN sites.sites AS s
              ON s.site_id = ps.site_id
            LEFT JOIN discounts.discount_policy_references AS p
              ON p.discount_policy_reference_id = COALESCE(
                    sdv.applied_policy_reference_id,
                    sdv.evaluated_policy_reference_id,
                    sdv.fallback_policy_reference_id)
            LEFT JOIN LATERAL (
                SELECT gross_amount, net_amount, currency_code
                FROM core.tariff_snapshots
                WHERE parking_session_id = sdv.parking_session_id
                  AND snapshot_status = 'ACTIVE'
                ORDER BY calculated_at DESC, tariff_snapshot_id DESC
                LIMIT 1
            ) AS active_tariff ON TRUE
            LEFT JOIN LATERAL (
                SELECT
                    ROUND(net_amount * 100)::bigint AS final_payable_amount_minor_units,
                    currency_code
                FROM core.tariff_snapshots
                WHERE statutory_discount_validation_id = sdv.statutory_discount_validation_id
                  AND statutory_discount_amount > 0
                ORDER BY calculated_at DESC, tariff_snapshot_id DESC
                LIMIT 1
            ) AS latest_application ON TRUE
            LEFT JOIN LATERAL (
                SELECT
                    COUNT(*)::int AS evidence_count,
                    (ARRAY_AGG(evidence_capture_status::text ORDER BY captured_at DESC, discount_evidence_reference_id DESC))[1] AS latest_evidence_status
                FROM discounts.discount_evidence_references
                WHERE statutory_discount_validation_id = sdv.statutory_discount_validation_id
                  AND purged_at IS NULL
            ) AS evidence_summary ON TRUE
            WHERE sdv.validation_channel = 'OPERATOR_ASSISTED'::discounts.statutory_discount_validations_channel_enum
              AND (@status IS NULL OR sdv.validation_status = @status::discounts.statutory_discount_validations_status_enum)
              AND (@entitlement_type IS NULL OR sdv.entitlement_type = @entitlement_type::discounts.statutory_entitlement_type_enum)
              AND (@site_id IS NULL OR ps.site_id = @site_id)
              AND (@created_from IS NULL OR sdv.requested_at >= @created_from)
              AND (@created_to IS NULL OR sdv.requested_at <= @created_to)
            ORDER BY sdv.requested_at DESC, sdv.statutory_discount_validation_id DESC
            LIMIT @limit
            OFFSET @offset;
            """;

        var limit = query.PageSize + 1;
        var offset = (query.Page - 1) * query.PageSize;
        var items = new List<OperatorConsoleStatutoryDiscountDraftQueueItemResult>();
        var totalCount = 0;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        AddQueueParameters(command, query, limit, offset);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (items.Count < query.PageSize)
            {
                items.Add(ReadQueueItem(reader));
            }

            totalCount = reader.GetInt32(reader.GetOrdinal("total_count"));
        }

        return new OperatorConsoleStatutoryDiscountDraftQueueResult(
            items,
            query.Page,
            query.PageSize,
            offset + items.Count < totalCount,
            query.CorrelationId);
    }

    /// <inheritdoc />
    public async Task<OperatorConsoleStatutoryDiscountDraftDetailResult?> GetDraftAsync(
        OperatorConsoleStatutoryDiscountDraftDetailQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        const string sql = """
            SELECT
                sdv.statutory_discount_validation_id,
                sdv.parking_session_id,
                COALESCE(ps.ticket_number_masked, ps.vendor_session_ref) AS ticket_reference,
                ps.plate_number_masked,
                ps.site_id,
                s.site_name,
                ps.site_group_id,
                sdv.entitlement_type::text,
                sdv.validation_status::text,
                decision_command.statutory_discount_decision_command_id,
                sdv.id_document_type,
                sdv.issuing_authority,
                sdv.id_expiry_date,
                sdv.masked_id_reference,
                sdv.requester_attestation,
                sdv.attestation_notes,
                sdv.evidence_required,
                sdv.evidence_captured,
                sdv.evidence_captured AS evidence_required_satisfied,
                COALESCE(evidence_summary.evidence_count, 0)::int AS evidence_count,
                evidence_summary.latest_evidence_status,
                sdv.requested_at,
                sdv.validated_at,
                sdv.requested_by_user_id,
                sdv.validated_by_user_id,
                sdv.decision_reason_code,
                sdv.failure_reason_code,
                sdv.policy_resolution_basis::text,
                COALESCE(sdv.applied_policy_reference_id, sdv.evaluated_policy_reference_id, sdv.fallback_policy_reference_id) AS statutory_discount_policy_id,
                NULL::uuid AS resolved_jurisdiction_id,
                p.policy_code,
                p.policy_name,
                COALESCE(p.local_ordinance_reference, p.national_law_reference) AS legal_basis_reference,
                p.local_ordinance_reference AS ordinance_reference,
                p.national_law_reference,
                p.policy_status::text AS verification_status,
                'STATUTORY_DISCOUNT_VAT_EXEMPT' AS benefit_type,
                NULL::integer AS free_duration_minutes,
                'APPLY_NATIONAL_STATUTORY_DISCOUNT' AS succeeding_hours_discount_rule,
                'VAT_EXCLUSIVE' AS discount_base_scope,
                'STATUTORY_FIRST' AS stacking_policy,
                jsonb_build_object(
                    'statutoryDiscountPolicyId', COALESCE(sdv.applied_policy_reference_id, sdv.evaluated_policy_reference_id, sdv.fallback_policy_reference_id),
                    'policyCode', p.policy_code,
                    'policyName', p.policy_name,
                    'entitlementType', sdv.entitlement_type::text,
                    'policyResolutionBasis', sdv.policy_resolution_basis::text,
                    'policyLevel', p.policy_level::text,
                    'policyType', p.policy_type::text,
                    'legalBasisReference', COALESCE(p.local_ordinance_reference, p.national_law_reference),
                    'ordinanceReference', p.local_ordinance_reference,
                    'nationalLawReference', p.national_law_reference,
                    'verificationStatus', p.policy_status::text,
                    'benefitType', 'STATUTORY_DISCOUNT_VAT_EXEMPT',
                    'freeDurationMinutes', NULL,
                    'succeedingHoursDiscountRule', 'APPLY_NATIONAL_STATUTORY_DISCOUNT',
                    'discountBaseScope', 'VAT_EXCLUSIVE',
                    'stackingPolicy', 'STATUTORY_FIRST',
                    'requiresEvidence', p.requires_evidence_capture
                )::text AS resolved_policy_snapshot_json,
                COALESCE(latest_application.original_tariff_snapshot_id, sdv.tariff_snapshot_id, active_tariff.tariff_snapshot_id) AS original_tariff_snapshot_id,
                latest_application.payable_basis_application_id,
                latest_application.application_status::text,
                latest_application.applied_tariff_snapshot_id,
                latest_application.vat_amount_minor_units,
                latest_application.vat_exclusive_amount_minor_units,
                ROUND(COALESCE(sdv.gross_amount_at_validation, active_tariff.gross_amount) * 100)::bigint AS original_amount_minor_units,
                COALESCE(
                    latest_application.statutory_discount_amount_minor_units,
                    ROUND(sdv.statutory_discount_amount * 100)::bigint
                ) AS statutory_discount_amount_minor_units,
                COALESCE(
                    latest_application.final_payable_amount_minor_units,
                    ROUND(COALESCE(sdv.net_amount_after_discount, active_tariff.net_amount) * 100)::bigint
                ) AS payable_amount_minor_units,
                latest_application.final_payable_amount_minor_units,
                COALESCE(sdv.currency_code, latest_application.currency_code, active_tariff.currency_code) AS currency_code
            FROM discounts.statutory_discount_validations AS sdv
            JOIN core.parking_sessions AS ps
              ON ps.parking_session_id = sdv.parking_session_id
            LEFT JOIN sites.sites AS s
              ON s.site_id = ps.site_id
            LEFT JOIN discounts.discount_policy_references AS p
              ON p.discount_policy_reference_id = COALESCE(
                    sdv.applied_policy_reference_id,
                    sdv.evaluated_policy_reference_id,
                    sdv.fallback_policy_reference_id)
            LEFT JOIN LATERAL (
                SELECT statutory_discount_decision_command_id
                FROM discounts.statutory_discount_decision_commands
                WHERE statutory_discount_validation_id = sdv.statutory_discount_validation_id
                  AND semantic_hash_source_version = 'statutory-discount-decision:sha256:v2'
                ORDER BY completed_at DESC NULLS LAST, created_at DESC, statutory_discount_decision_command_id DESC
                LIMIT 1
            ) AS decision_command ON TRUE
            LEFT JOIN LATERAL (
                SELECT tariff_snapshot_id, gross_amount, net_amount, currency_code
                FROM core.tariff_snapshots
                WHERE parking_session_id = sdv.parking_session_id
                  AND snapshot_status = 'ACTIVE'
                ORDER BY calculated_at DESC, tariff_snapshot_id DESC
                LIMIT 1
            ) AS active_tariff ON TRUE
            LEFT JOIN LATERAL (
                SELECT
                    app.original_tariff_snapshot_id,
                    app.statutory_discount_payable_basis_application_id AS payable_basis_application_id,
                    app.application_status::text AS application_status,
                    app.applied_tariff_snapshot_id,
                    app.vat_amount_minor_units,
                    app.vat_exclusive_amount_minor_units,
                    app.statutory_discount_amount_minor_units,
                    app.final_payable_amount_minor_units,
                    app.currency_code
                FROM discounts.statutory_discount_payable_basis_applications AS app
                WHERE app.statutory_discount_validation_id = sdv.statutory_discount_validation_id
                  AND app.application_status = 'APPLIED'::discounts.statutory_discount_payable_application_status_enum
                ORDER BY app.applied_at DESC NULLS LAST, app.updated_at DESC, app.statutory_discount_payable_basis_application_id DESC
                LIMIT 1
            ) AS latest_application ON TRUE
            LEFT JOIN LATERAL (
                SELECT
                    COUNT(*)::int AS evidence_count,
                    (ARRAY_AGG(evidence_capture_status::text ORDER BY captured_at DESC, discount_evidence_reference_id DESC))[1] AS latest_evidence_status
                FROM discounts.discount_evidence_references
                WHERE statutory_discount_validation_id = sdv.statutory_discount_validation_id
                  AND purged_at IS NULL
            ) AS evidence_summary ON TRUE
            WHERE sdv.statutory_discount_validation_id = @draft_id
              AND sdv.validation_channel = 'OPERATOR_ASSISTED'::discounts.statutory_discount_validations_channel_enum
            LIMIT 1;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.Add("draft_id", NpgsqlDbType.Uuid).Value = query.DraftId;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadDetail(reader);
    }

    /// <inheritdoc />
    public async Task<OperatorConsoleStatutoryDiscountAuditReportResult> ListAuditReportAsync(
        OperatorConsoleStatutoryDiscountAuditReportQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Design references:
        // docs/operator-console/OperatorConsole_Access_Readiness_API_Backend_Design_v1.md
        // docs/operator-console/OperatorConsole_Production_Readiness_Gap_Review_v1.md
        // Invariant: Operator Console audit/reporting is read-only and must not expose raw statutory evidence,
        // raw ID numbers, payment authority, gate authority, coupon authority, or reconciliation mutation.
        const string sql = """
            SELECT
                sdv.statutory_discount_validation_id,
                sdv.parking_session_id,
                COALESCE(ps.ticket_number_masked, ps.vendor_session_ref) AS ticket_reference,
                ps.plate_number_masked,
                ps.site_id,
                ps.site_group_id,
                sdv.entitlement_type::text,
                sdv.validation_status::text,
                sdv.evidence_required,
                sdv.evidence_captured,
                sdv.evidence_captured AS evidence_required_satisfied,
                COALESCE(evidence_summary.evidence_count, 0)::int AS evidence_count,
                evidence_summary.latest_evidence_status,
                latest_application.application_status,
                ROUND(COALESCE(sdv.gross_amount_at_validation, active_tariff.gross_amount) * 100)::bigint AS original_amount_minor_units,
                COALESCE(
                    latest_application.statutory_discount_amount_minor_units,
                    ROUND(sdv.statutory_discount_amount * 100)::bigint
                ) AS statutory_discount_amount_minor_units,
                COALESCE(
                    latest_application.final_payable_amount_minor_units,
                    ROUND(COALESCE(sdv.net_amount_after_discount, active_tariff.net_amount) * 100)::bigint
                ) AS final_payable_amount_minor_units,
                COALESCE(sdv.currency_code, latest_application.currency_code, active_tariff.currency_code) AS currency_code,
                sdv.requested_by_user_id,
                sdv.validated_by_user_id,
                sdv.requested_at,
                sdv.validated_at,
                sdv.correlation_id,
                p.policy_code,
                p.local_ordinance_reference AS ordinance_reference,
                COALESCE(p.local_ordinance_reference, p.national_law_reference) AS legal_basis_reference,
                latest_application.applied_tariff_snapshot_id,
                access_summary.access_evaluation_summary,
                access_summary.access_decision,
                COUNT(*) OVER() AS total_count
            FROM discounts.statutory_discount_validations AS sdv
            JOIN core.parking_sessions AS ps
              ON ps.parking_session_id = sdv.parking_session_id
            LEFT JOIN discounts.discount_policy_references AS p
              ON p.discount_policy_reference_id = COALESCE(
                    sdv.applied_policy_reference_id,
                    sdv.evaluated_policy_reference_id,
                    sdv.fallback_policy_reference_id)
            LEFT JOIN LATERAL (
                SELECT gross_amount, net_amount, currency_code
                FROM core.tariff_snapshots
                WHERE parking_session_id = sdv.parking_session_id
                  AND snapshot_status = 'ACTIVE'
                ORDER BY calculated_at DESC, tariff_snapshot_id DESC
                LIMIT 1
            ) AS active_tariff ON TRUE
            LEFT JOIN LATERAL (
                SELECT
                    applied_ts.tariff_snapshot_id AS applied_tariff_snapshot_id,
                    'APPLIED'::text AS application_status,
                    ROUND(applied_ts.statutory_discount_amount * 100)::bigint AS statutory_discount_amount_minor_units,
                    ROUND(applied_ts.net_amount * 100)::bigint AS final_payable_amount_minor_units,
                    applied_ts.currency_code
                FROM core.tariff_snapshots AS applied_ts
                WHERE applied_ts.statutory_discount_validation_id = sdv.statutory_discount_validation_id
                  AND applied_ts.statutory_discount_amount > 0
                ORDER BY applied_ts.calculated_at DESC, applied_ts.tariff_snapshot_id DESC
                LIMIT 1
            ) AS latest_application ON TRUE
            LEFT JOIN LATERAL (
                SELECT
                    COUNT(*)::int AS evidence_count,
                    (ARRAY_AGG(evidence_capture_status::text ORDER BY captured_at DESC, discount_evidence_reference_id DESC))[1] AS latest_evidence_status
                FROM discounts.discount_evidence_references
                WHERE statutory_discount_validation_id = sdv.statutory_discount_validation_id
                  AND purged_at IS NULL
            ) AS evidence_summary ON TRUE
            LEFT JOIN LATERAL (
                SELECT
                    action_status::text AS access_decision,
                    CONCAT(action_reason_code, ' / ', action_status::text, ' / ', performed_at::text) AS access_evaluation_summary
                FROM operations.operator_action_logs
                WHERE target_entity_id IN (sdv.parking_session_id, sdv.statutory_discount_validation_id)
                  AND action_type = 'CONTROLLED_RECHECK'::operations.operator_action_type_enum
                ORDER BY performed_at DESC, operator_action_log_id DESC
                LIMIT 1
            ) AS access_summary ON TRUE
            WHERE sdv.validation_channel = 'OPERATOR_ASSISTED'::discounts.statutory_discount_validations_channel_enum
              AND (@site_id IS NULL OR ps.site_id = @site_id)
              AND (@site_group_id IS NULL OR ps.site_group_id = @site_group_id)
              AND (@operator_user_id IS NULL OR sdv.requested_by_user_id = @operator_user_id OR sdv.validated_by_user_id = @operator_user_id)
              AND (@parking_session_id IS NULL OR sdv.parking_session_id = @parking_session_id)
              AND (@validation_status IS NULL OR sdv.validation_status = @validation_status::discounts.statutory_discount_validations_status_enum)
              AND (@evidence_status IS NULL OR evidence_summary.latest_evidence_status = @evidence_status)
              AND (@access_decision IS NULL OR access_summary.access_decision = @access_decision)
              AND (@from IS NULL OR sdv.requested_at >= @from)
              AND (@to IS NULL OR sdv.requested_at <= @to)
            ORDER BY sdv.requested_at DESC, sdv.statutory_discount_validation_id DESC
            LIMIT @limit
            OFFSET @offset;
            """;

        var items = new List<OperatorConsoleStatutoryDiscountAuditReportItemResult>();
        var totalCount = 0;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        AddAuditReportParameters(command, query);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadAuditReportItem(reader));
            totalCount = reader.GetInt32(reader.GetOrdinal("total_count"));
        }

        return new OperatorConsoleStatutoryDiscountAuditReportResult(
            items,
            totalCount,
            query.Limit,
            query.Offset,
            query.CorrelationId);
    }

    private static void AddQueueParameters(
        NpgsqlCommand command,
        OperatorConsoleStatutoryDiscountDraftQueueQuery query,
        int limit,
        int offset)
    {
        command.Parameters.Add("status", NpgsqlDbType.Text).Value = DbValue(query.Status);
        command.Parameters.Add("entitlement_type", NpgsqlDbType.Text).Value = DbValue(query.EntitlementType);
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = DbValue(query.SiteId);
        command.Parameters.Add("created_from", NpgsqlDbType.TimestampTz).Value = DbValue(query.CreatedFrom);
        command.Parameters.Add("created_to", NpgsqlDbType.TimestampTz).Value = DbValue(query.CreatedTo);
        command.Parameters.Add("limit", NpgsqlDbType.Integer).Value = limit;
        command.Parameters.Add("offset", NpgsqlDbType.Integer).Value = offset;
    }

    private static void AddAuditReportParameters(
        NpgsqlCommand command,
        OperatorConsoleStatutoryDiscountAuditReportQuery query)
    {
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = DbValue(query.SiteId);
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = DbValue(query.SiteGroupId);
        command.Parameters.Add("operator_user_id", NpgsqlDbType.Uuid).Value = DbValue(query.OperatorUserId);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = DbValue(query.ParkingSessionId);
        command.Parameters.Add("validation_status", NpgsqlDbType.Text).Value = DbValue(query.ValidationStatus);
        command.Parameters.Add("evidence_status", NpgsqlDbType.Text).Value = DbValue(query.EvidenceStatus);
        command.Parameters.Add("access_decision", NpgsqlDbType.Text).Value = DbValue(query.AccessDecision);
        command.Parameters.Add("from", NpgsqlDbType.TimestampTz).Value = DbValue(query.From);
        command.Parameters.Add("to", NpgsqlDbType.TimestampTz).Value = DbValue(query.To);
        command.Parameters.Add("limit", NpgsqlDbType.Integer).Value = query.Limit;
        command.Parameters.Add("offset", NpgsqlDbType.Integer).Value = query.Offset;
    }

    private static OperatorConsoleStatutoryDiscountDraftQueueItemResult ReadQueueItem(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid("statutory_discount_validation_id"),
            reader.GetGuid("parking_session_id"),
            GetNullableString(reader, "ticket_reference"),
            GetNullableString(reader, "plate_number_masked"),
            reader.GetGuid("site_id"),
            GetNullableString(reader, "site_name"),
            reader.GetString("entitlement_type"),
            reader.GetString("validation_status"),
            reader.GetBoolean(reader.GetOrdinal("evidence_required")),
            reader.GetBoolean(reader.GetOrdinal("evidence_required_satisfied")),
            reader.GetInt32(reader.GetOrdinal("evidence_count")),
            GetNullableString(reader, "latest_evidence_status"),
            GetNullableString(reader, "policy_resolution_basis"),
            GetNullableString(reader, "policy_code"),
            GetNullableString(reader, "policy_name"),
            GetNullableLong(reader, "original_amount_minor_units"),
            GetNullableLong(reader, "payable_amount_minor_units"),
            GetNullableString(reader, "currency_code"),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("requested_at")),
            GetNullableGuid(reader, "requested_by_user_id"),
            GetNullableString(reader, "blocked_reason"));

    private static OperatorConsoleStatutoryDiscountAuditReportItemResult ReadAuditReportItem(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid("statutory_discount_validation_id"),
            reader.GetGuid("parking_session_id"),
            GetNullableString(reader, "ticket_reference"),
            GetNullableString(reader, "plate_number_masked"),
            reader.GetGuid("site_id"),
            reader.GetGuid("site_group_id"),
            reader.GetString("entitlement_type"),
            reader.GetString("validation_status"),
            reader.GetBoolean(reader.GetOrdinal("evidence_required")),
            reader.GetBoolean(reader.GetOrdinal("evidence_captured")),
            reader.GetBoolean(reader.GetOrdinal("evidence_required_satisfied")),
            reader.GetInt32(reader.GetOrdinal("evidence_count")),
            GetNullableString(reader, "latest_evidence_status"),
            GetNullableString(reader, "application_status"),
            GetNullableLong(reader, "original_amount_minor_units"),
            GetNullableLong(reader, "statutory_discount_amount_minor_units"),
            GetNullableLong(reader, "final_payable_amount_minor_units"),
            GetNullableString(reader, "currency_code"),
            GetNullableGuid(reader, "requested_by_user_id"),
            GetNullableGuid(reader, "validated_by_user_id"),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("requested_at")),
            GetNullableDateTimeOffset(reader, "validated_at"),
            GetNullableGuid(reader, "correlation_id"),
            GetNullableString(reader, "policy_code"),
            GetNullableString(reader, "ordinance_reference"),
            GetNullableString(reader, "legal_basis_reference"),
            GetNullableGuid(reader, "applied_tariff_snapshot_id"),
            GetNullableString(reader, "access_evaluation_summary"));

    private static OperatorConsoleStatutoryDiscountDraftDetailResult ReadDetail(NpgsqlDataReader reader)
    {
        var status = reader.GetString("validation_status");
        var evidenceRequired = reader.GetBoolean(reader.GetOrdinal("evidence_required"));
        var evidenceCaptured = reader.GetBoolean(reader.GetOrdinal("evidence_captured"));
        var evidenceRequiredSatisfied = reader.GetBoolean(reader.GetOrdinal("evidence_required_satisfied"));
        var evidenceCount = reader.GetInt32(reader.GetOrdinal("evidence_count"));
        var latestEvidenceStatus = GetNullableString(reader, "latest_evidence_status");
        var requestedAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("requested_at"));
        var validatedAt = GetNullableDateTimeOffset(reader, "validated_at");
        var applicationStatus = GetNullableString(reader, "application_status");

        return new OperatorConsoleStatutoryDiscountDraftDetailResult(
            reader.GetGuid("statutory_discount_validation_id"),
            reader.GetGuid("parking_session_id"),
            GetNullableString(reader, "ticket_reference"),
            GetNullableString(reader, "plate_number_masked"),
            reader.GetGuid("site_id"),
            GetNullableString(reader, "site_name"),
            reader.GetGuid("site_group_id"),
            GetNullableString(reader, "entitlement_type"),
            status,
            GetNullableGuid(reader, "statutory_discount_decision_command_id"),
            GetNullableString(reader, "id_document_type"),
            GetNullableString(reader, "issuing_authority"),
            GetNullableDateOnly(reader, "id_expiry_date"),
            GetNullableString(reader, "masked_id_reference"),
            GetNullableBool(reader, "requester_attestation"),
            GetNullableString(reader, "attestation_notes"),
            evidenceRequired,
            evidenceCaptured,
            evidenceRequiredSatisfied,
            evidenceCount,
            latestEvidenceStatus,
            OperatorConsoleStatutoryDiscountEvidenceService.RequiredEvidenceTypes(
                GetNullableString(reader, "entitlement_type") ?? string.Empty,
                evidenceRequired),
            requestedAt,
            validatedAt,
            GetNullableGuid(reader, "requested_by_user_id"),
            GetNullableGuid(reader, "validated_by_user_id"),
            GetNullableString(reader, "decision_reason_code"),
            GetNullableString(reader, "failure_reason_code"),
            GetNullableString(reader, "policy_resolution_basis"),
            GetNullableGuid(reader, "statutory_discount_policy_id"),
            GetNullableGuid(reader, "resolved_jurisdiction_id"),
            GetNullableString(reader, "policy_code"),
            GetNullableString(reader, "policy_name"),
            GetNullableString(reader, "legal_basis_reference"),
            GetNullableString(reader, "ordinance_reference"),
            GetNullableString(reader, "national_law_reference"),
            GetNullableString(reader, "verification_status"),
            GetNullableString(reader, "benefit_type"),
            GetNullableInt(reader, "free_duration_minutes"),
            GetNullableString(reader, "succeeding_hours_discount_rule"),
            GetNullableString(reader, "discount_base_scope"),
            GetNullableString(reader, "stacking_policy"),
            GetNullableJson(reader, "resolved_policy_snapshot_json"),
            GetNullableGuid(reader, "original_tariff_snapshot_id"),
            GetNullableGuid(reader, "payable_basis_application_id"),
            applicationStatus,
            GetNullableGuid(reader, "applied_tariff_snapshot_id"),
            GetNullableLong(reader, "original_amount_minor_units"),
            GetNullableLong(reader, "vat_amount_minor_units"),
            GetNullableLong(reader, "vat_exclusive_amount_minor_units"),
            GetNullableLong(reader, "statutory_discount_amount_minor_units"),
            GetNullableLong(reader, "payable_amount_minor_units"),
            GetNullableLong(reader, "final_payable_amount_minor_units"),
            GetNullableString(reader, "currency_code"),
            BuildActivity(status, evidenceRequired, evidenceCaptured, requestedAt, validatedAt, applicationStatus));
    }

    private static IReadOnlyList<string> BuildActivity(
        string validationStatus,
        bool evidenceRequired,
        bool evidenceCaptured,
        DateTimeOffset requestedAt,
        DateTimeOffset? validatedAt,
        string? applicationStatus)
    {
        var activity = new List<string>
        {
            $"Draft requested at {requestedAt:O}.",
            evidenceRequired
                ? evidenceCaptured ? "Required evidence has been captured." : "Evidence upload is still pending."
                : "No evidence upload is required by the stored policy snapshot.",
            $"Current validation status is {validationStatus}."
        };

        if (validatedAt.HasValue)
        {
            activity.Add($"Decision recorded at {validatedAt.Value:O}.");
        }

        if (!string.IsNullOrWhiteSpace(applicationStatus))
        {
            activity.Add($"Payable-basis application status is {applicationStatus}.");
        }

        return activity;
    }

    private static JsonElement? GetNullableJson(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return JsonDocument.Parse(reader.GetString(ordinal)).RootElement.Clone();
    }

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

    private static int? GetNullableInt(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static long? GetNullableLong(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    private static bool? GetNullableBool(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetBoolean(ordinal);
    }

    private static DateOnly? GetNullableDateOnly(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return reader.GetFieldValue<DateOnly>(ordinal);
    }

    private static DateTimeOffset? GetNullableDateTimeOffset(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    private static object DbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static object DbValue(Guid? value) => value.HasValue ? value.Value : DBNull.Value;

    private static object DbValue(DateTimeOffset? value) => value.HasValue ? value.Value : DBNull.Value;
}
