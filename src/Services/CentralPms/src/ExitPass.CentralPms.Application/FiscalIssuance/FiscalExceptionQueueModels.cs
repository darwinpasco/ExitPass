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
    NotRequiredRecorded = 5,
    EligibleForControlledRetryPlanning = 6,
    BlockedReadbackMatched = 7,
    BlockedReadbackMismatch = 8,
    BlockedReadbackFailed = 9,
    BlockedIdentifierMissing = 10,
    BlockedReadbackUnsupported = 11,
    BlockedMissingRequestContext = 12,
    BlockedMissingUpstreamFinalityReference = 13,
    UnavailablePolicyNotConfigured = 14
}

public enum FiscalExceptionRetryEligibilityDecision
{
    NotEvaluated = 1,
    Eligible = 2,
    Blocked = 3,
    Unavailable = 4,
    NotRequired = 5
}

public enum FiscalExceptionRetryCommandPreparationStatus
{
    NotPrepared = 1,
    PreparedNonExecutable = 2,
    Blocked = 3,
    Unavailable = 4
}

public enum FiscalExceptionRetrySchedulingPreparationStatus
{
    NotPrepared = 1,
    Disabled = 2,
    ScheduledPrepared = 3,
    Blocked = 4,
    Unavailable = 5
}

public enum FiscalExceptionSemanticRequestHashAvailabilityStatus
{
    NotAvailableInCurrentModel = 1,
    AvailableAndConfirmed = 2,
    RequiredButMissing = 3,
    RequiredButUnconfirmed = 4
}

public enum FiscalExceptionIdempotencyContextAvailabilityStatus
{
    NotEvaluated = 1,
    Available = 2,
    MissingUpstreamFinalityReference = 3,
    NewUpstreamFinalityReferenceRejected = 4
}

