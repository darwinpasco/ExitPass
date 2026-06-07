namespace ExitPass.CentralPms.Application.Abstractions.Persistence;

/// <summary>
/// Effective payable-basis tariff snapshot resolved from an APPLIED statutory discount application.
/// </summary>
public sealed class EffectiveTariffSnapshotResolution
{
    /// <summary>
    /// Parking session whose effective payable basis was resolved.
    /// </summary>
    public Guid ParkingSessionId { get; init; }

    /// <summary>
    /// Payable-basis application that established the effective snapshot.
    /// </summary>
    public Guid? StatutoryDiscountApplicationId { get; init; }

    /// <summary>
    /// Original tariff snapshot superseded by the statutory discount application.
    /// </summary>
    public Guid? OriginalTariffSnapshotId { get; init; }

    /// <summary>
    /// Effective tariff snapshot that payment attempt creation must consume.
    /// </summary>
    public Guid? AppliedTariffSnapshotId { get; init; }

    /// <summary>
    /// Indicates whether the APPLIED payable-basis application points to a valid ACTIVE applied tariff snapshot.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Deterministic reason code when the effective payable basis is invalid.
    /// </summary>
    public string? InvalidReasonCode { get; init; }
}
