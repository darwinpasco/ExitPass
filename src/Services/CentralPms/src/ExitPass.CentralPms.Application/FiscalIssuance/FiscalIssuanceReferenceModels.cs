using ExitPass.CentralPms.Domain.FiscalIssuance;

namespace ExitPass.CentralPms.Application.FiscalIssuance;

public sealed record FiscalIssuanceReferenceRecord(
    Guid FiscalIssuanceReferenceId,
    Guid PaymentConfirmationId,
    Guid PaymentAttemptId,
    Guid ParkingSessionId,
    Guid? TariffSnapshotId,
    Guid? SiteId,
    Guid? SitePosServerId,
    string? SitePosServerRef,
    string? PayableBasisRef,
    string UpstreamFinalityReference,
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
    FiscalIssuanceResultClassification? ResultClassification,
    FiscalIssuanceEvidenceStatus? FiscalIssuanceEvidenceStatus,
    FiscalNumberAssignmentState FiscalNumberAssignmentState,
    FiscalIssuanceIntegrationState FiscalIssuanceState,
    FiscalIssuanceExceptionReason? LatestExceptionReason,
    string? LatestErrorCode,
    FiscalIssuanceErrorPosture? LatestErrorPosture,
    Guid? CorrelationId,
    DateTimeOffset? PosServerResponseTimestamp,
    DateTimeOffset FirstRecordedAt,
    DateTimeOffset LastUpdatedAt,
    Guid? RecordedByServiceIdentityId);

public sealed record CreateFiscalIssuanceReferenceRequest(
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
    FiscalIssuanceResultClassification? ResultClassification,
    FiscalIssuanceEvidenceStatus? FiscalIssuanceEvidenceStatus,
    FiscalNumberAssignmentState FiscalNumberAssignmentState,
    FiscalIssuanceIntegrationState FiscalIssuanceState,
    FiscalIssuanceExceptionReason? LatestExceptionReason,
    string? LatestErrorCode,
    FiscalIssuanceErrorPosture? LatestErrorPosture,
    Guid? CorrelationId,
    DateTimeOffset? PosServerResponseTimestamp,
    Guid? RecordedByServiceIdentityId)
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

        if (RequiresCompleteFiscalEvidence(FiscalIssuanceState))
        {
            if (PosServerFiscalDocumentId is null || PosServerFiscalDocumentId == Guid.Empty)
            {
                errors.Add("pos_server_fiscal_document_id_required");
            }

            if (FiscalIdentityId is null || FiscalIdentityId == Guid.Empty)
            {
                errors.Add("fiscal_identity_id_required");
            }

            if (FiscalSequencePolicyId is null || FiscalSequencePolicyId == Guid.Empty)
            {
                errors.Add("fiscal_sequence_policy_id_required");
            }

            if (FiscalSequenceValue is null or < 1)
            {
                errors.Add("fiscal_sequence_value_required");
            }

            if (string.IsNullOrWhiteSpace(FiscalDocumentNumber))
            {
                errors.Add("fiscal_document_number_required");
            }

            if (FiscalNumberAssignedAt is null)
            {
                errors.Add("fiscal_number_assigned_at_required");
            }

            if (FiscalIssuanceEvidenceStatus != Domain.FiscalIssuance.FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned)
            {
                errors.Add("fiscal_issuance_evidence_status_required");
            }

            if (FiscalNumberAssignmentState != FiscalNumberAssignmentState.Assigned)
            {
                errors.Add("fiscal_number_assignment_state_assigned_required");
            }
        }

        if (RequiresExceptionReason(FiscalIssuanceState) && LatestExceptionReason is null)
        {
            errors.Add("latest_exception_reason_required");
        }

        return errors;
    }

    public static bool RequiresCompleteFiscalEvidence(FiscalIssuanceIntegrationState state) =>
        state is FiscalIssuanceIntegrationState.FiscalIssuanceRecorded
            or FiscalIssuanceIntegrationState.FiscalIssuanceReplayed
            or FiscalIssuanceIntegrationState.FiscalIssuanceReconciled;

    public static bool RequiresExceptionReason(FiscalIssuanceIntegrationState state) =>
        state is FiscalIssuanceIntegrationState.FiscalIssuanceConflict
            or FiscalIssuanceIntegrationState.FiscalIssuanceFailedRequest
            or FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration
            or FiscalIssuanceIntegrationState.FiscalIssuanceFailedService
            or FiscalIssuanceIntegrationState.FiscalIssuanceUnknown
            or FiscalIssuanceIntegrationState.FiscalIssuanceManualReview
            or FiscalIssuanceIntegrationState.FiscalIssuanceExceptionReleased;
}

