using ExitPass.CentralPms.Application.StatutoryDiscounts;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class StatutoryDiscountZeroPayableFinalityResolverTests
{
    private static readonly Guid DecisionId = Guid.Parse("8a000000-0000-0000-0000-000000000001");
    private static readonly Guid ParkingSessionId = Guid.Parse("8a000000-0000-0000-0000-000000000002");
    private static readonly Guid SiteId = Guid.Parse("8a000000-0000-0000-0000-000000000003");
    private static readonly Guid SiteGroupId = Guid.Parse("8a000000-0000-0000-0000-000000000004");
    private static readonly Guid ValidationId = Guid.Parse("8a000000-0000-0000-0000-000000000005");
    private static readonly Guid PolicyId = Guid.Parse("8a000000-0000-0000-0000-000000000006");
    private static readonly Guid LegacyPolicyReferenceId = Guid.Parse("8a000000-0000-0000-0000-00000000000c");
    private static readonly Guid ApplicationCommandId = Guid.Parse("8a000000-0000-0000-0000-000000000007");
    private static readonly Guid ApplicationId = Guid.Parse("8a000000-0000-0000-0000-000000000008");
    private static readonly Guid OriginalTariffId = Guid.Parse("8a000000-0000-0000-0000-000000000009");
    private static readonly Guid AppliedTariffId = Guid.Parse("8a000000-0000-0000-0000-00000000000a");
    private static readonly Guid CorrelationId = Guid.Parse("8a000000-0000-0000-0000-00000000000b");
    private static readonly DateTimeOffset DecidedAt = DateTimeOffset.Parse("2026-09-08T01:00:00Z");
    private static readonly DateTimeOffset AppliedAt = DateTimeOffset.Parse("2026-09-08T01:01:00Z");

    [Fact]
    public void Resolve_WhenApprovedAppliedFullFeeBasisIsZero_ReturnsCanonicalFinality()
    {
        var result = StatutoryDiscountZeroPayableFinalityResolver.Resolve(Candidate());

        result.IsAvailable.Should().BeTrue();
        result.RejectionCode.Should().BeNull();
        result.Finality.Should().BeEquivalentTo(new StatutoryDiscountZeroPayableFinality(
            ParkingSessionId,
            DecisionId,
            ApplicationCommandId,
            ValidationId,
            LegacyPolicyReferenceId,
            OriginalTariffId,
            AppliedTariffId,
            SiteId,
            SiteGroupId,
            "SENIOR_CITIZEN",
            "FULL_FEE_EXEMPTION",
            3000,
            3000,
            321,
            0,
            "PHP",
            "WEBPAY",
            DecidedAt,
            AppliedAt,
            CorrelationId));
    }

    [Fact]
    public void Resolve_WhenReadRepeated_ReturnsSameFactsWithoutMutation()
    {
        var candidate = Candidate();

        var first = StatutoryDiscountZeroPayableFinalityResolver.Resolve(candidate);
        var replay = StatutoryDiscountZeroPayableFinalityResolver.Resolve(candidate);

        replay.Should().BeEquivalentTo(first);
        candidate.HasConflictingPaymentAttempt.Should().BeFalse();
    }

    [Fact]
    public void Resolve_WhenEntitlementIsPwd_ReturnsCanonicalFinality()
    {
        var original = Candidate();
        var candidate = original with
        {
            Decision = original.Decision with { EntitlementType = "PWD" },
            ApplicationCommand = original.ApplicationCommand with { EntitlementType = "PWD" },
            Validation = original.Validation with { EntitlementType = "PWD" },
            Policy = original.Policy with
            {
                AuthorityEntitlementType = "PWD",
                PolicyEntitlementType = "PWD"
            }
        };

        var result = StatutoryDiscountZeroPayableFinalityResolver.Resolve(candidate);

        result.IsAvailable.Should().BeTrue();
        result.Finality!.EntitlementType.Should().Be("PWD");
    }

    [Fact]
    public void Resolve_WhenEntitlementIsUnsupported_Rejects()
    {
        var candidate = Candidate() with
        {
            Decision = Candidate().Decision with { EntitlementType = "OTHER" }
        };

        AssertRejected(candidate, "ZERO_PAYABLE_STATUTORY_ENTITLEMENT_NOT_SUPPORTED");
    }

    [Fact]
    public void Resolve_WhenFinalPayableIsPositive_Rejects()
    {
        var candidate = Candidate() with
        {
            Decision = Candidate().Decision with { FinalPayableAmountMinorUnits = 100 },
            ApplicationCommand = Candidate().ApplicationCommand with { ApprovedFinalPayableAmountMinorUnits = 100 },
            Application = Candidate().Application with { FinalPayableAmountMinorUnits = 100 },
            Validation = Candidate().Validation with { FinalPayableAmountMinorUnits = 100 },
            AppliedTariff = Candidate().AppliedTariff with { NetAmountMinorUnits = 100 }
        };

        AssertRejected(candidate, "ZERO_PAYABLE_STATUTORY_FINAL_AMOUNT_NOT_ZERO");
    }

    [Theory]
    [InlineData("NOT_DECIDED")]
    [InlineData("REJECTED")]
    public void Resolve_WhenDecisionIsNotApproved_Rejects(string decisionStatus)
    {
        var candidate = Candidate() with { Decision = Candidate().Decision with { DecisionResultStatus = decisionStatus } };

        AssertRejected(candidate, "ZERO_PAYABLE_STATUTORY_DECISION_NOT_APPROVED");
    }

    [Fact]
    public void Resolve_WhenApplicationIsNotApplied_Rejects()
    {
        var candidate = Candidate() with
        {
            ApplicationCommand = Candidate().ApplicationCommand with { CommandStatus = "PROCESSING" },
            Application = Candidate().Application with { ApplicationStatus = "REQUESTED" }
        };

        AssertRejected(candidate, "ZERO_PAYABLE_STATUTORY_APPLICATION_NOT_APPLIED");
    }

    [Fact]
    public void Resolve_WhenValidationIsNotApproved_Rejects()
    {
        var candidate = Candidate() with
        {
            Validation = Candidate().Validation with { ValidationStatus = "PENDING_OPERATOR_REVIEW" }
        };

        AssertRejected(candidate, "ZERO_PAYABLE_STATUTORY_VALIDATION_NOT_APPROVED");
    }

    [Fact]
    public void Resolve_WhenBenefitIsNotFullFeeExemption_Rejects()
    {
        var candidate = Candidate() with
        {
            Policy = Candidate().Policy with
            {
                AuthorityBenefitType = "STATUTORY_DISCOUNT_VAT_EXEMPT",
                PolicyBenefitType = "STATUTORY_DISCOUNT_VAT_EXEMPT"
            }
        };

        AssertRejected(candidate, "ZERO_PAYABLE_STATUTORY_BENEFIT_NOT_FULL_FEE_EXEMPTION");
    }

    [Fact]
    public void Resolve_WhenPolicyLinkageDiffers_Rejects()
    {
        var candidate = Candidate() with
        {
            ApplicationCommand = Candidate().ApplicationCommand with { AppliedPolicyReferenceId = Guid.NewGuid() }
        };

        AssertRejected(candidate, "ZERO_PAYABLE_STATUTORY_APPLIED_POLICY_LINKAGE_MISMATCH");
    }

    [Fact]
    public void Resolve_WhenParkingSessionDiffers_Rejects()
    {
        var candidate = Candidate() with
        {
            Validation = Candidate().Validation with { ParkingSessionId = Guid.NewGuid() }
        };

        AssertRejected(candidate, "ZERO_PAYABLE_STATUTORY_PARKING_SESSION_LINKAGE_MISMATCH");
    }

    [Fact]
    public void Resolve_WhenAppliedTariffDiffersFromApplication_Rejects()
    {
        var candidate = Candidate() with
        {
            AppliedTariff = Candidate().AppliedTariff with { TariffSnapshotId = Guid.NewGuid() }
        };

        AssertRejected(candidate, "ZERO_PAYABLE_STATUTORY_TARIFF_LINKAGE_MISMATCH");
    }

    [Fact]
    public void Resolve_WhenSiteScopeDiffers_Rejects()
    {
        var candidate = Candidate() with
        {
            ApplicationCommand = Candidate().ApplicationCommand with { SiteId = Guid.NewGuid() }
        };

        AssertRejected(candidate, "ZERO_PAYABLE_STATUTORY_SITE_SCOPE_MISMATCH");
    }

    [Fact]
    public void Resolve_WhenAnyPaymentAttemptExistsForAppliedTariff_Rejects()
    {
        var candidate = Candidate() with { HasConflictingPaymentAttempt = true };

        AssertRejected(candidate, "ZERO_PAYABLE_STATUTORY_PAYMENT_CONFLICT");
    }

    [Fact]
    public void Resolve_WhenDecisionGrossDiffersFromCanonicalAppliedFacts_Rejects()
    {
        var candidate = Candidate() with
        {
            Decision = Candidate().Decision with { GrossAmountMinorUnits = 2999 }
        };

        AssertRejected(candidate, "ZERO_PAYABLE_STATUTORY_DECISION_FINANCIAL_FACTS_MISMATCH");
    }

    [Fact]
    public void Resolve_WhenDecisionVatDiffersFromCanonicalAppliedFacts_Rejects()
    {
        var candidate = Candidate() with
        {
            Decision = Candidate().Decision with { VatAmountMinorUnits = 320 }
        };

        AssertRejected(candidate, "ZERO_PAYABLE_STATUTORY_DECISION_FINANCIAL_FACTS_MISMATCH");
    }

    [Fact]
    public void Resolve_WhenDecisionDiscountDiffersFromCanonicalAppliedFacts_Rejects()
    {
        var candidate = Candidate() with
        {
            Decision = Candidate().Decision with { StatutoryDiscountAmountMinorUnits = 2678 }
        };

        AssertRejected(candidate, "ZERO_PAYABLE_STATUTORY_DECISION_FINANCIAL_FACTS_MISMATCH");
    }

    [Fact]
    public void Resolve_WhenDecisionFinalAmountDiffersFromCanonicalAppliedFacts_Rejects()
    {
        var candidate = Candidate() with
        {
            Decision = Candidate().Decision with { FinalPayableAmountMinorUnits = 1 }
        };

        AssertRejected(candidate, "ZERO_PAYABLE_STATUTORY_DECISION_FINANCIAL_FACTS_MISMATCH");
    }

    [Fact]
    public void Resolve_WhenDecisionCurrencyDiffersFromCanonicalAppliedFacts_Rejects()
    {
        var candidate = Candidate() with
        {
            Decision = Candidate().Decision with { Currency = "USD" }
        };

        AssertRejected(candidate, "ZERO_PAYABLE_STATUTORY_DECISION_FINANCIAL_FACTS_MISMATCH");
    }

    [Fact]
    public void Resolve_WhenApplicationCommandPolicyVersionDiffersFromAuthority_Rejects()
    {
        var candidate = Candidate() with
        {
            ApplicationCommand = Candidate().ApplicationCommand with { PolicyVersionId = Guid.NewGuid() }
        };

        AssertRejected(candidate, "ZERO_PAYABLE_STATUTORY_POLICY_VERSION_LINKAGE_MISMATCH");
    }

    [Fact]
    public void Resolve_WhenValidationPolicyVersionDiffersFromAuthority_Rejects()
    {
        var candidate = Candidate() with
        {
            Validation = Candidate().Validation with { PolicyVersionId = Guid.NewGuid() }
        };

        AssertRejected(candidate, "ZERO_PAYABLE_STATUTORY_POLICY_VERSION_LINKAGE_MISMATCH");
    }

    [Fact]
    public void Resolve_WhenOnlyOnePolicyVersionReferenceIsNull_Rejects()
    {
        var candidate = Candidate() with
        {
            ApplicationCommand = Candidate().ApplicationCommand with { PolicyVersionId = null }
        };

        AssertRejected(candidate, "ZERO_PAYABLE_STATUTORY_POLICY_VERSION_LINKAGE_MISMATCH");
    }

    [Fact]
    public void Resolve_WhenBothLegacyPolicyVersionReferencesAreNull_AcceptsNarrowCompatibilityShape()
    {
        var original = Candidate();
        var candidate = original with
        {
            ApplicationCommand = original.ApplicationCommand with { PolicyVersionId = null },
            Validation = original.Validation with { PolicyVersionId = null }
        };

        StatutoryDiscountZeroPayableFinalityResolver.Resolve(candidate).IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void Resolve_WhenPolicyIsNotCalculationSupported_Rejects()
    {
        var candidate = Candidate() with
        {
            Policy = Candidate().Policy with { PolicyEffectSupportStatus = "NOT_SUPPORTED_BY_CURRENT_CALCULATION" }
        };

        AssertRejected(candidate, "ZERO_PAYABLE_STATUTORY_POLICY_NOT_SUPPORTED");
    }

    [Fact]
    public void Resolve_WhenPolicyIsNotFullFeeExempt_Rejects()
    {
        var candidate = Candidate() with
        {
            Policy = Candidate().Policy with { FullFeeExempt = false }
        };

        AssertRejected(candidate, "ZERO_PAYABLE_STATUTORY_POLICY_NOT_SUPPORTED");
    }

    [Fact]
    public void Resolve_WhenPolicyAuthorityIsMissing_Rejects()
    {
        var candidate = Candidate() with
        {
            Policy = Candidate().Policy with { PolicyVersionId = null }
        };

        AssertRejected(candidate, "ZERO_PAYABLE_STATUTORY_POLICY_AUTHORITY_MISSING");
    }

    [Fact]
    public void Resolve_WhenFullWaiverAmountsDoNotReconcile_Rejects()
    {
        var original = Candidate();
        var candidate = original with
        {
            Decision = original.Decision with { VatAmountMinorUnits = 320 },
            ApplicationCommand = original.ApplicationCommand with { ApprovedVatAmountMinorUnits = 320 },
            Application = original.Application with { VatAmountMinorUnits = 320 }
        };

        AssertRejected(candidate, "ZERO_PAYABLE_STATUTORY_AMOUNT_MISMATCH");
    }

    [Fact]
    public void Resolve_WhenOriginalAmountIsZero_Rejects()
    {
        var candidate = Candidate() with
        {
            Application = Candidate().Application with { GrossAmountMinorUnits = 0 }
        };

        AssertRejected(candidate, "ZERO_PAYABLE_STATUTORY_ORIGINAL_AMOUNT_NOT_POSITIVE");
    }

    private static void AssertRejected(StatutoryDiscountZeroPayableFinalityCandidate candidate, string code)
    {
        var result = StatutoryDiscountZeroPayableFinalityResolver.Resolve(candidate);

        result.IsAvailable.Should().BeFalse();
        result.Finality.Should().BeNull();
        result.RejectionCode.Should().Be(code);
    }

    private static StatutoryDiscountZeroPayableFinalityCandidate Candidate() =>
        new(
            new StatutoryDiscountZeroPayableDecisionAnchor(
                DecisionId,
                ParkingSessionId,
                ValidationId,
                LegacyPolicyReferenceId,
                "APPROVED",
                "SENIOR_CITIZEN",
                "WEBPAY",
                3000,
                321,
                2679,
                0,
                "PHP",
                DecidedAt),
            new StatutoryDiscountZeroPayableApplicationCommandAnchor(
                ApplicationCommandId,
                DecisionId,
                ParkingSessionId,
                SiteId,
                ValidationId,
                ApplicationId,
                OriginalTariffId,
                AppliedTariffId,
                LegacyPolicyReferenceId,
                PolicyId,
                "APPLIED",
                "SENIOR_CITIZEN",
                2679,
                321,
                0,
                "PHP",
                "WEBPAY",
                CorrelationId,
                AppliedAt),
            new StatutoryDiscountZeroPayableApplicationAnchor(
                ApplicationId,
                ValidationId,
                ParkingSessionId,
                OriginalTariffId,
                AppliedTariffId,
                "APPLIED",
                3000,
                321,
                2679,
                0,
                "PHP",
                AppliedAt,
                CorrelationId),
            new StatutoryDiscountZeroPayableValidationAnchor(
                ValidationId,
                ParkingSessionId,
                AppliedTariffId,
                LegacyPolicyReferenceId,
                PolicyId,
                "APPROVED",
                "SENIOR_CITIZEN",
                3000,
                2679,
                0,
                "PHP"),
            new StatutoryDiscountZeroPayablePolicyAnchor(
                DecisionId,
                PolicyId,
                "SENIOR_CITIZEN",
                "FULL_FEE_EXEMPTION",
                "SENIOR_CITIZEN",
                "FULL_FEE_EXEMPTION",
                "SUPPORTED_BY_CURRENT_CALCULATION",
                true,
                SiteId,
                SiteGroupId),
            new StatutoryDiscountZeroPayableParkingAnchor(ParkingSessionId, SiteId, SiteGroupId),
            new StatutoryDiscountZeroPayableTariffAnchor(
                OriginalTariffId,
                ParkingSessionId,
                "SUPERSEDED",
                3000,
                0,
                3000,
                "PHP",
                null),
            new StatutoryDiscountZeroPayableTariffAnchor(
                AppliedTariffId,
                ParkingSessionId,
                "ACTIVE",
                3000,
                2679,
                0,
                "PHP",
                ValidationId),
            HasConflictingPaymentAttempt: false);
}
