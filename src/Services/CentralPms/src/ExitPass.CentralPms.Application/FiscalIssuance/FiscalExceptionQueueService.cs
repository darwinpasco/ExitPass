using ExitPass.CentralPms.Domain.FiscalIssuance;

namespace ExitPass.CentralPms.Application.FiscalIssuance;

public interface IFiscalExceptionQueueService
{
    Task<IReadOnlyList<FiscalExceptionQueueCaseSummary>> ListAsync(
        FiscalExceptionQueueQuery query,
        CancellationToken cancellationToken);

    Task<FiscalExceptionQueueCaseDetail?> GetAsync(
        Guid caseId,
        CancellationToken cancellationToken);

    Task<FiscalExceptionQueueCaseDetail> CreateOrUpdateFromFiscalReferenceAsync(
        FiscalIssuanceReferenceRecord source,
        CancellationToken cancellationToken);

    Task<FiscalExceptionReadbackPreparation?> PrepareReadbackAsync(
        Guid caseId,
        CancellationToken cancellationToken);
}

public sealed class FiscalExceptionQueueService : IFiscalExceptionQueueService
{
    private const string DuplicateCollapseStrategy = "source_fiscal_issuance_reference_identity";

    private readonly IFiscalExceptionQueueReferenceReader _referenceReader;
    private readonly IFiscalExceptionReadbackAttemptRepository? _readbackAttemptRepository;
    private readonly IFiscalExceptionRetryEligibilityEvaluator _retryEligibilityEvaluator;
    private readonly IFiscalExceptionRetryCommandPreparationService _retryCommandPreparationService;
    private readonly IFiscalExceptionRetryCommandPreparationAuditRepository? _retryCommandPreparationAuditRepository;
    private readonly IFiscalExceptionRetrySchedulingPreparationService _retrySchedulingPreparationService;
    private readonly IFiscalExceptionRetrySchedulingPreparationAuditRepository? _retrySchedulingPreparationAuditRepository;
    private readonly IFiscalExceptionRetryExecutionPreparationService _retryExecutionPreparationService;
    private readonly IFiscalExceptionPosServerRetryContractReadinessService _posServerRetryContractReadinessService;

    public FiscalExceptionQueueService(IFiscalExceptionQueueReferenceReader referenceReader)
        : this(referenceReader, null)
    {
    }

    public FiscalExceptionQueueService(
        IFiscalExceptionQueueReferenceReader referenceReader,
        IFiscalExceptionReadbackAttemptRepository? readbackAttemptRepository)
        : this(
            referenceReader,
            readbackAttemptRepository,
            new FiscalExceptionRetryEligibilityEvaluator(),
            new FiscalExceptionRetryCommandPreparationService(),
            retryCommandPreparationAuditRepository: null,
            new FiscalExceptionRetrySchedulingPreparationService(),
            retrySchedulingPreparationAuditRepository: null)
    {
    }

    public FiscalExceptionQueueService(
        IFiscalExceptionQueueReferenceReader referenceReader,
        IFiscalExceptionReadbackAttemptRepository? readbackAttemptRepository,
        IFiscalExceptionRetryEligibilityEvaluator retryEligibilityEvaluator)
        : this(
            referenceReader,
            readbackAttemptRepository,
            retryEligibilityEvaluator,
            new FiscalExceptionRetryCommandPreparationService(),
            retryCommandPreparationAuditRepository: null,
            new FiscalExceptionRetrySchedulingPreparationService(),
            retrySchedulingPreparationAuditRepository: null)
    {
    }

    public FiscalExceptionQueueService(
        IFiscalExceptionQueueReferenceReader referenceReader,
        IFiscalExceptionReadbackAttemptRepository? readbackAttemptRepository,
        IFiscalExceptionRetryEligibilityEvaluator retryEligibilityEvaluator,
        IFiscalExceptionRetryCommandPreparationService retryCommandPreparationService)
        : this(
            referenceReader,
            readbackAttemptRepository,
            retryEligibilityEvaluator,
            retryCommandPreparationService,
            retryCommandPreparationAuditRepository: null,
            new FiscalExceptionRetrySchedulingPreparationService(),
            retrySchedulingPreparationAuditRepository: null)
    {
    }

    public FiscalExceptionQueueService(
        IFiscalExceptionQueueReferenceReader referenceReader,
        IFiscalExceptionReadbackAttemptRepository? readbackAttemptRepository,
        IFiscalExceptionRetryEligibilityEvaluator retryEligibilityEvaluator,
        IFiscalExceptionRetryCommandPreparationService retryCommandPreparationService,
        IFiscalExceptionRetryCommandPreparationAuditRepository? retryCommandPreparationAuditRepository)
        : this(
            referenceReader,
            readbackAttemptRepository,
            retryEligibilityEvaluator,
            retryCommandPreparationService,
            retryCommandPreparationAuditRepository,
            new FiscalExceptionRetrySchedulingPreparationService(),
            retrySchedulingPreparationAuditRepository: null)
    {
    }

