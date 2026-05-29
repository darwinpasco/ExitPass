using ExitPass.CentralPms.Application.OperatorConsole;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.OperatorConsole;

/// <summary>
/// PostgreSQL-backed writer for privacy-minimized Operator Console statutory discount validation drafts.
///
/// ExitPass v1.2 Invariants Enforced:
/// - Writes are limited to discounts.statutory_discount_validations and metadata-only discounts.discount_evidence_references rows.
/// - This writer does not upload raw evidence or create fingerprint, payment, gate, coupon, provider, settlement, or reconciliation records.
/// </summary>
public sealed class OperatorConsoleStatutoryDiscountDraftWriter : IOperatorConsoleStatutoryDiscountDraftWriter
{
    private const string EvidenceStorageType = "EXTERNAL_REFERENCE";
    private const string EvidenceCaptureStatus = "REFERENCED";
    private const string EvidenceAccessClassification = "RESTRICTED";
    private const string EvidenceRedactionStatus = "NOT_REDACTED";
    private const string EvidenceRetentionPolicyCode = "OPERATOR_CONSOLE_STATUTORY_DISCOUNT_EVIDENCE_METADATA_V1";

    private readonly string _connectionString;

    /// <summary>
    /// Creates an Operator Console statutory discount validation draft writer.
    /// </summary>
    public OperatorConsoleStatutoryDiscountDraftWriter(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    /// <inheritdoc />
    public async Task<OperatorConsoleStatutoryDiscountDraftPersistenceResult> PersistAsync(
        OperatorConsoleStatutoryDiscountDraftPersistenceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var existing = await FindReusableDraftAsync(connection, transaction, command, cancellationToken);
            if (existing is not null)
            {
                existing = await EnsureEvidenceMetadataAsync(connection, transaction, command, existing, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return existing with { ReusedExistingDraft = true };
            }

            var result = await InsertDraftAsync(connection, transaction, command, cancellationToken);
            result = await EnsureEvidenceMetadataAsync(connection, transaction, command, result, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await ResolveUniqueViolationAsync(command, cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<OperatorConsoleStatutoryDiscountDraftPersistenceResult> ResolveUniqueViolationAsync(
        OperatorConsoleStatutoryDiscountDraftPersistenceCommand command,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await FindReusableDraftAsync(connection, transaction, command, cancellationToken);
        if (existing is not null)
        {
            existing = await EnsureEvidenceMetadataAsync(connection, transaction, command, existing, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return existing with { ReusedExistingDraft = true };
        }

        await transaction.RollbackAsync(cancellationToken);
        throw new OperatorConsoleStatutoryDiscountDraftAlreadyExistsException(
            command.ParkingSessionId,
            command.EntitlementType);
    }

    private static async Task<OperatorConsoleStatutoryDiscountDraftPersistenceResult?> FindReusableDraftAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        OperatorConsoleStatutoryDiscountDraftPersistenceCommand command,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                statutory_discount_validation_id,
                validation_status::text AS validation_status,
                evidence_required
            FROM discounts.statutory_discount_validations
            WHERE parking_session_id = @parking_session_id
              AND entitlement_type = @entitlement_type::discounts.statutory_entitlement_type_enum
              AND validation_channel = 'OPERATOR_ASSISTED'::discounts.statutory_discount_validations_channel_enum
              AND validation_status IN (
                    'REQUESTED'::discounts.statutory_discount_validations_status_enum,
                    'PENDING_OPERATOR_REVIEW'::discounts.statutory_discount_validations_status_enum
              )
              AND evidence_captured = false
              AND applied_policy_reference_id IS NULL
              AND validated_at IS NULL
            ORDER BY requested_at DESC, statutory_discount_validation_id DESC
            LIMIT 1;
            """;

        await using var npgsqlCommand = new NpgsqlCommand(sql, connection, transaction);
        npgsqlCommand.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = command.ParkingSessionId;
        npgsqlCommand.Parameters.Add("entitlement_type", NpgsqlDbType.Text).Value = command.EntitlementType;

        await using var reader = await npgsqlCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new OperatorConsoleStatutoryDiscountDraftPersistenceResult(
            reader.GetGuid(0),
            reader.GetString(1),
            Persisted: true,
            ReusedExistingDraft: true,
            EvidenceRequired: reader.GetBoolean(2),
            EvidenceReferenceCreated: false,
            EvidenceReferenceId: null);
    }

    private static async Task<OperatorConsoleStatutoryDiscountDraftPersistenceResult> InsertDraftAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OperatorConsoleStatutoryDiscountDraftPersistenceCommand command,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO discounts.statutory_discount_validations (
                parking_session_id,
                entitlement_type,
                policy_resolution_basis,
                validation_channel,
                validation_status,
                evidence_required,
                evidence_captured,
                decision_reason_code,
                requested_at,
                requested_by_user_id,
                correlation_id,
                created_by_user_id
            )
            VALUES (
                @parking_session_id,
                @entitlement_type::discounts.statutory_entitlement_type_enum,
                'SYSTEM_DEFAULT'::discounts.policy_resolution_basis_enum,
                'OPERATOR_ASSISTED'::discounts.statutory_discount_validations_channel_enum,
                'REQUESTED'::discounts.statutory_discount_validations_status_enum,
                @evidence_required,
                false,
                @decision_reason_code,
                now(),
                @requested_by_user_id,
                @correlation_id,
                @created_by_user_id
            )
            RETURNING statutory_discount_validation_id, validation_status::text;
            """;

        await using var npgsqlCommand = new NpgsqlCommand(sql, connection, transaction);
        npgsqlCommand.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = command.ParkingSessionId;
        npgsqlCommand.Parameters.Add("entitlement_type", NpgsqlDbType.Text).Value = command.EntitlementType;
        npgsqlCommand.Parameters.Add("evidence_required", NpgsqlDbType.Boolean).Value = command.EvidenceRequired;
        npgsqlCommand.Parameters.Add("decision_reason_code", NpgsqlDbType.Varchar).Value = DbValue(command.ReasonCode);
        npgsqlCommand.Parameters.Add("requested_by_user_id", NpgsqlDbType.Uuid).Value = command.RequestedByUserId;
        npgsqlCommand.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = command.CorrelationId;
        npgsqlCommand.Parameters.Add("created_by_user_id", NpgsqlDbType.Uuid).Value = command.RequestedByUserId;

        await using var reader = await npgsqlCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Operator Console statutory discount draft insert did not return a draft ID.");
        }

        return new OperatorConsoleStatutoryDiscountDraftPersistenceResult(
            reader.GetGuid(0),
            reader.GetString(1),
            Persisted: true,
            ReusedExistingDraft: false,
            EvidenceRequired: command.EvidenceRequired,
            EvidenceReferenceCreated: false,
            EvidenceReferenceId: null);
    }

    private static async Task<OperatorConsoleStatutoryDiscountDraftPersistenceResult> EnsureEvidenceMetadataAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OperatorConsoleStatutoryDiscountDraftPersistenceCommand command,
        OperatorConsoleStatutoryDiscountDraftPersistenceResult result,
        CancellationToken cancellationToken)
    {
        if (!command.EvidenceRequired)
        {
            return result;
        }

        await MarkDraftEvidenceRequiredAsync(connection, transaction, command, result.DraftId, cancellationToken);
        await LockEvidenceReferenceTableAsync(connection, transaction, cancellationToken);

        var evidenceType = EvidenceTypeForEntitlement(command.EntitlementType);
        var existingEvidenceReferenceId = await FindEvidenceReferenceAsync(
            connection,
            transaction,
            result.DraftId,
            evidenceType,
            cancellationToken);

        if (existingEvidenceReferenceId.HasValue)
        {
            return result with
            {
                EvidenceRequired = true,
                EvidenceReferenceCreated = false,
                EvidenceReferenceId = existingEvidenceReferenceId
            };
        }

        var evidenceReferenceId = await InsertEvidenceReferenceAsync(
            connection,
            transaction,
            command,
            result.DraftId,
            evidenceType,
            cancellationToken);

        return result with
        {
            EvidenceRequired = true,
            EvidenceReferenceCreated = true,
            EvidenceReferenceId = evidenceReferenceId
        };
    }

    private static async Task MarkDraftEvidenceRequiredAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OperatorConsoleStatutoryDiscountDraftPersistenceCommand command,
        Guid draftId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE discounts.statutory_discount_validations
            SET evidence_required = true,
                updated_at = now(),
                updated_by_user_id = @updated_by_user_id
            WHERE statutory_discount_validation_id = @statutory_discount_validation_id
              AND evidence_required = false;
            """;

        await using var npgsqlCommand = new NpgsqlCommand(sql, connection, transaction);
        npgsqlCommand.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = draftId;
        npgsqlCommand.Parameters.Add("updated_by_user_id", NpgsqlDbType.Uuid).Value = command.RequestedByUserId;
        await npgsqlCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task LockEvidenceReferenceTableAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = "LOCK TABLE discounts.discount_evidence_references IN SHARE ROW EXCLUSIVE MODE;";
        await using var npgsqlCommand = new NpgsqlCommand(sql, connection, transaction);
        await npgsqlCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Guid?> FindEvidenceReferenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid draftId,
        string evidenceType,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT discount_evidence_reference_id
            FROM discounts.discount_evidence_references
            WHERE statutory_discount_validation_id = @statutory_discount_validation_id
              AND evidence_type = @evidence_type::discounts.discount_evidence_type_enum
              AND evidence_storage_type = @evidence_storage_type::discounts.evidence_storage_type_enum
              AND evidence_capture_status = @evidence_capture_status::discounts.evidence_capture_status_enum
              AND evidence_storage_ref IS NULL
              AND evidence_hash IS NULL
              AND purged_at IS NULL
            ORDER BY created_at DESC, discount_evidence_reference_id DESC
            LIMIT 1;
            """;

        await using var npgsqlCommand = new NpgsqlCommand(sql, connection, transaction);
        npgsqlCommand.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = draftId;
        npgsqlCommand.Parameters.Add("evidence_type", NpgsqlDbType.Text).Value = evidenceType;
        npgsqlCommand.Parameters.Add("evidence_storage_type", NpgsqlDbType.Text).Value = EvidenceStorageType;
        npgsqlCommand.Parameters.Add("evidence_capture_status", NpgsqlDbType.Text).Value = EvidenceCaptureStatus;

        var value = await npgsqlCommand.ExecuteScalarAsync(cancellationToken);
        return value is Guid evidenceReferenceId ? evidenceReferenceId : null;
    }

    private static async Task<Guid> InsertEvidenceReferenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OperatorConsoleStatutoryDiscountDraftPersistenceCommand command,
        Guid draftId,
        string evidenceType,
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
                created_by_user_id
            )
            VALUES (
                @statutory_discount_validation_id,
                @evidence_type::discounts.discount_evidence_type_enum,
                @evidence_storage_type::discounts.evidence_storage_type_enum,
                NULL,
                NULL,
                @evidence_capture_status::discounts.evidence_capture_status_enum,
                @access_classification::discounts.evidence_access_classification_enum,
                @redaction_status::discounts.evidence_redaction_status_enum,
                @retention_policy_code,
                NULL,
                now(),
                @captured_by_user_id,
                @correlation_id,
                @created_by_user_id
            )
            RETURNING discount_evidence_reference_id;
            """;

        await using var npgsqlCommand = new NpgsqlCommand(sql, connection, transaction);
        npgsqlCommand.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = draftId;
        npgsqlCommand.Parameters.Add("evidence_type", NpgsqlDbType.Text).Value = evidenceType;
        npgsqlCommand.Parameters.Add("evidence_storage_type", NpgsqlDbType.Text).Value = EvidenceStorageType;
        npgsqlCommand.Parameters.Add("evidence_capture_status", NpgsqlDbType.Text).Value = EvidenceCaptureStatus;
        npgsqlCommand.Parameters.Add("access_classification", NpgsqlDbType.Text).Value = EvidenceAccessClassification;
        npgsqlCommand.Parameters.Add("redaction_status", NpgsqlDbType.Text).Value = EvidenceRedactionStatus;
        npgsqlCommand.Parameters.Add("retention_policy_code", NpgsqlDbType.Varchar).Value = EvidenceRetentionPolicyCode;
        npgsqlCommand.Parameters.Add("captured_by_user_id", NpgsqlDbType.Uuid).Value = command.RequestedByUserId;
        npgsqlCommand.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = command.CorrelationId;
        npgsqlCommand.Parameters.Add("created_by_user_id", NpgsqlDbType.Uuid).Value = command.RequestedByUserId;

        var value = await npgsqlCommand.ExecuteScalarAsync(cancellationToken);
        return value is Guid evidenceReferenceId
            ? evidenceReferenceId
            : throw new InvalidOperationException("Operator Console statutory discount evidence metadata insert did not return an evidence reference ID.");
    }

    private static string EvidenceTypeForEntitlement(string entitlementType) =>
        string.Equals(entitlementType, "PWD", StringComparison.Ordinal)
            ? "PWD_ID"
            : "SENIOR_CITIZEN_ID";

    private static object DbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
}
