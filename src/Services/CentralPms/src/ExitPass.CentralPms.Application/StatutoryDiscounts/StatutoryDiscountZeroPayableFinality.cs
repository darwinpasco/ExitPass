namespace ExitPass.CentralPms.Application.StatutoryDiscounts;

public static class StatutoryDiscountFinalityStates
{
    public const string ZeroPayableStatutoryFinality = "ZERO_PAYABLE_STATUTORY_FINALITY";
}

public sealed record StatutoryDiscountZeroPayableFinality(
    Guid ParkingSessionId,
    Guid StatutoryDiscountDecisionCommandId,
    Guid StatutoryDiscountPayableBasisApplicationCommandId,
    Guid StatutoryDiscountValidationId,
    Guid AppliedPolicyReferenceId,
    Guid OriginalTariffSnapshotId,
    Guid AppliedTariffSnapshotId,
    Guid SiteId,
    Guid SiteGroupId,
    string EntitlementType,
    string BenefitType,
    long OriginalAmountMinorUnits,
    long StatutoryWaiverAmountMinorUnits,
    long VatAmountMinorUnits,
    long FinalPayableAmountMinorUnits,
    string Currency,
    string SourceChannel,
    DateTimeOffset DecidedAt,
    DateTimeOffset AppliedAt,
    Guid CorrelationId,
    string FinalityState = StatutoryDiscountFinalityStates.ZeroPayableStatutoryFinality);

public sealed record StatutoryDiscountZeroPayableDecisionAnchor(
    Guid DecisionCommandId,
    Guid ParkingSessionId,
    Guid? ValidationId,
    Guid? AppliedPolicyReferenceId,
    string DecisionResultStatus,
    string EntitlementType,
    string SourceChannel,
    long? GrossAmountMinorUnits,
    long? VatAmountMinorUnits,
    long? StatutoryDiscountAmountMinorUnits,
    long? FinalPayableAmountMinorUnits,
    string? Currency,
    DateTimeOffset? DecidedAt);

public sealed record StatutoryDiscountZeroPayableApplicationCommandAnchor(
    Guid ApplicationCommandId,
    Guid DecisionCommandId,
    Guid ParkingSessionId,
    Guid? SiteId,
    Guid? ValidationId,
    Guid? ApplicationId,
    Guid? OriginalTariffSnapshotId,
    Guid? AppliedTariffSnapshotId,
    Guid? AppliedPolicyReferenceId,
    Guid? PolicyVersionId,
    string CommandStatus,
    string EntitlementType,
    long ApprovedDiscountAmountMinorUnits,
    long? ApprovedVatAmountMinorUnits,
    long ApprovedFinalPayableAmountMinorUnits,
    string Currency,
    string SourceChannel,
    Guid CorrelationId,
    DateTimeOffset? AppliedAt);

public sealed record StatutoryDiscountZeroPayableApplicationAnchor(
    Guid? ApplicationId,
    Guid? ValidationId,
    Guid? ParkingSessionId,
    Guid? OriginalTariffSnapshotId,
    Guid? AppliedTariffSnapshotId,
    string? ApplicationStatus,
    long? GrossAmountMinorUnits,
    long? VatAmountMinorUnits,
    long? StatutoryDiscountAmountMinorUnits,
    long? FinalPayableAmountMinorUnits,
    string? Currency,
    DateTimeOffset? AppliedAt,
    Guid? CorrelationId);

public sealed record StatutoryDiscountZeroPayableValidationAnchor(
    Guid? ValidationId,
    Guid? ParkingSessionId,
    Guid? TariffSnapshotId,
    Guid? AppliedPolicyReferenceId,
    Guid? PolicyVersionId,
    string? ValidationStatus,
    string? EntitlementType,
    long? GrossAmountMinorUnits,
    long? StatutoryDiscountAmountMinorUnits,
    long? FinalPayableAmountMinorUnits,
    string? Currency);

public sealed record StatutoryDiscountZeroPayablePolicyAnchor(
    Guid? DecisionCommandId,
    Guid? PolicyVersionId,
    string? AuthorityEntitlementType,
    string? AuthorityBenefitType,
    string? PolicyEntitlementType,
    string? PolicyBenefitType,
    string? PolicyEffectSupportStatus,
    bool? FullFeeExempt,
    Guid? PolicySiteId,
    Guid? PolicySiteGroupId);

