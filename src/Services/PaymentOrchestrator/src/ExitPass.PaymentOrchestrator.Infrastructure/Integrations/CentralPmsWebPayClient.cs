using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ExitPass.PaymentOrchestrator.Infrastructure.Integrations;

/// <summary>
/// HTTP client for Central PMS APIs composed by the WebPay payment intent flow.
/// </summary>
public sealed class CentralPmsWebPayClient : ICentralPmsWebPayClient
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
    private readonly Uri _statutoryDiscountDecisionsUri;
    private readonly Uri _statutoryDiscountDecisionsBaseUri;
    private readonly bool _statutoryDiscountServiceIdentityConfigured;
    private readonly Guid _statutoryDiscountWebPayServiceIdentityId;

    private const string CentralPmsPermissionsHeaderName = "X-ExitPass-Permissions";
    private const string CentralPmsServiceIdentityIdHeaderName = "X-ExitPass-Service-Identity-Id";
    private const string StatutoryDiscountSubmitWebPayPermission = "statutory-discounts.decision.submit.webpay";
    private const string StatutoryDiscountDecisionReadPermission = "statutory-discounts.decision.read";

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
        _statutoryDiscountDecisionsUri = new Uri(normalizedBaseUrl, "v1/statutory-discounts/decisions");
        _statutoryDiscountDecisionsBaseUri = new Uri(normalizedBaseUrl, "v1/statutory-discounts/decisions/");

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

    private sealed record ErrorResponse(
        string? ErrorCode,
        string? Message,
        Guid? CorrelationId,
        bool? Retryable,
        JsonElement? Details);
}
