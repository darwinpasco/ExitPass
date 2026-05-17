using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;
using ExitPass.PaymentOrchestrator.Contracts.Internal;
using ExitPass.PaymentOrchestrator.Contracts.Payments;
using ExitPass.PaymentOrchestrator.Contracts.Routing;
using ExitPass.PaymentOrchestrator.Contracts.WebPay;
using ExitPass.PaymentOrchestrator.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ExitPass.PaymentOrchestrator.IntegrationTests.WebPay;

/// <summary>
/// Integration tests for the WebPay payment intent endpoint.
/// </summary>
public sealed class WebPayPaymentIntentEndpointIntegrationTests
    : IClassFixture<PaymentOrchestratorWebApplicationFactory>
{
    private const string Route = "/v1/webpay/payment-intents";

    private readonly PaymentOrchestratorWebApplicationFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebPayPaymentIntentEndpointIntegrationTests"/> class.
    /// </summary>
    /// <param name="factory">Payment Orchestrator test factory.</param>
    public WebPayPaymentIntentEndpointIntegrationTests(PaymentOrchestratorWebApplicationFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Verifies plate lookup returns a provider-neutral handoff response.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenPlateResolved_ReturnsPaymentHandoff()
    {
        var state = new WebPayEndpointState("CARD", "AUB", "PAYMONGO");
        using var client = CreateClient(state);

        using var response = await client.PostAsJsonAsync(Route, DefaultRequest("CARD"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<WebPayPaymentIntentResponse>();
        Assert.NotNull(body);
        Assert.Equal("AUB", body!.SelectedProviderCode);
        Assert.Equal("PAYMONGO", body.FallbackProviderCode);
        Assert.Equal("https://payments.test/handoff", body.Handoff.HandoffUrl);
        Assert.Equal("AUB_CARD_CASHIER", state.CapturedPaymentProvider);
        Assert.Equal("CARD", state.CapturedPaymentMethod);
        Assert.Equal("AUB_CARD_CASHIER", state.CapturedInitiateRequest!.ProviderProduct);
    }

    /// <summary>
    /// Verifies ticketReference can drive the same flow without QR-source metadata.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenTicketReferenceProvided_DoesNotRequireQrSourceMetadata()
    {
        var state = new WebPayEndpointState("QRPH", "AUB", "PAYMONGO");
        using var client = CreateClient(state);
        var request = DefaultRequest("QRPH");
        request.PlateNumber = null;
        request.TicketReference = "TICKET-FROM-FUTURE-QR-SCAN";

        using var response = await client.PostAsJsonAsync(Route, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("TICKET-FROM-FUTURE-QR-SCAN", state.CapturedTicketReference);
        Assert.Equal("AUB_QRPH", state.CapturedPaymentProvider);
        Assert.Equal("QRPH", state.CapturedPaymentMethod);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("qrSource", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("camera", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies provider routing errors return a deterministic validation response.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenPreferredProviderUnsupported_ReturnsValidationError()
    {
        var state = new WebPayEndpointState("CARD", null, null, false, "PREFERRED_PROVIDER_UNSUPPORTED");
        using var client = CreateClient(state);
        var request = DefaultRequest("CARD");
        request.PreferredProviderCode = "UNSUPPORTED";

        using var response = await client.PostAsJsonAsync(Route, request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.False(state.CreatePaymentAttemptWasCalled);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("PREFERRED_PROVIDER_UNSUPPORTED", body);
    }

    /// <summary>
    /// Verifies vendor not found maps to not found before routing or provider handoff.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenVendorNotFound_ReturnsNotFound()
    {
        var state = new WebPayEndpointState("CARD", "AUB", "PAYMONGO");
        state.ResolveResult = CentralPmsWebPayResult<CentralPmsResolvedParking>.Failure(
            new CentralPmsWebPayError(404, "SESSION_NOT_FOUND", "Vendor parking session was not found.", false));
        using var client = CreateClient(state);

        using var response = await client.PostAsJsonAsync(Route, DefaultRequest("CARD"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(state.CapturedRouteRequest);
        Assert.Null(state.CapturedInitiateRequest);
    }

    /// <summary>
    /// Verifies active Central PMS payment attempt conflicts are returned as provider-neutral 409 responses.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenCentralPmsReturnsActivePaymentAttemptConflict_Returns409()
    {
        var state = new WebPayEndpointState("QRPH", "AUB", "PAYMONGO");
        state.CreateAttemptResult = CentralPmsWebPayResult<CentralPmsPaymentAttempt>.Failure(
            new CentralPmsWebPayError(
                409,
                "ACTIVE_PAYMENT_ATTEMPT_EXISTS",
                "An active payment attempt already exists for parking session.",
                false,
                Guid.Parse("33333333-3333-3333-3333-333333333333")));
        using var client = CreateClient(state);

        using var response = await client.PostAsJsonAsync(Route, DefaultRequest("QRPH"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True(state.CreatePaymentAttemptWasCalled);
        Assert.Null(state.CapturedInitiateRequest);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ACTIVE_PAYMENT_ATTEMPT_EXISTS", body);
        Assert.Contains("33333333-3333-3333-3333-333333333333", body);
        Assert.DoesNotContain("merchantReferenceNumber", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("providerProduct", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawResponse", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies missing plate and ticket fields are rejected before any backend calls.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenPlateAndTicketMissing_ReturnsBadRequest()
    {
        var state = new WebPayEndpointState("CARD", "AUB", "PAYMONGO");
        using var client = CreateClient(state);
        var request = DefaultRequest("CARD");
        request.PlateNumber = null;
        request.TicketReference = null;

        using var response = await client.PostAsJsonAsync(Route, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(state.ResolveVendorParkingWasCalled);
    }

    /// <summary>
    /// Verifies WebPay response does not expose raw provider DTO fields.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_DoesNotLeakProviderSpecificFields()
    {
        var state = new WebPayEndpointState("GCASH", "PAYMONGO", null);
        using var client = CreateClient(state);

        using var response = await client.PostAsJsonAsync(Route, DefaultRequest("GCASH"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("merchantReferenceNumber", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("providerProduct", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawResponse", body, StringComparison.OrdinalIgnoreCase);
    }

    private HttpClient CreateClient(WebPayEndpointState state)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICentralPmsWebPayClient>();
                services.RemoveAll<IPaymentProviderRoutingPolicyResolver>();
                services.RemoveAll<IProviderPaymentHandoffInitiator>();
                services.AddSingleton<ICentralPmsWebPayClient>(state);
                services.AddSingleton<IPaymentProviderRoutingPolicyResolver>(state);
                services.AddSingleton<IProviderPaymentHandoffInitiator>(state);
            });
        }).CreateClient();
    }

    private static WebPayPaymentIntentRequest DefaultRequest(string paymentMethod)
    {
        return new WebPayPaymentIntentRequest
        {
            SiteGroupId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            SiteId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            VendorSystemId = "HIKCENTRAL",
            PlateNumber = "ABC1234",
            PaymentMethod = paymentMethod,
            CorrelationId = Guid.Parse("33333333-3333-3333-3333-333333333333")
        };
    }

    private sealed class WebPayEndpointState :
        ICentralPmsWebPayClient,
        IPaymentProviderRoutingPolicyResolver,
        IProviderPaymentHandoffInitiator
    {
        private static readonly Guid ParkingSessionId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        private static readonly Guid TariffSnapshotId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        private static readonly Guid PaymentAttemptId = Guid.Parse("66666666-6666-6666-6666-666666666666");

        private readonly string _paymentMethod;
        private readonly string? _selectedProvider;
        private readonly string? _fallbackProvider;
        private readonly bool _isRouted;
        private readonly string? _routeErrorCode;

        public WebPayEndpointState(
            string paymentMethod,
            string? selectedProvider,
            string? fallbackProvider,
            bool isRouted = true,
            string? routeErrorCode = null)
        {
            _paymentMethod = paymentMethod;
            _selectedProvider = selectedProvider;
            _fallbackProvider = fallbackProvider;
            _isRouted = isRouted;
            _routeErrorCode = routeErrorCode;
        }

        public CentralPmsWebPayResult<CentralPmsResolvedParking> ResolveResult { get; set; } =
            CentralPmsWebPayResult<CentralPmsResolvedParking>.Success(new CentralPmsResolvedParking(
                ParkingSessionId,
                TariffSnapshotId,
                10000,
                "PHP",
                "HIKCENTRAL",
                Guid.Parse("33333333-3333-3333-3333-333333333333")));

        public CentralPmsWebPayResult<CentralPmsPaymentAttempt>? CreateAttemptResult { get; set; }

        public bool ResolveVendorParkingWasCalled { get; private set; }

        public bool CreatePaymentAttemptWasCalled { get; private set; }

        public string? CapturedPaymentProvider { get; private set; }

        public string? CapturedPaymentMethod { get; private set; }

        public string? CapturedTicketReference { get; private set; }

        public ResolvePaymentProviderRouteRequest? CapturedRouteRequest { get; private set; }

        public InitiateProviderPaymentRequest? CapturedInitiateRequest { get; private set; }

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
            return Task.FromResult(CreateAttemptResult ?? CentralPmsWebPayResult<CentralPmsPaymentAttempt>.Success(
                new CentralPmsPaymentAttempt(PaymentAttemptId, "PENDING_PROVIDER", paymentProvider, false)));
        }

        public Task<ResolvePaymentProviderRouteResponse> ResolveAsync(
            ResolvePaymentProviderRouteRequest request,
            CancellationToken cancellationToken)
        {
            CapturedRouteRequest = request;
            return Task.FromResult(new ResolvePaymentProviderRouteResponse(
                _isRouted,
                _paymentMethod,
                _selectedProvider,
                _fallbackProvider,
                Guid.Parse("77777777-7777-7777-7777-777777777777"),
                _isRouted ? "PRIMARY_PROVIDER" : "NO_ROUTE",
                _fallbackProvider is not null,
                request.CorrelationId,
                _routeErrorCode));
        }

        public Task<InitiateProviderPaymentResponse> InitiateAsync(
            InitiateProviderPaymentRequest request,
            CancellationToken cancellationToken)
        {
            CapturedInitiateRequest = request;
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
                    DateTimeOffset.Parse("2026-05-16T12:00:00Z")),
                DateTimeOffset.Parse("2026-05-16T12:00:00Z")));
        }
    }
}
