namespace ExitPass.CentralPms.Contracts.Public.VendorParking;

/// <summary>
/// Public API response returned after vendor parking session and tariff resolution succeeds.
/// </summary>
public sealed class ResolveVendorParkingResponse
{
    /// <summary>
    /// Central PMS parking session identifier resolved for the vendor parking session.
    /// </summary>
    public Guid? ParkingSessionId { get; set; }

    /// <summary>
    /// Central PMS tariff snapshot identifier resolved for the vendor tariff quote.
    /// </summary>
    public Guid? TariffSnapshotId { get; set; }

    /// <summary>
    /// Indicates that a safe live or projected session identity was found.
    /// </summary>
    public bool SessionFound { get; set; }

    /// <summary>
    /// Identifies whether session facts came from live vendor resolution or the continuity projection.
    /// </summary>
    public string SessionSource { get; set; } = string.Empty;

    /// <summary>
    /// Indicates that live vendor resolution did not provide an authoritative payable session.
    /// </summary>
    public bool Degraded { get; set; }

    /// <summary>
    /// Indicates that authoritative tariff and payable-basis facts accompany the session.
    /// </summary>
    public bool PayableBasisAvailable { get; set; }

    /// <summary>
    /// Site group that owns the resolved parking session.
    /// </summary>
    public string SiteGroupId { get; set; } = string.Empty;

    /// <summary>
    /// Site that owns the resolved parking session.
    /// </summary>
    public string SiteId { get; set; } = string.Empty;

    /// <summary>
    /// Business-friendly site group name, when available.
    /// </summary>
    public string? SiteGroupName { get; set; }

    /// <summary>
    /// Business-friendly site name, when available.
    /// </summary>
    public string? SiteName { get; set; }

    /// <summary>
    /// Provider-neutral lookup outcome.
    /// </summary>
    public string LookupOutcome { get; set; } = string.Empty;

    /// <summary>
    /// Vehicle plate number associated with the resolved parking session.
    /// </summary>
    public string? PlateNumber { get; set; }

    /// <summary>
    /// Ticket reference associated with the resolved parking session.
    /// </summary>
    public string? TicketReference { get; set; }

    /// <summary>
    /// Parking entry timestamp from the resolved parking session.
    /// </summary>
    public DateTimeOffset? EntryTime { get; set; }

    /// <summary>
    /// Timestamp used for the current tariff calculation.
    /// </summary>
    public DateTimeOffset? CurrentFeeCalculationTime { get; set; }

    /// <summary>
    /// Net payable amount in minor currency units.
    /// </summary>
    public long? NetPayableMinorUnits { get; set; }

    /// <summary>
    /// Indicates whether canonical Sales Invoice customer information exists for this parking session.
    /// No customer-entered values are exposed by this flag.
    /// </summary>
    public bool? CustomerInformationSubmitted { get; set; }

    /// <summary>
    /// ISO currency code for the resolved tariff quote.
    /// </summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Timestamp after which the tariff snapshot should not be used for payment initiation.
    /// </summary>
    public DateTimeOffset? TariffExpiresAt { get; set; }

    /// <summary>
    /// Tariff snapshot expiry used by WebPay as the fee-valid-until boundary.
    /// </summary>
    public DateTimeOffset? FeeValidUntil { get; set; }

    /// <summary>
    /// Current parking session status.
    /// </summary>
    public string? ParkingStatus { get; set; }

    /// <summary>
    /// Current payment attempt or confirmation status for WebPay display.
    /// </summary>
    public string? PaymentStatus { get; set; }

    /// <summary>
    /// Projection row used for continuity lookup, when the response is degraded.
    /// </summary>
    public Guid? VendorSessionProjectionId { get; set; }

    /// <summary>
    /// Lifecycle status of the projected session.
    /// </summary>
    public string? ProjectionStatus { get; set; }

    /// <summary>
    /// Last completed successful projection refresh used for freshness enforcement.
    /// </summary>
    public DateTimeOffset? ProjectionLastRefreshedAt { get; set; }

    /// <summary>
    /// Age of the successful projection refresh in seconds.
    /// </summary>
    public double? ProjectionFreshnessAgeSeconds { get; set; }

    /// <summary>
    /// Bounded original live lookup failure retained for operational diagnosis.
    /// </summary>
    public string? LiveLookupErrorCode { get; set; }

    /// <summary>
    /// Indicates whether the payable basis displayed by WebPay includes an applied statutory discount.
    /// </summary>
    public bool StatutoryDiscountApplied { get; set; }

    /// <summary>
    /// Statutory discount validation linked to the effective payable basis, when applicable.
    /// </summary>
    public Guid? StatutoryDiscountValidationId { get; set; }

    /// <summary>
    /// Statutory discount payable-basis application linked to the effective payable basis, when applicable.
    /// </summary>
    public Guid? StatutoryDiscountApplicationId { get; set; }

    /// <summary>
    /// Canonical shared statutory-discount decision command linked to the effective payable basis, when applicable.
    /// </summary>
    public Guid? StatutoryDiscountDecisionCommandId { get; set; }

    /// <summary>
    /// Original tariff snapshot superseded by the applied statutory discount snapshot, when applicable.
    /// </summary>
    public Guid? OriginalTariffSnapshotId { get; set; }

    /// <summary>
    /// Effective tariff snapshot used as the payable amount basis.
    /// </summary>
    public Guid? EffectiveTariffSnapshotId { get; set; }

    /// <summary>
    /// Applied statutory-discount-adjusted tariff snapshot, when applicable.
    /// </summary>
    public Guid? AppliedTariffSnapshotId { get; set; }

    /// <summary>
    /// Policy resolution basis captured with the statutory discount validation, when applicable.
    /// </summary>
    public string? PolicyResolutionBasis { get; set; }

    /// <summary>
    /// Applied policy reference linked to the canonical statutory-discount decision, when applicable.
    /// </summary>
    public Guid? StatutoryDiscountPolicyReferenceId { get; set; }

    /// <summary>
    /// Benefit type captured in the applied payable-basis computation policy context, when applicable.
    /// </summary>
    public string? BenefitType { get; set; }

    /// <summary>
    /// Entitlement type linked to the canonical statutory-discount decision, when applicable.
    /// </summary>
    public string? StatutoryDiscountEntitlementType { get; set; }

    /// <summary>
    /// Approved statutory discount amount in minor units, when applicable.
    /// </summary>
    public long? StatutoryDiscountAmountMinorUnits { get; set; }

    /// <summary>
    /// Final payable amount in minor units after statutory discount, when applicable.
    /// </summary>
    public long? StatutoryDiscountFinalPayableMinorUnits { get; set; }

    /// <summary>
    /// Canonical statutory-discount decision timestamp, when applicable.
    /// </summary>
    public DateTimeOffset? StatutoryDiscountDecisionTimestamp { get; set; }

    /// <summary>
    /// Provider-neutral vendor system identifier used for the lookup.
    /// </summary>
    public string VendorSystemId { get; set; } = string.Empty;

    /// <summary>
    /// End-to-end correlation identifier.
    /// </summary>
    public Guid CorrelationId { get; set; }
}
