using System.Text.Json;

namespace ExitPass.CentralPms.Contracts.OperatorConsole;

/// <summary>
/// Queue response for Operator Console statutory discount validation drafts.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountDraftQueueResponse(
    IReadOnlyList<OperatorConsoleStatutoryDiscountDraftQueueItem> Items,
    int Page,
    int PageSize,
    bool HasMore,
    Guid CorrelationId);

/// <summary>
/// Queue item for an Operator Console statutory discount validation draft.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountDraftQueueItem(
    Guid DraftId,
    Guid ParkingSessionId,
    string? TicketReference,
    string? PlateNumber,
    Guid SiteId,
    string? SiteName,
    string EntitlementType,
    string ValidationStatus,
    bool EvidenceRequired,
    bool EvidenceRequiredSatisfied,
    int EvidenceCount,
    string? LatestEvidenceStatus,
    string? PolicyResolutionBasis,
    string? PolicyCode,
    string? PolicyName,
    long? OriginalAmountMinorUnits,
    long? PayableAmountMinorUnits,
    string? CurrencyCode,
    DateTimeOffset RequestedAt,
    Guid? RequestedByUserId,
    string? BlockedReason);

/// <summary>
/// Detail response for an Operator Console statutory discount validation draft.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountDraftDetailResponse(
    Guid DraftId,
    Guid ParkingSessionId,
    string? TicketReference,
    string? PlateNumber,
    Guid SiteId,
    string? SiteName,
    Guid SiteGroupId,
    string? EntitlementType,
    string? ValidationStatus,
    bool EvidenceRequired,
    bool EvidenceCaptured,
    bool EvidenceRequiredSatisfied,
    int EvidenceCount,
    string? LatestEvidenceStatus,
    IReadOnlyList<string> RequiredEvidenceTypes,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ValidatedAt,
    Guid? RequestedByUserId,
    Guid? ValidatedByUserId,
    string? DecisionReasonCode,
    string? FailureReasonCode,
    string? PolicyResolutionBasis,
    Guid? StatutoryDiscountPolicyId,
    Guid? ResolvedJurisdictionId,
    string? PolicyCode,
    string? PolicyName,
    string? LegalBasisReference,
    string? OrdinanceReference,
    string? NationalLawReference,
    string? VerificationStatus,
    string? BenefitType,
    int? FreeDurationMinutes,
    string? SucceedingHoursDiscountRule,
    string? DiscountBaseScope,
    string? StackingPolicy,
    JsonElement? PolicySnapshot,
    Guid? OriginalTariffSnapshotId,
    Guid? PayableBasisApplicationId,
    string? PayableBasisApplicationStatus,
    Guid? AppliedTariffSnapshotId,
    long? OriginalAmountMinorUnits,
    long? VatAmountMinorUnits,
    long? VatExclusiveAmountMinorUnits,
    long? StatutoryDiscountAmountMinorUnits,
    long? PayableAmountMinorUnits,
    long? FinalPayableAmountMinorUnits,
    string? CurrencyCode,
    IReadOnlyList<string> Activity,
    Guid? StatutoryDiscountDecisionCommandId = null);

/// <summary>
/// Read-only audit/reporting response for Operator Console statutory discount validation.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountAuditReportResponse(
    IReadOnlyList<OperatorConsoleStatutoryDiscountAuditReportItem> Items,
    int TotalCount,
    int Limit,
    int Offset,
    Guid CorrelationId);

/// <summary>
/// Safe read-only statutory discount audit/reporting item.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountAuditReportItem(
    Guid StatutoryDiscountValidationId,
    Guid DraftId,
    Guid ParkingSessionId,
    string? TicketReference,
    string? PlateNumber,
    Guid SiteId,
    Guid SiteGroupId,
    string EntitlementType,
    string ValidationStatus,
    bool EvidenceRequired,
    bool EvidenceCaptured,
    bool EvidenceRequiredSatisfied,
    int EvidenceCount,
    string? LatestEvidenceStatus,
    string? PayableBasisApplicationStatus,
    long? OriginalAmountMinorUnits,
    long? StatutoryDiscountAmountMinorUnits,
    long? FinalPayableAmountMinorUnits,
    string? CurrencyCode,
    Guid? RequestedByUserId,
    Guid? ValidatedByUserId,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ValidatedAt,
    Guid? CorrelationId,
    string? PolicyCode,
    string? OrdinanceReference,
    string? LegalBasisReference,
    Guid? AppliedTariffSnapshotId,
    string? AccessEvaluationSummary);
