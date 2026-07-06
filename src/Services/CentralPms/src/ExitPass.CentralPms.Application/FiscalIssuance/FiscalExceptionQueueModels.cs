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
    UnavailablePolicyNotConfigured = 14,
    BlockedSemanticHashNotReady = 15
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

public enum FiscalExceptionRetryExecutionPreparationStatus
{
    NotPrepared = 1,
    ReadyForExecutionWhenEnabled = 2,
    Blocked = 3,
    Unavailable = 4,
    Disabled = 5,
    RequiresDualControl = 6,
    RequiresPosServerReadiness = 7
}

public enum FiscalExceptionRetryExecutionAuthorizationStatus
{
    NotEvaluated = 1,
    ServiceIdentityAllowed = 2,
    ServiceIdentityNotAllowed = 3,
    OperatorActionNotAllowed = 4,
    DualControlRequired = 5,
    DualControlSatisfied = 6
}

public enum FiscalExceptionRetryExecutionPosServerReadinessStatus
{
    NotEvaluated = 1,
    Confirmed = 2,
    NumberingNotReady = 3,
    IdempotencyContractNotConfirmed = 4,
    SequencePolicyNotConfirmed = 5,
    FiscalIdentityNotConfirmed = 6,
    ProductionBirReadinessNotConfirmed = 7
}

public enum FiscalExceptionPosServerRetryContractReadinessStatus
{
    NotEvaluated = 1,
    Ready = 2,
    Blocked = 3,
    Unconfirmed = 4,
    Unavailable = 5
}

public enum FiscalExceptionSemanticRequestHashAvailabilityStatus
{
    NotAvailableInCurrentModel = 1,
    AvailableAndConfirmed = 2,
    RequiredButMissing = 3,
    RequiredButUnconfirmed = 4
}

public enum FiscalExceptionSemanticHashReadinessStatus
{
    ReadyCurrent = 1,
    LegacyRecalculationRequired = 2,
    Missing = 3,
    Incomplete = 4,
    Incompatible = 5,
    Unavailable = 6
}

public enum FiscalExceptionSemanticHashRecalculationPosture
{
    Unknown = 1,
    Possible = 2,
    NotPossible = 3
}

public enum FiscalExceptionSemanticHashRecalculationPreviewStatus
{
    NotRequired = 1,
    PreviewCalculated = 2,
    Blocked = 3,
    Unavailable = 4
}

public enum FiscalExceptionSemanticHashRecalculationMutationStatus
{
    NotMutated = 1
}

public enum FiscalExceptionSemanticHashControlledBackfillApprovalStatus
{
    NotRequiredCurrent = 1,
    ReadyForControlledBackfill = 2,
    Blocked = 3,
    PendingDualControl = 4,
    Unavailable = 5
}

public enum FiscalExceptionSemanticHashControlledBackfillDualControlPosture
{
    NotRequired = 1,
    RequiredPending = 2,
    Satisfied = 3
}

public enum FiscalExceptionSemanticHashControlledBackfillApprovalPosture
{
    PolicyMissing = 1,
    ApprovalMissing = 2,
    ApprovalPresent = 3
}

public enum FiscalExceptionSemanticHashControlledBackfillActorAuthorizationPosture
{
    Missing = 1,
    Present = 2
}

public enum FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus
{
    NotPrepared = 1,
    PreparedButMutationDisabled = 2,
    Blocked = 3,
    Unavailable = 4,
    PreparedForControlledMutation = 5,
    Mutated = 6,
    Failed = 7,
    Stale = 8,
    Disabled = 9
}

public enum FiscalExceptionSemanticHashControlledBackfillMutationMode
{
    SingleRecordOnly = 1
}

public enum FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus
{
    NotRequested = 1,
    ReadyForOperatorApproval = 2,
    PreparedButMutationInvocationDisabled = 3,
    MutationInvoked = 4,
    Blocked = 5,
    Unavailable = 6
}

public enum FiscalExceptionSemanticHashBackfillOperatorWorkflowRequestMode
{
    SingleRecordOnly = 1,
    Batch = 2
}