public sealed record StatutoryDiscountZeroPayableParkingAnchor(
    Guid? ParkingSessionId,
    Guid? SiteId,
    Guid? SiteGroupId);

public sealed record StatutoryDiscountZeroPayableTariffAnchor(
    Guid? TariffSnapshotId,
    Guid? ParkingSessionId,
    string? SnapshotStatus,
    long? GrossAmountMinorUnits,
    long? StatutoryDiscountAmountMinorUnits,
    long? NetAmountMinorUnits,
    string? Currency,
    Guid? StatutoryDiscountValidationId);

public sealed record StatutoryDiscountZeroPayableFinalityCandidate(
    StatutoryDiscountZeroPayableDecisionAnchor Decision,
    StatutoryDiscountZeroPayableApplicationCommandAnchor ApplicationCommand,
    StatutoryDiscountZeroPayableApplicationAnchor Application,
    StatutoryDiscountZeroPayableValidationAnchor Validation,
    StatutoryDiscountZeroPayablePolicyAnchor Policy,
    StatutoryDiscountZeroPayableParkingAnchor ParkingSession,
    StatutoryDiscountZeroPayableTariffAnchor OriginalTariff,
    StatutoryDiscountZeroPayableTariffAnchor AppliedTariff,
    bool HasConflictingPaymentAttempt);

public sealed record StatutoryDiscountZeroPayableFinalityResolution(
    StatutoryDiscountZeroPayableFinality? Finality,
    string? RejectionCode)
{
    public bool IsAvailable => Finality is not null;
}

public interface IStatutoryDiscountZeroPayableFinalityReader
{
    Task<StatutoryDiscountZeroPayableFinalityCandidate?> ReadAsync(
        Guid statutoryDiscountDecisionCommandId,
        CancellationToken cancellationToken);
}

public static class StatutoryDiscountZeroPayableFinalityResolver
{
    private const string FullFeeExemption = "FULL_FEE_EXEMPTION";
    private const string SupportedCalculation = "SUPPORTED_BY_CURRENT_CALCULATION";

