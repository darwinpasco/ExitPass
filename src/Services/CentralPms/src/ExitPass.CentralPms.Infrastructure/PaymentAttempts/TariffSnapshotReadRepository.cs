using System.Data;
using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Domain.Tariffs;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.PaymentAttempts;

/// <summary>
/// Repository for reading immutable payable-basis records from Central PMS persistence.
///
/// BRD v1.2:
/// - Section 9.9 Payment Initiation
/// - Section 10.7.3 Tariff Snapshot Integrity Invariant
///
/// SDD v1.2:
/// - Section 6.3 Initiate Payment Attempt
/// - Section 8.2 TariffSnapshot State Machine
///
/// ExitPass v1.2 Database Design:
/// - Section 12.5.3 TariffSnapshot
/// - Section 12.5.8 Core Relationships
/// - Section 12.5.9 Cross-Entity Invariants
/// - Section 13.1.3 core.tariff_snapshots
///
/// Invariants enforced:
/// - Payment initiation must use the stored immutable TariffSnapshot as the payable basis.
/// - TariffSnapshot eligibility must be determined from canonical persistence state.
/// - A consumed, expired, invalidated, or superseded snapshot must not be reused for a new payment attempt.
/// - The repository must not read obsolete v1.0/v1.1 tariff snapshot columns.
/// </summary>
public sealed class TariffSnapshotReadRepository : ITariffSnapshotReadRepository
{
    private readonly string _connectionString;

    /// <summary>
    /// Creates a repository using the configured Central PMS PostgreSQL connection string.
    ///
    /// BRD v1.2:
    /// - Section 9.9 Payment Initiation
    ///
    /// SDD v1.2:
    /// - Section 6.3 Initiate Payment Attempt
    ///
    /// ExitPass v1.2 Database Design:
    /// - Section 13.1.3 core.tariff_snapshots
    ///
    /// Invariant enforced:
    /// - Payment initiation must fail closed when authoritative persistence is not configured.
    /// </summary>
    public TariffSnapshotReadRepository(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _connectionString = configuration.GetConnectionString("MainDatabase")
            ?? throw new InvalidOperationException("Connection string 'MainDatabase' is missing.");
    }

    /// <summary>
    /// Reads one immutable TariffSnapshot by its v1.2 UUID identifier.
    ///
    /// BRD v1.2:
    /// - Section 9.9 Payment Initiation
    /// - Section 10.7.3 Tariff Snapshot Integrity Invariant
    ///
    /// SDD v1.2:
    /// - Section 6.3 Initiate Payment Attempt
    /// - Section 8.2 TariffSnapshot State Machine
    ///
    /// ExitPass v1.2 Database Design:
    /// - Section 12.5.3 TariffSnapshot
    /// - Section 12.5.8 Core Relationships
    /// - Section 12.5.9 Cross-Entity Invariants
    /// - Section 13.1.3 core.tariff_snapshots
    ///
    /// Invariants enforced:
    /// - TariffSnapshot is the immutable payable basis for PaymentAttempt creation.
    /// - PaymentAttempt creation must not bypass the TariffSnapshot selected by Central PMS.
    /// - ACTIVE means the snapshot may still be used to create a payment attempt.
    /// - CONSUMED means it has already been bound to a payment attempt.
    /// </summary>
    public async Task<TariffSnapshot?> GetByIdAsync(
        Guid tariffSnapshotId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            /*
             * BRD v1.2:
             * - Section 9.9 Payment Initiation
             * - Section 10.7.3 Tariff Snapshot Integrity Invariant
             *
             * SDD v1.2:
             * - Section 6.3 Initiate Payment Attempt
             * - Section 8.2 TariffSnapshot State Machine
             *
             * ExitPass v1.2 Database Design:
             * - Section 12.5.3 TariffSnapshot
             * - Section 12.5.8 Core Relationships
             * - Section 12.5.9 Cross-Entity Invariants
             * - Section 13.1.3 core.tariff_snapshots
             *
             * Invariants enforced:
             * - TariffSnapshot is the immutable payable basis for payment initiation.
             * - The v1.2 payable amount is core.tariff_snapshots.net_amount.
             * - Obsolete v1.0/v1.1 columns are not read from core.tariff_snapshots.
             */
            SELECT
                ts.tariff_snapshot_id,
                ts.parking_session_id,

                CASE
                    WHEN ts.coupon_discount_amount > 0 THEN 'COUPON_ADJUSTED'
                    WHEN ts.statutory_discount_amount > 0 THEN 'STATUTORY_ADJUSTED'
                    ELSE 'BASE'
                END AS source_type,

                ts.gross_amount,
                ts.statutory_discount_amount,
                ts.coupon_discount_amount,
                ts.net_amount AS net_payable,
                ts.currency_code,
                ts.gross_amount AS base_fee_amount,
                ts.tariff_version_reference,
                NULL::varchar AS policy_version_reference,
                ts.calculated_at,
                ts.expires_at,
                ts.snapshot_status::text AS snapshot_status,
                ts.superseded_by_tariff_snapshot_id AS supersedes_tariff_snapshot_id,
                NULL::uuid AS consumed_by_payment_attempt_id
            FROM core.tariff_snapshots AS ts
            WHERE ts.tariff_snapshot_id = @tariff_snapshot_id
            LIMIT 1;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        command.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value = tariffSnapshotId;

        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SingleRow,
            cancellationToken);

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
            baseFeeAmount: reader.GetDecimal(reader.GetOrdinal("base_fee_amount")),
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

    /// <summary>
    /// Maps the compatibility source type projected from the v1.2 tariff snapshot amounts.
    ///
    /// BRD v1.2:
    /// - Section 10.7.3 Tariff Snapshot Integrity Invariant
    ///
    /// SDD v1.2:
    /// - Section 8.2 TariffSnapshot State Machine
    ///
    /// ExitPass v1.2 Database Design:
    /// - Section 13.1.3 core.tariff_snapshots
    ///
    /// Invariant enforced:
    /// - The repository adapts the v1.2 physical model without reintroducing obsolete source_type storage.
    /// </summary>
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

    /// <summary>
    /// Maps the v1.2 tariff snapshot lifecycle status into the current domain model.
    ///
    /// BRD v1.2:
    /// - Section 9.9 Payment Initiation
    /// - Section 10.7.3 Tariff Snapshot Integrity Invariant
    ///
    /// SDD v1.2:
    /// - Section 8.2 TariffSnapshot State Machine
    ///
    /// ExitPass v1.2 Database Design:
    /// - Section 13.1.3 core.tariff_snapshots
    ///
    /// Invariants enforced:
    /// - ACTIVE snapshots remain eligible for payment attempt creation.
    /// - CONSUMED, EXPIRED, SUPERSEDED, and INVALIDATED snapshots must not be treated as reusable payable bases.
    /// </summary>
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
