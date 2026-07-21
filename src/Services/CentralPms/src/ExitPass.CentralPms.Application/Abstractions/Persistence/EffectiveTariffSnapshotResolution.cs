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
    /// Canonical shared statutory-discount decision command linked to the applied payable basis, when present.
    /// </summary>
    public Guid? StatutoryDiscountDecisionCommandId { get; init; }

    /// <summary>
    /// Statutory discount validation linked to the applied payable basis, when present.
    /// </summary>
    public Guid? StatutoryDiscountValidationId { get; init; }

    /// <summary>
    /// Original tariff snapshot superseded by the statutory discount application.
    /// </summary>
    public Guid? OriginalTariffSnapshotId { get; init; }

    /// <summary>
    /// Effective tariff snapshot that payment attempt creation must consume.
    /// </summary>
    public Guid? AppliedTariffSnapshotId { get; init; }

    /// <summary>
    /// Policy-resolution reference associated with the canonical decision, when present.
    /// </summary>
    public Guid? StatutoryDiscountPolicyReferenceId { get; init; }

    /// <summary>
    /// Policy-resolution basis associated with the canonical decision, when present.
    /// </summary>
    public string? PolicyResolutionBasis { get; init; }

    /// <summary>
    /// Entitlement type associated with the canonical decision, when present.
    /// </summary>
    public string? EntitlementType { get; init; }

    /// <summary>
    /// Approved statutory discount amount in minor units, when present.
    /// </summary>
    public long? StatutoryDiscountAmountMinorUnits { get; init; }

    /// <summary>
    /// Final payable amount in minor units after the statutory discount, when present.
    /// </summary>
    public long? FinalPayableAmountMinorUnits { get; init; }

    /// <summary>
    /// Timestamp when the canonical decision was decided or completed, when present.
    /// </summary>
    public DateTimeOffset? DecisionTimestamp { get; init; }

    /// <summary>
    /// Indicates whether the APPLIED payable-basis application points to a valid ACTIVE applied tariff snapshot.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Deterministic reason code when the effective payable basis is invalid.
    /// </summary>
    public string? InvalidReasonCode { get; init; }
}
