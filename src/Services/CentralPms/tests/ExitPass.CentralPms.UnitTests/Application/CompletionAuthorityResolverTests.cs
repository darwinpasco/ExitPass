using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class CompletionAuthorityResolverTests
{
    private static readonly Guid ParkingSessionId = Guid.Parse("9a000000-0000-0000-0000-000000000001");
    private static readonly Guid TariffSnapshotId = Guid.Parse("9a000000-0000-0000-0000-000000000002");
    private static readonly Guid SiteId = Guid.Parse("9a000000-0000-0000-0000-000000000003");
    private static readonly Guid SiteGroupId = Guid.Parse("9a000000-0000-0000-0000-000000000004");
    private static readonly Guid PaymentAttemptId = Guid.Parse("9a000000-0000-0000-0000-000000000005");
    private static readonly Guid PaymentConfirmationId = Guid.Parse("9a000000-0000-0000-0000-000000000006");
    private static readonly Guid CorrelationId = Guid.Parse("9a000000-0000-0000-0000-000000000007");
    private static readonly DateTimeOffset EstablishedAt = DateTimeOffset.Parse("2026-09-08T10:00:00Z");

    [Fact]
    public void ResolvePaymentFinality_WhenConfirmedAndReconciled_EstablishesAuthority()
    {
        var result = CompletionAuthorityResolver.ResolvePaymentFinality(
            PaymentCandidate(),
            ParkingSessionId,
            PaymentAttemptId);

        result.IsEstablished.Should().BeTrue();
        result.Authority.Should().NotBeNull();
        result.Authority!.CompletionBasis.Should().Be(CompletionBasisCodes.PaymentFinality);
        result.Authority.PaymentAttemptId.Should().Be(PaymentAttemptId);
        result.Authority.PaymentConfirmationId.Should().Be(PaymentConfirmationId);
        result.Authority.TariffSnapshotId.Should().Be(TariffSnapshotId);
        result.Authority.DurableSourceReferenceId.Should().Be(PaymentConfirmationId);
    }

    [Theory]
    [InlineData("REQUESTED")]
    [InlineData("PENDING_PROVIDER")]
    [InlineData("FAILED")]
    [InlineData("CANCELLED")]
    [InlineData("EXPIRED")]
    public void ResolvePaymentFinality_WhenAttemptIsNotConfirmed_Rejects(string status)
    {
        var result = CompletionAuthorityResolver.ResolvePaymentFinality(
            PaymentCandidate() with { AttemptStatus = status },
            ParkingSessionId,
            PaymentAttemptId);

        result.IsEstablished.Should().BeFalse();
        result.RejectionCode.Should().Be("PAYMENT_FINALITY_NOT_VERIFIED");
    }

    [Fact]
    public void ResolvePaymentFinality_WhenSessionDiffers_Rejects()
    {
        var result = CompletionAuthorityResolver.ResolvePaymentFinality(
            PaymentCandidate(),
            Guid.NewGuid(),
            PaymentAttemptId);

        result.RejectionCode.Should().Be("PAYMENT_FINALITY_PARKING_SESSION_MISMATCH");
    }

    [Fact]
    public void ResolvePaymentFinality_WhenTariffAmountDiffers_Rejects()
    {
        var result = CompletionAuthorityResolver.ResolvePaymentFinality(
            PaymentCandidate() with { TariffNetAmountMinorUnits = 2999 },
            ParkingSessionId,
            PaymentAttemptId);

        result.RejectionCode.Should().Be("PAYMENT_FINALITY_AMOUNT_MISMATCH");
    }

    [Fact]
    public void ResolveZeroPayableStatutoryFinality_WhenCanonical_EstablishesNonPaymentAuthority()
    {
        var finality = ZeroPayableFinality();
        var result = CompletionAuthorityResolver.ResolveZeroPayableStatutoryFinality(
            new StatutoryDiscountZeroPayableFinalityResolution(finality, null),
            ParkingSessionId,
            TariffSnapshotId,
            SiteId,
            SiteGroupId);

        result.IsEstablished.Should().BeTrue();
        result.Authority!.CompletionBasis.Should().Be(CompletionBasisCodes.ZeroPayableStatutoryFinality);
        result.Authority.PaymentAttemptId.Should().BeNull();
        result.Authority.PaymentConfirmationId.Should().BeNull();
        result.Authority.FinalPayableAmountMinorUnits.Should().Be(0);
    }

    [Fact]
    public void ResolveZeroPayableStatutoryFinality_WhenReadRepeated_ReturnsSameAuthority()
    {
        var resolution = new StatutoryDiscountZeroPayableFinalityResolution(ZeroPayableFinality(), null);

        var first = CompletionAuthorityResolver.ResolveZeroPayableStatutoryFinality(
            resolution, ParkingSessionId, TariffSnapshotId, SiteId, SiteGroupId);
        var replay = CompletionAuthorityResolver.ResolveZeroPayableStatutoryFinality(
            resolution, ParkingSessionId, TariffSnapshotId, SiteId, SiteGroupId);

        replay.Should().BeEquivalentTo(first);
    }

    [Fact]
    public void ResolveZeroPayableStatutoryFinality_WhenSessionDiffers_Rejects()
    {
        var result = ResolveZero(expectedParkingSessionId: Guid.NewGuid());

        result.RejectionCode.Should().Be("ZERO_PAYABLE_COMPLETION_PARKING_SESSION_MISMATCH");
    }

    [Fact]
    public void ResolveZeroPayableStatutoryFinality_WhenTariffDiffers_Rejects()
    {
        var result = ResolveZero(expectedTariffSnapshotId: Guid.NewGuid());

        result.RejectionCode.Should().Be("ZERO_PAYABLE_COMPLETION_TARIFF_MISMATCH");
    }

    [Fact]
    public void ResolveZeroPayableStatutoryFinality_WhenSiteScopeDiffers_Rejects()
    {
        var result = ResolveZero(expectedSiteId: Guid.NewGuid());

        result.RejectionCode.Should().Be("ZERO_PAYABLE_COMPLETION_SITE_SCOPE_MISMATCH");
    }

    [Fact]
    public void ResolveZeroPayableStatutoryFinality_WhenSiteGroupScopeDiffers_Rejects()
    {
        var result = ResolveZero(expectedSiteGroupId: Guid.NewGuid());

        result.RejectionCode.Should().Be("ZERO_PAYABLE_COMPLETION_SITE_SCOPE_MISMATCH");
    }

    [Fact]
    public void EvaluateZeroPayableExitAuthorizationEligibility_PreservesFiscalBlock()
    {
        var authority = ResolveZero().Authority!;

        var eligibility = CompletionAuthorityResolver.EvaluateZeroPayableExitAuthorizationEligibility(authority);

        eligibility.CompletionAuthorityEligible.Should().BeTrue();
        eligibility.ExitAuthorizationIssuanceAllowed.Should().BeFalse();
        eligibility.Status.Should().Be(ExitAuthorizationEligibilityStatuses.ZeroPayableCompletionAuthorityReady);
        eligibility.BlockedReason.Should().Be(
            ExitAuthorizationEligibilityBlockedReasons.ZeroPayableFiscalPrerequisiteUnresolved);
        eligibility.CompletionBasis.Should().Be(CompletionBasisCodes.ZeroPayableStatutoryFinality);
    }

    [Fact]
    public void EvaluateZeroPayableExitAuthorizationEligibility_WhenFiscalEvidenceExists_ReportsSatisfiedButDoesNotBypassIssuancePath()
    {
        var authority = ResolveZero().Authority!;

        var eligibility = CompletionAuthorityResolver.EvaluateZeroPayableExitAuthorizationEligibility(
            authority,
            fiscalPrerequisiteSatisfied: true);

        eligibility.CompletionAuthorityEligible.Should().BeTrue();
        eligibility.ExitAuthorizationIssuanceAllowed.Should().BeFalse();
        eligibility.Status.Should().Be(ExitAuthorizationEligibilityStatuses.ZeroPayableFiscalPrerequisiteSatisfied);
        eligibility.BlockedReason.Should().Be(
            ExitAuthorizationEligibilityBlockedReasons.ZeroPayableExitAuthorizationIssuancePathUnavailable);
        eligibility.CompletionBasis.Should().Be(CompletionBasisCodes.ZeroPayableStatutoryFinality);
    }

    [Fact]
    public void EvaluateZeroPayableExitAuthorizationEligibility_WhenFiscalAndIssuancePathExist_AllowsIssuance()
    {
        var authority = ResolveZero().Authority!;

        var eligibility = CompletionAuthorityResolver.EvaluateZeroPayableExitAuthorizationEligibility(
            authority,
            fiscalPrerequisiteSatisfied: true,
            issuancePathAvailable: true);

        eligibility.CompletionAuthorityEligible.Should().BeTrue();
        eligibility.ExitAuthorizationIssuanceAllowed.Should().BeTrue();
        eligibility.Status.Should().Be(ExitAuthorizationEligibilityStatuses.ZeroPayableFiscalPrerequisiteSatisfied);
        eligibility.BlockedReason.Should().BeNull();
        eligibility.CompletionBasis.Should().Be(CompletionBasisCodes.ZeroPayableStatutoryFinality);
    }

    private static CompletionAuthorityResolution ResolveZero(
        Guid? expectedParkingSessionId = null,
        Guid? expectedTariffSnapshotId = null,
        Guid? expectedSiteId = null,
        Guid? expectedSiteGroupId = null) =>
        CompletionAuthorityResolver.ResolveZeroPayableStatutoryFinality(
            new StatutoryDiscountZeroPayableFinalityResolution(ZeroPayableFinality(), null),
            expectedParkingSessionId ?? ParkingSessionId,
            expectedTariffSnapshotId ?? TariffSnapshotId,
            expectedSiteId ?? SiteId,
            expectedSiteGroupId ?? SiteGroupId);

    private static PaymentFinalityCompletionCandidate PaymentCandidate() =>
        new(
            PaymentAttemptId,
            ParkingSessionId,
            TariffSnapshotId,
            SiteId,
            SiteGroupId,
            "CONFIRMED",
            EstablishedAt,
            3000,
            "PHP",
            ParkingSessionId,
            "ACTIVE",
            3000,
            "PHP",
            PaymentConfirmationId,
            "RECORDED",
            3000,
            "PHP",
            EstablishedAt,
            CorrelationId);

    private static StatutoryDiscountZeroPayableFinality ZeroPayableFinality() =>
        new(
            ParkingSessionId,
            Guid.Parse("9a000000-0000-0000-0000-000000000008"),
            Guid.Parse("9a000000-0000-0000-0000-000000000009"),
            Guid.Parse("9a000000-0000-0000-0000-00000000000a"),
            Guid.Parse("9a000000-0000-0000-0000-00000000000b"),
            Guid.Parse("9a000000-0000-0000-0000-00000000000c"),
            TariffSnapshotId,
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
            EstablishedAt.AddMinutes(-1),
            EstablishedAt,
            CorrelationId);
}
