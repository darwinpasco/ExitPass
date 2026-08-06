using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ExitPass.PaymentOrchestrator.Infrastructure.Integrations;

/// <summary>
/// HTTP client for Central PMS APIs composed by the WebPay payment intent flow.
/// </summary>
public sealed class CentralPmsWebPayClient : ICentralPmsWebPayClient, ICentralPmsWebPayStatutoryEvidenceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<CentralPmsWebPayClient> _logger;
    private readonly Uri _vendorParkingResolveUri;
    private readonly Uri _createPaymentAttemptUri;
    private readonly Uri _paymentAttemptsBaseUri;
    private readonly Uri _webPayPaymentAttemptsBaseUri;
    private readonly Uri _statutoryDiscountAvailabilityUri;
    private readonly Uri _statutoryDiscountPendingLifecycleRediscoveryUri;
    private readonly Uri _statutoryDiscountDecisionsUri;
    private readonly Uri _statutoryDiscountDecisionsBaseUri;
    private readonly Uri _statutoryEvidenceBaseUri;
    private readonly bool _statutoryDiscountServiceIdentityConfigured;
    private readonly Guid _statutoryDiscountWebPayServiceIdentityId;

    private const string CentralPmsPermissionsHeaderName = "X-ExitPass-Permissions";
    private const string CentralPmsServiceIdentityIdHeaderName = "X-ExitPass-Service-Identity-Id";
    private const string StatutoryDiscountSubmitWebPayPermission = "statutory-discounts.decision.submit.webpay";
    private const string StatutoryDiscountDecisionReadPermission = "statutory-discounts.decision.read";
    private const string StatutoryDiscountPendingLifecycleRediscoverWebPayPermission = "statutory-discounts.pending-lifecycle.rediscover.webpay";
    private const string StatutoryEvidenceCaptureWebPayPermission = "statutory-discounts.evidence.capture.webpay";

    /// <summary>
    /// Initializes a new instance of the <see cref="CentralPmsWebPayClient"/> class.
    /// </summary>
    /// <param name="httpClient">HTTP client used to call Central PMS.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="logger">Structured logger.</param>
    public CentralPmsWebPayClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<CentralPmsWebPayClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        var baseUrl = configuration["Integrations:CentralPms:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("Configuration value 'Integrations:CentralPms:BaseUrl' is required.");
        }

        var normalizedBaseUrl = new Uri(baseUrl.TrimEnd('/') + "/");
        _httpClient = httpClient;
        _logger = logger;
        _vendorParkingResolveUri = new Uri(normalizedBaseUrl, "v1/vendor-parking/resolve");
        _createPaymentAttemptUri = new Uri(normalizedBaseUrl, "v1/public/payment-attempts");
        _paymentAttemptsBaseUri = new Uri(normalizedBaseUrl, "v1/internal/payment-attempts/");
        _webPayPaymentAttemptsBaseUri = new Uri(normalizedBaseUrl, "v1/webpay/payment-attempts/");
        _statutoryDiscountAvailabilityUri = new Uri(normalizedBaseUrl, "v1/statutory-discounts/decisions/availability");
        _statutoryDiscountPendingLifecycleRediscoveryUri = new Uri(normalizedBaseUrl, "v1/webpay/statutory-discounts/pending-lifecycle/rediscover");
        _statutoryDiscountDecisionsUri = new Uri(normalizedBaseUrl, "v1/statutory-discounts/decisions");
        _statutoryDiscountDecisionsBaseUri = new Uri(normalizedBaseUrl, "v1/statutory-discounts/decisions/");
        _statutoryEvidenceBaseUri = new Uri(normalizedBaseUrl, "v1/webpay/statutory-discounts/evidence/");

        var serviceIdentityValue = configuration["Integrations:CentralPms:StatutoryDiscounts:WebPayServiceIdentityId"];
        _statutoryDiscountServiceIdentityConfigured =
            Guid.TryParse(serviceIdentityValue, out _statutoryDiscountWebPayServiceIdentityId) &&
            _statutoryDiscountWebPayServiceIdentityId != Guid.Empty;
    }

    /// <inheritdoc />
    public async Task<CentralPmsWebPayResult<CentralPmsResolvedParking>> ResolveVendorParkingAsync(
        Guid? siteGroupId,
        Guid? siteId,
        string vendorSystemId,
        string? plateNumber,
        string? ticketReference,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var body = new VendorParkingResolveRequest(
            SiteGroupId: siteGroupId?.ToString() ?? string.Empty,
            SiteId: siteId?.ToString() ?? string.Empty,
            VendorSystemId: vendorSystemId,
            PlateNumber: plateNumber,
            TicketReference: ticketReference,
            CorrelationId: correlationId);

        using var request = new HttpRequestMessage(HttpMethod.Post, _vendorParkingResolveUri)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        request.Headers.Add("X-Correlation-Id", correlationId.ToString());

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return CentralPmsWebPayResult<CentralPmsResolvedParking>.Failure(
                ReadError((int)response.StatusCode, responseBody, "VENDOR_PARKING_RESOLUTION_FAILED"));
        }

        var payload = JsonSerializer.Deserialize<VendorParkingResolveResponse>(responseBody, JsonOptions);
        if (payload is null)
        {
            return CentralPmsWebPayResult<CentralPmsResolvedParking>.Failure(new CentralPmsWebPayError(
                502,
                "MALFORMED_VENDOR_RESPONSE",
                "Central PMS vendor parking response could not be parsed.",
                true));
        }

        return CentralPmsWebPayResult<CentralPmsResolvedParking>.Success(new CentralPmsResolvedParking(
            payload.ParkingSessionId,
            payload.TariffSnapshotId,
            payload.NetPayableMinorUnits,
            payload.Currency,
            ResolveVendorSystemId(payload.VendorSystemId, vendorSystemId),
            payload.CorrelationId,
            payload.SiteName,
            payload.TicketReference,
            payload.PlateNumber,
            payload.EntryTime,
            payload.CurrentFeeCalculationTime,
            payload.TariffName,
            payload.ParkingStatus,
            payload.FeeValidUntil ?? payload.TariffExpiresAt,
            payload.PaymentStatus,
            ParseGuid(payload.SiteGroupId),
            ParseGuid(payload.SiteId),
            payload.SiteGroupName));
    }

    /// <inheritdoc />
    public async Task<CentralPmsWebPayResult<CentralPmsPaymentAttempt>> CreateOrReusePaymentAttemptAsync(
        Guid parkingSessionId,
        Guid tariffSnapshotId,
        string paymentProvider,
        string paymentMethod,
        string idempotencyKey,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var body = new CreatePaymentAttemptRequest(
            ParkingSessionId: parkingSessionId,
            TariffSnapshotId: tariffSnapshotId,
            PaymentProvider: paymentProvider,
            PaymentMethod: paymentMethod);

        using var request = new HttpRequestMessage(HttpMethod.Post, _createPaymentAttemptUri)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        request.Headers.Add("X-Correlation-Id", correlationId.ToString());
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return CentralPmsWebPayResult<CentralPmsPaymentAttempt>.Failure(
                ReadError((int)response.StatusCode, responseBody, "PAYMENT_ATTEMPT_CREATE_FAILED"));
        }

        var payload = JsonSerializer.Deserialize<CreatePaymentAttemptResponse>(responseBody, JsonOptions);
        if (payload is null)
        {
            return CentralPmsWebPayResult<CentralPmsPaymentAttempt>.Failure(new CentralPmsWebPayError(
                502,
                "MALFORMED_PAYMENT_ATTEMPT_RESPONSE",
                "Central PMS payment attempt response could not be parsed.",
                true));
        }

        return CentralPmsWebPayResult<CentralPmsPaymentAttempt>.Success(new CentralPmsPaymentAttempt(
            payload.PaymentAttemptId,
            payload.AttemptStatus,
            payload.PaymentProvider,
            payload.WasReused));
    }

    /// <inheritdoc />
    public async Task<CentralPmsWebPayResult<CentralPmsPaymentAttempt>> FinalizePaymentAttemptAsync(
        Guid paymentAttemptId,
        string finalAttemptStatus,
        string requestedBy,
        string idempotencyKey,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var body = new FinalizePaymentAttemptRequest(finalAttemptStatus, requestedBy);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(_paymentAttemptsBaseUri, $"{paymentAttemptId:D}/finalize"))
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        request.Headers.Add("X-Correlation-Id", correlationId.ToString());
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return CentralPmsWebPayResult<CentralPmsPaymentAttempt>.Failure(
                ReadError((int)response.StatusCode, responseBody, "PAYMENT_ATTEMPT_FINALIZE_FAILED"));
        }

        var payload = JsonSerializer.Deserialize<FinalizePaymentAttemptResponse>(responseBody, JsonOptions);
        if (payload is null)
        {
            return CentralPmsWebPayResult<CentralPmsPaymentAttempt>.Failure(new CentralPmsWebPayError(
                502,
                "MALFORMED_PAYMENT_ATTEMPT_FINALIZE_RESPONSE",
                "Central PMS payment attempt finalization response could not be parsed.",
                true));
        }

        return CentralPmsWebPayResult<CentralPmsPaymentAttempt>.Success(new CentralPmsPaymentAttempt(
            payload.PaymentAttemptId,
            payload.AttemptStatus,
            string.Empty,
            false));
    }

    /// <inheritdoc />
    public async Task<CentralPmsWebPayResult<CentralPmsWebPayReceiptPresentation>> GetReceiptPresentationAsync(
        Guid paymentAttemptId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(_webPayPaymentAttemptsBaseUri, $"{paymentAttemptId:D}/receipt-presentation"));
        request.Headers.Add("X-Correlation-Id", correlationId.ToString());

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return CentralPmsWebPayResult<CentralPmsWebPayReceiptPresentation>.Failure(
                ReadError((int)response.StatusCode, responseBody, "WEBPAY_RECEIPT_PRESENTATION_READ_FAILED"));
        }

        var payload = JsonSerializer.Deserialize<WebPayReceiptPresentationResponse>(responseBody, JsonOptions);
        if (payload is null)
        {
            return CentralPmsWebPayResult<CentralPmsWebPayReceiptPresentation>.Failure(new CentralPmsWebPayError(
                502,
                "MALFORMED_WEBPAY_RECEIPT_PRESENTATION_RESPONSE",
                "Central PMS receipt presentation response could not be parsed.",
                true));
        }

        return CentralPmsWebPayResult<CentralPmsWebPayReceiptPresentation>.Success(new CentralPmsWebPayReceiptPresentation(
            payload.PaymentAttemptId,
            payload.PaymentConfirmationId,
            payload.FiscalIssuanceReferenceId,
            payload.FiscalIssuanceState,
            payload.PosFiscalDocumentId,
            payload.FiscalDocumentNumber,
            payload.FiscalDocumentStatus,
            payload.ReceiptAvailabilityState,
            payload.PresentationVersion,
            payload.TemplateVersion,
            payload.ContentType,
            payload.AuthoritativePresentation,
            payload.VoidStatus,
            payload.VoidReasonCode,
            payload.VoidedAt,
            payload.CreatedAt,
            payload.UpdatedAt,
            payload.CorrelationId));
    }

    /// <inheritdoc />
    public async Task<CentralPmsWebPayResult<CentralPmsStatutoryDiscountAvailability>> ResolveStatutoryDiscountAvailabilityAsync(
        CentralPmsStatutoryDiscountAvailabilityRequest request,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = new StatutoryDiscountAvailabilityRequest(
            request.RequestReference,
            request.ParkingSessionId,
            request.RequestedEntitlementType,
            request.BeneficiaryResidencySatisfied);

        using var message = new HttpRequestMessage(HttpMethod.Post, _statutoryDiscountAvailabilityUri)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        message.Headers.Add("X-Correlation-Id", correlationId.ToString());
        if (!TryAddStatutoryDiscountServiceHeaders(
                message,
                StatutoryDiscountDecisionReadPermission,
                correlationId,
                out var serviceAuthError))
        {
            return CentralPmsWebPayResult<CentralPmsStatutoryDiscountAvailability>.Failure(serviceAuthError!);
        }

        using var response = await SendStatutoryDiscountAsync(
            message,
            "availability",
            correlationId,
            cancellationToken);
        if (response is null)
        {
            return CentralPmsWebPayResult<CentralPmsStatutoryDiscountAvailability>.Failure(
                BuildTransientStatutoryDiscountError(correlationId));
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return CentralPmsWebPayResult<CentralPmsStatutoryDiscountAvailability>.Failure(
                ReadError((int)response.StatusCode, responseBody, "STATUTORY_DISCOUNT_AVAILABILITY_FAILED"));
        }

        var payload = JsonSerializer.Deserialize<StatutoryDiscountAvailabilityResponse>(responseBody, JsonOptions);
        if (payload is null || string.IsNullOrWhiteSpace(payload.AvailabilityStatus))
        {
            return CentralPmsWebPayResult<CentralPmsStatutoryDiscountAvailability>.Failure(new CentralPmsWebPayError(
                502,
                "MALFORMED_STATUTORY_DISCOUNT_AVAILABILITY_RESPONSE",
                "Central PMS statutory-discount availability response could not be parsed.",
                true,
                correlationId));
        }

        return CentralPmsWebPayResult<CentralPmsStatutoryDiscountAvailability>.Success(ToStatutoryAvailability(payload));
    }

    /// <inheritdoc />
    public async Task<CentralPmsWebPayResult<CentralPmsStatutoryDiscountPendingLifecycleRediscovery>> RediscoverStatutoryDiscountPendingLifecycleAsync(
        CentralPmsStatutoryDiscountPendingLifecycleRediscoveryRequest request,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = new StatutoryDiscountPendingLifecycleRediscoveryRequest(
            request.LookupMode,
            request.ParkingSessionId,
            request.SiteId,
            request.SiteGroupId,
            request.TicketReference,
            request.PlateNumber,
            request.VendorSystemId,
            request.EntitlementType);

        using var message = new HttpRequestMessage(HttpMethod.Post, _statutoryDiscountPendingLifecycleRediscoveryUri)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        message.Headers.Add("X-Correlation-Id", correlationId.ToString());
        if (!TryAddStatutoryDiscountServiceHeaders(
                message,
                StatutoryDiscountPendingLifecycleRediscoverWebPayPermission,
                correlationId,
                out var serviceAuthError))
        {
            return CentralPmsWebPayResult<CentralPmsStatutoryDiscountPendingLifecycleRediscovery>.Failure(serviceAuthError!);
        }

        using var response = await SendStatutoryDiscountAsync(
            message,
            "pending lifecycle rediscovery",
            correlationId,
            cancellationToken);
        if (response is null)
        {
            return CentralPmsWebPayResult<CentralPmsStatutoryDiscountPendingLifecycleRediscovery>.Failure(
                BuildTransientStatutoryDiscountError(correlationId));
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return CentralPmsWebPayResult<CentralPmsStatutoryDiscountPendingLifecycleRediscovery>.Failure(
                ReadError((int)response.StatusCode, responseBody, "STATUTORY_DISCOUNT_PENDING_LIFECYCLE_REDISCOVERY_FAILED"));
        }

        var payload = JsonSerializer.Deserialize<StatutoryDiscountPendingLifecycleRediscoveryResponse>(responseBody, JsonOptions);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Classification))
        {
            return CentralPmsWebPayResult<CentralPmsStatutoryDiscountPendingLifecycleRediscovery>.Failure(new CentralPmsWebPayError(
                502,
                "MALFORMED_STATUTORY_DISCOUNT_PENDING_LIFECYCLE_REDISCOVERY_RESPONSE",
                "Central PMS statutory-discount pending lifecycle rediscovery response could not be parsed.",
                true,
                correlationId));
        }

        return CentralPmsWebPayResult<CentralPmsStatutoryDiscountPendingLifecycleRediscovery>.Success(
            ToPendingLifecycleRediscovery(payload));
    }

    /// <inheritdoc />
    public Task<CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>> SubmitStatutoryDiscountDecisionAsync(
        CentralPmsStatutoryDiscountDecisionRequest request,
        string idempotencyKey,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        return SendStatutoryDiscountDecisionAsync(
            request,
            idempotencyKey,
            correlationId,
            applyPayableBasis: false,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>> GetStatutoryDiscountDecisionAsync(
        Guid statutoryDiscountDecisionCommandId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(_statutoryDiscountDecisionsBaseUri, statutoryDiscountDecisionCommandId.ToString("D")));
        request.Headers.Add("X-Correlation-Id", correlationId.ToString());
        if (!TryAddStatutoryDiscountServiceHeaders(
                request,
                StatutoryDiscountDecisionReadPermission,
                correlationId,
                out var serviceAuthError))
        {
            return CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>.Failure(serviceAuthError!);
        }

        using var response = await SendStatutoryDiscountAsync(
            request,
            "readback",
            correlationId,
            cancellationToken);
        if (response is null)
        {
            return CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>.Failure(
                BuildTransientStatutoryDiscountError(correlationId));
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>.Failure(
                ReadError((int)response.StatusCode, responseBody, "STATUTORY_DISCOUNT_DECISION_READ_FAILED"));
        }

        var payload = JsonSerializer.Deserialize<StatutoryDiscountDecisionResponse>(responseBody, JsonOptions);
        if (payload is null)
        {
            return CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>.Failure(new CentralPmsWebPayError(
                502,
                "MALFORMED_STATUTORY_DISCOUNT_READBACK_RESPONSE",
                "Central PMS statutory-discount response could not be parsed.",
                true,
                correlationId));
        }

        return CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>.Success(ToStatutoryDecision(payload));
    }

    /// <inheritdoc />
    public Task<CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>> ApplyStatutoryDiscountPayableBasisAsync(
        CentralPmsStatutoryDiscountDecisionRequest request,
        string idempotencyKey,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        return SendStatutoryDiscountDecisionAsync(
            request,
            idempotencyKey,
            correlationId,
            applyPayableBasis: true,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<CentralPmsWebPayResult<CentralPmsStatutoryEvidenceChannel>> BootstrapAsync(
        CentralPmsStatutoryEvidenceBootstrapRequest request,
        Guid correlationId,
        CancellationToken cancellationToken) =>
        SendEvidenceChannelJsonAsync(
            HttpMethod.Post,
            new Uri(_statutoryEvidenceBaseUri, "bootstrap"),
            new StatutoryEvidenceBootstrapRequest(request.StatutoryDiscountDecisionCommandId, request.ClientOperationKey),
            "bootstrap",
            correlationId,
            cancellationToken);

    /// <inheritdoc />
    public Task<CentralPmsWebPayResult<CentralPmsStatutoryEvidenceChannel>> GetStatusAsync(
        Guid? statutoryDiscountDecisionCommandId,
        Guid? evidenceSetReference,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var query = statutoryDiscountDecisionCommandId is Guid decisionId
            ? $"status?statutoryDiscountDecisionCommandId={decisionId:D}"
            : $"status?evidenceSetReference={evidenceSetReference:D}";

        return SendEvidenceChannelJsonAsync<object?>(
            HttpMethod.Get,
            new Uri(_statutoryEvidenceBaseUri, query),
            null,
            "status",
            correlationId,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CentralPmsWebPayResult<CentralPmsStatutoryEvidenceUploadSession>> CreateUploadSessionAsync(
        CentralPmsStatutoryEvidenceUploadSessionRequest request,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(_statutoryEvidenceBaseUri, "upload-sessions"))
        {
            Content = JsonContent.Create(new StatutoryEvidenceUploadSessionRequest(
                request.EvidenceSetReference,
                request.EvidenceItemReference,
                request.DeclaredContentType,
                request.DeclaredContentLength,
                request.DeclaredChecksumSha256,
                request.ClientOperationKey), options: JsonOptions)
        };

        return await SendEvidenceUploadSessionAsync(message, "upload-session", correlationId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CentralPmsWebPayResult<CentralPmsStatutoryEvidenceUploadSession>> UploadAsync(
        Guid opaqueUploadSessionReference,
        string contentType,
        long contentLength,
        Stream content,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        using var streamContent = new StreamContent(content);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        streamContent.Headers.ContentLength = contentLength;
        using var message = new HttpRequestMessage(HttpMethod.Put, new Uri(_statutoryEvidenceBaseUri, $"upload-sessions/{opaqueUploadSessionReference:D}"))
        {
            Content = streamContent
        };

        return await SendEvidenceUploadSessionAsync(message, "upload", correlationId, cancellationToken, HttpCompletionOption.ResponseHeadersRead);
    }

    /// <inheritdoc />
    public Task<CentralPmsWebPayResult<CentralPmsStatutoryEvidenceChannel>> FinalizeAsync(
        Guid opaqueUploadSessionReference,
        string? clientOperationKey,
        Guid correlationId,
        CancellationToken cancellationToken) =>
        SendEvidenceChannelJsonAsync(
            HttpMethod.Post,
            new Uri(_statutoryEvidenceBaseUri, $"upload-sessions/{opaqueUploadSessionReference:D}/finalize"),
            new StatutoryEvidenceFinalizeRequest(clientOperationKey),
            "finalize",
            correlationId,
            cancellationToken);

    private async Task<CentralPmsWebPayResult<CentralPmsStatutoryEvidenceChannel>> SendEvidenceChannelJsonAsync<TRequest>(
        HttpMethod method,
        Uri uri,
        TRequest requestBody,
        string operation,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri);
        if (requestBody is not null)
        {
            request.Content = JsonContent.Create(requestBody, options: JsonOptions);
        }

        if (!TryAddStatutoryDiscountServiceHeaders(request, StatutoryEvidenceCaptureWebPayPermission, correlationId, out var authError))
        {
            return CentralPmsWebPayResult<CentralPmsStatutoryEvidenceChannel>.Failure(authError!);
        }

        request.Headers.Add("X-Correlation-Id", correlationId.ToString("D"));
        using var response = await SendStatutoryDiscountAsync(request, $"evidence-{operation}", correlationId, cancellationToken);
        if (response is null)
        {
            return CentralPmsWebPayResult<CentralPmsStatutoryEvidenceChannel>.Failure(BuildTransientStatutoryDiscountError(correlationId));
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return CentralPmsWebPayResult<CentralPmsStatutoryEvidenceChannel>.Failure(
                ReadError((int)response.StatusCode, responseBody, "STATUTORY_EVIDENCE_REQUEST_FAILED"));
        }

        var payload = JsonSerializer.Deserialize<StatutoryEvidenceChannelResponse>(responseBody, JsonOptions);
        return !IsValidStatutoryEvidenceChannel(payload)
            ? CentralPmsWebPayResult<CentralPmsStatutoryEvidenceChannel>.Failure(new CentralPmsWebPayError(
                502, "MALFORMED_STATUTORY_EVIDENCE_RESPONSE", "Central PMS evidence response could not be parsed.", true, correlationId))
            : CentralPmsWebPayResult<CentralPmsStatutoryEvidenceChannel>.Success(ToStatutoryEvidenceChannel(payload!));
    }

    private async Task<CentralPmsWebPayResult<CentralPmsStatutoryEvidenceUploadSession>> SendEvidenceUploadSessionAsync(
        HttpRequestMessage request,
        string operation,
        Guid correlationId,
        CancellationToken cancellationToken,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead)
    {
        if (!TryAddStatutoryDiscountServiceHeaders(request, StatutoryEvidenceCaptureWebPayPermission, correlationId, out var authError))
        {
            return CentralPmsWebPayResult<CentralPmsStatutoryEvidenceUploadSession>.Failure(authError!);
        }

        request.Headers.Add("X-Correlation-Id", correlationId.ToString("D"));
        HttpResponseMessage? response;
        try
        {
            response = await _httpClient.SendAsync(request, completionOption, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Central PMS statutory evidence {Operation} timed out. CorrelationId {CorrelationId}", operation, correlationId);
            response = null;
        }
        catch (HttpRequestException)
        {
            _logger.LogWarning("Central PMS statutory evidence {Operation} failed before a response was received. CorrelationId {CorrelationId}", operation, correlationId);
            response = null;
        }

        using (response)
        {
            if (response is null)
            {
                return CentralPmsWebPayResult<CentralPmsStatutoryEvidenceUploadSession>.Failure(BuildTransientStatutoryDiscountError(correlationId));
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return CentralPmsWebPayResult<CentralPmsStatutoryEvidenceUploadSession>.Failure(
                    ReadError((int)response.StatusCode, responseBody, "STATUTORY_EVIDENCE_UPLOAD_FAILED"));
            }

            var payload = JsonSerializer.Deserialize<StatutoryEvidenceUploadSessionResponse>(responseBody, JsonOptions);
            return !IsValidStatutoryEvidenceUploadSession(payload)
                ? CentralPmsWebPayResult<CentralPmsStatutoryEvidenceUploadSession>.Failure(new CentralPmsWebPayError(
                    502, "MALFORMED_STATUTORY_EVIDENCE_UPLOAD_RESPONSE", "Central PMS evidence upload response could not be parsed.", true, correlationId))
                : CentralPmsWebPayResult<CentralPmsStatutoryEvidenceUploadSession>.Success(ToStatutoryEvidenceUploadSession(payload!));
        }
    }

    private static bool IsValidStatutoryEvidenceChannel(StatutoryEvidenceChannelResponse? payload) =>
        payload is not null &&
        !string.IsNullOrWhiteSpace(payload.Classification) &&
        payload.CorrelationId != Guid.Empty &&
        string.Equals(payload.SourceChannel, "WEBPAY", StringComparison.Ordinal) &&
        payload.AllowedContentTypes is not null &&
        payload.MaximumContentLengthBytes >= 0 &&
        !string.IsNullOrWhiteSpace(payload.LifecycleClassification) &&
        !string.IsNullOrWhiteSpace(payload.ReplacementPosture) &&
        payload.EvaluatedAt != default;

    private static bool IsValidStatutoryEvidenceUploadSession(StatutoryEvidenceUploadSessionResponse? payload) =>
        payload is not null &&
        !string.IsNullOrWhiteSpace(payload.Classification) &&
        payload.CorrelationId != Guid.Empty &&
        payload.OpaqueUploadSessionReference is Guid reference && reference != Guid.Empty &&
        string.Equals(payload.Method, HttpMethod.Put.Method, StringComparison.Ordinal) &&
        payload.ExpiresAt is not null &&
        !string.IsNullOrWhiteSpace(payload.AcceptedContentType) &&
        payload.MaximumContentLengthBytes > 0;

    private static CentralPmsStatutoryEvidenceChannel ToStatutoryEvidenceChannel(StatutoryEvidenceChannelResponse payload) =>
        new(payload.Classification, payload.Retryable, payload.ErrorCode, payload.CorrelationId, payload.SourceChannel,
            payload.EvidenceRequired, payload.EvidenceSetReference, payload.EvidenceItemReference,
            payload.AllowedContentTypes ?? Array.Empty<string>(), payload.MaximumContentLengthBytes,
            payload.MaximumImageWidth, payload.MaximumImageHeight, payload.MaximumImagePixelCount,
            payload.RequiredDocumentType, payload.RequiredItemRole, payload.LifecycleClassification,
            payload.ReplacementPosture, payload.ReadyForReview, payload.ReadyForAptPreCash,
            payload.BlockingReasonCode, payload.EvaluatedAt);

    private static CentralPmsStatutoryEvidenceUploadSession ToStatutoryEvidenceUploadSession(StatutoryEvidenceUploadSessionResponse payload) =>
        new(payload.Classification, payload.Retryable, payload.ErrorCode, payload.CorrelationId,
            payload.OpaqueUploadSessionReference, payload.Method, payload.ExpiresAt,
            payload.AcceptedContentType, payload.MaximumContentLengthBytes);

    private async Task<CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>> SendStatutoryDiscountDecisionAsync(
        CentralPmsStatutoryDiscountDecisionRequest request,
        string idempotencyKey,
        Guid correlationId,
        bool applyPayableBasis,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = new StatutoryDiscountDecisionRequest(
            request.RequestReference,
            SourceChannel: "WEBPAY",
            request.ParkingSessionId,
            request.SiteId,
            request.SiteGroupId,
            request.TicketReference,
            request.PlateNumber,
            request.EntitlementType,
            request.IdDocumentType,
            request.IssuingAuthority,
            request.ExpiryDate,
            request.MaskedIdReference,
            request.EvidenceCaptureRequested,
            request.EvidenceReferences?.Select(static evidence => new StatutoryDiscountEvidenceReferenceRequest(
                evidence.EvidenceType,
                evidence.CaptureMethod,
                evidence.FileName,
                evidence.ContentType,
                evidence.SizeBytes,
                evidence.StorageReference,
                evidence.ReferenceNumberMasked,
                evidence.VerificationStatus)).ToArray(),
            ActorUserId: null,
            OperatorDeviceBindingId: null,
            OperatorShiftId: null,
            request.RequesterAttestation,
            request.AttestationNotes,
            request.ReasonCode,
            Decision: null,
            DecisionReasonCode: null,
            ReviewerUserId: null,
            ReviewerAttestation: null,
            applyPayableBasis,
            request.OriginalTariffSnapshotId);

        using var message = new HttpRequestMessage(HttpMethod.Post, _statutoryDiscountDecisionsUri)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        message.Headers.Add("X-Correlation-Id", correlationId.ToString());
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        if (!TryAddStatutoryDiscountServiceHeaders(
                message,
                StatutoryDiscountSubmitWebPayPermission,
                correlationId,
                out var serviceAuthError))
        {
            return CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>.Failure(serviceAuthError!);
        }

        using var response = await SendStatutoryDiscountAsync(
            message,
            applyPayableBasis ? "payable-basis application" : "decision submit",
            correlationId,
            cancellationToken);
        if (response is null)
        {
            return CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>.Failure(
                BuildTransientStatutoryDiscountError(correlationId));
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>.Failure(
                ReadError(
                    (int)response.StatusCode,
                    responseBody,
                    ResolveStatutoryDiscountSubmitFallbackCode((int)response.StatusCode, applyPayableBasis)));
        }

        var payload = JsonSerializer.Deserialize<StatutoryDiscountDecisionResponse>(responseBody, JsonOptions);
        if (payload is null)
        {
            return CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>.Failure(new CentralPmsWebPayError(
                502,
                "MALFORMED_STATUTORY_DISCOUNT_DECISION_RESPONSE",
                "Central PMS statutory-discount response could not be parsed.",
                true,
                correlationId));
        }

        return CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>.Success(ToStatutoryDecision(payload));
    }

    private bool TryAddStatutoryDiscountServiceHeaders(
        HttpRequestMessage request,
        string permission,
        Guid correlationId,
        out CentralPmsWebPayError? error)
    {
        if (!_statutoryDiscountServiceIdentityConfigured)
        {
            error = new CentralPmsWebPayError(
                503,
                "CENTRAL_PMS_AUTH_CONFIGURATION_MISSING",
                "Parking-privilege requests are temporarily unavailable. Please try again later or ask a parking attendant for assistance.",
                true,
                correlationId);
            return false;
        }

        request.Headers.Remove(CentralPmsServiceIdentityIdHeaderName);
        request.Headers.Remove(CentralPmsPermissionsHeaderName);
        request.Headers.Add(CentralPmsServiceIdentityIdHeaderName, _statutoryDiscountWebPayServiceIdentityId.ToString("D"));
        request.Headers.Add(CentralPmsPermissionsHeaderName, permission);
        error = null;
        return true;
    }

    private async Task<HttpResponseMessage?> SendStatutoryDiscountAsync(
        HttpRequestMessage request,
        string operation,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Central PMS statutory-discount {Operation} request timed out. CorrelationId {CorrelationId}",
                operation,
                correlationId);
            return null;
        }
        catch (HttpRequestException)
        {
            _logger.LogWarning(
                "Central PMS statutory-discount {Operation} request failed before a response was received. CorrelationId {CorrelationId}",
                operation,
                correlationId);
            return null;
        }
    }

    private static CentralPmsWebPayError BuildTransientStatutoryDiscountError(Guid correlationId) =>
        new(
            503,
            "CENTRAL_PMS_UNAVAILABLE",
            "We could not process the parking-privilege request right now. Please try again.",
            true,
            correlationId);

    private static string ResolveStatutoryDiscountSubmitFallbackCode(int statusCode, bool applyPayableBasis)
    {
        if (statusCode == 409)
        {
            return applyPayableBasis
                ? "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_SEMANTIC_CONFLICT"
                : "STATUTORY_DISCOUNT_DECISION_SEMANTIC_CONFLICT";
        }

        return applyPayableBasis
            ? "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_FAILED"
            : "STATUTORY_DISCOUNT_DECISION_SUBMIT_FAILED";
    }

    private CentralPmsWebPayError ReadError(int statusCode, string responseBody, string fallbackCode)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return new CentralPmsWebPayError(
                statusCode,
                fallbackCode,
                "Central PMS request failed.",
                statusCode >= 500);
        }

        try
        {
            var error = JsonSerializer.Deserialize<ErrorResponse>(responseBody, JsonOptions);
            if (HasStructuredError(error))
            {
                return new CentralPmsWebPayError(
                    statusCode,
                    string.IsNullOrWhiteSpace(error?.ErrorCode) ? fallbackCode : error.ErrorCode,
                    string.IsNullOrWhiteSpace(error?.Message) ? "Central PMS request failed." : error.Message,
                    error?.Retryable ?? statusCode >= 500,
                    error?.CorrelationId,
                    ExtractPaymentAttemptId(error?.Details));
            }

            using var document = JsonDocument.Parse(responseBody);
            var message = ExtractProblemMessage(document.RootElement);
            return new CentralPmsWebPayError(
                statusCode,
                fallbackCode,
                string.IsNullOrWhiteSpace(message) ? "Central PMS request failed." : message,
                statusCode >= 500);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Central PMS error response could not be parsed.");
            return new CentralPmsWebPayError(
                statusCode,
                fallbackCode,
                "Central PMS returned an unparseable error response.",
                statusCode >= 500);
        }
    }

    private static bool HasStructuredError(ErrorResponse? error)
    {
        return error is not null &&
            (!string.IsNullOrWhiteSpace(error.ErrorCode) ||
             !string.IsNullOrWhiteSpace(error.Message) ||
             error.Retryable.HasValue);
    }

    private static string? ExtractProblemMessage(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var propertyName in new[] { "message", "detail", "title", "error" })
        {
            if (root.TryGetProperty(propertyName, out var property) &&
                property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }
        }

        return null;
    }

    private static CentralPmsStatutoryDiscountDecision ToStatutoryDecision(StatutoryDiscountDecisionResponse payload) =>
        new(
            payload.StatutoryDiscountDecisionCommandId,
            payload.RequestReference,
            payload.StatutoryDiscountValidationId,
            payload.ParkingSessionId,
            payload.SourceChannel,
            payload.EntitlementType,
            payload.DecisionStatus,
            payload.PolicyResolutionBasis,
            payload.AppliedPolicyReferenceId,
            payload.FallbackPolicyReferenceId,
            payload.LocalOrdinanceApplied,
            payload.GrossAmountMinorUnits,
            payload.VatExclusiveBasisAmountMinorUnits,
            payload.VatAmountMinorUnits,
            payload.VatTreatment,
            payload.StatutoryDiscountAmountMinorUnits,
            payload.NetPayableAmountMinorUnits,
            payload.Currency,
            payload.EvidenceRequired,
            payload.EvidenceRecorded,
            payload.ReasonCode,
            payload.ErrorCode,
            payload.CorrelationId,
            payload.CreatedAt,
            payload.DecidedAt,
            payload.AppliedAt,
            payload.OriginalTariffSnapshotId,
            payload.AppliedTariffSnapshotId,
            payload.CommandStatus,
            payload.ClientResultStatus,
            payload.ResultClassification,
            payload.SemanticHashSourceVersion,
            payload.Retryable,
            payload.RecoveryClassification,
            payload.RecoveryAction,
            payload.SafeErrorCode,
            payload.DecisionCommandStatus,
            payload.DecisionResultStatus,
            payload.DecisionRetryable,
            payload.DecisionRecoveryClassification,
            payload.DecisionRecoveryAction,
            payload.StatutoryDiscountPayableBasisApplicationCommandId,
            payload.ApplicationRequested,
            payload.ApplicationCommandStatus,
            payload.ApplicationResultClassification,
            payload.ApplicationSemanticHashSourceVersion,
            payload.ApplicationRetryable,
            payload.ApplicationRecoveryClassification,
            payload.ApplicationRecoveryAction,
            payload.OverallResultClassification,
            payload.OneShotComplete,
            payload.SiteId,
            payload.SiteGroupId,
            payload.PayableBasisReady,
            payload.PayableBasisReadinessStatus,
            payload.PayableBasisReadinessAction);

    private static CentralPmsStatutoryDiscountAvailability ToStatutoryAvailability(StatutoryDiscountAvailabilityResponse payload) =>
        new(
            payload.RequestReference,
            payload.ParkingSessionId,
            payload.SiteId,
            payload.SiteGroupId,
            payload.AvailabilityStatus,
            payload.StatutoryParkingBenefitAvailable,
            payload.CoveredEntitlementTypes ?? Array.Empty<string>(),
            payload.RequestedEntitlementType,
            payload.SafeReasonCode,
            payload.Retryable,
            payload.RemediationAction,
            payload.RequiredEvidenceTypes?.Select(static requirement =>
                    new CentralPmsStatutoryDiscountAvailabilityEvidenceRequirement(
                        requirement.EvidenceType,
                        requirement.RequirementStatus,
                        requirement.SafeRequirementLabel,
                        requirement.SafeRequirementNotes))
                .ToArray() ?? Array.Empty<CentralPmsStatutoryDiscountAvailabilityEvidenceRequirement>(),
            payload.CorrelationId);

    private static CentralPmsStatutoryDiscountPendingLifecycleRediscovery ToPendingLifecycleRediscovery(
        StatutoryDiscountPendingLifecycleRediscoveryResponse payload) =>
        new(
            payload.Classification,
            payload.StatutoryDecisionId,
            payload.StatutoryDecisionCommandId,
            payload.RequestReference,
            payload.EntitlementType,
            payload.DecisionStatus,
            payload.PayableBasisStatus,
            payload.ParkingSessionId,
            payload.SiteId,
            payload.SiteGroupId,
            payload.OpaqueContinuationReference,
            payload.OpaqueContinuationUrl,
            payload.LifecycleState,
            payload.Retryable,
            payload.CorrelationId,
            payload.CreatedAt,
            payload.UpdatedAt,
            payload.SubmittedAt,
            payload.DecidedAt,
            payload.ReviewedAt);

    private static Guid? ExtractPaymentAttemptId(JsonElement? details)
    {
        if (details is not { ValueKind: JsonValueKind.Object } detailsObject)
        {
            return null;
        }

        foreach (var propertyName in new[] { "payment_attempt_id", "paymentAttemptId" })
        {
            if (detailsObject.TryGetProperty(propertyName, out var property) &&
                property.ValueKind == JsonValueKind.String &&
                Guid.TryParse(property.GetString(), out var paymentAttemptId))
            {
                return paymentAttemptId;
            }
        }

        return null;
    }

    private static Guid? ParseGuid(string? value)
    {
        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string ResolveVendorSystemId(string? responseVendorSystemId, string requestedVendorSystemId)
    {
        return string.IsNullOrWhiteSpace(responseVendorSystemId)
            ? requestedVendorSystemId
            : responseVendorSystemId.Trim();
    }

    private sealed record VendorParkingResolveRequest(
        string SiteGroupId,
        string SiteId,
        string VendorSystemId,
        string? PlateNumber,
        string? TicketReference,
        Guid CorrelationId);

    private sealed record VendorParkingResolveResponse(
        Guid ParkingSessionId,
        Guid TariffSnapshotId,
        string? SiteGroupId,
        string? SiteId,
        string LookupOutcome,
        string? PlateNumber,
        string? TicketReference,
        long NetPayableMinorUnits,
        string Currency,
        DateTimeOffset TariffExpiresAt,
        DateTimeOffset? FeeValidUntil,
        string? VendorSystemId,
        Guid CorrelationId,
        string? SiteGroupName,
        string? SiteName,
        DateTimeOffset? EntryTime,
        DateTimeOffset? CurrentFeeCalculationTime,
        string? TariffName,
        string? ParkingStatus,
        string? PaymentStatus);

    private sealed record CreatePaymentAttemptRequest(
        Guid ParkingSessionId,
        Guid TariffSnapshotId,
        string PaymentProvider,
        string PaymentMethod);

    private sealed record CreatePaymentAttemptResponse(
        Guid PaymentAttemptId,
        string AttemptStatus,
        string PaymentProvider,
        bool WasReused);

    private sealed record FinalizePaymentAttemptRequest(
        string FinalAttemptStatus,
        string RequestedBy);

    private sealed record FinalizePaymentAttemptResponse(
        Guid PaymentAttemptId,
        string AttemptStatus);

    private sealed record WebPayReceiptPresentationResponse(
        Guid PaymentAttemptId,
        Guid PaymentConfirmationId,
        Guid FiscalIssuanceReferenceId,
        string FiscalIssuanceState,
        Guid PosFiscalDocumentId,
        string? FiscalDocumentNumber,
        string? FiscalDocumentStatus,
        string ReceiptAvailabilityState,
        string? PresentationVersion,
        string? TemplateVersion,
        string? ContentType,
        JsonElement AuthoritativePresentation,
        string? VoidStatus,
        string? VoidReasonCode,
        DateTimeOffset? VoidedAt,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        Guid CorrelationId);

    private sealed record StatutoryDiscountDecisionRequest(
        Guid RequestReference,
        string SourceChannel,
        Guid ParkingSessionId,
        Guid? SiteId,
        Guid? SiteGroupId,
        string? TicketReference,
        string? PlateNumber,
        string EntitlementType,
        string IdDocumentType,
        string IssuingAuthority,
        DateOnly? ExpiryDate,
        string MaskedIdReference,
        bool EvidenceCaptureRequested,
        IReadOnlyList<StatutoryDiscountEvidenceReferenceRequest>? EvidenceReferences,
        Guid? ActorUserId,
        Guid? OperatorDeviceBindingId,
        Guid? OperatorShiftId,
        bool RequesterAttestation,
        string? AttestationNotes,
        string? ReasonCode,
        string? Decision,
        string? DecisionReasonCode,
        Guid? ReviewerUserId,
        bool? ReviewerAttestation,
        bool ApplyPayableBasis,
        Guid? OriginalTariffSnapshotId);

    private sealed record StatutoryDiscountEvidenceReferenceRequest(
        string EvidenceType,
        string CaptureMethod,
        string? FileName,
        string? ContentType,
        long? SizeBytes,
        string? StorageReference,
        string? ReferenceNumberMasked,
        string? VerificationStatus);

    private sealed record StatutoryDiscountAvailabilityRequest(
        Guid RequestReference,
        Guid ParkingSessionId,
        string? RequestedEntitlementType,
        bool? BeneficiaryResidencySatisfied);

    private sealed record StatutoryDiscountAvailabilityResponse(
        Guid RequestReference,
        Guid ParkingSessionId,
        Guid? SiteId,
        Guid? SiteGroupId,
        Guid? JurisdictionId,
        string? JurisdictionCode,
        string? JurisdictionDisplayName,
        string AvailabilityStatus,
        bool StatutoryParkingBenefitAvailable,
        IReadOnlyList<string>? CoveredEntitlementTypes,
        string? RequestedEntitlementType,
        Guid? PolicyVersionId,
        string? PolicyCode,
        string? PolicyVersion,
        string? OrdinanceNumber,
        string? OrdinanceTitle,
        string? PolicyDisplayName,
        string? VerificationStatus,
        string? PublicationStatus,
        DateTimeOffset? EffectiveFrom,
        DateTimeOffset? EffectiveTo,
        string? ResidencyRequirement,
        IReadOnlyList<StatutoryDiscountAvailabilityEvidenceRequirementResponse>? RequiredEvidenceTypes,
        string? ParkingServiceApplicability,
        string? BenefitEffectClassification,
        string? BenefitEffectSupportStatus,
        bool? OfficialSourceAvailable,
        bool? OrdinanceTextAvailable,
        bool? OrdinanceNumberAvailable,
        string? SafeReasonCode,
        bool Retryable,
        string RemediationAction,
        Guid CorrelationId);

    private sealed record StatutoryDiscountAvailabilityEvidenceRequirementResponse(
        string EvidenceType,
        string RequirementStatus,
        string SafeRequirementLabel,
        string? SafeRequirementNotes);

    private sealed record StatutoryDiscountPendingLifecycleRediscoveryRequest(
        string LookupMode,
        Guid? ParkingSessionId,
        Guid SiteId,
        Guid SiteGroupId,
        string? TicketReference,
        string? PlateNumber,
        string? VendorSystemId,
        string? EntitlementType);

    private sealed record StatutoryDiscountPendingLifecycleRediscoveryResponse(
        string Classification,
        Guid? StatutoryDecisionId,
        Guid? StatutoryDecisionCommandId,
        Guid? RequestReference,
        string? EntitlementType,
        string? DecisionStatus,
        string? PayableBasisStatus,
        Guid? ParkingSessionId,
        Guid? SiteId,
        Guid? SiteGroupId,
        string? OpaqueContinuationReference,
        string? OpaqueContinuationUrl,
        string LifecycleState,
        bool Retryable,
        Guid CorrelationId,
        DateTimeOffset? CreatedAt,
        DateTimeOffset? UpdatedAt,
        DateTimeOffset? SubmittedAt,
        DateTimeOffset? DecidedAt,
        DateTimeOffset? ReviewedAt);

    private sealed record StatutoryDiscountDecisionResponse(
        Guid StatutoryDiscountDecisionCommandId,
        Guid RequestReference,
        Guid? StatutoryDiscountValidationId,
        Guid ParkingSessionId,
        string SourceChannel,
        string EntitlementType,
        string DecisionStatus,
        string? PolicyResolutionBasis,
        Guid? AppliedPolicyReferenceId,
        Guid? FallbackPolicyReferenceId,
        bool LocalOrdinanceApplied,
        long? GrossAmountMinorUnits,
        long? StatutoryDiscountAmountMinorUnits,
        long? NetPayableAmountMinorUnits,
        string? Currency,
        bool EvidenceRequired,
        bool EvidenceRecorded,
        string? ReasonCode,
        string? ErrorCode,
        Guid CorrelationId,
        DateTimeOffset CreatedAt,
        DateTimeOffset? DecidedAt,
        DateTimeOffset? AppliedAt,
        Guid? OriginalTariffSnapshotId,
        Guid? AppliedTariffSnapshotId,
        string CommandStatus,
        string ClientResultStatus,
        string ResultClassification,
        string SemanticHashSourceVersion,
        bool Retryable,
        string RecoveryClassification,
        string? RecoveryAction,
        string? SafeErrorCode,
        string DecisionCommandStatus = "COMPLETED",
        string? DecisionResultStatus = null,
        bool DecisionRetryable = false,
        string DecisionRecoveryClassification = "NONE",
        string? DecisionRecoveryAction = null,
        Guid? StatutoryDiscountPayableBasisApplicationCommandId = null,
        bool ApplicationRequested = false,
        string ApplicationCommandStatus = "NOT_REQUESTED",
        string ApplicationResultClassification = "NOT_REQUESTED",
        string? ApplicationSemanticHashSourceVersion = null,
        bool ApplicationRetryable = false,
        string ApplicationRecoveryClassification = "NONE",
        string? ApplicationRecoveryAction = null,
        string OverallResultClassification = "ACCEPTED",
        bool OneShotComplete = true,
        Guid? SiteId = null,
        Guid? SiteGroupId = null,
        long? VatExclusiveBasisAmountMinorUnits = null,
        long? VatAmountMinorUnits = null,
        string? VatTreatment = null,
        bool PayableBasisReady = false,
        string PayableBasisReadinessStatus = "NOT_READY",
        string? PayableBasisReadinessAction = null);

    private sealed record StatutoryEvidenceBootstrapRequest(
        Guid StatutoryDiscountDecisionCommandId,
        string? ClientOperationKey);

    private sealed record StatutoryEvidenceUploadSessionRequest(
        Guid EvidenceSetReference,
        Guid EvidenceItemReference,
        string DeclaredContentType,
        long DeclaredContentLength,
        string DeclaredChecksumSha256,
        string? ClientOperationKey);

    private sealed record StatutoryEvidenceFinalizeRequest(string? ClientOperationKey);

    private sealed record StatutoryEvidenceChannelResponse(
        string Classification,
        bool Retryable,
        string? ErrorCode,
        Guid CorrelationId,
        string SourceChannel,
        bool EvidenceRequired,
        Guid? EvidenceSetReference,
        Guid? EvidenceItemReference,
        IReadOnlyList<string>? AllowedContentTypes,
        long MaximumContentLengthBytes,
        int? MaximumImageWidth,
        int? MaximumImageHeight,
        long? MaximumImagePixelCount,
        string? RequiredDocumentType,
        string? RequiredItemRole,
        string? LifecycleClassification,
        string ReplacementPosture,
        bool ReadyForReview,
        bool ReadyForAptPreCash,
        string? BlockingReasonCode,
        DateTimeOffset EvaluatedAt);

    private sealed record StatutoryEvidenceUploadSessionResponse(
        string Classification,
        bool Retryable,
        string? ErrorCode,
        Guid CorrelationId,
        Guid? OpaqueUploadSessionReference,
        string Method,
        DateTimeOffset? ExpiresAt,
        string AcceptedContentType,
        long MaximumContentLengthBytes);

    private sealed record ErrorResponse(
        string? ErrorCode,
        string? Message,
        Guid? CorrelationId,
        bool? Retryable,
        JsonElement? Details);
}
