namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;

/// <summary>
/// Provider-neutral parking and tariff data resolved by Central PMS.
/// </summary>
/// <param name="ParkingSessionId">Canonical Central PMS parking session identifier.</param>
/// <param name="TariffSnapshotId">Canonical Central PMS tariff snapshot identifier.</param>
/// <param name="NetPayableMinorUnits">Payable amount in minor currency units.</param>
/// <param name="Currency">ISO currency code.</param>
/// <param name="VendorSystemId">Provider-neutral vendor system identifier.</param>
/// <param name="CorrelationId">End-to-end correlation identifier.</param>
/// <param name="SiteName">Optional parking site display name.</param>
/// <param name="TicketReference">Optional resolved ticket reference.</param>
/// <param name="PlateNumber">Optional resolved plate number.</param>
/// <param name="EntryTime">Optional parking entry time.</param>
/// <param name="CurrentFeeCalculationTime">Optional tariff calculation time.</param>
/// <param name="TariffName">Optional tariff display name.</param>
/// <param name="ParkingStatus">Optional parking session status.</param>
/// <param name="FeeValidUntil">Optional authoritative fee validity timestamp.</param>
/// <param name="PaymentStatus">Optional current payment status.</param>
/// <param name="SiteGroupId">Optional resolved site group identifier.</param>
/// <param name="SiteId">Optional resolved site identifier.</param>
/// <param name="SiteGroupName">Optional site group display name.</param>
public sealed record CentralPmsResolvedParking(
    Guid ParkingSessionId,
    Guid TariffSnapshotId,
    long NetPayableMinorUnits,
    string Currency,
    string VendorSystemId,
    Guid CorrelationId,
    string? SiteName = null,
    string? TicketReference = null,
    string? PlateNumber = null,
    DateTimeOffset? EntryTime = null,
    DateTimeOffset? CurrentFeeCalculationTime = null,
    string? TariffName = null,
    string? ParkingStatus = null,
    DateTimeOffset? FeeValidUntil = null,
    string? PaymentStatus = null,
    Guid? SiteGroupId = null,
    Guid? SiteId = null,
    string? SiteGroupName = null);
