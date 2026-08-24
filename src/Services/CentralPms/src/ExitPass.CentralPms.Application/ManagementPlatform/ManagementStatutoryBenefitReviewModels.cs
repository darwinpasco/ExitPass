using ExitPass.CentralPms.Application.OperatorConsole;

namespace ExitPass.CentralPms.Application.ManagementPlatform;

public static class ManagementStatutoryBenefitReviewValues
{
    public const string ContractVersion = "management-platform-statutory-benefit-review:v1";
    public const string ListPolicy = "ManagementPlatformStatutoryBenefitReviewList";
    public const string DetailPolicy = "ManagementPlatformStatutoryBenefitReviewDetail";
    public const string EvidencePolicy = "ManagementPlatformStatutoryBenefitReviewEvidence";
    public const string DecisionPolicy = "ManagementPlatformStatutoryBenefitReviewDecision";
    public const string ListPermission = "statutory-discounts.review.queue.read";
    public const string DetailPermission = "statutory-discounts.review.detail.read";
    public const string EvidencePermission = "statutory-discounts.evidence.review.view";
    public const string ApprovePermission = "statutory-discounts.decision.approve";
    public const string RejectPermission = "statutory-discounts.decision.reject";
    public const string Currency = "PHP";
}

public sealed record ManagementStatutoryBenefitReviewQuery(
    string Status,
    Guid? SiteReference,
    string? SourceChannel,
    string? BenefitType,
    DateTimeOffset? SubmittedFrom,
    DateTimeOffset? SubmittedTo,
    string? Search,
    int Page,
    int PageSize,
    Guid CorrelationId);

public sealed record ManagementStatutoryBenefitReviewQueue(
    string ContractVersion,
    IReadOnlyList<ManagementStatutoryBenefitReviewQueueItem> Items,
    int Page,
    int PageSize,
    long TotalCount,
    bool HasMore,
    Guid CorrelationId);

public sealed record ManagementStatutoryBenefitReviewQueueItem(
    Guid RequestReference,
    Guid DecisionCommandReference,
    Guid ParkingSessionReference,
    string? TicketReference,
    Guid SiteReference,
    string SiteCode,
    string SiteName,
    string SourceChannel,
    string BenefitType,
    string Status,
    bool EvidenceRequired,
    bool EvidenceRecorded,
    DateTimeOffset SubmittedAt,
    string? ReviewerDisplayName,
    DateTimeOffset? DecidedAt);

public sealed record ManagementStatutoryBenefitReviewDetail(
    string ContractVersion,
    Guid RequestReference,
    Guid DecisionCommandReference,
    Guid ParkingSessionReference,
    string? TicketReference,
    Guid SiteReference,
    string SiteCode,
    string SiteName,
    string SourceChannel,
    string BenefitType,
    string Status,
    bool EvidenceRequired,
    bool EvidenceRecorded,
    string? IdDocumentType,
    string? IssuingAuthority,
    DateOnly? ExpiryDate,
    string? MaskedIdReference,
    bool RequesterAttestation,
    string? SubmissionReason,
    DateTimeOffset SubmittedAt,
    ManagementStatutoryBenefitMoney? Money,
    ManagementStatutoryBenefitDecision? Decision,
    long Version,
    Guid CorrelationId);

public sealed record ManagementStatutoryBenefitMoney(
    long OriginalAmountMinorUnits,
    long DiscountAmountMinorUnits,
    long FinalPayableAmountMinorUnits,
    string Currency);

public sealed record ManagementStatutoryBenefitDecision(
    string Decision,
    string? Reason,
    string ReviewerDisplayName,
    DateTimeOffset DecidedAt);

public sealed record ManagementStatutoryBenefitEvidence(
    string ContractVersion,
    Guid DecisionCommandReference,
    bool EvidenceRequired,
    bool EvidenceRecorded,
    IReadOnlyList<ManagementStatutoryBenefitEvidenceItem> Items,
    Guid CorrelationId);

public sealed record ManagementStatutoryBenefitEvidenceItem(
    string EvidenceType,
    string CaptureMethod,
    string? MaskedReference,
    string? VerificationStatus);

public sealed record ManagementStatutoryBenefitDecisionCommand(
    Guid DecisionCommandReference,
    string Decision,
    string? RejectionReason,
    long ExpectedVersion,
    string IdempotencyKey,
    Guid CorrelationId);

