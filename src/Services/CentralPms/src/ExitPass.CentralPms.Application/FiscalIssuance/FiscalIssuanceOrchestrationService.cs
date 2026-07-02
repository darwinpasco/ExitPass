using ExitPass.CentralPms.Domain.FiscalIssuance;

namespace ExitPass.CentralPms.Application.FiscalIssuance;

public interface IFiscalIssuanceOrchestrationService
{
    Task<FiscalIssuanceReferenceRecord> PreparePendingAsync(
        PrepareFiscalIssuanceCommand command,
        CancellationToken cancellationToken);

    Task<FiscalIssuanceReferenceRecord> MarkRequestedAsync(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceTransitionContext context,
        CancellationToken cancellationToken);

    Task<FiscalIssuanceReferenceRecord> MarkFailedRequestAsync(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceFailureTransitionContext context,
        CancellationToken cancellationToken);

    Task<FiscalIssuanceReferenceRecord> MarkFailedConfigurationAsync(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceFailureTransitionContext context,
        CancellationToken cancellationToken);

    Task<FiscalIssuanceReferenceRecord> MarkFailedServiceAsync(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceFailureTransitionContext context,
        CancellationToken cancellationToken);

    Task<FiscalIssuanceReferenceRecord> MarkUnknownAsync(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceFailureTransitionContext context,
        CancellationToken cancellationToken);

    Task<FiscalIssuanceReferenceRecord> MarkConflictAsync(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceFailureTransitionContext context,
        CancellationToken cancellationToken);

    Task<FiscalIssuanceReferenceRecord> MarkManualReviewRequiredAsync(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceFailureTransitionContext context,
        CancellationToken cancellationToken);

    Task<FiscalIssuanceReferenceRecord> MarkRecordedAsync(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceEvidenceInput evidence,
        CancellationToken cancellationToken);

    Task<FiscalIssuanceReferenceRecord> MarkReplayedAsync(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceEvidenceInput evidence,
        CancellationToken cancellationToken);

    Task<FiscalIssuanceReferenceRecord> ApplyPosServerCreateResultAsync(
        Guid fiscalIssuanceReferenceId,
        PosServerFiscalDocumentCreateResult result,
        PosServerCreateResultRecordingContext context,
        CancellationToken cancellationToken);

    Task<FiscalIssuanceReferenceRecord> ApplyPosServerFailureResultAsync(
        Guid fiscalIssuanceReferenceId,
        PosServerFiscalDocumentCreateResult result,
        PosServerCreateResultRecordingContext context,
        CancellationToken cancellationToken);

    Task<FiscalIssuanceReferenceRecord> MarkUnknownOutcomeAsync(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceUnknownOutcomeContext context,
        CancellationToken cancellationToken);

    Task<FiscalIssuanceReferenceRecord> MarkReadbackRequestedAsync(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceReadbackPlanningContext context,
        CancellationToken cancellationToken);

    Task<FiscalIssuanceReferenceRecord> ApplyReadbackPlanningResultAsync(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceReadbackPlanningResult result,
        CancellationToken cancellationToken);
}

public sealed class FiscalIssuanceOrchestrationService : IFiscalIssuanceOrchestrationService
{
    private readonly IFiscalIssuanceReferenceRepository _repository;

    public FiscalIssuanceOrchestrationService(IFiscalIssuanceReferenceRepository repository)
    {
        _repository = repository;
    }

    public Task<FiscalIssuanceReferenceRecord> PreparePendingAsync(
        PrepareFiscalIssuanceCommand command,
        CancellationToken cancellationToken)
    {
        var validationErrors = command.Validate();
        if (validationErrors.Count > 0)
        {
            throw new ArgumentException(
                $"Fiscal issuance orchestration command is invalid: {string.Join(", ", validationErrors)}",
                nameof(command));
        }

        return _repository.CreateAsync(command.ToCreateRequest(), cancellationToken);
    }

    public Task<FiscalIssuanceReferenceRecord> MarkRequestedAsync(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceTransitionContext context,
        CancellationToken cancellationToken) =>
        TransitionAsync(
            fiscalIssuanceReferenceId,
            new FiscalIssuanceStateTransitionRequest(
                FiscalIssuanceState: FiscalIssuanceIntegrationState.FiscalIssuanceRequested,
                PosServerFiscalDocumentId: null,
                FiscalIdentityId: null,
                FiscalSequencePolicyId: null,
                FiscalSequenceValue: null,
                FiscalDocumentNumber: null,
                FiscalSeries: null,
                FiscalNumberPrefixText: null,
                FiscalNumberSuffixText: null,
                FiscalNumberAssignedAt: null,
                FiscalNumberAssignedByRef: null,
                FiscalDocumentStatusCodeId: null,
                ResultClassification: null,
                FiscalIssuanceEvidenceStatus: null,
                FiscalNumberAssignmentState: FiscalNumberAssignmentState.NotAssigned,
                LatestExceptionReason: null,
                LatestErrorCode: null,
                LatestErrorPosture: null,
                CorrelationId: context.CorrelationId,
                PosServerResponseTimestamp: null,
                UpdatedByServiceIdentityId: context.ServiceIdentityId),
            cancellationToken);

    public Task<FiscalIssuanceReferenceRecord> MarkFailedRequestAsync(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceFailureTransitionContext context,
        CancellationToken cancellationToken) =>
        MarkFailureAsync(
            fiscalIssuanceReferenceId,
            FiscalIssuanceIntegrationState.FiscalIssuanceFailedRequest,
            context,
            cancellationToken);

    public Task<FiscalIssuanceReferenceRecord> MarkFailedConfigurationAsync(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceFailureTransitionContext context,
        CancellationToken cancellationToken) =>
        MarkFailureAsync(
            fiscalIssuanceReferenceId,
            FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration,
            context,
            cancellationToken);

    public Task<FiscalIssuanceReferenceRecord> MarkFailedServiceAsync(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceFailureTransitionContext context,
        CancellationToken cancellationToken) =>
        MarkFailureAsync(
            fiscalIssuanceReferenceId,
            FiscalIssuanceIntegrationState.FiscalIssuanceFailedService,
            context,
            cancellationToken);

