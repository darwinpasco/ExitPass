using System.Globalization;
using System.Text.Json;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;
using ExitPass.PaymentOrchestrator.Contracts.Payments;
using ExitPass.PaymentOrchestrator.Contracts.Providers;
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
/// </summary>
public sealed class PayMongoCheckoutAdapter : IPaymentProviderAdapter
{
    private readonly PayMongoClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="PayMongoCheckoutAdapter"/> class.
    /// </summary>
    /// <param name="client">The raw PayMongo client.</param>
    public PayMongoCheckoutAdapter(PayMongoClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
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

    private static CanonicalPaymentOutcomeStatus MapWebhookEventTypeToCanonicalStatus(string eventType)
    {
        return eventType switch
        {
            "checkout_session.payment.paid" => CanonicalPaymentOutcomeStatus.Succeeded,
            "payment.paid" => CanonicalPaymentOutcomeStatus.Succeeded,
            "payment.failed" => CanonicalPaymentOutcomeStatus.Failed,
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
}
