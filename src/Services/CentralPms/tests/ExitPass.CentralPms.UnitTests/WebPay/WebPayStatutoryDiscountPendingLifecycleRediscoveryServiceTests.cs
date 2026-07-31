using ExitPass.CentralPms.Application.WebPay;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.WebPay;

public sealed class WebPayStatutoryDiscountPendingLifecycleRediscoveryServiceTests
{
    private static readonly Guid ParkingSessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SiteId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SiteGroupId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid CorrelationId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid DecisionId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid RequestReference = Guid.Parse("66666666-6666-6666-6666-666666666666");

    [Fact]
    public async Task RediscoverAsync_WhenExistingWebPayPendingLifecycle_ReturnsSameDecisionAndContinuation()
    {
        var lifecycle = Lifecycle();
        var repository = new FakeRepository
        {
            SessionResult = new WebPayStatutoryDiscountPendingLifecycleSessionLookupResult(
                WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.Found,
                Session()),
            Lifecycle = lifecycle
        };
        var sut = new WebPayStatutoryDiscountPendingLifecycleRediscoveryService(repository);

        var result = await sut.RediscoverAsync(Query(), CancellationToken.None);

        result.Classification.Should().Be(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.Found);
        result.Lifecycle.Should().NotBeNull();
        result.Lifecycle!.StatutoryDecisionId.Should().Be(DecisionId);
        result.Lifecycle.RequestReference.Should().Be(RequestReference);
        result.Lifecycle.OpaqueContinuationReference.Should().Be(RequestReference.ToString("D"));
        repository.FindLatestLifecycleCallCount.Should().Be(1);
    }

