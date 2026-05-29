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
            var result = await InsertDraftAsync(connection, transaction, command, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
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
            Persisted: true);
    }

    private static object DbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
}
