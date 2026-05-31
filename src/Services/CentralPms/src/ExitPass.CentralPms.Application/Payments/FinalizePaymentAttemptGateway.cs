using System.Data;
using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Application.Payments;
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

    /// <summary>
    /// Creates the gateway that calls the Central PMS payment finalization routine.
    /// </summary>
    /// <param name="connectionString">Database connection string used to reach the authoritative routine.</param>
    public FinalizePaymentAttemptGateway(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public async Task<FinalizePaymentAttemptDbResult> FinalizeAsync(
        FinalizePaymentAttemptDbRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await ValidateAttemptPayableBasisAsync(connection, request.PaymentAttemptId, cancellationToken);

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

    private static async Task ValidateAttemptPayableBasisAsync(
        NpgsqlConnection connection,
        Guid paymentAttemptId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                pa.payment_attempt_id,
                pa.parking_session_id,
                pa.tariff_snapshot_id,
                pa.amount AS attempt_amount,
                pa.currency_code::text AS attempt_currency_code,
                ts.tariff_snapshot_id AS persisted_tariff_snapshot_id,
                ts.parking_session_id AS tariff_parking_session_id,
                ts.net_amount AS tariff_net_amount,
                ts.currency_code::text AS tariff_currency_code
            FROM core.payment_attempts AS pa
            LEFT JOIN core.tariff_snapshots AS ts
                ON ts.tariff_snapshot_id = pa.tariff_snapshot_id
            WHERE pa.payment_attempt_id = @payment_attempt_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        command.Parameters.AddWithValue("payment_attempt_id", paymentAttemptId);

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return;
        }

        if (reader.IsDBNull(reader.GetOrdinal("persisted_tariff_snapshot_id")))
        {
            throw new PaymentFinalityConflictException(
                "PAYMENT_ATTEMPT_TARIFF_SNAPSHOT_MISSING",
                $"Payment attempt {paymentAttemptId} does not have a persisted tariff snapshot.");
        }

        var attemptParkingSessionId = reader.GetGuid(reader.GetOrdinal("parking_session_id"));
        var tariffParkingSessionId = reader.GetGuid(reader.GetOrdinal("tariff_parking_session_id"));
        if (attemptParkingSessionId != tariffParkingSessionId)
        {
            throw new PaymentFinalityConflictException(
                "PAYMENT_ATTEMPT_TARIFF_SNAPSHOT_MISMATCH",
                "Payment attempt tariff snapshot belongs to a different parking session.");
        }

        var attemptAmount = reader.GetDecimal(reader.GetOrdinal("attempt_amount"));
        var tariffAmount = reader.GetDecimal(reader.GetOrdinal("tariff_net_amount"));
        if (attemptAmount != tariffAmount)
        {
            throw new PaymentFinalityConflictException(
                "PAYMENT_AMOUNT_MISMATCH",
                "Payment attempt amount does not match its persisted tariff snapshot payable amount.");
        }

        var attemptCurrency = reader.GetString(reader.GetOrdinal("attempt_currency_code")).Trim();
        var tariffCurrency = reader.GetString(reader.GetOrdinal("tariff_currency_code")).Trim();
        if (!string.Equals(attemptCurrency, tariffCurrency, StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentFinalityConflictException(
                "PAYMENT_CURRENCY_MISMATCH",
                "Payment attempt currency does not match its persisted tariff snapshot currency.");
        }
    }
}
