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
            LEFT JOIN discounts.statutory_discount_policy_registry AS p
              ON p.statutory_discount_policy_id = sdv.statutory_discount_policy_id
            LEFT JOIN LATERAL (
                SELECT gross_amount, net_amount, currency_code
                FROM core.tariff_snapshots
                WHERE parking_session_id = sdv.parking_session_id
                  AND snapshot_status = 'ACTIVE'
                ORDER BY calculated_at DESC, tariff_snapshot_id DESC
                LIMIT 1
            ) AS active_tariff ON TRUE
            LEFT JOIN LATERAL (
                SELECT final_payable_amount_minor_units, currency_code
                FROM discounts.statutory_discount_payable_basis_applications
                WHERE statutory_discount_validation_id = sdv.statutory_discount_validation_id
                ORDER BY created_at DESC, statutory_discount_payable_basis_application_id DESC
                LIMIT 1
            ) AS latest_application ON TRUE
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
                sdv.evidence_required,
                sdv.evidence_captured,
                sdv.requested_at,
                sdv.validated_at,
                sdv.requested_by_user_id,
                sdv.validated_by_user_id,
                sdv.decision_reason_code,
                sdv.failure_reason_code,
                sdv.policy_resolution_basis::text,
                sdv.statutory_discount_policy_id,
                sdv.resolved_jurisdiction_id,
                p.policy_code,
                p.policy_name,
                p.legal_basis_reference,
                p.ordinance_reference,
                p.national_law_reference,
                p.verification_status::text,
                p.benefit_type::text,
                p.free_duration_minutes,
                p.succeeding_hours_discount_rule::text,
                p.discount_base_scope::text,
                p.stacking_policy::text,
                sdv.resolved_policy_snapshot_json,
                latest_application.original_tariff_snapshot_id,
                latest_application.statutory_discount_payable_basis_application_id,
                latest_application.application_status::text,
                ROUND(COALESCE(sdv.gross_amount_at_validation, active_tariff.gross_amount) * 100)::bigint AS original_amount_minor_units,
                COALESCE(
                    latest_application.statutory_discount_amount_minor_units,
                    ROUND(sdv.statutory_discount_amount * 100)::bigint
                ) AS statutory_discount_amount_minor_units,
                COALESCE(
                    latest_application.final_payable_amount_minor_units,
                    ROUND(COALESCE(sdv.net_amount_after_discount, active_tariff.net_amount) * 100)::bigint
                ) AS payable_amount_minor_units,
                COALESCE(sdv.currency_code, latest_application.currency_code, active_tariff.currency_code) AS currency_code
            FROM discounts.statutory_discount_validations AS sdv
            JOIN core.parking_sessions AS ps
              ON ps.parking_session_id = sdv.parking_session_id
            LEFT JOIN sites.sites AS s
              ON s.site_id = ps.site_id
            LEFT JOIN discounts.statutory_discount_policy_registry AS p
              ON p.statutory_discount_policy_id = sdv.statutory_discount_policy_id
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
                    statutory_discount_payable_basis_application_id,
                    original_tariff_snapshot_id,
                    application_status,
                    statutory_discount_amount_minor_units,
                    final_payable_amount_minor_units,
                    currency_code
                FROM discounts.statutory_discount_payable_basis_applications
                WHERE statutory_discount_validation_id = sdv.statutory_discount_validation_id
                ORDER BY created_at DESC, statutory_discount_payable_basis_application_id DESC
                LIMIT 1
            ) AS latest_application ON TRUE
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
            GetNullableString(reader, "policy_resolution_basis"),
            GetNullableString(reader, "policy_code"),
            GetNullableString(reader, "policy_name"),
            GetNullableLong(reader, "original_amount_minor_units"),
            GetNullableLong(reader, "payable_amount_minor_units"),
            GetNullableString(reader, "currency_code"),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("requested_at")),
            GetNullableGuid(reader, "requested_by_user_id"),
            GetNullableString(reader, "blocked_reason"));

    private static OperatorConsoleStatutoryDiscountDraftDetailResult ReadDetail(NpgsqlDataReader reader)
    {
        var status = reader.GetString("validation_status");
        var evidenceRequired = reader.GetBoolean(reader.GetOrdinal("evidence_required"));
        var evidenceCaptured = reader.GetBoolean(reader.GetOrdinal("evidence_captured"));
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
            evidenceRequired,
            evidenceCaptured,
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
            GetNullableGuid(reader, "statutory_discount_payable_basis_application_id"),
            applicationStatus,
            GetNullableLong(reader, "original_amount_minor_units"),
            GetNullableLong(reader, "statutory_discount_amount_minor_units"),
            GetNullableLong(reader, "payable_amount_minor_units"),
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

    private static DateTimeOffset? GetNullableDateTimeOffset(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    private static object DbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static object DbValue(Guid? value) => value.HasValue ? value.Value : DBNull.Value;

    private static object DbValue(DateTimeOffset? value) => value.HasValue ? value.Value : DBNull.Value;
}
