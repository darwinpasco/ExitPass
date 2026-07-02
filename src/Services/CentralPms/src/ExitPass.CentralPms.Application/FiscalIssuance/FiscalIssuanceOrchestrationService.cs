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
