using System.Globalization;
using System.Text.Json;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;
using ExitPass.PaymentOrchestrator.Contracts.Payments;
using ProviderCodeConstants = ExitPass.PaymentOrchestrator.Contracts.Providers.ProviderCode;
using ProviderProductCodeConstants = ExitPass.PaymentOrchestrator.Contracts.Providers.ProviderProductCode;

namespace ExitPass.PaymentOrchestrator.Infrastructure.Providers.Aub;

/// <summary>
/// AUB payment provider adapter for ExitPass payment orchestration.
/// </summary>
public sealed class AubPaymentAdapter : IPaymentProviderAdapter
{
    private readonly AubClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="AubPaymentAdapter"/> class.
    /// </summary>
    /// <param name="client">The raw AUB client.</param>
    public AubPaymentAdapter(AubClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <inheritdoc />
    public string ProviderCode => ProviderCodeConstants.Aub;

    /// <inheritdoc />
    public string ProviderProduct => ProviderProductCodeConstants.AubCardCashier;

    /// <inheritdoc />
    public async Task<CreateProviderPaymentSessionResult> CreatePaymentSessionAsync(
        CreateProviderPaymentSessionCommand command,
        CancellationToken cancellationToken)
    {
        var providerResponse = await _client.CreatePaymentSessionAsync(command, cancellationToken);

        var handoffType = string.IsNullOrWhiteSpace(providerResponse.RedirectUrl)
            ? ProviderHandoffType.None
            : ProviderHandoffType.Redirect;

        var handoff = new ProviderHandoffDto(
            handoffType,
            providerResponse.RedirectUrl,
            string.IsNullOrWhiteSpace(providerResponse.RedirectUrl) ? null : "GET",
            null,
            null,
            null,
            providerResponse.ExpiresAtUtc);

        return new CreateProviderPaymentSessionResult(
            providerResponse.PaymentSessionId,
            providerResponse.ProviderReference,
            MapSessionStatus(providerResponse.Status),
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
            return Task.FromResult(CreateRejectedResult("AUB_WEBHOOK_EMPTY_BODY"));
        }

        if (!TryParseWebhookEvent(request.RawBody, out var webhookEvent, out var rejectionCode))
        {
            return Task.FromResult(CreateRejectedResult(rejectionCode));
        }

        var isSuccess = webhookEvent.CanonicalStatus == CanonicalPaymentOutcomeStatus.Succeeded;
        var isTerminal = webhookEvent.CanonicalStatus is
            CanonicalPaymentOutcomeStatus.Succeeded or
            CanonicalPaymentOutcomeStatus.Failed or
            CanonicalPaymentOutcomeStatus.Expired or
            CanonicalPaymentOutcomeStatus.Cancelled;

        var result = new ProviderWebhookVerificationResult(
            IsAuthentic: true,
            EventId: webhookEvent.EventId,
            EventType: webhookEvent.EventType,
            PaymentAttemptId: webhookEvent.PaymentAttemptId,
            ProviderReference: webhookEvent.ProviderReference,
            ProviderSessionId: webhookEvent.ProviderSessionId,
            CanonicalStatus: webhookEvent.CanonicalStatus,
            OccurredAtUtc: webhookEvent.OccurredAtUtc,
            AmountMinor: webhookEvent.AmountMinor,
            Currency: webhookEvent.Currency,
            IsTerminal: isTerminal,
            IsSuccess: isSuccess,
            RawAttributes: webhookEvent.RawAttributes);

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
        out AubWebhookEvent webhookEvent,
        out string rejectionCode)
    {
        webhookEvent = default!;
        rejectionCode = "AUB_WEBHOOK_MALFORMED";

        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                rejectionCode = "AUB_WEBHOOK_ROOT_NOT_OBJECT";
                return false;
            }

            if (!TryGetRequiredString(root, "event_id", out var eventId))
            {
                rejectionCode = "AUB_WEBHOOK_MISSING_EVENT_ID";
                return false;
            }

            if (!TryGetRequiredString(root, "event_type", out var eventType))
            {
                rejectionCode = "AUB_WEBHOOK_MISSING_EVENT_TYPE";
                return false;
            }

            if (!TryGetRequiredString(root, "payment_attempt_id", out var paymentAttemptIdText) ||
                !Guid.TryParse(paymentAttemptIdText, out var paymentAttemptId) ||
                paymentAttemptId == Guid.Empty)
            {
                rejectionCode = "AUB_WEBHOOK_MISSING_PAYMENT_ATTEMPT_ID";
                return false;
            }

            if (!TryGetRequiredString(root, "payment_session_id", out var providerSessionId))
            {
                rejectionCode = "AUB_WEBHOOK_MISSING_PAYMENT_SESSION_ID";
                return false;
            }

            if (!TryGetRequiredString(root, "status", out var providerStatus))
            {
                rejectionCode = "AUB_WEBHOOK_MISSING_STATUS";
                return false;
            }

            var providerReference = TryGetOptionalString(root, "reference", out var parsedReference)
                ? parsedReference
                : providerSessionId;

            var occurredAtUtc = DateTimeOffset.UtcNow;
            if (root.TryGetProperty("occurred_at", out var occurredAtProperty))
            {
                occurredAtUtc = ParseDateTimeOffset(occurredAtProperty);
            }

            var amountMinor = 0L;
            if (root.TryGetProperty("amount", out var amountProperty) &&
                amountProperty.ValueKind == JsonValueKind.Number &&
                amountProperty.TryGetInt64(out var parsedAmountMinor))
            {
                amountMinor = parsedAmountMinor;
            }

            var currency = TryGetOptionalString(root, "currency", out var parsedCurrency)
                ? parsedCurrency
                : "PHP";

            var rawAttributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["provider_status"] = providerStatus,
                ["event_type"] = eventType,
                ["provider_event_id"] = eventId
            };

