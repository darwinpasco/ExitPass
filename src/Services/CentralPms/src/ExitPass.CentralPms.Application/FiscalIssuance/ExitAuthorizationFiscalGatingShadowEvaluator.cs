namespace ExitPass.CentralPms.Application.FiscalIssuance;

public interface IExitAuthorizationFiscalGatingShadowEvaluator
{
    Task<FiscalGatingShadowEvaluation> EvaluateAsync(
        ExitAuthorizationFiscalGatingShadowContext context,
        CancellationToken cancellationToken);
}

public sealed class ExitAuthorizationFiscalGatingShadowEvaluator : IExitAuthorizationFiscalGatingShadowEvaluator
{
    public static readonly ExitAuthorizationFiscalGatingShadowEvaluator Instance = new();

    private readonly IFiscalIssuanceReferenceRepository? _repository;

    public ExitAuthorizationFiscalGatingShadowEvaluator(IFiscalIssuanceReferenceRepository? repository = null)
    {
        _repository = repository;
    }

    public async Task<FiscalGatingShadowEvaluation> EvaluateAsync(
        ExitAuthorizationFiscalGatingShadowContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        cancellationToken.ThrowIfCancellationRequested();

        if (context.IsFiscalIssuanceNotRequiredByPolicy)
        {
            return FiscalGatingShadowEvaluation.NotEvaluatedNotRequired();
        }

        var fiscalReference = context.FiscalReference;
        if (fiscalReference is null && _repository is not null &&
            context.CompletionBasis == Payments.CompletionBasisCodes.ZeroPayableStatutoryFinality &&
            context.StatutoryDiscountPayableBasisApplicationCommandId.HasValue)
        {
            fiscalReference = await _repository.FindByStatutoryApplicationCommandIdAsync(
                context.StatutoryDiscountPayableBasisApplicationCommandId.Value,
                cancellationToken);
        }
        else if (fiscalReference is null && _repository is not null && context.PaymentAttemptId != Guid.Empty)
        {
            fiscalReference = await _repository.FindLatestByPaymentAttemptIdAsync(
                context.PaymentAttemptId,
                cancellationToken);
        }

        if (fiscalReference is null)
        {
            return FiscalGatingShadowEvaluation.NotEvaluatedMissingFiscalContext();
        }

        if (context.CompletionBasis == Payments.CompletionBasisCodes.ZeroPayableStatutoryFinality &&
            (fiscalReference.CompletionBasis != Payments.CompletionBasisCodes.ZeroPayableStatutoryFinality ||
             fiscalReference.ParkingSessionId != context.ParkingSessionId ||
             fiscalReference.StatutoryDiscountPayableBasisApplicationCommandId !=
                context.StatutoryDiscountPayableBasisApplicationCommandId ||
             fiscalReference.PaymentAttemptId is not null ||
             fiscalReference.PaymentConfirmationId is not null))
        {
            return FiscalGatingShadowEvaluation.FromGatingEvaluation(
                new FiscalIssuanceGatingEvaluation(
                    false,
                    "zero_payable_fiscal_ancestry_mismatch",
                    fiscalReference.FiscalIssuanceState,
                    false,
                    false),
                fiscalReference);
        }

        var evaluation = FiscalIssuanceExitAuthorizationGateEvaluator.Evaluate(
            fiscalReference,
            new FiscalIssuanceGatingEvaluationContext(
                IsPaymentFinalityVerified: context.IsPaymentFinalityVerified,
                IsNoFiscalRequiredPolicyApproved: context.IsNoFiscalRequiredPolicyApproved,
                IsReconciledFiscalEvidencePolicyApproved: context.IsReconciledFiscalEvidencePolicyApproved,
                IsStatutoryCompletionAuthorityEstablished:
                    context.IsStatutoryCompletionAuthorityEstablished));

        if (evaluation.IsReadyForNormalExitAuthorization &&
            context.CompletionBasis == Payments.CompletionBasisCodes.ZeroPayableStatutoryFinality &&
            string.IsNullOrWhiteSpace(fiscalReference.ElectronicJournalEventReference))
        {
            evaluation = new FiscalIssuanceGatingEvaluation(
                false,
                "electronic_journal_not_recorded",
                fiscalReference.FiscalIssuanceState,
                false,
                false);
        }

        return FiscalGatingShadowEvaluation.FromGatingEvaluation(evaluation, fiscalReference);
    }
}