public enum FiscalExceptionSemanticHashBackfillOperatorWorkflowMutationInvocationPosture
{
    NotRequested = 1,
    DryRunOnly = 2,
    Disabled = 3,
    Invoked = 4,
    Blocked = 5
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

public enum FiscalSemanticRequestHashParityProofStatus
{
    Proven = 1,
    Unconfirmed = 2,
    Mismatch = 3,
    Unavailable = 4
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
    FiscalExceptionSemanticHashReadinessStatus SemanticHashReadinessStatus,
    string? SemanticHashReadinessBlockReasonCode,
    string? StoredSemanticHashSourceVersion,
    string RequiredSemanticHashSourceVersion,
    FiscalExceptionSemanticHashRecalculationPosture SemanticHashRecalculationPosture,
    string SafeSemanticHashReadinessSummary,
    FiscalExceptionSemanticHashRecalculationPreviewStatus SemanticHashRecalculationPreviewStatus,
    string? SemanticHashRecalculationPreviewBlockReasonCode,
    string? SemanticHashRecalculationPreviewStoredSourceVersion,
    string SemanticHashRecalculationPreviewRequiredSourceVersion,
    DateTimeOffset? SemanticHashRecalculationPreviewAttemptedAt,
    int? SemanticHashRecalculationPreviewAttemptCount,
    string SafeSemanticHashRecalculationPreviewSummary,
    FiscalExceptionSemanticHashRecalculationMutationStatus SemanticHashRecalculationMutationStatus,
    FiscalExceptionSemanticHashControlledBackfillApprovalStatus SemanticHashControlledBackfillApprovalStatus,
    string? SemanticHashControlledBackfillBlockReasonCode,
    Guid? SemanticHashControlledBackfillLatestPreviewAuditId,
    DateTimeOffset? SemanticHashControlledBackfillLatestPreviewAttemptedAt,
    FiscalExceptionSemanticHashControlledBackfillDualControlPosture SemanticHashControlledBackfillDualControlPosture,
    FiscalExceptionSemanticHashControlledBackfillApprovalPosture SemanticHashControlledBackfillApprovalPosture,
    FiscalExceptionSemanticHashControlledBackfillActorAuthorizationPosture SemanticHashControlledBackfillActorAuthorizationPosture,
    FiscalExceptionSemanticHashRecalculationMutationStatus SemanticHashControlledBackfillMutationStatus,
    string SafeSemanticHashControlledBackfillSummary,
    FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus SemanticHashControlledBackfillMutationPreparationStatus,
    string? SemanticHashControlledBackfillMutationBlockReasonCode,
    Guid? SemanticHashControlledBackfillLatestMutationAuditId,
    DateTimeOffset? SemanticHashControlledBackfillMutationAttemptedAt,
    int? SemanticHashControlledBackfillMutationAttemptCount,
    FiscalExceptionSemanticHashControlledBackfillMutationMode SemanticHashControlledBackfillMutationMode,
    bool SemanticHashControlledBackfillMutationEnabled,
    string? SemanticHashControlledBackfillMutationOldSourceVersion,
    string? SemanticHashControlledBackfillMutationNewSourceVersion,
    string? SemanticHashControlledBackfillMutationNewHashValue,
    string SafeSemanticHashControlledBackfillMutationSummary,
    FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus SemanticHashBackfillOperatorWorkflowStatus,
    string? SemanticHashBackfillOperatorWorkflowBlockReasonCode,
    Guid? SemanticHashBackfillOperatorWorkflowRequestId,
    string? SemanticHashBackfillOperatorWorkflowApprovalReference,
    FiscalExceptionSemanticHashControlledBackfillDualControlPosture SemanticHashBackfillOperatorWorkflowDualControlPosture,
    FiscalExceptionSemanticHashBackfillOperatorWorkflowMutationInvocationPosture
        SemanticHashBackfillOperatorWorkflowMutationInvocationPosture,
    string SafeSemanticHashBackfillOperatorWorkflowSummary,
    FiscalExceptionIdempotencyContextAvailabilityStatus IdempotencyContextAvailabilityStatus,
    DateTimeOffset? LastRetryCommandPreparationAttemptAt,
    int? RetryCommandPreparationAttemptCount,
    FiscalExceptionRetrySchedulingPreparationStatus RetrySchedulingPreparationStatus,
    string? RetrySchedulingBlockReasonCode,
    string SafeRetrySchedulingPreparationSummary,
    DateTimeOffset? LastRetrySchedulingPreparationAttemptAt,
    int? RetrySchedulingPreparationAttemptCount,
    FiscalExceptionRetryExecutionPreparationStatus RetryExecutionPreparationStatus,
    string? RetryExecutionBlockReasonCode,
    string SafeRetryExecutionPreparationSummary,
    bool RetryExecutionDualControlRequired,
    FiscalExceptionRetryExecutionAuthorizationStatus RetryExecutionAuthorizationStatus,
    FiscalExceptionRetryExecutionPosServerReadinessStatus RetryExecutionPosServerReadinessStatus,
    FiscalExceptionPosServerRetryContractReadinessStatus PosServerRetryContractReadinessStatus,
    FiscalExceptionPosServerRetryContractReadinessStatus PosServerSemanticHashCompatibilityStatus,
    FiscalExceptionPosServerRetryContractReadinessStatus PosServerIdempotencyMappingStatus,
    FiscalExceptionPosServerRetryContractReadinessStatus PosServerReadbackFieldCompatibilityStatus,
    FiscalExceptionPosServerRetryContractReadinessStatus PosServerFiscalNumberingReadinessStatus,
    FiscalExceptionPosServerRetryContractReadinessStatus PosServerConflictReplayBehaviorStatus,
    string? PosServerRetryContractBlockReasonCode,
    string SafePosServerRetryContractReadinessSummary,
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

public sealed class FiscalExceptionRetryExecutionPreparationOptions
{
    public const string SectionName = "FiscalExceptionRetryExecutionPreparation";

    public FiscalExceptionRetryExecutionPreparationOptions()
    {
    }

    public FiscalExceptionRetryExecutionPreparationOptions(
        bool EnableExecutionPreparation = false,
        bool ServiceIdentityAllowed = false,
        bool ProductionImpacting = true,
        bool DualControlSatisfied = false,
        bool PosServerNumberingReady = false,
        bool PosServerIdempotencyContractConfirmed = false,
        bool PosServerSequencePolicyConfirmed = false,
        bool PosServerFiscalIdentityConfirmed = false,
        bool ProductionBirReadinessConfirmed = false)
    {
        this.EnableExecutionPreparation = EnableExecutionPreparation;
        this.ServiceIdentityAllowed = ServiceIdentityAllowed;
        this.ProductionImpacting = ProductionImpacting;
        this.DualControlSatisfied = DualControlSatisfied;
        this.PosServerNumberingReady = PosServerNumberingReady;
        this.PosServerIdempotencyContractConfirmed = PosServerIdempotencyContractConfirmed;
        this.PosServerSequencePolicyConfirmed = PosServerSequencePolicyConfirmed;
        this.PosServerFiscalIdentityConfirmed = PosServerFiscalIdentityConfirmed;
        this.ProductionBirReadinessConfirmed = ProductionBirReadinessConfirmed;
    }

