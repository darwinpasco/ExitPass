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

        var handoffType = string.IsNullOrWhiteSpace(providerResponse.CashierUrl)
            ? ProviderHandoffType.None
            : ProviderHandoffType.Redirect;

        var handoff = new ProviderHandoffDto(
            handoffType,
            providerResponse.CashierUrl,
            string.IsNullOrWhiteSpace(providerResponse.CashierUrl) ? null : "GET",
            null,
            null,
            null,
            providerResponse.ExpiresAtUtc);

        return new CreateProviderPaymentSessionResult(
            providerResponse.OrderId,
            providerResponse.OrderId,
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

            if (!TryGetRequiredString(root, "code", out var code))
            {
                rejectionCode = "AUB_WEBHOOK_MISSING_CODE";
                return false;
            }

            if (!string.Equals(code, "00", StringComparison.OrdinalIgnoreCase))
            {
                rejectionCode = $"AUB_WEBHOOK_CODE_{code}";
                return false;
            }

            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                rejectionCode = "AUB_WEBHOOK_MISSING_DATA";
                return false;
            }

            if (!data.TryGetProperty("orderInformation", out var orderInformation) ||
                orderInformation.ValueKind != JsonValueKind.Object)
            {
                rejectionCode = "AUB_WEBHOOK_MISSING_ORDER_INFORMATION";
                return false;
            }

            if (!TryGetRequiredString(orderInformation, "orderId", out var orderId) ||
                !Guid.TryParse(orderId, out var paymentAttemptId) ||
                paymentAttemptId == Guid.Empty)
            {
                rejectionCode = "AUB_WEBHOOK_MISSING_PAYMENT_ATTEMPT_ID";
                return false;
            }

            if (!TryGetRequiredString(orderInformation, "transactionResult", out var providerStatus))
            {
                rejectionCode = "AUB_WEBHOOK_MISSING_TRANSACTION_RESULT";
                return false;
            }

            var providerReference = TryGetOptionalString(orderInformation, "referencedId", out var referencedId)
                ? referencedId
                : orderId;
            var occurredAtUtc = DateTimeOffset.UtcNow;
            if (orderInformation.TryGetProperty("responseDate", out var occurredAtProperty))
            {
                occurredAtUtc = ParseDateTimeOffset(occurredAtProperty);
            }

            var amountMinor = 0L;
            if (orderInformation.TryGetProperty("amount", out var amountProperty) &&
                amountProperty.ValueKind == JsonValueKind.Number &&
                amountProperty.TryGetInt64(out var parsedAmountMinor))
            {
                amountMinor = parsedAmountMinor;
            }

            var currency = TryGetOptionalString(orderInformation, "currency", out var parsedCurrency)
                ? parsedCurrency
                : "PHP";
            var eventId = providerReference;
            var eventType = "aub.card_cashier.notification";
            var rawAttributes = CreateRawAttributes(code, providerStatus, eventType, providerReference, orderInformation, data);

            webhookEvent = new AubWebhookEvent(
                eventId,
                eventType,
                paymentAttemptId,
                providerReference,
                orderId,
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

    private static CanonicalPaymentOutcomeStatus MapProviderStatus(string providerStatus)
    {
        return providerStatus.Trim().ToUpperInvariant() switch
        {
            "ACCEPTED" => CanonicalPaymentOutcomeStatus.PendingProvider,
            "PENDING" => CanonicalPaymentOutcomeStatus.PendingProvider,
            "AWAITING_CUSTOMER_ACTION" => CanonicalPaymentOutcomeStatus.AwaitingCustomerAction,
            "SUCCESS" => CanonicalPaymentOutcomeStatus.Succeeded,
            "FAILED" => CanonicalPaymentOutcomeStatus.Failed,
            "DECLINED" => CanonicalPaymentOutcomeStatus.Failed,
            "CANCELLED" => CanonicalPaymentOutcomeStatus.Cancelled,
            "CANCELED" => CanonicalPaymentOutcomeStatus.Cancelled,
            "EXPIRED" => CanonicalPaymentOutcomeStatus.Expired,
            _ => CanonicalPaymentOutcomeStatus.PendingProvider
        };
    }

    private static Dictionary<string, string> CreateRawAttributes(
        string code,
        string providerStatus,
        string eventType,
        string providerReference,
        JsonElement orderInformation,
        JsonElement data)
    {
        var rawAttributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["provider_code"] = code,
            ["provider_status"] = providerStatus,
            ["event_type"] = eventType,
            ["provider_event_id"] = providerReference
        };

        if (TryGetOptionalString(orderInformation, "paymentType", out var paymentType))
        {
            rawAttributes["payment_type"] = paymentType;
        }

        if (TryGetOptionalString(orderInformation, "paymentBrand", out var orderPaymentBrand))
        {
            rawAttributes["payment_brand"] = orderPaymentBrand;
        }

        if (TryGetOptionalString(orderInformation, "attach", out var attach))
        {
            rawAttributes["attach"] = attach;
            foreach (var pair in ParseAttachAttributes(attach))
            {
                rawAttributes[pair.Key] = pair.Value;
            }
        }

        if (data.TryGetProperty("card", out var card) && card.ValueKind == JsonValueKind.Object)
        {
            if (TryGetOptionalString(card, "paymentBrand", out var cardPaymentBrand))
            {
                rawAttributes["card_payment_brand"] = cardPaymentBrand;
            }

            if (TryGetOptionalString(card, "cardBin", out var cardBin))
            {
                rawAttributes["card_bin"] = cardBin;
            }

            if (TryGetOptionalString(card, "last4Digits", out var last4Digits))
            {
                rawAttributes["last4_digits"] = last4Digits;
            }
        }

        return rawAttributes;
    }

    private static IEnumerable<KeyValuePair<string, string>> ParseAttachAttributes(string attach)
    {
        foreach (var segment in attach.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = segment.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex <= 0 || separatorIndex == segment.Length - 1)
            {
                continue;
            }

            var key = segment[..separatorIndex].Trim();
            var value = segment[(separatorIndex + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                yield return new KeyValuePair<string, string>(key, value);
            }
        }
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