public enum FiscalSemanticRequestHashSourceStatus
{
    Unavailable = 1,
    Incomplete = 2,
    Available = 3
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
    FiscalExceptionRetryEligibilityDecision RetryEligibilityDecision,
    string? RetryBlockReasonCode,
    string SafeRetryEligibilitySummary,
    DateTimeOffset? RetryEligibilityEvaluatedAt,
    FiscalExceptionReadbackClassification? RetryEligibilityBasedOnReadbackClassification,
    bool RetryExecutionAvailable,
    FiscalExceptionRetryCommandPreparationStatus RetryCommandPreparationStatus,
    string? RetryCommandBlockReasonCode,
    string SafeRetryCommandPreparationSummary,
    FiscalExceptionSemanticRequestHashAvailabilityStatus SemanticRequestHashAvailabilityStatus,
    string? SemanticRequestHashValue,
    string? SemanticRequestHashAlgorithm,
    string? SemanticRequestHashSourceVersion,
    int? SemanticRequestHashSourceFactCount,
    string? SafeSemanticRequestHashSourceSummary,
    FiscalExceptionIdempotencyContextAvailabilityStatus IdempotencyContextAvailabilityStatus,
    DateTimeOffset? LastRetryCommandPreparationAttemptAt,
    int? RetryCommandPreparationAttemptCount,
    FiscalExceptionRetrySchedulingPreparationStatus RetrySchedulingPreparationStatus,
    string? RetrySchedulingBlockReasonCode,
    string SafeRetrySchedulingPreparationSummary,
    DateTimeOffset? LastRetrySchedulingPreparationAttemptAt,
    int? RetrySchedulingPreparationAttemptCount,
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

public sealed record FiscalExceptionRetryEligibilityEvaluation(
    FiscalExceptionRetryEligibilityStatus Status,
    FiscalExceptionRetryEligibilityDecision Decision,
    string? BlockReasonCode,
    string SafeSummary,
    DateTimeOffset EvaluatedAt,
    FiscalExceptionReadbackClassification? BasedOnReadbackClassification,
    DateTimeOffset? LastReadbackAttemptAt,
    int? ReadbackAttemptCount,
    bool RetryExecutionAvailable);

public interface IFiscalExceptionRetryEligibilityEvaluator
{
    FiscalExceptionRetryEligibilityEvaluation Evaluate(FiscalExceptionQueueCaseDetail detail);
}

public sealed record FiscalExceptionRetryCommandPreparationRequest(
    FiscalExceptionQueueCaseDetail Detail,
    bool TreatAsExecutableCommand = false,
    string? RequestedUpstreamFinalityReference = null);

public sealed record FiscalExceptionRetryCommandEnvelope(
    Guid FiscalIssuanceReferenceId,
    Guid PaymentConfirmationId,
    Guid PaymentAttemptId,
    Guid ParkingSessionId,
    Guid? SiteId,
    Guid? SitePosServerId,
    string? SitePosServerRef,
    string? FiscalDocumentTypeContextStatus,
    string UpstreamFinalityReference,
    FiscalExceptionSemanticRequestHashAvailabilityStatus SemanticRequestHashAvailabilityStatus,
    string? SemanticRequestHashValue,
    string? SemanticRequestHashAlgorithm,
    string? SemanticRequestHashSourceVersion,
    FiscalExceptionReadbackClassification LatestReadbackClassificationBasis,
    FiscalExceptionRetryEligibilityDecision RetryEligibilityDecisionBasis,
    string? SafeBlockReasonCode,
    Guid? CorrelationId,
    bool Executable);

public sealed record FiscalExceptionRetryCommandPreparationResult(
    FiscalExceptionRetryCommandPreparationStatus Status,
    string? BlockReasonCode,
    string SafeSummary,
    FiscalExceptionRetryCommandEnvelope? Command,
    FiscalExceptionSemanticRequestHashAvailabilityStatus SemanticRequestHashAvailabilityStatus,
    FiscalExceptionIdempotencyContextAvailabilityStatus IdempotencyContextAvailabilityStatus,
    bool PosServerPostCalled,
    bool RetryScheduled,
    bool PaymentFinalityChanged,
    bool ExitAuthorizationIssued,
    bool GateBehaviorTriggered,
    bool FiscalNumberEdited,
    bool ManualFiscalDocumentCreated,
    Guid? RetryCommandPreparationAttemptId = null,
    DateTimeOffset? RetryCommandPreparationAttemptedAt = null);

public interface IFiscalExceptionRetryCommandPreparationService
{
    Task<FiscalExceptionRetryCommandPreparationResult> PrepareAsync(
        FiscalExceptionRetryCommandPreparationRequest request,
    CancellationToken cancellationToken);
}

public sealed class FiscalExceptionRetrySchedulingPreparationOptions
{
    public const string SectionName = "FiscalExceptionRetrySchedulingPreparation";

    public FiscalExceptionRetrySchedulingPreparationOptions()
    {
    }

    public FiscalExceptionRetrySchedulingPreparationOptions(
        bool EnableSchedulePreparation = false,
        bool RetrySchedulePolicyConfigured = false,
        bool RetryBackoffPolicyConfigured = false)
    {
        this.EnableSchedulePreparation = EnableSchedulePreparation;
        this.RetrySchedulePolicyConfigured = RetrySchedulePolicyConfigured;
        this.RetryBackoffPolicyConfigured = RetryBackoffPolicyConfigured;
    }

    public bool EnableSchedulePreparation { get; set; }

    public bool RetrySchedulePolicyConfigured { get; set; }

