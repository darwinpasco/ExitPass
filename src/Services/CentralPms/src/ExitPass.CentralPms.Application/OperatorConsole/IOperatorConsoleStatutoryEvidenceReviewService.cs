using ExitPass.CentralPms.Application.StatutoryEvidence;

namespace ExitPass.CentralPms.Application.OperatorConsole;

public interface IOperatorConsoleStatutoryEvidenceReviewService
{
    Task<OperatorConsoleStatutoryEvidenceReviewResult?> ReadAsync(
        Guid statutoryDiscountDecisionCommandId,
        OperatorConsoleReviewAccessContext accessContext,
        CancellationToken cancellationToken);

    Task<OperatorConsoleStatutoryEvidencePreviewResult> OpenPreviewAsync(
        Guid statutoryDiscountDecisionCommandId,
        Guid evidenceItemReference,
        OperatorConsoleReviewAccessContext accessContext,
        CancellationToken cancellationToken);

    Task RecordPreviewStreamOutcomeAsync(
        OperatorConsoleStatutoryEvidencePreviewAuditContext context,
        string outcome,
        CancellationToken cancellationToken);
}

public interface IOperatorConsoleStatutoryEvidenceReviewRepository
{
    Task<OperatorConsoleStatutoryEvidenceReviewRecord?> ReadAsync(
        Guid statutoryDiscountDecisionCommandId,
        CancellationToken cancellationToken);

    Task<bool> IsCurrentPreviewTargetAsync(
        OperatorConsoleStatutoryEvidencePreviewTarget target,
        CancellationToken cancellationToken);

    Task RecordAccessEventAsync(
        OperatorConsoleStatutoryEvidenceAccessEvent accessEvent,
        CancellationToken cancellationToken);
}

public static class OperatorConsoleStatutoryEvidenceReviewConstants
{
    public const string Permission = "statutory-discounts.evidence.review.view";
    public const string Policy = "OperatorConsoleStatutoryEvidenceReviewView";
    public const string SourceChannel = "OPERATOR_CONSOLE";

    public static readonly ISet<string> SupportedPreviewMediaTypes = new HashSet<string>(
        ["image/jpeg", "image/png"],
        StringComparer.OrdinalIgnoreCase);
}

public sealed record OperatorConsoleStatutoryEvidenceReviewResult(
    Guid StatutoryDiscountDecisionCommandId,
    Guid? EvidenceSetReference,
    string SourceChannel,
    string DecisionResultStatus,
    string ReviewStatus,
    bool EvidenceRequired,
    bool EvidenceRecorded,
    string? SetStatus,
    string? RetentionStatus,
    string? DeletionStatus,
    bool HoldActive,
    string ReplacementPosture,
    IReadOnlyList<OperatorConsoleStatutoryEvidenceReviewItemResult> Items,
    Guid CorrelationId);

public sealed record OperatorConsoleStatutoryEvidenceReviewItemResult(
    Guid EvidenceItemReference,
    string DocumentType,
    string ItemRole,
    string? DeclaredContentType,
    string? AuthoritativeContentType,
    long? ContentLength,
    string UploadStatus,
    string ValidationStatus,
    string ScanStatus,
    string ReviewabilityStatus,
    string BindingStatus,
    string RetentionStatus,
    string DeletionStatus,
    bool HoldActive,
    DateTimeOffset? UploadedAt,
    DateTimeOffset? FinalizedAt,
    DateTimeOffset? ValidatedAt,
    DateTimeOffset? ScannedAt,
    DateTimeOffset? ReviewableAt,
    bool PreviewPermitted,
    string? PreviewDenialReason);

public sealed record OperatorConsoleStatutoryEvidenceReviewRecord(
    Guid StatutoryDiscountDecisionCommandId,
    Guid ParkingSessionId,
    Guid SiteId,
    Guid SiteGroupId,
    string SourceChannel,
    string DecisionResultStatus,
    string ReviewStatus,
    bool EvidenceRequired,
    bool EvidenceRecorded,
    Guid? EvidenceSetId,
    Guid? EvidenceSetReference,
    string? SetStatus,
    string? RetentionStatus,
    string? DeletionStatus,
    bool HoldActive,
    long? SetRowVersion,
    IReadOnlyList<OperatorConsoleStatutoryEvidenceReviewItemRecord> Items);

public sealed record OperatorConsoleStatutoryEvidenceReviewItemRecord(
    Guid EvidenceItemId,
    Guid EvidenceItemReference,
    string DocumentType,
    string ItemRole,
    string UploadStatus,
    string ValidationStatus,
    string ScanStatus,
    string ReviewabilityStatus,
    string BindingStatus,
    string RetentionStatus,
    string DeletionStatus,
    bool HoldActive,
    string? DeclaredContentType,
    string? InternalStorageLocatorReference,
    string? InternalChecksumSha256,
    DateTimeOffset? UploadedAt,
    DateTimeOffset? ReviewableAt,
    long ItemRowVersion,
    Guid? UploadAuthorizationId,
    Guid? UploadAuthorizationReference,
    string? AuthorizationStatus,
    string? BucketReference,
    string? InternalObjectKey,
    string? VerifiedContentType,
    long? VerifiedContentLength,
    string? VerifiedChecksumSha256,
    string? ProviderObjectVersion,
    DateTimeOffset? FinalizedAt,
    long? UploadAuthorizationRowVersion,
    DateTimeOffset? ScanCompletedAt);

public sealed record OperatorConsoleStatutoryEvidencePreviewTarget(
    Guid StatutoryDiscountDecisionCommandId,
    Guid ParkingSessionId,
    Guid SiteId,
    Guid SiteGroupId,
    Guid EvidenceSetId,
    Guid EvidenceSetReference,
    long SetRowVersion,
    Guid EvidenceItemId,
    Guid EvidenceItemReference,
    long ItemRowVersion,
    Guid UploadAuthorizationId,
    Guid UploadAuthorizationReference,
    long UploadAuthorizationRowVersion,
    string InternalObjectKey,
    string ContentType,
    long ContentLength,
    string ChecksumSha256,
    string? ProviderObjectVersion,
    Guid CorrelationId,
    Guid ReviewerUserId);

public sealed record OperatorConsoleStatutoryEvidencePreviewResult(
    string Classification,
    string? ErrorCode,
    bool Retryable,
    Guid CorrelationId,
    StatutoryEvidenceObjectContent? Content,
    OperatorConsoleStatutoryEvidencePreviewAuditContext? AuditContext);

public sealed record OperatorConsoleStatutoryEvidencePreviewAuditContext(
    OperatorConsoleStatutoryEvidencePreviewTarget Target,
    StatutoryEvidenceActor Actor);

public sealed record OperatorConsoleStatutoryEvidenceAccessEvent(
    string EventType,
    string EventResult,
    string SafeReasonCode,
    Guid? EvidenceSetId,
    Guid? EvidenceItemId,
    Guid? SiteId,
    Guid? SiteGroupId,
    Guid? ParkingSessionId,
    Guid CorrelationId,
    StatutoryEvidenceActor Actor);