    public Task<FiscalIssuanceReferenceRecord> MarkUnknownAsync(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceFailureTransitionContext context,
        CancellationToken cancellationToken) =>
        MarkFailureAsync(
            fiscalIssuanceReferenceId,
            FiscalIssuanceIntegrationState.FiscalIssuanceUnknown,
            context,
            cancellationToken);

    public Task<FiscalIssuanceReferenceRecord> MarkConflictAsync(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceFailureTransitionContext context,
        CancellationToken cancellationToken) =>
        MarkFailureAsync(
            fiscalIssuanceReferenceId,
            FiscalIssuanceIntegrationState.FiscalIssuanceConflict,
            context with
            {
                ExceptionReason = context.ExceptionReason ?? FiscalIssuanceExceptionReason.FiscalDocumentIdempotencyConflict,
                ErrorPosture = context.ErrorPosture ?? FiscalIssuanceErrorPosture.DoNotRetryWithoutRequestChange
            },
            cancellationToken);

    public Task<FiscalIssuanceReferenceRecord> MarkManualReviewRequiredAsync(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceFailureTransitionContext context,
        CancellationToken cancellationToken) =>
        MarkFailureAsync(
            fiscalIssuanceReferenceId,
            FiscalIssuanceIntegrationState.FiscalIssuanceManualReview,
            context,
            cancellationToken);

    public Task<FiscalIssuanceReferenceRecord> MarkRecordedAsync(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceEvidenceInput evidence,
        CancellationToken cancellationToken) =>
        MarkEvidenceAsync(
            fiscalIssuanceReferenceId,
            FiscalIssuanceIntegrationState.FiscalIssuanceRecorded,
            FiscalIssuanceResultClassification.NewlyCreated,
            evidence,
            cancellationToken);

    public Task<FiscalIssuanceReferenceRecord> MarkReplayedAsync(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceEvidenceInput evidence,
        CancellationToken cancellationToken) =>
        MarkEvidenceAsync(
            fiscalIssuanceReferenceId,
            FiscalIssuanceIntegrationState.FiscalIssuanceReplayed,
            FiscalIssuanceResultClassification.IdempotentReplay,
            evidence,
            cancellationToken);

    public async Task<FiscalIssuanceReferenceRecord> ApplyPosServerCreateResultAsync(
        Guid fiscalIssuanceReferenceId,
        PosServerFiscalDocumentCreateResult result,
        PosServerCreateResultRecordingContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(context);

        if (fiscalIssuanceReferenceId == Guid.Empty)
        {
            throw new ArgumentException("Fiscal issuance reference id is required.", nameof(fiscalIssuanceReferenceId));
        }

        if (string.IsNullOrWhiteSpace(context.UpstreamFinalityReference))
        {
            throw new ArgumentException("Upstream finality reference is required.", nameof(context));
        }

        if (result.Outcome != PosServerFiscalDocumentOutcome.Accepted || !result.Succeeded)
        {
            throw new ArgumentException("Only accepted POS Server create results are handled by this success/replay slice.", nameof(result));
        }

        var evidenceErrors = ValidateCompletePosServerEvidence(result);
        if (evidenceErrors.Count > 0)
        {
            throw new ArgumentException(
                $"POS Server fiscal issuance evidence is incomplete: {string.Join(", ", evidenceErrors)}",
                nameof(result));
        }

        var scopedReference = await _repository.FindByUpstreamFinalityReferenceAsync(
            context.UpstreamFinalityReference,
            context.SitePosServerId,
            context.FiscalDocumentTypeCodeId,
            cancellationToken);

        if (scopedReference is not null &&
            scopedReference.FiscalIssuanceReferenceId != fiscalIssuanceReferenceId)
        {
            throw new InvalidOperationException("duplicate_active_fiscal_reference_detected");
        }

        if (scopedReference is not null &&
            HasRecordedFiscalEvidence(scopedReference) &&
            !MatchesRecordedEvidence(scopedReference, result))
        {
            return await MarkManualReviewPreservingExistingEvidenceAsync(
                fiscalIssuanceReferenceId,
                scopedReference,
                context,
                cancellationToken);
        }

        var evidence = new FiscalIssuanceEvidenceInput(
            PosServerFiscalDocumentId: result.FiscalDocumentId,
            FiscalIdentityId: result.FiscalIdentityId,
            FiscalSequencePolicyId: result.FiscalSequencePolicyId,
            FiscalSequenceValue: result.FiscalSequenceValue,
            FiscalDocumentNumber: result.FiscalDocumentNumber,
            FiscalSeries: result.FiscalSeries,
            FiscalNumberPrefixText: result.FiscalNumberPrefixText,
            FiscalNumberSuffixText: result.FiscalNumberSuffixText,
            FiscalNumberAssignedAt: result.FiscalNumberAssignedAt,
            FiscalNumberAssignedByRef: result.FiscalNumberAssignedByRef,
            FiscalDocumentStatusCodeId: result.FiscalDocumentStatusCodeId,
            CorrelationId: context.CorrelationId,
            PosServerResponseTimestamp: context.PosServerResponseTimestamp,
            ServiceIdentityId: context.ServiceIdentityId);

        return result.ResultClassification switch
        {
            FiscalIssuanceResultClassification.NewlyCreated =>
                await MarkRecordedAsync(fiscalIssuanceReferenceId, evidence, cancellationToken),
            FiscalIssuanceResultClassification.IdempotentReplay =>
                await MarkReplayedAsync(fiscalIssuanceReferenceId, evidence, cancellationToken),
            _ => throw new ArgumentException("POS Server result classification is not supported by this slice.", nameof(result))
        };
    }

