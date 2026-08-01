using System.Net;
using System.Text;
using System.Text.Json;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;
using ExitPass.PaymentOrchestrator.Infrastructure.Integrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ExitPass.PaymentOrchestrator.UnitTests.Infrastructure.Integrations;

/// <summary>
/// Unit tests for <see cref="CentralPmsWebPayClient"/>.
/// </summary>
public sealed class CentralPmsWebPayClientTests
{
    private static readonly Guid ParkingSessionId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid TariffSnapshotId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid CorrelationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid StatutoryDecisionCommandId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid StatutoryApplicationCommandId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid WebPayServiceIdentityId = Guid.Parse("9b000000-0000-0000-0000-000000000005");

    /// <summary>
    /// Verifies Central PMS payment attempt creation receives both provider rail and payment method.
    /// </summary>
    [Fact]
    public async Task CreateOrReusePaymentAttemptAsync_SendsProviderRailPaymentMethodAndRequiredHeaders()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent(new
            {
                paymentAttemptId = Guid.Parse("66666666-6666-6666-6666-666666666666"),
                attemptStatus = "PENDING_PROVIDER",
                paymentProvider = "AUB_QRPH",
                wasReused = false
            })
        });
        var client = CreateClient(handler);

        var result = await client.CreateOrReusePaymentAttemptAsync(
            ParkingSessionId,
            TariffSnapshotId,
            "AUB_QRPH",
            "QRPH",
            "webpay:test",
            CorrelationId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal("/v1/public/payment-attempts", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Equal(CorrelationId.ToString(), handler.LastRequest.Headers.GetValues("X-Correlation-Id").Single());
        Assert.Equal("webpay:test", handler.LastRequest.Headers.GetValues("Idempotency-Key").Single());

        using var document = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("AUB_QRPH", document.RootElement.GetProperty("paymentProvider").GetString());
        Assert.Equal("QRPH", document.RootElement.GetProperty("paymentMethod").GetString());
    }

    /// <summary>
    /// Verifies QRPH remains the payment method and is not sent as the Central PMS payment provider.
    /// </summary>
    [Fact]
    public async Task CreateOrReusePaymentAttemptAsync_WhenQrphIsRoutedToPayMongo_SendsCheckoutSessionProvider()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent(new
            {
                paymentAttemptId = Guid.Parse("66666666-6666-6666-6666-666666666666"),
                attemptStatus = "PENDING_PROVIDER",
                paymentProvider = "PAYMONGO_CHECKOUT_SESSION",
                wasReused = false
            })
        });
        var client = CreateClient(handler);

        var result = await client.CreateOrReusePaymentAttemptAsync(
            ParkingSessionId,
            TariffSnapshotId,
            "PAYMONGO_CHECKOUT_SESSION",
            "QRPH",
            "webpay:test",
            CorrelationId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        using var document = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("PAYMONGO_CHECKOUT_SESSION", document.RootElement.GetProperty("paymentProvider").GetString());
        Assert.Equal("QRPH", document.RootElement.GetProperty("paymentMethod").GetString());
        Assert.NotEqual(
            document.RootElement.GetProperty("paymentMethod").GetString(),
            document.RootElement.GetProperty("paymentProvider").GetString());
    }

    /// <summary>
    /// Verifies Central PMS JSON problem responses are preserved as deterministic errors.
    /// </summary>
    [Fact]
    public async Task CreateOrReusePaymentAttemptAsync_WhenCentralPmsReturnsProblemJson_PreservesErrorBody()
    {
        const string responseBody = "{\"title\":\"Unsupported payment provider\",\"detail\":\"Unsupported payment provider: AUB\"}";
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        });
        var client = CreateClient(handler);

        var result = await client.CreateOrReusePaymentAttemptAsync(
            ParkingSessionId,
            TariffSnapshotId,
            "AUB",
            "QRPH",
            "webpay:test",
            CorrelationId,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Equal("PAYMENT_ATTEMPT_CREATE_FAILED", result.Error.ErrorCode);
        Assert.Equal("Unsupported payment provider: AUB", result.Error.Message);
        Assert.False(result.Error.Retryable);
    }

    /// <summary>
    /// Verifies Central PMS active-attempt conflict correlation is preserved.
    /// </summary>
    [Fact]
    public async Task CreateOrReusePaymentAttemptAsync_WhenActivePaymentAttemptConflict_PreservesCorrelationId()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent(new
            {
                errorCode = "ACTIVE_PAYMENT_ATTEMPT_EXISTS",
                message = "An active payment attempt already exists for parking session.",
                correlationId = CorrelationId,
                retryable = false
            })
        });
        var client = CreateClient(handler);

        var result = await client.CreateOrReusePaymentAttemptAsync(
            ParkingSessionId,
            TariffSnapshotId,
            "AUB_QRPH",
            "QRPH",
            "webpay:test",
            CorrelationId,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Error!.StatusCode);
        Assert.Equal("ACTIVE_PAYMENT_ATTEMPT_EXISTS", result.Error.ErrorCode);
        Assert.Equal(CorrelationId, result.Error.CorrelationId);
    }

    /// <summary>
    /// Verifies optional vendor parking summary fields are mapped when Central PMS supplies them.
    /// </summary>
    [Fact]
    public async Task ResolveVendorParkingAsync_WhenSummaryFieldsReturned_MapsSafeFields()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(new
            {
                parkingSessionId = ParkingSessionId,
                tariffSnapshotId = TariffSnapshotId,
                siteGroupId = "29b8b4f4-40dd-447b-ac06-dd52e6ad51c5",
                siteId = "93bd3cb3-e806-4c5c-ac8c-df6c4addff14",
                lookupOutcome = "resolved",
                plateNumber = "ABC 1234",
                ticketReference = "TICKET-TEST-023",
                netPayableMinorUnits = 12500,
                currency = "PHP",
                tariffExpiresAt = "2026-05-18T13:15:00+08:00",
                feeValidUntil = "2026-05-18T13:15:00+08:00",
                vendorSystemId = "45a625de-9034-4fb6-b527-0950d384e51f",
                correlationId = CorrelationId,
                siteGroupName = "WebPay Test Site Group 2026-05-19",
                siteName = "Mactan Newtown Parking",
                entryTime = "2026-05-18T10:42:00+08:00",
                currentFeeCalculationTime = "2026-05-18T12:57:00+08:00",
                tariffName = "Weekend Rate",
                parkingStatus = "PaymentRequired",
                paymentStatus = "Not Started"
            })
        });
        var client = CreateClient(handler);

        var result = await client.ResolveVendorParkingAsync(
            null,
            null,
            "45a625de-9034-4fb6-b527-0950d384e51f",
            null,
            "TICKET-TEST-023",
            CorrelationId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(Guid.Parse("29b8b4f4-40dd-447b-ac06-dd52e6ad51c5"), result.Value!.SiteGroupId);
        Assert.Equal(Guid.Parse("93bd3cb3-e806-4c5c-ac8c-df6c4addff14"), result.Value.SiteId);
        Assert.Equal("45a625de-9034-4fb6-b527-0950d384e51f", result.Value.VendorSystemId);
        Assert.Equal("WebPay Test Site Group 2026-05-19", result.Value.SiteGroupName);
        Assert.Equal("Mactan Newtown Parking", result.Value!.SiteName);
        Assert.Equal("TICKET-TEST-023", result.Value.TicketReference);
        Assert.Equal("ABC 1234", result.Value.PlateNumber);
        Assert.Equal("Weekend Rate", result.Value.TariffName);
        Assert.Equal("PaymentRequired", result.Value.ParkingStatus);
        Assert.Equal("Not Started", result.Value.PaymentStatus);
        Assert.Equal(DateTimeOffset.Parse("2026-05-18T13:15:00+08:00"), result.Value.FeeValidUntil);
    }

    /// <summary>
    /// Verifies WebPay receipt presentation is read through Central PMS by payment attempt.
    /// </summary>
    [Fact]
    public async Task GetReceiptPresentationAsync_UsesWebPayReceiptPresentationReadbackPath()
    {
        var paymentAttemptId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(new
            {
                paymentAttemptId,
                paymentConfirmationId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                fiscalIssuanceReferenceId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                fiscalIssuanceState = "FISCAL_ISSUANCE_RECORDED",
                posFiscalDocumentId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                fiscalDocumentNumber = "SI-20260523-000001",
                fiscalDocumentStatus = "RECORDED",
                receiptAvailabilityState = "AVAILABLE",
                presentationVersion = "digital-sales-invoice-presentation-json-v1",
                templateVersion = "digital-sales-invoice-json-v1",
                contentType = "application/json",
                authoritativePresentation = new
                {
                    presentation = new
                    {
                        documentTitle = "Sales Invoice"
                    }
                },
                createdAt = "2026-05-23T13:00:00+08:00",
                updatedAt = "2026-05-23T13:01:00+08:00",
                correlationId = CorrelationId
            })
        });
        var client = CreateClient(handler);

        var result = await client.GetReceiptPresentationAsync(
            paymentAttemptId,
            CorrelationId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal($"/v1/webpay/payment-attempts/{paymentAttemptId:D}/receipt-presentation", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Get, handler.LastRequest.Method);
        Assert.Equal(CorrelationId.ToString(), handler.LastRequest.Headers.GetValues("X-Correlation-Id").Single());
        Assert.Equal("SI-20260523-000001", result.Value!.FiscalDocumentNumber);
        Assert.Equal("Sales Invoice", result.Value.AuthoritativePresentation.GetProperty("presentation").GetProperty("documentTitle").GetString());
    }

    /// <summary>
    /// Verifies Central PMS receipt readback errors remain safe and retryable when applicable.
    /// </summary>
    [Fact]
    public async Task GetReceiptPresentationAsync_WhenCentralPmsReturnsPending_MapsSafeRetryableError()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent(new
            {
                errorCode = "WEBPAY_RECEIPT_PRESENTATION_NOT_READY",
                message = "Fiscal issuance is not recorded; Sales Invoice presentation is not available yet.",
                retryable = true,
                correlationId = CorrelationId
            })
        });
        var client = CreateClient(handler);

        var result = await client.GetReceiptPresentationAsync(
            Guid.Parse("66666666-6666-6666-6666-666666666666"),
            CorrelationId,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Error!.StatusCode);
        Assert.Equal("WEBPAY_RECEIPT_PRESENTATION_NOT_READY", result.Error.ErrorCode);
        Assert.True(result.Error.Retryable);
        Assert.Equal(CorrelationId, result.Error.CorrelationId);
    }

    /// <summary>
    /// Verifies WebPay statutory-discount submit calls the shared Central PMS decision route with server-controlled WEBPAY identity.
    /// </summary>
    [Fact]
    public async Task SubmitStatutoryDiscountDecisionAsync_UsesSharedDecisionRouteAndServerControlledWebPayIdentity()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = JsonContent(StatutoryDecisionResponse())
        });
        var client = CreateClient(handler);

        var result = await client.SubmitStatutoryDiscountDecisionAsync(
            StatutoryDecisionRequest(),
            "statutory-decision:test",
            CorrelationId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/v1/statutory-discounts/decisions", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal(CorrelationId.ToString(), handler.LastRequest.Headers.GetValues("X-Correlation-Id").Single());
        Assert.Equal("statutory-decision:test", handler.LastRequest.Headers.GetValues("Idempotency-Key").Single());
        Assert.Equal(WebPayServiceIdentityId.ToString("D"), handler.LastRequest.Headers.GetValues("X-ExitPass-Service-Identity-Id").Single());
        Assert.Equal("statutory-discounts.decision.submit.webpay", handler.LastRequest.Headers.GetValues("X-ExitPass-Permissions").Single());

        using var document = JsonDocument.Parse(handler.LastRequestBody!);
        var root = document.RootElement;
        Assert.Equal("WEBPAY", root.GetProperty("sourceChannel").GetString());
        Assert.False(root.GetProperty("applyPayableBasis").GetBoolean());
        Assert.Equal("SENIOR_CITIZEN", root.GetProperty("entitlementType").GetString());
        Assert.Equal("WEBPAY-REQ-001", root.GetProperty("ticketReference").GetString());
        Assert.Equal("SC-****-0001", root.GetProperty("maskedIdReference").GetString());
        Assert.Equal(TariffSnapshotId, root.GetProperty("originalTariffSnapshotId").GetGuid());
        Assert.False(root.TryGetProperty("reviewerUserId", out _));
        Assert.False(root.TryGetProperty("reviewerAttestation", out _));
        Assert.False(root.TryGetProperty("operatorShiftId", out _));
        Assert.False(root.TryGetProperty("operatorDeviceBindingId", out _));
        Assert.False(root.TryGetProperty("actorUserId", out _));
    }

    /// <summary>
    /// Verifies durable statutory-discount readback uses the shared Central PMS GET route.
    /// </summary>
    [Fact]
    public async Task GetStatutoryDiscountDecisionAsync_UsesSharedReadbackRoute()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(StatutoryDecisionResponse())
        });
        var client = CreateClient(handler);

        var result = await client.GetStatutoryDiscountDecisionAsync(
            StatutoryDecisionCommandId,
            CorrelationId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal($"/v1/statutory-discounts/decisions/{StatutoryDecisionCommandId:D}", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal(CorrelationId.ToString(), handler.LastRequest.Headers.GetValues("X-Correlation-Id").Single());
        Assert.Equal(WebPayServiceIdentityId.ToString("D"), handler.LastRequest.Headers.GetValues("X-ExitPass-Service-Identity-Id").Single());
        Assert.Equal("statutory-discounts.decision.read", handler.LastRequest.Headers.GetValues("X-ExitPass-Permissions").Single());
        Assert.Equal(StatutoryApplicationCommandId, result.Value!.StatutoryDiscountPayableBasisApplicationCommandId);
        Assert.True(result.Value.PayableBasisReady);
    }

    /// <summary>
    /// Verifies application intent reuses the shared Central PMS POST route with applyPayableBasis set server-side.
    /// </summary>
    [Fact]
    public async Task ApplyStatutoryDiscountPayableBasisAsync_PostsApplicationIntentOnce()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = JsonContent(StatutoryDecisionResponse())
        });
        var client = CreateClient(handler);

        var result = await client.ApplyStatutoryDiscountPayableBasisAsync(
            StatutoryDecisionRequest(),
            "statutory-application:test",
            CorrelationId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/v1/statutory-discounts/decisions", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal("statutory-application:test", handler.LastRequest.Headers.GetValues("Idempotency-Key").Single());
        Assert.Equal(WebPayServiceIdentityId.ToString("D"), handler.LastRequest.Headers.GetValues("X-ExitPass-Service-Identity-Id").Single());
        Assert.Equal("statutory-discounts.decision.submit.webpay", handler.LastRequest.Headers.GetValues("X-ExitPass-Permissions").Single());
        using var document = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.True(document.RootElement.GetProperty("applyPayableBasis").GetBoolean());
        Assert.Equal("WEBPAY", document.RootElement.GetProperty("sourceChannel").GetString());
    }

    /// <summary>
    /// Verifies Central PMS semantic-conflict codes are preserved for WebPay instead of flattened to a generic submit failure.
    /// </summary>
    [Fact]
    public async Task SubmitStatutoryDiscountDecisionAsync_WhenSemanticConflict_PreservesSafeConflictCode()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent(new
            {
                errorCode = "STATUTORY_DISCOUNT_DECISION_SEMANTIC_CONFLICT",
                message = "A statutory discount request already exists with different submitted details.",
                retryable = false,
                correlationId = CorrelationId
            })
        });
        var client = CreateClient(handler);

        var result = await client.SubmitStatutoryDiscountDecisionAsync(
            StatutoryDecisionRequest(),
            "statutory-decision:test",
            CorrelationId,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Error!.StatusCode);
        Assert.Equal("STATUTORY_DISCOUNT_DECISION_SEMANTIC_CONFLICT", result.Error.ErrorCode);
        Assert.Equal("A statutory discount request already exists with different submitted details.", result.Error.Message);
        Assert.False(result.Error.Retryable);
        Assert.Equal(CorrelationId, result.Error.CorrelationId);
    }

    /// <summary>
    /// Verifies an opaque Central PMS 409 from statutory submit still maps to a deterministic semantic-conflict code.
    /// </summary>
    [Fact]
    public async Task SubmitStatutoryDiscountDecisionAsync_WhenOpaqueConflict_UsesSemanticConflictFallback()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent(new { detail = "A statutory discount request already exists with different submitted details." })
        });
        var client = CreateClient(handler);

        var result = await client.SubmitStatutoryDiscountDecisionAsync(
            StatutoryDecisionRequest(),
            "statutory-decision:test",
            CorrelationId,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Error!.StatusCode);
        Assert.Equal("STATUTORY_DISCOUNT_DECISION_SEMANTIC_CONFLICT", result.Error.ErrorCode);
        Assert.Contains("different submitted details", result.Error.Message);
        Assert.False(result.Error.Retryable);
    }

    /// <summary>
    /// Verifies statutory calls fail closed when the server-side Central PMS service identity is not configured.
    /// </summary>
    [Fact]
    public async Task SubmitStatutoryDiscountDecisionAsync_WhenServiceIdentityMissing_FailsClosedWithoutHttpRequest()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = JsonContent(StatutoryDecisionResponse())
        });
        var client = CreateClient(handler, configureStatutoryServiceIdentity: false);

        var result = await client.SubmitStatutoryDiscountDecisionAsync(
            StatutoryDecisionRequest(),
            "statutory-decision:test",
            CorrelationId,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(503, result.Error!.StatusCode);
        Assert.Equal("CENTRAL_PMS_AUTH_CONFIGURATION_MISSING", result.Error.ErrorCode);
        Assert.Equal(CorrelationId, result.Error.CorrelationId);
        Assert.Contains("Parking-privilege requests are temporarily unavailable", result.Error.Message);
        Assert.Equal(0, handler.SendCount);
        Assert.Null(handler.LastRequest);
    }

    /// <summary>
    /// Verifies opaque downstream JSON bodies are not reflected to WebPay callers.
    /// </summary>
    [Fact]
    public async Task GetStatutoryDiscountDecisionAsync_WhenErrorIsOpaque_DoesNotExposeRawDownstreamBody()
    {
        const string downstreamBody = "{\"internalException\":\"database timeout for reviewer notes\",\"rawEvidence\":\"secret\"}";
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent(downstreamBody, Encoding.UTF8, "application/json")
        });
        var client = CreateClient(handler);

        var result = await client.GetStatutoryDiscountDecisionAsync(
            StatutoryDecisionCommandId,
            CorrelationId,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("STATUTORY_DISCOUNT_DECISION_READ_FAILED", result.Error!.ErrorCode);
        Assert.Equal("Central PMS request failed.", result.Error.Message);
        Assert.DoesNotContain("database timeout", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies statutory ordinance availability is resolved through the Central PMS service-channel route.
    /// </summary>
    [Fact]
    public async Task ResolveStatutoryDiscountAvailabilityAsync_UsesAvailabilityRouteAndReadPermission()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(StatutoryAvailabilityResponse())
        });
        var client = CreateClient(handler);

        var result = await client.ResolveStatutoryDiscountAvailabilityAsync(
            StatutoryAvailabilityRequest(),
            CorrelationId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/v1/statutory-discounts/decisions/availability", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal(CorrelationId.ToString(), handler.LastRequest.Headers.GetValues("X-Correlation-Id").Single());
        Assert.Equal(WebPayServiceIdentityId.ToString("D"), handler.LastRequest.Headers.GetValues("X-ExitPass-Service-Identity-Id").Single());
        Assert.Equal("statutory-discounts.decision.read", handler.LastRequest.Headers.GetValues("X-ExitPass-Permissions").Single());
        Assert.False(handler.LastRequest.Headers.Contains("X-ExitPass-Source-Channel"));

        using var requestDocument = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("SENIOR_CITIZEN", requestDocument.RootElement.GetProperty("requestedEntitlementType").GetString());
        Assert.False(requestDocument.RootElement.TryGetProperty("sourceChannel", out _));
        Assert.False(requestDocument.RootElement.TryGetProperty("reviewerUserId", out _));

        Assert.Equal("AVAILABLE", result.Value!.AvailabilityStatus);
        Assert.Equal(new[] { "SENIOR_CITIZEN", "PWD" }, result.Value.CoveredEntitlementTypes);
        Assert.True(result.Value.Covers("SENIOR_CITIZEN"));
        Assert.True(result.Value.Covers("PWD"));
    }

    /// <summary>
    /// Verifies statutory availability fails closed without an HTTP call when service identity configuration is missing.
    /// </summary>
    [Fact]
    public async Task ResolveStatutoryDiscountAvailabilityAsync_WhenServiceIdentityMissing_FailsClosedWithoutHttpRequest()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(StatutoryAvailabilityResponse())
        });
        var client = CreateClient(handler, configureStatutoryServiceIdentity: false);

        var result = await client.ResolveStatutoryDiscountAvailabilityAsync(
            StatutoryAvailabilityRequest(),
            CorrelationId,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(503, result.Error!.StatusCode);
        Assert.Equal("CENTRAL_PMS_AUTH_CONFIGURATION_MISSING", result.Error.ErrorCode);
        Assert.Equal(0, handler.SendCount);
        Assert.Null(handler.LastRequest);
    }

    /// <summary>
    /// Verifies transient Central PMS availability failures are safely classified.
    /// </summary>
    [Fact]
    public async Task ResolveStatutoryDiscountAvailabilityAsync_WhenCentralPmsUnavailable_ReturnsRetryableFailure()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = JsonContent(new
            {
                errorCode = "CENTRAL_PMS_TEMPORARILY_UNAVAILABLE",
                message = "Central PMS is unavailable.",
                retryable = true,
                correlationId = CorrelationId
            })
        });
        var client = CreateClient(handler);

        var result = await client.ResolveStatutoryDiscountAvailabilityAsync(
            StatutoryAvailabilityRequest(),
            CorrelationId,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(503, result.Error!.StatusCode);
        Assert.Equal("CENTRAL_PMS_TEMPORARILY_UNAVAILABLE", result.Error.ErrorCode);
        Assert.True(result.Error.Retryable);
        Assert.Equal(CorrelationId, result.Error.CorrelationId);
    }

    /// <summary>
    /// Verifies pending-lifecycle rediscovery uses the WebPay service identity and rediscovery permission.
    /// </summary>
    [Fact]
    public async Task RediscoverStatutoryDiscountPendingLifecycleAsync_UsesRediscoveryRouteAndPermission()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(StatutoryPendingLifecycleRediscoveryResponse())
        });
        var client = CreateClient(handler);

        var result = await client.RediscoverStatutoryDiscountPendingLifecycleAsync(
            StatutoryPendingLifecycleRediscoveryRequest(),
            CorrelationId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/v1/webpay/statutory-discounts/pending-lifecycle/rediscover", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal(CorrelationId.ToString(), handler.LastRequest.Headers.GetValues("X-Correlation-Id").Single());
        Assert.Equal(WebPayServiceIdentityId.ToString("D"), handler.LastRequest.Headers.GetValues("X-ExitPass-Service-Identity-Id").Single());
        Assert.Equal("statutory-discounts.pending-lifecycle.rediscover.webpay", handler.LastRequest.Headers.GetValues("X-ExitPass-Permissions").Single());
        Assert.False(handler.LastRequest.Headers.Contains("Authorization"));

        using var requestDocument = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("PARKING_SESSION_ID", requestDocument.RootElement.GetProperty("lookupMode").GetString());
        Assert.Equal(ParkingSessionId, requestDocument.RootElement.GetProperty("parkingSessionId").GetGuid());
        Assert.False(requestDocument.RootElement.TryGetProperty("sourceChannel", out _));
        Assert.False(requestDocument.RootElement.TryGetProperty("reviewerUserId", out _));

        Assert.Equal("FOUND", result.Value!.Classification);
        Assert.Equal(StatutoryDecisionCommandId, result.Value.StatutoryDecisionCommandId);
        Assert.Equal("continuation:test:existing", result.Value.OpaqueContinuationReference);
        Assert.Equal("https://pay.example.test/privilege-review/opaque-existing", result.Value.OpaqueContinuationUrl);
    }

    /// <summary>
    /// Verifies pending-lifecycle rediscovery fails closed without an HTTP call when service identity configuration is missing.
    /// </summary>
    [Fact]
    public async Task RediscoverStatutoryDiscountPendingLifecycleAsync_WhenServiceIdentityMissing_FailsClosedWithoutHttpRequest()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(StatutoryPendingLifecycleRediscoveryResponse())
        });
        var client = CreateClient(handler, configureStatutoryServiceIdentity: false);

        var result = await client.RediscoverStatutoryDiscountPendingLifecycleAsync(
            StatutoryPendingLifecycleRediscoveryRequest(),
            CorrelationId,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(503, result.Error!.StatusCode);
        Assert.Equal("CENTRAL_PMS_AUTH_CONFIGURATION_MISSING", result.Error.ErrorCode);
        Assert.Equal(0, handler.SendCount);
        Assert.Null(handler.LastRequest);
    }

    /// <summary>
    /// Verifies malformed pending-lifecycle rediscovery responses fail closed.
    /// </summary>
    [Fact]
    public async Task RediscoverStatutoryDiscountPendingLifecycleAsync_WhenMalformed_ReturnsRetryableFailure()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(new { classification = "" })
        });
        var client = CreateClient(handler);

        var result = await client.RediscoverStatutoryDiscountPendingLifecycleAsync(
            StatutoryPendingLifecycleRediscoveryRequest(),
            CorrelationId,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(502, result.Error!.StatusCode);
        Assert.Equal("MALFORMED_STATUTORY_DISCOUNT_PENDING_LIFECYCLE_REDISCOVERY_RESPONSE", result.Error.ErrorCode);
        Assert.True(result.Error.Retryable);
        Assert.Equal(CorrelationId, result.Error.CorrelationId);
    }

    private static CentralPmsWebPayClient CreateClient(
        HttpMessageHandler handler,
        bool configureStatutoryServiceIdentity = true)
    {
        var values = new Dictionary<string, string?>
        {
            ["Integrations:CentralPms:BaseUrl"] = "http://central-pms.test"
        };
        if (configureStatutoryServiceIdentity)
        {
            values["Integrations:CentralPms:StatutoryDiscounts:WebPayServiceIdentityId"] = WebPayServiceIdentityId.ToString("D");
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return new CentralPmsWebPayClient(
            new HttpClient(handler),
            configuration,
            NullLogger<CentralPmsWebPayClient>.Instance);
    }

    private static StringContent JsonContent(object value)
    {
        return new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
    }

    private static CentralPmsStatutoryDiscountDecisionRequest StatutoryDecisionRequest()
    {
        return new CentralPmsStatutoryDiscountDecisionRequest(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            ParkingSessionId,
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "WEBPAY-REQ-001",
            "ABC1234",
            "SENIOR_CITIZEN",
            "OSCA",
            "QUEZON_CITY",
            DateOnly.Parse("2030-12-31"),
            "SC-****-0001",
            true,
            new[]
            {
                new CentralPmsStatutoryDiscountEvidenceReference(
                    "CARD_REFERENCE",
                    "CUSTOMER_UPLOAD_REFERENCE",
                    "masked-card.txt",
                    "text/plain",
                    128,
                    "evidence:webpay:001",
                    "SC-****-0001",
                    "PENDING_REVIEW")
            },
            true,
            "Customer attests eligibility for review.",
            null,
            TariffSnapshotId);
    }

    private static CentralPmsStatutoryDiscountAvailabilityRequest StatutoryAvailabilityRequest()
    {
        return new CentralPmsStatutoryDiscountAvailabilityRequest(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            ParkingSessionId,
            "SENIOR_CITIZEN",
            BeneficiaryResidencySatisfied: null);
    }

    private static CentralPmsStatutoryDiscountPendingLifecycleRediscoveryRequest StatutoryPendingLifecycleRediscoveryRequest()
    {
        return new CentralPmsStatutoryDiscountPendingLifecycleRediscoveryRequest(
            "PARKING_SESSION_ID",
            ParkingSessionId,
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            null,
            null,
            "WEBPAY_LOCAL_MOCK_PMS",
            null);
    }

    private static object StatutoryAvailabilityResponse()
    {
        return new
        {
            requestReference = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            parkingSessionId = ParkingSessionId,
            siteId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            siteGroupId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            availabilityStatus = "AVAILABLE",
            statutoryParkingBenefitAvailable = true,
            coveredEntitlementTypes = new[] { "SENIOR_CITIZEN", "PWD" },
            requestedEntitlementType = "SENIOR_CITIZEN",
            safeReasonCode = (string?)null,
            retryable = false,
            remediationAction = "CONTINUE_WITH_ORDINARY_PAYMENT",
            requiredEvidenceTypes = Array.Empty<object>(),
            correlationId = CorrelationId
        };
    }

    private static object StatutoryPendingLifecycleRediscoveryResponse()
    {
        return new
        {
            classification = "FOUND",
            statutoryDecisionId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            statutoryDecisionCommandId = StatutoryDecisionCommandId,
            requestReference = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            entitlementType = "SENIOR_CITIZEN",
            decisionStatus = "AWAITING_REVIEW",
            payableBasisStatus = "AWAITING_REVIEW",
            parkingSessionId = ParkingSessionId,
            siteId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            siteGroupId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            opaqueContinuationReference = "continuation:test:existing",
            opaqueContinuationUrl = "https://pay.example.test/privilege-review/opaque-existing",
            lifecycleState = "PENDING_REVIEW",
            retryable = true,
            correlationId = CorrelationId,
            createdAt = "2026-07-30T08:00:00Z",
            updatedAt = "2026-07-30T08:01:00Z",
            submittedAt = "2026-07-30T08:00:30Z",
            decidedAt = (string?)null,
            reviewedAt = (string?)null
        };
    }

    private static object StatutoryDecisionResponse()
    {
        return new
        {
            statutoryDiscountDecisionCommandId = StatutoryDecisionCommandId,
            requestReference = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            statutoryDiscountValidationId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            parkingSessionId = ParkingSessionId,
            sourceChannel = "WEBPAY",
            entitlementType = "SENIOR_CITIZEN",
            decisionStatus = "APPROVED",
            policyResolutionBasis = "STATUTORY",
            localOrdinanceApplied = false,
            grossAmountMinorUnits = 12500,
            vatExclusiveBasisAmountMinorUnits = 11161,
            vatAmountMinorUnits = 1339,
            vatTreatment = "VAT_EXEMPT_SENIOR_CITIZEN",
            statutoryDiscountAmountMinorUnits = 2500,
            netPayableAmountMinorUnits = 10000,
            currency = "PHP",
            evidenceRequired = true,
            evidenceRecorded = true,
            correlationId = CorrelationId,
            createdAt = "2026-05-16T12:00:00Z",
            decidedAt = "2026-05-16T12:05:00Z",
            appliedAt = "2026-05-16T12:10:00Z",
            originalTariffSnapshotId = TariffSnapshotId,
            appliedTariffSnapshotId = TariffSnapshotId,
            commandStatus = "COMPLETED",
            clientResultStatus = "APPROVED",
            resultClassification = "APPLIED",
            semanticHashSourceVersion = "statutory-discount-decision:sha256:v2",
            retryable = false,
            recoveryClassification = "NONE",
            decisionCommandStatus = "COMPLETED",
            decisionResultStatus = "APPROVED",
            statutoryDiscountPayableBasisApplicationCommandId = StatutoryApplicationCommandId,
            applicationRequested = true,
            applicationCommandStatus = "APPLIED",
            applicationResultClassification = "APPLIED",
            applicationSemanticHashSourceVersion = "statutory-discount-payable-basis-application:sha256:v1",
            overallResultClassification = "APPLIED",
            oneShotComplete = true,
            siteId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            siteGroupId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            payableBasisReady = true,
            payableBasisReadinessStatus = "READY"
        };
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public CapturingHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastRequestBody { get; private set; }

        public int SendCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SendCount++;
            LastRequest = request;
            LastRequestBody = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            return Task.FromResult(_response);
        }
    }
}
