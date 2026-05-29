using ExitPass.CentralPms.Application.OperatorConsole;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.OperatorConsole;

/// <summary>
/// PostgreSQL-backed writer for privacy-minimized Operator Console statutory discount validation drafts.
///
/// ExitPass v1.2 Invariants Enforced:
/// - Writes are limited to discounts.statutory_discount_validations.
/// - This writer does not create evidence, fingerprint, payment, gate, coupon, provider, settlement, or reconciliation records.
/// </summary>
public sealed class OperatorConsoleStatutoryDiscountDraftWriter : IOperatorConsoleStatutoryDiscountDraftWriter
{
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
                await transaction.CommitAsync(cancellationToken);
                return existing with { ReusedExistingDraft = true };
            }

            var result = await InsertDraftAsync(connection, transaction, command, cancellationToken);
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

        var existing = await FindReusableDraftAsync(connection, transaction: null, command, cancellationToken);
        if (existing is not null)
        {
            return existing with { ReusedExistingDraft = true };
        }

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
                validation_status::text AS validation_status
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
            ReusedExistingDraft: true);
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
            ReusedExistingDraft: false);
    }

    private static object DbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
}
