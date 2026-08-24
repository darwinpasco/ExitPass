using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.Payments;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ExitPass.CentralPms.Infrastructure.Payments;

public sealed class PostgresDigitalPaymentFiscalContextReader : IDigitalPaymentFiscalContextReader
{
    private readonly string _connectionString;
    private readonly FiscalIssuancePosServerIntegrationOptions _options;

    public PostgresDigitalPaymentFiscalContextReader(
        string connectionString,
        IOptions<FiscalIssuancePosServerIntegrationOptions> options)
    {
        _connectionString = connectionString;
        _options = options.Value;
    }

    public async Task<DigitalPaymentFiscalContext> ReadAsync(
        Guid paymentAttemptId,
        Guid paymentConfirmationId,
        Guid parkingSessionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                ps.site_id,
                pa.tariff_snapshot_id,
                pa.amount,
                pa.currency_code::text,
                pc.verified_timestamp,
                ts.statutory_discount_validation_id
            FROM core.payment_attempts pa
            INNER JOIN core.payment_confirmations pc
                ON pc.payment_attempt_id = pa.payment_attempt_id
            INNER JOIN core.parking_sessions ps
                ON ps.parking_session_id = pa.parking_session_id
            INNER JOIN core.tariff_snapshots ts
                ON ts.tariff_snapshot_id = pa.tariff_snapshot_id
               AND ts.parking_session_id = pa.parking_session_id
            WHERE pa.payment_attempt_id = @payment_attempt_id
              AND pc.payment_confirmation_id = @payment_confirmation_id
              AND pa.parking_session_id = @parking_session_id
              AND pa.attempt_status = 'CONFIRMED'
              AND pc.provider_signature_valid = true
              AND pa.amount = ts.net_amount
              AND pa.currency_code = ts.currency_code;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.AddWithValue("payment_attempt_id", paymentAttemptId);
        command.Parameters.AddWithValue("payment_confirmation_id", paymentConfirmationId);
        command.Parameters.AddWithValue("parking_session_id", parkingSessionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("digital_payment_fiscal_context_not_found");
        }

        if (!reader.IsDBNull(reader.GetOrdinal("statutory_discount_validation_id")))
        {
            throw new InvalidOperationException("digital_payment_statutory_fiscal_context_not_supported");
        }

        var siteId = reader.GetGuid(reader.GetOrdinal("site_id"));
        var endpointMatches = _options.Endpoints
            .Where(endpoint => endpoint.SiteId == siteId)
            .ToArray();
        if (endpointMatches.Length != 1)
        {
            throw new InvalidOperationException(
                endpointMatches.Length == 0
                    ? "site_pos_server_site_binding_not_found"
                    : "site_pos_server_site_binding_ambiguous");
        }

        var endpoint = endpointMatches[0];
        if (!endpoint.Enabled ||
            !string.Equals(endpoint.Environment, _options.RuntimeEnvironment, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("site_pos_server_site_binding_inactive");
        }

        var amount = reader.GetDecimal(reader.GetOrdinal("amount"));
        var amountMinorUnits = checked((long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));

        return new DigitalPaymentFiscalContext(
            siteId,
            reader.GetGuid(reader.GetOrdinal("tariff_snapshot_id")),
            amountMinorUnits,
            reader.GetString(reader.GetOrdinal("currency_code")).Trim().ToUpperInvariant(),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("verified_timestamp")),
            endpoint.SitePosServerId,
            endpoint.SitePosServerRef!.Trim());
    }
}