    public Task<FiscalIssuanceReferenceRecord> ApplyPosServerFailureResultAsync(
        Guid fiscalIssuanceReferenceId,
        PosServerFiscalDocumentCreateResult result,
        PosServerCreateResultRecordingContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(context);

        if (fiscalIssuanceReferenceId == Guid.Empty)
        {
            throw new ArgumentException("Fiscal issuance reference id is required.", nameof(fiscalIssuanceReferenceId));
        }

        if (string.IsNullOrWhiteSpace(context.UpstreamFinalityReference))
        {
            throw new ArgumentException("Upstream finality reference is required.", nameof(context));
        }

        if (result.Outcome == PosServerFiscalDocumentOutcome.Accepted && result.Succeeded)
        {
            throw new ArgumentException(
                "Accepted POS Server create results must be handled by ApplyPosServerCreateResultAsync.",
                nameof(result));
        }

        var normalizedCode = NormalizeFailureCode(result.Code);

        if (IsFiscalDocumentIdempotencyConflict(result, normalizedCode))
        {
            return MarkConflictAsync(
                fiscalIssuanceReferenceId,
                FailureContextFromPosServerResult(
                    result,
                    context,
                    FiscalIssuanceExceptionReason.FiscalDocumentIdempotencyConflict,
                    FiscalIssuanceErrorPosture.DoNotRetryWithoutRequestChange,
                    normalizedCode),
                cancellationToken);
        }

        if (IsFiscalNumberAssignmentIncomplete(normalizedCode))
        {
            var transitionContext = FailureContextFromPosServerResult(
                result,
                context,
                FiscalIssuanceExceptionReason.FiscalNumberAssignmentIncomplete,
                FiscalIssuanceErrorPosture.RetryAfterServiceRecovery,
                normalizedCode);

            return result.FiscalDocumentId is null || result.FiscalDocumentId == Guid.Empty
                ? MarkFailedServiceAsync(fiscalIssuanceReferenceId, transitionContext, cancellationToken)
                : MarkUnknownAsync(fiscalIssuanceReferenceId, transitionContext, cancellationToken);
        }

        var exceptionReason = MapExceptionReason(normalizedCode);

        if (ShouldMapToFailedConfiguration(result, exceptionReason))
        {
            return MarkFailedConfigurationAsync(
                fiscalIssuanceReferenceId,
                FailureContextFromPosServerResult(
                    result,
                    context,
                    exceptionReason ?? FiscalIssuanceExceptionReason.FiscalIdentityNotFound,
                    FiscalIssuanceErrorPosture.RetryAfterConfigurationCorrection,
                    normalizedCode),
                cancellationToken);
        }

        if (ShouldMapToFailedRequest(result, exceptionReason))
        {
            return MarkFailedRequestAsync(
                fiscalIssuanceReferenceId,
                FailureContextFromPosServerResult(
                    result,
                    context,
                    exceptionReason ?? FiscalIssuanceExceptionReason.RequestConstructionError,
                    FiscalIssuanceErrorPosture.DoNotRetryWithoutRequestChange,
                    normalizedCode),
                cancellationToken);
        }

        if (ShouldMapToFailedService(result, exceptionReason))
        {
            return MarkFailedServiceAsync(
                fiscalIssuanceReferenceId,
                FailureContextFromPosServerResult(
                    result,
                    context,
                    exceptionReason ?? FiscalIssuanceExceptionReason.PersistenceWriteFailed,
                    FiscalIssuanceErrorPosture.RetryAfterServiceRecovery,
                    normalizedCode),
                cancellationToken);
        }

        return MarkManualReviewRequiredAsync(
            fiscalIssuanceReferenceId,
            FailureContextFromPosServerResult(
                result,
                context,
                exceptionReason ?? FiscalIssuanceExceptionReason.ManualReviewRequired,
                FiscalIssuanceErrorPosture.DoNotRetryWithoutRequestChange,
                normalizedCode),
            cancellationToken);
    }

    public Task<FiscalIssuanceReferenceRecord> MarkUnknownOutcomeAsync(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceUnknownOutcomeContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ValidateUnknownOutcomeReason(context.ExceptionReason);

        return MarkUnknownAsync(
            fiscalIssuanceReferenceId,
            new FiscalIssuanceFailureTransitionContext(
                ExceptionReason: context.ExceptionReason,
                ErrorCode: string.IsNullOrWhiteSpace(context.ErrorCode)
                    ? ToErrorCode(context.ExceptionReason)
                    : context.ErrorCode,
                ErrorPosture: context.ErrorPosture ?? FiscalIssuanceErrorPosture.RetryAfterServiceRecovery,
                CorrelationId: context.CorrelationId,
                ServiceIdentityId: context.ServiceIdentityId),
            cancellationToken);
    }

    public Task<FiscalIssuanceReferenceRecord> MarkReadbackRequestedAsync(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceReadbackPlanningContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.KnownPosServerFiscalDocumentId is null || context.KnownPosServerFiscalDocumentId == Guid.Empty)
        {
            throw new ArgumentException("Known POS Server fiscal document id is required for readback planning.", nameof(context));
        }

