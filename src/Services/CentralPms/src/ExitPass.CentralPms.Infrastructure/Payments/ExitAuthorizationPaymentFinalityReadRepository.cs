using ExitPass.CentralPms.Application.Payments;
using Npgsql;

namespace ExitPass.CentralPms.Infrastructure.Payments;

/// <summary>
/// PostgreSQL read model used by the ExitAuthorization application preflight.
/// </summary>
public sealed class ExitAuthorizationPaymentFinalityReadRepository : IExitAuthorizationPaymentFinalityReadRepository
{
    private readonly string _connectionString;

    public ExitAuthorizationPaymentFinalityReadRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<bool> IsPaymentFinalityVerifiedAsync(
        Guid parkingSessionId,
        Guid paymentAttemptId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                EXISTS (
                    SELECT 1
                    FROM core.payment_attempts AS pa
                    WHERE pa.payment_attempt_id = @payment_attempt_id
                      AND pa.parking_session_id = @parking_session_id
                ) AS attempt_exists,
                EXISTS (
                    SELECT 1
                    FROM core.payment_attempts AS pa
                    JOIN core.payment_confirmations AS pc
                      ON pc.payment_attempt_id = pa.payment_attempt_id
                     AND pc.confirmation_status = 'RECORDED'
                    WHERE pa.payment_attempt_id = @payment_attempt_id
                      AND pa.parking_session_id = @parking_session_id
                      AND pa.attempt_status = 'CONFIRMED'
                      AND pa.finalized_at IS NOT NULL
                      AND pc.confirmed_amount = pa.amount
                      AND pc.currency_code = pa.currency_code
                ) AS finality_verified;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        command.Parameters.AddWithValue("payment_attempt_id", paymentAttemptId);
        command.Parameters.AddWithValue("parking_session_id", parkingSessionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return false;
        }

        if (!reader.GetBoolean(reader.GetOrdinal("attempt_exists")))
        {
            throw new KeyNotFoundException("Payment attempt was not found.");
        }

        return reader.GetBoolean(reader.GetOrdinal("finality_verified"));
    }
}
