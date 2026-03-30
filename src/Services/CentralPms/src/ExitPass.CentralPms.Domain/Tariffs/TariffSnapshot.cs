using ExitPass.CentralPms.Domain.Common;
using ExitPass.CentralPms.Domain.Tariffs.Exceptions;

namespace ExitPass.CentralPms.Domain.Tariffs;

public sealed class TariffSnapshot
{
    public Guid TariffSnapshotId { get; private set; }
    public Guid ParkingSessionId { get; private set; }
    public TariffSnapshotSourceType SourceType { get; private set; }
    public decimal GrossAmount { get; private set; }
    public decimal StatutoryDiscountAmount { get; private set; }
    public decimal CouponDiscountAmount { get; private set; }
    public decimal NetPayable { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public decimal? BaseFeeAmount { get; private set; }
    public string? TariffVersionReference { get; private set; }
    public string? PolicyVersionReference { get; private set; }
    public DateTimeOffset CalculatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public TariffSnapshotStatus SnapshotStatus { get; private set; }
    public Guid? SupersedesTariffSnapshotId { get; private set; }
    public Guid? ConsumedByPaymentAttemptId { get; private set; }

    public bool IsActive() => SnapshotStatus == TariffSnapshotStatus.Active;

    public bool IsExpired(ISystemClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return ExpiresAt <= clock.UtcNow;
    }

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
            throw new TariffSnapshotNotEligibleException(TariffSnapshotId, SnapshotStatus, ExpiresAt, ConsumedByPaymentAttemptId);
        }
    }
}