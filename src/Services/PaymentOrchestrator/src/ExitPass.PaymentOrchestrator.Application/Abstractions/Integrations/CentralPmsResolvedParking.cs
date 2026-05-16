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
public sealed record CentralPmsResolvedParking(
    Guid ParkingSessionId,
    Guid TariffSnapshotId,
    long NetPayableMinorUnits,
    string Currency,
    string VendorSystemId,
    Guid CorrelationId);
