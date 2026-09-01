using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;
using ExitPass.PaymentOrchestrator.Contracts.Payments;
using ExitPass.PaymentOrchestrator.Contracts.Providers;
using Microsoft.Extensions.Options;
using ProviderCodeConstants = ExitPass.PaymentOrchestrator.Contracts.Providers.ProviderCode;
using ProviderProductCodeConstants = ExitPass.PaymentOrchestrator.Contracts.Providers.ProviderProductCode;

namespace ExitPass.PaymentOrchestrator.Infrastructure.Providers.PayMongo;

/// <summary>
/// PayMongo Checkout Session adapter for the ExitPass MVP slice.
///
/// BRD:
/// - 12 Payment Orchestration
///
/// SDD:
/// - 10.5.1 Initiate Provider Payment
/// - 10.5.2 Payment Provider Webhook
///
/// Invariants Enforced:
/// - PayMongo-specific API behavior remains behind the adapter boundary.
/// - Provider results are normalized before entering platform control logic.
/// - Malformed provider webhooks must fail closed instead of causing unhandled exceptions.
/// - PayMongo webhooks must pass signature verification before they are treated as authentic.
/// - Checkout-session rails must preserve event-type distinctions so non-authoritative events can be safely ignored upstream.
/// </summary>
public sealed class PayMongoCheckoutAdapter : IPaymentProviderAdapter, IProviderStatusQueryAdapter
{
    private readonly PayMongoClient _client;
    private readonly PayMongoOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="PayMongoCheckoutAdapter"/> class.
    /// </summary>
    /// <param name="client">The raw PayMongo client.</param>
    /// <param name="options">The bound PayMongo options.</param>
    public PayMongoCheckoutAdapter(
        PayMongoClient client,
        IOptions<PayMongoOptions> options)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public string ProviderCode => ProviderCodeConstants.PayMongo;

    /// <inheritdoc />
    public string ProviderProduct => ProviderProductCodeConstants.PayMongoCheckoutSession;

    /// <inheritdoc />
    public async Task<CreateProviderPaymentSessionResult> CreatePaymentSessionAsync(
        CreateProviderPaymentSessionCommand command,
        CancellationToken cancellationToken)
    {
        var providerResponse = await _client.CreateCheckoutSessionAsync(command, cancellationToken);

        var handoff = new ProviderHandoffDto(
            ProviderHandoffType.Redirect,
            providerResponse.CheckoutUrl,
            "GET",
            null,
            null,
            null,
            providerResponse.ExpiresAtUtc);

        return new CreateProviderPaymentSessionResult(
            providerResponse.CheckoutSessionId,
            providerResponse.CheckoutSessionId,
            "PENDING_PROVIDER",
            handoff,
            providerResponse.ExpiresAtUtc,
            providerResponse.RawJson);
    }

