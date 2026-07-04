using ExitPass.CentralPms.Domain.FiscalIssuance;

namespace ExitPass.CentralPms.Application.FiscalIssuance;

public enum FiscalExceptionQueueCategory
{
    PosServerUnavailable = 1,
    PosServerTimeout = 2,
    PosServerHttpFailure = 3,
    UnknownOutcome = 4,
    PosServerAcceptedCentralPmsRecordingFailed = 5,
    IdempotencyConflict = 6,
    SemanticRequestHashMismatch = 7,
    FiscalConfigurationMissing = 8,
    CentralPmsMappingFailure = 9,
    FiscalMismatch = 10,
    ManualReviewRequired = 11,
    OtherFiscalFailure = 12
}

public enum FiscalExceptionQueueState
{
    Queued = 1,
    ReadbackRequired = 2,
    ManualReviewRequired = 3,
    MismatchReview = 4,
    BlockedRequiresConfigFix = 5,
    Reconciled = 6,
    Closed = 7
}

public enum FiscalExceptionReadbackStatus
{
    NotRequired = 1,
    RequiredNotStarted = 2,
    PendingFutureSlice = 3,
    Attempted = 4
}

public enum FiscalExceptionReadbackClassification
{
    Matched = 1,
    NotFound = 2,
    Mismatch = 3,
    Failed = 4,
    Unavailable = 5,
    Unknown = 6,
    IdentifierMissing = 7,
    NotSupportedYet = 8
}

public enum FiscalExceptionRetryEligibilityStatus
{
    UnavailableInThisSlice = 1,
    BlockedPendingReadback = 2,
    BlockedManualReview = 3,
    BlockedConfiguration = 4,
    NotRequiredRecorded = 5
}

public sealed record FiscalExceptionQueueQuery(
    int Limit = 100,
    Guid? SiteId = null,
    Guid? SitePosServerId = null);

public sealed record FiscalExceptionQueueCaseSummary(
    Guid CaseId,
    Guid FiscalIssuanceReferenceId,
    Guid PaymentConfirmationId,
    Guid PaymentAttemptId,
    Guid ParkingSessionId,
    Guid? SiteId,
    Guid? SitePosServerId,
    string? SitePosServerRef,
    string UpstreamFinalityReference,
    string? PayableBasisRef,
    FiscalExceptionQueueCategory Category,
    FiscalExceptionQueueState QueueState,
    FiscalIssuanceIntegrationState FiscalIssuanceState,
    FiscalIssuanceExceptionReason? LatestExceptionReason,
    string? SafeErrorSummary,
    FiscalExceptionReadbackStatus ReadbackStatus,
    FiscalExceptionReadbackClassification? ReadbackClassification,
    DateTimeOffset? LastReadbackAttemptAt,
    int? ReadbackAttemptCount,
    string? LastReadbackSafeSummary,
    FiscalExceptionRetryEligibilityStatus RetryEligibilityStatus,
    bool RetryExecutionAvailable,
    string DuplicateCollapseKey,
    string DuplicateCollapseStrategy,
    DateTimeOffset FirstDetectedAt,
    DateTimeOffset LastUpdatedAt);

public sealed record FiscalExceptionQueueCaseDetail(
    FiscalExceptionQueueCaseSummary Summary,
    Guid? PosServerFiscalDocumentId,
    string? FiscalDocumentNumber,
    Guid? FiscalIdentityId,
    Guid? FiscalSequencePolicyId,
    long? FiscalSequenceValue,
    Guid? FiscalDocumentStatusCodeId,
    FiscalIssuanceResultClassification? ResultClassification,
    FiscalIssuanceEvidenceStatus? FiscalIssuanceEvidenceStatus,
    FiscalNumberAssignmentState FiscalNumberAssignmentState,
    FiscalIssuanceErrorPosture? LatestErrorPosture,
    Guid? CorrelationId,
    DateTimeOffset? PosServerResponseTimestamp,
    bool PaymentFinalityChanged,
    bool ExitAuthorizationIssued,
    bool GateBehaviorTriggered,
    bool FiscalNumberEditingAllowed,
    bool ManualFiscalDocumentCreationAllowed);

public sealed record FiscalExceptionReadbackPreparation(
    Guid CaseId,
    Guid FiscalIssuanceReferenceId,
    bool ReadbackRequired,
    FiscalExceptionReadbackStatus ReadbackStatus,
    Guid? KnownPosServerFiscalDocumentId,
    Guid? SitePosServerId,
    string? SitePosServerRef,
    string UpstreamFinalityReference,
    string? PayableBasisRef,
    FiscalExceptionRetryEligibilityStatus RetryEligibilityStatus,
    bool RetryExecutionAvailable,
    bool PosServerReadbackCallPerformed,
    string PreparationStatus);

public sealed record FiscalExceptionReadbackWorkerResult(
    Guid CaseId,
    Guid FiscalIssuanceReferenceId,
    FiscalExceptionReadbackClassification Classification,
    DateTimeOffset AttemptedAt,
    string SafeSummary,
    Guid? ReadbackAttemptId,
    bool PosServerReadbackCallAttempted,
    bool RetryScheduled,
    bool PaymentFinalityChanged,
    bool ExitAuthorizationIssued,
    bool GateBehaviorTriggered,
    FiscalExceptionQueueCaseDetail? UpdatedCase);

public interface IFiscalExceptionQueueReferenceReader
{
    Task<IReadOnlyList<FiscalIssuanceReferenceRecord>> ListFiscalExceptionReferencesAsync(
        FiscalExceptionQueueQuery query,
        CancellationToken cancellationToken);

    Task<FiscalIssuanceReferenceRecord?> FindFiscalExceptionReferenceAsync(
        Guid fiscalIssuanceReferenceId,
        CancellationToken cancellationToken);
}

public sealed record FiscalExceptionReadbackAttemptWrite(
    Guid FiscalIssuanceReferenceId,
    Guid PaymentConfirmationId,
    DateTimeOffset AttemptedAt,
    FiscalExceptionReadbackClassification Classification,
    string IdentifierType,
    string? IdentifierValue,
    Guid? PosServerFiscalDocumentId,
    int? PosServerHttpStatus,
    string SafeResultCode,
    string? SafeErrorSummary,
    Guid? CorrelationId,
    Guid? ServiceIdentityId);

public sealed record FiscalExceptionReadbackAttemptRecord(
    Guid ReadbackAttemptId,
    Guid FiscalIssuanceReferenceId,
    Guid PaymentConfirmationId,
    DateTimeOffset AttemptedAt,
    FiscalExceptionReadbackClassification Classification,
    string? SafeResultCode,
    string? SafeErrorSummary,
    Guid? PosServerFiscalDocumentId,
    int? PosServerHttpStatus,
    Guid? ServiceIdentityId);

public sealed record FiscalExceptionReadbackAttemptSummary(
    FiscalExceptionReadbackClassification Classification,
    DateTimeOffset AttemptedAt,
    int AttemptCount,
    string? SafeErrorSummary);

public interface IFiscalExceptionReadbackAttemptRepository
{
    Task<FiscalExceptionReadbackAttemptRecord> RecordAsync(
        FiscalExceptionReadbackAttemptWrite attempt,
        CancellationToken cancellationToken);

    Task<FiscalExceptionReadbackAttemptSummary?> GetSummaryAsync(
        Guid fiscalIssuanceReferenceId,
        CancellationToken cancellationToken);
}

