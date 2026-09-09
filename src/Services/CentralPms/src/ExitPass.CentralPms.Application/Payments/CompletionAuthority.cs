using ExitPass.CentralPms.Application.StatutoryDiscounts;

namespace ExitPass.CentralPms.Application.Payments;

public static class CompletionBasisCodes
{
    public const string PaymentFinality = "PAYMENT_FINALITY";
    public const string ZeroPayableStatutoryFinality = "ZERO_PAYABLE_STATUTORY_FINALITY";
}

public static class CompletionAuthorityStates
{
    public const string Established = "ESTABLISHED";
}

public static class ExitAuthorizationEligibilityStatuses
{
    public const string PaymentCompletionAuthorityReady = "PAYMENT_COMPLETION_AUTHORITY_READY";
    public const string ZeroPayableCompletionAuthorityReady = "ZERO_PAYABLE_COMPLETION_AUTHORITY_READY";
    public const string ZeroPayableFiscalPrerequisiteSatisfied = "ZERO_PAYABLE_FISCAL_PREREQUISITE_SATISFIED";
}

public static class ExitAuthorizationEligibilityBlockedReasons
{
    public const string ZeroPayableFiscalPrerequisiteUnresolved =
        "ZERO_PAYABLE_FISCAL_PREREQUISITE_UNRESOLVED";
    public const string ZeroPayableExitAuthorizationIssuancePathUnavailable =
        "ZERO_PAYABLE_EXIT_AUTHORIZATION_ISSUANCE_PATH_UNAVAILABLE";
}

public sealed record CompletionAuthority(
    Guid ParkingSessionId,
    Guid TariffSnapshotId,
    Guid SiteId,
    Guid SiteGroupId,
    string CompletionBasis,
    Guid DurableSourceReferenceId,
    DateTimeOffset EstablishedAt,
    Guid CorrelationId,
    long FinalPayableAmountMinorUnits,
    string Currency,
    Guid? PaymentAttemptId = null,
    Guid? PaymentConfirmationId = null,
    Guid? StatutoryDiscountDecisionCommandId = null,
    Guid? StatutoryDiscountPayableBasisApplicationCommandId = null,
    Guid? StatutoryDiscountValidationId = null,
    Guid? AppliedPolicyReferenceId = null,
    string AuthorityState = CompletionAuthorityStates.Established);

public sealed record CompletionAuthorityResolution(
    CompletionAuthority? Authority,
    string? RejectionCode)
{
    public bool IsEstablished => Authority is not null;
}

public sealed record ExitAuthorizationEligibility(
    bool CompletionAuthorityEligible,
    bool ExitAuthorizationIssuanceAllowed,
    string Status,
    string? BlockedReason,
    string CompletionBasis);

public sealed record PaymentFinalityCompletionCandidate(
    Guid PaymentAttemptId,
    Guid ParkingSessionId,
    Guid TariffSnapshotId,
    Guid? SiteId,
    Guid? SiteGroupId,
    string AttemptStatus,
    DateTimeOffset? FinalizedAt,
    long AmountMinorUnits,
    string Currency,
    Guid? TariffParkingSessionId,
    string? TariffStatus,
    long? TariffNetAmountMinorUnits,
    string? TariffCurrency,
    Guid? PaymentConfirmationId,
    string? ConfirmationStatus,
    long? ConfirmedAmountMinorUnits,
    string? ConfirmationCurrency,
    DateTimeOffset? ConfirmedAt,
    Guid? CorrelationId);

public interface IPaymentFinalityCompletionAuthorityReader
{
    Task<PaymentFinalityCompletionCandidate?> ReadAsync(
        Guid paymentAttemptId,
        CancellationToken cancellationToken);
}

public static class CompletionAuthorityResolver
{
    public static CompletionAuthorityResolution ResolvePaymentFinality(
        PaymentFinalityCompletionCandidate? candidate,
        Guid expectedParkingSessionId,
        Guid expectedPaymentAttemptId)
    {
        if (candidate is null)
        {
            return Rejected("PAYMENT_ATTEMPT_NOT_FOUND");
        }

        if (candidate.PaymentAttemptId != expectedPaymentAttemptId)
        {
            return Rejected("PAYMENT_ATTEMPT_LINKAGE_MISMATCH");
        }

        if (candidate.ParkingSessionId != expectedParkingSessionId ||
            candidate.TariffParkingSessionId != expectedParkingSessionId)
        {
            return Rejected("PAYMENT_FINALITY_PARKING_SESSION_MISMATCH");
        }

        if (!EqualsCanonical(candidate.AttemptStatus, "CONFIRMED") || candidate.FinalizedAt is null)
        {
            return Rejected("PAYMENT_FINALITY_NOT_VERIFIED");
        }

        if (!candidate.PaymentConfirmationId.HasValue ||
            !EqualsCanonical(candidate.ConfirmationStatus, "RECORDED") ||
            candidate.ConfirmedAt is null)
        {
            return Rejected("PAYMENT_CONFIRMATION_NOT_RECORDED");
        }

        if (!candidate.SiteId.HasValue || !candidate.SiteGroupId.HasValue ||
            candidate.SiteId == Guid.Empty || candidate.SiteGroupId == Guid.Empty ||
            candidate.TariffSnapshotId == Guid.Empty ||
            candidate.TariffParkingSessionId is null ||
            candidate.TariffNetAmountMinorUnits is null ||
            string.IsNullOrWhiteSpace(candidate.TariffCurrency))
        {
            return Rejected("PAYMENT_FINALITY_PAYABLE_BASIS_INVALID");
        }

        if (candidate.TariffNetAmountMinorUnits != candidate.AmountMinorUnits ||
            candidate.ConfirmedAmountMinorUnits != candidate.AmountMinorUnits)
        {
            return Rejected("PAYMENT_FINALITY_AMOUNT_MISMATCH");
        }

        if (!EqualsCanonical(candidate.Currency, candidate.TariffCurrency) ||
            !EqualsCanonical(candidate.Currency, candidate.ConfirmationCurrency))
        {
            return Rejected("PAYMENT_FINALITY_CURRENCY_MISMATCH");
        }

        return new CompletionAuthorityResolution(
            new CompletionAuthority(
                candidate.ParkingSessionId,
                candidate.TariffSnapshotId,
                candidate.SiteId.Value,
                candidate.SiteGroupId.Value,
                CompletionBasisCodes.PaymentFinality,
                candidate.PaymentConfirmationId.Value,
                candidate.ConfirmedAt.Value,
                candidate.CorrelationId ?? Guid.Empty,
                candidate.AmountMinorUnits,
                candidate.Currency.Trim().ToUpperInvariant(),
                PaymentAttemptId: candidate.PaymentAttemptId,
                PaymentConfirmationId: candidate.PaymentConfirmationId),
            RejectionCode: null);
    }

