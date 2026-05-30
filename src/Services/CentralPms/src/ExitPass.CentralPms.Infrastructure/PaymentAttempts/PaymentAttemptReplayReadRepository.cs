using System.Data;
using ExitPass.CentralPms.Application.Abstractions.Persistence;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.PaymentAttempts;

/// <summary>
/// Reads persisted payment attempts for safe idempotent replay checks.
/// </summary>
public sealed class PaymentAttemptReplayReadRepository : IPaymentAttemptReplayReadRepository
{
    private readonly string _connectionString;

    /// <summary>
    /// Creates a repository using the configured Central PMS PostgreSQL connection string.
    /// </summary>
    /// <param name="configuration">Application configuration.</param>
    public PaymentAttemptReplayReadRepository(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _connectionString = configuration.GetConnectionString("MainDatabase")
            ?? throw new InvalidOperationException("Connection string 'MainDatabase' is missing.");
    }

    /// <inheritdoc />
    public async Task<PaymentAttemptReplayRecord?> GetByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        const string sql = """
            /*
             * #204 replay guard:
             * - read the existing attempt before tariff-snapshot active-status validation so true idempotent
             *   replay can reuse a snapshot consumed by the first successful attempt.
             * - this query is read-only and does not alter provider routing, tariff lifecycle, or payment state.
             */
            SELECT
                pa.payment_attempt_id,
                pa.parking_session_id,
                pa.tariff_snapshot_id,
                pa.idempotency_key,
                pa.amount,
                pa.currency_code,
                pr.rail_code,
                pr.provider_code
            FROM core.payment_attempts AS pa
            LEFT JOIN payments.payment_rails AS pr
                ON pr.payment_rail_id = pa.payment_rail_id
            WHERE pa.idempotency_key = @idempotency_key
            LIMIT 1;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        command.Parameters.Add("idempotency_key", NpgsqlDbType.Varchar).Value = idempotencyKey;

        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SingleRow,
            cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new PaymentAttemptReplayRecord
        {
            PaymentAttemptId = reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
            ParkingSessionId = reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            TariffSnapshotId = reader.GetGuid(reader.GetOrdinal("tariff_snapshot_id")),
            IdempotencyKey = reader.GetString(reader.GetOrdinal("idempotency_key")),
            Amount = reader.GetDecimal(reader.GetOrdinal("amount")),
            CurrencyCode = reader.GetString(reader.GetOrdinal("currency_code")).Trim(),
            RailCode = reader.IsDBNull(reader.GetOrdinal("rail_code"))
                ? null
                : reader.GetString(reader.GetOrdinal("rail_code")),
            ProviderCode = reader.IsDBNull(reader.GetOrdinal("provider_code"))
                ? null
                : reader.GetString(reader.GetOrdinal("provider_code"))
        };
    }
}