    public bool EnableExecutionPreparation { get; set; }

    public bool ServiceIdentityAllowed { get; set; }

    public bool ProductionImpacting { get; set; } = true;

    public bool DualControlSatisfied { get; set; }

    public bool PosServerNumberingReady { get; set; }

    public bool PosServerIdempotencyContractConfirmed { get; set; }

    public bool PosServerSequencePolicyConfirmed { get; set; }

    public bool PosServerFiscalIdentityConfirmed { get; set; }

    public bool ProductionBirReadinessConfirmed { get; set; }
}

public sealed record FiscalExceptionRetryExecutionPreparationRequest(
    FiscalExceptionQueueCaseDetail Detail,
    FiscalExceptionRetryCommandPreparationResult CommandPreparation,
    FiscalExceptionRetrySchedulingPreparationResult SchedulingPreparation,
    FiscalExceptionPosServerRetryContractReadinessResult? PosServerRetryContractReadiness = null,
    bool TreatAsExecutableRetry = false,
    bool OperatorOrSupportActionRequested = false,
    string? RequestedUpstreamFinalityReference = null);

public sealed record FiscalExceptionRetryExecutionPreparationResult(
    FiscalExceptionRetryExecutionPreparationStatus Status,
    string? BlockReasonCode,
    string SafeSummary,
    FiscalExceptionRetryExecutionAuthorizationStatus AuthorizationStatus,
    FiscalExceptionRetryExecutionPosServerReadinessStatus PosServerReadinessStatus,
    bool DualControlRequired,
    bool PosServerPostCalled,
    bool ExecutableJobEnqueued,
    bool RetryEndpointExposed,
    bool RetryExecuted,
    bool PaymentFinalityChanged,
    bool FiscalReferenceSuccessRecorded,
    bool ExitAuthorizationIssued,
    bool GateBehaviorTriggered,
    bool FiscalNumberEdited,
    bool ManualFiscalDocumentCreated);

public interface IFiscalExceptionRetryExecutionPreparationService
{
    Task<FiscalExceptionRetryExecutionPreparationResult> EvaluateAsync(
        FiscalExceptionRetryExecutionPreparationRequest request,
        CancellationToken cancellationToken);
}

public sealed record FiscalExceptionPosServerRetryContractReadinessRequest(
    FiscalExceptionQueueCaseDetail Detail,
    string? RequestedUpstreamFinalityReference = null,
    FiscalSemanticRequestHashParityProofResult? SemanticHashParityProof = null);

public sealed record FiscalExceptionPosServerRetryContractReadinessResult(
    FiscalExceptionPosServerRetryContractReadinessStatus Status,
    FiscalExceptionPosServerRetryContractReadinessStatus SemanticHashCompatibilityStatus,
    FiscalExceptionPosServerRetryContractReadinessStatus IdempotencyMappingStatus,
    FiscalExceptionPosServerRetryContractReadinessStatus ReadbackFieldCompatibilityStatus,
    FiscalExceptionPosServerRetryContractReadinessStatus FiscalNumberingReadinessStatus,
    FiscalExceptionPosServerRetryContractReadinessStatus ConflictReplayBehaviorStatus,
    string? BlockReasonCode,
    string SafeSummary,
    bool RetryExecutionAvailable);

public interface IFiscalExceptionPosServerRetryContractReadinessService
{
    FiscalExceptionPosServerRetryContractReadinessResult Evaluate(
        FiscalExceptionPosServerRetryContractReadinessRequest request);
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

public sealed record FiscalSemanticRequestHashCanonicalInspectionResult(
    FiscalSemanticRequestHashSourceStatus Status,
    string? HashValue,
    string HashAlgorithm,
    string HashSourceVersion,
    int SourceFactCount,
    string SafeSourceSummary,
    string? BlockReasonCode,
    IReadOnlyList<string> NormalizedFacts,
    string? CanonicalSourceText);

public interface IFiscalSemanticRequestHashCalculator
{
    FiscalSemanticRequestHashResult Calculate(PosServerFiscalDocumentCreateRequest request);
}

public sealed record FiscalExceptionSemanticHashReadinessResult(
    FiscalExceptionSemanticHashReadinessStatus Status,
    string? BlockReasonCode,
    string? StoredSourceVersion,
    string RequiredSourceVersion,
    FiscalExceptionSemanticHashRecalculationPosture RecalculationPosture,
    string SafeSummary);

public sealed record FiscalExceptionSemanticHashRecalculationPreviewRequest(
    FiscalIssuanceReferenceRecord FiscalIssuanceReference,
    PosServerFiscalDocumentCreateRequest? OriginalFiscalRequestFacts = null,
    Guid? ServiceIdentityId = null,
    DateTimeOffset? RequestedAt = null);

public sealed record FiscalExceptionSemanticHashRecalculationPreviewResult(
    FiscalExceptionSemanticHashRecalculationPreviewStatus Status,
    string? BlockReasonCode,
    string SafeSummary,
    string? StoredSourceVersion,
    string RequiredSourceVersion,
    bool CompleteOriginalFiscalRequestFactsAvailable,
    string? RecalculatedHashValue,
    string? RecalculatedHashAlgorithm,
    string? RecalculatedHashSourceVersion,
    int? RecalculatedSourceFactCount,
    string? RecalculatedSafeSourceSummary,
    bool? RecalculatedHashMatchesStoredHash,
    DateTimeOffset? PreviewAttemptedAt,
    FiscalExceptionSemanticHashRecalculationMutationStatus MutationStatus,
    bool FiscalIssuanceReferenceMutated,
    bool PosServerPostCalled,
    bool RetryExecuted,
    bool RetryScheduled,
    bool PaymentFinalityChanged,
    bool ExitAuthorizationIssued,
    bool GateBehaviorTriggered,
    bool FiscalNumberEdited,
    bool ManualFiscalDocumentCreated,
    Guid? RecalculationPreviewAuditId = null,
    DateTimeOffset? RecalculationPreviewCreatedAt = null);

public interface IFiscalExceptionSemanticHashRecalculationPreviewService
{
    FiscalExceptionSemanticHashRecalculationPreviewResult Preview(
        FiscalExceptionSemanticHashRecalculationPreviewRequest request);