public sealed record ExitAuthorizationFiscalGatingShadowContext(
    Guid ParkingSessionId,
    Guid PaymentAttemptId,
    Guid CorrelationId,
    bool IsPaymentFinalityVerified,
    FiscalIssuanceReferenceRecord? FiscalReference = null,
    bool IsFiscalIssuanceNotRequiredByPolicy = false,
    bool IsNoFiscalRequiredPolicyApproved = false,
    bool IsReconciledFiscalEvidencePolicyApproved = false,
    string CompletionBasis = Payments.CompletionBasisCodes.PaymentFinality,
    bool IsStatutoryCompletionAuthorityEstablished = false,
    Guid? StatutoryDiscountPayableBasisApplicationCommandId = null);

public sealed record FiscalGatingShadowEvaluation(
    string Status,
    bool IsReadyForNormalExitAuthorization,
    string? BlockedReason,
    Domain.FiscalIssuance.FiscalIssuanceIntegrationState? State,
    bool RequiresManualReview,
    bool IsExceptionReleaseOnly,
    FiscalIssuanceReferenceRecord? FiscalReference = null)
{
    public static FiscalGatingShadowEvaluation FromGatingEvaluation(
        FiscalIssuanceGatingEvaluation evaluation,
        FiscalIssuanceReferenceRecord? fiscalReference = null) =>
        new(
            Status: evaluation.IsReadyForNormalExitAuthorization
                ? FiscalGatingShadowEvaluationStatuses.EvaluatedReady
                : FiscalGatingShadowEvaluationStatuses.EvaluatedBlocked,
            IsReadyForNormalExitAuthorization: evaluation.IsReadyForNormalExitAuthorization,
            BlockedReason: evaluation.BlockedReason,
            State: evaluation.State,
            RequiresManualReview: evaluation.RequiresManualReview,
            IsExceptionReleaseOnly: evaluation.IsExceptionReleaseOnly,
            FiscalReference: fiscalReference);

    public static FiscalGatingShadowEvaluation NotEvaluatedMissingFiscalContext() =>
        new(
            Status: FiscalGatingShadowEvaluationStatuses.NotEvaluatedMissingFiscalContext,
            IsReadyForNormalExitAuthorization: false,
            BlockedReason: "fiscal_reference_not_recorded",
            State: null,
            RequiresManualReview: false,
            IsExceptionReleaseOnly: false);

    public static FiscalGatingShadowEvaluation NotEvaluatedNotRequired() =>
        new(
            Status: FiscalGatingShadowEvaluationStatuses.NotEvaluatedNotRequired,
            IsReadyForNormalExitAuthorization: true,
            BlockedReason: null,
            State: null,
            RequiresManualReview: false,
            IsExceptionReleaseOnly: false);

    public static FiscalGatingShadowEvaluation EvaluationFailedNonBlocking(string blockedReason) =>
        new(
            Status: FiscalGatingShadowEvaluationStatuses.EvaluationFailedNonBlocking,
            IsReadyForNormalExitAuthorization: false,
            BlockedReason: blockedReason,
            State: null,
            RequiresManualReview: false,
            IsExceptionReleaseOnly: false);
}

public static class FiscalGatingShadowEvaluationStatuses
{
    public const string EvaluatedReady = "evaluated_ready";
    public const string EvaluatedBlocked = "evaluated_blocked";
    public const string NotEvaluatedMissingFiscalContext = "not_evaluated_missing_fiscal_context";
    public const string NotEvaluatedNotRequired = "not_evaluated_not_required";
    public const string EvaluationFailedNonBlocking = "evaluation_failed_non_blocking";
}
