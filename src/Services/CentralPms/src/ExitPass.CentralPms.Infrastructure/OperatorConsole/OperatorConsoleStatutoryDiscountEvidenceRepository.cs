using ExitPass.CentralPms.Application.OperatorConsole;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.OperatorConsole;

/// <summary>
/// PostgreSQL-backed metadata-only evidence repository for Operator Console statutory discount drafts.
///
/// ExitPass v1.2 Invariants Enforced:
/// - Writes only evidence metadata in discounts.discount_evidence_references and evidence_captured state.
/// - Does not store raw evidence bytes, OCR results, ID numbers, payment, provider, gate, coupon, or reconciliation data.
/// </summary>
public sealed class OperatorConsoleStatutoryDiscountEvidenceRepository
    : IOperatorConsoleStatutoryDiscountEvidenceRepository
{
    private const string EvidenceStorageType = "EXTERNAL_REFERENCE";
    private const string EvidenceCaptureStatus = "CAPTURED";
    private const string EvidenceAccessClassification = "RESTRICTED";
    private const string EvidenceRedactionStatus = "NOT_REDACTED";
    private const string EvidenceRetentionPolicyCode = "OPERATOR_CONSOLE_STATUTORY_DISCOUNT_EVIDENCE_METADATA_V1";

    private readonly string _connectionString;

    /// <summary>
    /// Creates a metadata-only statutory discount evidence repository.
    /// </summary>
    public OperatorConsoleStatutoryDiscountEvidenceRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    /// <inheritdoc />
    public async Task<OperatorConsoleStatutoryDiscountEvidenceDraftContext?> GetDraftContextAsync(
        Guid draftId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                sdv.statutory_discount_validation_id,
                sdv.parking_session_id,
                ps.site_id,
                ps.site_group_id,
                sdv.entitlement_type::text,
                sdv.validation_status::text,
                sdv.evidence_required,
                sdv.evidence_captured
            FROM discounts.statutory_discount_validations AS sdv
            JOIN core.parking_sessions AS ps
              ON ps.parking_session_id = sdv.parking_session_id
            WHERE sdv.statutory_discount_validation_id = @draft_id
              AND sdv.validation_channel = 'OPERATOR_ASSISTED'::discounts.statutory_discount_validations_channel_enum
            LIMIT 1;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("draft_id", NpgsqlDbType.Uuid).Value = draftId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new OperatorConsoleStatutoryDiscountEvidenceDraftContext(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetBoolean(6),
            reader.GetBoolean(7));
    }

    /// <inheritdoc />
    public async Task<OperatorConsoleStatutoryDiscountEvidenceCaptureResult> CaptureAsync(
        OperatorConsoleStatutoryDiscountEvidencePersistenceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var current = await ReadDraftForUpdateAsync(connection, transaction, command.DraftId, cancellationToken);
            if (current is null)
            {
                throw new InvalidOperationException("Statutory discount draft was not found during evidence capture.");
            }

            var existingEvidenceId = await FindReusableEvidenceReferenceAsync(
                connection,
                transaction,
                command.DraftId,
                command.EvidenceType,
                cancellationToken);

            var evidence = existingEvidenceId.HasValue
                ? await UpdateEvidenceReferenceAsync(connection, transaction, command, existingEvidenceId.Value, cancellationToken)
                : await InsertEvidenceReferenceAsync(connection, transaction, command, cancellationToken);

            var evidenceRequiredSatisfied = await MarkDraftEvidenceCapturedAsync(
                connection,
                transaction,
                command,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new OperatorConsoleStatutoryDiscountEvidenceCaptureResult(
                evidence.EvidenceId,
                command.DraftId,
                evidence.EvidenceType,
                command.CaptureMethod,
                command.FileName,
                command.ContentType,
                command.SizeBytes,
                evidence.StorageReference,
                command.ReferenceNumberMasked,
                evidence.CapturedByUserId,
                evidence.CapturedAt,
                evidence.RedactionStatus,
                ToVerificationStatus(evidence.CaptureStatus),
                evidenceRequiredSatisfied,
                current.ValidationStatus,
                AccessAllowed: true,
                ErrorCode: null,
                command.CorrelationId);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<OperatorConsoleStatutoryDiscountEvidenceListResult> ListAsync(
        Guid draftId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                sdv.statutory_discount_validation_id,
                sdv.entitlement_type::text,
                sdv.evidence_required,
                sdv.evidence_captured,
                der.discount_evidence_reference_id,
                der.evidence_type::text,
                der.evidence_storage_ref,
                der.evidence_capture_status::text,
                der.redaction_status::text,
                der.captured_at,
                der.captured_by_user_id,
                der.correlation_id
            FROM discounts.statutory_discount_validations AS sdv
            LEFT JOIN discounts.discount_evidence_references AS der
              ON der.statutory_discount_validation_id = sdv.statutory_discount_validation_id
             AND der.purged_at IS NULL
            WHERE sdv.statutory_discount_validation_id = @draft_id
              AND sdv.validation_channel = 'OPERATOR_ASSISTED'::discounts.statutory_discount_validations_channel_enum
            ORDER BY der.captured_at DESC NULLS LAST, der.discount_evidence_reference_id DESC NULLS LAST;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("draft_id", NpgsqlDbType.Uuid).Value = draftId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<OperatorConsoleStatutoryDiscountEvidenceMetadataResult>();
        string? entitlementType = null;
        var evidenceRequired = false;
        var evidenceCaptured = false;

        while (await reader.ReadAsync(cancellationToken))
        {
            entitlementType ??= reader.GetString(reader.GetOrdinal("entitlement_type"));
            evidenceRequired = reader.GetBoolean(reader.GetOrdinal("evidence_required"));
            evidenceCaptured = reader.GetBoolean(reader.GetOrdinal("evidence_captured"));

            if (reader.IsDBNull(reader.GetOrdinal("discount_evidence_reference_id")))
            {
                continue;
            }

            var captureStatus = reader.GetString(reader.GetOrdinal("evidence_capture_status"));
            items.Add(new OperatorConsoleStatutoryDiscountEvidenceMetadataResult(
                reader.GetGuid(reader.GetOrdinal("discount_evidence_reference_id")),
                draftId,
                reader.GetString(reader.GetOrdinal("evidence_type")),
                CaptureMethodFromStorageReference(GetNullableString(reader, "evidence_storage_ref")),
                GetNullableString(reader, "evidence_storage_ref"),
                GetNullableGuid(reader, "captured_by_user_id"),
                reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("captured_at")),
                reader.GetString(reader.GetOrdinal("redaction_status")),
                ToVerificationStatus(captureStatus),
                GetNullableGuid(reader, "correlation_id")));
        }

        return new OperatorConsoleStatutoryDiscountEvidenceListResult(
            draftId,
            evidenceRequired,
            evidenceCaptured,
            entitlementType is null
                ? Array.Empty<string>()
                : OperatorConsoleStatutoryDiscountEvidenceService.RequiredEvidenceTypes(entitlementType, evidenceRequired),
            items.Count,
            items.FirstOrDefault()?.VerificationStatus,
            items,
            correlationId);
    }

    private static async Task<DraftEvidenceRow?> ReadDraftForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid draftId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                statutory_discount_validation_id,
                validation_status::text,
                evidence_required,
                evidence_captured
            FROM discounts.statutory_discount_validations
            WHERE statutory_discount_validation_id = @draft_id
              AND validation_channel = 'OPERATOR_ASSISTED'::discounts.statutory_discount_validations_channel_enum
            FOR UPDATE;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("draft_id", NpgsqlDbType.Uuid).Value = draftId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new DraftEvidenceRow(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetBoolean(2),
            reader.GetBoolean(3));
    }

    private static async Task<Guid?> FindReusableEvidenceReferenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid draftId,
        string evidenceType,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT discount_evidence_reference_id
            FROM discounts.discount_evidence_references
            WHERE statutory_discount_validation_id = @draft_id
              AND evidence_type = @evidence_type::discounts.discount_evidence_type_enum
              AND evidence_storage_type = @evidence_storage_type::discounts.evidence_storage_type_enum
              AND evidence_capture_status IN (
                    'REFERENCED'::discounts.evidence_capture_status_enum,
                    'CAPTURED'::discounts.evidence_capture_status_enum
              )
              AND purged_at IS NULL
            ORDER BY
                CASE WHEN evidence_capture_status = 'REFERENCED'::discounts.evidence_capture_status_enum THEN 0 ELSE 1 END,
                captured_at DESC,
                discount_evidence_reference_id DESC
            LIMIT 1
            FOR UPDATE;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("draft_id", NpgsqlDbType.Uuid).Value = draftId;
        command.Parameters.Add("evidence_type", NpgsqlDbType.Text).Value = ToDatabaseEvidenceType(evidenceType);
        command.Parameters.Add("evidence_storage_type", NpgsqlDbType.Text).Value = EvidenceStorageType;

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid evidenceReferenceId ? evidenceReferenceId : null;
    }

    private static async Task<EvidenceMetadataRow> InsertEvidenceReferenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OperatorConsoleStatutoryDiscountEvidencePersistenceCommand command,
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
                @draft_id,
                @evidence_type::discounts.discount_evidence_type_enum,
                @evidence_storage_type::discounts.evidence_storage_type_enum,
                @evidence_storage_ref,
                NULL,
                @evidence_capture_status::discounts.evidence_capture_status_enum,
                @access_classification::discounts.evidence_access_classification_enum,
                @redaction_status::discounts.evidence_redaction_status_enum,
                @retention_policy_code,
                NULL,
                now(),
                @captured_by_user_id,
                @correlation_id,
                @created_by_user_id,
                @updated_by_user_id
            )
            RETURNING
                discount_evidence_reference_id,
                evidence_type::text,
                evidence_storage_ref,
                evidence_capture_status::text,
                redaction_status::text,
                captured_at,
                captured_by_user_id;
            """;

        await using var npgsqlCommand = BuildEvidenceCommand(sql, connection, transaction, command);
        await using var reader = await npgsqlCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Operator Console statutory discount evidence insert did not return metadata.");
        }

        return ReadEvidenceMetadata(reader);
    }

    private static async Task<EvidenceMetadataRow> UpdateEvidenceReferenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OperatorConsoleStatutoryDiscountEvidencePersistenceCommand command,
        Guid evidenceReferenceId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE discounts.discount_evidence_references
               SET evidence_storage_ref = @evidence_storage_ref,
                   evidence_capture_status = @evidence_capture_status::discounts.evidence_capture_status_enum,
                   access_classification = @access_classification::discounts.evidence_access_classification_enum,
                   redaction_status = @redaction_status::discounts.evidence_redaction_status_enum,
                   captured_at = now(),
                   captured_by_user_id = @captured_by_user_id,
                   correlation_id = @correlation_id,
                   updated_at = now(),
                   updated_by_user_id = @updated_by_user_id,
                   row_version = row_version + 1
             WHERE discount_evidence_reference_id = @evidence_reference_id
             RETURNING
                discount_evidence_reference_id,
                evidence_type::text,
                evidence_storage_ref,
                evidence_capture_status::text,
                redaction_status::text,
                captured_at,
                captured_by_user_id;
            """;

        await using var npgsqlCommand = BuildEvidenceCommand(sql, connection, transaction, command);
        npgsqlCommand.Parameters.Add("evidence_reference_id", NpgsqlDbType.Uuid).Value = evidenceReferenceId;

        await using var reader = await npgsqlCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Operator Console statutory discount evidence update did not return metadata.");
        }

        return ReadEvidenceMetadata(reader);
    }

    private static NpgsqlCommand BuildEvidenceCommand(
        string sql,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OperatorConsoleStatutoryDiscountEvidencePersistenceCommand command)
    {
        var npgsqlCommand = new NpgsqlCommand(sql, connection, transaction);
        npgsqlCommand.Parameters.Add("draft_id", NpgsqlDbType.Uuid).Value = command.DraftId;
        npgsqlCommand.Parameters.Add("evidence_type", NpgsqlDbType.Text).Value = ToDatabaseEvidenceType(command.EvidenceType);
        npgsqlCommand.Parameters.Add("evidence_storage_type", NpgsqlDbType.Text).Value = EvidenceStorageType;
        npgsqlCommand.Parameters.Add("evidence_storage_ref", NpgsqlDbType.Varchar).Value = DbValue(command.StorageReference);
        npgsqlCommand.Parameters.Add("evidence_capture_status", NpgsqlDbType.Text).Value = EvidenceCaptureStatus;
        npgsqlCommand.Parameters.Add("access_classification", NpgsqlDbType.Text).Value = EvidenceAccessClassification;
        npgsqlCommand.Parameters.Add("redaction_status", NpgsqlDbType.Text).Value = EvidenceRedactionStatus;
        npgsqlCommand.Parameters.Add("retention_policy_code", NpgsqlDbType.Varchar).Value = EvidenceRetentionPolicyCode;
        npgsqlCommand.Parameters.Add("captured_by_user_id", NpgsqlDbType.Uuid).Value = command.CapturedByUserId;
        npgsqlCommand.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = command.CorrelationId;
        npgsqlCommand.Parameters.Add("created_by_user_id", NpgsqlDbType.Uuid).Value = command.CapturedByUserId;
        npgsqlCommand.Parameters.Add("updated_by_user_id", NpgsqlDbType.Uuid).Value = command.CapturedByUserId;
        return npgsqlCommand;
    }

    private static async Task<bool> MarkDraftEvidenceCapturedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OperatorConsoleStatutoryDiscountEvidencePersistenceCommand command,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE discounts.statutory_discount_validations
               SET evidence_captured = true,
                   correlation_id = @correlation_id,
                   updated_at = now(),
                   updated_by_user_id = @updated_by_user_id,
                   row_version = row_version + 1
             WHERE statutory_discount_validation_id = @draft_id
             RETURNING evidence_captured;
            """;

        await using var npgsqlCommand = new NpgsqlCommand(sql, connection, transaction);
        npgsqlCommand.Parameters.Add("draft_id", NpgsqlDbType.Uuid).Value = command.DraftId;
        npgsqlCommand.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = command.CorrelationId;
        npgsqlCommand.Parameters.Add("updated_by_user_id", NpgsqlDbType.Uuid).Value = command.CapturedByUserId;

        var value = await npgsqlCommand.ExecuteScalarAsync(cancellationToken);
        return value is bool captured && captured;
    }

    private static EvidenceMetadataRow ReadEvidenceMetadata(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetFieldValue<DateTimeOffset>(5),
            reader.IsDBNull(6) ? null : reader.GetGuid(6));

    private static string ToDatabaseEvidenceType(string evidenceType) =>
        evidenceType == "OTHER_SUPPORTING_DOCUMENT" ? "SUPPORTING_DOCUMENT" : evidenceType;

    private static string ToVerificationStatus(string captureStatus) =>
        captureStatus switch
        {
            "CAPTURED" => "CAPTURED",
            "REJECTED" => "REJECTED",
            "REFERENCED" => "PENDING_REVIEW",
            _ => captureStatus
        };

    private static string CaptureMethodFromStorageReference(string? storageReference)
    {
        if (storageReference?.StartsWith("upload-metadata:", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "UPLOAD";
        }

        if (storageReference?.StartsWith("manual-reference:", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "MANUAL_REFERENCE";
        }

        return "OPERATOR_CONFIRMED";
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

    private static object DbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private sealed record DraftEvidenceRow(
        Guid DraftId,
        string ValidationStatus,
        bool EvidenceRequired,
        bool EvidenceCaptured);

    private sealed record EvidenceMetadataRow(
        Guid EvidenceId,
        string EvidenceType,
        string? StorageReference,
        string CaptureStatus,
        string RedactionStatus,
        DateTimeOffset CapturedAt,
        Guid? CapturedByUserId);
}

