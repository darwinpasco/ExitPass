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
