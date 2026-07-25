using System.Data;
using System.Text.Json;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.StatutoryDiscounts;

/// <summary>
/// PostgreSQL-backed service-channel statutory-discount review linkage repository.
/// </summary>
public sealed class PostgresStatutoryDiscountServiceChannelReviewRepository
    : IStatutoryDiscountServiceChannelReviewRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _connectionString;

    public PostgresStatutoryDiscountServiceChannelReviewRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    public async Task UpsertIntakeAsync(
        StatutoryDiscountServiceChannelReviewIntakeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        const string sql = """
            INSERT INTO operator_console.statutory_discount_service_channel_reviews (
                statutory_discount_decision_command_id,
                request_reference,
                parking_session_id,
                source_channel,
                site_id,
                site_group_id,
                ticket_reference,
                plate_number,
                entitlement_type,
                id_document_type,
                issuing_authority,
                expiry_date,
                masked_id_reference,
                evidence_references,
                requester_attestation,
                attestation_notes,
                reason_code,
                original_tariff_snapshot_id,
                review_status,
                intake_correlation_id,
                submitted_at,
                created_at,
                updated_at
            )
            VALUES (
                @statutory_discount_decision_command_id,
                @request_reference,
                @parking_session_id,
                @source_channel,
                @site_id,
                @site_group_id,
                @ticket_reference,
                @plate_number,
                @entitlement_type,
                @id_document_type,
                @issuing_authority,
                @expiry_date,
                @masked_id_reference,
                @evidence_references,
                @requester_attestation,
                @attestation_notes,
                @reason_code,
                @original_tariff_snapshot_id,
                'PENDING_REVIEW',
                @correlation_id,
                @submitted_at,
                now(),
                now()
            )
            ON CONFLICT (statutory_discount_decision_command_id) DO UPDATE
               SET request_reference = operator_console.statutory_discount_service_channel_reviews.request_reference,
                   ticket_reference = COALESCE(operator_console.statutory_discount_service_channel_reviews.ticket_reference, EXCLUDED.ticket_reference),
                   plate_number = COALESCE(operator_console.statutory_discount_service_channel_reviews.plate_number, EXCLUDED.plate_number),
                   evidence_references = CASE
                       WHEN operator_console.statutory_discount_service_channel_reviews.evidence_references = '[]'::jsonb
                       THEN EXCLUDED.evidence_references
                       ELSE operator_console.statutory_discount_service_channel_reviews.evidence_references
                   END,
                   intake_correlation_id = operator_console.statutory_discount_service_channel_reviews.intake_correlation_id,
                   updated_at = now();
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        AddIntakeParameters(dbCommand, command);
        await dbCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<StatutoryDiscountServiceChannelReviewQueueResult> ListAsync(
        StatutoryDiscountServiceChannelReviewQueueQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        const string sql = """
            SELECT
                r.statutory_discount_decision_command_id,
                r.parking_session_id,
                r.source_channel,
                r.site_id,
                r.site_group_id,
                r.ticket_reference,
                r.plate_number,
                r.entitlement_type,
                d.command_status,
                d.decision_result_status,
                r.review_status,
                d.evidence_required,
                d.evidence_recorded,
                COALESCE(d.original_tariff_snapshot_id, r.original_tariff_snapshot_id) AS original_tariff_snapshot_id,
                r.submitted_at,
                r.intake_correlation_id,
                COUNT(*) OVER() AS total_count
            FROM operator_console.statutory_discount_service_channel_reviews AS r
            JOIN discounts.statutory_discount_decision_commands AS d
              ON d.statutory_discount_decision_command_id = r.statutory_discount_decision_command_id
            WHERE d.command_status = 'AWAITING_REVIEW'
              AND d.decision_result_status = 'NOT_DECIDED'
              AND r.source_channel IN ('WEBPAY', 'ASSISTED_PAYMENT_TERMINAL')
              AND (@site_id IS NULL OR r.site_id = @site_id)
              AND (@site_group_id IS NULL OR r.site_group_id = @site_group_id)
              AND (@source_channel IS NULL OR r.source_channel = @source_channel)
              AND (@entitlement_type IS NULL OR r.entitlement_type = @entitlement_type)
              AND (@parking_session_id IS NULL OR r.parking_session_id = @parking_session_id)
              AND (@submitted_from IS NULL OR r.submitted_at >= @submitted_from)
              AND (@submitted_to IS NULL OR r.submitted_at <= @submitted_to)
            ORDER BY r.submitted_at ASC, r.statutory_discount_decision_command_id ASC
            LIMIT @limit
            OFFSET @offset;
            """;

        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? 25 : Math.Min(query.PageSize, 100);
        var limit = pageSize + 1;
        var offset = (page - 1) * pageSize;
        var items = new List<StatutoryDiscountServiceChannelReviewQueueItem>();
        var total = 0;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        AddListParameters(command, query, limit, offset);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (items.Count < pageSize)
            {
                items.Add(ReadQueueItem(reader));
            }

            total = reader.GetInt32(reader.GetOrdinal("total_count"));
        }

        return new StatutoryDiscountServiceChannelReviewQueueResult(
            items,
            page,
            pageSize,
            offset + items.Count < total,
            query.CorrelationId);
    }

    public async Task<StatutoryDiscountServiceChannelReviewDetail?> GetAsync(
        Guid statutoryDiscountDecisionCommandId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                r.*,
                d.command_status,
                d.decision_result_status,
                d.evidence_required,
                d.evidence_recorded,
                d.gross_amount_minor_units,
                d.vat_exclusive_amount_minor_units,
                d.vat_amount_minor_units,
                d.statutory_discount_amount_minor_units,
                d.net_payable_amount_minor_units,
                d.currency_code,
                COALESCE(d.original_tariff_snapshot_id, r.original_tariff_snapshot_id) AS effective_original_tariff_snapshot_id
            FROM operator_console.statutory_discount_service_channel_reviews AS r
            JOIN discounts.statutory_discount_decision_commands AS d
              ON d.statutory_discount_decision_command_id = r.statutory_discount_decision_command_id
            WHERE r.statutory_discount_decision_command_id = @statutory_discount_decision_command_id;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.Add("statutory_discount_decision_command_id", NpgsqlDbType.Uuid).Value = statutoryDiscountDecisionCommandId;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadDetail(reader, correlationId)
            : null;
    }

    public async Task<StatutoryDiscountServiceChannelValidationLinkage?> EnsureApprovedValidationLinkageAsync(
        Guid statutoryDiscountDecisionCommandId,
        Guid reviewerUserId,
        string? decisionReasonCode,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var source = await ReadReviewSourceForUpdateAsync(
                connection,
                transaction,
                statutoryDiscountDecisionCommandId,
                cancellationToken)
            .ConfigureAwait(false);
        if (source is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (source.StatutoryDiscountValidationId.HasValue)
        {
            var existing = await ReadValidationLinkageAsync(
                    connection,
                    transaction,
                    statutoryDiscountDecisionCommandId,
                    source.StatutoryDiscountValidationId.Value,
                    cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return existing;
        }

        var policy = await ResolvePolicyAsync(connection, transaction, source, cancellationToken)
            .ConfigureAwait(false);
        var tariff = await ResolveOriginalTariffAsync(connection, transaction, source, cancellationToken)
            .ConfigureAwait(false);
        var evidence = source.EvidenceReferences ?? [];
        if (policy is null ||
            tariff is null ||
            (policy.RequiresEvidence && evidence.Count == 0))
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        var validationId = await InsertApprovedValidationAsync(
                connection,
                transaction,
                source,
                policy,
                tariff,
                reviewerUserId,
                decisionReasonCode,
                correlationId,
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var evidenceReference in evidence)
        {
            await InsertCapturedEvidenceReferenceAsync(
                    connection,
                    transaction,
                    validationId,
                    evidenceReference,
                    reviewerUserId,
                    correlationId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await LinkReviewToValidationAsync(
                connection,
                transaction,
                statutoryDiscountDecisionCommandId,
                validationId,
                cancellationToken)
            .ConfigureAwait(false);

        var linkage = await ReadValidationLinkageAsync(
                connection,
                transaction,
                statutoryDiscountDecisionCommandId,
                validationId,
                cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return linkage;
    }

    public async Task<Guid?> GetValidationReviewerUserIdAsync(
        Guid statutoryDiscountValidationId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(
            """
            SELECT validated_by_user_id
            FROM discounts.statutory_discount_validations
            WHERE statutory_discount_validation_id = @statutory_discount_validation_id;
            """,
            connection)
        {
            CommandTimeout = 30
        };
        command.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value =
            statutoryDiscountValidationId;

        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is Guid reviewerUserId
            ? reviewerUserId
            : null;
    }

    public async Task<StatutoryDiscountServiceChannelReviewDetail> RecordReviewCompletionAsync(
        Guid statutoryDiscountDecisionCommandId,
        Guid reviewerUserId,
        Guid? operatorDeviceBindingId,
        Guid? operatorShiftId,
        Guid accessEvaluationId,
        string decision,
        string? decisionReasonCode,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE operator_console.statutory_discount_service_channel_reviews
               SET review_status = CASE
                       WHEN review_status = 'PENDING_REVIEW' AND @decision = 'APPROVE' THEN 'APPROVED'
                       WHEN review_status = 'PENDING_REVIEW' THEN 'REJECTED'
                       ELSE review_status
                   END,
                   reviewer_user_id = CASE
                       WHEN review_status = 'PENDING_REVIEW' THEN @reviewer_user_id
                       ELSE reviewer_user_id
                   END,
                   reviewer_operator_device_binding_id = CASE
                       WHEN review_status = 'PENDING_REVIEW' THEN @operator_device_binding_id
                       ELSE reviewer_operator_device_binding_id
                   END,
                   reviewer_operator_shift_id = CASE
                       WHEN review_status = 'PENDING_REVIEW' THEN @operator_shift_id
                       ELSE reviewer_operator_shift_id
                   END,
                   reviewer_access_evaluation_id = CASE
                       WHEN review_status = 'PENDING_REVIEW' THEN @access_evaluation_id
                       ELSE reviewer_access_evaluation_id
                   END,
                   reviewer_decision = CASE
                       WHEN review_status = 'PENDING_REVIEW' THEN @decision
                       ELSE reviewer_decision
                   END,
                   reviewer_decision_reason_code = CASE
                       WHEN review_status = 'PENDING_REVIEW' THEN @decision_reason_code
                       ELSE reviewer_decision_reason_code
                   END,
                   review_correlation_id = CASE
                       WHEN review_status = 'PENDING_REVIEW' THEN @correlation_id
                       ELSE review_correlation_id
                   END,
                   reviewed_at = CASE
                       WHEN review_status = 'PENDING_REVIEW' THEN COALESCE(reviewed_at, now())
                       ELSE reviewed_at
                   END,
                   updated_at = CASE
                       WHEN review_status = 'PENDING_REVIEW' THEN now()
                       ELSE updated_at
                   END
             WHERE statutory_discount_decision_command_id = @statutory_discount_decision_command_id
               AND (
                   review_status = 'PENDING_REVIEW'
                   OR reviewer_decision = @decision
               );
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.Add("statutory_discount_decision_command_id", NpgsqlDbType.Uuid).Value = statutoryDiscountDecisionCommandId;
        command.Parameters.Add("reviewer_user_id", NpgsqlDbType.Uuid).Value = reviewerUserId;
        AddNullable(command, "operator_device_binding_id", NpgsqlDbType.Uuid, operatorDeviceBindingId);
        AddNullable(command, "operator_shift_id", NpgsqlDbType.Uuid, operatorShiftId);
        command.Parameters.Add("access_evaluation_id", NpgsqlDbType.Uuid).Value = accessEvaluationId;
        command.Parameters.Add("decision", NpgsqlDbType.Varchar).Value = NormalizeRequired(decision);
        AddNullable(command, "decision_reason_code", NpgsqlDbType.Varchar, NormalizeOptional(decisionReasonCode));
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = correlationId;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return await GetAsync(statutoryDiscountDecisionCommandId, correlationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Service-channel review linkage was not found after completion.");
    }

    private static void AddIntakeParameters(NpgsqlCommand command, StatutoryDiscountServiceChannelReviewIntakeCommand source)
    {
        command.Parameters.Add("statutory_discount_decision_command_id", NpgsqlDbType.Uuid).Value = source.StatutoryDiscountDecisionCommandId;
        command.Parameters.Add("request_reference", NpgsqlDbType.Uuid).Value = source.RequestReference;
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = source.ParkingSessionId;
        command.Parameters.Add("source_channel", NpgsqlDbType.Varchar).Value = NormalizeRequired(source.SourceChannel);
        AddNullable(command, "site_id", NpgsqlDbType.Uuid, source.SiteId);
        AddNullable(command, "site_group_id", NpgsqlDbType.Uuid, source.SiteGroupId);
        AddNullable(command, "ticket_reference", NpgsqlDbType.Varchar, NormalizeOptional(source.TicketReference));
        AddNullable(command, "plate_number", NpgsqlDbType.Varchar, NormalizeOptional(source.PlateNumber));
        command.Parameters.Add("entitlement_type", NpgsqlDbType.Varchar).Value = NormalizeRequired(source.EntitlementType);
        AddNullable(command, "id_document_type", NpgsqlDbType.Varchar, NormalizeOptional(source.IdDocumentType));
        AddNullable(command, "issuing_authority", NpgsqlDbType.Varchar, NormalizeOptional(source.IssuingAuthority));
        AddNullable(command, "expiry_date", NpgsqlDbType.Date, source.ExpiryDate);
        AddNullable(command, "masked_id_reference", NpgsqlDbType.Varchar, source.MaskedIdReference);
        command.Parameters.Add("evidence_references", NpgsqlDbType.Jsonb).Value =
            JsonSerializer.Serialize(source.EvidenceReferences ?? [], JsonOptions);
        command.Parameters.Add("requester_attestation", NpgsqlDbType.Boolean).Value = source.RequesterAttestation;
        AddNullable(command, "attestation_notes", NpgsqlDbType.Varchar, source.AttestationNotes);
        AddNullable(command, "reason_code", NpgsqlDbType.Varchar, NormalizeOptional(source.ReasonCode));
        AddNullable(command, "original_tariff_snapshot_id", NpgsqlDbType.Uuid, source.OriginalTariffSnapshotId);
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = source.CorrelationId;
        command.Parameters.Add("submitted_at", NpgsqlDbType.TimestampTz).Value = source.SubmittedAt;
    }

    private static void AddListParameters(
        NpgsqlCommand command,
        StatutoryDiscountServiceChannelReviewQueueQuery query,
        int limit,
        int offset)
    {
        AddNullable(command, "site_id", NpgsqlDbType.Uuid, query.SiteId);
        AddNullable(command, "site_group_id", NpgsqlDbType.Uuid, query.SiteGroupId);
        AddNullable(command, "source_channel", NpgsqlDbType.Varchar, NormalizeOptional(query.SourceChannel));
        AddNullable(command, "entitlement_type", NpgsqlDbType.Varchar, NormalizeOptional(query.EntitlementType));
        AddNullable(command, "parking_session_id", NpgsqlDbType.Uuid, query.ParkingSessionId);
        AddNullable(command, "submitted_from", NpgsqlDbType.TimestampTz, query.SubmittedFrom);
        AddNullable(command, "submitted_to", NpgsqlDbType.TimestampTz, query.SubmittedTo);
        command.Parameters.Add("limit", NpgsqlDbType.Integer).Value = limit;
        command.Parameters.Add("offset", NpgsqlDbType.Integer).Value = offset;
    }

    private static StatutoryDiscountServiceChannelReviewQueueItem ReadQueueItem(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(reader.GetOrdinal("statutory_discount_decision_command_id")),
            reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            reader.GetString(reader.GetOrdinal("source_channel")),
            GetNullableGuid(reader, "site_id"),
            GetNullableGuid(reader, "site_group_id"),
            GetNullableString(reader, "ticket_reference"),
            GetNullableString(reader, "plate_number"),
            reader.GetString(reader.GetOrdinal("entitlement_type")),
            reader.GetString(reader.GetOrdinal("command_status")),
            reader.GetString(reader.GetOrdinal("decision_result_status")),
            reader.GetString(reader.GetOrdinal("review_status")),
            reader.GetBoolean(reader.GetOrdinal("evidence_required")),
            reader.GetBoolean(reader.GetOrdinal("evidence_recorded")),
            GetNullableGuid(reader, "original_tariff_snapshot_id"),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("submitted_at")),
            reader.GetGuid(reader.GetOrdinal("intake_correlation_id")));

    private static StatutoryDiscountServiceChannelReviewDetail ReadDetail(NpgsqlDataReader reader, Guid correlationId) =>
        new(
            reader.GetGuid(reader.GetOrdinal("statutory_discount_decision_command_id")),
            GetNullableGuid(reader, "statutory_discount_validation_id"),
            reader.GetGuid(reader.GetOrdinal("request_reference")),
            reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            reader.GetString(reader.GetOrdinal("source_channel")),
            GetNullableGuid(reader, "site_id"),
            GetNullableGuid(reader, "site_group_id"),
            GetNullableString(reader, "ticket_reference"),
            GetNullableString(reader, "plate_number"),
            reader.GetString(reader.GetOrdinal("entitlement_type")),
            reader.GetString(reader.GetOrdinal("command_status")),
            reader.GetString(reader.GetOrdinal("decision_result_status")),
            reader.GetString(reader.GetOrdinal("review_status")),
            GetNullableString(reader, "id_document_type"),
            GetNullableString(reader, "issuing_authority"),
            GetNullableDateOnly(reader, "expiry_date"),
            GetNullableString(reader, "masked_id_reference"),
            ReadEvidence(reader),
            reader.GetBoolean(reader.GetOrdinal("requester_attestation")),
            GetNullableString(reader, "attestation_notes"),
            GetNullableString(reader, "reason_code"),
            reader.GetBoolean(reader.GetOrdinal("evidence_required")),
            reader.GetBoolean(reader.GetOrdinal("evidence_recorded")),
            GetNullableGuid(reader, "effective_original_tariff_snapshot_id"),
            GetNullableInt64(reader, "gross_amount_minor_units"),
            GetNullableInt64(reader, "vat_exclusive_amount_minor_units"),
            GetNullableInt64(reader, "vat_amount_minor_units"),
            GetNullableInt64(reader, "statutory_discount_amount_minor_units"),
            GetNullableInt64(reader, "net_payable_amount_minor_units"),
            GetNullableString(reader, "currency_code")?.Trim(),
            GetNullableGuid(reader, "reviewer_user_id"),
            GetNullableGuid(reader, "reviewer_access_evaluation_id"),
            GetNullableString(reader, "reviewer_decision"),
            GetNullableString(reader, "reviewer_decision_reason_code"),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("submitted_at")),
            GetNullableDateTimeOffset(reader, "reviewed_at"),
            correlationId);

    private static async Task<ReviewSourceRow?> ReadReviewSourceForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid statutoryDiscountDecisionCommandId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                statutory_discount_decision_command_id,
                statutory_discount_validation_id,
                parking_session_id,
                site_id,
                site_group_id,
                entitlement_type,
                id_document_type,
                issuing_authority,
                expiry_date,
                masked_id_reference,
                evidence_references::text,
                requester_attestation,
                attestation_notes,
                original_tariff_snapshot_id,
                submitted_at
            FROM operator_console.statutory_discount_service_channel_reviews
            WHERE statutory_discount_decision_command_id = @statutory_discount_decision_command_id
            FOR UPDATE;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        command.Parameters.Add("statutory_discount_decision_command_id", NpgsqlDbType.Uuid).Value =
            statutoryDiscountDecisionCommandId;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var evidenceJson = reader.GetString(reader.GetOrdinal("evidence_references"));
        return new ReviewSourceRow(
            reader.GetGuid(reader.GetOrdinal("statutory_discount_decision_command_id")),
            GetNullableGuid(reader, "statutory_discount_validation_id"),
            reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            GetNullableGuid(reader, "site_id"),
            GetNullableGuid(reader, "site_group_id"),
            reader.GetString(reader.GetOrdinal("entitlement_type")),
            GetNullableString(reader, "id_document_type"),
            GetNullableString(reader, "issuing_authority"),
            GetNullableDateOnly(reader, "expiry_date"),
            GetNullableString(reader, "masked_id_reference"),
            JsonSerializer.Deserialize<IReadOnlyList<StatutoryDiscountServiceChannelReviewEvidenceFact>>(evidenceJson, JsonOptions) ?? [],
            reader.GetBoolean(reader.GetOrdinal("requester_attestation")),
            GetNullableString(reader, "attestation_notes"),
            GetNullableGuid(reader, "original_tariff_snapshot_id"),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("submitted_at")));
    }

    private static async Task<PolicyReferenceRow?> ResolvePolicyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ReviewSourceRow source,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                discount_policy_reference_id,
                fallback_policy_reference_id,
                policy_code,
                policy_name,
                national_law_reference,
                local_ordinance_reference,
                requires_evidence_capture,
                CASE
                    WHEN local_ordinance_reference IS NOT NULL THEN 'LOCAL_ORDINANCE_APPLIED'
                    ELSE 'NATIONAL_LAW_FALLBACK'
                END AS policy_resolution_basis,
                local_ordinance_reference IS NOT NULL AS local_ordinance_applied
            FROM discounts.discount_policy_references
            WHERE entitlement_type = @entitlement_type::discounts.statutory_entitlement_type_enum
              AND policy_status = 'ACTIVE'::discounts.discount_policy_status_enum
              AND (
                    site_id = @site_id
                 OR site_group_id = @site_group_id
                 OR (site_id IS NULL AND site_group_id IS NULL)
              )
            ORDER BY
                CASE
                    WHEN site_id = @site_id THEN 0
                    WHEN site_group_id = @site_group_id THEN 1
                    ELSE 2
                END,
                precedence_rank ASC,
                effective_from DESC,
                discount_policy_reference_id ASC
            LIMIT 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        command.Parameters.Add("entitlement_type", NpgsqlDbType.Text).Value = NormalizeRequired(source.EntitlementType);
        AddNullable(command, "site_id", NpgsqlDbType.Uuid, source.SiteId);
        AddNullable(command, "site_group_id", NpgsqlDbType.Uuid, source.SiteGroupId);

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new PolicyReferenceRow(
            reader.GetGuid(reader.GetOrdinal("discount_policy_reference_id")),
            GetNullableGuid(reader, "fallback_policy_reference_id"),
            reader.GetString(reader.GetOrdinal("policy_code")),
            GetNullableString(reader, "policy_name"),
            GetNullableString(reader, "national_law_reference"),
            GetNullableString(reader, "local_ordinance_reference"),
            reader.GetBoolean(reader.GetOrdinal("requires_evidence_capture")),
            reader.GetString(reader.GetOrdinal("policy_resolution_basis")),
            reader.GetBoolean(reader.GetOrdinal("local_ordinance_applied")));
    }

    private static async Task<TariffSnapshotRow?> ResolveOriginalTariffAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ReviewSourceRow source,
        CancellationToken cancellationToken)
    {
        var sql = source.OriginalTariffSnapshotId.HasValue
            ? """
                SELECT tariff_snapshot_id, parking_session_id, currency_code, gross_amount
                FROM core.tariff_snapshots
                WHERE tariff_snapshot_id = @tariff_snapshot_id
                  AND parking_session_id = @parking_session_id
                LIMIT 1;
                """
            : """
                SELECT tariff_snapshot_id, parking_session_id, currency_code, gross_amount
                FROM core.tariff_snapshots
                WHERE parking_session_id = @parking_session_id
                  AND snapshot_status = 'ACTIVE'::core.tariff_snapshot_status_enum
                ORDER BY calculated_at DESC, tariff_snapshot_id DESC
                LIMIT 1;
                """;

        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = source.ParkingSessionId;
        command.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value =
            source.OriginalTariffSnapshotId ?? Guid.Empty;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new TariffSnapshotRow(
            reader.GetGuid(reader.GetOrdinal("tariff_snapshot_id")),
            reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            reader.GetString(reader.GetOrdinal("currency_code")),
            reader.GetDecimal(reader.GetOrdinal("gross_amount")));
    }

    private static async Task<Guid> InsertApprovedValidationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ReviewSourceRow source,
        PolicyReferenceRow policy,
        TariffSnapshotRow tariff,
        Guid reviewerUserId,
        string? decisionReasonCode,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO discounts.statutory_discount_validations (
                parking_session_id,
                tariff_snapshot_id,
                entitlement_type,
                id_document_type,
                issuing_authority,
                id_expiry_date,
                masked_id_reference,
                policy_resolution_basis,
                local_ordinance_applied,
                national_law_fallback_applied,
                validation_channel,
                validation_status,
                currency_code,
                evidence_required,
                evidence_captured,
                decision_reason_code,
                requester_attestation,
                attestation_notes,
                evaluated_policy_reference_id,
                applied_policy_reference_id,
                fallback_policy_reference_id,
                requested_at,
                validated_at,
                requested_by_user_id,
                validated_by_user_id,
                correlation_id,
                created_by_user_id,
                updated_by_user_id
            )
            VALUES (
                @parking_session_id,
                @tariff_snapshot_id,
                @entitlement_type::discounts.statutory_entitlement_type_enum,
                @id_document_type,
                @issuing_authority,
                @id_expiry_date,
                @masked_id_reference,
                @policy_resolution_basis::discounts.policy_resolution_basis_enum,
                @local_ordinance_applied,
                @national_law_fallback_applied,
                'OPERATOR_ASSISTED'::discounts.statutory_discount_validations_channel_enum,
                'APPROVED'::discounts.statutory_discount_validations_status_enum,
                @currency_code,
                @evidence_required,
                @evidence_captured,
                @decision_reason_code,
                @requester_attestation,
                @attestation_notes,
                @policy_reference_id,
                @policy_reference_id,
                @fallback_policy_reference_id,
                @requested_at,
                now(),
                NULL,
                @validated_by_user_id,
                @correlation_id,
                @created_by_user_id,
                @updated_by_user_id
            )
            RETURNING statutory_discount_validation_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = source.ParkingSessionId;
        command.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value = tariff.TariffSnapshotId;
        command.Parameters.Add("entitlement_type", NpgsqlDbType.Text).Value = NormalizeRequired(source.EntitlementType);
        AddNullable(command, "id_document_type", NpgsqlDbType.Varchar, NormalizeOptional(source.IdDocumentType));
        AddNullable(command, "issuing_authority", NpgsqlDbType.Varchar, NormalizeOptional(source.IssuingAuthority));
        AddNullable(command, "id_expiry_date", NpgsqlDbType.Date, source.ExpiryDate);
        AddNullable(command, "masked_id_reference", NpgsqlDbType.Varchar, source.MaskedIdReference);
        command.Parameters.Add("policy_resolution_basis", NpgsqlDbType.Text).Value = policy.PolicyResolutionBasis;
        command.Parameters.Add("local_ordinance_applied", NpgsqlDbType.Boolean).Value = policy.LocalOrdinanceApplied;
        command.Parameters.Add("national_law_fallback_applied", NpgsqlDbType.Boolean).Value = !policy.LocalOrdinanceApplied;
        command.Parameters.Add("currency_code", NpgsqlDbType.Varchar).Value = tariff.CurrencyCode;
        command.Parameters.Add("evidence_required", NpgsqlDbType.Boolean).Value = policy.RequiresEvidence;
        command.Parameters.Add("evidence_captured", NpgsqlDbType.Boolean).Value = policy.RequiresEvidence;
        AddNullable(command, "decision_reason_code", NpgsqlDbType.Varchar, NormalizeOptional(decisionReasonCode));
        command.Parameters.Add("requester_attestation", NpgsqlDbType.Boolean).Value = source.RequesterAttestation;
        AddNullable(command, "attestation_notes", NpgsqlDbType.Varchar, source.AttestationNotes);
        command.Parameters.Add("policy_reference_id", NpgsqlDbType.Uuid).Value = policy.PolicyReferenceId;
        AddNullable(command, "fallback_policy_reference_id", NpgsqlDbType.Uuid, policy.FallbackPolicyReferenceId);
        command.Parameters.Add("requested_at", NpgsqlDbType.TimestampTz).Value = source.SubmittedAt;
        command.Parameters.Add("validated_by_user_id", NpgsqlDbType.Uuid).Value = reviewerUserId;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = correlationId;
        command.Parameters.Add("created_by_user_id", NpgsqlDbType.Uuid).Value = reviewerUserId;
        command.Parameters.Add("updated_by_user_id", NpgsqlDbType.Uuid).Value = reviewerUserId;

        return (Guid)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Service-channel statutory validation insert did not return an ID."));
    }

    private static async Task InsertCapturedEvidenceReferenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid validationId,
        StatutoryDiscountServiceChannelReviewEvidenceFact evidence,
        Guid reviewerUserId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO discounts.discount_evidence_references (
                statutory_discount_validation_id,
                evidence_type,
                evidence_storage_type,
                evidence_storage_ref,
                evidence_hash,
                evidence_capture_status,
                access_classification,
                redaction_status,
                retention_policy_code,
                retention_expires_at,
                captured_at,
                captured_by_user_id,
                correlation_id,
                created_by_user_id,
                updated_by_user_id
            )
            VALUES (
                @statutory_discount_validation_id,
                @evidence_type::discounts.discount_evidence_type_enum,
                'EXTERNAL_REFERENCE'::discounts.evidence_storage_type_enum,
                @evidence_storage_ref,
                NULL,
                'CAPTURED'::discounts.evidence_capture_status_enum,
                'RESTRICTED'::discounts.evidence_access_classification_enum,
                'NOT_REDACTED'::discounts.evidence_redaction_status_enum,
                'OPERATOR_CONSOLE_STATUTORY_DISCOUNT_EVIDENCE_METADATA_V1',
                NULL,
                now(),
                @captured_by_user_id,
                @correlation_id,
                @created_by_user_id,
                @updated_by_user_id
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        command.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = validationId;
        command.Parameters.Add("evidence_type", NpgsqlDbType.Text).Value = ToDatabaseEvidenceType(evidence.EvidenceType);
        AddNullable(command, "evidence_storage_ref", NpgsqlDbType.Varchar, NormalizeOptional(evidence.StorageReference));
        command.Parameters.Add("captured_by_user_id", NpgsqlDbType.Uuid).Value = reviewerUserId;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = correlationId;
        command.Parameters.Add("created_by_user_id", NpgsqlDbType.Uuid).Value = reviewerUserId;
        command.Parameters.Add("updated_by_user_id", NpgsqlDbType.Uuid).Value = reviewerUserId;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task LinkReviewToValidationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid statutoryDiscountDecisionCommandId,
        Guid validationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE operator_console.statutory_discount_service_channel_reviews
               SET statutory_discount_validation_id = @statutory_discount_validation_id,
                   updated_at = now()
             WHERE statutory_discount_decision_command_id = @statutory_discount_decision_command_id
               AND statutory_discount_validation_id IS NULL;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        command.Parameters.Add("statutory_discount_decision_command_id", NpgsqlDbType.Uuid).Value =
            statutoryDiscountDecisionCommandId;
        command.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = validationId;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<StatutoryDiscountServiceChannelValidationLinkage?> ReadValidationLinkageAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid statutoryDiscountDecisionCommandId,
        Guid validationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                r.statutory_discount_decision_command_id,
                sdv.statutory_discount_validation_id,
                sdv.parking_session_id,
                sdv.entitlement_type::text,
                COALESCE(sdv.tariff_snapshot_id, r.original_tariff_snapshot_id, ts.tariff_snapshot_id) AS original_tariff_snapshot_id,
                COALESCE(sdv.applied_policy_reference_id, sdv.evaluated_policy_reference_id) AS applied_policy_reference_id,
                sdv.fallback_policy_reference_id,
                sdv.policy_resolution_basis::text,
                sdv.local_ordinance_applied,
                ROUND(ts.gross_amount * 100)::bigint AS gross_amount_minor_units,
                ts.currency_code,
                'STATUTORY_DISCOUNT_VAT_EXEMPT' AS benefit_type,
                'VAT_EXCLUSIVE' AS discount_base_scope
            FROM operator_console.statutory_discount_service_channel_reviews AS r
            JOIN discounts.statutory_discount_validations AS sdv
              ON sdv.statutory_discount_validation_id = @statutory_discount_validation_id
            JOIN core.tariff_snapshots AS ts
              ON ts.tariff_snapshot_id = COALESCE(sdv.tariff_snapshot_id, r.original_tariff_snapshot_id)
            WHERE r.statutory_discount_decision_command_id = @statutory_discount_decision_command_id
              AND r.statutory_discount_validation_id = sdv.statutory_discount_validation_id
            LIMIT 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        command.Parameters.Add("statutory_discount_decision_command_id", NpgsqlDbType.Uuid).Value =
            statutoryDiscountDecisionCommandId;
        command.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = validationId;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new StatutoryDiscountServiceChannelValidationLinkage(
            reader.GetGuid(reader.GetOrdinal("statutory_discount_decision_command_id")),
            reader.GetGuid(reader.GetOrdinal("statutory_discount_validation_id")),
            reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            reader.GetString(reader.GetOrdinal("entitlement_type")),
            reader.GetGuid(reader.GetOrdinal("original_tariff_snapshot_id")),
            GetNullableGuid(reader, "applied_policy_reference_id"),
            GetNullableGuid(reader, "fallback_policy_reference_id"),
            reader.GetString(reader.GetOrdinal("policy_resolution_basis")),
            reader.GetBoolean(reader.GetOrdinal("local_ordinance_applied")),
            reader.GetInt64(reader.GetOrdinal("gross_amount_minor_units")),
            reader.GetString(reader.GetOrdinal("currency_code")),
            reader.GetString(reader.GetOrdinal("benefit_type")),
            reader.GetString(reader.GetOrdinal("discount_base_scope")));
    }

    private static IReadOnlyList<StatutoryDiscountServiceChannelReviewEvidenceFact> ReadEvidence(NpgsqlDataReader reader)
    {
        var json = reader.GetString(reader.GetOrdinal("evidence_references"));
        return JsonSerializer.Deserialize<IReadOnlyList<StatutoryDiscountServiceChannelReviewEvidenceFact>>(json, JsonOptions)
            ?? [];
    }

    private static string NormalizeRequired(string value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static void AddNullable<T>(NpgsqlCommand command, string name, NpgsqlDbType type, T? value)
    {
        var parameter = command.Parameters.Add(name, type);
        parameter.Value = value is null ? DBNull.Value : value;
    }

    private static string? GetNullableString(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static Guid? GetNullableGuid(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static long? GetNullableInt64(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    private static DateOnly? GetNullableDateOnly(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : DateOnly.FromDateTime(reader.GetDateTime(ordinal));
    }

    private static DateTimeOffset? GetNullableDateTimeOffset(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    private static string ToDatabaseEvidenceType(string evidenceType) =>
        NormalizeRequired(evidenceType) == "OTHER_SUPPORTING_DOCUMENT"
            ? "SUPPORTING_DOCUMENT"
            : NormalizeRequired(evidenceType);

    private sealed record ReviewSourceRow(
        Guid StatutoryDiscountDecisionCommandId,
        Guid? StatutoryDiscountValidationId,
        Guid ParkingSessionId,
        Guid? SiteId,
        Guid? SiteGroupId,
        string EntitlementType,
        string? IdDocumentType,
        string? IssuingAuthority,
        DateOnly? ExpiryDate,
        string? MaskedIdReference,
        IReadOnlyList<StatutoryDiscountServiceChannelReviewEvidenceFact> EvidenceReferences,
        bool RequesterAttestation,
        string? AttestationNotes,
        Guid? OriginalTariffSnapshotId,
        DateTimeOffset SubmittedAt);

    private sealed record PolicyReferenceRow(
        Guid PolicyReferenceId,
        Guid? FallbackPolicyReferenceId,
        string PolicyCode,
        string? PolicyName,
        string? NationalLawReference,
        string? LocalOrdinanceReference,
        bool RequiresEvidence,
        string PolicyResolutionBasis,
        bool LocalOrdinanceApplied);

    private sealed record TariffSnapshotRow(
        Guid TariffSnapshotId,
        Guid ParkingSessionId,
        string CurrencyCode,
        decimal GrossAmount);
}