    public FiscalExceptionQueueService(
        IFiscalExceptionQueueReferenceReader referenceReader,
        IFiscalExceptionReadbackAttemptRepository? readbackAttemptRepository,
        IFiscalExceptionRetryEligibilityEvaluator retryEligibilityEvaluator,
        IFiscalExceptionRetryCommandPreparationService retryCommandPreparationService,
        IFiscalExceptionRetryCommandPreparationAuditRepository? retryCommandPreparationAuditRepository,
        IFiscalExceptionRetrySchedulingPreparationService retrySchedulingPreparationService,
        IFiscalExceptionRetrySchedulingPreparationAuditRepository? retrySchedulingPreparationAuditRepository)
        : this(
            referenceReader,
            readbackAttemptRepository,
            retryEligibilityEvaluator,
            retryCommandPreparationService,
            retryCommandPreparationAuditRepository,
            retrySchedulingPreparationService,
            retrySchedulingPreparationAuditRepository,
            new FiscalExceptionRetryExecutionPreparationService())
    {
    }

    public FiscalExceptionQueueService(
        IFiscalExceptionQueueReferenceReader referenceReader,
        IFiscalExceptionReadbackAttemptRepository? readbackAttemptRepository,
        IFiscalExceptionRetryEligibilityEvaluator retryEligibilityEvaluator,
        IFiscalExceptionRetryCommandPreparationService retryCommandPreparationService,
        IFiscalExceptionRetryCommandPreparationAuditRepository? retryCommandPreparationAuditRepository,
        IFiscalExceptionRetrySchedulingPreparationService retrySchedulingPreparationService,
        IFiscalExceptionRetrySchedulingPreparationAuditRepository? retrySchedulingPreparationAuditRepository,
        IFiscalExceptionRetryExecutionPreparationService retryExecutionPreparationService)
        : this(
            referenceReader,
            readbackAttemptRepository,
            retryEligibilityEvaluator,
            retryCommandPreparationService,
            retryCommandPreparationAuditRepository,
            retrySchedulingPreparationService,
            retrySchedulingPreparationAuditRepository,
            retryExecutionPreparationService,
            new FiscalExceptionPosServerRetryContractReadinessService())
    {
    }

    public FiscalExceptionQueueService(
        IFiscalExceptionQueueReferenceReader referenceReader,
        IFiscalExceptionReadbackAttemptRepository? readbackAttemptRepository,
        IFiscalExceptionRetryEligibilityEvaluator retryEligibilityEvaluator,
        IFiscalExceptionRetryCommandPreparationService retryCommandPreparationService,
        IFiscalExceptionRetryCommandPreparationAuditRepository? retryCommandPreparationAuditRepository,
        IFiscalExceptionRetrySchedulingPreparationService retrySchedulingPreparationService,
        IFiscalExceptionRetrySchedulingPreparationAuditRepository? retrySchedulingPreparationAuditRepository,
        IFiscalExceptionRetryExecutionPreparationService retryExecutionPreparationService,
        IFiscalExceptionPosServerRetryContractReadinessService posServerRetryContractReadinessService)
    {
        _referenceReader = referenceReader;
        _readbackAttemptRepository = readbackAttemptRepository;
        _retryEligibilityEvaluator = retryEligibilityEvaluator;
        _retryCommandPreparationService = retryCommandPreparationService;
        _retryCommandPreparationAuditRepository = retryCommandPreparationAuditRepository;
        _retrySchedulingPreparationService = retrySchedulingPreparationService;
        _retrySchedulingPreparationAuditRepository = retrySchedulingPreparationAuditRepository;
        _retryExecutionPreparationService = retryExecutionPreparationService;
        _posServerRetryContractReadinessService = posServerRetryContractReadinessService;
    }

    public async Task<IReadOnlyList<FiscalExceptionQueueCaseSummary>> ListAsync(
        FiscalExceptionQueueQuery query,
        CancellationToken cancellationToken)
    {
        var records = await _referenceReader.ListFiscalExceptionReferencesAsync(
            NormalizeQuery(query),
            cancellationToken);

        return records
            .Where(IsFiscalExceptionCandidate)
            .Select(ToSummary)
            .ToArray();
    }

    public async Task<FiscalExceptionQueueCaseDetail?> GetAsync(
        Guid caseId,
        CancellationToken cancellationToken)
    {
        if (caseId == Guid.Empty)
        {
            throw new ArgumentException("FEQ case id is required.", nameof(caseId));
        }

        var record = await _referenceReader.FindFiscalExceptionReferenceAsync(caseId, cancellationToken);
        if (record is null || !IsFiscalExceptionCandidate(record))
        {
            return null;
        }

        var detail = ToDetail(record);
        if (_readbackAttemptRepository is not null)
        {
            var attemptSummary = await _readbackAttemptRepository.GetSummaryAsync(
                record.FiscalIssuanceReferenceId,
                cancellationToken);

            if (attemptSummary is not null)
            {
                detail = ApplyReadbackAttemptSummary(detail, attemptSummary);
            }
        }

        detail = ApplyRetryEligibilityEvaluation(
            detail,
            _retryEligibilityEvaluator.Evaluate(detail));

        var commandPreparation = await _retryCommandPreparationService.PrepareAsync(
            new FiscalExceptionRetryCommandPreparationRequest(detail),
            cancellationToken);

        detail = ApplyRetryCommandPreparation(detail, commandPreparation);

        if (_retryCommandPreparationAuditRepository is not null)
        {
            var auditSummary = await _retryCommandPreparationAuditRepository.GetSummaryAsync(
                record.FiscalIssuanceReferenceId,
                cancellationToken);

            if (auditSummary is not null)
            {
                detail = ApplyRetryCommandPreparationAuditSummary(detail, auditSummary);
            }
        }

        var schedulingPreparation = await _retrySchedulingPreparationService.PrepareAsync(
                new FiscalExceptionRetrySchedulingPreparationRequest(detail, commandPreparation),
                cancellationToken);

        detail = ApplyRetrySchedulingPreparation(detail, schedulingPreparation);

        if (_retrySchedulingPreparationAuditRepository is not null)
        {
            var schedulingAuditSummary = await _retrySchedulingPreparationAuditRepository.GetSummaryAsync(
                record.FiscalIssuanceReferenceId,
                cancellationToken);

            if (schedulingAuditSummary is not null)
            {
                detail = ApplyRetrySchedulingPreparationAuditSummary(detail, schedulingAuditSummary);
            }
        }

        var contractReadiness = _posServerRetryContractReadinessService.Evaluate(
            new FiscalExceptionPosServerRetryContractReadinessRequest(detail));

        detail = ApplyPosServerRetryContractReadiness(detail, contractReadiness);

        var executionPreparation = await _retryExecutionPreparationService.EvaluateAsync(
            new FiscalExceptionRetryExecutionPreparationRequest(
                detail,
                commandPreparation,
                schedulingPreparation,
                contractReadiness),
            cancellationToken);

        return ApplyRetryExecutionPreparation(detail, executionPreparation);
    }