    public static StatutoryDiscountZeroPayableFinalityResolution Resolve(
        StatutoryDiscountZeroPayableFinalityCandidate? candidate)
    {
        if (candidate is null)
        {
            return Rejected("ZERO_PAYABLE_STATUTORY_FINALITY_NOT_FOUND");
        }

        var decision = candidate.Decision;
        var command = candidate.ApplicationCommand;
        var application = candidate.Application;
        var validation = candidate.Validation;
        var policy = candidate.Policy;
        var parkingSession = candidate.ParkingSession;
        var originalTariff = candidate.OriginalTariff;
        var appliedTariff = candidate.AppliedTariff;

        if (!EqualsCanonical(decision.DecisionResultStatus, StatutoryDiscountDecisionV2ResultStates.Approved))
        {
            return Rejected("ZERO_PAYABLE_STATUTORY_DECISION_NOT_APPROVED");
        }

        if (decision.EntitlementType is not ("SENIOR_CITIZEN" or "PWD"))
        {
            return Rejected("ZERO_PAYABLE_STATUTORY_ENTITLEMENT_NOT_SUPPORTED");
        }

        if (!EqualsCanonical(command.CommandStatus, StatutoryDiscountPayableBasisApplicationV1CommandStates.Applied) ||
            !EqualsCanonical(application.ApplicationStatus, "APPLIED"))
        {
            return Rejected("ZERO_PAYABLE_STATUTORY_APPLICATION_NOT_APPLIED");
        }

        if (!EqualsCanonical(validation.ValidationStatus, "APPROVED"))
        {
            return Rejected("ZERO_PAYABLE_STATUTORY_VALIDATION_NOT_APPROVED");
        }

        if (!EqualsCanonical(policy.AuthorityBenefitType, FullFeeExemption) ||
            !EqualsCanonical(policy.PolicyBenefitType, FullFeeExemption))
        {
            return Rejected("ZERO_PAYABLE_STATUTORY_BENEFIT_NOT_FULL_FEE_EXEMPTION");
        }

        if (policy.FullFeeExempt is not true ||
            !EqualsCanonical(policy.PolicyEffectSupportStatus, SupportedCalculation))
        {
            return Rejected("ZERO_PAYABLE_STATUTORY_POLICY_NOT_SUPPORTED");
        }

        if (application.FinalPayableAmountMinorUnits is not 0 ||
            command.ApprovedFinalPayableAmountMinorUnits != 0 ||
            validation.FinalPayableAmountMinorUnits is not 0 ||
            appliedTariff.NetAmountMinorUnits is not 0)
        {
            return Rejected("ZERO_PAYABLE_STATUTORY_FINAL_AMOUNT_NOT_ZERO");
        }

        if (application.GrossAmountMinorUnits is not > 0)
        {
            return Rejected("ZERO_PAYABLE_STATUTORY_ORIGINAL_AMOUNT_NOT_POSITIVE");
        }

        var linkageError = ResolveLinkageError(candidate);
        if (linkageError is not null)
        {
            return Rejected(linkageError);
        }

        if (!HasMatchingDecisionFinancialFacts(candidate))
        {
            return Rejected("ZERO_PAYABLE_STATUTORY_DECISION_FINANCIAL_FACTS_MISMATCH");
        }

        if (!HasMatchingAmounts(candidate))
        {
            return Rejected("ZERO_PAYABLE_STATUTORY_AMOUNT_MISMATCH");
        }

        if (candidate.HasConflictingPaymentAttempt)
        {
            return Rejected("ZERO_PAYABLE_STATUTORY_PAYMENT_CONFLICT");
        }

        var originalAmount = application.GrossAmountMinorUnits.Value;
        var finalPayableAmount = application.FinalPayableAmountMinorUnits.Value;
        return new StatutoryDiscountZeroPayableFinalityResolution(
            new StatutoryDiscountZeroPayableFinality(
                decision.ParkingSessionId,
                decision.DecisionCommandId,
                command.ApplicationCommandId,
                application.ValidationId!.Value,
                decision.AppliedPolicyReferenceId!.Value,
                application.OriginalTariffSnapshotId!.Value,
                application.AppliedTariffSnapshotId!.Value,
                parkingSession.SiteId!.Value,
                parkingSession.SiteGroupId!.Value,
                decision.EntitlementType,
                FullFeeExemption,
                originalAmount,
                originalAmount - finalPayableAmount,
                application.VatAmountMinorUnits!.Value,
                finalPayableAmount,
                application.Currency!,
                decision.SourceChannel,
                decision.DecidedAt!.Value,
                application.AppliedAt!.Value,
                application.CorrelationId!.Value),
            RejectionCode: null);
    }