    Task<FiscalExceptionSemanticHashRecalculationPreviewResult> PreviewAsync(
        FiscalExceptionSemanticHashRecalculationPreviewRequest request,
        CancellationToken cancellationToken);
}

public sealed record FiscalExceptionSemanticHashRecalculationPreviewAuditWrite(
    Guid FiscalIssuanceReferenceId,
    string? StoredSemanticHashSourceVersion,
    string RequiredSemanticHashSourceVersion,
    string? StoredSemanticHashValue,
    FiscalExceptionSemanticHashRecalculationPreviewStatus PreviewStatus,
    string? BlockReasonCode,
    bool CompleteOriginalRequestFactsAvailable,
    string? RecalculatedHashValue,
    string? RecalculatedHashAlgorithm,
    string? RecalculatedHashSourceVersion,
    int? RecalculatedSourceFactCount,
    string? RecalculatedSafeSourceSummary,
    bool? RecalculatedHashMatchesStoredHash,
    FiscalExceptionSemanticHashRecalculationMutationStatus MutationStatus,
    DateTimeOffset AttemptedAt,
    string SafeSummary,
    Guid? CorrelationId,
    Guid? ServiceIdentityId);

public sealed record FiscalExceptionSemanticHashRecalculationPreviewAuditRecord(
    Guid RecalculationPreviewAuditId,
    Guid FiscalIssuanceReferenceId,
    string? StoredSemanticHashSourceVersion,
    string RequiredSemanticHashSourceVersion,
    string? StoredSemanticHashValue,
    FiscalExceptionSemanticHashRecalculationPreviewStatus PreviewStatus,
    string? BlockReasonCode,
    bool CompleteOriginalRequestFactsAvailable,
    string? RecalculatedHashValue,
    string? RecalculatedHashAlgorithm,
    string? RecalculatedHashSourceVersion,
    int? RecalculatedSourceFactCount,
    string? RecalculatedSafeSourceSummary,
    bool? RecalculatedHashMatchesStoredHash,
    FiscalExceptionSemanticHashRecalculationMutationStatus MutationStatus,
    DateTimeOffset AttemptedAt,
    string SafeSummary,
    Guid? CorrelationId,
    Guid? ServiceIdentityId,
    DateTimeOffset CreatedAt);

public sealed record FiscalExceptionSemanticHashRecalculationPreviewAuditSummary(
    Guid LastRecalculationPreviewAuditId,
    FiscalExceptionSemanticHashRecalculationPreviewStatus LastPreviewStatus,
    DateTimeOffset LastAttemptedAt,
    int AttemptCount,
    string? LastBlockReasonCode,
    bool CompleteOriginalRequestFactsAvailable,
    string? RecalculatedHashValue,
    string? RecalculatedHashAlgorithm,
    string? RecalculatedHashSourceVersion,
    int? RecalculatedSourceFactCount,
    string? RecalculatedSafeSourceSummary,
    bool? RecalculatedHashMatchesStoredHash,
    FiscalExceptionSemanticHashRecalculationMutationStatus MutationStatus,
    string SafeSummary);

public interface IFiscalExceptionSemanticHashRecalculationPreviewAuditRepository
{
    Task<FiscalExceptionSemanticHashRecalculationPreviewAuditRecord> RecordAsync(
        FiscalExceptionSemanticHashRecalculationPreviewAuditWrite attempt,
        CancellationToken cancellationToken);

    Task<FiscalExceptionSemanticHashRecalculationPreviewAuditSummary?> GetSummaryAsync(
        Guid fiscalIssuanceReferenceId,
        CancellationToken cancellationToken);
}

public sealed class FiscalExceptionSemanticHashControlledBackfillApprovalOptions
{
    public const string SectionName = "FiscalExceptionSemanticHashControlledBackfillApproval";

    public FiscalExceptionSemanticHashControlledBackfillApprovalOptions()
    {
    }

    public FiscalExceptionSemanticHashControlledBackfillApprovalOptions(
        bool approvalPolicyConfigured = false,
        bool dualControlRequired = true,
        bool dualControlSatisfied = false,
        bool actorOrServiceAuthorized = false,
        bool explicitApprovalPresent = false)
    {
        ApprovalPolicyConfigured = approvalPolicyConfigured;
        DualControlRequired = dualControlRequired;
        DualControlSatisfied = dualControlSatisfied;
        ActorOrServiceAuthorized = actorOrServiceAuthorized;
        ExplicitApprovalPresent = explicitApprovalPresent;
    }

