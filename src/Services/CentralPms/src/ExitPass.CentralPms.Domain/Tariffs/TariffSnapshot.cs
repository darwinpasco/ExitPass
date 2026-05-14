using ExitPass.CentralPms.Domain.Common;
using ExitPass.CentralPms.Domain.Tariffs.Exceptions;

namespace ExitPass.CentralPms.Domain.Tariffs;

/// <summary>
/// Immutable quoted fee snapshot that binds a payable amount to a Central PMS payment attempt.
/// </summary>
public sealed class TariffSnapshot
{
    /// <summary>
    /// Canonical tariff snapshot identifier.
    /// </summary>
    public Guid TariffSnapshotId { get; private set; }

    /// <summary>
    /// Parking session for which the quote was calculated.
    /// </summary>
    public Guid ParkingSessionId { get; private set; }

    /// <summary>
    /// Source category of the quote.
    /// </summary>
    public TariffSnapshotSourceType SourceType { get; private set; }

    /// <summary>
    /// Gross amount calculated before discounts.
    /// </summary>
    public decimal GrossAmount { get; private set; }

    /// <summary>
    /// Statutory discount amount included in the quote.
    /// </summary>
    public decimal StatutoryDiscountAmount { get; private set; }

    /// <summary>
    /// Coupon discount amount included in the quote.
    /// </summary>
    public decimal CouponDiscountAmount { get; private set; }

    /// <summary>
    /// Net payable amount that the payment attempt must collect.
    /// </summary>
    public decimal NetPayable { get; private set; }

    /// <summary>
    /// Currency code for the quoted amount.
    /// </summary>
    public string CurrencyCode { get; private set; } = string.Empty;

    /// <summary>
    /// Base fee component when available from the tariff calculation.
    /// </summary>
    public decimal? BaseFeeAmount { get; private set; }

    /// <summary>
    /// Tariff version used to calculate the quote.
    /// </summary>
    public string? TariffVersionReference { get; private set; }

    /// <summary>
    /// Policy version used to calculate discount or eligibility behavior.
    /// </summary>
    public string? PolicyVersionReference { get; private set; }

    /// <summary>
    /// Timestamp when the quote was calculated.
    /// </summary>
    public DateTimeOffset CalculatedAt { get; private set; }

    /// <summary>
    /// Timestamp after which the quote must not back a payment attempt.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>
    /// Current snapshot state used for payment attempt eligibility.
    /// </summary>
    public TariffSnapshotStatus SnapshotStatus { get; private set; }

    /// <summary>
    /// Prior snapshot replaced by this quote, when applicable.
    /// </summary>
    public Guid? SupersedesTariffSnapshotId { get; private set; }

    /// <summary>
    /// Payment attempt that already consumed this snapshot, when applicable.
    /// </summary>
    public Guid? ConsumedByPaymentAttemptId { get; private set; }

    private TariffSnapshot()
    {
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
    /// - each TariffSnapshot belongs to exactly one ParkingSession
    /// - the snapshot is the immutable payable basis for payment attempt creation
    /// - consumed, expired, invalidated, or superseded snapshots must not be reused
    /// </summary>
    public static TariffSnapshot Rehydrate(
        Guid tariffSnapshotId,
        Guid parkingSessionId,
        TariffSnapshotSourceType sourceType,
        decimal grossAmount,
        decimal statutoryDiscountAmount,
        decimal couponDiscountAmount,
        decimal netPayable,
        string currencyCode,
        decimal? baseFeeAmount,
        string? tariffVersionReference,
        string? policyVersionReference,
        DateTimeOffset calculatedAt,
        DateTimeOffset expiresAt,
        TariffSnapshotStatus snapshotStatus,
        Guid? supersedesTariffSnapshotId,
        Guid? consumedByPaymentAttemptId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currencyCode);

        return new TariffSnapshot
        {
            TariffSnapshotId = tariffSnapshotId,
            ParkingSessionId = parkingSessionId,
            SourceType = sourceType,
            GrossAmount = grossAmount,
            StatutoryDiscountAmount = statutoryDiscountAmount,
            CouponDiscountAmount = couponDiscountAmount,
            NetPayable = netPayable,
            CurrencyCode = currencyCode,
            BaseFeeAmount = baseFeeAmount,
            TariffVersionReference = tariffVersionReference,
            PolicyVersionReference = policyVersionReference,
            CalculatedAt = calculatedAt,
            ExpiresAt = expiresAt,
            SnapshotStatus = snapshotStatus,
            SupersedesTariffSnapshotId = supersedesTariffSnapshotId,
            ConsumedByPaymentAttemptId = consumedByPaymentAttemptId
        };
    }

    /// <summary>
    /// Returns whether the snapshot is in the active lifecycle state.
    /// </summary>
    public bool IsActive() => SnapshotStatus == TariffSnapshotStatus.Active;

    /// <summary>
    /// Returns whether the snapshot has passed its payable validity window.
    /// </summary>
    public bool IsExpired(ISystemClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return ExpiresAt <= clock.UtcNow;
    }

    /// <summary>
    /// Returns whether the snapshot can be bound to a payment attempt.
    /// </summary>
    public bool CanBeUsedForPayment(ISystemClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return IsActive() && !IsExpired(clock) && ConsumedByPaymentAttemptId is null;
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
    /// - tariff snapshot must be ACTIVE to support payment
    /// - expired snapshot must not be reused
    /// - already-bound snapshot must not support another payment attempt
    /// </summary>
    public void EnsureEligibleForPayment(ISystemClock clock)
    {
        if (!CanBeUsedForPayment(clock))
        {
            throw new TariffSnapshotNotEligibleException(
                TariffSnapshotId,
                SnapshotStatus,
                ExpiresAt,
                ConsumedByPaymentAttemptId);
        }
    }
}