    public bool RetryBackoffPolicyConfigured { get; set; }
}

public sealed record FiscalExceptionRetrySchedulingPreparationRequest(
    FiscalExceptionQueueCaseDetail Detail,
    FiscalExceptionRetryCommandPreparationResult CommandPreparation,
    bool TreatAsExecutableJob = false,
    string? RequestedUpstreamFinalityReference = null,
    Guid? ServiceIdentityId = null);

public sealed record FiscalExceptionRetrySchedulePreparationEnvelope(
    Guid RetrySchedulePreparationAttemptId,
    Guid FiscalIssuanceReferenceId,
    Guid? RetryCommandPreparationAttemptId,
    FiscalExceptionRetryEligibilityDecision RetryEligibilityDecisionBasis,
    FiscalExceptionReadbackClassification? LatestReadbackClassificationBasis,
    FiscalExceptionSemanticRequestHashAvailabilityStatus SemanticRequestHashAvailabilityStatus,
    FiscalExceptionIdempotencyContextAvailabilityStatus IdempotencyContextAvailabilityStatus,
    string UpstreamFinalityReference,
    DateTimeOffset RequestedAt,
    DateTimeOffset? EarliestEligibleAt,
    string SchedulePolicySummary,
    Guid? CorrelationId,
    bool Executable);

public sealed record FiscalExceptionRetrySchedulingPreparationResult(
    FiscalExceptionRetrySchedulingPreparationStatus Status,
    string? BlockReasonCode,
    string SafeSummary,
    FiscalExceptionRetrySchedulePreparationEnvelope? Schedule,
    bool PosServerPostCalled,
    bool ExecutableJobEnqueued,
    bool RetryEndpointExposed,
    bool PaymentFinalityChanged,
    bool FiscalReferenceSuccessRecorded,
    bool ExitAuthorizationIssued,
    bool GateBehaviorTriggered,
    bool FiscalNumberEdited,
    bool ManualFiscalDocumentCreated,
    Guid? RetrySchedulePreparationAttemptId = null,
    DateTimeOffset? RetrySchedulePreparationAttemptedAt = null);

public interface IFiscalExceptionRetrySchedulingPreparationService
{
    Task<FiscalExceptionRetrySchedulingPreparationResult> PrepareAsync(
        FiscalExceptionRetrySchedulingPreparationRequest request,
        CancellationToken cancellationToken);
}

public sealed record FiscalExceptionRetryCommandPreparationAttemptWrite(
    Guid FiscalIssuanceReferenceId,
    Guid? PaymentConfirmationId,
    Guid? PaymentAttemptId,
    Guid? ParkingSessionId,
    Guid? SiteId,
    Guid? SitePosServerId,
    string? SitePosServerRef,
    FiscalExceptionReadbackClassification? LatestReadbackClassificationBasis,
    FiscalExceptionRetryEligibilityDecision RetryEligibilityDecisionBasis,
    FiscalExceptionRetryCommandPreparationStatus CommandPreparationStatus,
    string? CommandBlockReasonCode,
    FiscalExceptionSemanticRequestHashAvailabilityStatus SemanticRequestHashAvailabilityStatus,
    FiscalExceptionIdempotencyContextAvailabilityStatus IdempotencyContextAvailabilityStatus,
    DateTimeOffset AttemptedAt,
    string SafeSummary,
    Guid? CorrelationId,
    Guid? ServiceIdentityId);

public sealed record FiscalExceptionRetryCommandPreparationAttemptRecord(
    Guid RetryCommandPreparationAttemptId,
    Guid FiscalIssuanceReferenceId,
    Guid? PaymentConfirmationId,
    Guid? PaymentAttemptId,
    Guid? ParkingSessionId,
    Guid? SiteId,
    Guid? SitePosServerId,
    string? SitePosServerRef,
    FiscalExceptionReadbackClassification? LatestReadbackClassificationBasis,
    FiscalExceptionRetryEligibilityDecision RetryEligibilityDecisionBasis,
    FiscalExceptionRetryCommandPreparationStatus CommandPreparationStatus,
    string? CommandBlockReasonCode,
    FiscalExceptionSemanticRequestHashAvailabilityStatus SemanticRequestHashAvailabilityStatus,
    FiscalExceptionIdempotencyContextAvailabilityStatus IdempotencyContextAvailabilityStatus,
    DateTimeOffset AttemptedAt,
    string SafeSummary,
    Guid? CorrelationId,
    Guid? ServiceIdentityId,
    DateTimeOffset CreatedAt);

public sealed record FiscalExceptionRetryCommandPreparationAttemptSummary(
    Guid LastRetryCommandPreparationAttemptId,
    FiscalExceptionRetryCommandPreparationStatus LastCommandPreparationStatus,
    DateTimeOffset LastAttemptedAt,
    int AttemptCount,
    string? LastCommandBlockReasonCode,
    FiscalExceptionSemanticRequestHashAvailabilityStatus SemanticRequestHashAvailabilityStatus,
    FiscalExceptionIdempotencyContextAvailabilityStatus IdempotencyContextAvailabilityStatus,
    string SafeSummary);

public interface IFiscalExceptionRetryCommandPreparationAuditRepository
{
    Task<FiscalExceptionRetryCommandPreparationAttemptRecord> RecordAsync(
        FiscalExceptionRetryCommandPreparationAttemptWrite attempt,
        CancellationToken cancellationToken);

