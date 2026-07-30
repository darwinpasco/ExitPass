using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Persistence;
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
    private const string ResolveRoute = "/v1/webpay/parking-session";
    private const string StatutoryDecisionRoute = "/v1/webpay/statutory-discounts/decisions";
    private static readonly Guid StatutoryDecisionCommandId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid StatutoryApplicationCommandId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

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
        var state = new WebPayEndpointState("QRPH", "PAYMONGO", null);
        using var client = CreateClient(state);

        using var response = await client.PostAsJsonAsync(Route, DefaultRequest("QRPH"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<WebPayPaymentIntentResponse>();
        Assert.NotNull(body);
        Assert.Equal("PAYMONGO", body!.SelectedProviderCode);
        Assert.Null(body.FallbackProviderCode);
        Assert.Equal("https://payments.test/handoff", body.Handoff.HandoffUrl);
        Assert.Equal("PAYMONGO_CHECKOUT_SESSION", state.CapturedPaymentProvider);
        Assert.Equal("QRPH", state.CapturedPaymentMethod);
        Assert.Equal("PAYMONGO_CHECKOUT_SESSION", state.CapturedInitiateRequest!.ProviderProduct);
    }

    /// <summary>
    /// Verifies identical WebPay payment-intent replay returns the durable provider handoff without initiating a new provider session.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenPaymentAttemptReplayHasProviderSession_ReturnsExistingHandoffWithoutProviderCall()
    {
        var state = new WebPayEndpointState("QRPH", "PAYMONGO", null);
        state.LatestProviderSessionByPaymentAttempt = new ProviderSessionRecord(
            Guid.Parse("88888888-8888-8888-8888-888888888888"),
            Guid.Parse("66666666-6666-6666-6666-666666666666"),
            "PAYMONGO",
            "PAYMONGO_CHECKOUT_SESSION",
            "cs_test_replay",
            "reference_test_replay",
            "PENDING",
            "https://payments.test/replay-checkout",
            null,
            DateTimeOffset.Parse("2026-05-16T12:15:00Z"),
            "existing-idempotency-key",
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "{}",
            "{}",
            DateTimeOffset.Parse("2026-05-16T12:00:00Z"));
        using var client = CreateClient(state);

        using var response = await client.PostAsJsonAsync(Route, DefaultRequest("QRPH"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(state.CapturedInitiateRequest);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal("66666666-6666-6666-6666-666666666666", root.GetProperty("paymentAttemptId").GetString());
        Assert.Equal("PENDING", root.GetProperty("status").GetString());
        Assert.Equal("https://payments.test/replay-checkout", root.GetProperty("handoff").GetProperty("handoffUrl").GetString());
        Assert.DoesNotContain("providerSessionRef", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ux_provider_sessions", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies ticketReference can drive the same flow without QR-source metadata.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenTicketReferenceProvided_DoesNotRequireQrSourceMetadata()
    {
        var state = new WebPayEndpointState("QRPH", "PAYMONGO", null);
        using var client = CreateClient(state);
        var request = DefaultRequest("QRPH");
        request.PlateNumber = null;
        request.TicketReference = "TICKET-FROM-FUTURE-QR-SCAN";

        using var response = await client.PostAsJsonAsync(Route, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("TICKET-FROM-FUTURE-QR-SCAN", state.CapturedTicketReference);
        Assert.Equal("PAYMONGO_CHECKOUT_SESSION", state.CapturedPaymentProvider);
        Assert.Equal("QRPH", state.CapturedPaymentMethod);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("qrSource", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("camera", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies stale QRPH-to-AUB routing is rejected without creating an attempt or provider handoff.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenQrphRouteSelectsAub_ReturnsRoutingRegressionError()
    {
        var state = new WebPayEndpointState("QRPH", "AUB", "PAYMONGO");
        using var client = CreateClient(state);

        using var response = await client.PostAsJsonAsync(Route, DefaultRequest("QRPH"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.False(state.CreatePaymentAttemptWasCalled);
        Assert.Null(state.CapturedInitiateRequest);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal("WEBPAY_PAYMONGO_PROVIDER_ROUTE_REGRESSION", root.GetProperty("errorCode").GetString());
        Assert.Equal("AUB", root.GetProperty("selectedProviderCode").GetString());
        Assert.Equal("PAYMONGO", root.GetProperty("fallbackProviderCode").GetString());
    }

    /// <summary>
    /// Verifies WebPay methods routed to PayMongo send PayMongo's Central PMS provider rail while preserving the selected method.
    /// </summary>
    [Theory]
    [InlineData("QRPH")]
    [InlineData("GCASH")]
    [InlineData("MAYA")]
    [InlineData("CARD")]
    public async Task WebPayPaymentIntent_WhenAllowedMethodRoutesToPayMongo_SendsCheckoutSessionProviderAndSelectedMethod(
        string paymentMethod)
    {
        var state = new WebPayEndpointState(paymentMethod, "PAYMONGO", null);
        using var client = CreateClient(state);

        using var response = await client.PostAsJsonAsync(Route, DefaultRequest(paymentMethod));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("PAYMONGO_CHECKOUT_SESSION", state.CapturedPaymentProvider);
        Assert.Equal(paymentMethod, state.CapturedPaymentMethod);
        Assert.NotEqual(state.CapturedPaymentMethod, state.CapturedPaymentProvider);
        Assert.Equal("PAYMONGO_CHECKOUT_SESSION", state.CapturedInitiateRequest!.ProviderProduct);
    }

    /// <summary>
    /// Verifies unsupported WebPay methods are rejected before vendor resolution or payment attempt creation.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenUnsupportedMethodRequested_ReturnsUnsupportedPaymentMethod()
    {
        var state = new WebPayEndpointState("BANK_TRANSFER", "PAYMONGO", null);
        using var client = CreateClient(state);

        using var response = await client.PostAsJsonAsync(Route, DefaultRequest("BANK_TRANSFER"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.False(state.ResolveVendorParkingWasCalled);
        Assert.False(state.CreatePaymentAttemptWasCalled);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("UNSUPPORTED_PAYMENT_METHOD", body.RootElement.GetProperty("errorCode").GetString());
    }

    /// <summary>
    /// Verifies provider routing errors return a deterministic validation response.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenPreferredProviderUnsupported_ReturnsValidationError()
    {
        var state = new WebPayEndpointState("QRPH", null, null, false, "PREFERRED_PROVIDER_UNSUPPORTED");
        using var client = CreateClient(state);
        var request = DefaultRequest("QRPH");
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
        var state = new WebPayEndpointState("QRPH", "PAYMONGO", null);
        state.ResolveResult = CentralPmsWebPayResult<CentralPmsResolvedParking>.Failure(
            new CentralPmsWebPayError(404, "SESSION_NOT_FOUND", "Vendor parking session was not found.", false));
        using var client = CreateClient(state);

        using var response = await client.PostAsJsonAsync(Route, DefaultRequest("QRPH"));

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
        var state = new WebPayEndpointState("QRPH", "PAYMONGO", null);
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
    /// Verifies active payment attempt conflicts surface a provider-neutral resume URL when an existing checkout URL is available.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenActivePaymentAttemptHasCheckoutUrl_ReturnsResumePaymentUrl()
    {
        var state = new WebPayEndpointState("QRPH", "PAYMONGO", null);
        state.CreateAttemptResult = CentralPmsWebPayResult<CentralPmsPaymentAttempt>.Failure(
            new CentralPmsWebPayError(
                409,
                "ACTIVE_PAYMENT_ATTEMPT_EXISTS",
                "An active payment attempt already exists for parking session.",
                false,
                Guid.Parse("33333333-3333-3333-3333-333333333333")));
        state.LatestActiveProviderSession = new ProviderSessionRecord(
            Guid.Parse("88888888-8888-8888-8888-888888888888"),
            Guid.Parse("66666666-6666-6666-6666-666666666666"),
            "PAYMONGO",
            "PAYMONGO_CHECKOUT_SESSION",
            "cs_test_existing",
            "reference_test_existing",
            "PENDING_PROVIDER",
            "https://payments.test/existing-checkout",
            null,
            DateTimeOffset.Parse("2026-05-16T12:15:00Z"),
            "existing-idempotency-key",
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "{}",
            "{}",
            DateTimeOffset.Parse("2026-05-16T12:00:00Z"));
        using var client = CreateClient(state);

        using var response = await client.PostAsJsonAsync(Route, DefaultRequest("QRPH"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True(state.CreatePaymentAttemptWasCalled);
        Assert.Null(state.CapturedInitiateRequest);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal("ACTIVE_PAYMENT_ATTEMPT_EXISTS", root.GetProperty("errorCode").GetString());
        Assert.Equal("https://payments.test/existing-checkout", root.GetProperty("resumePaymentUrl").GetString());
        Assert.Equal("https://payments.test/existing-checkout", root.GetProperty("handoffUrl").GetString());
        Assert.DoesNotContain("providerSessionRef", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies orphan active payment attempts are finalized through Central PMS and do not remain a permanent WebPay retry blocker.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenActivePaymentAttemptIsOrphan_RecoversAndReturnsFreshHandoff()
    {
        var state = new WebPayEndpointState("QRPH", "PAYMONGO", null);
        state.EnqueueCreateAttemptResult(CentralPmsWebPayResult<CentralPmsPaymentAttempt>.Failure(
            new CentralPmsWebPayError(
                409,
                "ACTIVE_PAYMENT_ATTEMPT_EXISTS",
                "An active payment attempt already exists for parking session.",
                false,
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Guid.Parse("66666666-6666-6666-6666-666666666666"))));
        state.EnqueueCreateAttemptResult(CentralPmsWebPayResult<CentralPmsPaymentAttempt>.Success(
            new CentralPmsPaymentAttempt(
                Guid.Parse("99999999-9999-9999-9999-999999999999"),
                "PENDING_PROVIDER",
                "PAYMONGO_CHECKOUT_SESSION",
                false)));
        using var client = CreateClient(state);

        using var response = await client.PostAsJsonAsync(Route, DefaultRequest("QRPH"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, state.CreatePaymentAttemptCallCount);
        Assert.Equal(1, state.FinalizePaymentAttemptCallCount);
        Assert.Equal("FAILED", state.FinalAttemptStatus);
        Assert.NotNull(state.CapturedInitiateRequest);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("https://payments.test/handoff", body.RootElement.GetProperty("handoff").GetProperty("handoffUrl").GetString());
    }

    /// <summary>
    /// Verifies parking session resolution returns safe summary fields before payment attempt creation.
    /// </summary>
    [Fact]
    public async Task WebPayParkingSessionResolve_WhenSessionResolved_ReturnsSummaryWithoutCreatingPaymentAttempt()
    {
        var state = new WebPayEndpointState("QRPH", "PAYMONGO", null);
        state.ResolveResult = CentralPmsWebPayResult<CentralPmsResolvedParking>.Success(new CentralPmsResolvedParking(
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            10000,
            "PHP",
            "HIKCENTRAL",
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "Mactan Newtown Parking",
            "TICKET-TEST-027",
            "ABC 1234",
            DateTimeOffset.Parse("2026-05-18T10:42:00+08:00"),
            DateTimeOffset.Parse("2026-05-18T11:15:00+08:00"),
            "Weekend Rate",
            "PAYABLE",
            DateTimeOffset.Parse("2026-05-18T11:30:00+08:00"),
            "Not Started",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "WebPay Test Site Group"));
        using var client = CreateClient(state);

        using var response = await client.PostAsJsonAsync(ResolveRoute, DefaultRequest("QRPH"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(state.ResolveVendorParkingWasCalled);
        Assert.False(state.CreatePaymentAttemptWasCalled);
        Assert.Null(state.CapturedRouteRequest);
        Assert.Null(state.CapturedInitiateRequest);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal("11111111-1111-1111-1111-111111111111", root.GetProperty("siteGroupId").GetString());
        Assert.Equal("22222222-2222-2222-2222-222222222222", root.GetProperty("siteId").GetString());
        Assert.Equal("HIKCENTRAL", root.GetProperty("vendorSystemId").GetString());
        Assert.Equal("WebPay Test Site Group", root.GetProperty("siteGroupName").GetString());
        Assert.Equal("Mactan Newtown Parking", root.GetProperty("siteName").GetString());
        Assert.Equal("TICKET-TEST-027", root.GetProperty("ticketReference").GetString());
        Assert.Equal("PAYABLE", root.GetProperty("parkingStatus").GetString());
        Assert.Equal(10000, root.GetProperty("amountMinorUnits").GetInt32());
        Assert.NotEqual("2030-04-01T01:45:00+00:00", root.GetProperty("feeValidUntil").GetString());
    }

    /// <summary>
    /// Verifies missing plate and ticket fields are rejected before any backend calls.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenPlateAndTicketMissing_ReturnsBadRequest()
    {
        var state = new WebPayEndpointState("QRPH", "PAYMONGO", null);
        using var client = CreateClient(state);
        var request = DefaultRequest("QRPH");
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
        var state = new WebPayEndpointState("QRPH", "PAYMONGO", null);
        using var client = CreateClient(state);

        using var response = await client.PostAsJsonAsync(Route, DefaultRequest("QRPH"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("merchantReferenceNumber", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("providerProduct", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawResponse", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies the WebPay statutory-discount submit endpoint accepts only browser-safe facts and forwards idempotency.
    /// </summary>
    [Fact]
    public async Task WebPayStatutoryDiscountSubmit_WhenRequestIsValid_ReturnsBrowserSafeReadback()
    {
        var state = new WebPayEndpointState("QRPH", "PAYMONGO", null);
        using var client = CreateClient(state);
        using var request = new HttpRequestMessage(HttpMethod.Post, StatutoryDecisionRoute)
        {
            Content = JsonContent.Create(StatutoryDecisionRequest())
        };
        request.Headers.Add("Idempotency-Key", "statutory-decision:webpay:test");
        request.Headers.Add("X-Correlation-Id", "33333333-3333-3333-3333-333333333333");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<WebPayStatutoryDiscountDecisionResponse>();
        Assert.NotNull(body);
        Assert.Equal(StatutoryDecisionCommandId, body!.StatutoryDiscountDecisionCommandId);
        Assert.Equal("SENIOR_CITIZEN", body.EntitlementType);
        Assert.Equal("READY", body.PayableBasisReadinessStatus);
        Assert.Equal("statutory-decision:webpay:test", state.CapturedStatutorySubmitIdempotencyKey);
        Assert.Equal("SENIOR_CITIZEN", state.CapturedStatutorySubmitRequest!.EntitlementType);
    }

    /// <summary>
    /// Verifies the WebPay statutory-discount submit endpoint rejects missing idempotency.
    /// </summary>
    [Fact]
    public async Task WebPayStatutoryDiscountSubmit_WhenIdempotencyKeyMissing_ReturnsBadRequest()
    {
        var state = new WebPayEndpointState("QRPH", "PAYMONGO", null);
        using var client = CreateClient(state);

        using var response = await client.PostAsJsonAsync(StatutoryDecisionRoute, StatutoryDecisionRequest());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(state.CapturedStatutorySubmitRequest);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("IDEMPOTENCY_KEY_REQUIRED", body);
    }

    /// <summary>
    /// Verifies upstream Central PMS authentication failures are translated to a customer-safe service posture.
    /// </summary>
    [Fact]
    public async Task WebPayStatutoryDiscountSubmit_WhenCentralPmsAuthenticationFails_ReturnsSafeServiceUnavailable()
    {
        var state = new WebPayEndpointState("QRPH", "PAYMONGO", null)
        {
            StatutoryDecisionResult = CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>.Failure(
                new CentralPmsWebPayError(
                    401,
                    "CENTRAL_PMS_AUTHENTICATED_ACTOR_REQUIRED",
                    "Authenticated user or service identity is required for statutory-discount decision submission.",
                    false,
                    Guid.Parse("33333333-3333-3333-3333-333333333333")))
        };
        using var client = CreateClient(state);
        using var request = new HttpRequestMessage(HttpMethod.Post, StatutoryDecisionRoute)
        {
            Content = JsonContent.Create(StatutoryDecisionRequest())
        };
        request.Headers.Add("Idempotency-Key", "statutory-decision:webpay:test");
        request.Headers.Add("X-Correlation-Id", "33333333-3333-3333-3333-333333333333");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("WEBPAY_STATUTORY_SERVICE_UNAVAILABLE", body);
        Assert.Contains("Parking-privilege requests are temporarily unavailable", body);
        Assert.DoesNotContain("Authenticated user", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("service identity", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CENTRAL_PMS_AUTHENTICATED_ACTOR_REQUIRED", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("X-ExitPass", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("permission", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies upstream Central PMS authorization failures do not leak policy or permission details.
    /// </summary>
    [Fact]
    public async Task WebPayStatutoryDiscountReadback_WhenCentralPmsAuthorizationFails_ReturnsSafeServiceUnavailable()
    {
        var state = new WebPayEndpointState("QRPH", "PAYMONGO", null)
        {
            StatutoryDecisionResult = CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>.Failure(
                new CentralPmsWebPayError(
                    403,
                    "CENTRAL_PMS_SOURCE_CHANNEL_FORBIDDEN",
                    "The caller is not authorized to submit statutory-discount decisions for a supported source channel.",
                    false,
                    Guid.Parse("33333333-3333-3333-3333-333333333333")))
        };
        using var client = CreateClient(state);

        using var response = await client.GetAsync($"{StatutoryDecisionRoute}/{StatutoryDecisionCommandId:D}");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("WEBPAY_STATUTORY_SERVICE_UNAVAILABLE", body);
        Assert.DoesNotContain("authorized", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("source channel", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CENTRAL_PMS_SOURCE_CHANNEL_FORBIDDEN", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("permission", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies transient Central PMS failures are translated to retryable customer guidance.
    /// </summary>
    [Fact]
    public async Task WebPayStatutoryDiscountSubmit_WhenCentralPmsUnavailable_ReturnsSafeRetryableGuidance()
    {
        var state = new WebPayEndpointState("QRPH", "PAYMONGO", null)
        {
            StatutoryDecisionResult = CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>.Failure(
                new CentralPmsWebPayError(
                    503,
                    "CENTRAL_PMS_TIMEOUT",
                    "Central PMS request failed.",
                    true,
                    Guid.Parse("33333333-3333-3333-3333-333333333333")))
        };
        using var client = CreateClient(state);
        using var request = new HttpRequestMessage(HttpMethod.Post, StatutoryDecisionRoute)
        {
            Content = JsonContent.Create(StatutoryDecisionRequest())
        };
        request.Headers.Add("Idempotency-Key", "statutory-decision:webpay:test");
        request.Headers.Add("X-Correlation-Id", "33333333-3333-3333-3333-333333333333");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("WEBPAY_STATUTORY_REQUEST_TEMPORARILY_UNAVAILABLE", body);
        Assert.Contains("Please try again", body);
        Assert.Contains("\"retryable\":true", body);
        Assert.DoesNotContain("Central PMS", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies safe Central PMS validation guidance is preserved for customer-correctable input errors.
    /// </summary>
    [Fact]
    public async Task WebPayStatutoryDiscountSubmit_WhenCentralPmsValidationFails_PreservesSafeGuidance()
    {
        var state = new WebPayEndpointState("QRPH", "PAYMONGO", null)
        {
            StatutoryDecisionResult = CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>.Failure(
                new CentralPmsWebPayError(
                    400,
                    "STATUTORY_DISCOUNT_REQUEST_INVALID",
                    "maskedIdReference must be masked.",
                    false,
                    Guid.Parse("33333333-3333-3333-3333-333333333333")))
        };
        using var client = CreateClient(state);
        using var request = new HttpRequestMessage(HttpMethod.Post, StatutoryDecisionRoute)
        {
            Content = JsonContent.Create(StatutoryDecisionRequest())
        };
        request.Headers.Add("Idempotency-Key", "statutory-decision:webpay:test");
        request.Headers.Add("X-Correlation-Id", "33333333-3333-3333-3333-333333333333");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("STATUTORY_DISCOUNT_REQUEST_INVALID", body);
        Assert.Contains("maskedIdReference must be masked.", body);
    }

    /// <summary>
    /// Verifies statutory business conflicts remain deterministic and are not flattened to service failures.
    /// </summary>
    [Fact]
    public async Task WebPayStatutoryDiscountSubmit_WhenCentralPmsConflictOccurs_PreservesSafeConflict()
    {
        var state = new WebPayEndpointState("QRPH", "PAYMONGO", null)
        {
            StatutoryDecisionResult = CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>.Failure(
                new CentralPmsWebPayError(
                    409,
                    "STATUTORY_DISCOUNT_DECISION_SEMANTIC_CONFLICT",
                    "The statutory-discount request already exists with different submitted facts.",
                    false,
                    Guid.Parse("33333333-3333-3333-3333-333333333333")))
        };
        using var client = CreateClient(state);
        using var request = new HttpRequestMessage(HttpMethod.Post, StatutoryDecisionRoute)
        {
            Content = JsonContent.Create(StatutoryDecisionRequest())
        };
        request.Headers.Add("Idempotency-Key", "statutory-decision:webpay:test");
        request.Headers.Add("X-Correlation-Id", "33333333-3333-3333-3333-333333333333");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("STATUTORY_DISCOUNT_DECISION_SEMANTIC_CONFLICT", body);
        Assert.Contains("different submitted facts", body);
    }

    /// <summary>
    /// Verifies durable statutory-discount readback is exposed without mutation.
    /// </summary>
    [Fact]
    public async Task WebPayStatutoryDiscountReadback_WhenDecisionExists_ReturnsDurableState()
    {
        var state = new WebPayEndpointState("QRPH", "PAYMONGO", null);
        using var client = CreateClient(state);

        using var response = await client.GetAsync($"{StatutoryDecisionRoute}/{StatutoryDecisionCommandId:D}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<WebPayStatutoryDiscountDecisionResponse>();
        Assert.NotNull(body);
        Assert.Equal(StatutoryApplicationCommandId, body!.StatutoryDiscountPayableBasisApplicationCommandId);
        Assert.True(body.PayableBasisReady);
        Assert.Equal(1, state.GetStatutoryDiscountDecisionCallCount);
        Assert.Equal(0, state.ApplyStatutoryDiscountPayableBasisCallCount);
    }

    /// <summary>
    /// Verifies post-approval application intent reuses the canonical decision route through the Payment Orchestrator.
    /// </summary>
    [Fact]
    public async Task WebPayStatutoryDiscountApplyPayableBasis_WhenRequestMatchesReadback_SubmitsApplicationIntent()
    {
        var state = new WebPayEndpointState("QRPH", "PAYMONGO", null);
        using var client = CreateClient(state);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{StatutoryDecisionRoute}/{StatutoryDecisionCommandId:D}/apply-payable-basis")
        {
            Content = JsonContent.Create(StatutoryDecisionRequest())
        };
        request.Headers.Add("Idempotency-Key", "statutory-application:webpay:test");
        request.Headers.Add("X-Correlation-Id", "33333333-3333-3333-3333-333333333333");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, state.GetStatutoryDiscountDecisionCallCount);
        Assert.Equal(1, state.ApplyStatutoryDiscountPayableBasisCallCount);
        Assert.Equal("statutory-application:webpay:test", state.CapturedStatutoryApplyIdempotencyKey);
        Assert.Equal(StatutoryDecisionCommandId, state.CapturedStatutoryReadbackId);
    }

    /// <summary>
    /// Verifies statutory pending-review readback blocks payment attempt creation through the endpoint.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenStatutoryDecisionAwaitsReview_ReturnsConflictWithoutProviderSideEffects()
    {
        var state = new WebPayEndpointState("QRPH", "PAYMONGO", null)
        {
            StatutoryDecisionResult = CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>.Success(
                StatutoryDecision(payableBasisReady: false, decisionCommandStatus: "AWAITING_REVIEW", decisionResultStatus: "NOT_DECIDED", applicationCommandStatus: "NOT_REQUESTED", readinessStatus: "AWAITING_REVIEW"))
        };
        using var client = CreateClient(state);
        var request = DefaultRequest("QRPH");
        request.StatutoryDiscountDecisionCommandId = StatutoryDecisionCommandId;
        request.TariffSnapshotId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        request.ExpectedAmountMinorUnits = 10000;
        request.ExpectedCurrency = "PHP";

        using var response = await client.PostAsJsonAsync(Route, request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.False(state.CreatePaymentAttemptWasCalled);
        Assert.Null(state.CapturedInitiateRequest);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("STATUTORY_DISCOUNT_AWAITING_REVIEW", body);
    }

    private HttpClient CreateClient(WebPayEndpointState state)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICentralPmsWebPayClient>();
                services.RemoveAll<IPaymentProviderRoutingPolicyResolver>();
                services.RemoveAll<IProviderProductResolver>();
                services.RemoveAll<IProviderPaymentHandoffInitiator>();
                services.RemoveAll<IProviderSessionRepository>();
                services.AddSingleton<ICentralPmsWebPayClient>(state);
                services.AddSingleton<IPaymentProviderRoutingPolicyResolver>(state);
                services.AddSingleton<IProviderProductResolver>(state);
                services.AddSingleton<IProviderPaymentHandoffInitiator>(state);
                services.AddSingleton<IProviderSessionRepository>(state);
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

    private static WebPayStatutoryDiscountDecisionRequest StatutoryDecisionRequest()
    {
        return new WebPayStatutoryDiscountDecisionRequest
        {
            RequestReference = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            ParkingSessionId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            SiteId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            SiteGroupId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            TicketReference = "WEBPAY-REQ-001",
            PlateNumber = "ABC1234",
            EntitlementType = "SENIOR_CITIZEN",
            IdDocumentType = "OSCA",
            IssuingAuthority = "QUEZON_CITY",
            ExpiryDate = DateOnly.Parse("2030-12-31"),
            MaskedIdReference = "SC-****-0001",
            EvidenceCaptureRequested = true,
            EvidenceReferences = new[]
            {
                new WebPayStatutoryDiscountEvidenceReference
                {
                    EvidenceType = "CARD_REFERENCE",
                    CaptureMethod = "CUSTOMER_UPLOAD_REFERENCE",
                    ReferenceNumberMasked = "SC-****-0001",
                    StorageReference = "evidence:webpay:001",
                    VerificationStatus = "PENDING_REVIEW"
                }
            },
            RequesterAttestation = true,
            AttestationNotes = "Customer attests eligibility for review.",
            OriginalTariffSnapshotId = Guid.Parse("55555555-5555-5555-5555-555555555555")
        };
    }

    private static CentralPmsStatutoryDiscountDecision StatutoryDecision(
        bool payableBasisReady = true,
        string decisionCommandStatus = "COMPLETED",
        string? decisionResultStatus = "APPROVED",
        string applicationCommandStatus = "APPLIED",
        string readinessStatus = "READY")
    {
        return new CentralPmsStatutoryDiscountDecision(
            StatutoryDecisionCommandId,
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            "WEBPAY",
            "SENIOR_CITIZEN",
            decisionResultStatus ?? "NOT_DECIDED",
            "STATUTORY",
            null,
            null,
            false,
            12500,
            11161,
            1339,
            "VAT_EXEMPT_SENIOR_CITIZEN",
            2500,
            10000,
            "PHP",
            true,
            true,
            null,
            null,
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            DateTimeOffset.Parse("2026-05-16T12:00:00Z"),
            decisionResultStatus == "APPROVED" ? DateTimeOffset.Parse("2026-05-16T12:05:00Z") : null,
            applicationCommandStatus == "APPLIED" ? DateTimeOffset.Parse("2026-05-16T12:10:00Z") : null,
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            decisionCommandStatus,
            decisionResultStatus ?? "NOT_DECIDED",
            payableBasisReady ? "APPLIED" : "PENDING",
            "statutory-discount-decision:sha256:v2",
            !payableBasisReady,
            payableBasisReady ? "NONE" : "RETRYABLE",
            payableBasisReady ? null : "POLL_READBACK",
            null,
            decisionCommandStatus,
            decisionResultStatus,
            !payableBasisReady,
            payableBasisReady ? "NONE" : "RETRYABLE",
            payableBasisReady ? null : "POLL_READBACK",
            StatutoryApplicationCommandId,
            applicationCommandStatus != "NOT_REQUESTED",
            applicationCommandStatus,
            applicationCommandStatus == "APPLIED" ? "APPLIED" : applicationCommandStatus,
            "statutory-discount-payable-basis-application:sha256:v1",
            !payableBasisReady,
            payableBasisReady ? "NONE" : "RETRYABLE",
            payableBasisReady ? null : "POLL_READBACK",
            payableBasisReady ? "APPLIED" : "PENDING",
            payableBasisReady,
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            payableBasisReady,
            readinessStatus,
            payableBasisReady ? null : "POLL_READBACK");
    }

    private sealed class WebPayEndpointState :
        ICentralPmsWebPayClient,
        IPaymentProviderRoutingPolicyResolver,
        IProviderProductResolver,
        IProviderPaymentHandoffInitiator,
        IProviderSessionRepository
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

        public CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision> StatutoryDecisionResult { get; set; } =
            CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>.Success(StatutoryDecision());

        private readonly Queue<CentralPmsWebPayResult<CentralPmsPaymentAttempt>> _createAttemptResults = new();

        public bool ResolveVendorParkingWasCalled { get; private set; }

        public bool CreatePaymentAttemptWasCalled { get; private set; }

        public int CreatePaymentAttemptCallCount { get; private set; }

        public int FinalizePaymentAttemptCallCount { get; private set; }

        public int GetStatutoryDiscountDecisionCallCount { get; private set; }

        public int ApplyStatutoryDiscountPayableBasisCallCount { get; private set; }

        public Guid? CapturedStatutoryReadbackId { get; private set; }

        public string? CapturedStatutorySubmitIdempotencyKey { get; private set; }

        public string? CapturedStatutoryApplyIdempotencyKey { get; private set; }

        public CentralPmsStatutoryDiscountDecisionRequest? CapturedStatutorySubmitRequest { get; private set; }

        public CentralPmsStatutoryDiscountDecisionRequest? CapturedStatutoryApplyRequest { get; private set; }

        public string? FinalAttemptStatus { get; private set; }

        public string? CapturedPaymentProvider { get; private set; }

        public string? CapturedPaymentMethod { get; private set; }

        public string? CapturedTicketReference { get; private set; }

        public ResolvePaymentProviderRouteRequest? CapturedRouteRequest { get; private set; }

        public InitiateProviderPaymentRequest? CapturedInitiateRequest { get; private set; }

        public ProviderSessionRecord? LatestActiveProviderSession { get; set; }

        public ProviderSessionRecord? LatestProviderSessionByPaymentAttempt { get; set; }

        public ProviderSessionRecord? ReservedProviderSession { get; private set; }

        public void EnqueueCreateAttemptResult(CentralPmsWebPayResult<CentralPmsPaymentAttempt> result)
        {
            _createAttemptResults.Enqueue(result);
        }

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
            CreatePaymentAttemptCallCount++;
            CapturedPaymentProvider = paymentProvider;
            CapturedPaymentMethod = paymentMethod;
            if (_createAttemptResults.Count > 0)
            {
                return Task.FromResult(_createAttemptResults.Dequeue());
            }

            return Task.FromResult(CreateAttemptResult ?? CentralPmsWebPayResult<CentralPmsPaymentAttempt>.Success(
                new CentralPmsPaymentAttempt(PaymentAttemptId, "PENDING_PROVIDER", paymentProvider, false)));
        }

        public Task<CentralPmsWebPayResult<CentralPmsPaymentAttempt>> FinalizePaymentAttemptAsync(
            Guid paymentAttemptId,
            string finalAttemptStatus,
            string requestedBy,
            string idempotencyKey,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            FinalizePaymentAttemptCallCount++;
            FinalAttemptStatus = finalAttemptStatus;

            return Task.FromResult(CentralPmsWebPayResult<CentralPmsPaymentAttempt>.Success(
                new CentralPmsPaymentAttempt(paymentAttemptId, finalAttemptStatus, string.Empty, false)));
        }

        public Task<CentralPmsWebPayResult<CentralPmsWebPayReceiptPresentation>> GetReceiptPresentationAsync(
            Guid paymentAttemptId,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>> SubmitStatutoryDiscountDecisionAsync(
            CentralPmsStatutoryDiscountDecisionRequest request,
            string idempotencyKey,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            CapturedStatutorySubmitRequest = request;
            CapturedStatutorySubmitIdempotencyKey = idempotencyKey;
            return Task.FromResult(StatutoryDecisionResult);
        }

        public Task<CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>> GetStatutoryDiscountDecisionAsync(
            Guid statutoryDiscountDecisionCommandId,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            GetStatutoryDiscountDecisionCallCount++;
            CapturedStatutoryReadbackId = statutoryDiscountDecisionCommandId;
            return Task.FromResult(StatutoryDecisionResult);
        }

        public Task<CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>> ApplyStatutoryDiscountPayableBasisAsync(
            CentralPmsStatutoryDiscountDecisionRequest request,
            string idempotencyKey,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            ApplyStatutoryDiscountPayableBasisCallCount++;
            CapturedStatutoryApplyRequest = request;
            CapturedStatutoryApplyIdempotencyKey = idempotencyKey;
            return Task.FromResult(StatutoryDecisionResult);
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

        public string ResolveProviderProduct(string providerCode, string paymentMethod)
        {
            return "PAYMONGO_CHECKOUT_SESSION";
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

        public Task<ProviderSessionInitiationReservationResult> TryReserveInitiationAsync(
            ProviderSessionInitiationReservation reservation,
            CancellationToken cancellationToken)
        {
            if (LatestProviderSessionByPaymentAttempt?.PaymentAttemptId == reservation.PaymentAttemptId)
            {
                return Task.FromResult(new ProviderSessionInitiationReservationResult(
                    ProviderSessionInitiationReservationOutcome.Existing,
                    LatestProviderSessionByPaymentAttempt));
            }

            if (ReservedProviderSession?.PaymentAttemptId == reservation.PaymentAttemptId)
            {
                return Task.FromResult(new ProviderSessionInitiationReservationResult(
                    ProviderSessionInitiationReservationOutcome.Existing,
                    ReservedProviderSession));
            }

            ReservedProviderSession = new ProviderSessionRecord(
                reservation.ProviderSessionRecordId,
                reservation.PaymentAttemptId,
                "PAYMONGO",
                reservation.ProviderProduct,
                string.Empty,
                null,
                "CREATED",
                null,
                null,
                null,
                reservation.IdempotencyKey,
                reservation.CorrelationId,
                reservation.RequestPayloadJson,
                "{}",
                reservation.CreatedAtUtc,
                reservation.AmountMinorUnits,
                reservation.CurrencyCode);

            return Task.FromResult(new ProviderSessionInitiationReservationResult(
                ProviderSessionInitiationReservationOutcome.Acquired,
                ReservedProviderSession));
        }

        public Task CompleteInitiationAsync(
            Guid providerSessionRecordId,
            ProviderSessionRecord record,
            CancellationToken cancellationToken)
        {
            ReservedProviderSession = record with
            {
                ProviderSessionRecordId = providerSessionRecordId
            };
            LatestProviderSessionByPaymentAttempt = ReservedProviderSession;
            return Task.CompletedTask;
        }

        public Task AddAsync(ProviderSessionRecord record, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ProviderSessionRecord?> FindByProviderSessionIdAsync(
            string providerCode,
            string providerSessionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<ProviderSessionRecord?>(null);
        }

        public Task<ProviderSessionRecord?> FindLatestActiveByParkingSessionIdAsync(
            Guid parkingSessionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(LatestActiveProviderSession);
        }

        public Task<ProviderSessionRecord?> FindLatestByPaymentAttemptIdAsync(
            Guid paymentAttemptId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(LatestProviderSessionByPaymentAttempt?.PaymentAttemptId == paymentAttemptId
                ? LatestProviderSessionByPaymentAttempt
                : null);
        }

        public Task MarkWebhookOutcomeAsync(
            string providerCode,
            string providerSessionId,
            string? providerReference,
            string sessionStatus,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