    public bool ApprovalPolicyConfigured { get; set; }

    public bool DualControlRequired { get; set; } = true;

    public bool DualControlSatisfied { get; set; }

    public bool ActorOrServiceAuthorized { get; set; }

    public bool ExplicitApprovalPresent { get; set; }
}

public sealed record FiscalExceptionSemanticHashControlledBackfillApprovalRequest(
    FiscalExceptionQueueCaseDetail Detail,
    FiscalExceptionSemanticHashRecalculationPreviewAuditSummary? LatestRecalculationPreviewAuditSummary = null);

public sealed record FiscalExceptionSemanticHashControlledBackfillApprovalResult(
    FiscalExceptionSemanticHashControlledBackfillApprovalStatus Status,
    string? BlockReasonCode,
    string SafeSummary,
    string? LegacySourceVersion,
    string RequiredSourceVersion,
    Guid? LatestRecalculationPreviewAuditId,
    DateTimeOffset? LatestRecalculationPreviewAttemptedAt,
    bool LatestRecalculationPreviewAuditExists,
    bool PreviewSuccessful,
    bool CompleteOriginalRequestFactsAvailable,
    bool RecalculatedHashIsSha256V1,
    bool RecalculatedHashMetadataComplete,
    bool DualControlRequired,
    bool DualControlSatisfied,
    bool ExplicitApprovalPresent,
    bool ActorOrServiceAuthorizationPresent,
    FiscalExceptionSemanticHashControlledBackfillDualControlPosture DualControlPosture,
    FiscalExceptionSemanticHashControlledBackfillApprovalPosture ApprovalPosture,
    FiscalExceptionSemanticHashControlledBackfillActorAuthorizationPosture ActorAuthorizationPosture,
    FiscalExceptionSemanticHashRecalculationMutationStatus MutationStatus,
    bool FiscalIssuanceReferenceMutated,
    bool RetryExecutionAvailable,
    bool PosServerPostCalled,
    bool RetryExecuted,
    bool RetryScheduled,
    bool PaymentFinalityChanged,
    bool ExitAuthorizationIssued,
    bool GateBehaviorTriggered,
    bool FiscalNumberEdited,
    bool ManualFiscalDocumentCreated);

public interface IFiscalExceptionSemanticHashControlledBackfillApprovalService
{
    FiscalExceptionSemanticHashControlledBackfillApprovalResult Evaluate(
        FiscalExceptionSemanticHashControlledBackfillApprovalRequest request);
}

public sealed class FiscalExceptionSemanticHashControlledBackfillMutationOptions
{
    public const string SectionName = "FiscalExceptionSemanticHashControlledBackfillMutation";

    public FiscalExceptionSemanticHashControlledBackfillMutationOptions()
    {
    }

    public FiscalExceptionSemanticHashControlledBackfillMutationOptions(
        bool enableControlledMutation = false)
    {
        EnableControlledMutation = enableControlledMutation;
    }

