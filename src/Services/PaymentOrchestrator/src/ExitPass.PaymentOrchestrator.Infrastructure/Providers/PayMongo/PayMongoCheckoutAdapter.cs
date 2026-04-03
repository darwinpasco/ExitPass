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

        var normalizedEvent = ParseWebhookEvent(request.RawBody);

        var canonicalStatus = MapWebhookEventTypeToCanonicalStatus(normalizedEvent.EventType);
        var isSuccess = canonicalStatus == CanonicalPaymentOutcomeStatus.Succeeded;
        var isTerminal = canonicalStatus is
            CanonicalPaymentOutcomeStatus.Succeeded or
            CanonicalPaymentOutcomeStatus.Failed or
            CanonicalPaymentOutcomeStatus.Expired or
            CanonicalPaymentOutcomeStatus.Cancelled;

        var result = new ProviderWebhookVerificationResult(
            true,
            normalizedEvent.EventId,
            normalizedEvent.EventType,
            normalizedEvent.PaymentAttemptId,
            normalizedEvent.ProviderReference,
            normalizedEvent.ProviderSessionId,
            canonicalStatus,
            normalizedEvent.OccurredAtUtc,
            normalizedEvent.AmountMinor,
            normalizedEvent.Currency,
            isTerminal,
            isSuccess,
            normalizedEvent.RawAttributes);

        return Task.FromResult(result);
    }

    private static PayMongoWebhookEvent ParseWebhookEvent(string rawBody)
    {
        using var document = JsonDocument.Parse(rawBody);

        var data = document.RootElement.GetProperty("data");
        var eventId = data.GetProperty("id").GetString() ?? throw new InvalidOperationException("Webhook event id is required.");

        var attributes = data.GetProperty("attributes");
        var eventType = attributes.GetProperty("type").GetString() ?? throw new InvalidOperationException("Webhook event type is required.");

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
            nestedData.ValueKind == JsonValueKind.Object &&
            nestedData.TryGetProperty("attributes", out var nestedAttributes) &&
            nestedAttributes.ValueKind == JsonValueKind.Object)
        {
            if (nestedData.TryGetProperty("id", out var nestedId) && nestedId.ValueKind == JsonValueKind.String)
            {
                providerReference = nestedId.GetString() ?? string.Empty;
            }

            if (nestedAttributes.TryGetProperty("amount", out var amountProperty) && amountProperty.ValueKind == JsonValueKind.Number)
            {
                amountMinor = amountProperty.GetInt64();
            }

            if (nestedAttributes.TryGetProperty("currency", out var currencyProperty) && currencyProperty.ValueKind == JsonValueKind.String)
            {
                currency = currencyProperty.GetString() ?? currency;
            }

            if (nestedAttributes.TryGetProperty("checkout_session_id", out var checkoutSessionProperty) &&
                checkoutSessionProperty.ValueKind == JsonValueKind.String)
            {
                providerSessionId = checkoutSessionProperty.GetString() ?? string.Empty;
            }

            if (nestedAttributes.TryGetProperty("metadata", out var metadataProperty) &&
                metadataProperty.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in metadataProperty.EnumerateObject())
                {
                    rawAttributes[property.Name] = property.Value.ToString();
                }

                if (metadataProperty.TryGetProperty("payment_attempt_id", out var paymentAttemptIdProperty) &&
                    paymentAttemptIdProperty.ValueKind == JsonValueKind.String &&
                    Guid.TryParse(paymentAttemptIdProperty.GetString(), out var parsedPaymentAttemptId))
                {
                    paymentAttemptId = parsedPaymentAttemptId;
                }
            }
        }

        if (paymentAttemptId == Guid.Empty)
        {
            throw new InvalidOperationException("PayMongo webhook did not include a valid payment_attempt_id metadata value.");
        }

        if (string.IsNullOrWhiteSpace(providerSessionId))
        {
            providerSessionId = providerReference;
        }

        rawAttributes["event_type"] = eventType;
        rawAttributes["provider_event_id"] = eventId;

        return new PayMongoWebhookEvent(
            eventId,
            eventType,
            paymentAttemptId,
            providerReference,
            providerSessionId,
            occurredAtUtc,
            amountMinor,
            currency,
            rawAttributes);
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
}
