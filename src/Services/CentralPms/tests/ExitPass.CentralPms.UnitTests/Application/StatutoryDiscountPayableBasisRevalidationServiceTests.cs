using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using ExitPass.CentralPms.Application.VendorParking;
using ExitPass.CentralPms.Application.VendorParking.Routing;
using ExitPass.CentralPms.Application.VendorSessions;
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
    private static readonly Guid AdapterIdentityId = Guid.Parse("10000000-0000-0000-0000-000000000010");
    private static readonly Guid ProjectionId = Guid.Parse("10000000-0000-0000-0000-000000000011");
    private static readonly DateTimeOffset EntryTime = DateTimeOffset.Parse("2026-10-20T11:19:57Z");
    private const string VendorRecordGuid = "35FF05F6145B4841B6FC87710E51361F";

    [Fact]
    public async Task RevalidateAsync_UsesCanonicalProjectionPlateInsteadOfMaskedReviewPlate()
    {
        var fixture = CreateFixture(
            canonicalSession: CanonicalSession(ticket: null, plate: "QC582"),
            projection: Projection(card: null, plate: "QC582"));

        var result = await fixture.Sut.RevalidateAsync(
            Review(ticket: null, plate: "QC5**"),
            CorrelationId,
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        await fixture.Resolver.Received(1).ExecuteAsync(
            Arg.Is<ResolveVendorParkingCommand>(command =>
                command.PlateNumber == "QC582" &&
                command.TicketReference == null &&
                command.CorrelationId == CorrelationId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevalidateAsync_PlateOnlyAnprSessionUsesAuthoritativeProjectionPlate()
    {
        var fixture = CreateFixture(
            canonicalSession: CanonicalSession(ticket: null, plate: "ABC1234"),
            projection: Projection(card: null, plate: "ABC1234"));

        var result = await fixture.Sut.RevalidateAsync(
            Review(ticket: null, plate: null),
            CorrelationId,
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        await fixture.Resolver.Received(1).ExecuteAsync(
            Arg.Is<ResolveVendorParkingCommand>(command =>
                command.PlateNumber == "ABC1234" && command.TicketReference == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevalidateAsync_PhysicalTicketPrefersAuthoritativeProjectionTicket()
    {
        var fixture = CreateFixture(
            canonicalSession: CanonicalSession(ticket: "TICKET-001", plate: "ABC1234"),
            projection: Projection(card: "TICKET-001", plate: "ABC1234"));

        var result = await fixture.Sut.RevalidateAsync(
            Review(ticket: "TICK******", plate: "ABC****"),
            CorrelationId,
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        await fixture.Resolver.Received(1).ExecuteAsync(
            Arg.Is<ResolveVendorParkingCommand>(command =>
                command.TicketReference == "TICKET-001" && command.PlateNumber == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevalidateAsync_MissingCanonicalTicketAndPlateFailsClosed()
    {
        var fixture = CreateFixture(
            canonicalSession: CanonicalSession(ticket: null, plate: null),
            projection: Projection(card: null, plate: null));

        var result = await fixture.Sut.RevalidateAsync(
            Review(ticket: null, plate: "QC5**"),
            CorrelationId,
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("STATUTORY_DISCOUNT_PAYABLE_BASIS_REVALIDATION_AUTHORITATIVE_IDENTITY_UNAVAILABLE");
        await fixture.Resolver.DidNotReceive().ExecuteAsync(
            Arg.Any<ResolveVendorParkingCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevalidateAsync_UnmaskedReviewIdentityConflictsWithCanonicalProjection_FailsClosed()
    {
        var fixture = CreateFixture(
            canonicalSession: CanonicalSession(ticket: null, plate: "QC582"),
            projection: Projection(card: null, plate: "QC582"));

        var result = await fixture.Sut.RevalidateAsync(
            Review(ticket: null, plate: "OTHER123"),
            CorrelationId,
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("STATUTORY_DISCOUNT_PAYABLE_BASIS_REVALIDATION_IDENTITY_MISMATCH");
        await fixture.Resolver.DidNotReceive().ExecuteAsync(
            Arg.Any<ResolveVendorParkingCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevalidateAsync_CanonicalSessionHasDifferentSite_FailsClosed()
    {
        var fixture = CreateFixture(canonicalSession: CanonicalSession(siteId: Guid.NewGuid()));

        var result = await fixture.Sut.RevalidateAsync(Review(), CorrelationId, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("STATUTORY_DISCOUNT_PAYABLE_BASIS_REVALIDATION_IDENTITY_MISMATCH");
        await fixture.Projections.DidNotReceive().FindForCanonicalSessionAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevalidateAsync_ProjectionUsesDifferentAdapter_FailsClosed()
    {
        var fixture = CreateFixture(
            projection: Projection(sourceAdapterIdentityId: Guid.NewGuid()));

        var result = await fixture.Sut.RevalidateAsync(Review(), CorrelationId, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("STATUTORY_DISCOUNT_PAYABLE_BASIS_REVALIDATION_IDENTITY_MISMATCH");
        await fixture.Resolver.DidNotReceive().ExecuteAsync(
            Arg.Any<ResolveVendorParkingCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevalidateAsync_SyntheticVendorReferenceIsNeverSubmittedAsTicket()
    {
        var synthetic = $"HIKCENTRAL:QC582:{EntryTime:yyyyMMddHHmmss}";
        var fixture = CreateFixture(
            canonicalSession: CanonicalSession(ticket: null, plate: "QC582"),
            projection: Projection(card: synthetic, plate: "QC582"));

        var result = await fixture.Sut.RevalidateAsync(
            Review(ticket: VendorRecordGuid, plate: "QC5**"),
            CorrelationId,
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        await fixture.Resolver.Received(1).ExecuteAsync(
            Arg.Is<ResolveVendorParkingCommand>(command =>
                command.TicketReference == null && command.PlateNumber == "QC582"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevalidateAsync_AmbiguousCanonicalProjectionFailsClosed()
    {
        var fixture = CreateFixture();
        fixture.Projections.FindForCanonicalSessionAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns<Task<CanonicalVendorSessionProjectionIdentity?>>(
                _ => throw new VendorSessionProjectionLookupException("PROJECTION_CANONICAL_SESSION_AMBIGUOUS"));

        var result = await fixture.Sut.RevalidateAsync(Review(), CorrelationId, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("STATUTORY_DISCOUNT_PAYABLE_BASIS_REVALIDATION_IDENTITY_AMBIGUOUS");
    }

    [Fact]
    public async Task RevalidateAsync_ResolvedSessionIdentityDiffers_FailsClosed()
    {
        var fixture = CreateFixture(
            resolution: Resolved(Guid.NewGuid(), SiteId, SiteGroupId, DateTimeOffset.UtcNow.AddMinutes(10)));

        var result = await fixture.Sut.RevalidateAsync(Review(), CorrelationId, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("STATUTORY_DISCOUNT_PAYABLE_BASIS_REVALIDATION_IDENTITY_MISMATCH");
    }

    [Fact]
    public async Task RevalidateAsync_FreshResolutionIsAlreadyExpired_IsRetryable()
    {
        var fixture = CreateFixture(
            resolution: Resolved(ParkingSessionId, SiteId, SiteGroupId, DateTimeOffset.UtcNow.AddMinutes(-1)));

        var result = await fixture.Sut.RevalidateAsync(Review(), CorrelationId, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("STATUTORY_DISCOUNT_PAYABLE_BASIS_REVALIDATION_TEMPORARILY_UNAVAILABLE");
        result.Retryable.Should().BeTrue();
    }

    [Fact]
    public async Task RevalidateAsync_RepeatedContinuationUsesSameCanonicalIdentity()
    {
        var fixture = CreateFixture(
            canonicalSession: CanonicalSession(ticket: null, plate: "QC582"),
            projection: Projection(card: null, plate: "QC582"));
        var review = Review(ticket: null, plate: "QC5**");

        var first = await fixture.Sut.RevalidateAsync(review, CorrelationId, CancellationToken.None);
        var replay = await fixture.Sut.RevalidateAsync(review, CorrelationId, CancellationToken.None);

        first.Should().Be(replay);
        await fixture.Resolver.Received(2).ExecuteAsync(
            Arg.Is<ResolveVendorParkingCommand>(command => command.PlateNumber == "QC582"),
            Arg.Any<CancellationToken>());
    }

    private static Fixture CreateFixture(
        ParkingSession? canonicalSession = null,
        VendorSessionProjection? projection = null,
        ResolveVendorParkingResult? resolution = null)
    {
        canonicalSession ??= CanonicalSession();
        projection ??= Projection();
        resolution ??= Resolved(ParkingSessionId, SiteId, SiteGroupId, DateTimeOffset.UtcNow.AddMinutes(10));

        var resolver = Substitute.For<IResolveVendorParkingUseCase>();
        resolver.ExecuteAsync(Arg.Any<ResolveVendorParkingCommand>(), Arg.Any<CancellationToken>())
            .Returns(resolution);

        var parkingSessions = Substitute.For<IParkingSessionReadRepository>();
        parkingSessions.GetByIdAsync(ParkingSessionId, Arg.Any<CancellationToken>())
            .Returns(canonicalSession);

        var projections = Substitute.For<IVendorSessionProjectionRepository>();
        projections.FindForCanonicalSessionAsync(
                ParkingSessionId,
                SiteId,
                SiteGroupId,
                VendorSystemId,
                Arg.Any<CancellationToken>())
            .Returns(new CanonicalVendorSessionProjectionIdentity(
                projection.VendorSessionProjectionId,
                projection.VendorSystemId!.Value,
                projection.SiteId!.Value,
                projection.SiteGroupId!.Value,
                projection.SourceAdapterIdentityId!.Value,
                projection.VendorRecordGuid!,
                projection.CardNum,
                projection.PlateLicense,
                projection.EnterTime!.Value,
                projection.ProjectionStatus));

        var routes = Substitute.For<ISiteVendorAdapterRouteRegistry>();
        routes.ResolveAsync(SiteId, SiteGroupId, VendorSystemId, Arg.Any<CancellationToken>())
            .Returns(Route());

        return new Fixture(
            new StatutoryDiscountPayableBasisRevalidationService(
                resolver,
                parkingSessions,
                projections,
                routes),
            resolver,
            parkingSessions,
            projections,
            routes);
    }

    private static ParkingSession CanonicalSession(
        string? ticket = "TICKET-001",
        string? plate = "ABC1234",
        Guid? siteId = null) =>
        ParkingSession.Rehydrate(
            ParkingSessionId,
            SiteGroupId.ToString("D"),
            (siteId ?? SiteId).ToString("D"),
            "HIKCENTRAL",
            VendorRecordGuid,
            ticket is null ? "PLATE" : "TICKET",
            plate,
            ticket,
            EntryTime,
            ParkingSessionStatus.PaymentRequired);

    private static VendorSessionProjection Projection(
        string? card = "TICKET-001",
        string? plate = "ABC1234",
        Guid? sourceAdapterIdentityId = null) =>
        new(
            ProjectionId,
            VendorSystemId,
            SiteId,
            SiteGroupId,
            "1",
            "PITX",
            "PASSAGEWAY-01",
            "PITX Level 3 Entry",
            "LANE-01",
            "Entry Lane",
            "ENTRY",
            VendorRecordGuid,
            card,
            plate,
            EntryTime,
            ExitTime: null,
            AllowType: "NORMAL",
            AllowResult: "ALLOWED",
            ImageUrl: null,
            SourceApi: "HIKCENTRAL_PASSAGEWAY",
            SourcePayloadHash: new string('a', 64),
            SourcePayloadReference: null,
            SourceEventAt: EntryTime,
            StableIdentityType: "GUID",
            StableIdentityKey: $"{VendorSystemId:N}|GUID|{VendorRecordGuid}",
            FirstSeenAt: EntryTime,
            LastSeenAt: EntryTime,
            LastRefreshedAt: DateTimeOffset.UtcNow,
            ProjectionStatus: VendorSessionProjectionStatus.Active,
            CorrelationId: CorrelationId,
            CreatedAt: EntryTime,
            UpdatedAt: DateTimeOffset.UtcNow)
        {
            SourceAdapterIdentityId = sourceAdapterIdentityId ?? AdapterIdentityId
        };

    private static SiteVendorAdapterRoute Route() =>
        new(
            SiteId,
            SiteGroupId,
            VendorSystemId,
            AdapterIdentityId,
            new Uri("https://pitx-site-adapter:8443"),
            "secret://site-adapter/api-key",
            "LIVE",
            DateTimeOffset.UtcNow.AddDays(-1),
            EffectiveTo: null);

    private static StatutoryDiscountServiceChannelReviewDetail Review(
        string? ticket = "TICKET-001",
        string? plate = "ABC1234") =>
        new(
            DecisionId,
            ValidationId,
            RequestReference,
            ParkingSessionId,
            "WEBPAY",
            SiteId,
            SiteGroupId,
            ticket,
            plate,
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
            ReviewerUserId: Guid.Parse("10000000-0000-0000-0000-000000000012"),
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
            VendorRecordGuid,
            "TICKET",
            "ABC1234",
            "TICKET-001",
            EntryTime,
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

    private sealed record Fixture(
        StatutoryDiscountPayableBasisRevalidationService Sut,
        IResolveVendorParkingUseCase Resolver,
        IParkingSessionReadRepository ParkingSessions,
        IVendorSessionProjectionRepository Projections,
        ISiteVendorAdapterRouteRegistry Routes);
}
