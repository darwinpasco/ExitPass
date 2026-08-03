using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Application.TerminalCashPayments;
using ExitPass.CentralPms.Contracts.TerminalCashPayments;
using ExitPass.CentralPms.Domain.Sessions;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class AptStatutoryOrdinanceAvailabilityServiceTests
{
    private static readonly Guid SiteGroupId = Guid.Parse("91000000-0000-0000-0000-000000000001");
    private static readonly Guid SiteId = Guid.Parse("91000000-0000-0000-0000-000000000002");
    private static readonly Guid LocalGovernmentUnitId = Guid.Parse("91000000-0000-0000-0000-000000000004");
    private static readonly Guid ParkingSessionId = Guid.Parse("91000000-0000-0000-0000-000000000003");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-03T02:30:00Z");

    [Theory]
    [InlineData("SENIOR_CITIZEN")]
    [InlineData("PWD")]
    public async Task Resolve_WhenEntitlementCovered_AllowsStatutoryRequestWithoutChangingOrdinaryPayment(string entitlement)
    {
        var service = CreateService(Candidate(entitlement));

        var result = await service.ResolveAsync(Request(entitlementType: entitlement), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response!.Classification.Should().Be(AptStatutoryOrdinanceAvailabilityValues.Available);
        result.Response.OrdinanceCoverageAvailable.Should().BeTrue();
        result.Response.StatutoryRequestAllowed.Should().BeTrue();
        result.Response.OrdinaryPaymentPreserved.Should().BeTrue();
        result.Response.ReadyForStatutoryCashFlow.Should().BeTrue();
        result.Response.PreCashRevalidationPassed.Should().BeFalse();
    }

    [Fact]
    public async Task Resolve_WhenPwdPolicyMissing_DoesNotReuseSeniorCoverage()
    {
        var service = CreateService(Candidate("SENIOR_CITIZEN"));

        var result = await service.ResolveAsync(Request(entitlementType: "PWD"), CancellationToken.None);

        result.Response!.Classification.Should().Be(AptStatutoryOrdinanceAvailabilityValues.NoConfiguredPolicy);
        result.Response.OrdinanceCoverageAvailable.Should().BeFalse();
        result.Response.StatutoryRequestAllowed.Should().BeFalse();
        result.Response.OrdinaryPaymentPreserved.Should().BeTrue();
    }

    [Theory]
    [InlineData("2026-09-01", null, "ACTIVE", AptStatutoryOrdinanceAvailabilityValues.NotYetEffective)]
    [InlineData("2026-01-01", "2026-07-01", "ACTIVE", AptStatutoryOrdinanceAvailabilityValues.Expired)]
    [InlineData("2026-01-01", null, "INACTIVE", AptStatutoryOrdinanceAvailabilityValues.Inactive)]
    public async Task Resolve_ClassifiesNonActiveCoverageStates(
        string effectiveFrom,
        string? effectiveTo,
        string status,
        string expected)
    {
        var service = CreateService(Candidate(
            "SENIOR_CITIZEN",
            status,
            DateOnly.Parse(effectiveFrom),
            effectiveTo is null ? null : DateOnly.Parse(effectiveTo)));

        var result = await service.ResolveAsync(Request(), CancellationToken.None);

        result.Response!.Classification.Should().Be(expected);
        result.Response.StatutoryRequestAllowed.Should().BeFalse();
        result.Response.OrdinaryPaymentPreserved.Should().BeTrue();
    }

    [Fact]
    public async Task Revalidate_WhenCoverageRevoked_FailsStatutoryPathButPreservesOrdinaryPayment()
    {
        var service = CreateService();

        var result = await service.RevalidateAsync(Request(), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response!.RevalidationOutcome.Should().Be(AptStatutoryOrdinanceAvailabilityValues.Failed);
        result.Response.PreCashRevalidationPassed.Should().BeFalse();
        result.Response.ReadyForStatutoryCashFlow.Should().BeFalse();
        result.Response.OrdinaryPaymentPreserved.Should().BeTrue();
    }

    [Fact]
    public async Task Resolve_WhenSessionLookupAmbiguous_ReturnsAmbiguousSession()
    {
        var parkingSessions = new FakeParkingSessionRepository(Session())
        {
            TicketLookupStatus = ParkingSessionLookupStatus.Ambiguous
        };
        var service = CreateService(parkingSessions: parkingSessions);

        var result = await service.ResolveAsync(Request(parkingSessionId: null, ticketReference: "TICKET-1"), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(AptStatutoryOrdinanceAvailabilityValues.AmbiguousSession);
        result.HttpStatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Resolve_WhenSessionScopeConflicts_ReturnsAmbiguousScope()
    {
        var wrongSite = Guid.Parse("91000000-0000-0000-0000-000000000099");
        var service = CreateService(parkingSessions: new FakeParkingSessionRepository(Session(siteId: wrongSite)));

        var result = await service.ResolveAsync(Request(), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(AptStatutoryOrdinanceAvailabilityValues.AmbiguousScope);
        result.HttpStatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Resolve_WhenCoverageSourceUnavailable_FailsClosedForStatutoryPath()
    {
        var coverage = new FakeCoverageRepository
        {
            ThrowOnReadCandidates = true
        };
        var service = CreateService(coverageRepository: coverage);

        var result = await service.ResolveAsync(Request(), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(AptStatutoryOrdinanceAvailabilityValues.SourceUnavailable);
        result.Retryable.Should().BeTrue();
        result.HttpStatusCode.Should().Be(503);
    }

    private static AptStatutoryOrdinanceAvailabilityService CreateService(
        ManagementPlatformStatutoryDiscountPolicyCoverageCandidate? candidate = null,
        FakeParkingSessionRepository? parkingSessions = null,
        FakeCoverageRepository? coverageRepository = null)
    {
        var coverage = coverageRepository ?? new FakeCoverageRepository();
        if (candidate is not null)
        {
            coverage.Candidates = [candidate];
        }

        return new AptStatutoryOrdinanceAvailabilityService(
            parkingSessions ?? new FakeParkingSessionRepository(Session()),
            coverage,
            new FixedTimeProvider(Now));
    }

    private static AptStatutoryOrdinanceAvailabilityRequest Request(
        string entitlementType = "SENIOR_CITIZEN",
        string? parkingSessionId = null,
        string? ticketReference = null,
        string? plateNumber = null) =>
        new(
            SiteGroupId.ToString("D"),
            SiteId.ToString("D"),
            "APT-TERMINAL-001",
            "FAKE-PMS",
            ticketReference is not null || plateNumber is not null ? parkingSessionId : parkingSessionId ?? ParkingSessionId.ToString("D"),
            ticketReference,
            plateNumber,
            entitlementType,
            Guid.NewGuid());

    private static ParkingSession Session(Guid? siteGroupId = null, Guid? siteId = null) =>
        ParkingSession.Rehydrate(
            ParkingSessionId,
            (siteGroupId ?? SiteGroupId).ToString("D"),
            (siteId ?? SiteId).ToString("D"),
            "FAKE-PMS",
            "TICKET-1",
            "TICKET",
            plateNumber: null,
            ticketNumber: "TICKET-1",
            entryTimestamp: Now,
            sessionStatus: ParkingSessionStatus.PaymentRequired);

    private static ManagementPlatformStatutoryDiscountPolicyCoverageCandidate Candidate(
        string entitlementType,
        string status = "ACTIVE",
        DateOnly? effectiveFrom = null,
        DateOnly? effectiveTo = null) =>
        new(
            SiteId,
            entitlementType,
            Guid.NewGuid(),
            $"{entitlementType}-POLICY",
            "Synthetic policy",
            status,
            "ACTIVE_APPROVED",
            "LOCAL_ORDINANCE",
            "LOCAL_ORDINANCE_APPLIED",
            "ORD-SYNTHETIC",
            "ORD-SYNTHETIC",
            null,
            effectiveFrom ?? DateOnly.Parse("2026-01-01"),
            effectiveTo,
            "source-v1",
            Now,
            "UNIT_TEST");

    private sealed class FakeParkingSessionRepository : IParkingSessionReadRepository
    {
        private readonly ParkingSession _session;

        public ParkingSessionLookupStatus TicketLookupStatus { get; init; } = ParkingSessionLookupStatus.Found;

        public FakeParkingSessionRepository(ParkingSession session)
        {
            _session = session;
        }

        public Task<ParkingSession?> GetByIdAsync(Guid parkingSessionId, CancellationToken cancellationToken) =>
            Task.FromResult(parkingSessionId == _session.ParkingSessionId ? _session : null);

        public Task<ParkingSessionLookupResult> FindByTicketReferenceAsync(
            Guid siteGroupId,
            Guid siteId,
            string? vendorSystemId,
            string ticketReference,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ParkingSessionLookupResult(
                TicketLookupStatus,
                TicketLookupStatus == ParkingSessionLookupStatus.Found ? _session : null));

        public Task<ParkingSessionLookupResult> FindByPlateNumberAsync(
            Guid siteGroupId,
            Guid siteId,
            string? vendorSystemId,
            string plateNumber,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ParkingSessionLookupResult(ParkingSessionLookupStatus.Found, _session));
    }

    private sealed class FakeCoverageRepository : IManagementPlatformStatutoryDiscountPolicyCoverageRepository
    {
        public IReadOnlyList<ManagementPlatformStatutoryDiscountPolicyCoverageCandidate> Candidates { get; set; } = [];
        public bool ThrowOnReadCandidates { get; init; }

        public Task<ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadResult> ResolveScopeAsync(
            Guid? actorUserId,
            string scopeType,
            Guid scopeId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Scope());

        public Task<ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadResult> ResolveServiceSiteScopeAsync(
            Guid siteId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Scope());

        public Task<IReadOnlyList<ManagementPlatformStatutoryDiscountPolicyCoverageCandidate>> ReadPolicyCandidatesAsync(
            IReadOnlyList<ManagementPlatformStatutoryDiscountPolicyCoverageSite> sites,
            IReadOnlyList<string> entitlementTypes,
            bool includeInactive,
            DateOnly evaluationDate,
            CancellationToken cancellationToken)
        {
            if (ThrowOnReadCandidates)
            {
                throw new InvalidOperationException("Synthetic source unavailable.");
            }

            return Task.FromResult(Candidates);
        }

        private static ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadResult Scope() =>
            new(
                ManagementPlatformStatutoryDiscountPolicyCoverageScopeReadStatus.Resolved,
                "Synthetic Site",
                [new ManagementPlatformStatutoryDiscountPolicyCoverageSite(
                    SiteId,
                    SiteGroupId,
                    "Synthetic Site",
                    "Synthetic Group",
                    "SYNTHETIC_LGU",
                    LocalGovernmentUnitId,
                    "SYNTHETIC_LGU",
                    "Synthetic City",
                    "CITY",
                    null,
                    ManagementPlatformStatutoryDiscountPolicyCoverageValues.ScopeJurisdictionSingleLgu)]);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
