using System.Data;
using ExitPass.CentralPms.Application.Abstractions.Persistence;
using Npgsql;

namespace ExitPass.CentralPms.Infrastructure.Payments;

/// <summary>
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 6.4 Finalize Payment
/// - 9.7 Recommended Database Functions
///
/// Invariants Enforced:
/// - Finalization is delegated to the authoritative DB routine
/// - Application code must not bypass storage-level finalization and conflict handling
/// </summary>
public sealed class FinalizePaymentAttemptGateway : IFinalizePaymentAttemptGateway
{
    private readonly string _connectionString;

    public FinalizePaymentAttemptGateway(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    public async Task<FinalizePaymentAttemptDbResult> FinalizeAsync(
        FinalizePaymentAttemptDbRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = BuildCommand(connection, request);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The database routine returned no rows.");
        }

        return new FinalizePaymentAttemptDbResult
        {
            PaymentAttemptId = reader["payment_attempt_id"] is DBNull
                ? Guid.Empty
                : (Guid)reader["payment_attempt_id"],
            AttemptStatus = reader["attempt_status"] as string ?? string.Empty
        };
    }

    private static NpgsqlCommand BuildCommand(
        NpgsqlConnection connection,
        FinalizePaymentAttemptDbRequest request)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            select *
            from core.finalize_payment_attempt(
                @p_payment_attempt_id,
                @p_final_attempt_status,
                @p_requested_by,
                @p_correlation_id,
                @p_now
            );
            """;

        command.Parameters.AddWithValue("p_payment_attempt_id", request.PaymentAttemptId);
        command.Parameters.AddWithValue("p_final_attempt_status", request.FinalAttemptStatus);
        command.Parameters.AddWithValue("p_requested_by", request.RequestedBy);
        command.Parameters.AddWithValue("p_correlation_id", request.CorrelationId);
        command.Parameters.AddWithValue("p_now", request.RequestedAt);

        return command;
    }
}