        return MarkUnknownAsync(
            fiscalIssuanceReferenceId,
            new FiscalIssuanceFailureTransitionContext(
                ExceptionReason: context.ExceptionReason ?? FiscalIssuanceExceptionReason.GetReadbackInconclusive,
                ErrorCode: string.IsNullOrWhiteSpace(context.ErrorCode) ? "get_readback_requested" : context.ErrorCode,
                ErrorPosture: FiscalIssuanceErrorPosture.RetryAfterServiceRecovery,
                CorrelationId: context.CorrelationId,
                ServiceIdentityId: context.ServiceIdentityId),
            cancellationToken);
    }

    public Task<FiscalIssuanceReferenceRecord> ApplyReadbackPlanningResultAsync(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceReadbackPlanningResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Outcome switch
        {
            FiscalIssuanceReadbackPlanningOutcome.Requested => MarkReadbackRequestedAsync(
                fiscalIssuanceReferenceId,
                new FiscalIssuanceReadbackPlanningContext(
                    KnownPosServerFiscalDocumentId: result.KnownPosServerFiscalDocumentId,
                    ExceptionReason: result.ExceptionReason,
                    ErrorCode: result.ErrorCode,
                    CorrelationId: result.CorrelationId,
                    ServiceIdentityId: result.ServiceIdentityId),
                cancellationToken),
            FiscalIssuanceReadbackPlanningOutcome.Inconclusive => MarkUnknownOutcomeAsync(
                fiscalIssuanceReferenceId,
                UnknownOutcomeContextFromReadbackResult(
                    result,
                    FiscalIssuanceExceptionReason.GetReadbackInconclusive,
                    "get_readback_inconclusive"),
                cancellationToken),
            FiscalIssuanceReadbackPlanningOutcome.NotFound => MarkUnknownOutcomeAsync(
                fiscalIssuanceReferenceId,
                UnknownOutcomeContextFromReadbackResult(
                    result,
                    FiscalIssuanceExceptionReason.GetReadbackNotFound,
                    "get_readback_not_found"),
                cancellationToken),
            FiscalIssuanceReadbackPlanningOutcome.ServiceFailed => MarkUnknownOutcomeAsync(
                fiscalIssuanceReferenceId,
                UnknownOutcomeContextFromReadbackResult(
                    result,
                    FiscalIssuanceExceptionReason.GetReadbackServiceFailed,
                    "get_readback_service_failed"),
                cancellationToken),
            FiscalIssuanceReadbackPlanningOutcome.Mismatch => MarkManualReviewRequiredAsync(
                fiscalIssuanceReferenceId,
                new FiscalIssuanceFailureTransitionContext(
                    ExceptionReason: result.ExceptionReason ?? FiscalIssuanceExceptionReason.FiscalReferenceMismatch,
                    ErrorCode: string.IsNullOrWhiteSpace(result.ErrorCode) ? "fiscal_reference_mismatch" : result.ErrorCode,
                    ErrorPosture: FiscalIssuanceErrorPosture.DoNotRetryWithoutRequestChange,
                    CorrelationId: result.CorrelationId,
                    ServiceIdentityId: result.ServiceIdentityId),
                cancellationToken),
            _ => throw new ArgumentException("Readback planning outcome is not supported.", nameof(result))
        };
    }

    public static bool IsNormalExitAuthorizationGatingReady(FiscalIssuanceReferenceRecord record) =>
        record.FiscalIssuanceState is FiscalIssuanceIntegrationState.FiscalIssuanceRecorded
            or FiscalIssuanceIntegrationState.FiscalIssuanceReplayed
            or FiscalIssuanceIntegrationState.FiscalIssuanceReconciled
        && record.FiscalIssuanceEvidenceStatus == FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned
        && record.FiscalNumberAssignmentState == FiscalNumberAssignmentState.Assigned
        && record.PosServerFiscalDocumentId is not null
        && record.FiscalIdentityId is not null
        && record.FiscalSequencePolicyId is not null
        && record.FiscalSequenceValue is > 0
        && !string.IsNullOrWhiteSpace(record.FiscalDocumentNumber);

    private Task<FiscalIssuanceReferenceRecord> MarkFailureAsync(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceIntegrationState state,
        FiscalIssuanceFailureTransitionContext context,
        CancellationToken cancellationToken)
    {
        if (context.ExceptionReason is null)
        {
            throw new ArgumentException("Fiscal issuance exception reason is required.", nameof(context));
        }

        return TransitionAsync(
            fiscalIssuanceReferenceId,
            new FiscalIssuanceStateTransitionRequest(
                FiscalIssuanceState: state,
                PosServerFiscalDocumentId: null,
                FiscalIdentityId: null,
                FiscalSequencePolicyId: null,
                FiscalSequenceValue: null,
                FiscalDocumentNumber: null,
                FiscalSeries: null,
                FiscalNumberPrefixText: null,
                FiscalNumberSuffixText: null,
                FiscalNumberAssignedAt: null,
                FiscalNumberAssignedByRef: null,
                FiscalDocumentStatusCodeId: null,
                ResultClassification: null,
                FiscalIssuanceEvidenceStatus: null,
                FiscalNumberAssignmentState: FiscalNumberAssignmentState.NotAssigned,
                LatestExceptionReason: context.ExceptionReason,
                LatestErrorCode: context.ErrorCode,
                LatestErrorPosture: context.ErrorPosture,
                CorrelationId: context.CorrelationId,
                PosServerResponseTimestamp: null,
                UpdatedByServiceIdentityId: context.ServiceIdentityId),
            cancellationToken);
    }

    private Task<FiscalIssuanceReferenceRecord> MarkManualReviewPreservingExistingEvidenceAsync(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceReferenceRecord existing,
        PosServerCreateResultRecordingContext context,
        CancellationToken cancellationToken) =>
        TransitionAsync(
            fiscalIssuanceReferenceId,
            new FiscalIssuanceStateTransitionRequest(
                FiscalIssuanceState: FiscalIssuanceIntegrationState.FiscalIssuanceManualReview,
                PosServerFiscalDocumentId: existing.PosServerFiscalDocumentId,
                FiscalIdentityId: existing.FiscalIdentityId,
                FiscalSequencePolicyId: existing.FiscalSequencePolicyId,
                FiscalSequenceValue: existing.FiscalSequenceValue,
                FiscalDocumentNumber: existing.FiscalDocumentNumber,
                FiscalSeries: existing.FiscalSeries,
                FiscalNumberPrefixText: existing.FiscalNumberPrefixText,
                FiscalNumberSuffixText: existing.FiscalNumberSuffixText,
                FiscalNumberAssignedAt: existing.FiscalNumberAssignedAt,
                FiscalNumberAssignedByRef: existing.FiscalNumberAssignedByRef,
                FiscalDocumentStatusCodeId: existing.FiscalDocumentStatusCodeId,
                ResultClassification: existing.ResultClassification,
                FiscalIssuanceEvidenceStatus: existing.FiscalIssuanceEvidenceStatus,
                FiscalNumberAssignmentState: existing.FiscalNumberAssignmentState,
                LatestExceptionReason: FiscalIssuanceExceptionReason.ReplayMismatch,
                LatestErrorCode: "replay_mismatch",
                LatestErrorPosture: FiscalIssuanceErrorPosture.DoNotRetryWithoutRequestChange,
                CorrelationId: context.CorrelationId,
                PosServerResponseTimestamp: existing.PosServerResponseTimestamp,
                UpdatedByServiceIdentityId: context.ServiceIdentityId),
            cancellationToken);

    private Task<FiscalIssuanceReferenceRecord> MarkEvidenceAsync(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceIntegrationState state,
        FiscalIssuanceResultClassification resultClassification,
        FiscalIssuanceEvidenceInput evidence,
        CancellationToken cancellationToken) =>
        TransitionAsync(
            fiscalIssuanceReferenceId,
            new FiscalIssuanceStateTransitionRequest(
                FiscalIssuanceState: state,
                PosServerFiscalDocumentId: evidence.PosServerFiscalDocumentId,
                FiscalIdentityId: evidence.FiscalIdentityId,
                FiscalSequencePolicyId: evidence.FiscalSequencePolicyId,
                FiscalSequenceValue: evidence.FiscalSequenceValue,
                FiscalDocumentNumber: evidence.FiscalDocumentNumber,
                FiscalSeries: evidence.FiscalSeries,
                FiscalNumberPrefixText: evidence.FiscalNumberPrefixText,
                FiscalNumberSuffixText: evidence.FiscalNumberSuffixText,
                FiscalNumberAssignedAt: evidence.FiscalNumberAssignedAt,
                FiscalNumberAssignedByRef: evidence.FiscalNumberAssignedByRef,
                FiscalDocumentStatusCodeId: evidence.FiscalDocumentStatusCodeId,
                ResultClassification: resultClassification,
                FiscalIssuanceEvidenceStatus: FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned,
                FiscalNumberAssignmentState: FiscalNumberAssignmentState.Assigned,
                LatestExceptionReason: null,
                LatestErrorCode: null,
                LatestErrorPosture: null,
                CorrelationId: evidence.CorrelationId,
                PosServerResponseTimestamp: evidence.PosServerResponseTimestamp,
                UpdatedByServiceIdentityId: evidence.ServiceIdentityId),
            cancellationToken);

    private Task<FiscalIssuanceReferenceRecord> TransitionAsync(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceStateTransitionRequest request,
        CancellationToken cancellationToken)
    {
        if (fiscalIssuanceReferenceId == Guid.Empty)
        {
            throw new ArgumentException("Fiscal issuance reference id is required.", nameof(fiscalIssuanceReferenceId));
        }

        var validationErrors = request.Validate();
        if (validationErrors.Count > 0)
        {
            throw new ArgumentException(
                $"Fiscal issuance transition request is invalid: {string.Join(", ", validationErrors)}",
                nameof(request));
        }

        return _repository.UpdateStateAsync(fiscalIssuanceReferenceId, request, cancellationToken);
    }

    private static IReadOnlyList<string> ValidateCompletePosServerEvidence(PosServerFiscalDocumentCreateResult result)
    {
        var errors = new List<string>();

        if (result.FiscalDocumentId is null || result.FiscalDocumentId == Guid.Empty)
        {
            errors.Add("pos_server_fiscal_document_id_required");
        }

        if (result.FiscalIdentityId is null || result.FiscalIdentityId == Guid.Empty)
        {
            errors.Add("fiscal_identity_id_required");
        }

        if (result.FiscalSequencePolicyId is null || result.FiscalSequencePolicyId == Guid.Empty)
        {
            errors.Add("fiscal_sequence_policy_id_required");
        }

        if (result.FiscalSequenceValue is null or < 1)
        {
            errors.Add("fiscal_sequence_value_required");
        }

        if (string.IsNullOrWhiteSpace(result.FiscalDocumentNumber))
        {
            errors.Add("fiscal_document_number_required");
        }

        if (result.FiscalDocumentStatusCodeId is null || result.FiscalDocumentStatusCodeId == Guid.Empty)
        {
            errors.Add("fiscal_document_status_code_id_required");
        }

        if (result.FiscalIssuanceEvidenceStatus != FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned)
        {
            errors.Add("fiscal_issuance_evidence_status_required");
        }

        if (result.FiscalNumberAssignmentState != FiscalNumberAssignmentState.Assigned)
        {
            errors.Add("fiscal_number_assignment_state_assigned_required");
        }

        if (result.FiscalNumberAssignedAt is null)
        {
            errors.Add("fiscal_number_assigned_at_required");
        }

        if (string.IsNullOrWhiteSpace(result.FiscalNumberAssignedByRef))
        {
            errors.Add("fiscal_number_assigned_by_ref_required");
        }

        return errors;
    }

    private static bool HasRecordedFiscalEvidence(FiscalIssuanceReferenceRecord record) =>
        record.PosServerFiscalDocumentId is not null ||
        record.FiscalDocumentNumber is not null ||
        record.FiscalSequenceValue is not null ||
        record.FiscalIdentityId is not null ||
        record.FiscalSequencePolicyId is not null;

    private static bool MatchesRecordedEvidence(
        FiscalIssuanceReferenceRecord record,
        PosServerFiscalDocumentCreateResult result) =>
        NullableGuidMatches(record.PosServerFiscalDocumentId, result.FiscalDocumentId) &&
        NullableGuidMatches(record.FiscalIdentityId, result.FiscalIdentityId) &&
        NullableGuidMatches(record.FiscalSequencePolicyId, result.FiscalSequencePolicyId) &&
        NullableLongMatches(record.FiscalSequenceValue, result.FiscalSequenceValue) &&
        NullableStringMatches(record.FiscalDocumentNumber, result.FiscalDocumentNumber) &&
        NullableGuidMatches(record.FiscalDocumentStatusCodeId, result.FiscalDocumentStatusCodeId);

    private static bool NullableGuidMatches(Guid? recorded, Guid? incoming) =>
        recorded is null || incoming is null || recorded == incoming;

    private static bool NullableLongMatches(long? recorded, long? incoming) =>
        recorded is null || incoming is null || recorded == incoming;

    private static bool NullableStringMatches(string? recorded, string? incoming) =>
        string.IsNullOrWhiteSpace(recorded) ||
        string.IsNullOrWhiteSpace(incoming) ||
        string.Equals(recorded, incoming, StringComparison.Ordinal);

    private static FiscalIssuanceFailureTransitionContext FailureContextFromPosServerResult(
        PosServerFiscalDocumentCreateResult result,
        PosServerCreateResultRecordingContext context,
        FiscalIssuanceExceptionReason exceptionReason,
        FiscalIssuanceErrorPosture defaultErrorPosture,
        string normalizedCode) =>
        new(
            ExceptionReason: exceptionReason,
            ErrorCode: normalizedCode,
            ErrorPosture: result.ErrorPosture ?? defaultErrorPosture,
            CorrelationId: context.CorrelationId,
            ServiceIdentityId: context.ServiceIdentityId);

    private static string NormalizeFailureCode(string? code) =>
        string.IsNullOrWhiteSpace(code) ? "pos_server_failure" : code;

    private static bool IsFiscalDocumentIdempotencyConflict(
        PosServerFiscalDocumentCreateResult result,
        string normalizedCode) =>
        result.Outcome == PosServerFiscalDocumentOutcome.Conflict ||
        result.HttpStatusCode == 409 ||
        string.Equals(normalizedCode, "fiscal_document_idempotency_conflict", StringComparison.Ordinal);

    private static bool IsFiscalNumberAssignmentIncomplete(string normalizedCode) =>
        string.Equals(normalizedCode, "fiscal_number_assignment_incomplete", StringComparison.Ordinal);

    private static bool ShouldMapToFailedConfiguration(
        PosServerFiscalDocumentCreateResult result,
        FiscalIssuanceExceptionReason? exceptionReason) =>
        result.Outcome == PosServerFiscalDocumentOutcome.FailedConfiguration ||
        (result.HttpStatusCode != 503 &&
            result.ErrorPosture == FiscalIssuanceErrorPosture.RetryAfterConfigurationCorrection) ||
        (result.HttpStatusCode == 400 && IsConfigurationReason(exceptionReason));

    private static bool ShouldMapToFailedRequest(
        PosServerFiscalDocumentCreateResult result,
        FiscalIssuanceExceptionReason? exceptionReason) =>
        result.Outcome == PosServerFiscalDocumentOutcome.FailedRequest ||
        (result.HttpStatusCode == 400 && !IsConfigurationReason(exceptionReason)) ||
        (result.HttpStatusCode != 503 &&
            result.Outcome != PosServerFiscalDocumentOutcome.FailedService &&
            result.ErrorPosture == FiscalIssuanceErrorPosture.DoNotRetryWithoutRequestChange);

    private static bool ShouldMapToFailedService(
        PosServerFiscalDocumentCreateResult result,
        FiscalIssuanceExceptionReason? exceptionReason) =>
        result.Outcome == PosServerFiscalDocumentOutcome.FailedService ||
        result.HttpStatusCode == 503 ||
        result.ErrorPosture == FiscalIssuanceErrorPosture.RetryAfterServiceRecovery ||
        IsServiceOrUnknownReason(exceptionReason);

    private static FiscalIssuanceExceptionReason? MapExceptionReason(string normalizedCode) =>
        normalizedCode switch
        {
            "missing_payable_basis" => FiscalIssuanceExceptionReason.MissingPayableBasis,
            "missing_upstream_finality_reference" => FiscalIssuanceExceptionReason.MissingUpstreamFinalityReference,
            "unapproved_discount_reference" => FiscalIssuanceExceptionReason.UnapprovedDiscountReference,
            "unsupported_fiscal_document_request" => FiscalIssuanceExceptionReason.UnsupportedFiscalDocumentRequest,
            "invalid_fiscal_tender" => FiscalIssuanceExceptionReason.InvalidFiscalTender,
            "missing_fiscal_tender" => FiscalIssuanceExceptionReason.MissingFiscalTender,
            "invalid_fiscal_tax_detail" => FiscalIssuanceExceptionReason.InvalidFiscalTaxDetail,
            "invalid_fiscal_discount_privilege_detail" => FiscalIssuanceExceptionReason.InvalidFiscalDiscountPrivilegeDetail,
            "invalid_fiscal_total" => FiscalIssuanceExceptionReason.InvalidFiscalTotal,
            "sensitive_payload_rejected" => FiscalIssuanceExceptionReason.SensitivePayloadRejected,
            "request_construction_error" => FiscalIssuanceExceptionReason.RequestConstructionError,
            "fiscal_identity_not_found" => FiscalIssuanceExceptionReason.FiscalIdentityNotFound,
            "fiscal_identity_ambiguous" => FiscalIssuanceExceptionReason.FiscalIdentityAmbiguous,
            "fiscal_identity_not_effective" => FiscalIssuanceExceptionReason.FiscalIdentityNotEffective,
            "fiscal_sequence_policy_not_found" => FiscalIssuanceExceptionReason.FiscalSequencePolicyNotFound,
            "fiscal_sequence_policy_ambiguous" => FiscalIssuanceExceptionReason.FiscalSequencePolicyAmbiguous,
            "fiscal_sequence_policy_not_effective" => FiscalIssuanceExceptionReason.FiscalSequencePolicyNotEffective,
            "fiscal_sequence_state_not_found" => FiscalIssuanceExceptionReason.FiscalSequenceStateNotFound,
            "fiscal_sequence_state_not_effective" => FiscalIssuanceExceptionReason.FiscalSequenceStateNotEffective,
            "fiscal_number_allocation_failed" => FiscalIssuanceExceptionReason.FiscalNumberAllocationFailed,
            "fiscal_document_number_format_failed" => FiscalIssuanceExceptionReason.FiscalDocumentNumberFormatFailed,
            "fiscal_document_idempotency_conflict" => FiscalIssuanceExceptionReason.FiscalDocumentIdempotencyConflict,
            "replay_mismatch" => FiscalIssuanceExceptionReason.ReplayMismatch,
            "duplicate_reference_detected" => FiscalIssuanceExceptionReason.DuplicateReferenceDetected,
            "persistence_not_configured" => FiscalIssuanceExceptionReason.PersistenceNotConfigured,
            "invalid_persistence_configuration" => FiscalIssuanceExceptionReason.InvalidPersistenceConfiguration,
            "persistence_write_failed" => FiscalIssuanceExceptionReason.PersistenceWriteFailed,
            "fiscal_number_assignment_incomplete" => FiscalIssuanceExceptionReason.FiscalNumberAssignmentIncomplete,
            "post_timeout" => FiscalIssuanceExceptionReason.PostTimeout,
            "network_disconnect_after_possible_commit" => FiscalIssuanceExceptionReason.NetworkDisconnectAfterPossibleCommit,
            "get_readback_not_found" => FiscalIssuanceExceptionReason.GetReadbackNotFound,
            "get_readback_service_failed" => FiscalIssuanceExceptionReason.GetReadbackServiceFailed,
            "get_readback_inconclusive" => FiscalIssuanceExceptionReason.GetReadbackInconclusive,
            "central_pms_reference_persistence_failed" => FiscalIssuanceExceptionReason.CentralPmsReferencePersistenceFailed,
            "manual_review_required" => FiscalIssuanceExceptionReason.ManualReviewRequired,
            "manual_release_requested_after_fiscal_failure" => FiscalIssuanceExceptionReason.ManualReleaseRequestedAfterFiscalFailure,
            "fiscal_reference_mismatch" => FiscalIssuanceExceptionReason.FiscalReferenceMismatch,
            "reconciliation_required" => FiscalIssuanceExceptionReason.ReconciliationRequired,
            "reconciliation_closed" => FiscalIssuanceExceptionReason.ReconciliationClosed,
            _ => null
        };

    private static bool IsConfigurationReason(FiscalIssuanceExceptionReason? reason) =>
        reason is FiscalIssuanceExceptionReason.FiscalIdentityNotFound
            or FiscalIssuanceExceptionReason.FiscalIdentityAmbiguous
            or FiscalIssuanceExceptionReason.FiscalIdentityNotEffective
            or FiscalIssuanceExceptionReason.FiscalSequencePolicyNotFound
            or FiscalIssuanceExceptionReason.FiscalSequencePolicyAmbiguous
            or FiscalIssuanceExceptionReason.FiscalSequencePolicyNotEffective
            or FiscalIssuanceExceptionReason.FiscalSequenceStateNotFound
            or FiscalIssuanceExceptionReason.FiscalSequenceStateNotEffective
            or FiscalIssuanceExceptionReason.FiscalNumberAllocationFailed
            or FiscalIssuanceExceptionReason.FiscalDocumentNumberFormatFailed;

    private static bool IsServiceOrUnknownReason(FiscalIssuanceExceptionReason? reason) =>
        reason is FiscalIssuanceExceptionReason.PersistenceNotConfigured
            or FiscalIssuanceExceptionReason.InvalidPersistenceConfiguration
            or FiscalIssuanceExceptionReason.PersistenceWriteFailed
            or FiscalIssuanceExceptionReason.FiscalNumberAssignmentIncomplete
            or FiscalIssuanceExceptionReason.PostTimeout
            or FiscalIssuanceExceptionReason.NetworkDisconnectAfterPossibleCommit
            or FiscalIssuanceExceptionReason.GetReadbackNotFound
            or FiscalIssuanceExceptionReason.GetReadbackServiceFailed
            or FiscalIssuanceExceptionReason.GetReadbackInconclusive
            or FiscalIssuanceExceptionReason.CentralPmsReferencePersistenceFailed;

    private static FiscalIssuanceUnknownOutcomeContext UnknownOutcomeContextFromReadbackResult(
        FiscalIssuanceReadbackPlanningResult result,
        FiscalIssuanceExceptionReason defaultReason,
        string defaultErrorCode) =>
        new(
            ExceptionReason: result.ExceptionReason ?? defaultReason,
            ErrorCode: string.IsNullOrWhiteSpace(result.ErrorCode) ? defaultErrorCode : result.ErrorCode,
            ErrorPosture: FiscalIssuanceErrorPosture.RetryAfterServiceRecovery,
            KnownPosServerFiscalDocumentId: result.KnownPosServerFiscalDocumentId,
            CorrelationId: result.CorrelationId,
            ServiceIdentityId: result.ServiceIdentityId);

    private static void ValidateUnknownOutcomeReason(FiscalIssuanceExceptionReason reason)
    {
        if (reason is not (FiscalIssuanceExceptionReason.PostTimeout
            or FiscalIssuanceExceptionReason.NetworkDisconnectAfterPossibleCommit
            or FiscalIssuanceExceptionReason.GetReadbackInconclusive
            or FiscalIssuanceExceptionReason.GetReadbackNotFound
            or FiscalIssuanceExceptionReason.GetReadbackServiceFailed
            or FiscalIssuanceExceptionReason.CentralPmsReferencePersistenceFailed
            or FiscalIssuanceExceptionReason.FiscalNumberAssignmentIncomplete))
        {
            throw new ArgumentException("Fiscal issuance exception reason is not an unknown/readback reason.", nameof(reason));
        }
    }

    private static string ToErrorCode(FiscalIssuanceExceptionReason reason) =>
        reason switch
        {
            FiscalIssuanceExceptionReason.PostTimeout => "post_timeout",
            FiscalIssuanceExceptionReason.NetworkDisconnectAfterPossibleCommit => "network_disconnect_after_possible_commit",
            FiscalIssuanceExceptionReason.GetReadbackInconclusive => "get_readback_inconclusive",
            FiscalIssuanceExceptionReason.GetReadbackNotFound => "get_readback_not_found",
            FiscalIssuanceExceptionReason.GetReadbackServiceFailed => "get_readback_service_failed",
            FiscalIssuanceExceptionReason.CentralPmsReferencePersistenceFailed => "central_pms_reference_persistence_failed",
            FiscalIssuanceExceptionReason.FiscalNumberAssignmentIncomplete => "fiscal_number_assignment_incomplete",
            _ => "fiscal_issuance_unknown"
        };
}

