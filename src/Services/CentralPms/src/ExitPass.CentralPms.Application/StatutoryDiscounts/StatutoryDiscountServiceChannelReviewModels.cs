namespace ExitPass.CentralPms.Application.StatutoryDiscounts;

/// <summary>
/// Durable safe review statuses for service-channel-originated statutory-discount decisions.
/// </summary>
public static class StatutoryDiscountServiceChannelReviewStatuses
{
    public const string PendingReview = "PENDING_REVIEW";
    public const string Approved = "APPROVED";
    public const string Rejected = "REJECTED";
    public const string ReviewFactsUnavailable = "REVIEW_FACTS_UNAVAILABLE";
}

/// <summary>
/// Safe service-channel evidence metadata retained for Operator Console review.
/// </summary>
public sealed record StatutoryDiscountServiceChannelReviewEvidenceFact(
    string EvidenceType,
    string CaptureMethod,
    string? StorageReference,
    string? ReferenceNumberMasked,
    string? VerificationStatus);

/// <summary>
/// Safe service-channel intake facts retained for Operator Console review.
/// </summary>
public sealed record StatutoryDiscountServiceChannelReviewIntakeCommand(
    Guid StatutoryDiscountDecisionCommandId,
    Guid RequestReference,
    Guid ParkingSessionId,
    string SourceChannel,
    Guid? SiteId,
    Guid? SiteGroupId,
    string? TicketReference,
    string? PlateNumber,
    string EntitlementType,
    string? IdDocumentType,
    string? IssuingAuthority,
    DateOnly? ExpiryDate,
    string? MaskedIdReference,
    IReadOnlyList<StatutoryDiscountServiceChannelReviewEvidenceFact> EvidenceReferences,
    bool RequesterAttestation,
    string? AttestationNotes,
    string? ReasonCode,
    Guid? OriginalTariffSnapshotId,
    Guid CorrelationId,
    DateTimeOffset SubmittedAt);

/// <summary>
/// Query for Operator Console service-channel statutory-discount review rows.
/// </summary>
public sealed record StatutoryDiscountServiceChannelReviewQueueQuery(
    Guid? SiteId,
    Guid? SiteGroupId,
    string? SourceChannel,
    string? EntitlementType,
    Guid? ParkingSessionId,
    DateTimeOffset? SubmittedFrom,
    DateTimeOffset? SubmittedTo,
    int Page,
    int PageSize,
    Guid CorrelationId);

/// <summary>
/// Operator Console service-channel statutory-discount review queue.
/// </summary>
public sealed record StatutoryDiscountServiceChannelReviewQueueResult(
    IReadOnlyList<StatutoryDiscountServiceChannelReviewQueueItem> Items,
    int Page,
    int PageSize,
    bool HasMore,
    Guid CorrelationId);

/// <summary>
/// Safe queue item for service-channel statutory-discount review.
/// </summary>
public sealed record StatutoryDiscountServiceChannelReviewQueueItem(
    Guid StatutoryDiscountDecisionCommandId,
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
    bool EvidenceRequired,
    bool EvidenceRecorded,
    Guid? OriginalTariffSnapshotId,
    DateTimeOffset SubmittedAt,
    Guid CorrelationId);

/// <summary>
/// Safe Operator Console detail for one service-channel statutory-discount review.
/// </summary>
public sealed record StatutoryDiscountServiceChannelReviewDetail(
    Guid StatutoryDiscountDecisionCommandId,
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
    IReadOnlyList<StatutoryDiscountServiceChannelReviewEvidenceFact> EvidenceReferences,
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
    Guid? ReviewerUserId,
    Guid? ReviewerAccessEvaluationId,
    string? ReviewerDecision,
    string? ReviewerReasonCode,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? ReviewedAt,
    Guid CorrelationId);

/// <summary>
/// Command for completing a service-channel-originated canonical decision from Operator Console.
/// </summary>
public sealed record StatutoryDiscountServiceChannelReviewDecisionCommand(
    Guid StatutoryDiscountDecisionCommandId,
    Guid UserId,
    Guid? OperatorDeviceBindingId,
    Guid? SiteId,
    Guid? SiteGroupId,
    Guid? OperatorShiftId,
    string Decision,
    string? DecisionReasonCode,
    string? DecisionNotes,
    bool ReviewerAttestation,
    string IdempotencyKey,
    Guid CorrelationId);

/// <summary>
/// Operator Console result for service-channel review completion.
/// </summary>
public sealed record StatutoryDiscountServiceChannelReviewDecisionResult(
    Guid AccessEvaluationId,
    bool AccessAllowed,
    string AccessDecision,
    IReadOnlyList<string> AccessDenialReasons,
    bool AccessPersisted,
    bool DecisionAccepted,
    bool DecisionPersisted,
    Guid StatutoryDiscountDecisionCommandId,
    Guid ParkingSessionId,
    string SourceChannel,
    string EntitlementType,
    string PreviousCommandStatus,
    string CurrentCommandStatus,
    string PreviousDecisionResultStatus,
    string CurrentDecisionResultStatus,
    string ReviewStatus,
    string Decision,
    string? DecisionReasonCode,
    bool AlreadyDecided,
    bool DecisionChanged,
    string? IneligibilityReason,
    string? ErrorCode,
    Guid CorrelationId);
