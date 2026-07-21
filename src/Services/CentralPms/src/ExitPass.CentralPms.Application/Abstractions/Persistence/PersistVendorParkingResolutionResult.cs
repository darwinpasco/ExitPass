using ExitPass.CentralPms.Domain.Sessions;
using ExitPass.CentralPms.Domain.Tariffs;

namespace ExitPass.CentralPms.Application.Abstractions.Persistence;

/// <summary>
/// Persistence result for a vendor-resolved parking session and tariff snapshot.
/// </summary>
public sealed class PersistVendorParkingResolutionResult
{
    /// <summary>
    /// Persisted or reused Central PMS parking session.
    /// </summary>
    public ParkingSession ParkingSession { get; init; } = null!;

    /// <summary>
    /// Persisted or reused Central PMS tariff snapshot.
    /// </summary>
    public TariffSnapshot TariffSnapshot { get; init; } = null!;

    /// <summary>
    /// Indicates whether an existing Central PMS parking session was reused.
    /// </summary>
    public bool ParkingSessionWasReused { get; init; }

    /// <summary>
    /// Indicates whether an existing Central PMS tariff snapshot was reused.
    /// </summary>
    public bool TariffSnapshotWasReused { get; init; }

    /// <summary>
    /// Canonical vendor system identifier for the persisted or reused Central PMS parking session.
    /// </summary>
    public string VendorSystemId { get; init; } = string.Empty;

    /// <summary>
    /// Business-friendly site group name resolved from canonical site data.
    /// </summary>
    public string? SiteGroupName { get; init; }

    /// <summary>
    /// Business-friendly site name resolved from canonical site data.
    /// </summary>
    public string? SiteName { get; init; }

    /// <summary>
    /// Current payment status display value derived from authoritative payment attempts.
    /// </summary>
    public string PaymentStatus { get; init; } = "Not Started";

    /// <summary>
    /// Applied statutory discount payable-basis summary when the effective tariff snapshot is statutory-adjusted.
    /// </summary>
    public EffectivePayableBasisSummary? EffectivePayableBasis { get; init; }
}

/// <summary>
/// Read-model summary for the effective payable basis selected for WebPay display.
/// </summary>
public sealed class EffectivePayableBasisSummary
{
    /// <summary>
    /// Indicates whether the effective payable basis came from an APPLIED statutory discount application.
    /// </summary>
    public bool StatutoryDiscountApplied { get; init; }

    /// <summary>
    /// Statutory discount validation linked to the applied payable basis.
    /// </summary>
    public Guid? StatutoryDiscountValidationId { get; init; }

    /// <summary>
    /// Payable-basis application linked to the applied tariff snapshot.
    /// </summary>
    public Guid? StatutoryDiscountApplicationId { get; init; }

    /// <summary>
    /// Canonical shared statutory-discount decision command linked to the applied payable basis.
    /// </summary>
    public Guid? StatutoryDiscountDecisionCommandId { get; init; }

    /// <summary>
    /// Original tariff snapshot superseded by the applied statutory discount snapshot.
    /// </summary>
    public Guid? OriginalTariffSnapshotId { get; init; }

    /// <summary>
    /// Effective tariff snapshot used for amount due display and payment handoff.
    /// </summary>
    public Guid EffectiveTariffSnapshotId { get; init; }

    /// <summary>
    /// Applied statutory-discount-adjusted tariff snapshot when present.
    /// </summary>
    public Guid? AppliedTariffSnapshotId { get; init; }

    /// <summary>
    /// Policy resolution basis captured on the statutory discount validation.
    /// </summary>
    public string? PolicyResolutionBasis { get; init; }

    /// <summary>
    /// Applied policy reference captured on the canonical shared decision command.
    /// </summary>
    public Guid? AppliedPolicyReferenceId { get; init; }

    /// <summary>
    /// Policy benefit type captured in the payable-basis computation policy context.
    /// </summary>
    public string? BenefitType { get; init; }

    /// <summary>
    /// Entitlement type captured on the canonical shared decision command.
    /// </summary>
    public string? EntitlementType { get; init; }

    /// <summary>
    /// Approved statutory discount amount in minor units.
    /// </summary>
    public long? StatutoryDiscountAmountMinorUnits { get; init; }

    /// <summary>
    /// Final payable amount in minor units after the statutory discount.
    /// </summary>
    public long? FinalPayableAmountMinorUnits { get; init; }

    /// <summary>
    /// Timestamp when the canonical shared decision was decided or completed.
    /// </summary>
    public DateTimeOffset? StatutoryDiscountDecisionTimestamp { get; init; }
}
