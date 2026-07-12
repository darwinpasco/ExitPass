using ExitPass.CentralPms.Application.OperatorConsole;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.OperatorConsole;

/// <summary>
/// PostgreSQL-backed writer for Operator Console statutory discount validation decisions.
///
/// ExitPass v1.3 Invariants Enforced:
/// - Writes are limited to discounts.statutory_discount_validations decision/status columns.
/// - The requester who created the statutory discount validation cannot approve or reject it.
/// - This writer does not apply discounts or create payment, gate, coupon, provider, settlement, evidence upload, fingerprint, or reconciliation records.
/// </summary>
public sealed class OperatorConsoleStatutoryDiscountDecisionWriter : IOperatorConsoleStatutoryDiscountDecisionWriter
{
    private readonly string _connectionString;

    /// <summary>
    /// Creates an Operator Console statutory discount validation decision writer.
    /// </summary>
    public OperatorConsoleStatutoryDiscountDecisionWriter(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    /// <inheritdoc />
    public async Task<OperatorConsoleStatutoryDiscountDecisionPersistenceResult> PersistAsync(
        OperatorConsoleStatutoryDiscountDecisionPersistenceCommand command,
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
                await transaction.CommitAsync(cancellationToken);
                return NotFound(command);
            }

            var result = await DecideAsync(connection, transaction, command, current, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task<OperatorConsoleStatutoryDiscountDecisionPersistenceResult> DecideAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OperatorConsoleStatutoryDiscountDecisionPersistenceCommand command,
        DraftDecisionRow current,
        CancellationToken cancellationToken)
    {
        if (current.ValidationStatus == command.TargetValidationStatus)
        {
            return ToResult(
                command,
                current,
                current.ValidationStatus,
                decisionAccepted: true,
                decisionPersisted: true,
                alreadyDecided: true,
                decisionChanged: false,
                ineligibilityReason: null,
                errorCode: null);
        }

        if (current.ValidationStatus is "APPROVED" or "REJECTED")
        {
            throw new OperatorConsoleStatutoryDiscountDecisionConflictException(
                command.DraftId,
                current.ValidationStatus,
                command.Decision);
        }

        if (current.ValidationStatus is not ("REQUESTED" or "PENDING_OPERATOR_REVIEW"))
        {
            return ToResult(
                command,
                current,
                current.ValidationStatus,
                decisionAccepted: false,
                decisionPersisted: false,
                alreadyDecided: false,
                decisionChanged: false,
                ineligibilityReason: "DRAFT_NOT_DECISIONABLE",
                errorCode: "STATUTORY_DISCOUNT_DRAFT_NOT_DECISIONABLE");
        }

        if (current.RequestedByUserId == command.DecidedByUserId)
        {
            return ToResult(
                command,
                current,
                current.ValidationStatus,
                decisionAccepted: false,
                decisionPersisted: false,
                alreadyDecided: false,
                decisionChanged: false,
                ineligibilityReason: "REQUESTER_CANNOT_APPROVE_OWN_DISCOUNT",
                errorCode: "REQUESTER_CANNOT_APPROVE_OWN_DISCOUNT");
        }

        if (command.TargetValidationStatus == "APPROVED" &&
            current.EvidenceRequired &&
            !current.EvidenceCaptured)
        {
            return ToResult(
                command,
                current,
                current.ValidationStatus,
                decisionAccepted: false,
                decisionPersisted: false,
                alreadyDecided: false,
                decisionChanged: false,
                ineligibilityReason: "EVIDENCE_REQUIRED_NOT_CAPTURED",
                errorCode: "EVIDENCE_REQUIRED_NOT_CAPTURED");
        }

        var updated = await UpdateDecisionAsync(connection, transaction, command, cancellationToken);
        return ToResult(
            command,
            current,
            updated.ValidationStatus,
            decisionAccepted: true,
            decisionPersisted: true,
            alreadyDecided: false,
            decisionChanged: true,
            ineligibilityReason: null,
            errorCode: null);
    }

    private static async Task<DraftDecisionRow?> ReadDraftForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid draftId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                statutory_discount_validation_id,
                parking_session_id,
                entitlement_type::text,
                validation_status::text,
                evidence_required,
                evidence_captured,
                requested_by_user_id,
                decision_reason_code
            FROM discounts.statutory_discount_validations
            WHERE statutory_discount_validation_id = @statutory_discount_validation_id
            FOR UPDATE;
            """;

        await using var npgsqlCommand = new NpgsqlCommand(sql, connection, transaction);
        npgsqlCommand.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = draftId;

        await using var reader = await npgsqlCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new DraftDecisionRow(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetBoolean(4),
            reader.GetBoolean(5),
            reader.GetGuid(6),
            reader.IsDBNull(7) ? null : reader.GetString(7));
    }

    private static async Task<DraftDecisionRow> UpdateDecisionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OperatorConsoleStatutoryDiscountDecisionPersistenceCommand command,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE discounts.statutory_discount_validations
               SET validation_status = @validation_status::discounts.statutory_discount_validations_status_enum,
                   decision_reason_code = @decision_reason_code,
                   validated_at = now(),
                   validated_by_user_id = @validated_by_user_id,
                   correlation_id = @correlation_id,
                   updated_at = now(),
                   updated_by_user_id = @updated_by_user_id,
                   row_version = row_version + 1
             WHERE statutory_discount_validation_id = @statutory_discount_validation_id
             RETURNING
                statutory_discount_validation_id,
                parking_session_id,
                entitlement_type::text,
                validation_status::text,
                evidence_required,
                evidence_captured,
                requested_by_user_id,
                decision_reason_code;
            """;

        await using var npgsqlCommand = new NpgsqlCommand(sql, connection, transaction);
        npgsqlCommand.Parameters.Add("statutory_discount_validation_id", NpgsqlDbType.Uuid).Value = command.DraftId;
        npgsqlCommand.Parameters.Add("validation_status", NpgsqlDbType.Text).Value = command.TargetValidationStatus;
        npgsqlCommand.Parameters.Add("decision_reason_code", NpgsqlDbType.Varchar).Value = DbValue(command.DecisionReasonCode);
        npgsqlCommand.Parameters.Add("validated_by_user_id", NpgsqlDbType.Uuid).Value = command.DecidedByUserId;
        npgsqlCommand.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = command.CorrelationId;
        npgsqlCommand.Parameters.Add("updated_by_user_id", NpgsqlDbType.Uuid).Value = command.DecidedByUserId;

        await using var reader = await npgsqlCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Operator Console statutory discount decision update did not return a draft row.");
        }

