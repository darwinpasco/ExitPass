namespace ExitPass.CentralPms.Domain.Tariffs;

/// <summary>
/// Source of the fee quote captured in a tariff snapshot.
/// </summary>
public enum TariffSnapshotSourceType
{
    /// <summary>
    /// Base tariff quote without discount adjustment.
    /// </summary>
    Base = 1,

    /// <summary>
    /// Tariff quote after statutory discount adjustment.
    /// </summary>
    StatutoryAdjusted = 2,

    /// <summary>
    /// Tariff quote after coupon adjustment.
    /// </summary>
    CouponAdjusted = 3
}
