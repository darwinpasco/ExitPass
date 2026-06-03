using System.Text.Json;

namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Query for Operator Console statutory discount validation draft queue rows.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountDraftQueueQuery(
    string? Status,
    string? EntitlementType,
    Guid? SiteId,
    DateTimeOffset? CreatedFrom,
    DateTimeOffset? CreatedTo,
    int Page,
    int PageSize,
    Guid CorrelationId);

/// <summary>
/// Queue result for Operator Console statutory discount validation drafts.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountDraftQueueResult(
    IReadOnlyList<OperatorConsoleStatutoryDiscountDraftQueueItemResult> Items,
    int Page,
    int PageSize,
    bool HasMore,
    Guid CorrelationId);

/// <summary>
/// Queue row for an Operator Console statutory discount validation draft.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountDraftQueueItemResult(
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
/// Query for a single Operator Console statutory discount validation draft.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountDraftDetailQuery(Guid DraftId, Guid CorrelationId);

/// <summary>
/// Detail result for an Operator Console statutory discount validation draft.
/// </summary>
public sealed record OperatorConsoleStatutoryDiscountDraftDetailResult(
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
    long? OriginalAmountMinorUnits,
    long? StatutoryDiscountAmountMinorUnits,
    long? PayableAmountMinorUnits,
    string? CurrencyCode,
    IReadOnlyList<string> Activity);
