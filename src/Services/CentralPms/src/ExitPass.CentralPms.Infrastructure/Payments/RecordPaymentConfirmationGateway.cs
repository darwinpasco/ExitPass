using ExitPass.CentralPms.Application.Payments;
using Npgsql;

namespace ExitPass.CentralPms.Infrastructure.Payments;

/// <summary>
/// BRD: 9.10 Payment Processing and Confirmation
/// SDD: 7.3 Provider Callback / Confirmation Handling
/// Invariant: Provider confirmation is recorded through the canonical DB routine only.
/// </summary>
public sealed class RecordPaymentConfirmationGateway : IRecordPaymentConfirmationGateway
{
    private readonly string _connectionString;

    public RecordPaymentConfirmationGateway(string connectionString)
    {
        _connectionString = connectionString;
    }

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
}