    internal static FiscalExceptionQueueCaseDetail ApplyReadbackAttemptSummary(
        FiscalExceptionQueueCaseDetail detail,
        FiscalExceptionReadbackAttemptSummary attemptSummary)
    {
        var current = detail.Summary;
        var summary = current with
        {
            ReadbackStatus = FiscalExceptionReadbackStatus.Attempted,
            ReadbackClassification = attemptSummary.Classification,
            LastReadbackAttemptAt = attemptSummary.AttemptedAt,
            ReadbackAttemptCount = attemptSummary.AttemptCount,
            LastReadbackSafeSummary = attemptSummary.SafeErrorSummary ?? current.LastReadbackSafeSummary
        };

        return detail with
        {
            Summary = summary
        };
    }

    internal static FiscalExceptionQueueCaseDetail ApplyRetryEligibilityEvaluation(
        FiscalExceptionQueueCaseDetail detail,
        FiscalExceptionRetryEligibilityEvaluation evaluation)
    {
        var current = detail.Summary;
        var summary = current with
        {
            RetryEligibilityStatus = evaluation.Status,
            RetryEligibilityDecision = evaluation.Decision,
            RetryBlockReasonCode = evaluation.BlockReasonCode,
            SafeRetryEligibilitySummary = evaluation.SafeSummary,
            RetryEligibilityEvaluatedAt = evaluation.EvaluatedAt,
            RetryEligibilityBasedOnReadbackClassification = evaluation.BasedOnReadbackClassification,
            RetryExecutionAvailable = false
        };

        return detail with
        {
            Summary = summary
        };
    }

    internal static FiscalExceptionQueueCaseDetail ApplyRetryCommandPreparation(
        FiscalExceptionQueueCaseDetail detail,
        FiscalExceptionRetryCommandPreparationResult preparation)
    {
        var current = detail.Summary;
        var summary = current with
        {
            RetryCommandPreparationStatus = preparation.Status,
            RetryCommandBlockReasonCode = preparation.BlockReasonCode,
            SafeRetryCommandPreparationSummary = preparation.SafeSummary,
            SemanticRequestHashAvailabilityStatus = preparation.SemanticRequestHashAvailabilityStatus,
            IdempotencyContextAvailabilityStatus = preparation.IdempotencyContextAvailabilityStatus,
            LastRetryCommandPreparationAttemptAt = preparation.RetryCommandPreparationAttemptedAt,
            RetryExecutionAvailable = false
        };

        return detail with
        {
            Summary = summary
        };
    }

    internal static FiscalExceptionQueueCaseDetail ApplyRetryCommandPreparationAuditSummary(
        FiscalExceptionQueueCaseDetail detail,
        FiscalExceptionRetryCommandPreparationAttemptSummary auditSummary)
    {
        var current = detail.Summary;
        var summary = current with
        {
            RetryCommandPreparationStatus = auditSummary.LastCommandPreparationStatus,
            RetryCommandBlockReasonCode = auditSummary.LastCommandBlockReasonCode,
            SafeRetryCommandPreparationSummary = auditSummary.SafeSummary,
            SemanticRequestHashAvailabilityStatus = auditSummary.SemanticRequestHashAvailabilityStatus,
            IdempotencyContextAvailabilityStatus = auditSummary.IdempotencyContextAvailabilityStatus,
            LastRetryCommandPreparationAttemptAt = auditSummary.LastAttemptedAt,
            RetryCommandPreparationAttemptCount = auditSummary.AttemptCount,
            RetryExecutionAvailable = false
        };

        return detail with
        {
            Summary = summary
        };
    }

    internal static FiscalExceptionQueueCaseDetail ApplyRetrySchedulingPreparation(
        FiscalExceptionQueueCaseDetail detail,
        FiscalExceptionRetrySchedulingPreparationResult preparation)
    {
        var current = detail.Summary;
        var summary = current with
        {
            RetrySchedulingPreparationStatus = preparation.Status,
            RetrySchedulingBlockReasonCode = preparation.BlockReasonCode,
            SafeRetrySchedulingPreparationSummary = preparation.SafeSummary,
            LastRetrySchedulingPreparationAttemptAt = preparation.RetrySchedulePreparationAttemptedAt,
            RetryExecutionAvailable = false
        };

        return detail with
        {
            Summary = summary
        };
    }