public sealed record FiscalIssuanceAttemptRecord(
    Guid FiscalIssuanceAttemptId,
    Guid? FiscalIssuanceReferenceId,
    Guid PaymentConfirmationId,
    int AttemptSequenceNumber,
    string TriggerSource,
    string ActionType,
    string UpstreamFinalityReference,
    Guid? CorrelationId,
    int? PosServerHttpStatus,
    string? PosServerResponseCode,
    FiscalIssuanceResultClassification? ResultClassification,
    FiscalIssuanceEvidenceStatus? FiscalIssuanceEvidenceStatus,
    FiscalNumberAssignmentState? FiscalNumberAssignmentState,
    Guid? PosServerFiscalDocumentId,
    string? ErrorCode,
    FiscalIssuanceErrorPosture? ErrorPosture,
    DateTimeOffset AttemptedAt,
    DateTimeOffset? CompletedAt,
    string OutcomeClassification);

public sealed record FiscalIssuanceStateTransitionRequest(
    FiscalIssuanceIntegrationState FiscalIssuanceState,
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
    FiscalIssuanceResultClassification? ResultClassification,
    FiscalIssuanceEvidenceStatus? FiscalIssuanceEvidenceStatus,
    FiscalNumberAssignmentState FiscalNumberAssignmentState,
    FiscalIssuanceExceptionReason? LatestExceptionReason,
    string? LatestErrorCode,
    FiscalIssuanceErrorPosture? LatestErrorPosture,
    Guid? CorrelationId,
    DateTimeOffset? PosServerResponseTimestamp,
    Guid? UpdatedByServiceIdentityId)
{
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (CreateFiscalIssuanceReferenceRequest.RequiresCompleteFiscalEvidence(FiscalIssuanceState))
        {
            if (PosServerFiscalDocumentId is null || PosServerFiscalDocumentId == Guid.Empty)
            {
                errors.Add("pos_server_fiscal_document_id_required");
            }

            if (FiscalIdentityId is null || FiscalIdentityId == Guid.Empty)
            {
                errors.Add("fiscal_identity_id_required");
            }

            if (FiscalSequencePolicyId is null || FiscalSequencePolicyId == Guid.Empty)
            {
                errors.Add("fiscal_sequence_policy_id_required");
            }

            if (FiscalSequenceValue is null or < 1)
            {
                errors.Add("fiscal_sequence_value_required");
            }

            if (string.IsNullOrWhiteSpace(FiscalDocumentNumber))
            {
                errors.Add("fiscal_document_number_required");
            }

            if (FiscalNumberAssignedAt is null)
            {
                errors.Add("fiscal_number_assigned_at_required");
            }

            if (FiscalIssuanceEvidenceStatus != Domain.FiscalIssuance.FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned)
            {
                errors.Add("fiscal_issuance_evidence_status_required");
            }

            if (FiscalNumberAssignmentState != FiscalNumberAssignmentState.Assigned)
            {
                errors.Add("fiscal_number_assignment_state_assigned_required");
            }
        }

        if (CreateFiscalIssuanceReferenceRequest.RequiresExceptionReason(FiscalIssuanceState) && LatestExceptionReason is null)
        {
            errors.Add("latest_exception_reason_required");
        }

        return errors;
    }
}

public interface IFiscalIssuanceReferenceRepository
{
    Task<FiscalIssuanceReferenceRecord> CreateAsync(
        CreateFiscalIssuanceReferenceRequest request,
        CancellationToken cancellationToken);

    Task<FiscalIssuanceReferenceRecord> UpdateStateAsync(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceStateTransitionRequest request,
        CancellationToken cancellationToken);

    Task<FiscalIssuanceReferenceRecord?> FindByPaymentConfirmationIdAsync(
        Guid paymentConfirmationId,
        CancellationToken cancellationToken);

    Task<FiscalIssuanceReferenceRecord?> FindByUpstreamFinalityReferenceAsync(
        string upstreamFinalityReference,
        Guid? sitePosServerId,
        Guid? fiscalDocumentTypeCodeId,
        CancellationToken cancellationToken);

    Task<FiscalIssuanceReferenceRecord?> FindByPosServerFiscalDocumentIdAsync(
        Guid posServerFiscalDocumentId,
        CancellationToken cancellationToken);
}