            if (root.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in metadata.EnumerateObject())
                {
                    rawAttributes[property.Name] = property.Value.ToString();
                }
            }

            webhookEvent = new AubWebhookEvent(
                eventId,
                eventType,
                paymentAttemptId,
                providerReference,
                providerSessionId,
                MapProviderStatus(providerStatus),
                occurredAtUtc,
                amountMinor,
                currency,
                rawAttributes);

            rejectionCode = string.Empty;
            return true;
        }
        catch (JsonException)
        {
            rejectionCode = "AUB_WEBHOOK_INVALID_JSON";
            return false;
        }
    }

    private static string MapSessionStatus(string providerStatus)
    {
        return MapProviderStatus(providerStatus) switch
        {
            CanonicalPaymentOutcomeStatus.Succeeded => "SUCCEEDED",
            CanonicalPaymentOutcomeStatus.Failed => "FAILED",
            CanonicalPaymentOutcomeStatus.Cancelled => "CANCELLED",
            CanonicalPaymentOutcomeStatus.Expired => "EXPIRED",
            CanonicalPaymentOutcomeStatus.AwaitingCustomerAction => "AWAITING_CUSTOMER_ACTION",
            _ => "PENDING_PROVIDER"
        };
    }

    private static CanonicalPaymentOutcomeStatus MapProviderStatus(string providerStatus)
    {
        return providerStatus.Trim().ToUpperInvariant() switch
        {
            "ACCEPTED" => CanonicalPaymentOutcomeStatus.PendingProvider,
            "PENDING" => CanonicalPaymentOutcomeStatus.PendingProvider,
            "AWAITING_CUSTOMER_ACTION" => CanonicalPaymentOutcomeStatus.AwaitingCustomerAction,
            "PAID" => CanonicalPaymentOutcomeStatus.Succeeded,
            "SUCCEEDED" => CanonicalPaymentOutcomeStatus.Succeeded,
            "SUCCESS" => CanonicalPaymentOutcomeStatus.Succeeded,
            "FAILED" => CanonicalPaymentOutcomeStatus.Failed,
            "DECLINED" => CanonicalPaymentOutcomeStatus.Failed,
            "CANCELLED" => CanonicalPaymentOutcomeStatus.Cancelled,
            "CANCELED" => CanonicalPaymentOutcomeStatus.Cancelled,
            "EXPIRED" => CanonicalPaymentOutcomeStatus.Expired,
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

        if (!TryGetOptionalString(element, propertyName, out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryGetOptionalString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;

        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }
}
