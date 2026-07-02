using ExitPass.CentralPms.Domain.FiscalIssuance;

namespace ExitPass.CentralPms.Application.FiscalIssuance;

public static class FiscalIssuanceExitAuthorizationGateEvaluator
{
    public static FiscalIssuanceGatingEvaluation Evaluate(
        FiscalIssuanceReferenceRecord? reference,
        FiscalIssuanceGatingEvaluationContext context)
    {
        if (!context.IsPaymentFinalityVerified)
        {
            return Blocked(
                reference?.FiscalIssuanceState,
                "payment_finality_not_verified",
                requiresManualReview: false,
                isExceptionReleaseOnly: false);
        }

        if (reference is null)
        {
            return Blocked(
                state: null,
                blockedReason: "fiscal_reference_not_recorded",
                requiresManualReview: false,
                isExceptionReleaseOnly: false);
        }

        return reference.FiscalIssuanceState switch
        {
            FiscalIssuanceIntegrationState.NotRequired =>
                EvaluateNotRequired(reference, context),
            FiscalIssuanceIntegrationState.FiscalIssuanceRecorded =>
                EvaluateCompleteEvidenceState(reference),
            FiscalIssuanceIntegrationState.FiscalIssuanceReplayed =>
                EvaluateCompleteEvidenceState(reference),
            FiscalIssuanceIntegrationState.FiscalIssuanceReconciled =>
                EvaluateReconciled(reference, context),
            FiscalIssuanceIntegrationState.PendingFiscalIssuance =>
                Blocked(reference.FiscalIssuanceState, "fiscal_issuance_pending", false, false),
            FiscalIssuanceIntegrationState.FiscalIssuanceRequested =>
                Blocked(reference.FiscalIssuanceState, "fiscal_issuance_requested", false, false),
            FiscalIssuanceIntegrationState.FiscalIssuanceConflict =>
                Blocked(reference.FiscalIssuanceState, "fiscal_issuance_conflict", true, false),
            FiscalIssuanceIntegrationState.FiscalIssuanceFailedRequest =>
                Blocked(reference.FiscalIssuanceState, "fiscal_issuance_failed_request", true, false),
            FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration =>
                Blocked(reference.FiscalIssuanceState, "fiscal_issuance_failed_configuration", true, false),
            FiscalIssuanceIntegrationState.FiscalIssuanceFailedService =>
                Blocked(reference.FiscalIssuanceState, "fiscal_issuance_failed_service", true, false),
            FiscalIssuanceIntegrationState.FiscalIssuanceUnknown =>
                Blocked(reference.FiscalIssuanceState, "fiscal_issuance_unknown", true, false),
            FiscalIssuanceIntegrationState.FiscalIssuanceManualReview =>
                Blocked(reference.FiscalIssuanceState, "fiscal_issuance_manual_review", true, false),
            FiscalIssuanceIntegrationState.FiscalIssuanceExceptionReleased =>
                Blocked(reference.FiscalIssuanceState, "fiscal_issuance_exception_release_only", false, true),
            _ => Blocked(reference.FiscalIssuanceState, "fiscal_issuance_evidence_incomplete", true, false)
        };
    }

    private static FiscalIssuanceGatingEvaluation EvaluateNotRequired(
        FiscalIssuanceReferenceRecord reference,
        FiscalIssuanceGatingEvaluationContext context) =>
        context.IsNoFiscalRequiredPolicyApproved
            ? Ready(reference.FiscalIssuanceState)
            : Blocked(
                reference.FiscalIssuanceState,
                "fiscal_issuance_not_required_policy_required",
                requiresManualReview: false,
                isExceptionReleaseOnly: false);

    private static FiscalIssuanceGatingEvaluation EvaluateReconciled(
        FiscalIssuanceReferenceRecord reference,
        FiscalIssuanceGatingEvaluationContext context)
    {
        if (!context.IsReconciledFiscalEvidencePolicyApproved)
        {
            return Blocked(
                reference.FiscalIssuanceState,
                "fiscal_issuance_reconciled_policy_required",
                requiresManualReview: false,
                isExceptionReleaseOnly: false);
        }

        return EvaluateCompleteEvidenceState(reference);
    }

    private static FiscalIssuanceGatingEvaluation EvaluateCompleteEvidenceState(
        FiscalIssuanceReferenceRecord reference)
    {
        if (reference.FiscalIssuanceEvidenceStatus != FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned)
        {
            return Blocked(reference.FiscalIssuanceState, "fiscal_issuance_evidence_incomplete", false, false);
        }

        if (reference.FiscalNumberAssignmentState != FiscalNumberAssignmentState.Assigned)
        {
            return Blocked(reference.FiscalIssuanceState, "fiscal_number_not_assigned", false, false);
        }

        if (reference.PosServerFiscalDocumentId is null ||
            reference.PosServerFiscalDocumentId == Guid.Empty ||
            reference.FirstRecordedAt == default)
        {
            return Blocked(reference.FiscalIssuanceState, "fiscal_reference_not_recorded", false, false);
        }

        if (reference.FiscalIdentityId is null ||
            reference.FiscalIdentityId == Guid.Empty ||
            reference.FiscalSequencePolicyId is null ||
            reference.FiscalSequencePolicyId == Guid.Empty ||
            reference.FiscalSequenceValue is null or < 1 ||
            string.IsNullOrWhiteSpace(reference.FiscalDocumentNumber) ||
            reference.FiscalNumberAssignedAt is null ||
            string.IsNullOrWhiteSpace(reference.FiscalNumberAssignedByRef) ||
            reference.FiscalDocumentStatusCodeId is null ||
            reference.FiscalDocumentStatusCodeId == Guid.Empty)
        {
            return Blocked(reference.FiscalIssuanceState, "fiscal_issuance_evidence_incomplete", false, false);
        }

        return Ready(reference.FiscalIssuanceState);
    }

    private static FiscalIssuanceGatingEvaluation Ready(FiscalIssuanceIntegrationState? state) =>
        new(
            IsReadyForNormalExitAuthorization: true,
            BlockedReason: null,
            State: state,
            RequiresManualReview: false,
            IsExceptionReleaseOnly: false);

    private static FiscalIssuanceGatingEvaluation Blocked(
        FiscalIssuanceIntegrationState? state,
        string blockedReason,
        bool requiresManualReview,
        bool isExceptionReleaseOnly) =>
        new(
            IsReadyForNormalExitAuthorization: false,
            BlockedReason: blockedReason,
            State: state,
            RequiresManualReview: requiresManualReview,
            IsExceptionReleaseOnly: isExceptionReleaseOnly);
}

public sealed record FiscalIssuanceGatingEvaluationContext(
    bool IsPaymentFinalityVerified,
    bool IsNoFiscalRequiredPolicyApproved = false,
    bool IsReconciledFiscalEvidencePolicyApproved = false);

public sealed record FiscalIssuanceGatingEvaluation(
    bool IsReadyForNormalExitAuthorization,
    string? BlockedReason,
    FiscalIssuanceIntegrationState? State,
    bool RequiresManualReview,
    bool IsExceptionReleaseOnly);
