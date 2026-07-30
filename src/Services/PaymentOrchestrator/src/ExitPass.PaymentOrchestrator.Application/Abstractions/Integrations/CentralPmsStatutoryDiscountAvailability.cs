namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;

/// <summary>
/// Central PMS statutory parking local-ordinance availability request.
/// </summary>
/// <param name="RequestReference">Non-secret request reference used for safe support correlation.</param>
/// <param name="ParkingSessionId">Canonical parking session identifier.</param>
/// <param name="RequestedEntitlementType">Optional entitlement filter.</param>
/// <param name="BeneficiaryResidencySatisfied">Optional beneficiary residency posture when available.</param>
public sealed record CentralPmsStatutoryDiscountAvailabilityRequest(
    Guid RequestReference,
    Guid ParkingSessionId,
    string? RequestedEntitlementType,
    bool? BeneficiaryResidencySatisfied);

/// <summary>
/// Central PMS authoritative statutory parking local-ordinance availability result.
/// </summary>
/// <param name="RequestReference">Non-secret request reference returned by Central PMS.</param>
/// <param name="ParkingSessionId">Canonical parking session identifier.</param>
/// <param name="SiteId">Resolved Site identifier.</param>
/// <param name="SiteGroupId">Resolved Site Group identifier.</param>
/// <param name="AvailabilityStatus">Authoritative availability status.</param>
/// <param name="StatutoryParkingBenefitAvailable">Indicates whether Central PMS reports an active statutory parking benefit.</param>
/// <param name="CoveredEntitlementTypes">Entitlement types covered by the authoritative result.</param>
/// <param name="RequestedEntitlementType">Requested entitlement filter echoed by Central PMS when supplied.</param>
/// <param name="SafeReasonCode">Browser-safe reason code.</param>
/// <param name="Retryable">Indicates whether the availability request can be retried.</param>
/// <param name="RemediationAction">Browser-safe remediation action.</param>
/// <param name="RequiredEvidenceTypes">Evidence requirement metadata returned for active coverage.</param>
/// <param name="CorrelationId">Correlation identifier for diagnostics and support.</param>
public sealed record CentralPmsStatutoryDiscountAvailability(
    Guid RequestReference,
    Guid ParkingSessionId,
    Guid? SiteId,
    Guid? SiteGroupId,
    string AvailabilityStatus,
    bool StatutoryParkingBenefitAvailable,
    IReadOnlyList<string> CoveredEntitlementTypes,
    string? RequestedEntitlementType,
    string? SafeReasonCode,
    bool Retryable,
    string RemediationAction,
    IReadOnlyList<CentralPmsStatutoryDiscountAvailabilityEvidenceRequirement> RequiredEvidenceTypes,
    Guid CorrelationId)
{
    /// <summary>
    /// Returns true only when Central PMS explicitly reports active coverage for the entitlement.
    /// </summary>
    public bool Covers(string entitlementType) =>
        StatutoryParkingBenefitAvailable &&
        string.Equals(AvailabilityStatus, "AVAILABLE", StringComparison.OrdinalIgnoreCase) &&
        CoveredEntitlementTypes.Any(covered => string.Equals(covered, entitlementType, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Browser-safe evidence requirement metadata returned by Central PMS availability.
/// </summary>
/// <param name="EvidenceType">Safe evidence type code.</param>
/// <param name="RequirementStatus">Requirement status.</param>
/// <param name="SafeRequirementLabel">Customer-safe requirement label.</param>
/// <param name="SafeRequirementNotes">Optional customer-safe requirement notes.</param>
public sealed record CentralPmsStatutoryDiscountAvailabilityEvidenceRequirement(
    string EvidenceType,
    string RequirementStatus,
    string SafeRequirementLabel,
    string? SafeRequirementNotes);
