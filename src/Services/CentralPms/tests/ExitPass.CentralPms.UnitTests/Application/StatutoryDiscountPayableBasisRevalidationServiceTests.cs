using ExitPass.CentralPms.Application.StatutoryDiscounts;
using ExitPass.CentralPms.Application.VendorParking;
using ExitPass.CentralPms.Domain.Sessions;
using ExitPass.CentralPms.Domain.Tariffs;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class StatutoryDiscountPayableBasisRevalidationServiceTests
{
    private static readonly Guid DecisionId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid ValidationId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid RequestReference = Guid.Parse("10000000-0000-0000-0000-000000000003");
    private static readonly Guid ParkingSessionId = Guid.Parse("10000000-0000-0000-0000-000000000004");
    private static readonly Guid SiteId = Guid.Parse("10000000-0000-0000-0000-000000000005");
    private static readonly Guid SiteGroupId = Guid.Parse("10000000-0000-0000-0000-000000000006");
    private static readonly Guid VendorSystemId = Guid.Parse("10000000-0000-0000-0000-000000000007");
    private static readonly Guid TariffSnapshotId = Guid.Parse("10000000-0000-0000-0000-000000000008");
    private static readonly Guid CorrelationId = Guid.Parse("10000000-0000-0000-0000-000000000009");

    [Fact]
    public async Task RevalidateAsync_WhenVendorResolutionMatchesCanonicalReview_ReturnsFreshSnapshot()
    {
        var resolver = Substitute.For<IResolveVendorParkingUseCase>();
        resolver.ExecuteAsync(Arg.Any<ResolveVendorParkingCommand>(), Arg.Any<CancellationToken>())
            .Returns(Resolved(ParkingSessionId, SiteId, SiteGroupId, DateTimeOffset.UtcNow.AddMinutes(10)));
        var sut = new StatutoryDiscountPayableBasisRevalidationService(resolver);

        var result = await sut.RevalidateAsync(Review(), CorrelationId, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.TariffSnapshotId.Should().Be(TariffSnapshotId);
        result.ErrorCode.Should().BeNull();
        await resolver.Received(1).ExecuteAsync(
            Arg.Is<ResolveVendorParkingCommand>(command =>
                command.SiteId == SiteId.ToString("D") &&
                command.SiteGroupId == SiteGroupId.ToString("D") &&
                command.VendorSystemId == VendorSystemId.ToString("D") &&
                command.TicketReference == "TICKET-001" &&
                command.PlateNumber == null &&
                command.CorrelationId == CorrelationId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevalidateAsync_WhenResolvedSessionIdentityDiffers_FailsClosed()
    {
        var resolver = Substitute.For<IResolveVendorParkingUseCase>();
        resolver.ExecuteAsync(Arg.Any<ResolveVendorParkingCommand>(), Arg.Any<CancellationToken>())
            .Returns(Resolved(Guid.NewGuid(), SiteId, SiteGroupId, DateTimeOffset.UtcNow.AddMinutes(10)));
        var sut = new StatutoryDiscountPayableBasisRevalidationService(resolver);

        var result = await sut.RevalidateAsync(Review(), CorrelationId, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("STATUTORY_DISCOUNT_PAYABLE_BASIS_REVALIDATION_IDENTITY_MISMATCH");
        result.Retryable.Should().BeFalse();
    }

    [Fact]
    public async Task RevalidateAsync_WhenFreshResolutionIsAlreadyExpired_IsRetryableAndDoesNotReturnSnapshot()
    {
        var resolver = Substitute.For<IResolveVendorParkingUseCase>();
        resolver.ExecuteAsync(Arg.Any<ResolveVendorParkingCommand>(), Arg.Any<CancellationToken>())
            .Returns(Resolved(ParkingSessionId, SiteId, SiteGroupId, DateTimeOffset.UtcNow.AddMinutes(-1)));
        var sut = new StatutoryDiscountPayableBasisRevalidationService(resolver);

        var result = await sut.RevalidateAsync(Review(), CorrelationId, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.TariffSnapshotId.Should().BeNull();
        result.ErrorCode.Should().Be("STATUTORY_DISCOUNT_PAYABLE_BASIS_REVALIDATION_TEMPORARILY_UNAVAILABLE");
        result.Retryable.Should().BeTrue();
    }

    private static StatutoryDiscountServiceChannelReviewDetail Review() =>
        new(
            DecisionId,
            ValidationId,
            RequestReference,
            ParkingSessionId,
            "WEBPAY",
            SiteId,
            SiteGroupId,
            "TICKET-001",
            "ABC1234",
            "SENIOR_CITIZEN",
            StatutoryDiscountDecisionV2CommandStates.Completed,
            StatutoryDiscountDecisionV2ResultStates.Approved,
            StatutoryDiscountServiceChannelReviewStatuses.Approved,
            "SENIOR_CITIZEN_ID",
            "OSCA",
            DateOnly.Parse("2030-01-01"),
            "*****3456",
            [],
            RequesterAttestation: true,
            AttestationNotes: null,
            ReasonCode: "ELIGIBLE",
            EvidenceRequired: true,
            EvidenceRecorded: true,
            TariffSnapshotId,
            OriginalAmountMinorUnits: 10000,
            VatExclusiveAmountMinorUnits: 8929,
            VatAmountMinorUnits: 1071,
            StatutoryDiscountAmountMinorUnits: 10000,
            FinalPayableAmountMinorUnits: 0,
            Currency: "PHP",
            GoverningPolicy: null,
            ReviewerUserId: Guid.Parse("10000000-0000-0000-0000-000000000010"),
            ReviewerAccessEvaluationId: null,
            ReviewerDecision: "APPROVE",
            ReviewerReasonCode: "ELIGIBLE",
            SubmittedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            ReviewedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            PayableBasisApplicationStatus: "NOT_REQUESTED",
            CorrelationId)
        {
            VendorSystemId = VendorSystemId
        };

    private static ResolveVendorParkingResult Resolved(
        Guid parkingSessionId,
        Guid siteId,
        Guid siteGroupId,
        DateTimeOffset expiresAt)
    {
        var calculatedAt = expiresAt.AddMinutes(-10);
        var session = ParkingSession.Rehydrate(
            parkingSessionId,
            siteGroupId.ToString("D"),
            siteId.ToString("D"),
            "HIKCENTRAL",
            "VENDOR-SESSION-001",
            "TICKET",
            "ABC1234",
            "TICKET-001",
            calculatedAt.AddHours(-1),
            ParkingSessionStatus.PaymentRequired);
        var snapshot = TariffSnapshot.Rehydrate(
            TariffSnapshotId,
            parkingSessionId,
            TariffSnapshotSourceType.Base,
            grossAmount: 100m,
            statutoryDiscountAmount: 0m,
            couponDiscountAmount: 0m,
            netPayable: 100m,
            currencyCode: "PHP",
            baseFeeAmount: 100m,
            tariffVersionReference: "HIK-TARIFF-001",
            policyVersionReference: null,
            calculatedAt,
            expiresAt,
            TariffSnapshotStatus.Active,
            supersedesTariffSnapshotId: null,
            consumedByPaymentAttemptId: null);

        return ResolveVendorParkingResult.Resolved(
            session,
            snapshot,
            CorrelationId,
            VendorSystemId.ToString("D"),
            siteGroupName: "PITX",
            siteName: "PITX Level 3",
            paymentStatus: "UNPAID",
            effectivePayableBasis: null);
    }
}
