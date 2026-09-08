using ExitPass.CentralPms.Application.Payments;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.Payments;

public sealed class PaymentFinalityCompletionAuthorityReader : IPaymentFinalityCompletionAuthorityReader
{
    private readonly string _connectionString;

    public PaymentFinalityCompletionAuthorityReader(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    public async Task<PaymentFinalityCompletionCandidate?> ReadAsync(
        Guid paymentAttemptId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                attempt.payment_attempt_id,
                attempt.parking_session_id,
                attempt.tariff_snapshot_id,
                attempt.attempt_status::text,
                attempt.finalized_at,
                ROUND(attempt.amount * 100)::bigint AS attempt_amount_minor,
                attempt.currency_code::text AS attempt_currency,
                parking.site_id,
                parking.site_group_id,
                tariff.parking_session_id AS tariff_parking_session_id,
                tariff.snapshot_status::text AS tariff_status,
                ROUND(tariff.net_amount * 100)::bigint AS tariff_net_amount_minor,
                tariff.currency_code::text AS tariff_currency,
                confirmation.payment_confirmation_id,
                confirmation.confirmation_status::text AS confirmation_status,
                ROUND(confirmation.confirmed_amount * 100)::bigint AS confirmed_amount_minor,
                confirmation.currency_code::text AS confirmation_currency,
                confirmation.confirmed_at,
                confirmation.correlation_id
            FROM core.payment_attempts AS attempt
            LEFT JOIN core.parking_sessions AS parking
              ON parking.parking_session_id = attempt.parking_session_id
            LEFT JOIN core.tariff_snapshots AS tariff
              ON tariff.tariff_snapshot_id = attempt.tariff_snapshot_id
            LEFT JOIN core.payment_confirmations AS confirmation
              ON confirmation.payment_attempt_id = attempt.payment_attempt_id
             AND confirmation.confirmation_status = 'RECORDED'
            WHERE attempt.payment_attempt_id = @payment_attempt_id;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.Add("payment_attempt_id", NpgsqlDbType.Uuid).Value = paymentAttemptId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var candidate = new PaymentFinalityCompletionCandidate(
            reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
            reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            reader.GetGuid(reader.GetOrdinal("tariff_snapshot_id")),
            NullableGuid(reader, "site_id"),
            NullableGuid(reader, "site_group_id"),
            reader.GetString(reader.GetOrdinal("attempt_status")),
            NullableTimestamp(reader, "finalized_at"),
            reader.GetInt64(reader.GetOrdinal("attempt_amount_minor")),
            reader.GetString(reader.GetOrdinal("attempt_currency")),
            NullableGuid(reader, "tariff_parking_session_id"),
            NullableString(reader, "tariff_status"),
            NullableInt64(reader, "tariff_net_amount_minor"),
            NullableString(reader, "tariff_currency"),
            NullableGuid(reader, "payment_confirmation_id"),
            NullableString(reader, "confirmation_status"),
            NullableInt64(reader, "confirmed_amount_minor"),
            NullableString(reader, "confirmation_currency"),
            NullableTimestamp(reader, "confirmed_at"),
            NullableGuid(reader, "correlation_id"));

        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "Payment finality resolved more than one recorded confirmation for the payment attempt.");
        }

        return candidate;
    }

    private static Guid? NullableGuid(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static long? NullableInt64(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    private static string? NullableString(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset? NullableTimestamp(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }
}