    /// <summary>
    /// Queries PayMongo checkout-session status and maps it to provider-neutral evidence.
    /// </summary>
    /// <param name="command">The scoped provider status-query command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Provider-neutral status-query evidence. This is not platform payment finality.</returns>
    public async Task<ProviderStatusQueryResult> QueryProviderSessionStatusAsync(
        ProviderStatusQueryCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.ProviderSessionId))
        {
            return CreateStatusQueryFailure(
                command.ProviderSessionId,
                command.ProviderReference,
                null,
                CanonicalPaymentOutcomeStatus.PendingProvider,
                retryable: false,
                "PAYMONGO_STATUS_QUERY_MISSING_PROVIDER_SESSION",
                "Provider session id is required.",
                command.CorrelationId);
        }

        PayMongoCheckoutSessionStatusResponse response;
        try
        {
            response = await _client.RetrieveCheckoutSessionStatusAsync(
                command.ProviderSessionId,
                cancellationToken);
        }
        catch (PayMongoProviderApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return CreateStatusQueryFailure(
                command.ProviderSessionId,
                command.ProviderReference,
                null,
                CanonicalPaymentOutcomeStatus.PendingProvider,
                retryable: false,
                "PAYMONGO_STATUS_QUERY_PROVIDER_SESSION_NOT_FOUND",
                "PayMongo checkout session was not found.",
                command.CorrelationId,
                new Dictionary<string, string>
                {
                    ["http_status_code"] = ((int)ex.StatusCode).ToString(CultureInfo.InvariantCulture),
                    ["provider_reason_code"] = ex.ReasonCode
                });
        }
        catch (PayMongoProviderApiException ex) when ((int)ex.StatusCode >= 500)
        {
            return CreateStatusQueryFailure(
                command.ProviderSessionId,
                command.ProviderReference,
                null,
                CanonicalPaymentOutcomeStatus.PendingProvider,
                retryable: true,
                "PAYMONGO_STATUS_QUERY_PROVIDER_UNAVAILABLE",
                "PayMongo status query failed with a retryable provider error.",
                command.CorrelationId,
                new Dictionary<string, string>
                {
                    ["http_status_code"] = ((int)ex.StatusCode).ToString(CultureInfo.InvariantCulture),
                    ["provider_reason_code"] = ex.ReasonCode
                });
        }
        catch (PayMongoProviderApiException ex)
        {
            return CreateStatusQueryFailure(
                command.ProviderSessionId,
                command.ProviderReference,
                null,
                CanonicalPaymentOutcomeStatus.PendingProvider,
                retryable: false,
                "PAYMONGO_STATUS_QUERY_PROVIDER_REJECTED",
                "PayMongo status query failed with a non-retryable provider response.",
                command.CorrelationId,
                new Dictionary<string, string>
                {
                    ["http_status_code"] = ((int)ex.StatusCode).ToString(CultureInfo.InvariantCulture),
                    ["provider_reason_code"] = ex.ReasonCode
                });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CreateStatusQueryFailure(
                command.ProviderSessionId,
                command.ProviderReference,
                null,
                CanonicalPaymentOutcomeStatus.PendingProvider,
                retryable: true,
                "PAYMONGO_STATUS_QUERY_TIMEOUT",
                "PayMongo status query timed out.",
                command.CorrelationId);
        }
        catch (JsonException)
        {
            return CreateStatusQueryFailure(
                command.ProviderSessionId,
                command.ProviderReference,
                null,
                CanonicalPaymentOutcomeStatus.PendingProvider,
                retryable: false,
                "PAYMONGO_STATUS_QUERY_MALFORMED_RESPONSE",
                "PayMongo status query returned malformed JSON.",
                command.CorrelationId);
        }
        catch (InvalidOperationException)
        {
            return CreateStatusQueryFailure(
                command.ProviderSessionId,
                command.ProviderReference,
                null,
                CanonicalPaymentOutcomeStatus.PendingProvider,
                retryable: false,
                "PAYMONGO_STATUS_QUERY_MALFORMED_RESPONSE",
                "PayMongo status query returned an invalid response shape.",
                command.CorrelationId);
        }

        return MapStatusResponse(command, response);
    }

    /// <inheritdoc />
    public Task<ProviderWebhookVerificationResult> VerifyWebhookAsync(
        ProviderWebhookRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.RawBody))
        {
            return Task.FromResult(CreateRejectedResult("PAYMONGO_WEBHOOK_EMPTY_BODY"));
        }

        if (!TryParseWebhookEvent(request.RawBody, out var normalizedEvent, out var rejectionCode))
        {
            return Task.FromResult(CreateRejectedResult(rejectionCode));
        }

        if (!TryGetHeaderValue(request.Headers, "Paymongo-Signature", out var signatureHeader))
        {
            return Task.FromResult(CreateRejectedResult("PAYMONGO_WEBHOOK_MISSING_SIGNATURE"));
        }

        if (string.IsNullOrWhiteSpace(_options.WebhookSecretKey))
        {
            return Task.FromResult(CreateRejectedResult("PAYMONGO_WEBHOOK_SECRET_NOT_CONFIGURED"));
        }

        if (!TryValidatePayMongoSignature(
                signatureHeader,
                request.RawBody,
                _options.WebhookSecretKey,
                _options.IsLiveMode,
                _options.WebhookSignatureToleranceSeconds,
                out var signatureRejectionCode))
        {
            return Task.FromResult(CreateRejectedResult(signatureRejectionCode));
        }

        var canonicalStatus = MapWebhookEventTypeToCanonicalStatus(normalizedEvent.EventType);
        var isSuccess = canonicalStatus == CanonicalPaymentOutcomeStatus.Succeeded;
        var isTerminal = canonicalStatus is
            CanonicalPaymentOutcomeStatus.Succeeded or
            CanonicalPaymentOutcomeStatus.Failed or
            CanonicalPaymentOutcomeStatus.Expired or
            CanonicalPaymentOutcomeStatus.Cancelled;

        var result = new ProviderWebhookVerificationResult(
            IsAuthentic: true,
            EventId: normalizedEvent.EventId,
            EventType: normalizedEvent.EventType,
            PaymentAttemptId: normalizedEvent.PaymentAttemptId,
            ProviderReference: normalizedEvent.ProviderReference,
            ProviderSessionId: normalizedEvent.ProviderSessionId,
            CanonicalStatus: canonicalStatus,
            OccurredAtUtc: normalizedEvent.OccurredAtUtc,
            AmountMinor: normalizedEvent.AmountMinor,
            Currency: normalizedEvent.Currency,
            IsTerminal: isTerminal,
            IsSuccess: isSuccess,
            RawAttributes: normalizedEvent.RawAttributes);

        return Task.FromResult(result);
    }

    private static ProviderWebhookVerificationResult CreateRejectedResult(string rejectionCode)
    {
        return new ProviderWebhookVerificationResult(
            IsAuthentic: false,
            EventId: rejectionCode,
            EventType: string.Empty,
            PaymentAttemptId: Guid.Empty,
            ProviderReference: string.Empty,
            ProviderSessionId: string.Empty,
            CanonicalStatus: CanonicalPaymentOutcomeStatus.PendingProvider,
            OccurredAtUtc: DateTimeOffset.UtcNow,
            AmountMinor: 0,
            Currency: "PHP",
            IsTerminal: false,
            IsSuccess: false,
            RawAttributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["rejection_code"] = rejectionCode
            });
    }

    private static ProviderStatusQueryResult MapStatusResponse(
        ProviderStatusQueryCommand command,
        PayMongoCheckoutSessionStatusResponse response)
    {
        if (!string.Equals(command.ProviderSessionId.Trim(), response.CheckoutSessionId, StringComparison.Ordinal))
        {
            return CreateStatusQueryFailure(
                response.CheckoutSessionId,
                response.ProviderReference,
                response.SourceStatus,
                CanonicalPaymentOutcomeStatus.PendingProvider,
                retryable: false,
                "PAYMONGO_STATUS_QUERY_PROVIDER_SESSION_MISMATCH",
                "PayMongo checkout session id did not match the requested provider session.",
                command.CorrelationId);
        }

        if (!string.IsNullOrWhiteSpace(command.ProviderReference) &&
            !string.Equals(command.ProviderReference.Trim(), response.ProviderReference, StringComparison.Ordinal))
        {
            return CreateStatusQueryFailure(
                response.CheckoutSessionId,
                response.ProviderReference,
                response.SourceStatus,
                CanonicalPaymentOutcomeStatus.PendingProvider,
                retryable: false,
                "PAYMONGO_STATUS_QUERY_PROVIDER_REFERENCE_MISMATCH",
                "PayMongo provider reference did not match the expected provider reference.",
                command.CorrelationId);
        }

        if (command.ExpectedAmountMinor is not null &&
            response.AmountMinor is not null &&
            command.ExpectedAmountMinor.Value != response.AmountMinor.Value)
        {
            return CreateStatusQueryFailure(
                response.CheckoutSessionId,
                response.ProviderReference,
                response.SourceStatus,
                CanonicalPaymentOutcomeStatus.PendingProvider,
                retryable: false,
                "PAYMONGO_STATUS_QUERY_AMOUNT_MISMATCH",
                "PayMongo amount did not match the expected amount.",
                command.CorrelationId,
                CreateSafeStatusDiagnostics(response));
        }

        if (!string.IsNullOrWhiteSpace(command.ExpectedCurrencyCode) &&
            !string.IsNullOrWhiteSpace(response.CurrencyCode) &&
            !string.Equals(command.ExpectedCurrencyCode.Trim(), response.CurrencyCode.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return CreateStatusQueryFailure(
                response.CheckoutSessionId,
                response.ProviderReference,
                response.SourceStatus,
                CanonicalPaymentOutcomeStatus.PendingProvider,
                retryable: false,
                "PAYMONGO_STATUS_QUERY_CURRENCY_MISMATCH",
                "PayMongo currency did not match the expected currency.",
                command.CorrelationId,
                CreateSafeStatusDiagnostics(response));
        }

        if (string.IsNullOrWhiteSpace(response.SourceStatus))
        {
            return CreateStatusQueryFailure(
                response.CheckoutSessionId,
                response.ProviderReference,
                null,
                CanonicalPaymentOutcomeStatus.PendingProvider,
                retryable: false,
                "PAYMONGO_STATUS_QUERY_MISSING_STATUS",
                "PayMongo status query response did not include a provider status.",
                command.CorrelationId,
                CreateSafeStatusDiagnostics(response));
        }

        var normalized = MapStatusQuerySourceStatus(response.SourceStatus);
        if (normalized.Unknown)
        {
            return CreateStatusQueryFailure(
                response.CheckoutSessionId,
                response.ProviderReference,
                response.SourceStatus,
                CanonicalPaymentOutcomeStatus.PendingProvider,
                retryable: false,
                "PAYMONGO_STATUS_QUERY_UNKNOWN_STATUS",
                "PayMongo status query returned an unknown provider status.",
                command.CorrelationId,
                CreateSafeStatusDiagnostics(response));
        }

        return new ProviderStatusQueryResult(
            ProviderCodeConstants.PayMongo,
            ProviderProductCodeConstants.PayMongoCheckoutSession,
            response.CheckoutSessionId,
            response.ProviderReference,
            response.SourceStatus,
            normalized.Status,
            normalized.IsTerminal,
            normalized.IsSuccess,
            normalized.Retryable,
            normalized.ReportableToCentralPms,
            response.AmountMinor,
            response.CurrencyCode,
            response.ObservedAtUtc,
            command.CorrelationId,
            null,
            null,
            CreateSafeStatusDiagnostics(response));
    }

    private static ProviderStatusQueryResult CreateStatusQueryFailure(
        string? providerSessionId,
        string? providerReference,
        string? sourceStatus,
        CanonicalPaymentOutcomeStatus normalizedStatus,
        bool retryable,
        string errorCode,
        string errorMessage,
        Guid? correlationId,
        IReadOnlyDictionary<string, string>? diagnostics = null)
    {
        return new ProviderStatusQueryResult(
            ProviderCodeConstants.PayMongo,
            ProviderProductCodeConstants.PayMongoCheckoutSession,
            providerSessionId?.Trim() ?? string.Empty,
            providerReference,
            sourceStatus,
            normalizedStatus,
            IsTerminal: false,
            IsSuccess: false,
            Retryable: retryable,
            ReportableToCentralPms: false,
            AmountMinor: null,
            CurrencyCode: null,
            ProviderObservedAtUtc: null,
            CorrelationId: correlationId,
            ErrorCode: errorCode,
            ErrorMessage: errorMessage,
            Diagnostics: diagnostics ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static IReadOnlyDictionary<string, string> CreateSafeStatusDiagnostics(
        PayMongoCheckoutSessionStatusResponse response)
    {
        var diagnostics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["checkout_session_id"] = response.CheckoutSessionId
        };

        if (!string.IsNullOrWhiteSpace(response.ProviderReference))
        {
            diagnostics["provider_reference"] = response.ProviderReference;
        }

        if (!string.IsNullOrWhiteSpace(response.SourceStatus))
        {
            diagnostics["source_status"] = response.SourceStatus;
        }

        return diagnostics;
    }

    private static PayMongoStatusMapping MapStatusQuerySourceStatus(string sourceStatus)
    {
        return sourceStatus.Trim().ToLowerInvariant() switch
        {
            "paid" or "succeeded" or "success" => new PayMongoStatusMapping(
                CanonicalPaymentOutcomeStatus.Succeeded,
                IsTerminal: true,
                IsSuccess: true,
                Retryable: false,
                ReportableToCentralPms: true,
                Unknown: false),

            "failed" or "declined" => new PayMongoStatusMapping(
                CanonicalPaymentOutcomeStatus.Failed,
                IsTerminal: true,
                IsSuccess: false,
                Retryable: false,
                ReportableToCentralPms: false,
                Unknown: false),

            "expired" => new PayMongoStatusMapping(
                CanonicalPaymentOutcomeStatus.Expired,
                IsTerminal: true,
                IsSuccess: false,
                Retryable: false,
                ReportableToCentralPms: false,
                Unknown: false),

            "cancelled" or "canceled" => new PayMongoStatusMapping(
                CanonicalPaymentOutcomeStatus.Cancelled,
                IsTerminal: true,
                IsSuccess: false,
                Retryable: false,
                ReportableToCentralPms: false,
                Unknown: false),

            "pending" or "awaiting_payment" or "processing" or "active" or "unpaid" => new PayMongoStatusMapping(
                CanonicalPaymentOutcomeStatus.PendingProvider,
                IsTerminal: false,
                IsSuccess: false,
                Retryable: true,
                ReportableToCentralPms: false,
                Unknown: false),

            _ => new PayMongoStatusMapping(
                CanonicalPaymentOutcomeStatus.PendingProvider,
                IsTerminal: false,
                IsSuccess: false,
                Retryable: false,
                ReportableToCentralPms: false,
                Unknown: true)
        };
    }

    private static bool TryParseWebhookEvent(
        string rawBody,
        out PayMongoWebhookEvent webhookEvent,
        out string rejectionCode)
    {
        webhookEvent = default!;
        rejectionCode = "PAYMONGO_WEBHOOK_MALFORMED";

        try
        {
            using var document = JsonDocument.Parse(rawBody);

            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                rejectionCode = "PAYMONGO_WEBHOOK_ROOT_NOT_OBJECT";
                return false;
            }

            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                rejectionCode = "PAYMONGO_WEBHOOK_MISSING_DATA";
                return false;
            }

            if (!TryGetRequiredString(data, "id", out var eventId))
            {
                rejectionCode = "PAYMONGO_WEBHOOK_MISSING_EVENT_ID";
                return false;
            }

            if (!data.TryGetProperty("attributes", out var attributes) || attributes.ValueKind != JsonValueKind.Object)
            {
                rejectionCode = "PAYMONGO_WEBHOOK_MISSING_ATTRIBUTES";
                return false;
            }

            if (!TryGetRequiredString(attributes, "type", out var eventType))
            {
                rejectionCode = "PAYMONGO_WEBHOOK_MISSING_EVENT_TYPE";
                return false;
            }

            var occurredAtUtc = DateTimeOffset.UtcNow;
            if (attributes.TryGetProperty("created_at", out var createdAtProperty))
            {
                occurredAtUtc = ParseDateTimeOffset(createdAtProperty);
            }

            string providerReference = string.Empty;
            string providerSessionId = string.Empty;
            long amountMinor = 0L;
            string currency = "PHP";
            Guid paymentAttemptId = Guid.Empty;
            var rawAttributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (attributes.TryGetProperty("data", out var nestedData) &&
                nestedData.ValueKind == JsonValueKind.Object)
            {
                if (TryGetOptionalString(nestedData, "id", out var nestedId))
                {
                    providerReference = nestedId;
                }

                if (nestedData.TryGetProperty("attributes", out var nestedAttributes) &&
                    nestedAttributes.ValueKind == JsonValueKind.Object)
                {
                    if (nestedAttributes.TryGetProperty("amount", out var amountProperty) &&
                        amountProperty.ValueKind == JsonValueKind.Number &&
                        amountProperty.TryGetInt64(out var parsedAmountMinor))
                    {
                        amountMinor = parsedAmountMinor;
                    }

                    if (TryGetOptionalString(nestedAttributes, "currency", out var parsedCurrency))
                    {
                        currency = parsedCurrency;
                    }

                    if (TryGetOptionalString(nestedAttributes, "checkout_session_id", out var checkoutSessionId))
                    {
                        providerSessionId = checkoutSessionId;
                    }

                    if (TryGetOptionalString(nestedData, "type", out var nestedType) &&
                        string.Equals(nestedType, "checkout_session", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(nestedId))
                    {
                        providerSessionId = nestedId;
                    }

                    TryApplyPaymentEvidenceFromPaymentsArray(
                        nestedAttributes,
                        ref providerReference,
                        ref amountMinor,
                        ref currency);

                    TryApplyPaymentEvidenceFromPaymentIntent(
                        nestedAttributes,
                        ref amountMinor,
                        ref currency);

                    if (nestedAttributes.TryGetProperty("metadata", out var metadataProperty) &&
                        metadataProperty.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var property in metadataProperty.EnumerateObject())
                        {
                            rawAttributes[property.Name] = property.Value.ToString();
                        }

                        if (TryGetOptionalString(metadataProperty, "payment_attempt_id", out var paymentAttemptIdText) &&
                            Guid.TryParse(paymentAttemptIdText, out var parsedPaymentAttemptId))
                        {
                            paymentAttemptId = parsedPaymentAttemptId;
                        }
                    }
                }
            }

            if (paymentAttemptId == Guid.Empty)
            {
                rejectionCode = "PAYMONGO_WEBHOOK_MISSING_PAYMENT_ATTEMPT_ID";
                return false;
            }

            if (string.IsNullOrWhiteSpace(providerSessionId))
            {
                providerSessionId = providerReference;
            }

            rawAttributes["event_type"] = eventType;
            rawAttributes["provider_event_id"] = eventId;

            webhookEvent = new PayMongoWebhookEvent(
                eventId,
                eventType,
                paymentAttemptId,
                providerReference,
                providerSessionId,
                occurredAtUtc,
                amountMinor,
                currency,
                rawAttributes);

            rejectionCode = string.Empty;
            return true;
        }
        catch (JsonException)
        {
            rejectionCode = "PAYMONGO_WEBHOOK_INVALID_JSON";
            return false;
        }
    }

    private static void TryApplyPaymentEvidenceFromPaymentsArray(
        JsonElement attributes,
        ref string providerReference,
        ref long amountMinor,
        ref string currency)
    {
        if (!attributes.TryGetProperty("payments", out var payments) ||
            payments.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var payment in payments.EnumerateArray())
        {
            if (payment.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (TryGetOptionalString(payment, "id", out var paymentId))
            {
                providerReference = paymentId;
            }

            if (!payment.TryGetProperty("attributes", out var paymentAttributes) ||
                paymentAttributes.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (amountMinor == 0 &&
                paymentAttributes.TryGetProperty("amount", out var paymentAmount) &&
                paymentAmount.ValueKind == JsonValueKind.Number &&
                paymentAmount.TryGetInt64(out var parsedPaymentAmount))
            {
                amountMinor = parsedPaymentAmount;
            }

            if (TryGetOptionalString(paymentAttributes, "currency", out var paymentCurrency))
            {
                currency = paymentCurrency;
            }

            return;
        }
    }

    private static void TryApplyPaymentEvidenceFromPaymentIntent(
        JsonElement attributes,
        ref long amountMinor,
        ref string currency)
    {
        if (!attributes.TryGetProperty("payment_intent", out var paymentIntent) ||
            paymentIntent.ValueKind != JsonValueKind.Object ||
            !paymentIntent.TryGetProperty("attributes", out var paymentIntentAttributes) ||
            paymentIntentAttributes.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (amountMinor == 0 &&
            paymentIntentAttributes.TryGetProperty("amount", out var paymentIntentAmount) &&
            paymentIntentAmount.ValueKind == JsonValueKind.Number &&
            paymentIntentAmount.TryGetInt64(out var parsedPaymentIntentAmount))
        {
            amountMinor = parsedPaymentIntentAmount;
        }

        if (TryGetOptionalString(paymentIntentAttributes, "currency", out var paymentIntentCurrency))
        {
            currency = paymentIntentCurrency;
        }
    }

    private static bool TryGetHeaderValue(
        IReadOnlyDictionary<string, string> headers,
        string headerName,
        out string value)
    {
        value = string.Empty;

        if (headers.TryGetValue(headerName, out var directValue) && !string.IsNullOrWhiteSpace(directValue))
        {
            value = directValue;
            return true;
        }

        foreach (var pair in headers)
        {
            if (string.Equals(pair.Key, headerName, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(pair.Value))
            {
                value = pair.Value;
                return true;
            }
        }

        return false;
    }

    private static bool TryValidatePayMongoSignature(
        string signatureHeader,
        string rawBody,
        string webhookSecretKey,
        bool isLiveMode,
        int toleranceSeconds,
        out string rejectionCode)
    {
        rejectionCode = "PAYMONGO_WEBHOOK_INVALID_SIGNATURE";
        var parts = ParseSignatureHeader(signatureHeader);
        if (!parts.TryGetValue("t", out var timestamp) || string.IsNullOrWhiteSpace(timestamp))
        {
            rejectionCode = "PAYMONGO_WEBHOOK_MISSING_SIGNATURE_TIMESTAMP";
            return false;
        }

        if (!long.TryParse(timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixTimestamp))
        {
            rejectionCode = "PAYMONGO_WEBHOOK_INVALID_SIGNATURE_TIMESTAMP";
            return false;
        }

        var signedAt = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp);
        var age = (DateTimeOffset.UtcNow - signedAt).Duration();
        if (age > TimeSpan.FromSeconds(toleranceSeconds))
        {
            rejectionCode = "PAYMONGO_WEBHOOK_SIGNATURE_TIMESTAMP_OUTSIDE_WINDOW";
            return false;
        }

        var signatureToCompare = isLiveMode
            ? parts.GetValueOrDefault("li", string.Empty)
            : parts.GetValueOrDefault("te", string.Empty);

        if (string.IsNullOrWhiteSpace(signatureToCompare))
        {
            rejectionCode = isLiveMode
                ? "PAYMONGO_WEBHOOK_MISSING_LIVE_SIGNATURE"
                : "PAYMONGO_WEBHOOK_MISSING_TEST_SIGNATURE";
            return false;
        }

        var signedPayload = $"{timestamp}.{rawBody}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecretKey));
        var computedBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
        var computedSignature = Convert.ToHexString(computedBytes).ToLowerInvariant();

        var isValid = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedSignature),
            Encoding.UTF8.GetBytes(signatureToCompare.Trim().ToLowerInvariant()));
        if (isValid)
        {
            rejectionCode = string.Empty;
        }

        return isValid;
    }

    private static Dictionary<string, string> ParseSignatureHeader(string signatureHeader)
    {
        var parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var segment in signatureHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = segment.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex >= segment.Length - 1)
            {
                continue;
            }

            var key = segment[..separatorIndex].Trim();
            var value = segment[(separatorIndex + 1)..].Trim();
            parts[key] = value;
        }

        return parts;
    }

    private static CanonicalPaymentOutcomeStatus MapWebhookEventTypeToCanonicalStatus(string eventType)
    {
        return eventType switch
        {
            "checkout_session.paid" => CanonicalPaymentOutcomeStatus.Succeeded,
            "checkout_session.payment.paid" => CanonicalPaymentOutcomeStatus.Succeeded,
            "checkout_session.expired" => CanonicalPaymentOutcomeStatus.Expired,
            "checkout_session.cancelled" => CanonicalPaymentOutcomeStatus.Cancelled,
            "checkout_session.payment.failed" => CanonicalPaymentOutcomeStatus.Failed,
            "payment.failed" => CanonicalPaymentOutcomeStatus.Failed,
            "payment.expired" => CanonicalPaymentOutcomeStatus.Expired,
            "payment.cancelled" => CanonicalPaymentOutcomeStatus.Cancelled,
            "payment.canceled" => CanonicalPaymentOutcomeStatus.Cancelled,
            "payment.paid" => CanonicalPaymentOutcomeStatus.Succeeded,
            "payment.succeeded" => CanonicalPaymentOutcomeStatus.Succeeded,
            _ => CanonicalPaymentOutcomeStatus.PendingProvider
        };
    }

    private static DateTimeOffset ParseDateTimeOffset(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String when DateTimeOffset.TryParse(
                element.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsed) => parsed.ToUniversalTime(),

            JsonValueKind.Number when element.TryGetInt64(out var unixSeconds) => DateTimeOffset.FromUnixTimeSeconds(unixSeconds),

            _ => DateTimeOffset.UtcNow
        };
    }

    private static bool TryGetRequiredString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;

        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var parsed = property.GetString();
        if (string.IsNullOrWhiteSpace(parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryGetOptionalString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;

        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private sealed record PayMongoStatusMapping(
        CanonicalPaymentOutcomeStatus Status,
        bool IsTerminal,
        bool IsSuccess,
        bool Retryable,
        bool ReportableToCentralPms,
        bool Unknown);
}