    internal static FiscalExceptionQueueCaseDetail ApplyRetrySchedulingPreparationAuditSummary(
        FiscalExceptionQueueCaseDetail detail,
        FiscalExceptionRetrySchedulingPreparationAttemptSummary auditSummary)
    {
        var current = detail.Summary;
        var summary = current with
        {
            RetrySchedulingPreparationStatus = auditSummary.LastSchedulingPreparationStatus,
            RetrySchedulingBlockReasonCode = auditSummary.LastSchedulingBlockReasonCode,
            SafeRetrySchedulingPreparationSummary = auditSummary.SafeSummary,
            LastRetrySchedulingPreparationAttemptAt = auditSummary.LastRequestedAt,
            RetrySchedulingPreparationAttemptCount = auditSummary.AttemptCount,
            RetryExecutionAvailable = false
        };

        return detail with
        {
            Summary = summary
        };
    }

    internal static FiscalExceptionQueueCaseDetail ApplyRetryExecutionPreparation(
        FiscalExceptionQueueCaseDetail detail,
        FiscalExceptionRetryExecutionPreparationResult preparation)
    {
        var current = detail.Summary;
        var summary = current with
        {
            RetryExecutionPreparationStatus = preparation.Status,
            RetryExecutionBlockReasonCode = preparation.BlockReasonCode,
            SafeRetryExecutionPreparationSummary = preparation.SafeSummary,
            RetryExecutionDualControlRequired = preparation.DualControlRequired,
            RetryExecutionAuthorizationStatus = preparation.AuthorizationStatus,
            RetryExecutionPosServerReadinessStatus = preparation.PosServerReadinessStatus,
            RetryExecutionAvailable = false
        };

        return detail with
        {
            Summary = summary
        };
    }

    internal static FiscalExceptionQueueCaseDetail ApplyPosServerRetryContractReadiness(
        FiscalExceptionQueueCaseDetail detail,
        FiscalExceptionPosServerRetryContractReadinessResult readiness)
    {
        var current = detail.Summary;
        var summary = current with
        {
            PosServerRetryContractReadinessStatus = readiness.Status,
            PosServerSemanticHashCompatibilityStatus = readiness.SemanticHashCompatibilityStatus,
            PosServerIdempotencyMappingStatus = readiness.IdempotencyMappingStatus,
            PosServerReadbackFieldCompatibilityStatus = readiness.ReadbackFieldCompatibilityStatus,
            PosServerFiscalNumberingReadinessStatus = readiness.FiscalNumberingReadinessStatus,
            PosServerConflictReplayBehaviorStatus = readiness.ConflictReplayBehaviorStatus,
            PosServerRetryContractBlockReasonCode = readiness.BlockReasonCode,
            SafePosServerRetryContractReadinessSummary = readiness.SafeSummary,
            RetryExecutionAvailable = false
        };

        return detail with
        {
            Summary = summary
        };
    }

    public Task<FiscalExceptionQueueCaseDetail> CreateOrUpdateFromFiscalReferenceAsync(
        FiscalIssuanceReferenceRecord source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!IsFiscalExceptionCandidate(source))
        {
            throw new ArgumentException(
                "Fiscal issuance reference is not in a FEQ-visible exception state.",
                nameof(source));
        }

