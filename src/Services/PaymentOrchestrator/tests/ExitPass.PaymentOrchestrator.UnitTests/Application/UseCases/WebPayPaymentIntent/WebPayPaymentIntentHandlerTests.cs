using ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;
using ExitPass.PaymentOrchestrator.Application.UseCases.WebPayPaymentIntents;
using ExitPass.PaymentOrchestrator.Contracts.Internal;
using ExitPass.PaymentOrchestrator.Contracts.Payments;
using ExitPass.PaymentOrchestrator.Contracts.Routing;
using ExitPass.PaymentOrchestrator.Contracts.WebPay;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ExitPass.PaymentOrchestrator.UnitTests.Application.UseCases.WebPayPaymentIntent;

/// <summary>
/// Unit tests for <see cref="WebPayPaymentIntentHandler"/>.
/// </summary>
public sealed class WebPayPaymentIntentHandlerTests
{
    private static readonly Guid SiteGroupId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SiteId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CorrelationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ParkingSessionId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid TariffSnapshotId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid PaymentAttemptId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    /// <summary>
    /// Verifies QRPH routes through the DB-backed policy result and returns AUB with PayMongo fallback.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenQrphRequested_SelectsAubAndPayMongoFallbackFromRoutingPolicy()
    {
        var fixture = CreateFixture("QRPH", "AUB", "PAYMONGO");

        var result = await fixture.Sut.HandleAsync(DefaultRequest("QRPH"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("AUB", result.Response!.SelectedProviderCode);
        Assert.Equal("PAYMONGO", result.Response.FallbackProviderCode);
        Assert.Equal("QRPH", fixture.CapturedRouteRequest!.PaymentMethod);
        Assert.Equal("AUB_QRPH", fixture.CapturedPaymentProvider);
        Assert.Equal("QRPH", fixture.CapturedPaymentMethod);
        Assert.Equal("AUB_CARD_CASHIER", fixture.CapturedInitiateRequest!.ProviderProduct);
    }

    /// <summary>
    /// Verifies GCash routes through the DB-backed policy result and returns PayMongo.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenGcashRequested_SelectsPayMongoFromRoutingPolicy()
    {
        var fixture = CreateFixture("GCASH", "PAYMONGO", null);

        var result = await fixture.Sut.HandleAsync(DefaultRequest("GCASH"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("PAYMONGO", result.Response!.SelectedProviderCode);
        Assert.Null(result.Response.FallbackProviderCode);
        Assert.Equal("PAYMONGO_CHECKOUT_SESSION", fixture.CapturedPaymentProvider);
        Assert.Equal("GCASH", fixture.CapturedPaymentMethod);
        Assert.Equal("PAYMONGO_CHECKOUT_SESSION", fixture.CapturedInitiateRequest!.ProviderProduct);
    }

    /// <summary>
    /// Verifies card routes selected for AUB use the Central PMS AUB card cashier rail.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenCardRequestedWithAubRoute_CreatesAubCardCashierAttempt()
    {
        var fixture = CreateFixture("CARD", "AUB", "PAYMONGO");

        var result = await fixture.Sut.HandleAsync(DefaultRequest("CARD"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("AUB_CARD_CASHIER", fixture.CapturedPaymentProvider);
        Assert.Equal("CARD", fixture.CapturedPaymentMethod);
    }

    /// <summary>
    /// Verifies Maya routes selected for PayMongo use the Central PMS PayMongo checkout rail.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenMayaRequestedWithPayMongoRoute_CreatesPayMongoCheckoutAttempt()
    {
        var fixture = CreateFixture("MAYA", "PAYMONGO", null);

        var result = await fixture.Sut.HandleAsync(DefaultRequest("MAYA"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("PAYMONGO_CHECKOUT_SESSION", fixture.CapturedPaymentProvider);
        Assert.Equal("MAYA", fixture.CapturedPaymentMethod);
    }

    /// <summary>
    /// Verifies PayMongo card routes use the Central PMS PayMongo checkout rail.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenCardRequestedWithPayMongoRoute_CreatesPayMongoCheckoutAttempt()
    {
        var fixture = CreateFixture("CARD", "PAYMONGO", null);

        var result = await fixture.Sut.HandleAsync(DefaultRequest("CARD"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("PAYMONGO_CHECKOUT_SESSION", fixture.CapturedPaymentProvider);
        Assert.Equal("CARD", fixture.CapturedPaymentMethod);
    }

    /// <summary>
    /// Verifies preferred provider override is passed to routing and used only through a valid route result.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenPreferredProviderSupported_UsesPreferredProvider()
    {
        var fixture = CreateFixture("CARD", "PAYMONGO", null, routingReason: "PREFERRED_PROVIDER");
        var request = DefaultRequest("CARD");
        request.PreferredProviderCode = "PAYMONGO";

        var result = await fixture.Sut.HandleAsync(request, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("PAYMONGO", result.Response!.SelectedProviderCode);
        Assert.Equal("PAYMONGO", fixture.CapturedRouteRequest!.PreferredProviderCode);
        Assert.Equal("PREFERRED_PROVIDER", result.Response.RoutingReason);
    }

    /// <summary>
    /// Verifies unsupported preferred provider routes fail deterministically before attempt creation.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenPreferredProviderUnsupported_ReturnsValidationError()
    {
        var fixture = CreateFixture("CARD", null, null, isRouted: false, errorCode: "PREFERRED_PROVIDER_UNSUPPORTED");
        var request = DefaultRequest("CARD");
        request.PreferredProviderCode = "UNSUPPORTED";

        var result = await fixture.Sut.HandleAsync(request, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(422, result.Error!.StatusCode);
        Assert.Equal("PREFERRED_PROVIDER_UNSUPPORTED", result.Error.ErrorCode);
        Assert.False(fixture.CreatePaymentAttemptWasCalled);
        Assert.Null(fixture.CapturedInitiateRequest);
    }

    /// <summary>
    /// Verifies provider rail mappings reject unsupported combinations before creating a payment attempt.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenProviderRailMappingIsUnsupported_ReturnsDeterministicError()
    {
        var fixture = CreateFixture("GCASH", "AUB", null);

        var result = await fixture.Sut.HandleAsync(DefaultRequest("GCASH"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(422, result.Error!.StatusCode);
        Assert.Equal("PAYMENT_PROVIDER_MAPPING_NOT_SUPPORTED", result.Error.ErrorCode);
        Assert.False(fixture.CreatePaymentAttemptWasCalled);
        Assert.Null(fixture.CapturedInitiateRequest);
    }

    /// <summary>
    /// Verifies ticketReference works without QR source metadata.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenTicketReferenceProvided_DoesNotRequireQrSourceMetadata()
    {
        var fixture = CreateFixture("MAYA", "PAYMONGO", null);
        var request = DefaultRequest("MAYA");
        request.PlateNumber = null;
        request.TicketReference = "TICKET-QR-NORMALIZED-001";

        var result = await fixture.Sut.HandleAsync(request, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("TICKET-QR-NORMALIZED-001", fixture.CapturedTicketReference);
        Assert.Equal(CorrelationId, result.Response!.CorrelationId);
    }

    /// <summary>
    /// Verifies missing plate and ticket data returns a bad request.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenPlateAndTicketMissing_ReturnsBadRequest()
    {
        var fixture = CreateFixture("CARD", "AUB", "PAYMONGO");
        var request = DefaultRequest("CARD");
        request.PlateNumber = null;
        request.TicketReference = null;

        var result = await fixture.Sut.HandleAsync(request, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.False(fixture.ResolveVendorParkingWasCalled);
    }

    /// <summary>
    /// Verifies vendor not found maps to a 404 response without provider routing.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenVendorNotFound_ReturnsNotFound()
    {
        var fixture = CreateFixture("CARD", "AUB", "PAYMONGO");
        fixture.CentralPms.ResolveResult = CentralPmsWebPayResult<CentralPmsResolvedParking>.Failure(
            new CentralPmsWebPayError(404, "SESSION_NOT_FOUND", "Vendor parking session was not found.", false));

        var result = await fixture.Sut.HandleAsync(DefaultRequest("CARD"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Error!.StatusCode);
        Assert.Equal("SESSION_NOT_FOUND", result.Error.ErrorCode);
        Assert.Null(fixture.CapturedRouteRequest);
    }

    /// <summary>
    /// Verifies Central PMS payment attempt creation or reuse happens before provider handoff creation.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenPlateResolved_ReturnsPaymentHandoff()
    {
        var fixture = CreateFixture("CARD", "AUB", "PAYMONGO");

        var result = await fixture.Sut.HandleAsync(DefaultRequest("CARD"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(PaymentAttemptId, result.Response!.PaymentAttemptId);
        Assert.Equal(ParkingSessionId, result.Response.ParkingSessionId);
        Assert.Equal(TariffSnapshotId, result.Response.TariffSnapshotId);
        Assert.Equal(12500, result.Response.AmountMinorUnits);
        Assert.Equal("PHP", result.Response.Currency);
        Assert.Equal("https://payments.test/handoff", result.Response.Handoff.HandoffUrl);
        Assert.DoesNotContain("merchantReferenceNumber", SerializePublicResponse(result.Response), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("providerProduct", SerializePublicResponse(result.Response), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawResponse", SerializePublicResponse(result.Response), StringComparison.OrdinalIgnoreCase);
    }

    private static Fixture CreateFixture(
        string paymentMethod,
        string? selectedProvider,
        string? fallbackProvider,
        bool isRouted = true,
        string routingReason = "PRIMARY_PROVIDER",
        string? errorCode = null)
    {
        var centralPms = new FakeCentralPmsWebPayClient();
        var routing = new CapturingRoutingPolicyResolver(
            new ResolvePaymentProviderRouteResponse(
                isRouted,
                paymentMethod,
                selectedProvider,
                fallbackProvider,
                Guid.Parse("77777777-7777-7777-7777-777777777777"),
                routingReason,
                fallbackProvider is not null,
                CorrelationId,
                errorCode));
        var handoff = new CapturingProviderPaymentHandoffInitiator();

        var sut = new WebPayPaymentIntentHandler(
            centralPms,
            routing,
            new ProviderProductResolver(),
            handoff,
            NullLogger<WebPayPaymentIntentHandler>.Instance);

        return new Fixture(sut, centralPms, routing, handoff);
    }

    private static WebPayPaymentIntentRequest DefaultRequest(string paymentMethod)
    {
        return new WebPayPaymentIntentRequest
        {
            SiteGroupId = SiteGroupId,
            SiteId = SiteId,
            VendorSystemId = "HIKCENTRAL",
            PlateNumber = "ABC1234",
            PaymentMethod = paymentMethod,
            CorrelationId = CorrelationId
        };
    }

    private static string SerializePublicResponse(object response)
    {
        return System.Text.Json.JsonSerializer.Serialize(response);
    }

    private sealed class Fixture
    {
        public Fixture(
            WebPayPaymentIntentHandler sut,
            FakeCentralPmsWebPayClient centralPms,
            CapturingRoutingPolicyResolver routing,
            CapturingProviderPaymentHandoffInitiator handoff)
        {
            Sut = sut;
            CentralPms = centralPms;
            Routing = routing;
            Handoff = handoff;
        }

        public WebPayPaymentIntentHandler Sut { get; }

        public FakeCentralPmsWebPayClient CentralPms { get; }

        public CapturingRoutingPolicyResolver Routing { get; }

        public CapturingProviderPaymentHandoffInitiator Handoff { get; }

        public ResolvePaymentProviderRouteRequest? CapturedRouteRequest => Routing.CapturedRequest;

        public InitiateProviderPaymentRequest? CapturedInitiateRequest => Handoff.CapturedRequest;

        public string? CapturedPaymentProvider => CentralPms.CapturedPaymentProvider;

        public string? CapturedPaymentMethod => CentralPms.CapturedPaymentMethod;

        public string? CapturedTicketReference => CentralPms.CapturedTicketReference;

        public bool ResolveVendorParkingWasCalled => CentralPms.ResolveVendorParkingWasCalled;

        public bool CreatePaymentAttemptWasCalled => CentralPms.CreatePaymentAttemptWasCalled;
    }

    private sealed class FakeCentralPmsWebPayClient : ICentralPmsWebPayClient
    {
        public CentralPmsWebPayResult<CentralPmsResolvedParking> ResolveResult { get; set; } =
            CentralPmsWebPayResult<CentralPmsResolvedParking>.Success(new CentralPmsResolvedParking(
                ParkingSessionId,
                TariffSnapshotId,
                12500,
                "PHP",
                "HIKCENTRAL",
                CorrelationId));

        public bool ResolveVendorParkingWasCalled { get; private set; }

        public bool CreatePaymentAttemptWasCalled { get; private set; }

        public string? CapturedPaymentProvider { get; private set; }

        public string? CapturedPaymentMethod { get; private set; }

        public string? CapturedTicketReference { get; private set; }

        public Task<CentralPmsWebPayResult<CentralPmsResolvedParking>> ResolveVendorParkingAsync(
            Guid? siteGroupId,
            Guid? siteId,
            string vendorSystemId,
            string? plateNumber,
            string? ticketReference,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            ResolveVendorParkingWasCalled = true;
            CapturedTicketReference = ticketReference;
            return Task.FromResult(ResolveResult);
        }

        public Task<CentralPmsWebPayResult<CentralPmsPaymentAttempt>> CreateOrReusePaymentAttemptAsync(
            Guid parkingSessionId,
            Guid tariffSnapshotId,
            string paymentProvider,
            string paymentMethod,
            string idempotencyKey,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            CreatePaymentAttemptWasCalled = true;
            CapturedPaymentProvider = paymentProvider;
            CapturedPaymentMethod = paymentMethod;

            return Task.FromResult(CentralPmsWebPayResult<CentralPmsPaymentAttempt>.Success(
                new CentralPmsPaymentAttempt(PaymentAttemptId, "PENDING_PROVIDER", paymentProvider, false)));
        }
    }

    private sealed class CapturingRoutingPolicyResolver : IPaymentProviderRoutingPolicyResolver
    {
        private readonly ResolvePaymentProviderRouteResponse _response;

        public CapturingRoutingPolicyResolver(ResolvePaymentProviderRouteResponse response)
        {
            _response = response;
        }

        public ResolvePaymentProviderRouteRequest? CapturedRequest { get; private set; }

        public Task<ResolvePaymentProviderRouteResponse> ResolveAsync(
            ResolvePaymentProviderRouteRequest request,
            CancellationToken cancellationToken)
        {
            CapturedRequest = request;
            return Task.FromResult(_response);
        }
    }

    private sealed class CapturingProviderPaymentHandoffInitiator : IProviderPaymentHandoffInitiator
    {
        public InitiateProviderPaymentRequest? CapturedRequest { get; private set; }

        public Task<InitiateProviderPaymentResponse> InitiateAsync(
            InitiateProviderPaymentRequest request,
            CancellationToken cancellationToken)
        {
            CapturedRequest = request;
            return Task.FromResult(new InitiateProviderPaymentResponse(
                request.PaymentAttemptId,
                request.ProviderCode,
                request.ProviderProduct,
                "session_test_001",
                "reference_test_001",
                "PENDING_PROVIDER",
                new ProviderHandoffDto(
                    ProviderHandoffType.Redirect,
                    "https://payments.test/handoff",
                    null,
                    null,
                    null,
                    null,
                    DateTimeOffset.UtcNow.AddMinutes(15)),
                DateTimeOffset.UtcNow.AddMinutes(15)));
        }
    }
}
