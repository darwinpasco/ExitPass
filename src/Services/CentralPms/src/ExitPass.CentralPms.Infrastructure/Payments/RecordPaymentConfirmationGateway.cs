using ExitPass.CentralPms.Application.Payments;
using Npgsql;

namespace ExitPass.CentralPms.Infrastructure.Payments;

/// <summary>
/// Records canonical provider confirmation evidence through the authoritative DB routine.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 7.3 Provider Callback / Confirmation Handling
///
/// Invariants Enforced:
/// - Provider confirmation is recorded through the canonical DB routine only
/// - Duplicate provider references are surfaced as deterministic business conflicts
/// </summary>
public sealed class RecordPaymentConfirmationGateway : IRecordPaymentConfirmationGateway
{
    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecordPaymentConfirmationGateway"/> class.
    /// </summary>
    /// <param name="connectionString">Primary database connection string.</param>
    public RecordPaymentConfirmationGateway(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Records provider confirmation evidence through <c>core.record_payment_confirmation(...)</c>.
    /// </summary>
    /// <param name="command">Normalized confirmation command.</param>
    /// <param name="now">Authoritative request timestamp.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The authoritative recorded payment confirmation.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the target payment attempt does not exist.</exception>
    /// <exception cref="DuplicatePaymentConfirmationException">
    /// Thrown when the provider reference has already been recorded.
    /// </exception>
    /// <exception cref="InvalidOperationException">Thrown when the DB routine returns no rows.</exception>
    public async Task<RecordPaymentConfirmationResult> RecordAsync(
        RecordPaymentConfirmationCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                payment_confirmation_id,
                payment_attempt_id,
                provider_reference,
                provider_status,
                'RECORDED'::character varying AS confirmation_status,
                verified_timestamp
            FROM core.record_payment_confirmation(
                @p_payment_attempt_id,
                @p_provider_reference,
                @p_provider_status,
                @p_requested_by,
                @p_correlation_id,
                @p_now
            );
            """;

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var dbCommand = new NpgsqlCommand(sql, connection)
            {
                CommandTimeout = 30
            };

            dbCommand.Parameters.AddWithValue("p_payment_attempt_id", command.PaymentAttemptId);
            dbCommand.Parameters.AddWithValue("p_provider_reference", command.ProviderReference);
            dbCommand.Parameters.AddWithValue("p_provider_status", command.ProviderStatus);
            dbCommand.Parameters.AddWithValue("p_requested_by", command.RequestedBy);
            dbCommand.Parameters.AddWithValue("p_correlation_id", command.CorrelationId);
            dbCommand.Parameters.AddWithValue("p_now", now);

            await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("record_payment_confirmation() returned no rows.");
            }

            return new RecordPaymentConfirmationResult(
                PaymentConfirmationId: reader.GetGuid(reader.GetOrdinal("payment_confirmation_id")),
                PaymentAttemptId: reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
                ProviderReference: reader.GetString(reader.GetOrdinal("provider_reference")),
                ProviderStatus: reader.GetString(reader.GetOrdinal("provider_status")),
                ConfirmationStatus: reader.GetString(reader.GetOrdinal("confirmation_status")),
                VerifiedTimestamp: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("verified_timestamp")));
        }
        catch (PostgresException ex) when (ex.SqlState == "P0002")
        {
            throw new KeyNotFoundException(ex.MessageText, ex);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            var message = ex.MessageText;

            if (message.Contains("uq_payment_confirmations__payment_attempt", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentConfirmationConflictException(
                    "PAYMENT_CONFIRMATION_ALREADY_EXISTS",
                    message);
            }

            if (message.Contains("provider reference", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("provider_reference", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentConfirmationConflictException(
                    "PROVIDER_REFERENCE_ALREADY_RECORDED",
                    message);
            }

            throw new PaymentConfirmationConflictException(
                "PAYMENT_CONFIRMATION_CONFLICT",
                message);
        }
    }
}