        // This first FEQ slice uses the fiscal reference row as the stable case identity.
        // Duplicate detections for the same fiscal reference collapse to the same case id.
        return Task.FromResult(ToDetail(source));
    }

    public async Task<FiscalExceptionReadbackPreparation?> PrepareReadbackAsync(
        Guid caseId,
        CancellationToken cancellationToken)
    {
        var detail = await GetAsync(caseId, cancellationToken);
        if (detail is null)
        {
            return null;
        }

        var summary = detail.Summary;
        var readbackRequired = summary.ReadbackStatus != FiscalExceptionReadbackStatus.NotRequired;

        return new FiscalExceptionReadbackPreparation(
            CaseId: summary.CaseId,
            FiscalIssuanceReferenceId: summary.FiscalIssuanceReferenceId,
            ReadbackRequired: readbackRequired,
            ReadbackStatus: readbackRequired
                ? FiscalExceptionReadbackStatus.PendingFutureSlice
                : FiscalExceptionReadbackStatus.NotRequired,
            KnownPosServerFiscalDocumentId: detail.PosServerFiscalDocumentId,
            SitePosServerId: summary.SitePosServerId,
            SitePosServerRef: summary.SitePosServerRef,
            UpstreamFinalityReference: summary.UpstreamFinalityReference,
            PayableBasisRef: summary.PayableBasisRef,
            RetryEligibilityStatus: summary.RetryEligibilityStatus,
            RetryExecutionAvailable: false,
            PosServerReadbackCallPerformed: false,
            PreparationStatus: readbackRequired
                ? "readback_contract_prepared_no_pos_server_call"
                : "readback_not_required_for_current_case_state");
    }

    internal static bool IsFiscalExceptionCandidate(FiscalIssuanceReferenceRecord record) =>
        record.FiscalIssuanceState is FiscalIssuanceIntegrationState.FiscalIssuanceConflict
            or FiscalIssuanceIntegrationState.FiscalIssuanceFailedRequest
            or FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration
            or FiscalIssuanceIntegrationState.FiscalIssuanceFailedService
            or FiscalIssuanceIntegrationState.FiscalIssuanceUnknown
            or FiscalIssuanceIntegrationState.FiscalIssuanceManualReview
            or FiscalIssuanceIntegrationState.FiscalIssuanceExceptionReleased
            or FiscalIssuanceIntegrationState.FiscalIssuanceReconciled;

    internal static FiscalExceptionQueueCaseSummary ToSummary(FiscalIssuanceReferenceRecord record)
    {
        var readbackStatus = ResolveReadbackStatus(record);
        var readbackClassification = ResolveReadbackClassification(record);
        var retryEligibilityStatus = ResolveRetryEligibility(record, readbackStatus);
        var semanticHashReadiness = FiscalExceptionSemanticHashReadinessPolicy.Evaluate(record);
        var semanticHashRecalculationPreview =
            FiscalExceptionSemanticHashRecalculationPreviewService.PreviewWithoutOriginalFacts(record);

        return new FiscalExceptionQueueCaseSummary(
            CaseId: record.FiscalIssuanceReferenceId,
            FiscalIssuanceReferenceId: record.FiscalIssuanceReferenceId,
            PaymentConfirmationId: record.PaymentConfirmationId,
            PaymentAttemptId: record.PaymentAttemptId,
            ParkingSessionId: record.ParkingSessionId,
            SiteId: record.SiteId,
            SitePosServerId: record.SitePosServerId,
            SitePosServerRef: record.SitePosServerRef,
            UpstreamFinalityReference: record.UpstreamFinalityReference,
            PayableBasisRef: record.PayableBasisRef,
            Category: ResolveCategory(record),
            QueueState: ResolveQueueState(record),
            FiscalIssuanceState: record.FiscalIssuanceState,
            LatestExceptionReason: record.LatestExceptionReason,
            SafeErrorSummary: SafeErrorSummary(record),
            ReadbackStatus: readbackStatus,
            ReadbackClassification: readbackClassification,
            LastReadbackAttemptAt: readbackClassification is null ? null : record.LastUpdatedAt,
            ReadbackAttemptCount: null,
            LastReadbackSafeSummary: null,
            RetryEligibilityStatus: retryEligibilityStatus,
            RetryEligibilityDecision: ToRetryEligibilityDecision(retryEligibilityStatus),
            RetryBlockReasonCode: ToRetryBlockReasonCode(retryEligibilityStatus),
            SafeRetryEligibilitySummary: ToSafeRetryEligibilitySummary(retryEligibilityStatus),
            RetryEligibilityEvaluatedAt: null,
            RetryEligibilityBasedOnReadbackClassification: readbackClassification,
            RetryExecutionAvailable: false,
            RetryCommandPreparationStatus: FiscalExceptionRetryCommandPreparationStatus.NotPrepared,
            RetryCommandBlockReasonCode: "retry_command_not_prepared",
            SafeRetryCommandPreparationSummary: "retry_command_not_prepared_read_detail_for_evaluation",
            SemanticRequestHashAvailabilityStatus:
                FiscalExceptionSemanticHashReadinessPolicy.ToAvailabilityStatus(semanticHashReadiness.Status),
            SemanticRequestHashValue: record.SemanticRequestHashValue,
            SemanticRequestHashAlgorithm: record.SemanticRequestHashAlgorithm,
            SemanticRequestHashSourceVersion: record.SemanticRequestHashSourceVersion,
            SemanticRequestHashSourceFactCount: record.SemanticRequestHashSourceFactCount,
            SafeSemanticRequestHashSourceSummary: record.SemanticRequestHashSafeSummary,
            SemanticHashReadinessStatus: semanticHashReadiness.Status,
            SemanticHashReadinessBlockReasonCode: semanticHashReadiness.BlockReasonCode,
            StoredSemanticHashSourceVersion: semanticHashReadiness.StoredSourceVersion,
            RequiredSemanticHashSourceVersion: semanticHashReadiness.RequiredSourceVersion,
            SemanticHashRecalculationPosture: semanticHashReadiness.RecalculationPosture,
            SafeSemanticHashReadinessSummary: semanticHashReadiness.SafeSummary,
            SemanticHashRecalculationPreviewStatus: semanticHashRecalculationPreview.Status,
            SemanticHashRecalculationPreviewBlockReasonCode: semanticHashRecalculationPreview.BlockReasonCode,
            SemanticHashRecalculationPreviewStoredSourceVersion: semanticHashRecalculationPreview.StoredSourceVersion,
            SemanticHashRecalculationPreviewRequiredSourceVersion: semanticHashRecalculationPreview.RequiredSourceVersion,
            SemanticHashRecalculationPreviewAttemptedAt: semanticHashRecalculationPreview.PreviewAttemptedAt,
            SafeSemanticHashRecalculationPreviewSummary: semanticHashRecalculationPreview.SafeSummary,
            SemanticHashRecalculationMutationStatus: semanticHashRecalculationPreview.MutationStatus,
            IdempotencyContextAvailabilityStatus: ToIdempotencyContextAvailability(record.UpstreamFinalityReference),
            LastRetryCommandPreparationAttemptAt: null,
            RetryCommandPreparationAttemptCount: null,
            RetrySchedulingPreparationStatus: FiscalExceptionRetrySchedulingPreparationStatus.NotPrepared,
            RetrySchedulingBlockReasonCode: "retry_scheduling_not_prepared",
            SafeRetrySchedulingPreparationSummary: "retry_scheduling_not_prepared_read_detail_for_evaluation",
            LastRetrySchedulingPreparationAttemptAt: null,
            RetrySchedulingPreparationAttemptCount: null,
            RetryExecutionPreparationStatus: FiscalExceptionRetryExecutionPreparationStatus.NotPrepared,
            RetryExecutionBlockReasonCode: "retry_execution_preparation_not_prepared",
            SafeRetryExecutionPreparationSummary: "retry_execution_preparation_not_prepared_read_detail_for_evaluation",
            RetryExecutionDualControlRequired: false,
            RetryExecutionAuthorizationStatus: FiscalExceptionRetryExecutionAuthorizationStatus.NotEvaluated,
            RetryExecutionPosServerReadinessStatus: FiscalExceptionRetryExecutionPosServerReadinessStatus.NotEvaluated,
            PosServerRetryContractReadinessStatus: FiscalExceptionPosServerRetryContractReadinessStatus.NotEvaluated,
            PosServerSemanticHashCompatibilityStatus: FiscalExceptionPosServerRetryContractReadinessStatus.NotEvaluated,
            PosServerIdempotencyMappingStatus: FiscalExceptionPosServerRetryContractReadinessStatus.NotEvaluated,
            PosServerReadbackFieldCompatibilityStatus: FiscalExceptionPosServerRetryContractReadinessStatus.NotEvaluated,
            PosServerFiscalNumberingReadinessStatus: FiscalExceptionPosServerRetryContractReadinessStatus.NotEvaluated,
            PosServerConflictReplayBehaviorStatus: FiscalExceptionPosServerRetryContractReadinessStatus.NotEvaluated,
            PosServerRetryContractBlockReasonCode: "pos_server_retry_contract_readiness_not_evaluated",
            SafePosServerRetryContractReadinessSummary: "pos_server_retry_contract_readiness_read_detail_for_evaluation",
            DuplicateCollapseKey: $"fiscal-reference:{record.FiscalIssuanceReferenceId:N}",
            DuplicateCollapseStrategy: DuplicateCollapseStrategy,
            FirstDetectedAt: record.FirstRecordedAt,
            LastUpdatedAt: record.LastUpdatedAt);
    }

    internal static FiscalExceptionQueueCaseDetail ToDetail(FiscalIssuanceReferenceRecord record) =>
        new(
            Summary: ToSummary(record),
            PosServerFiscalDocumentId: record.PosServerFiscalDocumentId,
            FiscalDocumentNumber: record.FiscalDocumentNumber,
            FiscalIdentityId: record.FiscalIdentityId,
            FiscalSequencePolicyId: record.FiscalSequencePolicyId,
            FiscalSequenceValue: record.FiscalSequenceValue,
            FiscalDocumentStatusCodeId: record.FiscalDocumentStatusCodeId,
            ResultClassification: record.ResultClassification,
            FiscalIssuanceEvidenceStatus: record.FiscalIssuanceEvidenceStatus,
            FiscalNumberAssignmentState: record.FiscalNumberAssignmentState,
            LatestErrorPosture: record.LatestErrorPosture,
            CorrelationId: record.CorrelationId,
            PosServerResponseTimestamp: record.PosServerResponseTimestamp,
            PaymentFinalityChanged: false,
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false,
            FiscalNumberEditingAllowed: false,
            ManualFiscalDocumentCreationAllowed: false);

    private static FiscalExceptionQueueQuery NormalizeQuery(FiscalExceptionQueueQuery query) =>
        query with
        {
            Limit = query.Limit switch
            {
                < 1 => 100,
                > 500 => 500,
                _ => query.Limit
            }
        };

    private static FiscalExceptionQueueCategory ResolveCategory(FiscalIssuanceReferenceRecord record) =>
        record.LatestExceptionReason switch
        {
            FiscalIssuanceExceptionReason.PostTimeout => FiscalExceptionQueueCategory.PosServerTimeout,
            FiscalIssuanceExceptionReason.NetworkDisconnectAfterPossibleCommit => FiscalExceptionQueueCategory.UnknownOutcome,
            FiscalIssuanceExceptionReason.GetReadbackInconclusive => FiscalExceptionQueueCategory.UnknownOutcome,
            FiscalIssuanceExceptionReason.GetReadbackNotFound => FiscalExceptionQueueCategory.UnknownOutcome,
            FiscalIssuanceExceptionReason.GetReadbackServiceFailed => FiscalExceptionQueueCategory.PosServerUnavailable,
            FiscalIssuanceExceptionReason.CentralPmsReferencePersistenceFailed => FiscalExceptionQueueCategory.PosServerAcceptedCentralPmsRecordingFailed,
            FiscalIssuanceExceptionReason.PersistenceWriteFailed => FiscalExceptionQueueCategory.PosServerAcceptedCentralPmsRecordingFailed,
            FiscalIssuanceExceptionReason.FiscalDocumentIdempotencyConflict => FiscalExceptionQueueCategory.IdempotencyConflict,
            FiscalIssuanceExceptionReason.ReplayMismatch => FiscalExceptionQueueCategory.SemanticRequestHashMismatch,
            FiscalIssuanceExceptionReason.FiscalReferenceMismatch => FiscalExceptionQueueCategory.FiscalMismatch,
            FiscalIssuanceExceptionReason.ManualReviewRequired => FiscalExceptionQueueCategory.ManualReviewRequired,
            FiscalIssuanceExceptionReason.FiscalIdentityNotFound
                or FiscalIssuanceExceptionReason.FiscalIdentityAmbiguous
                or FiscalIssuanceExceptionReason.FiscalIdentityNotEffective
                or FiscalIssuanceExceptionReason.FiscalSequencePolicyNotFound
                or FiscalIssuanceExceptionReason.FiscalSequencePolicyAmbiguous
                or FiscalIssuanceExceptionReason.FiscalSequencePolicyNotEffective
                or FiscalIssuanceExceptionReason.FiscalSequenceStateNotFound
                or FiscalIssuanceExceptionReason.FiscalSequenceStateNotEffective
                or FiscalIssuanceExceptionReason.FiscalNumberAllocationFailed
                or FiscalIssuanceExceptionReason.FiscalDocumentNumberFormatFailed =>
                FiscalExceptionQueueCategory.FiscalConfigurationMissing,
            FiscalIssuanceExceptionReason.RequestConstructionError
                or FiscalIssuanceExceptionReason.MissingPayableBasis
                or FiscalIssuanceExceptionReason.MissingUpstreamFinalityReference
                or FiscalIssuanceExceptionReason.UnsupportedFiscalDocumentRequest
                or FiscalIssuanceExceptionReason.InvalidFiscalTender
                or FiscalIssuanceExceptionReason.MissingFiscalTender
                or FiscalIssuanceExceptionReason.InvalidFiscalTaxDetail
                or FiscalIssuanceExceptionReason.InvalidFiscalDiscountPrivilegeDetail
                or FiscalIssuanceExceptionReason.InvalidFiscalTotal
                or FiscalIssuanceExceptionReason.SensitivePayloadRejected =>
                FiscalExceptionQueueCategory.CentralPmsMappingFailure,
            _ => record.FiscalIssuanceState switch
            {
                FiscalIssuanceIntegrationState.FiscalIssuanceUnknown => FiscalExceptionQueueCategory.UnknownOutcome,
                FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration => FiscalExceptionQueueCategory.FiscalConfigurationMissing,
                FiscalIssuanceIntegrationState.FiscalIssuanceConflict => FiscalExceptionQueueCategory.IdempotencyConflict,
                FiscalIssuanceIntegrationState.FiscalIssuanceFailedService => FiscalExceptionQueueCategory.PosServerUnavailable,
                FiscalIssuanceIntegrationState.FiscalIssuanceFailedRequest => FiscalExceptionQueueCategory.CentralPmsMappingFailure,
                FiscalIssuanceIntegrationState.FiscalIssuanceManualReview => FiscalExceptionQueueCategory.ManualReviewRequired,
                _ => FiscalExceptionQueueCategory.OtherFiscalFailure
            }
        };

    private static FiscalExceptionQueueState ResolveQueueState(FiscalIssuanceReferenceRecord record) =>
        record.FiscalIssuanceState switch
        {
            FiscalIssuanceIntegrationState.FiscalIssuanceUnknown => FiscalExceptionQueueState.ReadbackRequired,
            FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration => FiscalExceptionQueueState.BlockedRequiresConfigFix,
            FiscalIssuanceIntegrationState.FiscalIssuanceConflict => FiscalExceptionQueueState.MismatchReview,
            FiscalIssuanceIntegrationState.FiscalIssuanceManualReview => FiscalExceptionQueueState.ManualReviewRequired,
            FiscalIssuanceIntegrationState.FiscalIssuanceExceptionReleased => FiscalExceptionQueueState.ManualReviewRequired,
            FiscalIssuanceIntegrationState.FiscalIssuanceReconciled => FiscalExceptionQueueState.Reconciled,
            _ => FiscalExceptionQueueState.Queued
        };

    private static FiscalExceptionReadbackStatus ResolveReadbackStatus(FiscalIssuanceReferenceRecord record) =>
        ResolveReadbackClassification(record) is not null
            ? FiscalExceptionReadbackStatus.Attempted
            : record.FiscalIssuanceState == FiscalIssuanceIntegrationState.FiscalIssuanceUnknown ||
            record.LatestExceptionReason is FiscalIssuanceExceptionReason.PostTimeout
                or FiscalIssuanceExceptionReason.NetworkDisconnectAfterPossibleCommit
                or FiscalIssuanceExceptionReason.FiscalNumberAssignmentIncomplete
                or FiscalIssuanceExceptionReason.CentralPmsReferencePersistenceFailed
            ? FiscalExceptionReadbackStatus.RequiredNotStarted
            : FiscalExceptionReadbackStatus.NotRequired;

    private static FiscalExceptionReadbackClassification? ResolveReadbackClassification(
        FiscalIssuanceReferenceRecord record) =>
        record.LatestExceptionReason switch
        {
            FiscalIssuanceExceptionReason.GetReadbackNotFound => FiscalExceptionReadbackClassification.NotFound,
            FiscalIssuanceExceptionReason.GetReadbackServiceFailed => FiscalExceptionReadbackClassification.Failed,
            FiscalIssuanceExceptionReason.GetReadbackInconclusive => FiscalExceptionReadbackClassification.Unknown,
            FiscalIssuanceExceptionReason.FiscalReferenceMismatch => FiscalExceptionReadbackClassification.Mismatch,
            _ => null
        };

    private static FiscalExceptionRetryEligibilityStatus ResolveRetryEligibility(
        FiscalIssuanceReferenceRecord record,
        FiscalExceptionReadbackStatus readbackStatus)
    {
        if (record.FiscalIssuanceState == FiscalIssuanceIntegrationState.FiscalIssuanceReconciled)
        {
            return FiscalExceptionRetryEligibilityStatus.NotRequiredRecorded;
        }

        if (ResolveQueueState(record) is FiscalExceptionQueueState.MismatchReview
            or FiscalExceptionQueueState.ManualReviewRequired)
        {
            return FiscalExceptionRetryEligibilityStatus.BlockedManualReview;
        }

        if (ResolveQueueState(record) == FiscalExceptionQueueState.BlockedRequiresConfigFix)
        {
            return FiscalExceptionRetryEligibilityStatus.BlockedConfiguration;
        }

        if (readbackStatus != FiscalExceptionReadbackStatus.NotRequired)
        {
            return FiscalExceptionRetryEligibilityStatus.BlockedPendingReadback;
        }

        return FiscalExceptionRetryEligibilityStatus.UnavailableInThisSlice;
    }

    private static string? SafeErrorSummary(FiscalIssuanceReferenceRecord record)
    {
        if (record.LatestExceptionReason is null && string.IsNullOrWhiteSpace(record.LatestErrorCode))
        {
            return null;
        }

        var reason = record.LatestExceptionReason?.ToString() ?? "FiscalIssuanceException";
        return string.IsNullOrWhiteSpace(record.LatestErrorCode)
            ? reason
            : $"{reason}:{record.LatestErrorCode}";
    }
    private static FiscalExceptionRetryEligibilityDecision ToRetryEligibilityDecision(
        FiscalExceptionRetryEligibilityStatus status) =>
        status switch
        {
            FiscalExceptionRetryEligibilityStatus.EligibleForControlledRetryPlanning =>
                FiscalExceptionRetryEligibilityDecision.Eligible,
            FiscalExceptionRetryEligibilityStatus.NotRequiredRecorded =>
                FiscalExceptionRetryEligibilityDecision.NotRequired,
            FiscalExceptionRetryEligibilityStatus.UnavailableInThisSlice or
                FiscalExceptionRetryEligibilityStatus.UnavailablePolicyNotConfigured =>
                FiscalExceptionRetryEligibilityDecision.Unavailable,
            _ => FiscalExceptionRetryEligibilityDecision.Blocked
        };

    private static string? ToRetryBlockReasonCode(FiscalExceptionRetryEligibilityStatus status) =>
        status switch
        {
            FiscalExceptionRetryEligibilityStatus.EligibleForControlledRetryPlanning => null,
            FiscalExceptionRetryEligibilityStatus.BlockedPendingReadback => "readback_attempt_history_missing",
            FiscalExceptionRetryEligibilityStatus.BlockedManualReview => "manual_review_required",
            FiscalExceptionRetryEligibilityStatus.BlockedConfiguration => "fiscal_configuration_invalid_or_missing",
            FiscalExceptionRetryEligibilityStatus.NotRequiredRecorded => "not_required_recorded",
            FiscalExceptionRetryEligibilityStatus.UnavailableInThisSlice => "retry_execution_unavailable_in_this_slice",
            FiscalExceptionRetryEligibilityStatus.BlockedReadbackMatched => "readback_matched",
            FiscalExceptionRetryEligibilityStatus.BlockedReadbackMismatch => "readback_mismatch",
            FiscalExceptionRetryEligibilityStatus.BlockedReadbackFailed => "readback_failed_or_unknown",
            FiscalExceptionRetryEligibilityStatus.BlockedIdentifierMissing => "readback_identifier_missing",
            FiscalExceptionRetryEligibilityStatus.BlockedReadbackUnsupported => "readback_not_supported_yet",
            FiscalExceptionRetryEligibilityStatus.BlockedMissingRequestContext => "original_request_context_missing",
            FiscalExceptionRetryEligibilityStatus.BlockedMissingUpstreamFinalityReference => "upstream_finality_reference_missing",
            FiscalExceptionRetryEligibilityStatus.BlockedSemanticHashNotReady => "semantic_hash_not_ready",
            FiscalExceptionRetryEligibilityStatus.UnavailablePolicyNotConfigured => "unavailable_policy_not_configured",
            _ => "retry_eligibility_not_evaluated"
        };

    private static string ToSafeRetryEligibilitySummary(FiscalExceptionRetryEligibilityStatus status) =>
        status switch
        {
            FiscalExceptionRetryEligibilityStatus.EligibleForControlledRetryPlanning =>
                "retry_eligible_for_controlled_retry_planning_no_execution",
            FiscalExceptionRetryEligibilityStatus.NotRequiredRecorded =>
                "retry_not_required_recorded_or_reconciled",
            FiscalExceptionRetryEligibilityStatus.BlockedManualReview =>
                "retry_blocked_manual_review_required",
            FiscalExceptionRetryEligibilityStatus.BlockedConfiguration =>
                "retry_blocked_fiscal_configuration_invalid_or_missing",
            FiscalExceptionRetryEligibilityStatus.UnavailableInThisSlice =>
                "retry_unavailable_in_this_slice",
            FiscalExceptionRetryEligibilityStatus.BlockedSemanticHashNotReady =>
                "retry_blocked_semantic_hash_not_ready",
            _ => "retry_blocked_until_evaluated_by_feq_retry_eligibility_evaluator"
        };

    private static FiscalExceptionIdempotencyContextAvailabilityStatus ToIdempotencyContextAvailability(
        string? upstreamFinalityReference) =>
        string.IsNullOrWhiteSpace(upstreamFinalityReference)
            ? FiscalExceptionIdempotencyContextAvailabilityStatus.MissingUpstreamFinalityReference
            : FiscalExceptionIdempotencyContextAvailabilityStatus.Available;

}