        return new DraftDecisionRow(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetBoolean(4),
            reader.GetBoolean(5),
            reader.GetGuid(6),
            reader.IsDBNull(7) ? null : reader.GetString(7));
    }

    private static OperatorConsoleStatutoryDiscountDecisionPersistenceResult ToResult(
        OperatorConsoleStatutoryDiscountDecisionPersistenceCommand command,
        DraftDecisionRow current,
        string currentValidationStatus,
        bool decisionAccepted,
        bool decisionPersisted,
        bool alreadyDecided,
        bool decisionChanged,
        string? ineligibilityReason,
        string? errorCode) =>
        new(
            Found: true,
            decisionAccepted,
            decisionPersisted,
            current.DraftId,
            current.ParkingSessionId,
            current.EntitlementType,
            current.ValidationStatus,
            currentValidationStatus,
            command.Decision,
            decisionChanged ? command.DecisionReasonCode : current.DecisionReasonCode,
            alreadyDecided,
            decisionChanged,
            ineligibilityReason,
            errorCode);

    private static OperatorConsoleStatutoryDiscountDecisionPersistenceResult NotFound(
        OperatorConsoleStatutoryDiscountDecisionPersistenceCommand command) =>
        new(
            Found: false,
            DecisionAccepted: false,
            DecisionPersisted: false,
            DraftId: command.DraftId,
            ParkingSessionId: null,
            EntitlementType: null,
            PreviousValidationStatus: null,
            CurrentValidationStatus: null,
            command.Decision,
            command.DecisionReasonCode,
            AlreadyDecided: false,
            DecisionChanged: false,
            IneligibilityReason: "DRAFT_NOT_FOUND",
            ErrorCode: "DRAFT_NOT_FOUND");

    private static object DbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private sealed record DraftDecisionRow(
        Guid DraftId,
        Guid ParkingSessionId,
        string EntitlementType,
        string ValidationStatus,
        bool EvidenceRequired,
        bool EvidenceCaptured,
        Guid RequestedByUserId,
        string? DecisionReasonCode);
}