    Task<FiscalExceptionRetryCommandPreparationAttemptSummary?> GetSummaryAsync(
        Guid fiscalIssuanceReferenceId,
        CancellationToken cancellationToken);
}

public sealed record FiscalExceptionRetrySchedulingPreparationAttemptWrite(
    Guid FiscalIssuanceReferenceId,
    Guid? RetryCommandPreparationAttemptId,
    Guid? PaymentConfirmationId,
    Guid? PaymentAttemptId,
    Guid? ParkingSessionId,
    Guid? SiteId,
    Guid? SitePosServerId,
    string? SitePosServerRef,
    FiscalExceptionReadbackClassification? LatestReadbackClassificationBasis,
    FiscalExceptionRetryEligibilityDecision RetryEligibilityDecisionBasis,
    FiscalExceptionSemanticRequestHashAvailabilityStatus SemanticRequestHashAvailabilityStatus,
    FiscalExceptionIdempotencyContextAvailabilityStatus IdempotencyContextAvailabilityStatus,
    FiscalExceptionRetrySchedulingPreparationStatus SchedulingPreparationStatus,
    string? SchedulingBlockReasonCode,
    DateTimeOffset RequestedAt,
    DateTimeOffset? EarliestEligibleAt,
    string SafeSummary,
    Guid? CorrelationId,
    Guid? ServiceIdentityId);

public sealed record FiscalExceptionRetrySchedulingPreparationAttemptRecord(
    Guid RetrySchedulePreparationAttemptId,
    Guid FiscalIssuanceReferenceId,
    Guid? RetryCommandPreparationAttemptId,
    Guid? PaymentConfirmationId,
    Guid? PaymentAttemptId,
    Guid? ParkingSessionId,
    Guid? SiteId,
    Guid? SitePosServerId,
    string? SitePosServerRef,
    FiscalExceptionReadbackClassification? LatestReadbackClassificationBasis,
    FiscalExceptionRetryEligibilityDecision RetryEligibilityDecisionBasis,
    FiscalExceptionSemanticRequestHashAvailabilityStatus SemanticRequestHashAvailabilityStatus,
    FiscalExceptionIdempotencyContextAvailabilityStatus IdempotencyContextAvailabilityStatus,
    FiscalExceptionRetrySchedulingPreparationStatus SchedulingPreparationStatus,
    string? SchedulingBlockReasonCode,
    DateTimeOffset RequestedAt,
    DateTimeOffset? EarliestEligibleAt,
    string SafeSummary,
    Guid? CorrelationId,
    Guid? ServiceIdentityId,
    DateTimeOffset CreatedAt);

public sealed record FiscalExceptionRetrySchedulingPreparationAttemptSummary(
    FiscalExceptionRetrySchedulingPreparationStatus LastSchedulingPreparationStatus,
    DateTimeOffset LastRequestedAt,
    int AttemptCount,
    string? LastSchedulingBlockReasonCode,
    string SafeSummary);

public interface IFiscalExceptionRetrySchedulingPreparationAuditRepository
{
    Task<FiscalExceptionRetrySchedulingPreparationAttemptRecord> RecordAsync(
        FiscalExceptionRetrySchedulingPreparationAttemptWrite attempt,
        CancellationToken cancellationToken);

    Task<FiscalExceptionRetrySchedulingPreparationAttemptSummary?> GetSummaryAsync(
        Guid fiscalIssuanceReferenceId,
        CancellationToken cancellationToken);
}

public sealed record FiscalSemanticRequestHashResult(
    FiscalSemanticRequestHashSourceStatus Status,
    string? HashValue,
    string HashAlgorithm,
    string HashSourceVersion,
    int SourceFactCount,
    string SafeSourceSummary,
    string? BlockReasonCode);

public interface IFiscalSemanticRequestHashCalculator
{
    FiscalSemanticRequestHashResult Calculate(PosServerFiscalDocumentCreateRequest request);
}

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