    private static string? ResolveLinkageError(StatutoryDiscountZeroPayableFinalityCandidate candidate)
    {
        var decision = candidate.Decision;
        var command = candidate.ApplicationCommand;
        var application = candidate.Application;
        var validation = candidate.Validation;
        var policy = candidate.Policy;
        var parkingSession = candidate.ParkingSession;
        var originalTariff = candidate.OriginalTariff;
        var appliedTariff = candidate.AppliedTariff;

        if (command.DecisionCommandId != decision.DecisionCommandId ||
            policy.DecisionCommandId != decision.DecisionCommandId)
        {
            return "ZERO_PAYABLE_STATUTORY_DECISION_LINKAGE_MISMATCH";
        }

        if (command.ParkingSessionId != decision.ParkingSessionId ||
            application.ParkingSessionId != decision.ParkingSessionId ||
            validation.ParkingSessionId != decision.ParkingSessionId ||
            parkingSession.ParkingSessionId != decision.ParkingSessionId ||
            originalTariff.ParkingSessionId != decision.ParkingSessionId ||
            appliedTariff.ParkingSessionId != decision.ParkingSessionId)
        {
            return "ZERO_PAYABLE_STATUTORY_PARKING_SESSION_LINKAGE_MISMATCH";
        }

        if (!parkingSession.SiteId.HasValue ||
            !parkingSession.SiteGroupId.HasValue ||
            command.SiteId != parkingSession.SiteId ||
            (policy.PolicySiteId.HasValue && policy.PolicySiteId != parkingSession.SiteId) ||
            (policy.PolicySiteGroupId.HasValue && policy.PolicySiteGroupId != parkingSession.SiteGroupId))
        {
            return "ZERO_PAYABLE_STATUTORY_SITE_SCOPE_MISMATCH";
        }

        if (!decision.ValidationId.HasValue ||
            decision.ValidationId != command.ValidationId ||
            command.ValidationId != application.ValidationId ||
            application.ValidationId != validation.ValidationId ||
            appliedTariff.StatutoryDiscountValidationId != validation.ValidationId)
        {
            return "ZERO_PAYABLE_STATUTORY_VALIDATION_LINKAGE_MISMATCH";
        }

        if (!command.ApplicationId.HasValue || command.ApplicationId != application.ApplicationId)
        {
            return "ZERO_PAYABLE_STATUTORY_APPLICATION_LINKAGE_MISMATCH";
        }

        if (!command.OriginalTariffSnapshotId.HasValue ||
            command.OriginalTariffSnapshotId != application.OriginalTariffSnapshotId ||
            application.OriginalTariffSnapshotId != originalTariff.TariffSnapshotId ||
            !command.AppliedTariffSnapshotId.HasValue ||
            command.AppliedTariffSnapshotId != application.AppliedTariffSnapshotId ||
            application.AppliedTariffSnapshotId != appliedTariff.TariffSnapshotId ||
            validation.TariffSnapshotId != appliedTariff.TariffSnapshotId ||
            !EqualsCanonical(originalTariff.SnapshotStatus, "SUPERSEDED") ||
            !EqualsCanonical(appliedTariff.SnapshotStatus, "ACTIVE"))
        {
            return "ZERO_PAYABLE_STATUTORY_TARIFF_LINKAGE_MISMATCH";
        }

        if (!policy.PolicyVersionId.HasValue)
        {
            return "ZERO_PAYABLE_STATUTORY_POLICY_AUTHORITY_MISSING";
        }

        if (!HasCompatiblePolicyVersionLinkage(command.PolicyVersionId, validation.PolicyVersionId, policy.PolicyVersionId.Value))
        {
            return "ZERO_PAYABLE_STATUTORY_POLICY_VERSION_LINKAGE_MISMATCH";
        }

        if (!decision.AppliedPolicyReferenceId.HasValue ||
            command.AppliedPolicyReferenceId != decision.AppliedPolicyReferenceId ||
            validation.AppliedPolicyReferenceId != decision.AppliedPolicyReferenceId)
        {
            return "ZERO_PAYABLE_STATUTORY_APPLIED_POLICY_LINKAGE_MISMATCH";
        }

        if (!EqualsCanonical(decision.EntitlementType, command.EntitlementType) ||
            !EqualsCanonical(decision.EntitlementType, validation.EntitlementType) ||
            !EqualsCanonical(decision.EntitlementType, policy.AuthorityEntitlementType) ||
            !EqualsCanonical(decision.EntitlementType, policy.PolicyEntitlementType))
        {
            return "ZERO_PAYABLE_STATUTORY_ENTITLEMENT_LINKAGE_MISMATCH";
        }

        return !EqualsCanonical(decision.SourceChannel, command.SourceChannel) ||
               command.CorrelationId != application.CorrelationId ||
               !decision.DecidedAt.HasValue ||
               !command.AppliedAt.HasValue ||
               !application.AppliedAt.HasValue ||
               !application.CorrelationId.HasValue
            ? "ZERO_PAYABLE_STATUTORY_FINALITY_FACTS_INCOMPLETE"
            : null;
    }

    private static bool HasCompatiblePolicyVersionLinkage(
        Guid? commandPolicyVersionId,
        Guid? validationPolicyVersionId,
        Guid authorityPolicyVersionId)
    {
        if (commandPolicyVersionId.HasValue || validationPolicyVersionId.HasValue)
        {
            return commandPolicyVersionId == authorityPolicyVersionId &&
                   validationPolicyVersionId == authorityPolicyVersionId;
        }

        // Canonical records created before policy-version linkage was populated carry neither reference.
        // Compatibility is limited to that exact both-null shape; partial or mismatched references fail closed.
        return true;
    }