    public static CompletionAuthorityResolution ResolveZeroPayableStatutoryFinality(
        StatutoryDiscountZeroPayableFinalityResolution finalityResolution,
        Guid expectedParkingSessionId,
        Guid expectedTariffSnapshotId,
        Guid expectedSiteId,
        Guid expectedSiteGroupId)
    {
        if (!finalityResolution.IsAvailable || finalityResolution.Finality is null)
        {
            return Rejected(
                finalityResolution.RejectionCode ?? "ZERO_PAYABLE_STATUTORY_FINALITY_NOT_AVAILABLE");
        }

        var finality = finalityResolution.Finality;
        if (finality.ParkingSessionId != expectedParkingSessionId)
        {
            return Rejected("ZERO_PAYABLE_COMPLETION_PARKING_SESSION_MISMATCH");
        }

        if (finality.AppliedTariffSnapshotId != expectedTariffSnapshotId)
        {
            return Rejected("ZERO_PAYABLE_COMPLETION_TARIFF_MISMATCH");
        }

        if (finality.SiteId != expectedSiteId || finality.SiteGroupId != expectedSiteGroupId)
        {
            return Rejected("ZERO_PAYABLE_COMPLETION_SITE_SCOPE_MISMATCH");
        }

        if (finality.FinalPayableAmountMinorUnits != 0)
        {
            return Rejected("ZERO_PAYABLE_COMPLETION_AMOUNT_NOT_ZERO");
        }

        return new CompletionAuthorityResolution(
            new CompletionAuthority(
                finality.ParkingSessionId,
                finality.AppliedTariffSnapshotId,
                finality.SiteId,
                finality.SiteGroupId,
                CompletionBasisCodes.ZeroPayableStatutoryFinality,
                finality.StatutoryDiscountPayableBasisApplicationCommandId,
                finality.AppliedAt,
                finality.CorrelationId,
                finality.FinalPayableAmountMinorUnits,
                finality.Currency,
                StatutoryDiscountDecisionCommandId: finality.StatutoryDiscountDecisionCommandId,
                StatutoryDiscountPayableBasisApplicationCommandId:
                    finality.StatutoryDiscountPayableBasisApplicationCommandId,
                StatutoryDiscountValidationId: finality.StatutoryDiscountValidationId,
                AppliedPolicyReferenceId: finality.AppliedPolicyReferenceId),
            RejectionCode: null);
    }

    public static ExitAuthorizationEligibility EvaluateZeroPayableExitAuthorizationEligibility(
        CompletionAuthority authority,
        bool fiscalPrerequisiteSatisfied = false,
        bool issuancePathAvailable = false)
    {
        ArgumentNullException.ThrowIfNull(authority);

        if (!EqualsCanonical(authority.CompletionBasis, CompletionBasisCodes.ZeroPayableStatutoryFinality) ||
            authority.FinalPayableAmountMinorUnits != 0)
        {
            throw new InvalidOperationException(
                "Zero-payable ExitAuthorization eligibility requires statutory zero-payable completion authority.");
        }

        return new ExitAuthorizationEligibility(
            CompletionAuthorityEligible: true,
            ExitAuthorizationIssuanceAllowed: fiscalPrerequisiteSatisfied && issuancePathAvailable,
            Status: fiscalPrerequisiteSatisfied
                ? ExitAuthorizationEligibilityStatuses.ZeroPayableFiscalPrerequisiteSatisfied
                : ExitAuthorizationEligibilityStatuses.ZeroPayableCompletionAuthorityReady,
            BlockedReason: !fiscalPrerequisiteSatisfied
                ? ExitAuthorizationEligibilityBlockedReasons.ZeroPayableFiscalPrerequisiteUnresolved
                : issuancePathAvailable
                    ? null
                    : ExitAuthorizationEligibilityBlockedReasons.ZeroPayableExitAuthorizationIssuancePathUnavailable,
            CompletionBasis: CompletionBasisCodes.ZeroPayableStatutoryFinality);
    }

    private static bool EqualsCanonical(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static CompletionAuthorityResolution Rejected(string code) => new(null, code);
}