    public bool EnableControlledMutation { get; set; }
}

public sealed record FiscalExceptionSemanticHashControlledBackfillMutationPreparationRequest(
    FiscalExceptionQueueCaseDetail Detail,
    FiscalExceptionSemanticHashControlledBackfillApprovalResult ApprovalBasis,
    FiscalExceptionSemanticHashRecalculationPreviewAuditSummary? LatestRecalculationPreviewAuditSummary,
    Guid? ActorServiceIdentityId = null,
    string? ApprovalReference = null,
    string? DualControlReference = null,
    bool DryRunOnly = true,
    DateTimeOffset? RequestedAt = null);

public sealed record FiscalExceptionSemanticHashControlledBackfillMutationCommand(
    Guid FiscalIssuanceReferenceId,
    Guid LatestRecalculationPreviewAuditId,
    FiscalExceptionSemanticHashControlledBackfillApprovalStatus ApprovalBasisStatus,
    string? StoredSourceVersion,
    string RequiredSourceVersion,
    string RecalculatedHashValue,
    string RecalculatedHashAlgorithm,
    string RecalculatedHashSourceVersion,
    int RecalculatedSourceFactCount,
    string RecalculatedSafeSourceSummary,
    Guid? ActorServiceIdentityId,
    string? ApprovalReference,
    string? DualControlReference,
    Guid? CorrelationId,
    FiscalExceptionSemanticHashControlledBackfillMutationMode MutationMode,
    bool DryRunOnly,
    FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus MutationStatus);

public sealed record FiscalExceptionSemanticHashControlledBackfillMutationPreparationResult(
    FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus Status,
    string? BlockReasonCode,
    string SafeSummary,
    FiscalExceptionSemanticHashControlledBackfillMutationCommand? Command,
    FiscalExceptionSemanticHashControlledBackfillMutationMode MutationMode,
    bool MutationEnabled,
    bool DryRunOnly,
    bool AuditPersisted,
    bool FiscalIssuanceReferenceMutated,
    bool RetryExecutionAvailable,
    bool PosServerPostCalled,
    bool RetryExecuted,
    bool RetryScheduled,
    bool PaymentFinalityChanged,
    bool ExitAuthorizationIssued,
    bool GateBehaviorTriggered,
    bool FiscalNumberEdited,
    bool ManualFiscalDocumentCreated,
    Guid? MutationAuditId = null,
    DateTimeOffset? MutationAttemptedAt = null);

public sealed record FiscalExceptionSemanticHashGuardedBackfillMutationRequest(
    FiscalExceptionQueueCaseDetail Detail,
    FiscalExceptionSemanticHashControlledBackfillApprovalResult ApprovalBasis,
    FiscalExceptionSemanticHashRecalculationPreviewAuditSummary LatestRecalculationPreviewAuditSummary,
    FiscalExceptionSemanticHashControlledBackfillMutationPreparationResult MutationPreparationBasis,
    Guid ActorServiceIdentityId,
    string ApprovalReference,
    string? DualControlReference = null,
    DateTimeOffset? RequestedAt = null);

public sealed record FiscalExceptionSemanticHashGuardedBackfillMutationCommand(
    Guid FiscalIssuanceReferenceId,
    Guid RecalculationPreviewAuditId,
    Guid MutationPreparationAuditId,
    FiscalExceptionSemanticHashControlledBackfillApprovalStatus ApprovalBasisStatus,
    string ExpectedOldSourceVersion,
    string RequiredSourceVersion,
    string? OldHashValue,
    string NewHashValue,
    string NewHashAlgorithm,
    string NewHashSourceVersion,
    int NewHashSourceFactCount,
    string SafeSourceSummary,
    Guid ActorServiceIdentityId,
    string ApprovalReference,
    string? DualControlReference,
    Guid? CorrelationId,
    DateTimeOffset AttemptedAt);

public sealed record FiscalExceptionSemanticHashGuardedBackfillMutationResult(
    FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus Status,
    string? BlockReasonCode,
    string SafeSummary,
    Guid? MutationAuditId,
    string? OldSourceVersion,
    string? NewSourceVersion,
    string? OldHashValue,
    string? NewHashValue,
    DateTimeOffset? MutationTimestamp,
    bool FiscalIssuanceReferenceMutated,
    bool RetryExecutionAvailable,
    bool PosServerPostCalled,
    bool RetryExecuted,
    bool RetryScheduled,
    bool PaymentFinalityChanged,
    bool ExitAuthorizationIssued,
    bool GateBehaviorTriggered,
    bool FiscalNumberEdited,
    bool ManualFiscalDocumentCreated);

public interface IFiscalExceptionSemanticHashControlledBackfillMutationPreparationService
{
    Task<FiscalExceptionSemanticHashControlledBackfillMutationPreparationResult> PrepareAsync(
        FiscalExceptionSemanticHashControlledBackfillMutationPreparationRequest request,
        CancellationToken cancellationToken);
}

public interface IFiscalExceptionSemanticHashGuardedBackfillMutationService
{
    Task<FiscalExceptionSemanticHashGuardedBackfillMutationResult> MutateAsync(
        FiscalExceptionSemanticHashGuardedBackfillMutationRequest request,
        CancellationToken cancellationToken);
}

public interface IFiscalExceptionSemanticHashGuardedBackfillMutationRepository
{
    Task<FiscalExceptionSemanticHashGuardedBackfillMutationResult> MutateAsync(
        FiscalExceptionSemanticHashGuardedBackfillMutationCommand command,
        CancellationToken cancellationToken);
}

public sealed class FiscalExceptionSemanticHashBackfillOperatorWorkflowOptions
{
    public const string SectionName = "FiscalExceptionSemanticHashBackfillOperatorWorkflow";

    public FiscalExceptionSemanticHashBackfillOperatorWorkflowOptions()
    {
    }

    public FiscalExceptionSemanticHashBackfillOperatorWorkflowOptions(
        bool enableControlledMutationInvocation = false)
    {
        EnableControlledMutationInvocation = enableControlledMutationInvocation;
    }

    public bool EnableControlledMutationInvocation { get; set; }
}

public sealed class FiscalExceptionSemanticHashBackfillInternalApiOptions
{
    public const string SectionName = "FiscalExceptionSemanticHashBackfillInternalApi";

    public FiscalExceptionSemanticHashBackfillInternalApiOptions()
    {
    }

    public FiscalExceptionSemanticHashBackfillInternalApiOptions(bool enabled = false)
    {
        Enabled = enabled;
    }

