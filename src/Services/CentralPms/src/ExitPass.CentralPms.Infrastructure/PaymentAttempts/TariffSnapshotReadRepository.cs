
using System.Data;
using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Domain.Tariffs;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace ExitPass.CentralPms.Infrastructure.PaymentAttempts;

public sealed class TariffSnapshotReadRepository : ITariffSnapshotReadRepository
{
    private readonly string _connectionString;

    public TariffSnapshotReadRepository(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _connectionString = configuration.GetConnectionString("MainDatabase")
            ?? throw new InvalidOperationException("Connection string 'MainDatabase' is missing.");
    }

    /// <summary>
    /// BRD:
    /// - 9.9 Payment Initiation
    /// - 10.7.3 Tariff Snapshot Integrity Invariant
    ///
    /// SDD:
    /// - 6.3 Initiate Payment Attempt
    /// - 8.2 TariffSnapshot State Machine
    ///
    /// Invariants Enforced:
    /// - payment initiation must use the stored immutable TariffSnapshot as the payable basis
    /// - TariffSnapshot eligibility must be determined from canonical persistence state
    /// - consumed, expired, invalidated, or superseded snapshots must not be reused
    /// </summary>
    public async Task<TariffSnapshot?> GetByIdAsync(Guid tariffSnapshotId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                tariff_snapshot_id,
                parking_session_id,
                source_type,
                gross_amount,
                statutory_discount_amount,
                coupon_discount_amount,
                net_payable,
                currency_code,
                base_fee_amount,
                tariff_version_reference,
                policy_version_reference,
                calculated_at,
                expires_at,
                snapshot_status,
                supersedes_tariff_snapshot_id,
                consumed_by_payment_attempt_id
            FROM core.tariff_snapshots
            WHERE tariff_snapshot_id = @tariff_snapshot_id
            LIMIT 1;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tariff_snapshot_id", tariffSnapshotId);

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return TariffSnapshot.Rehydrate(
            tariffSnapshotId: reader.GetGuid(reader.GetOrdinal("tariff_snapshot_id")),
            parkingSessionId: reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            sourceType: MapTariffSnapshotSourceType(reader.GetString(reader.GetOrdinal("source_type"))),
            grossAmount: reader.GetDecimal(reader.GetOrdinal("gross_amount")),
            statutoryDiscountAmount: reader.GetDecimal(reader.GetOrdinal("statutory_discount_amount")),
            couponDiscountAmount: reader.GetDecimal(reader.GetOrdinal("coupon_discount_amount")),
            netPayable: reader.GetDecimal(reader.GetOrdinal("net_payable")),
            currencyCode: reader.GetString(reader.GetOrdinal("currency_code")).Trim(),
            baseFeeAmount: reader.IsDBNull(reader.GetOrdinal("base_fee_amount"))
                ? null
                : reader.GetDecimal(reader.GetOrdinal("base_fee_amount")),
            tariffVersionReference: reader.IsDBNull(reader.GetOrdinal("tariff_version_reference"))
                ? null
                : reader.GetString(reader.GetOrdinal("tariff_version_reference")),
            policyVersionReference: reader.IsDBNull(reader.GetOrdinal("policy_version_reference"))
                ? null
                : reader.GetString(reader.GetOrdinal("policy_version_reference")),
            calculatedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("calculated_at")),
            expiresAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("expires_at")),
            snapshotStatus: MapTariffSnapshotStatus(reader.GetString(reader.GetOrdinal("snapshot_status"))),
            supersedesTariffSnapshotId: reader.IsDBNull(reader.GetOrdinal("supersedes_tariff_snapshot_id"))
                ? null
                : reader.GetGuid(reader.GetOrdinal("supersedes_tariff_snapshot_id")),
            consumedByPaymentAttemptId: reader.IsDBNull(reader.GetOrdinal("consumed_by_payment_attempt_id"))
                ? null
                : reader.GetGuid(reader.GetOrdinal("consumed_by_payment_attempt_id")));
    }

    private static TariffSnapshotSourceType MapTariffSnapshotSourceType(string dbValue)
    {
        return dbValue.ToUpperInvariant() switch
        {
            "BASE" => TariffSnapshotSourceType.Base,
            "STATUTORY_ADJUSTED" => TariffSnapshotSourceType.StatutoryAdjusted,
            "COUPON_ADJUSTED" => TariffSnapshotSourceType.CouponAdjusted,
            _ => throw new InvalidOperationException($"Unsupported tariff snapshot source type '{dbValue}'.")
        };
    }

    private static TariffSnapshotStatus MapTariffSnapshotStatus(string dbValue)
    {
        return dbValue.ToUpperInvariant() switch
        {
            "ACTIVE" => TariffSnapshotStatus.Active,
            "SUPERSEDED" => TariffSnapshotStatus.Superseded,
            "EXPIRED" => TariffSnapshotStatus.Expired,
            "CONSUMED" => TariffSnapshotStatus.Consumed,
            "INVALIDATED" => TariffSnapshotStatus.Invalidated,
            _ => throw new InvalidOperationException($"Unsupported tariff snapshot status '{dbValue}'.")
        };
    }
}
