using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Application.TerminalCashPayments;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ExitPass.CentralPms.Infrastructure.Payments;

public sealed class PostgresDigitalPaymentFiscalContextReader : IDigitalPaymentFiscalContextReader
{
    private readonly string _connectionString;
    private readonly FiscalIssuancePosServerIntegrationOptions _options;
    private readonly IStatutoryFiscalLinkageReader _statutoryFiscalLinkageReader;

    public PostgresDigitalPaymentFiscalContextReader(
        string connectionString,
        IOptions<FiscalIssuancePosServerIntegrationOptions> options,
        IStatutoryFiscalLinkageReader statutoryFiscalLinkageReader)
    {
        _connectionString = connectionString;
        _options = options.Value;
        _statutoryFiscalLinkageReader = statutoryFiscalLinkageReader;
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
                ps.site_group_id,
                pa.tariff_snapshot_id,
                pa.amount,
                pa.currency_code::text,
                pc.verified_at AS verified_timestamp,
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
              AND pc.confirmation_status = 'RECORDED'
              AND pc.confirmed_amount = pa.amount
              AND pc.currency_code = pa.currency_code
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

        var siteId = reader.GetGuid(reader.GetOrdinal("site_id"));
        var siteGroupId = reader.GetGuid(reader.GetOrdinal("site_group_id"));
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
        var currency = reader.GetString(reader.GetOrdinal("currency_code")).Trim().ToUpperInvariant();
        var tariffSnapshotId = reader.GetGuid(reader.GetOrdinal("tariff_snapshot_id"));
        var hasStatutoryDiscount = !reader.IsDBNull(reader.GetOrdinal("statutory_discount_validation_id"));
        TerminalCashStatutoryFiscalLinkageContext? statutoryContext = null;
        if (hasStatutoryDiscount)
        {
            var linkage = await _statutoryFiscalLinkageReader.ReadByAppliedTariffSnapshotAsync(
                new StatutoryFiscalLinkageSubject(
                    parkingSessionId,
                    tariffSnapshotId,
                    siteId,
                    siteGroupId,
                    amountMinorUnits,
                    currency),
                cancellationToken);
            var approvedContext = linkage.Status switch
            {
                TerminalCashStatutoryFiscalLinkageStatus.CompleteApprovedContext => linkage.Context
                    ?? throw new InvalidOperationException("STATUTORY_FISCAL_CONTEXT_INVALID"),
                TerminalCashStatutoryFiscalLinkageStatus.RetryableUnavailable =>
                    throw new InvalidOperationException(linkage.SafeErrorCode ?? "STATUTORY_FISCAL_CONTEXT_TEMPORARILY_UNAVAILABLE"),
                _ => throw new InvalidOperationException(linkage.SafeErrorCode ?? "STATUTORY_FISCAL_CONTEXT_INVALID")
            };

            if (!string.Equals(approvedContext.SourceChannel, "WEBPAY", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("STATUTORY_FISCAL_SOURCE_CHANNEL_MISMATCH");
            }

            statutoryContext = approvedContext;
        }

        return new DigitalPaymentFiscalContext(
            siteId,
            siteGroupId,
            tariffSnapshotId,
            amountMinorUnits,
            currency,
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("verified_timestamp")),
            endpoint.SitePosServerId,
            endpoint.SitePosServerRef!.Trim(),
            statutoryContext);
    }
}
