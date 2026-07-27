using System.Data;
using ExitPass.CentralPms.Application.TerminalCashPayments;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.TerminalCashPayments;

/// <summary>
/// Read-only terminal-cash payable-basis eligibility reader reused by the APT pre-cash facade.
/// </summary>
public sealed class TerminalCashPayableBasisEligibilityReader : ITerminalCashPayableBasisEligibilityReader
{
    private const string SupportedCurrency = "PHP";
    private readonly string _connectionString;

    public TerminalCashPayableBasisEligibilityReader(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    public async Task<TerminalCashPayableBasisEligibility> EvaluateAsync(
        TerminalCashPayableBasisEligibilityRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                ps.site_id,
                ps.site_group_id,
                ps.session_status::text AS session_status,
                ts.currency_code::text AS currency_code,
                ts.net_amount,
                ts.snapshot_status::text AS snapshot_status,
                ts.expires_at,
                ts.consumed_at,
                EXISTS (
                    SELECT 1
                    FROM core.payment_confirmations pc
                    INNER JOIN core.payment_attempts pa
                        ON pa.payment_attempt_id = pc.payment_attempt_id
                    WHERE pa.parking_session_id = @parking_session_id
                    LIMIT 1
                ) AS has_final_payment,
                EXISTS (
                    SELECT 1
                    FROM payments.payment_rails
                    WHERE rail_code = 'CASH'
                      AND rail_status = 'ACTIVE'
                      AND supported_currency_code = @expected_currency
                    LIMIT 1
                ) AS cash_rail_configured
            FROM core.parking_sessions ps
            INNER JOIN core.tariff_snapshots ts
                ON ts.parking_session_id = ps.parking_session_id
            WHERE ps.parking_session_id = @parking_session_id
              AND ts.tariff_snapshot_id = @tariff_snapshot_id;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = request.ParkingSessionId;
        command.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value = request.TariffSnapshotId;
        command.Parameters.Add("expected_currency", NpgsqlDbType.Char).Value = request.ExpectedCurrency.Trim().ToUpperInvariant();

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return Blocked(
                "INVALID_SESSION_TARIFF_RELATIONSHIP",
                "Parking session and tariff snapshot do not match.",
                retryable: false);
        }

        var siteId = reader.GetGuid(reader.GetOrdinal("site_id"));
        var siteGroupId = reader.GetGuid(reader.GetOrdinal("site_group_id"));
        if (siteId != request.SiteId || siteGroupId != request.SiteGroupId)
        {
            return Blocked(
                "INVALID_SESSION_TARIFF_RELATIONSHIP",
                "Submitted site context does not match the parking session.",
                retryable: false);
        }

        var sessionStatus = reader.GetString(reader.GetOrdinal("session_status"));
        if (!IsPayableSessionStatus(sessionStatus))
        {
            return Blocked("SESSION_NOT_PAYABLE", "Parking session is not payable.", retryable: false);
        }

        if (reader.GetBoolean(reader.GetOrdinal("has_final_payment")))
        {
            return Blocked(
                "PAYMENT_ALREADY_FINAL",
                "Parking session already has a canonical payment confirmation.",
                retryable: false);
        }

        var snapshotStatus = reader.GetString(reader.GetOrdinal("snapshot_status"));
        var expiresAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("expires_at"));
        if (!string.Equals(snapshotStatus, "ACTIVE", StringComparison.OrdinalIgnoreCase) ||
            expiresAt <= request.RequestedAt ||
            !reader.IsDBNull(reader.GetOrdinal("consumed_at")))
        {
            return Blocked("STALE_TARIFF", "Tariff snapshot is stale or expired.", retryable: false);
        }

        var currency = reader.GetString(reader.GetOrdinal("currency_code")).Trim().ToUpperInvariant();
        if (!string.Equals(currency, request.ExpectedCurrency.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return Blocked(
                "PAYABLE_BASIS_MISMATCH",
                "Expected currency does not match the authoritative payable basis.",
                retryable: false);
        }

        if (!string.Equals(currency, SupportedCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return Blocked(
                "UNSUPPORTED_CURRENCY",
                "Currency is not supported for terminal cash.",
                retryable: false);
        }

        var netAmountMinorUnits = decimal.ToInt64(decimal.Round(
            reader.GetDecimal(reader.GetOrdinal("net_amount")) * 100m,
            0,
            MidpointRounding.AwayFromZero));
        if (netAmountMinorUnits != request.ExpectedAmountMinorUnits)
        {
            return Blocked(
                "PAYABLE_BASIS_MISMATCH",
                "Expected amount does not match the authoritative payable basis.",
                retryable: false);
        }

        if (!reader.GetBoolean(reader.GetOrdinal("cash_rail_configured")))
        {
            return Blocked(
                "CASH_PAYMENT_RAIL_NOT_CONFIGURED",
                "Active CASH payment rail is not configured.",
                retryable: false);
        }

        return new TerminalCashPayableBasisEligibility(
            true,
            AptPayableBasisReadinessStatuses.Ready,
            null,
            false,
            "Terminal cash is available for this payable basis.");
    }

    private static bool IsPayableSessionStatus(string value) =>
        string.Equals(value, "ACTIVE", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "PAYMENT_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "PAYMENT_IN_PROGRESS", StringComparison.OrdinalIgnoreCase);

    private static TerminalCashPayableBasisEligibility Blocked(
        string code,
        string message,
        bool retryable) =>
        new(false, AptPayableBasisReadinessStatuses.Blocked, code, retryable, message);
}
