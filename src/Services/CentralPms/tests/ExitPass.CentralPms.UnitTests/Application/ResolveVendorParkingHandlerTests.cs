using System.Collections.Concurrent;
using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Application.Eventing;
using ExitPass.CentralPms.Application.Observability;
using ExitPass.CentralPms.Application.PaymentAttempts;
using ExitPass.CentralPms.Application.PaymentAttempts.Commands;
using ExitPass.CentralPms.Application.PaymentAttempts.Results;
using ExitPass.CentralPms.Application.VendorParking;
using ExitPass.CentralPms.Domain.Common;
using ExitPass.CentralPms.Domain.PaymentAttempts;
using ExitPass.CentralPms.Domain.PaymentAttempts.Policies;
using ExitPass.CentralPms.Domain.Sessions;
using ExitPass.CentralPms.Domain.Tariffs;
using ExitPass.VendorPmsAdapter.Contracts.Parking;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Unit tests for provider-neutral Vendor PMS Adapter resolution into Central PMS parking session and tariff objects.
/// </summary>
public sealed class ResolveVendorParkingHandlerTests
{
    private static readonly Guid CorrelationId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid PaymentAttemptId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly DateTimeOffset Now = new(2026, 4, 1, 1, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// Verifies that a plate lookup maps provider-neutral vendor data into Central PMS session and tariff objects.
    /// </summary>
    [Fact]
    public async Task ResolveVendorSession_WhenPlateExists_CreatesParkingSessionAndTariffSnapshot()
    {
        var sut = CreateSut(FakeVendorPmsParkingResolutionClient.FoundWithInlineQuote());

        var result = await sut.ExecuteAsync(PlateCommand(), CancellationToken.None);

        result.Outcome.Should().Be(ResolveVendorParkingOutcome.Resolved);
        result.ParkingSession.Should().NotBeNull();
        result.TariffSnapshot.Should().NotBeNull();
        result.ParkingSession!.VendorSystemCode.Should().Be("FAKE-PMS");
        result.ParkingSession.VendorSessionRef.Should().Be("VENDOR-SESSION-001");
        result.ParkingSession.IdentifierType.Should().Be("PLATE");
        result.ParkingSession.PlateNumber.Should().Be("ABC1234");
        result.ParkingSession.SessionStatus.Should().Be(ParkingSessionStatus.PaymentRequired);
        result.TariffSnapshot!.ParkingSessionId.Should().Be(result.ParkingSession.ParkingSessionId);
        result.TariffSnapshot.NetPayable.Should().Be(125.50m);
        result.TariffSnapshot.CurrencyCode.Should().Be("PHP");
        result.TariffSnapshot.TariffVersionReference.Should().Be("FAKE-TARIFF-001");
        result.TariffSnapshot.SnapshotStatus.Should().Be(TariffSnapshotStatus.Active);
    }

    /// <summary>
    /// Verifies that a successful vendor parking resolution publishes the integration event envelope.
    /// </summary>
    [Fact]
    public async Task ResolveVendorParking_WhenResolved_PublishesVendorParkingResolved()
    {
        var publisher = new RecordingIntegrationEventPublisher();
        var sut = CreateSut(FakeVendorPmsParkingResolutionClient.FoundWithInlineQuote(), publisher);

        var result = await sut.ExecuteAsync(PlateCommand(), CancellationToken.None);

        publisher.Published.Should().ContainSingle();
        var envelope = publisher.Published.Single();
        envelope.EventType.Should().Be(IntegrationEventTypes.VendorParkingResolved);
        envelope.AggregateId.Should().Be(result.ParkingSession!.ParkingSessionId.ToString());
        envelope.AggregateType.Should().Be(nameof(ParkingSession));
        envelope.CorrelationId.Should().Be(CorrelationId);
        envelope.EventId.Should().NotBeEmpty();
        envelope.SchemaVersion.Should().Be(1);
        envelope.Payload.Should().BeOfType<VendorParkingResolvedPayload>()
            .Which.Should().Match<VendorParkingResolvedPayload>(payload =>
                payload.ParkingSessionId == result.ParkingSession!.ParkingSessionId &&
                payload.TariffSnapshotId == result.TariffSnapshot!.TariffSnapshotId &&
                payload.SiteId == "SITE-TEST-001" &&
                payload.SiteGroupId == "SG-TEST-001" &&
                payload.VendorSystemId == "FAKE-PMS" &&
                payload.LookupReferenceType == "plate" &&
                payload.LookupOutcome == ResolveVendorParkingOutcome.Resolved.ToString() &&
                payload.NetPayableMinorUnits == 12550 &&
                payload.Currency == "PHP");
    }

    /// <summary>
    /// Verifies that a ticket lookup maps provider-neutral vendor data into Central PMS session and tariff objects.
    /// </summary>
    [Fact]
    public async Task ResolveVendorSession_WhenTicketExists_CreatesParkingSessionAndTariffSnapshot()
    {
        var sut = CreateSut(FakeVendorPmsParkingResolutionClient.FoundWithInlineQuote());

        var result = await sut.ExecuteAsync(TicketCommand(), CancellationToken.None);

        result.Outcome.Should().Be(ResolveVendorParkingOutcome.Resolved);
        result.ParkingSession!.IdentifierType.Should().Be("TICKET");
        result.ParkingSession.TicketNumber.Should().Be("TICKET-001");
        result.TariffSnapshot!.NetPayable.Should().Be(125.50m);
    }

    /// <summary>
    /// Verifies that a vendor not-found response remains deterministic for Central PMS callers.
    /// </summary>
    [Fact]
    public async Task ResolveVendorSession_WhenVendorReturnsNotFound_ReturnsSessionNotFound()
    {
        var publisher = new RecordingIntegrationEventPublisher();
        var sut = CreateSut(FakeVendorPmsParkingResolutionClient.NotFound(), publisher);

        var result = await sut.ExecuteAsync(PlateCommand(), CancellationToken.None);

        result.Outcome.Should().Be(ResolveVendorParkingOutcome.SessionNotFound);
        result.ErrorCode.Should().Be("SESSION_NOT_FOUND");
        result.Retryable.Should().BeFalse();
        result.ParkingSession.Should().BeNull();
        result.TariffSnapshot.Should().BeNull();
        publisher.Published.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that unsuccessful vendor lookups do not publish success events.
    /// </summary>
    /// <param name="clientScenario">Vendor PMS client response setup name.</param>
    [Theory]
    [MemberData(nameof(FailedVendorResolutionClients))]
    public async Task ResolveVendorParking_WhenNotFoundUnavailableOrMalformed_DoesNotPublishVendorParkingResolved(
        string clientScenario)
    {
        var publisher = new RecordingIntegrationEventPublisher();
        var sut = CreateSut(FailedVendorResolutionClient(clientScenario), publisher);

        var result = await sut.ExecuteAsync(PlateCommand(), CancellationToken.None);

        result.Outcome.Should().NotBe(ResolveVendorParkingOutcome.Resolved);
        publisher.Published.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that the published event preserves the request correlation identifier.
    /// </summary>
    [Fact]
    public async Task ResolveVendorParking_PublishedEventContainsCorrelationId()
    {
        var publisher = new RecordingIntegrationEventPublisher();
        var sut = CreateSut(FakeVendorPmsParkingResolutionClient.FoundWithInlineQuote(), publisher);

        await sut.ExecuteAsync(PlateCommand(), CancellationToken.None);

        publisher.Published.Single().CorrelationId.Should().Be(CorrelationId);
    }

    /// <summary>
    /// Verifies that a retryable adapter outage maps to a bounded Central PMS retryable outcome.
    /// </summary>
    [Fact]
    public async Task ResolveVendorSession_WhenVendorUnavailable_ReturnsRetryableUnavailable()
    {
        var sut = CreateSut(FakeVendorPmsParkingResolutionClient.Unavailable());

        var result = await sut.ExecuteAsync(PlateCommand(), CancellationToken.None);

        result.Outcome.Should().Be(ResolveVendorParkingOutcome.RetryableUnavailable);
        result.ErrorCode.Should().Be("VENDOR_UNAVAILABLE");
        result.Retryable.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that malformed provider-neutral vendor data is rejected deterministically.
    /// </summary>
    [Fact]
    public async Task ResolveVendorSession_WhenVendorResponseMalformed_ReturnsMalformedVendorResponse()
    {
        var sut = CreateSut(FakeVendorPmsParkingResolutionClient.MalformedSession());

        var result = await sut.ExecuteAsync(PlateCommand(), CancellationToken.None);

        result.Outcome.Should().Be(ResolveVendorParkingOutcome.MalformedVendorResponse);
        result.ErrorCode.Should().Be("MALFORMED_VENDOR_SESSION");
        result.Retryable.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a resolved vendor session can feed the existing PaymentAttempt creation path.
    /// </summary>
    [Fact]
    public async Task CreatePaymentAttempt_WhenVendorSessionResolved_CreatesPaymentAttempt()
    {
        var resolved = await CreateSut(FakeVendorPmsParkingResolutionClient.FoundWithInlineQuote())
            .ExecuteAsync(PlateCommand(), CancellationToken.None);
        var paymentSut = CreatePaymentAttemptSut(resolved, wasReused: false);

        var result = await paymentSut.ExecuteAsync(CreatePaymentCommand(resolved, "idem-vendor-001"), CancellationToken.None);

        result.PaymentAttemptId.Should().Be(PaymentAttemptId);
        result.ParkingSessionId.Should().Be(resolved.ParkingSession!.ParkingSessionId);
        result.TariffSnapshotId.Should().Be(resolved.TariffSnapshot!.TariffSnapshotId);
        result.WasReused.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that the resolved vendor tariff quote is the tariff snapshot bound by PaymentAttempt creation.
    /// </summary>
    [Fact]
    public async Task CreatePaymentAttempt_WhenVendorTariffResolved_BindsTariffSnapshot()
    {
        var resolved = await CreateSut(FakeVendorPmsParkingResolutionClient.FoundWithSeparateTariff())
            .ExecuteAsync(PlateCommand(), CancellationToken.None);
        var gateway = Substitute.For<IPaymentAttemptDbRoutineGateway>();
        gateway.CreateOrReusePaymentAttemptAsync(Arg.Any<CreateOrReusePaymentAttemptDbRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<CreateOrReusePaymentAttemptDbRequest>();
                return SuccessfulDbResult(request, wasReused: false);
            });
        var paymentSut = CreatePaymentAttemptSut(resolved, gateway);

        await paymentSut.ExecuteAsync(CreatePaymentCommand(resolved, "idem-vendor-tariff"), CancellationToken.None);

        await gateway.Received(1).CreateOrReusePaymentAttemptAsync(
            Arg.Is<CreateOrReusePaymentAttemptDbRequest>(r =>
                r.TariffSnapshotId == resolved.TariffSnapshot!.TariffSnapshotId),
            Arg.Any<CancellationToken>());
        resolved.TariffSnapshot!.NetPayable.Should().Be(250.75m);
        resolved.TariffSnapshot.TariffVersionReference.Should().Be("FAKE-TARIFF-SEPARATE");
    }

    /// <summary>
    /// Verifies that repeated payment attempt creation for the same resolved session preserves idempotent reuse.
    /// </summary>
    [Fact]
    public async Task CreatePaymentAttempt_WhenSameResolvedSessionRepeated_IsIdempotentOrReusesCorrectly()
    {
        var resolved = await CreateSut(FakeVendorPmsParkingResolutionClient.FoundWithInlineQuote())
            .ExecuteAsync(PlateCommand(), CancellationToken.None);
        var gateway = Substitute.For<IPaymentAttemptDbRoutineGateway>();
        gateway.CreateOrReusePaymentAttemptAsync(Arg.Any<CreateOrReusePaymentAttemptDbRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                call => SuccessfulDbResult(call.Arg<CreateOrReusePaymentAttemptDbRequest>(), wasReused: false),
                call => SuccessfulDbResult(call.Arg<CreateOrReusePaymentAttemptDbRequest>(), wasReused: true));
        var paymentSut = CreatePaymentAttemptSut(resolved, gateway);
        var command = CreatePaymentCommand(resolved, "idem-vendor-repeat");

        var first = await paymentSut.ExecuteAsync(command, CancellationToken.None);
        var second = await paymentSut.ExecuteAsync(command, CancellationToken.None);

        second.PaymentAttemptId.Should().Be(first.PaymentAttemptId);
        second.WasReused.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that Central PMS contracts in this seam do not expose HikCentral-specific fields.
    /// </summary>
    [Fact]
    public void CreatePaymentAttempt_DoesNotLeakHikCentralFieldsIntoCentralPmsContracts()
    {
        var centralTypes = new[]
        {
            typeof(ResolveVendorParkingCommand),
            typeof(ResolveVendorParkingResult),
            typeof(IVendorPmsParkingResolutionClient)
        };

        var publicNames = centralTypes
            .SelectMany(type => type.GetMembers().Select(member => member.Name).Append(type.Name));

        publicNames.Should().NotContain(name => name.Contains("HikCentral", StringComparison.OrdinalIgnoreCase));
        publicNames.Should().NotContain(name => name.Contains("Ak", StringComparison.OrdinalIgnoreCase));
        publicNames.Should().NotContain(name => name.Contains("Sk", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Provides failed vendor-resolution scenarios that must not emit success events.
    /// </summary>
    /// <returns>Failed vendor-resolution scenario names.</returns>
    public static TheoryData<string> FailedVendorResolutionClients()
    {
        return new TheoryData<string>
        {
            "not-found",
            "unavailable",
            "malformed"
        };
    }

    private static FakeVendorPmsParkingResolutionClient FailedVendorResolutionClient(string scenario)
    {
        return scenario switch
        {
            "not-found" => FakeVendorPmsParkingResolutionClient.NotFound(),
            "unavailable" => FakeVendorPmsParkingResolutionClient.Unavailable(),
            "malformed" => FakeVendorPmsParkingResolutionClient.MalformedSession(),
            _ => throw new InvalidOperationException($"Unknown failed vendor resolution scenario '{scenario}'.")
        };
    }

    private static ResolveVendorParkingHandler CreateSut(
        FakeVendorPmsParkingResolutionClient vendorClient,
        RecordingIntegrationEventPublisher? eventPublisher = null)
    {
        return new ResolveVendorParkingHandler(
            vendorClient,
            new PassThroughVendorParkingResolutionPersistence(),
            eventPublisher ?? new RecordingIntegrationEventPublisher(),
            new CentralPmsMetrics(),
            NullLogger<ResolveVendorParkingHandler>.Instance);
    }

    private static CreateOrReusePaymentAttemptHandler CreatePaymentAttemptSut(
        ResolveVendorParkingResult resolved,
        bool wasReused)
    {
        var gateway = Substitute.For<IPaymentAttemptDbRoutineGateway>();
        gateway.CreateOrReusePaymentAttemptAsync(Arg.Any<CreateOrReusePaymentAttemptDbRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => SuccessfulDbResult(call.Arg<CreateOrReusePaymentAttemptDbRequest>(), wasReused));

        return CreatePaymentAttemptSut(resolved, gateway);
    }

    private static CreateOrReusePaymentAttemptHandler CreatePaymentAttemptSut(
        ResolveVendorParkingResult resolved,
        IPaymentAttemptDbRoutineGateway gateway)
    {
        var providerHandoffFactory = Substitute.For<IProviderHandoffFactory>();
        providerHandoffFactory.CreatePlaceholder(Arg.Any<PaymentProvider>(), PaymentAttemptId)
            .Returns(new ProviderHandoffResult
            {
                Type = "REDIRECT",
                Url = "/payments/fake",
                ExpiresAt = Now.AddMinutes(15)
            });

        var clock = Substitute.For<ISystemClock>();
        clock.UtcNow.Returns(Now);

        return new CreateOrReusePaymentAttemptHandler(
            new InMemoryParkingSessionReadRepository(resolved.ParkingSession!),
            new InMemoryTariffSnapshotReadRepository(resolved.TariffSnapshot!),
            gateway,
            Substitute.For<IPaymentAttemptCreationPolicy>(),
            providerHandoffFactory,
            new RecordingIntegrationEventPublisher(),
            clock,
            new CentralPmsMetrics(),
            NullLogger<CreateOrReusePaymentAttemptHandler>.Instance);
    }

    private static CreateOrReusePaymentAttemptCommand CreatePaymentCommand(
        ResolveVendorParkingResult resolved,
        string idempotencyKey)
    {
        return new CreateOrReusePaymentAttemptCommand
        {
            ParkingSessionId = resolved.ParkingSession!.ParkingSessionId,
            TariffSnapshotId = resolved.TariffSnapshot!.TariffSnapshotId,
            PaymentProviderCode = "GCASH",
            IdempotencyKey = idempotencyKey,
            CorrelationId = CorrelationId,
            RequestedBy = "unit-test"
        };
    }

    private static CreateOrReusePaymentAttemptDbResult SuccessfulDbResult(
        CreateOrReusePaymentAttemptDbRequest request,
        bool wasReused)
    {
        return new CreateOrReusePaymentAttemptDbResult
        {
            PaymentAttemptId = PaymentAttemptId,
            ParkingSessionId = request.ParkingSessionId,
            TariffSnapshotId = request.TariffSnapshotId,
            AttemptStatus = "INITIATED",
            PaymentProviderCode = request.PaymentProviderCode,
            WasReused = wasReused,
            OutcomeCode = wasReused ? "REUSED_BY_IDEMPOTENCY_KEY" : "CREATED",
            GrossAmountSnapshot = 125.50m,
            StatutoryDiscountSnapshot = 0m,
            CouponDiscountSnapshot = 0m,
            NetAmountDueSnapshot = 125.50m,
            CurrencyCode = "PHP",
            TariffVersionReference = "FAKE-TARIFF-001",
            IdempotencyKey = request.IdempotencyKey
        };
    }

    private static ResolveVendorParkingCommand PlateCommand()
    {
        return new ResolveVendorParkingCommand
        {
            SiteGroupId = "SG-TEST-001",
            SiteId = "SITE-TEST-001",
            PlateNumber = "ABC1234",
            CorrelationId = CorrelationId
        };
    }

    private static ResolveVendorParkingCommand TicketCommand()
    {
        return new ResolveVendorParkingCommand
        {
            SiteGroupId = "SG-TEST-001",
            SiteId = "SITE-TEST-001",
            TicketReference = "TICKET-001",
            CorrelationId = CorrelationId
        };
    }

    private sealed class FakeVendorPmsParkingResolutionClient : IVendorPmsParkingResolutionClient
    {
        private readonly VendorParkingSessionLookupResponse _sessionResponse;
        private readonly VendorTariffQuoteResponse _tariffResponse;

        private FakeVendorPmsParkingResolutionClient(
            VendorParkingSessionLookupResponse sessionResponse,
            VendorTariffQuoteResponse tariffResponse)
        {
            _sessionResponse = sessionResponse;
            _tariffResponse = tariffResponse;
        }

        public static FakeVendorPmsParkingResolutionClient FoundWithInlineQuote()
        {
            var quote = new VendorTariffQuoteDto(12550, "PHP", "FAKE-TARIFF-001", "Fake tariff", Now);
            return Found(quote, quote);
        }

        public static FakeVendorPmsParkingResolutionClient FoundWithSeparateTariff()
        {
            return Found(
                sessionQuote: null,
                tariffQuote: new VendorTariffQuoteDto(25075, "PHP", "FAKE-TARIFF-SEPARATE", "Separate fake tariff", Now));
        }

        public static FakeVendorPmsParkingResolutionClient NotFound()
        {
            return new FakeVendorPmsParkingResolutionClient(
                new VendorParkingSessionLookupResponse(VendorParkingLookupStatus.NotFound, null, "SESSION_NOT_FOUND", false, CorrelationId),
                new VendorTariffQuoteResponse(VendorParkingLookupStatus.NotFound, null, "SESSION_NOT_FOUND", false, CorrelationId));
        }

        public static FakeVendorPmsParkingResolutionClient Unavailable()
        {
            return new FakeVendorPmsParkingResolutionClient(
                new VendorParkingSessionLookupResponse(VendorParkingLookupStatus.UnavailableRetryable, null, "VENDOR_UNAVAILABLE", true, CorrelationId),
                new VendorTariffQuoteResponse(VendorParkingLookupStatus.UnavailableRetryable, null, "VENDOR_UNAVAILABLE", true, CorrelationId));
        }

        public static FakeVendorPmsParkingResolutionClient MalformedSession()
        {
            var malformedSession = new VendorParkingSessionDto(
                string.Empty,
                "VENDOR-SESSION-001",
                "ABC1234",
                Now.AddHours(-2),
                7200,
                "PAYMENT_REQUIRED",
                new VendorTariffQuoteDto(12550, "PHP", "FAKE-TARIFF-001", "Fake tariff", Now));

            return new FakeVendorPmsParkingResolutionClient(
                new VendorParkingSessionLookupResponse(VendorParkingLookupStatus.Found, malformedSession, null, false, CorrelationId),
                new VendorTariffQuoteResponse(VendorParkingLookupStatus.Found, malformedSession.TariffQuote, null, false, CorrelationId));
        }

        public Task<VendorParkingSessionLookupResponse> ResolveSessionAsync(
            VendorParkingSessionLookupRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_sessionResponse);
        }

        public Task<VendorTariffQuoteResponse> ResolveTariffAsync(
            VendorTariffQuoteRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_tariffResponse);
        }

        private static FakeVendorPmsParkingResolutionClient Found(
            VendorTariffQuoteDto? sessionQuote,
            VendorTariffQuoteDto tariffQuote)
        {
            var session = new VendorParkingSessionDto(
                "FAKE-PMS",
                "VENDOR-SESSION-001",
                "ABC1234",
                Now.AddHours(-2),
                7200,
                "PAYMENT_REQUIRED",
                sessionQuote);

            return new FakeVendorPmsParkingResolutionClient(
                new VendorParkingSessionLookupResponse(VendorParkingLookupStatus.Found, session, null, false, CorrelationId),
                new VendorTariffQuoteResponse(VendorParkingLookupStatus.Found, tariffQuote, null, false, CorrelationId));
        }
    }

    private sealed class PassThroughVendorParkingResolutionPersistence : IVendorParkingResolutionPersistence
    {
        public Task<PersistVendorParkingResolutionResult> PersistAsync(
            PersistVendorParkingResolutionRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new PersistVendorParkingResolutionResult
            {
                ParkingSession = request.ParkingSession,
                TariffSnapshot = request.TariffSnapshot,
                ParkingSessionWasReused = false,
                TariffSnapshotWasReused = false
            });
        }
    }

    private sealed class InMemoryParkingSessionReadRepository : IParkingSessionReadRepository
    {
        private readonly ConcurrentDictionary<Guid, ParkingSession> _sessions;

        public InMemoryParkingSessionReadRepository(ParkingSession session)
        {
            _sessions = new ConcurrentDictionary<Guid, ParkingSession>(
                new[] { new KeyValuePair<Guid, ParkingSession>(session.ParkingSessionId, session) });
        }

        public Task<ParkingSession?> GetByIdAsync(Guid parkingSessionId, CancellationToken cancellationToken)
        {
            _sessions.TryGetValue(parkingSessionId, out var session);
            return Task.FromResult(session);
        }
    }

    private sealed class InMemoryTariffSnapshotReadRepository : ITariffSnapshotReadRepository
    {
        private readonly ConcurrentDictionary<Guid, TariffSnapshot> _snapshots;

        public InMemoryTariffSnapshotReadRepository(TariffSnapshot snapshot)
        {
            _snapshots = new ConcurrentDictionary<Guid, TariffSnapshot>(
                new[] { new KeyValuePair<Guid, TariffSnapshot>(snapshot.TariffSnapshotId, snapshot) });
        }

        public Task<TariffSnapshot?> GetByIdAsync(Guid tariffSnapshotId, CancellationToken cancellationToken)
        {
            _snapshots.TryGetValue(tariffSnapshotId, out var snapshot);
            return Task.FromResult(snapshot);
        }
    }

    private sealed class RecordingIntegrationEventPublisher : IIntegrationEventPublisher
    {
        public List<IntegrationEventEnvelope> Published { get; } = new();

        public Task PublishAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
        {
            Published.Add(envelope);
            return Task.CompletedTask;
        }
    }
}
