namespace ExitPass.CentralPms.Application.VendorParking;

/// <summary>Reads durable tariff provenance before exit or vendor-side effects.</summary>
public interface IContinuityTariffProvenanceReader
{
    Task<bool> IsContinuityTariffAsync(
        Guid parkingSessionId,
        Guid? tariffSnapshotId,
        Guid? paymentAttemptId,
        CancellationToken cancellationToken);
}