    public bool Enabled { get; set; }
}

public sealed record FiscalExceptionSemanticHashBackfillInternalApiRequest(
    Guid FiscalIssuanceReferenceId,
    Guid? RecalculationPreviewAuditId,
    Guid? MutationPreparationAuditId,
    string? ApprovalReference,
    string? DualControlReference,
    Guid? ActorServiceIdentityId,
    string? ReasonCode,
    string? SafeJustification,
    Guid? CorrelationId,
    bool DryRunOnly = true,
    bool ExecuteControlledMutation = false,
    IReadOnlyList<Guid>? FiscalIssuanceReferenceIds = null);

public sealed record FiscalExceptionSemanticHashBackfillInternalApiResponse(
    Guid? WorkflowRequestId,
    FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus WorkflowStatus,
    string? BlockReasonCode,
    FiscalExceptionSemanticHashBackfillOperatorWorkflowMutationInvocationPosture MutationInvocationPosture,
    Guid? GuardedMutationAuditId,
    FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus? GuardedMutationStatus,
    bool RetryExecutionAvailable,
    string SafeSummary,
    int HttpStatusCode);

public interface IFiscalExceptionSemanticHashBackfillInternalApiHandler
{
    Task<FiscalExceptionSemanticHashBackfillInternalApiResponse> RequestAsync(
        FiscalExceptionSemanticHashBackfillInternalApiRequest request,
        CancellationToken cancellationToken);
}

public sealed record FiscalExceptionSemanticHashBackfillOperatorWorkflowRequest(
    FiscalExceptionQueueCaseDetail Detail,
    Guid FiscalIssuanceReferenceId,
    Guid? RecalculationPreviewAuditId,
    Guid? MutationPreparationAuditId,
    Guid? ActorServiceIdentityId,
    string? ApprovalReference,
    string? DualControlReference,
    string? ReasonCode,
    string? SafeJustification,
    Guid? CorrelationId,
    FiscalExceptionSemanticHashBackfillOperatorWorkflowRequestMode RequestMode =
        FiscalExceptionSemanticHashBackfillOperatorWorkflowRequestMode.SingleRecordOnly,
    bool ExecuteControlledMutation = false,
    bool DryRunOnly = true,
    DateTimeOffset? RequestedAt = null,
    FiscalExceptionSemanticHashRecalculationPreviewAuditSummary? LatestRecalculationPreviewAuditSummary = null,
    FiscalExceptionSemanticHashControlledBackfillMutationPreparationResult? MutationPreparationBasis = null);

public sealed record FiscalExceptionSemanticHashBackfillOperatorWorkflowResult(
    FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus Status,
    string? BlockReasonCode,
    string SafeSummary,
    Guid? WorkflowRequestId,
    Guid FiscalIssuanceReferenceId,
    Guid? RecalculationPreviewAuditId,
    Guid? MutationPreparationAuditId,
    string? ApprovalReference,
    string? DualControlReference,
    Guid? ActorServiceIdentityId,
    string? ReasonCode,
    FiscalExceptionSemanticHashBackfillOperatorWorkflowRequestMode RequestMode,
    FiscalExceptionSemanticHashBackfillOperatorWorkflowMutationInvocationPosture MutationInvocationPosture,
    bool ExecuteControlledMutationRequested,
    bool MutationInvocationEnabled,
    bool DryRunOnly,
    Guid? GuardedMutationAuditId,
    FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus? GuardedMutationStatus,
    bool WorkflowAuditPersisted,
    DateTimeOffset RequestedAt,
    DateTimeOffset? CreatedAt,
    bool FiscalIssuanceReferenceMutated,
    bool RetryExecutionAvailable,
    bool PosServerPostCalled,
    bool RetryExecuted,
    bool RetryScheduled,
    bool PaymentFinalityChanged,
    bool ExitAuthorizationIssued,
    bool GateBehaviorTriggered,
    bool FiscalNumberEdited,
    bool ManualFiscalDocumentCreated);

public interface IFiscalExceptionSemanticHashBackfillOperatorWorkflowService
{
    Task<FiscalExceptionSemanticHashBackfillOperatorWorkflowResult> RequestAsync(
        FiscalExceptionSemanticHashBackfillOperatorWorkflowRequest request,
        CancellationToken cancellationToken);
}

public sealed record FiscalExceptionSemanticHashBackfillOperatorWorkflowAuditWrite(
    Guid FiscalIssuanceReferenceId,
    Guid? RecalculationPreviewAuditId,
    Guid? MutationPreparationAuditId,
    string? ApprovalReference,
    string? DualControlReference,
    Guid? ActorServiceIdentityId,
    string? ReasonCode,
    string? SafeJustification,
    FiscalExceptionSemanticHashBackfillOperatorWorkflowRequestMode RequestMode,
    FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus WorkflowStatus,
    string? BlockReasonCode,
    FiscalExceptionSemanticHashBackfillOperatorWorkflowMutationInvocationPosture MutationInvocationPosture,
    Guid? GuardedMutationAuditId,
    FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus? GuardedMutationStatus,
    bool ExecuteControlledMutationRequested,
    bool MutationInvocationEnabled,
    bool DryRunOnly,
    DateTimeOffset RequestedAt,
    Guid? CorrelationId,
    string SafeSummary);

public sealed record FiscalExceptionSemanticHashBackfillOperatorWorkflowAuditRecord(
    Guid WorkflowRequestId,
    Guid FiscalIssuanceReferenceId,
    Guid? RecalculationPreviewAuditId,
    Guid? MutationPreparationAuditId,
    string? ApprovalReference,
    string? DualControlReference,
    Guid? ActorServiceIdentityId,
    string? ReasonCode,
    string? SafeJustification,
    FiscalExceptionSemanticHashBackfillOperatorWorkflowRequestMode RequestMode,
    FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus WorkflowStatus,
    string? BlockReasonCode,
    FiscalExceptionSemanticHashBackfillOperatorWorkflowMutationInvocationPosture MutationInvocationPosture,
    Guid? GuardedMutationAuditId,
    FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus? GuardedMutationStatus,
    bool ExecuteControlledMutationRequested,
    bool MutationInvocationEnabled,
    bool DryRunOnly,
    DateTimeOffset RequestedAt,
    Guid? CorrelationId,
    string SafeSummary,
    DateTimeOffset CreatedAt);

public sealed record FiscalExceptionSemanticHashBackfillOperatorWorkflowAuditSummary(
    Guid LastWorkflowRequestId,
    FiscalExceptionSemanticHashBackfillOperatorWorkflowStatus LastWorkflowStatus,
    DateTimeOffset LastRequestedAt,
    int RequestCount,
    string? LastBlockReasonCode,
    string? ApprovalReference,
    FiscalExceptionSemanticHashControlledBackfillDualControlPosture DualControlPosture,
    FiscalExceptionSemanticHashBackfillOperatorWorkflowMutationInvocationPosture MutationInvocationPosture,
    Guid? GuardedMutationAuditId,
    FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus? GuardedMutationStatus,
    string SafeSummary);

public interface IFiscalExceptionSemanticHashBackfillOperatorWorkflowAuditRepository
{
    Task<FiscalExceptionSemanticHashBackfillOperatorWorkflowAuditRecord> RecordAsync(
        FiscalExceptionSemanticHashBackfillOperatorWorkflowAuditWrite attempt,
        CancellationToken cancellationToken);

