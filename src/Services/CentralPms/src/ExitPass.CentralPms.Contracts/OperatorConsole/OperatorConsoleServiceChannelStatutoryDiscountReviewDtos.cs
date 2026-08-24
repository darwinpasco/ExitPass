using ExitPass.CentralPms.Contracts.StatutoryDiscounts;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExitPass.CentralPms.Contracts.OperatorConsole;

public sealed record OperatorConsoleServiceChannelStatutoryDiscountReviewQueueResponse(
    IReadOnlyList<OperatorConsoleServiceChannelStatutoryDiscountReviewQueueItem> Items,
    int TotalCount,
    int Page,
    int PageSize,
    bool HasMore,
    Guid CorrelationId);

public sealed record OperatorConsoleServiceChannelStatutoryDiscountReviewQueueItem(
    Guid StatutoryDiscountDecisionCommandId,
    Guid RequestReference,
    Guid ParkingSessionId,
    string SourceChannel,
    Guid? SiteId,
    Guid? SiteGroupId,
    string? TicketReference,
    string EntitlementType,
    string CommandStatus,
    string DecisionResultStatus,
    string ReviewStatus,
    bool EvidenceRequired,
    bool EvidenceRecorded,
    Guid? OriginalTariffSnapshotId,
    DateTimeOffset SubmittedAt,
    Guid CorrelationId);

public sealed record OperatorConsoleServiceChannelStatutoryDiscountReviewDetailResponse(
    Guid StatutoryDiscountDecisionCommandId,
    Guid? StatutoryDiscountValidationId,
    Guid RequestReference,
    Guid ParkingSessionId,
    string SourceChannel,
    Guid? SiteId,
    Guid? SiteGroupId,
    string? TicketReference,
    string? PlateNumber,
    string EntitlementType,
    string CommandStatus,
    string DecisionResultStatus,
    string ReviewStatus,
    string? IdDocumentType,
    string? IssuingAuthority,
    DateOnly? ExpiryDate,
    string? MaskedIdReference,
    IReadOnlyList<OperatorConsoleServiceChannelStatutoryDiscountReviewEvidenceReference> EvidenceReferences,
    bool RequesterAttestation,
    string? AttestationNotes,
    string? ReasonCode,
    bool EvidenceRequired,
    bool EvidenceRecorded,
    Guid? OriginalTariffSnapshotId,
    long? OriginalAmountMinorUnits,
    long? VatExclusiveAmountMinorUnits,
    long? VatAmountMinorUnits,
    long? StatutoryDiscountAmountMinorUnits,
    long? FinalPayableAmountMinorUnits,
    string? Currency,
    OperatorConsoleServiceChannelStatutoryDiscountReviewPolicyAuthority? GoverningPolicy,
    Guid? ReviewerUserId,
    Guid? ReviewerAccessEvaluationId,
    string? ReviewerDecision,
    string? ReviewerReasonCode,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? ReviewedAt,
    string? PayableBasisApplicationStatus,
    Guid CorrelationId);

/// <summary>
/// Browser-safe Operator Console decision body. Reviewer identity, timestamp, and scope
/// are deliberately absent and are derived by Central PMS from the authenticated session.
/// </summary>
public sealed record OperatorConsoleCanonicalStatutoryReviewDecisionRequest(
    string Decision,
    string? DecisionReasonCode,
    bool ReviewerAttestation,
    string IdempotencyKey)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalFields { get; init; }
}

/// <summary>Browser-safe evidence-preview selector carried in the body, never in the URL.</summary>
public sealed record OperatorConsoleStatutoryEvidencePreviewRequest(Guid EvidenceItemReference);

public sealed record OperatorConsoleServiceChannelStatutoryDiscountReviewPolicyAuthority(
    Guid StatutoryDiscountPolicyVersionId,
    Guid JurisdictionId,
    string JurisdictionCode,
    string JurisdictionDisplayName,
    string PolicyCode,
    string PolicyVersion,
    string? OrdinanceNumber,
    string? OrdinanceTitle,
    string SourceVerificationStatus,
    string TransactionPublicationStatus,
    string DetailedRuleVerificationStatus,
    string ParkingServiceApplicability,
    string BenefitType,
    string BeneficiaryResidencyScope,
    bool? OfficialSourceAvailable,
    bool? OrdinanceTextAvailable,
    bool? OrdinanceNumberAvailable,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    IReadOnlyList<StatutoryDiscountPolicyEvidenceRequirementDto> RequiredEvidenceTypes,
    string LegalApprovabilityReason);

public sealed record OperatorConsoleServiceChannelStatutoryDiscountReviewEvidenceReference(
    string EvidenceType,
    string CaptureMethod,
    string? ReferenceNumberMasked,
    string? VerificationStatus);
