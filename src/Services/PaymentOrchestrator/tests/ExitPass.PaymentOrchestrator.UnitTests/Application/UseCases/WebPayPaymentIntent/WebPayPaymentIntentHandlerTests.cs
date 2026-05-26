using ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Persistence;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;
using ExitPass.PaymentOrchestrator.Application.UseCases.WebPayPaymentIntents;
using ExitPass.PaymentOrchestrator.Contracts.Internal;
using ExitPass.PaymentOrchestrator.Contracts.Payments;
using ExitPass.PaymentOrchestrator.Contracts.Routing;
using ExitPass.PaymentOrchestrator.Contracts.WebPay;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ExitPass.PaymentOrchestrator.UnitTests.Application.UseCases.WebPayPaymentIntent;

/// <summary>
/// Unit tests for <see cref="WebPayPaymentIntentHandler"/>.
/// </summary>
public sealed class WebPayPaymentIntentHandlerTests
{
    private const string GuidPattern = "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}";

    private static readonly Guid SiteGroupId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SiteId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CorrelationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ParkingSessionId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid TariffSnapshotId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid PaymentAttemptId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    /// <summary>
    /// Verifies WebPay provider routing is normalized into Central PMS payment provider rails.
    /// </summary>
    /// <param name="selectedProvider">The provider selected by routing policy.</param>
    /// <param name="paymentMethod">The customer-selected payment method.</param>
    /// <param name="expectedCentralPmsProvider">The Central PMS provider rail code.</param>
    [Theory]
    [InlineData("PAYMONGO", "QRPH", "PAYMONGO_CHECKOUT_SESSION")]
    public async Task WebPayPaymentIntent_WhenRouteIsSupported_SendsCentralPmsProviderRailAndPaymentMethod(
        string selectedProvider,
        string paymentMethod,
        string expectedCentralPmsProvider)
    {
        var fixture = CreateFixture(paymentMethod, selectedProvider, null);

        var result = await fixture.Sut.HandleAsync(DefaultRequest(paymentMethod), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(expectedCentralPmsProvider, fixture.CapturedPaymentProvider);
        Assert.Equal(paymentMethod, fixture.CapturedPaymentMethod);
        Assert.NotEqual(paymentMethod, fixture.CapturedPaymentProvider);
    }

    /// <summary>
    /// Verifies QRPH routes through PayMongo from the DB-backed policy result.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenQrphRequested_SelectsPayMongoFromRoutingPolicy()
    {
        var fixture = CreateFixture("QRPH", "PAYMONGO", null);

        var result = await fixture.Sut.HandleAsync(DefaultRequest("QRPH"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("PAYMONGO", result.Response!.SelectedProviderCode);
        Assert.Null(result.Response.FallbackProviderCode);
        Assert.Equal("QRPH", fixture.CapturedRouteRequest!.PaymentMethod);
        Assert.Equal("PAYMONGO_CHECKOUT_SESSION", fixture.CapturedPaymentProvider);
        Assert.Equal("QRPH", fixture.CapturedPaymentMethod);
        Assert.Equal("PAYMONGO_CHECKOUT_SESSION", fixture.CapturedInitiateRequest!.ProviderProduct);
    }

    /// <summary>
    /// Verifies stale QRPH-to-AUB routing is rejected before payment attempt creation or provider handoff.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenQrphRouteSelectsAub_ReturnsRoutingRegressionError()
    {
        var fixture = CreateFixture("QRPH", "AUB", "PAYMONGO");

        var result = await fixture.Sut.HandleAsync(DefaultRequest("QRPH"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(422, result.Error!.StatusCode);
        Assert.Equal("WEBPAY_QRPH_PROVIDER_ROUTE_REGRESSION", result.Error.ErrorCode);
        Assert.Equal("AUB", result.Error.SelectedProviderCode);
        Assert.Equal("PAYMONGO", result.Error.FallbackProviderCode);
        Assert.Equal("QRPH", fixture.CapturedRouteRequest!.PaymentMethod);
        Assert.False(fixture.CreatePaymentAttemptWasCalled);
        Assert.Null(fixture.CapturedInitiateRequest);
    }

    /// <summary>
    /// Verifies current-day PayMongo checkout display text uses parker-facing context instead of internal UUIDs.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenCreatingPayMongoCheckoutForMay23Ticket_UsesParkerFriendlyDisplayText()
    {
        var fixture = CreateFixture("QRPH", "PAYMONGO", null);
        fixture.CentralPms.ResolveResult = CentralPmsWebPayResult<CentralPmsResolvedParking>.Success(
            new CentralPmsResolvedParking(
                ParkingSessionId,
                TariffSnapshotId,
                10000,
                "PHP",
                "25831de5-7144-4a34-a6ea-4ef2bd65c89c",
                CorrelationId,
                SiteName: "WebPay Test Site 2026-05-23",
                TicketReference: "WEBPAY-20260523-FRESH-001",
                PlateNumber: "WEBPAY001",
                SiteGroupId: SiteGroupId,
                SiteId: SiteId));

        var result = await fixture.Sut.HandleAsync(DefaultRequest("QRPH"), CancellationToken.None);

        Assert.True(result.Succeeded);
        var request = fixture.CapturedInitiateRequest!;
        Assert.Equal("PAYMONGO", result.Response!.SelectedProviderCode);
        Assert.Equal("ExitPass Parking Fee - WEBPAY-20260523-FRESH-001", request.CustomerDisplayName);
        Assert.Equal(
            "Site: WebPay Test Site 2026-05-23  Ticket: WEBPAY-20260523-FRESH-001  Plate: WEBPAY001",
            request.Description);
        Assert.DoesNotContain("Amount:", request.Description);
        Assert.DoesNotContain("PHP 100.00", request.Description);
        Assert.DoesNotMatch(GuidPattern, request.CustomerDisplayName);
        Assert.DoesNotMatch(GuidPattern, request.Description);
        Assert.Equal(PaymentAttemptId.ToString(), request.Metadata["payment_attempt_id"]);
        Assert.Equal(ParkingSessionId.ToString(), request.Metadata["parking_session_id"]);
        Assert.Equal(TariffSnapshotId.ToString(), request.Metadata["tariff_snapshot_id"]);
        Assert.Equal(CorrelationId.ToString(), request.Metadata["correlation_id"]);
        Assert.Equal("WEBPAY-20260523-FRESH-001", request.Metadata["ticket_reference"]);
        Assert.StartsWith("https://webpay.public.test/webpay/payment-return?", request.SuccessUrl);
        Assert.Contains("ticketReference=WEBPAY-20260523-FRESH-001", request.SuccessUrl);
        Assert.Contains($"paymentAttemptId={PaymentAttemptId}", request.SuccessUrl);
        Assert.Contains($"correlationId={CorrelationId}", request.SuccessUrl);
        Assert.Contains("result=success", request.SuccessUrl);
        Assert.StartsWith("https://webpay.public.test/webpay/payment-cancelled?", request.CancelUrl);
        Assert.Contains("ticketReference=WEBPAY-20260523-FRESH-001", request.CancelUrl);
        Assert.Contains($"paymentAttemptId={PaymentAttemptId}", request.CancelUrl);
        Assert.Contains($"correlationId={CorrelationId}", request.CancelUrl);
        Assert.Contains("result=cancelled", request.CancelUrl);
    }

    /// <summary>
    /// Verifies expired historical tariff data is rejected without weakening Central PMS eligibility.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenHistoricalMay21TariffIsRejected_ReturnsTariffEligibilityError()
    {
        var fixture = CreateFixture("QRPH", "PAYMONGO", null);
        fixture.CentralPms.ResolveResult = CentralPmsWebPayResult<CentralPmsResolvedParking>.Success(
            new CentralPmsResolvedParking(
                ParkingSessionId,
                TariffSnapshotId,
                10000,
                "PHP",
                "25831de5-7144-4a34-a6ea-4ef2bd65c89c",
                CorrelationId,
                SiteName: "WebPay Test Site 2026-05-21",
                TicketReference: "WEBPAY-20260521-FRESH-001",
                PlateNumber: "WEBPAY001",
                SiteGroupId: SiteGroupId,
                SiteId: SiteId));
        fixture.CentralPms.CreateAttemptResult = CentralPmsWebPayResult<CentralPmsPaymentAttempt>.Failure(
            new CentralPmsWebPayError(
                409,
                "TARIFF_SNAPSHOT_INVALID",
                "Tariff snapshot is not eligible for payment.",
                false,
                CorrelationId));

        var result = await fixture.Sut.HandleAsync(DefaultRequest("QRPH"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Error!.StatusCode);
        Assert.Equal("TARIFF_SNAPSHOT_INVALID", result.Error.ErrorCode);
        Assert.Contains("not eligible", result.Error.Message);
        Assert.Equal("PAYMONGO_CHECKOUT_SESSION", fixture.CapturedPaymentProvider);
        Assert.Null(fixture.CapturedInitiateRequest);
    }

    /// <summary>
    /// Verifies WebPay cannot create a payment attempt against a stale payable basis after coupon or statutory changes.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenExpectedPayableBasisDoesNotMatchCurrentCentralPmsBasis_ReturnsLockedError()
    {
        var fixture = CreateFixture("QRPH", "PAYMONGO", null);
        var request = DefaultRequest("QRPH");
        request.TariffSnapshotId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        request.ExpectedAmountMinorUnits = 7500;

        var result = await fixture.Sut.HandleAsync(request, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Error!.StatusCode);
        Assert.Equal("PAYABLE_BASIS_LOCKED", result.Error.ErrorCode);
        Assert.False(fixture.CreatePaymentAttemptWasCalled);
        Assert.Null(fixture.CapturedRouteRequest);
        Assert.Null(fixture.CapturedInitiateRequest);
    }

    /// <summary>
    /// Verifies non-QRPH methods are rejected before vendor resolution, routing, attempt creation, or handoff.
    /// </summary>
    [Theory]
    [InlineData("GCASH")]
    [InlineData("MAYA")]
    [InlineData("CARD")]
    public async Task WebPayPaymentIntent_WhenUnsupportedMethodRequested_ReturnsUnsupportedPaymentMethod(string paymentMethod)
    {
        var fixture = CreateFixture(paymentMethod, "PAYMONGO", null);

        var result = await fixture.Sut.HandleAsync(DefaultRequest(paymentMethod), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(422, result.Error!.StatusCode);
        Assert.Equal("UNSUPPORTED_PAYMENT_METHOD", result.Error.ErrorCode);
        Assert.Equal(paymentMethod, result.Error.PaymentMethod);
        Assert.False(fixture.ResolveVendorParkingWasCalled);
        Assert.Null(fixture.CapturedRouteRequest);
        Assert.False(fixture.CreatePaymentAttemptWasCalled);
        Assert.Null(fixture.CapturedInitiateRequest);
    }

    /// <summary>
    /// Verifies preferred provider override is passed to routing and used only through a valid route result.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenPreferredProviderSupported_UsesPreferredProvider()
    {
        var fixture = CreateFixture("QRPH", "PAYMONGO", null, routingReason: "PREFERRED_PROVIDER");
        var request = DefaultRequest("QRPH");
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
        var fixture = CreateFixture("QRPH", null, null, isRouted: false, errorCode: "PREFERRED_PROVIDER_UNSUPPORTED");
        var request = DefaultRequest("QRPH");
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
        var fixture = CreateFixture("QRPH", null, null, isRouted: false, errorCode: "PAYMENT_PROVIDER_MAPPING_NOT_SUPPORTED");

        var result = await fixture.Sut.HandleAsync(DefaultRequest("QRPH"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(422, result.Error!.StatusCode);
        Assert.Equal("PAYMENT_PROVIDER_MAPPING_NOT_SUPPORTED", result.Error.ErrorCode);
        Assert.False(fixture.CreatePaymentAttemptWasCalled);
        Assert.Null(fixture.CapturedInitiateRequest);
    }

    /// <summary>
    /// Verifies provider configuration failures return diagnosable payment-intent errors.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenProviderHandoffConfigurationFails_ReturnsProviderDiagnostics()
    {
        var fixture = CreateFixture("QRPH", "PAYMONGO", null);
        fixture.Handoff.ExceptionToThrow = new InvalidOperationException("PayMongo checkout configuration is required.");

        var result = await fixture.Sut.HandleAsync(DefaultRequest("QRPH"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(502, result.Error!.StatusCode);
        Assert.Equal("PAYMENT_PROVIDER_CONFIGURATION_ERROR", result.Error.ErrorCode);
        Assert.Equal("PAYMONGO", result.Error.SelectedProviderCode);
        Assert.Null(result.Error.FallbackProviderCode);
        Assert.Equal("PAYMONGO_CHECKOUT_SESSION", result.Error.ProviderProduct);
        Assert.Equal(PaymentAttemptId, result.Error.PaymentAttemptId);
        Assert.Contains("PayMongo checkout configuration is required.", result.Error.Message);
    }

    /// <summary>
    /// Verifies PayMongo hosted checkout creation fails closed when no public WebPay return base URL is configured.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenWebPayPublicBaseUrlIsMissing_ReturnsProviderDiagnosticsWithoutHandoff()
    {
        var fixture = CreateFixture(
            "QRPH",
            "PAYMONGO",
            null,
            returnUrlOptions: new WebPayReturnUrlOptions
            {
                PublicBaseUrl = string.Empty,
                PaymentSuccessPath = "/webpay/payment-return",
                PaymentCancelPath = "/webpay/payment-cancelled"
            });

        var result = await fixture.Sut.HandleAsync(DefaultRequest("QRPH"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(502, result.Error!.StatusCode);
        Assert.Equal("PAYMENT_PROVIDER_CONFIGURATION_ERROR", result.Error.ErrorCode);
        Assert.Equal("PAYMONGO", result.Error.SelectedProviderCode);
        Assert.Equal("PAYMONGO_CHECKOUT_SESSION", result.Error.ProviderProduct);
        Assert.Contains("WEBPAY_PUBLIC_BASE_URL", result.Error.Message);
        Assert.True(fixture.CreatePaymentAttemptWasCalled);
        Assert.Null(fixture.CapturedInitiateRequest);
        Assert.Equal(0, fixture.Handoff.InitiateCallCount);
    }

    /// <summary>
    /// Verifies ticketReference works without QR source metadata.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenTicketReferenceProvided_DoesNotRequireQrSourceMetadata()
    {
        var fixture = CreateFixture("QRPH", "PAYMONGO", null);
        var request = DefaultRequest("QRPH");
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
        var fixture = CreateFixture("QRPH", "PAYMONGO", null);
        var request = DefaultRequest("QRPH");
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
        var fixture = CreateFixture("QRPH", "PAYMONGO", null);
        fixture.CentralPms.ResolveResult = CentralPmsWebPayResult<CentralPmsResolvedParking>.Failure(
            new CentralPmsWebPayError(404, "SESSION_NOT_FOUND", "Vendor parking session was not found.", false));

        var result = await fixture.Sut.HandleAsync(DefaultRequest("QRPH"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Error!.StatusCode);
        Assert.Equal("SESSION_NOT_FOUND", result.Error.ErrorCode);
        Assert.Null(fixture.CapturedRouteRequest);
    }

    /// <summary>
    /// Verifies active payment attempt conflicts remain conflicts and do not start a provider handoff.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenCentralPmsReturnsActivePaymentAttemptConflict_Returns409()
    {
        var fixture = CreateFixture("QRPH", "PAYMONGO", null);
        fixture.CentralPms.CreateAttemptResult = CentralPmsWebPayResult<CentralPmsPaymentAttempt>.Failure(
            new CentralPmsWebPayError(
                409,
                "ACTIVE_PAYMENT_ATTEMPT_EXISTS",
                "An active payment attempt already exists for parking session.",
                false,
                CorrelationId));

        var result = await fixture.Sut.HandleAsync(DefaultRequest("QRPH"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Error!.StatusCode);
        Assert.Equal("ACTIVE_PAYMENT_ATTEMPT_EXISTS", result.Error.ErrorCode);
        Assert.Equal(CorrelationId, result.Error.CorrelationId);
        Assert.True(fixture.CreatePaymentAttemptWasCalled);
        Assert.Null(fixture.CapturedInitiateRequest);
    }

    /// <summary>
    /// Verifies active payment conflicts surface an existing provider checkout URL without creating a new handoff.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenActivePaymentAttemptHasProviderSession_ReturnsResumeHandoff()
    {
        var fixture = CreateFixture("QRPH", "PAYMONGO", "AUB");
        fixture.CentralPms.CreateAttemptResult = CentralPmsWebPayResult<CentralPmsPaymentAttempt>.Failure(
            new CentralPmsWebPayError(
                409,
                "ACTIVE_PAYMENT_ATTEMPT_EXISTS",
                "An active payment attempt already exists for parking session.",
                false,
                CorrelationId));
        fixture.ProviderSessions.LatestActiveProviderSession = new ProviderSessionRecord(
            Guid.Parse("99999999-9999-9999-9999-999999999999"),
            PaymentAttemptId,
            "PAYMONGO",
            "PAYMONGO_CHECKOUT_SESSION",
            "cs_test_existing",
            "pi_test_existing",
            "PENDING",
            "https://payments.test/existing-checkout",
            null,
            DateTimeOffset.UtcNow.AddMinutes(15),
            "existing-idempotency-key",
            CorrelationId,
            "{}",
            "{}",
            DateTimeOffset.UtcNow);

        var result = await fixture.Sut.HandleAsync(DefaultRequest("QRPH"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("ACTIVE_PAYMENT_ATTEMPT_EXISTS", result.Error!.ErrorCode);
        Assert.Equal(PaymentAttemptId, result.Error.PaymentAttemptId);
        Assert.Equal("https://payments.test/existing-checkout", result.Error.Handoff!.HandoffUrl);
        Assert.Equal(12500, result.Error.AmountMinorUnits);
        Assert.Equal("PHP", result.Error.Currency);
        Assert.Null(fixture.CapturedInitiateRequest);
    }

    /// <summary>
    /// Verifies Central PMS payment attempt creation or reuse happens before provider handoff creation.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenPlateResolved_ReturnsPaymentHandoff()
    {
        var fixture = CreateFixture("QRPH", "PAYMONGO", null);

        var result = await fixture.Sut.HandleAsync(DefaultRequest("QRPH"), CancellationToken.None);

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

    /// <summary>
    /// Verifies safe Central PMS parking summary fields are passed through to WebPay.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenCentralPmsReturnsSummaryFields_MapsThemToResponse()
    {
        var fixture = CreateFixture("QRPH", "PAYMONGO", null);
        fixture.CentralPms.ResolveResult = CentralPmsWebPayResult<CentralPmsResolvedParking>.Success(
            new CentralPmsResolvedParking(
                ParkingSessionId,
                TariffSnapshotId,
                12500,
                "PHP",
                "HIKCENTRAL",
                CorrelationId,
                "Mactan Newtown Parking",
                "TICKET-TEST-023",
                "ABC 1234",
                DateTimeOffset.Parse("2026-05-18T10:42:00+08:00"),
                DateTimeOffset.Parse("2026-05-18T11:15:00+08:00"),
                "Weekend Rate",
                "PAYABLE",
                DateTimeOffset.Parse("2026-05-18T11:30:00+08:00"),
                "Not Started",
                SiteGroupId,
                SiteId,
                "WebPay Test Site Group"));

        var result = await fixture.Sut.HandleAsync(DefaultRequest("QRPH"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Mactan Newtown Parking", result.Response!.SiteName);
        Assert.Equal(SiteGroupId, result.Response.SiteGroupId);
        Assert.Equal(SiteId, result.Response.SiteId);
        Assert.Equal("HIKCENTRAL", result.Response.VendorSystemId);
        Assert.Equal("WebPay Test Site Group", result.Response.SiteGroupName);
        Assert.Equal("TICKET-TEST-023", result.Response.TicketReference);
        Assert.Equal("ABC 1234", result.Response.PlateNumber);
        Assert.Equal("Weekend Rate", result.Response.TariffName);
        Assert.Equal("Pending Payment", result.Response.PaymentStatus);
        Assert.Equal(DateTimeOffset.Parse("2026-05-18T11:30:00+08:00"), result.Response.FeeValidUntil);
    }

    /// <summary>
    /// Verifies the pre-payment parking session resolve path returns summary fields without creating payment records.
    /// </summary>
    [Fact]
    public async Task WebPayParkingSessionResolve_WhenCentralPmsReturnsSummaryFields_DoesNotCreatePaymentAttemptOrHandoff()
    {
        var fixture = CreateFixture("QRPH", "PAYMONGO", "AUB");
        fixture.CentralPms.ResolveResult = CentralPmsWebPayResult<CentralPmsResolvedParking>.Success(
            new CentralPmsResolvedParking(
                ParkingSessionId,
                TariffSnapshotId,
                12500,
                "PHP",
                "HIKCENTRAL",
                CorrelationId,
                "Mactan Newtown Parking",
                "TICKET-TEST-027",
                "ABC 1234",
                DateTimeOffset.Parse("2026-05-18T10:42:00+08:00"),
                DateTimeOffset.Parse("2026-05-18T11:15:00+08:00"),
                "Weekend Rate",
                "PAYABLE",
                DateTimeOffset.Parse("2026-05-18T11:30:00+08:00"),
                "Not Started",
                SiteGroupId,
                SiteId,
                "WebPay Test Site Group"));

        var result = await fixture.Sut.ResolveAsync(new WebPayParkingSessionResolveRequest
        {
            SiteGroupId = SiteGroupId,
            SiteId = SiteId,
            VendorSystemId = "HIKCENTRAL",
            TicketReference = "TICKET-TEST-027",
            CorrelationId = CorrelationId
        }, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(SiteGroupId, result.Response!.SiteGroupId);
        Assert.Equal(SiteId, result.Response.SiteId);
        Assert.Equal("HIKCENTRAL", result.Response.VendorSystemId);
        Assert.Equal("WebPay Test Site Group", result.Response.SiteGroupName);
        Assert.Equal("Mactan Newtown Parking", result.Response!.SiteName);
        Assert.Equal("TICKET-TEST-027", result.Response.TicketReference);
        Assert.Equal("PAYABLE", result.Response.ParkingStatus);
        Assert.Equal("Not Started", result.Response.PaymentStatus);
        Assert.Equal(12500, result.Response.AmountMinorUnits);
        Assert.True(fixture.ResolveVendorParkingWasCalled);
        Assert.False(fixture.CreatePaymentAttemptWasCalled);
        Assert.Null(fixture.CapturedRouteRequest);
        Assert.Null(fixture.CapturedInitiateRequest);
    }

    /// <summary>
    /// Verifies orphan active attempts without provider session evidence are failed through Central PMS and retried once.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenActivePaymentAttemptHasNoProviderSession_RecoversAndCreatesFreshHandoff()
    {
        var fixture = CreateFixture("QRPH", "PAYMONGO", "AUB");
        fixture.CentralPms.EnqueueCreateAttemptResult(CentralPmsWebPayResult<CentralPmsPaymentAttempt>.Failure(
            new CentralPmsWebPayError(
                409,
                "ACTIVE_PAYMENT_ATTEMPT_EXISTS",
                "An active payment attempt already exists for parking session.",
                false,
                CorrelationId,
                PaymentAttemptId)));
        fixture.CentralPms.EnqueueCreateAttemptResult(CentralPmsWebPayResult<CentralPmsPaymentAttempt>.Success(
            new CentralPmsPaymentAttempt(Guid.Parse("77777777-7777-7777-7777-777777777777"), "PENDING_PROVIDER", "PAYMONGO_CHECKOUT_SESSION", false)));

        var result = await fixture.Sut.HandleAsync(DefaultRequest("QRPH"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(Guid.Parse("77777777-7777-7777-7777-777777777777"), result.Response!.PaymentAttemptId);
        Assert.Equal(PaymentAttemptId, fixture.CentralPms.FinalizedPaymentAttemptId);
        Assert.Equal("FAILED", fixture.CentralPms.FinalAttemptStatus);
        Assert.Equal(2, fixture.CentralPms.CreatePaymentAttemptCallCount);
        Assert.NotNull(fixture.CapturedInitiateRequest);
    }

    /// <summary>
    /// Verifies active attempts with provider evidence but no checkout URL are treated as non-resumable orphans.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task WebPayPaymentIntent_WhenActivePaymentAttemptHasBlankCheckoutUrl_RecoversAndDoesNotDuplicateProviderSessions(
        string? checkoutUrl)
    {
        var fixture = CreateFixture("QRPH", "PAYMONGO", "AUB");
        fixture.CentralPms.EnqueueCreateAttemptResult(CentralPmsWebPayResult<CentralPmsPaymentAttempt>.Failure(
            new CentralPmsWebPayError(
                409,
                "ACTIVE_PAYMENT_ATTEMPT_EXISTS",
                "An active payment attempt already exists for parking session.",
                false,
                CorrelationId,
                PaymentAttemptId)));
        fixture.CentralPms.EnqueueCreateAttemptResult(CentralPmsWebPayResult<CentralPmsPaymentAttempt>.Success(
            new CentralPmsPaymentAttempt(Guid.Parse("77777777-7777-7777-7777-777777777777"), "PENDING_PROVIDER", "PAYMONGO_CHECKOUT_SESSION", false)));
        fixture.ProviderSessions.LatestProviderSessionByPaymentAttempt = new ProviderSessionRecord(
            Guid.Parse("99999999-9999-9999-9999-999999999999"),
            PaymentAttemptId,
            "PAYMONGO",
            "PAYMONGO_CHECKOUT_SESSION",
            "cs_test_orphan",
            "pi_test_orphan",
            "PENDING",
            checkoutUrl,
            null,
            DateTimeOffset.UtcNow.AddMinutes(15),
            "existing-idempotency-key",
            CorrelationId,
            "{}",
            "{}",
            DateTimeOffset.UtcNow);

        var result = await fixture.Sut.HandleAsync(DefaultRequest("QRPH"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, fixture.CentralPms.CreatePaymentAttemptCallCount);
        Assert.Equal(1, fixture.CentralPms.FinalizePaymentAttemptCallCount);
        Assert.Equal(1, fixture.Handoff.InitiateCallCount);
        Assert.Equal(Guid.Parse("77777777-7777-7777-7777-777777777777"), fixture.CapturedInitiateRequest!.PaymentAttemptId);
    }

    private static Fixture CreateFixture(
        string paymentMethod,
        string? selectedProvider,
        string? fallbackProvider,
        bool isRouted = true,
        string routingReason = "PRIMARY_PROVIDER",
        string? errorCode = null,
        WebPayReturnUrlOptions? returnUrlOptions = null)
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
        var providerSessions = new FakeProviderSessionRepository();

        var sut = new WebPayPaymentIntentHandler(
            centralPms,
            routing,
            new ProviderProductResolver(),
            handoff,
            providerSessions,
            Options.Create(returnUrlOptions ?? new WebPayReturnUrlOptions
            {
                PublicBaseUrl = "https://webpay.public.test",
                PaymentSuccessPath = "/webpay/payment-return",
                PaymentCancelPath = "/webpay/payment-cancelled"
            }),
            NullLogger<WebPayPaymentIntentHandler>.Instance);

        return new Fixture(sut, centralPms, routing, handoff, providerSessions);
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
            CapturingProviderPaymentHandoffInitiator handoff,
            FakeProviderSessionRepository providerSessions)
        {
            Sut = sut;
            CentralPms = centralPms;
            Routing = routing;
            Handoff = handoff;
            ProviderSessions = providerSessions;
        }

        public WebPayPaymentIntentHandler Sut { get; }

        public FakeCentralPmsWebPayClient CentralPms { get; }

        public CapturingRoutingPolicyResolver Routing { get; }

        public CapturingProviderPaymentHandoffInitiator Handoff { get; }

        public FakeProviderSessionRepository ProviderSessions { get; }

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

        public CentralPmsWebPayResult<CentralPmsPaymentAttempt>? CreateAttemptResult { get; set; }

        private readonly Queue<CentralPmsWebPayResult<CentralPmsPaymentAttempt>> _createAttemptResults = new();

        public bool ResolveVendorParkingWasCalled { get; private set; }

        public bool CreatePaymentAttemptWasCalled { get; private set; }

        public int CreatePaymentAttemptCallCount { get; private set; }

        public int FinalizePaymentAttemptCallCount { get; private set; }

        public Guid? FinalizedPaymentAttemptId { get; private set; }

        public string? FinalAttemptStatus { get; private set; }

        public string? CapturedPaymentProvider { get; private set; }

        public string? CapturedPaymentMethod { get; private set; }

        public string? CapturedTicketReference { get; private set; }

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
            FinalizedPaymentAttemptId = paymentAttemptId;
            FinalAttemptStatus = finalAttemptStatus;

            return Task.FromResult(CentralPmsWebPayResult<CentralPmsPaymentAttempt>.Success(
                new CentralPmsPaymentAttempt(paymentAttemptId, finalAttemptStatus, string.Empty, false)));
        }
    }

    private sealed class FakeProviderSessionRepository : IProviderSessionRepository
    {
        public ProviderSessionRecord? LatestActiveProviderSession { get; set; }

        public ProviderSessionRecord? LatestProviderSessionByPaymentAttempt { get; set; }

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
            return Task.FromResult(LatestProviderSessionByPaymentAttempt);
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

        public int InitiateCallCount { get; private set; }

        public InvalidOperationException? ExceptionToThrow { get; set; }

        public Task<InitiateProviderPaymentResponse> InitiateAsync(
            InitiateProviderPaymentRequest request,
            CancellationToken cancellationToken)
        {
            InitiateCallCount++;
            CapturedRequest = request;
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

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