    private static bool HasMatchingDecisionFinancialFacts(
        StatutoryDiscountZeroPayableFinalityCandidate candidate)
    {
        var decision = candidate.Decision;
        var command = candidate.ApplicationCommand;
        var application = candidate.Application;
        var validation = candidate.Validation;
        var originalTariff = candidate.OriginalTariff;
        var appliedTariff = candidate.AppliedTariff;

        return decision.GrossAmountMinorUnits.HasValue &&
               decision.GrossAmountMinorUnits == application.GrossAmountMinorUnits &&
               decision.GrossAmountMinorUnits == validation.GrossAmountMinorUnits &&
               decision.GrossAmountMinorUnits == originalTariff.NetAmountMinorUnits &&
               decision.GrossAmountMinorUnits == appliedTariff.GrossAmountMinorUnits &&
               decision.VatAmountMinorUnits.HasValue &&
               decision.VatAmountMinorUnits == application.VatAmountMinorUnits &&
               decision.VatAmountMinorUnits == command.ApprovedVatAmountMinorUnits &&
               decision.StatutoryDiscountAmountMinorUnits.HasValue &&
               decision.StatutoryDiscountAmountMinorUnits == application.StatutoryDiscountAmountMinorUnits &&
               decision.StatutoryDiscountAmountMinorUnits == command.ApprovedDiscountAmountMinorUnits &&
               decision.StatutoryDiscountAmountMinorUnits == validation.StatutoryDiscountAmountMinorUnits &&
               decision.StatutoryDiscountAmountMinorUnits == appliedTariff.StatutoryDiscountAmountMinorUnits &&
               decision.FinalPayableAmountMinorUnits.HasValue &&
               decision.FinalPayableAmountMinorUnits == application.FinalPayableAmountMinorUnits &&
               decision.FinalPayableAmountMinorUnits == command.ApprovedFinalPayableAmountMinorUnits &&
               decision.FinalPayableAmountMinorUnits == validation.FinalPayableAmountMinorUnits &&
               decision.FinalPayableAmountMinorUnits == appliedTariff.NetAmountMinorUnits &&
               EqualsCanonical(decision.Currency, application.Currency) &&
               EqualsCanonical(decision.Currency, command.Currency) &&
               EqualsCanonical(decision.Currency, validation.Currency) &&
               EqualsCanonical(decision.Currency, originalTariff.Currency) &&
               EqualsCanonical(decision.Currency, appliedTariff.Currency);
    }

    private static bool HasMatchingAmounts(StatutoryDiscountZeroPayableFinalityCandidate candidate)
    {
        var command = candidate.ApplicationCommand;
        var application = candidate.Application;
        var validation = candidate.Validation;
        var originalTariff = candidate.OriginalTariff;
        var appliedTariff = candidate.AppliedTariff;

        return application.GrossAmountMinorUnits == validation.GrossAmountMinorUnits &&
               application.GrossAmountMinorUnits == originalTariff.NetAmountMinorUnits &&
               application.GrossAmountMinorUnits == appliedTariff.GrossAmountMinorUnits &&
               application.VatAmountMinorUnits.HasValue &&
               application.VatAmountMinorUnits == command.ApprovedVatAmountMinorUnits &&
               application.StatutoryDiscountAmountMinorUnits == command.ApprovedDiscountAmountMinorUnits &&
               application.StatutoryDiscountAmountMinorUnits == validation.StatutoryDiscountAmountMinorUnits &&
               application.StatutoryDiscountAmountMinorUnits == appliedTariff.StatutoryDiscountAmountMinorUnits &&
               application.StatutoryDiscountAmountMinorUnits + application.VatAmountMinorUnits ==
                   application.GrossAmountMinorUnits &&
               EqualsCanonical(application.Currency, command.Currency) &&
               EqualsCanonical(application.Currency, validation.Currency) &&
               EqualsCanonical(application.Currency, originalTariff.Currency) &&
               EqualsCanonical(application.Currency, appliedTariff.Currency);
    }

    private static bool EqualsCanonical(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static StatutoryDiscountZeroPayableFinalityResolution Rejected(string code) => new(null, code);
}