public sealed record PrepareFiscalIssuanceCommand(
    Guid PaymentConfirmationId,
    Guid PaymentAttemptId,
    Guid ParkingSessionId,
    Guid? TariffSnapshotId,
    Guid? SiteId,
    Guid? SitePosServerId,
    string? SitePosServerRef,
    Guid? FiscalDocumentTypeCodeId,
    string? FiscalDocumentTypeCodeKey,
    string? PayableBasisRef,
    string UpstreamFinalityReference,
    Guid? CorrelationId,
    Guid? ServiceIdentityId)
{
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (PaymentConfirmationId == Guid.Empty)
        {
            errors.Add("payment_confirmation_id_required");
        }

        if (PaymentAttemptId == Guid.Empty)
        {
            errors.Add("payment_attempt_id_required");
        }

        if (ParkingSessionId == Guid.Empty)
        {
            errors.Add("parking_session_id_required");
        }

        if (string.IsNullOrWhiteSpace(UpstreamFinalityReference))
        {
            errors.Add("upstream_finality_reference_required");
        }

        if (SitePosServerId is null && string.IsNullOrWhiteSpace(SitePosServerRef))
        {
            errors.Add("site_pos_server_context_required");
        }

        if (FiscalDocumentTypeCodeId is null && string.IsNullOrWhiteSpace(FiscalDocumentTypeCodeKey))
        {
            errors.Add("fiscal_document_type_required");
        }

        return errors;
    }

    public CreateFiscalIssuanceReferenceRequest ToCreateRequest() =>
        new(
            PaymentConfirmationId: PaymentConfirmationId,
            PaymentAttemptId: PaymentAttemptId,
            ParkingSessionId: ParkingSessionId,
            TariffSnapshotId: TariffSnapshotId,
            SiteId: SiteId,
            SitePosServerId: SitePosServerId,
            SitePosServerRef: SitePosServerRef,
            FiscalDocumentTypeCodeId: FiscalDocumentTypeCodeId,
            FiscalDocumentTypeCodeKey: FiscalDocumentTypeCodeKey,
            PayableBasisRef: PayableBasisRef,
            UpstreamFinalityReference: UpstreamFinalityReference,
            PosServerFiscalDocumentId: null,
            FiscalIdentityId: null,
            FiscalSequencePolicyId: null,
            FiscalSequenceValue: null,
            FiscalDocumentNumber: null,
            FiscalSeries: null,
            FiscalNumberPrefixText: null,
            FiscalNumberSuffixText: null,
            FiscalNumberAssignedAt: null,
            FiscalNumberAssignedByRef: null,
            FiscalDocumentStatusCodeId: null,
            ResultClassification: null,
            FiscalIssuanceEvidenceStatus: null,
            FiscalNumberAssignmentState: FiscalNumberAssignmentState.NotAssigned,
            FiscalIssuanceState: FiscalIssuanceIntegrationState.PendingFiscalIssuance,
            LatestExceptionReason: null,
            LatestErrorCode: null,
            LatestErrorPosture: null,
            CorrelationId: CorrelationId,
            PosServerResponseTimestamp: null,
            RecordedByServiceIdentityId: ServiceIdentityId);
}