    Task<FiscalExceptionSemanticHashBackfillOperatorWorkflowAuditSummary?> GetSummaryAsync(
        Guid fiscalIssuanceReferenceId,
        CancellationToken cancellationToken);
}

public sealed record FiscalExceptionSemanticHashControlledBackfillMutationAuditWrite(
    Guid FiscalIssuanceReferenceId,
    Guid? RecalculationPreviewAuditId,
    Guid? MutationPreparationAuditId,
    FiscalExceptionSemanticHashControlledBackfillApprovalStatus ApprovalBasisStatus,
    string? OldSourceVersion,
    string RequiredSourceVersion,
    string? OldHashValue,
    string? NewHashValue,
    string? NewHashAlgorithm,
    string? NewHashSourceVersion,
    int? NewHashSourceFactCount,
    string? SafeSourceSummary,
    FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus MutationStatus,
    string? BlockReasonCode,
    FiscalExceptionSemanticHashControlledBackfillMutationMode MutationMode,
    bool MutationEnabled,
    bool FiscalIssuanceReferenceMutated,
    DateTimeOffset AttemptedAt,
    string SafeSummary,
    Guid? CorrelationId,
    Guid? ActorServiceIdentityId,
    string? ApprovalReference,
    string? DualControlReference);

public sealed record FiscalExceptionSemanticHashControlledBackfillMutationAuditRecord(
    Guid MutationAuditId,
    Guid FiscalIssuanceReferenceId,
    Guid? RecalculationPreviewAuditId,
    Guid? MutationPreparationAuditId,
    FiscalExceptionSemanticHashControlledBackfillApprovalStatus ApprovalBasisStatus,
    string? OldSourceVersion,
    string RequiredSourceVersion,
    string? OldHashValue,
    string? NewHashValue,
    string? NewHashAlgorithm,
    string? NewHashSourceVersion,
    int? NewHashSourceFactCount,
    string? SafeSourceSummary,
    FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus MutationStatus,
    string? BlockReasonCode,
    FiscalExceptionSemanticHashControlledBackfillMutationMode MutationMode,
    bool MutationEnabled,
    bool FiscalIssuanceReferenceMutated,
    DateTimeOffset AttemptedAt,
    string SafeSummary,
    Guid? CorrelationId,
    Guid? ActorServiceIdentityId,
    string? ApprovalReference,
    string? DualControlReference,
    DateTimeOffset CreatedAt);

public sealed record FiscalExceptionSemanticHashControlledBackfillMutationAuditSummary(
    Guid LastMutationAuditId,
    FiscalExceptionSemanticHashControlledBackfillMutationPreparationStatus LastMutationStatus,
    DateTimeOffset LastAttemptedAt,
    int AttemptCount,
    string? LastBlockReasonCode,
    FiscalExceptionSemanticHashControlledBackfillMutationMode MutationMode,
    bool MutationEnabled,
    bool FiscalIssuanceReferenceMutated,
    string? OldSourceVersion,
    string? NewSourceVersion,
    string? NewHashValue,
    string SafeSummary);

public interface IFiscalExceptionSemanticHashControlledBackfillMutationAuditRepository
{
    Task<FiscalExceptionSemanticHashControlledBackfillMutationAuditRecord> RecordAsync(
        FiscalExceptionSemanticHashControlledBackfillMutationAuditWrite attempt,
        CancellationToken cancellationToken);

    Task<FiscalExceptionSemanticHashControlledBackfillMutationAuditRecord?> GetRecordAsync(
        Guid mutationAuditId,
        CancellationToken cancellationToken);

    Task<FiscalExceptionSemanticHashControlledBackfillMutationAuditSummary?> GetSummaryAsync(
        Guid fiscalIssuanceReferenceId,
        CancellationToken cancellationToken);
}

public sealed record FiscalSemanticRequestHashParityFixture(
    string PosServerHashSourceVersion,
    string PosServerCanonicalSourceText,
    string PosServerSemanticRequestHash);

public sealed record FiscalSemanticRequestHashParityProofResult(
    FiscalSemanticRequestHashParityProofStatus Status,
    string? BlockReasonCode,
    string SafeSummary,
    string CentralPmsHashSourceVersion,
    string? CentralPmsCanonicalSourceText,
    IReadOnlyList<string> CentralPmsNormalizedFacts,
    string? CentralPmsSemanticRequestHash,
    string? PosServerExpectedHashSourceVersion,
    string? PosServerExpectedCanonicalSourceText,
    string? PosServerExpectedSemanticRequestHash);

public interface IFiscalSemanticRequestHashParityProofService
{
    FiscalSemanticRequestHashParityProofResult Prove(
        PosServerFiscalDocumentCreateRequest request,
        FiscalSemanticRequestHashParityFixture? posServerExpected);
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

