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
    [InlineData("PAYMONGO", "GCASH", "PAYMONGO_CHECKOUT_SESSION")]
    [InlineData("PAYMONGO", "MAYA", "PAYMONGO_CHECKOUT_SESSION")]
    [InlineData("PAYMONGO", "CARD", "PAYMONGO_CHECKOUT_SESSION")]
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
    /// Verifies WebPay customer-facing methods route through PayMongo from the DB-backed policy result.
    /// </summary>
    [Theory]
    [InlineData("QRPH")]
    [InlineData("GCASH")]
    [InlineData("MAYA")]
    [InlineData("CARD")]
    public async Task WebPayPaymentIntent_WhenAllowedMethodRequested_SelectsPayMongoFromRoutingPolicy(string paymentMethod)
    {
        var fixture = CreateFixture(paymentMethod, "PAYMONGO", null);

        var result = await fixture.Sut.HandleAsync(DefaultRequest(paymentMethod), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("PAYMONGO", result.Response!.SelectedProviderCode);
        Assert.Null(result.Response.FallbackProviderCode);
        Assert.Equal(paymentMethod, fixture.CapturedRouteRequest!.PaymentMethod);
        Assert.Equal("PAYMONGO_CHECKOUT_SESSION", fixture.CapturedPaymentProvider);
        Assert.Equal(paymentMethod, fixture.CapturedPaymentMethod);
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
        Assert.Equal("WEBPAY_PAYMONGO_PROVIDER_ROUTE_REGRESSION", result.Error.ErrorCode);
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
        Assert.Equal("ExitPass Parking Fee", request.CustomerDisplayName);
        Assert.Equal("Site: WebPay Test Site 2026-05-23", request.Description);
        Assert.DoesNotContain("WEBPAY-20260523-FRESH-001", request.CustomerDisplayName);
        Assert.DoesNotContain("WEBPAY-20260523-FRESH-001", request.Description);
        Assert.DoesNotContain("WEBPAY001", request.Description);
        Assert.DoesNotContain("Amount:", request.Description);
        Assert.DoesNotContain("PHP 100.00", request.Description);
        Assert.DoesNotMatch(GuidPattern, request.CustomerDisplayName);
        Assert.DoesNotMatch(GuidPattern, request.Description);
        Assert.Equal(PaymentAttemptId.ToString(), request.Metadata["payment_attempt_id"]);
        Assert.Equal(ParkingSessionId.ToString(), request.Metadata["parking_session_id"]);
        Assert.Equal(TariffSnapshotId.ToString(), request.Metadata["tariff_snapshot_id"]);
        Assert.Equal(CorrelationId.ToString(), request.Metadata["correlation_id"]);
        Assert.DoesNotContain("ticket_reference", request.Metadata.Keys);
        Assert.DoesNotContain("plate_number", request.Metadata.Keys);
        Assert.StartsWith("https://webpay.public.test/webpay/payment-return?", request.SuccessUrl);
        Assert.Contains($"paymentAttemptId={PaymentAttemptId}", request.SuccessUrl);
        Assert.Contains($"correlationId={CorrelationId}", request.SuccessUrl);
        Assert.Contains("result=success", request.SuccessUrl);
        Assert.DoesNotContain("ticketReference=", request.SuccessUrl);
        Assert.DoesNotContain("plateNumber=", request.SuccessUrl);
        Assert.StartsWith("https://webpay.public.test/webpay/payment-cancelled?", request.CancelUrl);
        Assert.Contains($"paymentAttemptId={PaymentAttemptId}", request.CancelUrl);
        Assert.Contains($"correlationId={CorrelationId}", request.CancelUrl);
        Assert.Contains("result=cancelled", request.CancelUrl);
        Assert.DoesNotContain("ticketReference=", request.CancelUrl);
        Assert.DoesNotContain("plateNumber=", request.CancelUrl);
    }

    /// <summary>
    /// Verifies a plate-originated checkout uses only its durable payment-attempt return identity.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenCreatingPlateOnlyCheckout_UsesPaymentAttemptInReturnUrls()
    {
        var fixture = CreateFixture("CARD", "PAYMONGO", null);
        fixture.CentralPms.ResolveResult = CentralPmsWebPayResult<CentralPmsResolvedParking>.Success(
            new CentralPmsResolvedParking(
                ParkingSessionId,
                TariffSnapshotId,
                2500,
                "PHP",
                "HIKCENTRAL",
                CorrelationId,
                SiteName: "Restart 35 Site A",
                TicketReference: null,
                PlateNumber: "R35-PLATE-A",
                SiteGroupId: SiteGroupId,
                SiteId: SiteId));

        var result = await fixture.Sut.HandleAsync(DefaultRequest("CARD"), CancellationToken.None);

        Assert.True(result.Succeeded);
        var request = fixture.CapturedInitiateRequest!;
        Assert.Contains($"paymentAttemptId={PaymentAttemptId}", request.SuccessUrl);
        Assert.Contains($"paymentAttemptId={PaymentAttemptId}", request.CancelUrl);
        Assert.DoesNotContain("plateNumber=", request.SuccessUrl);
        Assert.DoesNotContain("plateNumber=", request.CancelUrl);
        Assert.DoesNotContain("ticketReference=", request.SuccessUrl);
        Assert.DoesNotContain("ticketReference=", request.CancelUrl);
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
    /// Verifies expired payable basis errors are surfaced as recoverable refresh-required results.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenCentralPmsRequiresPayableBasisRefresh_ReturnsRecoverableRefreshError()
    {
        var fixture = CreateFixture("QRPH", "PAYMONGO", null);
        fixture.CentralPms.CreateAttemptResult = CentralPmsWebPayResult<CentralPmsPaymentAttempt>.Failure(
            new CentralPmsWebPayError(
                409,
                "PAYABLE_BASIS_REFRESH_REQUIRED",
                "Tariff snapshot has expired. Refresh the payable basis before retrying payment.",
                true,
                CorrelationId,
                ParkingSessionId));

        var result = await fixture.Sut.HandleAsync(DefaultRequest("QRPH"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Error!.StatusCode);
        Assert.Equal("PAYABLE_BASIS_REFRESH_REQUIRED", result.Error.ErrorCode);
        Assert.True(result.Error.Retryable);
        Assert.Contains("Refresh", result.Error.Message);
        Assert.Equal("PAYMONGO_CHECKOUT_SESSION", fixture.CapturedPaymentProvider);
        Assert.Null(fixture.CapturedInitiateRequest);
    }

    /// <summary>
    /// Verifies a browser-held payable basis is submitted to Central PMS so expired snapshots surface as refresh-required.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenBrowserHeldSnapshotDiffersButAmountMatches_DelegatesEligibilityToCentralPms()
    {
        var fixture = CreateFixture("QRPH", "PAYMONGO", null);
        var browserHeldTariffSnapshotId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var request = DefaultRequest("QRPH");
        request.TariffSnapshotId = browserHeldTariffSnapshotId;
        request.ExpectedAmountMinorUnits = 12500;
        fixture.CentralPms.CreateAttemptResult = CentralPmsWebPayResult<CentralPmsPaymentAttempt>.Failure(
            new CentralPmsWebPayError(
                409,
                "PAYABLE_BASIS_REFRESH_REQUIRED",
                "Tariff snapshot has expired. Refresh the payable basis before retrying payment.",
                true,
                CorrelationId,
                ParkingSessionId));

        var result = await fixture.Sut.HandleAsync(request, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Error!.StatusCode);
        Assert.Equal("PAYABLE_BASIS_REFRESH_REQUIRED", result.Error.ErrorCode);
        Assert.True(result.Error.Retryable);
        Assert.Equal(1, fixture.CentralPms.ResolveVendorParkingCallCount);
        Assert.Equal(1, fixture.CentralPms.CreatePaymentAttemptCallCount);
        Assert.Equal(browserHeldTariffSnapshotId, fixture.CentralPms.CapturedTariffSnapshotIds.Single());
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
    /// Verifies unsupported methods are rejected before vendor resolution, routing, attempt creation, or handoff.
    /// </summary>
    [Theory]
    [InlineData("BANK_TRANSFER")]
    [InlineData("PAYPAL")]
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
        var fixture = CreateFixture("QRPH", "PAYMONGO", null);
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
    /// Verifies identical payment-intent replay reuses durable provider-session evidence before provider initiation.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenPaymentAttemptReplayHasProviderSession_ReturnsExistingHandoffWithoutProviderCall()
    {
        var fixture = CreateFixture("QRPH", "PAYMONGO", null);
        fixture.ProviderSessions.LatestProviderSessionByPaymentAttempt = ExistingProviderSession(
            "cs_test_replay",
            "https://payments.test/replay-checkout",
            "PENDING");

        var result = await fixture.Sut.HandleAsync(DefaultRequest("QRPH"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(PaymentAttemptId, result.Response!.PaymentAttemptId);
        Assert.Equal("PENDING", result.Response.Status);
        Assert.Equal("Pending Payment", result.Response.PaymentStatus);
        Assert.Equal("https://payments.test/replay-checkout", result.Response.Handoff.HandoffUrl);
        Assert.Null(fixture.CapturedInitiateRequest);
        Assert.Equal(0, fixture.Handoff.InitiateCallCount);
    }

    /// <summary>
    /// Verifies non-resumable provider-session evidence is reported safely instead of creating a second handoff.
    /// </summary>
    [Theory]
    [InlineData("FAILED")]
    [InlineData("EXPIRED")]
    [InlineData("UNKNOWN")]
    public async Task WebPayPaymentIntent_WhenPaymentAttemptReplayHasNonReusableProviderSession_ReturnsSafeRecovery(
        string sessionStatus)
    {
        var fixture = CreateFixture("QRPH", "PAYMONGO", null);
        fixture.ProviderSessions.LatestProviderSessionByPaymentAttempt = ExistingProviderSession(
            "cs_test_nonreusable",
            "https://payments.test/nonreusable",
            sessionStatus);

        var result = await fixture.Sut.HandleAsync(DefaultRequest("QRPH"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Error!.StatusCode);
        Assert.Equal("PAYMENT_PROVIDER_HANDOFF_NOT_REUSABLE", result.Error.ErrorCode);
        Assert.Equal(PaymentAttemptId, result.Error.PaymentAttemptId);
        Assert.Equal(sessionStatus, result.Error.Status);
        Assert.Null(fixture.CapturedInitiateRequest);
        Assert.Equal(0, fixture.Handoff.InitiateCallCount);
    }

    /// <summary>
    /// Verifies an incomplete durable initiation reservation is reported as in progress instead of starting another handoff.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenPaymentAttemptReplayHasFreshIncompleteProviderReservation_ReturnsInProgress()
    {
        var fixture = CreateFixture("QRPH", "PAYMONGO", null);
        fixture.ProviderSessions.LatestProviderSessionByPaymentAttempt = ExistingProviderSession(
            string.Empty,
            string.Empty,
            "CREATED");

        var result = await fixture.Sut.HandleAsync(DefaultRequest("QRPH"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Error!.StatusCode);
        Assert.Equal("PAYMENT_PROVIDER_HANDOFF_IN_PROGRESS", result.Error.ErrorCode);
        Assert.Equal(PaymentAttemptId, result.Error.PaymentAttemptId);
        Assert.Equal("CREATED", result.Error.Status);
        Assert.Null(fixture.CapturedInitiateRequest);
        Assert.Equal(0, fixture.Handoff.InitiateCallCount);
    }

    /// <summary>
    /// Verifies payment is blocked until the statutory decision has completed Operator Console review.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenStatutoryDecisionAwaitsReview_BlocksPaymentSideEffects()
    {
        var fixture = CreateFixture("QRPH", "PAYMONGO", null);
        fixture.CentralPms.StatutoryDecisionResult = CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>.Success(
            StatutoryDecision(
                decisionCommandStatus: "AWAITING_REVIEW",
                decisionResultStatus: "NOT_DECIDED",
                applicationCommandStatus: "NOT_REQUESTED",
                payableBasisReady: false,
                readinessStatus: "AWAITING_REVIEW",
                readinessAction: "POLL_READBACK"));

        var request = DefaultStatutoryPaymentRequest("QRPH");
        var result = await fixture.Sut.HandleAsync(request, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("STATUTORY_DISCOUNT_AWAITING_REVIEW", result.Error!.ErrorCode);
        Assert.Equal(1, fixture.CentralPms.GetStatutoryDiscountDecisionCallCount);
        Assert.False(fixture.CreatePaymentAttemptWasCalled);
        Assert.Null(fixture.CapturedInitiateRequest);
    }

    /// <summary>
    /// Verifies rejected statutory decisions never create a payment attempt.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenStatutoryDecisionRejected_BlocksPaymentSideEffects()
    {
        var fixture = CreateFixture("QRPH", "PAYMONGO", null);
        fixture.CentralPms.StatutoryDecisionResult = CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>.Success(
            StatutoryDecision(
                decisionResultStatus: "REJECTED",
                applicationCommandStatus: "NOT_REQUESTED",
                payableBasisReady: false,
                readinessStatus: "DECISION_REJECTED",
                readinessAction: "DO_NOT_RETRY"));

        var result = await fixture.Sut.HandleAsync(DefaultStatutoryPaymentRequest("QRPH"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("STATUTORY_DISCOUNT_DECISION_REJECTED", result.Error!.ErrorCode);
        Assert.False(fixture.CreatePaymentAttemptWasCalled);
        Assert.Null(fixture.CapturedInitiateRequest);
    }

    /// <summary>
    /// Verifies approved-but-unapplied decisions require the explicit application-intent path before payment.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenApplicationIntentRequired_BlocksPaymentSideEffects()
    {
        var fixture = CreateFixture("QRPH", "PAYMONGO", null);
        fixture.CentralPms.StatutoryDecisionResult = CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>.Success(
            StatutoryDecision(
                applicationCommandStatus: "NOT_REQUESTED",
                payableBasisReady: false,
                readinessStatus: "DECISION_APPROVED_APPLICATION_NOT_REQUESTED",
                readinessAction: "SUBMIT_APPLICATION_INTENT"));

        var result = await fixture.Sut.HandleAsync(DefaultStatutoryPaymentRequest("QRPH"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("STATUTORY_DISCOUNT_APPLICATION_INTENT_REQUIRED", result.Error!.ErrorCode);
        Assert.False(fixture.CreatePaymentAttemptWasCalled);
        Assert.Null(fixture.CapturedInitiateRequest);
    }

    /// <summary>
    /// Verifies in-flight application processing remains pending and does not start a payment.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenApplicationProcessing_BlocksPaymentSideEffects()
    {
        var fixture = CreateFixture("QRPH", "PAYMONGO", null);
        fixture.CentralPms.StatutoryDecisionResult = CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>.Success(
            StatutoryDecision(
                applicationCommandStatus: "PROCESSING",
                payableBasisReady: false,
                readinessStatus: "APPLICATION_PROCESSING",
                readinessAction: "POLL_READBACK",
                retryable: true));

        var result = await fixture.Sut.HandleAsync(DefaultStatutoryPaymentRequest("QRPH"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("STATUTORY_DISCOUNT_APPLICATION_PROCESSING", result.Error!.ErrorCode);
        Assert.True(result.Error.Retryable);
        Assert.False(fixture.CreatePaymentAttemptWasCalled);
        Assert.Null(fixture.CapturedInitiateRequest);
    }

    /// <summary>
    /// Verifies retryable Central PMS statutory readback failures block payment and do not call the provider.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenStatutoryReadbackTemporarilyUnavailable_BlocksPaymentSideEffects()
    {
        var fixture = CreateFixture("QRPH", "PAYMONGO", null);
        fixture.CentralPms.StatutoryDecisionResult = CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>.Failure(
            new CentralPmsWebPayError(
                503,
                "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_TEMPORARILY_UNAVAILABLE",
                "Statutory-discount readback is temporarily unavailable.",
                true,
                CorrelationId));

        var result = await fixture.Sut.HandleAsync(DefaultStatutoryPaymentRequest("QRPH"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("STATUTORY_DISCOUNT_TEMPORARILY_UNAVAILABLE", result.Error!.ErrorCode);
        Assert.True(result.Error.Retryable);
        Assert.False(fixture.CreatePaymentAttemptWasCalled);
        Assert.Null(fixture.CapturedInitiateRequest);
    }

    /// <summary>
    /// Verifies authoritative statutory payable-basis facts must match the requested payment.
    /// </summary>
    [Theory]
    [InlineData("snapshot")]
    [InlineData("amount")]
    [InlineData("currency")]
    [InlineData("session")]
    public async Task WebPayPaymentIntent_WhenStatutoryAppliedFactsMismatch_BlocksPaymentSideEffects(string mismatch)
    {
        var fixture = CreateFixture("QRPH", "PAYMONGO", null);
        var request = DefaultStatutoryPaymentRequest("QRPH");

        if (mismatch == "snapshot")
        {
            request.TariffSnapshotId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        }
        else if (mismatch == "amount")
        {
            request.ExpectedAmountMinorUnits = 9900;
        }
        else if (mismatch == "currency")
        {
            request.ExpectedCurrency = "USD";
        }
        else
        {
            fixture.CentralPms.StatutoryDecisionResult = CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>.Success(
                StatutoryDecision(parkingSessionId: Guid.Parse("88888888-8888-8888-8888-888888888888")));
        }

        var result = await fixture.Sut.HandleAsync(request, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("MISMATCH", result.Error!.ErrorCode, StringComparison.OrdinalIgnoreCase);
        Assert.False(fixture.CreatePaymentAttemptWasCalled);
        Assert.Null(fixture.CapturedInitiateRequest);
    }

    /// <summary>
    /// Verifies missing applied statutory payable-basis facts block payment.
    /// </summary>
    [Theory]
    [InlineData("snapshot")]
    [InlineData("amount")]
    [InlineData("currency")]
    public async Task WebPayPaymentIntent_WhenAppliedStatutoryFactsAreMissing_BlocksPaymentSideEffects(string missing)
    {
        var fixture = CreateFixture("QRPH", "PAYMONGO", null);
        fixture.CentralPms.StatutoryDecisionResult = CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>.Success(
            StatutoryDecision(
                appliedTariffSnapshotId: TariffSnapshotId,
                finalAmount: missing == "amount" ? null : 10000,
                currency: missing == "currency" ? null : "PHP",
                omitAppliedTariffSnapshot: missing == "snapshot"));

        var result = await fixture.Sut.HandleAsync(DefaultStatutoryPaymentRequest("QRPH"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("_MISSING", result.Error!.ErrorCode, StringComparison.OrdinalIgnoreCase);
        Assert.False(fixture.CreatePaymentAttemptWasCalled);
        Assert.Null(fixture.CapturedInitiateRequest);
    }

    /// <summary>
    /// Verifies payment is allowed only after Central PMS marks the statutory payable basis ready.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenStatutoryPayableBasisReady_UsesAppliedSnapshotAmountAndCurrency()
    {
        var fixture = CreateFixture("QRPH", "PAYMONGO", null);
        var appliedTariffSnapshotId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        fixture.CentralPms.StatutoryDecisionResult = CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>.Success(
            StatutoryDecision(
                appliedTariffSnapshotId: appliedTariffSnapshotId,
                finalAmount: 9500,
                currency: "PHP"));
        var request = DefaultStatutoryPaymentRequest("QRPH");
        request.TariffSnapshotId = appliedTariffSnapshotId;
        request.ExpectedAmountMinorUnits = 9500;
        request.ExpectedCurrency = "PHP";

        var result = await fixture.Sut.HandleAsync(request, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(appliedTariffSnapshotId, result.Response!.TariffSnapshotId);
        Assert.Equal(9500, result.Response.AmountMinorUnits);
        Assert.Equal("PHP", result.Response.Currency);
        Assert.Equal(appliedTariffSnapshotId, fixture.CentralPms.CapturedTariffSnapshotIds.Single());
        Assert.Equal(1, fixture.CentralPms.CreatePaymentAttemptCallCount);
        Assert.Equal(1, fixture.Handoff.InitiateCallCount);
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
    /// Verifies fallback-looking backend site names are not exposed in the WebPay parking-session response.
    /// </summary>
    [Fact]
    public async Task WebPayParkingSessionResolve_WhenCentralPmsReturnsGeneratedSiteNames_ReturnsSafeGenericNames()
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
                SiteName: "Site a153da55e9895cdbafb8373eccf589e0",
                TicketReference: "TICKET-TEST-028",
                PlateNumber: "ABC 1234",
                SiteGroupId: SiteGroupId,
                SiteId: SiteId,
                SiteGroupName: "Site Group bca924a0a27f5b9dacca291bf1391b49"));

        var result = await fixture.Sut.ResolveAsync(new WebPayParkingSessionResolveRequest
        {
            SiteGroupId = SiteGroupId,
            SiteId = SiteId,
            VendorSystemId = "HIKCENTRAL",
            TicketReference = "TICKET-TEST-028",
            CorrelationId = CorrelationId
        }, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Parking Group", result.Response!.SiteGroupName);
        Assert.Equal("Parking Site", result.Response.SiteName);
        Assert.DoesNotContain("bca924a0a27f5b9dacca291bf1391b49", result.Response.SiteGroupName);
        Assert.DoesNotContain("a153da55e9895cdbafb8373eccf589e0", result.Response.SiteName);
        Assert.False(fixture.CreatePaymentAttemptWasCalled);
        Assert.Null(fixture.CapturedInitiateRequest);
    }

    /// <summary>
    /// Verifies orphan active attempts without provider session evidence are failed through Central PMS and retried once.
    /// </summary>
    [Fact]
    public async Task WebPayPaymentIntent_WhenActivePaymentAttemptHasNoProviderSession_RecoversAndCreatesFreshHandoff()
    {
        var fixture = CreateFixture("QRPH", "PAYMONGO", null);
        var refreshedTariffSnapshotId = Guid.Parse("88888888-8888-8888-8888-888888888888");
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
        fixture.CentralPms.EnqueueResolveResult(fixture.CentralPms.ResolveResult);
        fixture.CentralPms.EnqueueResolveResult(CentralPmsWebPayResult<CentralPmsResolvedParking>.Success(
            new CentralPmsResolvedParking(
                ParkingSessionId,
                refreshedTariffSnapshotId,
                12500,
                "PHP",
                "HIKCENTRAL",
                CorrelationId,
                SiteGroupId: SiteGroupId,
                SiteId: SiteId)));

        var result = await fixture.Sut.HandleAsync(DefaultRequest("QRPH"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(Guid.Parse("77777777-7777-7777-7777-777777777777"), result.Response!.PaymentAttemptId);
        Assert.Equal(refreshedTariffSnapshotId, result.Response.TariffSnapshotId);
        Assert.Equal(PaymentAttemptId, fixture.CentralPms.FinalizedPaymentAttemptId);
        Assert.Equal("FAILED", fixture.CentralPms.FinalAttemptStatus);
        Assert.Equal(2, fixture.CentralPms.CreatePaymentAttemptCallCount);
        Assert.Equal(2, fixture.CentralPms.ResolveVendorParkingCallCount);
        Assert.Equal(new[] { TariffSnapshotId, refreshedTariffSnapshotId }, fixture.CentralPms.CapturedTariffSnapshotIds);
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
        var fixture = CreateFixture("QRPH", "PAYMONGO", null);
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

    private static WebPayPaymentIntentRequest DefaultStatutoryPaymentRequest(string paymentMethod)
    {
        var request = DefaultRequest(paymentMethod);
        request.TariffSnapshotId = TariffSnapshotId;
        request.ExpectedAmountMinorUnits = 10000;
        request.ExpectedCurrency = "PHP";
        request.StatutoryDiscountDecisionCommandId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        request.StatutoryDiscountPayableBasisApplicationCommandId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        return request;
    }

    private static ProviderSessionRecord ExistingProviderSession(
        string providerSessionId,
        string checkoutUrl,
        string sessionStatus)
    {
        return new ProviderSessionRecord(
            Guid.Parse("99999999-9999-9999-9999-999999999999"),
            PaymentAttemptId,
            "PAYMONGO",
            "PAYMONGO_CHECKOUT_SESSION",
            providerSessionId,
            "pi_test_existing",
            sessionStatus,
            checkoutUrl,
            null,
            DateTimeOffset.UtcNow.AddMinutes(15),
            "existing-idempotency-key",
            CorrelationId,
            "{}",
            "{}",
            DateTimeOffset.UtcNow,
            12500,
            "PHP");
    }

    private static CentralPmsStatutoryDiscountDecision StatutoryDecision(
        Guid? parkingSessionId = null,
        Guid? appliedTariffSnapshotId = null,
        long? finalAmount = 10000,
        string? currency = "PHP",
        string decisionCommandStatus = "COMPLETED",
        string? decisionResultStatus = "APPROVED",
        string applicationCommandStatus = "APPLIED",
        bool payableBasisReady = true,
        string readinessStatus = "READY",
        string? readinessAction = null,
        bool retryable = false,
        bool omitAppliedTariffSnapshot = false)
    {
        return new CentralPmsStatutoryDiscountDecision(
            Guid.Parse("99999999-9999-9999-9999-999999999999"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            parkingSessionId ?? ParkingSessionId,
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
            finalAmount.HasValue ? 2500 : null,
            finalAmount,
            currency,
            true,
            true,
            null,
            null,
            CorrelationId,
            DateTimeOffset.Parse("2026-05-16T12:00:00Z"),
            decisionResultStatus == "APPROVED" ? DateTimeOffset.Parse("2026-05-16T12:05:00Z") : null,
            applicationCommandStatus == "APPLIED" ? DateTimeOffset.Parse("2026-05-16T12:10:00Z") : null,
            TariffSnapshotId,
            omitAppliedTariffSnapshot ? null : appliedTariffSnapshotId ?? TariffSnapshotId,
            decisionCommandStatus,
            decisionResultStatus ?? "NOT_DECIDED",
            payableBasisReady ? "APPLIED" : "PENDING",
            "statutory-discount-decision:sha256:v2",
            retryable,
            retryable ? "RETRYABLE" : "NONE",
            retryable ? "POLL_READBACK" : null,
            null,
            decisionCommandStatus,
            decisionResultStatus,
            retryable,
            retryable ? "RETRYABLE" : "NONE",
            retryable ? "POLL_READBACK" : null,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            applicationCommandStatus != "NOT_REQUESTED",
            applicationCommandStatus,
            applicationCommandStatus == "APPLIED" ? "APPLIED" : applicationCommandStatus,
            "statutory-discount-payable-basis-application:sha256:v1",
            retryable,
            retryable ? "RETRYABLE" : "NONE",
            retryable ? "POLL_READBACK" : null,
            payableBasisReady ? "APPLIED" : "PENDING",
            payableBasisReady,
            SiteId,
            SiteGroupId,
            payableBasisReady,
            readinessStatus,
            readinessAction);
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

        public CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision> StatutoryDecisionResult { get; set; } =
            CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>.Success(StatutoryDecision());

        public CentralPmsWebPayResult<CentralPmsStatutoryDiscountAvailability> StatutoryAvailabilityResult { get; set; } =
            CentralPmsWebPayResult<CentralPmsStatutoryDiscountAvailability>.Success(StatutoryAvailability());

        public CentralPmsWebPayResult<CentralPmsStatutoryDiscountPendingLifecycleRediscovery> PendingLifecycleRediscoveryResult { get; set; } =
            CentralPmsWebPayResult<CentralPmsStatutoryDiscountPendingLifecycleRediscovery>.Success(new CentralPmsStatutoryDiscountPendingLifecycleRediscovery(
                "NOT_FOUND",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                "NOT_FOUND",
                false,
                CorrelationId,
                null,
                null,
                null,
                null,
                null));

        private readonly Queue<CentralPmsWebPayResult<CentralPmsPaymentAttempt>> _createAttemptResults = new();
        private readonly Queue<CentralPmsWebPayResult<CentralPmsResolvedParking>> _resolveResults = new();
        private readonly List<Guid> _capturedTariffSnapshotIds = new();

        public bool ResolveVendorParkingWasCalled { get; private set; }

        public int ResolveVendorParkingCallCount { get; private set; }

        public bool CreatePaymentAttemptWasCalled { get; private set; }

        public int CreatePaymentAttemptCallCount { get; private set; }

        public int FinalizePaymentAttemptCallCount { get; private set; }

        public int GetStatutoryDiscountDecisionCallCount { get; private set; }

        public int ResolveStatutoryDiscountAvailabilityCallCount { get; private set; }

        public int RediscoverStatutoryDiscountPendingLifecycleCallCount { get; private set; }

        public Guid? FinalizedPaymentAttemptId { get; private set; }

        public string? FinalAttemptStatus { get; private set; }

        public string? CapturedPaymentProvider { get; private set; }

        public string? CapturedPaymentMethod { get; private set; }

        public string? CapturedTicketReference { get; private set; }

        public IReadOnlyList<Guid> CapturedTariffSnapshotIds => _capturedTariffSnapshotIds;

        public void EnqueueCreateAttemptResult(CentralPmsWebPayResult<CentralPmsPaymentAttempt> result)
        {
            _createAttemptResults.Enqueue(result);
        }

        public void EnqueueResolveResult(CentralPmsWebPayResult<CentralPmsResolvedParking> result)
        {
            _resolveResults.Enqueue(result);
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
            ResolveVendorParkingCallCount++;
            CapturedTicketReference = ticketReference;
            if (_resolveResults.Count > 0)
            {
                return Task.FromResult(_resolveResults.Dequeue());
            }

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
            _capturedTariffSnapshotIds.Add(tariffSnapshotId);

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

        public Task<CentralPmsWebPayResult<CentralPmsWebPayReceiptPresentation>> GetReceiptPresentationAsync(
            Guid paymentAttemptId,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<CentralPmsWebPayResult<CentralPmsWebPayPaymentAttemptStatus>> GetPaymentAttemptStatusAsync(
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
            throw new NotSupportedException();
        }

        public Task<CentralPmsWebPayResult<CentralPmsStatutoryDiscountAvailability>> ResolveStatutoryDiscountAvailabilityAsync(
            CentralPmsStatutoryDiscountAvailabilityRequest request,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            ResolveStatutoryDiscountAvailabilityCallCount++;
            return Task.FromResult(StatutoryAvailabilityResult);
        }

        public Task<CentralPmsWebPayResult<CentralPmsStatutoryDiscountPendingLifecycleRediscovery>> RediscoverStatutoryDiscountPendingLifecycleAsync(
            CentralPmsStatutoryDiscountPendingLifecycleRediscoveryRequest request,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            RediscoverStatutoryDiscountPendingLifecycleCallCount++;
            return Task.FromResult(PendingLifecycleRediscoveryResult);
        }

        public Task<CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>> GetStatutoryDiscountDecisionAsync(
            Guid statutoryDiscountDecisionCommandId,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            GetStatutoryDiscountDecisionCallCount++;
            return Task.FromResult(StatutoryDecisionResult);
        }

        public Task<CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>> ApplyStatutoryDiscountPayableBasisAsync(
            CentralPmsStatutoryDiscountDecisionRequest request,
            string idempotencyKey,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private static CentralPmsStatutoryDiscountAvailability StatutoryAvailability(
        IReadOnlyList<string>? coveredEntitlementTypes = null,
        string availabilityStatus = "AVAILABLE",
        bool statutoryParkingBenefitAvailable = true,
        bool retryable = false) =>
        new(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            ParkingSessionId,
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            availabilityStatus,
            statutoryParkingBenefitAvailable,
            coveredEntitlementTypes ?? new[] { "SENIOR_CITIZEN", "PWD" },
            null,
            null,
            retryable,
            retryable ? "WAIT_AND_RETRY" : "CONTINUE_WITH_ORDINARY_PAYMENT",
            Array.Empty<CentralPmsStatutoryDiscountAvailabilityEvidenceRequirement>(),
            CorrelationId);

    private sealed class FakeProviderSessionRepository : IProviderSessionRepository
    {
        public ProviderSessionRecord? LatestActiveProviderSession { get; set; }

        public ProviderSessionRecord? LatestProviderSessionByPaymentAttempt { get; set; }

        public Task<ProviderSessionInitiationReservationResult> TryReserveInitiationAsync(
            ProviderSessionInitiationReservation reservation,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task CompleteInitiationAsync(
            Guid providerSessionRecordId,
            ProviderSessionRecord record,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
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