public sealed record ManagementStatutoryBenefitDecisionResult(
    string ContractVersion,
    Guid DecisionCommandReference,
    string Status,
    string Decision,
    string? Reason,
    string ReviewerDisplayName,
    DateTimeOffset DecidedAt,
    bool AlreadyDecided,
    long Version,
    Guid CorrelationId);

public enum ManagementStatutoryBenefitReviewOutcome
{
    Success,
    Invalid,
    Forbidden,
    NotFound,
    Conflict,
    SourceUnavailable
}

public sealed record ManagementStatutoryBenefitReviewResult<T>(
    ManagementStatutoryBenefitReviewOutcome Outcome,
    string Classification,
    string Message,
    Guid CorrelationId,
    T? Value = default)
{
    public static ManagementStatutoryBenefitReviewResult<T> Succeeded(T value, Guid correlationId) =>
        new(ManagementStatutoryBenefitReviewOutcome.Success, "ACCEPTED", "The statutory-benefit review request completed.", correlationId, value);

    public static ManagementStatutoryBenefitReviewResult<T> Failed(
        ManagementStatutoryBenefitReviewOutcome outcome,
        string classification,
        string message,
        Guid correlationId) => new(outcome, classification, message, correlationId);
}

public sealed record ManagementStatutoryBenefitAuthorizedSites(
    IReadOnlySet<Guid> SiteReferences,
    bool HasGlobalGrant);

public sealed record ManagementStatutoryBenefitReviewMetadata(
    Guid SiteReference,
    string SiteCode,
    string SiteName,
    string? ReviewerDisplayName,
    long Version);

public interface IManagementStatutoryBenefitReviewRepository
{
    Task<ManagementStatutoryBenefitAuthorizedSites?> ResolveAuthorizedSitesAsync(
        IdentityAdministrationActor actor,
        string permission,
        CancellationToken cancellationToken);

    Task<ManagementStatutoryBenefitReviewQueue> ListAsync(
        ManagementStatutoryBenefitReviewQuery query,
        IReadOnlySet<Guid> authorizedSites,
        CancellationToken cancellationToken);

    Task<ManagementStatutoryBenefitReviewMetadata?> GetMetadataAsync(
        Guid decisionCommandReference,
        CancellationToken cancellationToken);

}

public interface IManagementStatutoryBenefitReviewService
{
    Task<ManagementStatutoryBenefitReviewResult<ManagementStatutoryBenefitReviewQueue>> ListAsync(
        IdentityAdministrationActor actor,
        ManagementStatutoryBenefitReviewQuery query,
        CancellationToken cancellationToken);

    Task<ManagementStatutoryBenefitReviewResult<ManagementStatutoryBenefitReviewDetail>> GetAsync(
        IdentityAdministrationActor actor,
        Guid decisionCommandReference,
        Guid correlationId,
        CancellationToken cancellationToken);

    Task<ManagementStatutoryBenefitReviewResult<ManagementStatutoryBenefitEvidence>> GetEvidenceAsync(
        IdentityAdministrationActor actor,
        Guid decisionCommandReference,
        Guid correlationId,
        CancellationToken cancellationToken);

    Task<ManagementStatutoryBenefitReviewResult<ManagementStatutoryBenefitDecisionResult>> DecideAsync(
        IdentityAdministrationActor actor,
        ManagementStatutoryBenefitDecisionCommand command,
        CancellationToken cancellationToken);
}

public sealed record AuthorizedStatutoryBenefitDecisionCommand(
    Guid DecisionCommandReference,
    Guid ReviewerUserId,
    Guid AuthorizationEvaluationReference,
    string Decision,
    string? Reason,
    string IdempotencyKey,
    Guid CorrelationId);

public sealed record AuthorizedStatutoryBenefitDecisionResult(
    bool Accepted,
    bool Persisted,
    bool AlreadyDecided,
    string Status,
    string Decision,
    string? Reason,
    string? ErrorCode,
    DateTimeOffset? DecidedAt);

public interface IAuthorizedStatutoryBenefitDecisionService
{
    Task<AuthorizedStatutoryBenefitDecisionResult> DecideAuthorizedAsync(
        AuthorizedStatutoryBenefitDecisionCommand command,
        CancellationToken cancellationToken);
}