public sealed record FiscalIssuanceTransitionContext(
    Guid? CorrelationId,
    Guid? ServiceIdentityId);

public sealed record FiscalIssuanceFailureTransitionContext(
    FiscalIssuanceExceptionReason? ExceptionReason,
    string? ErrorCode,
    FiscalIssuanceErrorPosture? ErrorPosture,
    Guid? CorrelationId,
    Guid? ServiceIdentityId);

public sealed record FiscalIssuanceEvidenceInput(
    Guid? PosServerFiscalDocumentId,
    Guid? FiscalIdentityId,
    Guid? FiscalSequencePolicyId,
    long? FiscalSequenceValue,
    string? FiscalDocumentNumber,
    string? FiscalSeries,
    string? FiscalNumberPrefixText,
    string? FiscalNumberSuffixText,
    DateTimeOffset? FiscalNumberAssignedAt,
    string? FiscalNumberAssignedByRef,
    Guid? FiscalDocumentStatusCodeId,
    Guid? CorrelationId,
    DateTimeOffset? PosServerResponseTimestamp,
    Guid? ServiceIdentityId);

public sealed record PosServerCreateResultRecordingContext(
    string UpstreamFinalityReference,
    Guid? SitePosServerId,
    Guid? FiscalDocumentTypeCodeId,
    Guid? CorrelationId,
    DateTimeOffset? PosServerResponseTimestamp,
    Guid? ServiceIdentityId);

public sealed record FiscalIssuanceUnknownOutcomeContext(
    FiscalIssuanceExceptionReason ExceptionReason,
    string? ErrorCode,
    FiscalIssuanceErrorPosture? ErrorPosture,
    Guid? KnownPosServerFiscalDocumentId,
    Guid? CorrelationId,
    Guid? ServiceIdentityId);

public sealed record FiscalIssuanceReadbackPlanningContext(
    Guid? KnownPosServerFiscalDocumentId,
    FiscalIssuanceExceptionReason? ExceptionReason,
    string? ErrorCode,
    Guid? CorrelationId,
    Guid? ServiceIdentityId);

public enum FiscalIssuanceReadbackPlanningOutcome
{
    Requested = 1,
    Inconclusive = 2,
    NotFound = 3,
    ServiceFailed = 4,
    Mismatch = 5
}

public sealed record FiscalIssuanceReadbackPlanningResult(
    FiscalIssuanceReadbackPlanningOutcome Outcome,
    Guid? KnownPosServerFiscalDocumentId,
    FiscalIssuanceExceptionReason? ExceptionReason,
    string? ErrorCode,
    Guid? CorrelationId,
    Guid? ServiceIdentityId);