    [Theory]
    [InlineData(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.NotFound)]
    [InlineData(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.NoActiveLifecycle)]
    [InlineData(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.AmbiguousSession)]
    [InlineData(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.AccessDenied)]
    [InlineData(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.SourceUnavailable)]
    public async Task RediscoverAsync_WhenSessionOrLifecycleUnavailable_ReturnsSafeClassification(string classification)
    {
        var repository = new FakeRepository();
        if (classification is WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.NoActiveLifecycle)
        {
            repository.SessionResult = new WebPayStatutoryDiscountPendingLifecycleSessionLookupResult(
                WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.Found,
                Session());
            repository.Lifecycle = null;
        }
        else
        {
            repository.SessionResult = new WebPayStatutoryDiscountPendingLifecycleSessionLookupResult(
                classification,
                Session: null,
                Retryable: classification is WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.SourceUnavailable);
        }

        var sut = new WebPayStatutoryDiscountPendingLifecycleRediscoveryService(repository);

        var result = await sut.RediscoverAsync(Query(), CancellationToken.None);

        result.Classification.Should().Be(classification);
        result.Lifecycle.Should().BeNull();
        result.CorrelationId.Should().Be(CorrelationId);
        result.SafeMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RediscoverAsync_WhenAuthoritativeStateIsMalformed_FailsClosed()
    {
        var repository = new FakeRepository
        {
            SessionResult = new WebPayStatutoryDiscountPendingLifecycleSessionLookupResult(
                WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.Found,
                Session()),
            Lifecycle = Lifecycle() with
            {
                RequestReference = Guid.Empty,
                OpaqueContinuationReference = string.Empty
            }
        };
        var sut = new WebPayStatutoryDiscountPendingLifecycleRediscoveryService(repository);

        var result = await sut.RediscoverAsync(Query(), CancellationToken.None);

        result.Classification.Should().Be(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.MalformedAuthoritativeState);
        result.Lifecycle.Should().BeNull();
    }

    [Fact]
    public async Task RediscoverAsync_WhenRepositoryThrows_MapsUnexpectedFailureSafely()
    {
        var repository = new FakeRepository
        {
            ThrowsOnResolve = true
        };
        var sut = new WebPayStatutoryDiscountPendingLifecycleRediscoveryService(repository);

        var result = await sut.RediscoverAsync(Query(), CancellationToken.None);

        result.Classification.Should().Be(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.UnexpectedFailure);
        result.SafeMessage.Should().NotContain("Exception");
    }

    [Theory]
    [MemberData(nameof(MalformedQueries))]
    public async Task RediscoverAsync_WhenRequestIsMalformed_RejectsWithoutRepositoryCall(
        WebPayStatutoryDiscountPendingLifecycleRediscoveryQuery query)
    {
        var repository = new FakeRepository();
        var sut = new WebPayStatutoryDiscountPendingLifecycleRediscoveryService(repository);

        var act = () => sut.RediscoverAsync(query, CancellationToken.None);

        await act.Should().ThrowAsync<WebPayStatutoryDiscountPendingLifecycleRediscoveryRejectedException>()
            .Where(exception => exception.ErrorCode == "INVALID_REQUEST");
        repository.ResolveSessionCallCount.Should().Be(0);
        repository.FindLatestLifecycleCallCount.Should().Be(0);
    }

    public static TheoryData<WebPayStatutoryDiscountPendingLifecycleRediscoveryQuery> MalformedQueries()
    {
        var data = new TheoryData<WebPayStatutoryDiscountPendingLifecycleRediscoveryQuery>
        {
            Query() with { LookupMode = string.Empty },
            Query() with { SiteId = Guid.Empty },
            Query() with { SiteGroupId = Guid.Empty },
            Query() with { ParkingSessionId = null },
            Query() with { TicketReference = "TICKET-1" },
            Query() with { EntitlementType = "NATIONAL_DEFAULT" },
            Query(
                WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.LookupModeTicketReference,
                parkingSessionId: null,
                ticketReference: null),
            Query(
                WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.LookupModePlateNumber,
                parkingSessionId: null,
                plateNumber: null)
        };

        return data;
    }

    private static WebPayStatutoryDiscountPendingLifecycleRediscoveryQuery Query(
        string lookupMode = WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.LookupModeParkingSessionId,
        Guid? parkingSessionId = null,
        string? ticketReference = null,
        string? plateNumber = null) =>
        new(
            lookupMode,
            parkingSessionId ?? ParkingSessionId,
            SiteId,
            SiteGroupId,
            ticketReference,
            plateNumber,
            VendorSystemId: null,
            "SENIOR_CITIZEN",
            CorrelationId);

    private static WebPayStatutoryDiscountPendingLifecycleSession Session() =>
        new(ParkingSessionId, SiteId, SiteGroupId, "TICKET-1", "ABC1234", DateTimeOffset.Parse("2026-07-31T08:00:00+08:00"));

    private static WebPayStatutoryDiscountPendingLifecycleRecord Lifecycle() =>
        new(
            DecisionId,
            DecisionId,
            RequestReference,
            "SENIOR_CITIZEN",
            "AWAITING_REVIEW",
            "AWAITING_REVIEW",
            ParkingSessionId,
            SiteId,
            SiteGroupId,
            RequestReference.ToString("D"),
            OpaqueContinuationUrl: null,
            "PENDING_REVIEW",
            Retryable: true,
            DateTimeOffset.Parse("2026-07-31T08:00:00+08:00"),
            DateTimeOffset.Parse("2026-07-31T08:01:00+08:00"),
            DateTimeOffset.Parse("2026-07-31T08:00:30+08:00"),
            DecidedAt: null,
            ReviewedAt: null);

    private sealed class FakeRepository : IWebPayStatutoryDiscountPendingLifecycleRediscoveryRepository
    {
        public WebPayStatutoryDiscountPendingLifecycleSessionLookupResult SessionResult { get; set; } =
            new(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.Found, Session());

        public WebPayStatutoryDiscountPendingLifecycleRecord? Lifecycle { get; set; } =
            WebPayStatutoryDiscountPendingLifecycleRediscoveryServiceTests.Lifecycle();

        public int ResolveSessionCallCount { get; private set; }
        public int FindLatestLifecycleCallCount { get; private set; }
        public bool ThrowsOnResolve { get; set; }

        public Task<WebPayStatutoryDiscountPendingLifecycleSessionLookupResult> ResolveSessionAsync(
            WebPayStatutoryDiscountPendingLifecycleRediscoveryQuery query,
            CancellationToken cancellationToken)
        {
            ResolveSessionCallCount++;
            if (ThrowsOnResolve)
            {
                throw new InvalidOperationException("Simulated repository failure.");
            }

            return Task.FromResult(SessionResult);
        }

        public Task<WebPayStatutoryDiscountPendingLifecycleRecord?> FindLatestLifecycleAsync(
            Guid parkingSessionId,
            Guid siteId,
            Guid siteGroupId,
            string? entitlementType,
            CancellationToken cancellationToken)
        {
            FindLatestLifecycleCallCount++;
            return Task.FromResult(Lifecycle);
        }
    }
}
