using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;
using Microsoft.Extensions.Options;

namespace ExitPass.PaymentOrchestrator.Infrastructure.Providers.PayMongo;

/// <summary>
/// Thin HTTP client for PayMongo provider operations used by the MVP Checkout Session slice.
///
/// BRD:
/// - 12 Payment Orchestration
///
/// SDD:
/// - 10.5.1 Initiate Provider Payment
///
/// Invariants Enforced:
/// - Raw provider HTTP concerns must stay in infrastructure.
/// - Provider credentials must not be hardcoded in application logic.
/// - Provider configuration must be sourced from the bound PayMongo provider options.
/// </summary>
public sealed class PayMongoClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly PayMongoOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="PayMongoClient"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client.</param>
    /// <param name="options">The bound PayMongo provider options.</param>
    /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
    public PayMongoClient(HttpClient httpClient, IOptions<PayMongoOptions> options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Creates a PayMongo Checkout Session.
    /// </summary>
    /// <param name="command">The provider session creation command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The normalized PayMongo Checkout Session response.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when required PayMongo configuration is missing or the provider returns an invalid response.
    /// </exception>
    public async Task<PayMongoCheckoutSessionResponse> CreateCheckoutSessionAsync(
        CreateProviderPaymentSessionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validationErrors = _options.Validate();
        if (validationErrors.Count > 0)
        {
            throw new InvalidOperationException(
                $"PayMongo configuration is invalid: {string.Join(" ", validationErrors)}");
        }

        var requestPayload = BuildCheckoutSessionRequest(command);
        var requestJson = JsonSerializer.Serialize(requestPayload, JsonOptions);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_options.BaseUrl.TrimEnd('/')}/v1/checkout_sessions");

        request.Headers.Authorization = BuildBasicAuthorizationHeader(_options.SecretKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new PayMongoProviderApiException(
                response.StatusCode,
                ResolveProviderFailureReason(responseJson));
        }

        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;

        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("PayMongo response did not contain a valid data object.");
        }

        if (!data.TryGetProperty("attributes", out var attributes) || attributes.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("PayMongo response did not contain a valid attributes object.");
        }

        var checkoutSessionId = data.TryGetProperty("id", out var idProperty) && idProperty.ValueKind == JsonValueKind.String
            ? idProperty.GetString()
            : null;

        var checkoutUrl = attributes.TryGetProperty("checkout_url", out var checkoutUrlProperty) && checkoutUrlProperty.ValueKind == JsonValueKind.String
            ? checkoutUrlProperty.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(checkoutSessionId))
        {
            throw new InvalidOperationException("PayMongo response did not contain a checkout session id.");
        }

        if (string.IsNullOrWhiteSpace(checkoutUrl))
        {
            throw new InvalidOperationException("PayMongo response did not contain a checkout URL.");
        }

        DateTimeOffset? expiresAtUtc = null;
        if (attributes.TryGetProperty("checkout_url_expires_at", out var expiresProperty) &&
            expiresProperty.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(expiresProperty.GetString(), out var parsedExpiry))
        {
            expiresAtUtc = parsedExpiry.ToUniversalTime();
        }

        return new PayMongoCheckoutSessionResponse(
            checkoutSessionId,
            checkoutUrl,
            expiresAtUtc,
            responseJson);
    }

    /// <summary>
    /// Retrieves a PayMongo Checkout Session status by provider session reference.
    /// </summary>
    /// <param name="providerSessionReference">The PayMongo Checkout Session identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The normalized PayMongo Checkout Session status response.</returns>
    /// <exception cref="ArgumentException">Thrown when the provider session reference is blank.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when required PayMongo configuration is missing or the provider returns an invalid response.
    /// </exception>
    public async Task<PayMongoCheckoutSessionStatusResponse> RetrieveCheckoutSessionStatusAsync(
        string providerSessionReference,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(providerSessionReference))
        {
            throw new ArgumentException("Provider session reference is required.", nameof(providerSessionReference));
        }

        var validationErrors = _options.Validate();
        if (validationErrors.Count > 0)
        {
            throw new InvalidOperationException(
                $"PayMongo configuration is invalid: {string.Join(" ", validationErrors)}");
        }

        var trimmedReference = providerSessionReference.Trim();

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_options.BaseUrl.TrimEnd('/')}/v1/checkout_sessions/{Uri.EscapeDataString(trimmedReference)}");

        request.Headers.Authorization = BuildBasicAuthorizationHeader(_options.SecretKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new PayMongoProviderApiException(
                response.StatusCode,
                ResolveProviderFailureReason(responseJson));
        }

        return ParseCheckoutSessionStatusResponse(responseJson);
    }

    /// <summary>
    /// Builds the PayMongo checkout session request payload.
    /// </summary>
    /// <param name="command">The normalized provider session creation command.</param>
    /// <returns>The PayMongo request payload object.</returns>
    private PayMongoCheckoutSessionRequest BuildCheckoutSessionRequest(CreateProviderPaymentSessionCommand command)
    {
        var customerDisplayName = string.IsNullOrWhiteSpace(command.CustomerDisplayName)
            ? "ExitPass Parking Fee"
            : command.CustomerDisplayName.Trim();
        var referenceNumber = BuildCustomerReferenceNumber(command);

        return new PayMongoCheckoutSessionRequest(
            new PayMongoCheckoutSessionData(
                new PayMongoCheckoutSessionAttributes(
                    null,
                    command.CancelUrl,
                    command.Description,
                    _options.AllowedPaymentMethodTypes,
                    new[]
                    {
                        new PayMongoCheckoutSessionLineItem(
                            command.Currency,
                            command.AmountMinor,
                            customerDisplayName,
                            1)
                    },
                    command.Metadata,
                    referenceNumber,
                    false,
                    true,
                    true,
                    command.SuccessUrl)));
    }

    private static string BuildCustomerReferenceNumber(CreateProviderPaymentSessionCommand command)
    {
        if (command.Metadata.TryGetValue("ticket_reference", out var ticketReference) &&
            !string.IsNullOrWhiteSpace(ticketReference))
        {
            return ticketReference.Trim();
        }

        return $"EP-{command.PaymentAttemptId:N}"[..15].ToUpperInvariant();
    }

    private static PayMongoCheckoutSessionStatusResponse ParseCheckoutSessionStatusResponse(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;

        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("PayMongo status response did not contain a valid data object.");
        }

        var checkoutSessionId = data.TryGetProperty("id", out var idProperty) && idProperty.ValueKind == JsonValueKind.String
            ? idProperty.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(checkoutSessionId))
        {
            throw new InvalidOperationException("PayMongo status response did not contain a checkout session id.");
        }

        if (!data.TryGetProperty("attributes", out var attributes) || attributes.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("PayMongo status response did not contain a valid attributes object.");
        }

        var sourceStatus = GetOptionalString(attributes, "status") ??
            GetOptionalString(attributes, "payment_status") ??
            GetFirstPaymentString(attributes, "status");
        var providerReference = GetFirstPaymentId(attributes) ??
            GetOptionalString(attributes, "payment_id") ??
            GetOptionalString(attributes, "provider_reference");
        var amountMinor = GetOptionalInt64(attributes, "amount") ??
            GetFirstPaymentInt64(attributes, "amount");
        var currencyCode = GetOptionalString(attributes, "currency") ??
            GetFirstPaymentString(attributes, "currency");
        var observedAtUtc =
            GetOptionalDateTimeOffset(attributes, "paid_at") ??
            GetOptionalDateTimeOffset(attributes, "completed_at") ??
            GetOptionalDateTimeOffset(attributes, "updated_at") ??
            GetOptionalDateTimeOffset(attributes, "created_at") ??
            GetFirstPaymentDateTimeOffset(attributes, "paid_at") ??
            GetFirstPaymentDateTimeOffset(attributes, "updated_at") ??
            GetFirstPaymentDateTimeOffset(attributes, "created_at");

        return new PayMongoCheckoutSessionStatusResponse(
            checkoutSessionId,
            providerReference,
            sourceStatus,
            amountMinor,
            currencyCode,
            observedAtUtc,
            responseJson);
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(property.GetString())
                ? property.GetString()
                : null;
    }

    private static long? GetOptionalInt64(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt64(out var parsed)
                ? parsed
                : null;
    }

    private static DateTimeOffset? GetOptionalDateTimeOffset(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(property.GetString(), out var parsedString))
        {
            return parsedString.ToUniversalTime();
        }

        if (property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt64(out var unixSeconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }

        return null;
    }

    private static string? GetFirstPaymentId(JsonElement checkoutAttributes)
    {
        if (!TryGetFirstPayment(checkoutAttributes, out var payment))
        {
            return null;
        }

        return GetOptionalString(payment, "id");
    }

    private static string? GetFirstPaymentString(JsonElement checkoutAttributes, string propertyName)
    {
        if (!TryGetFirstPaymentAttributes(checkoutAttributes, out var paymentAttributes))
        {
            return null;
        }

        return GetOptionalString(paymentAttributes, propertyName);
    }

    private static long? GetFirstPaymentInt64(JsonElement checkoutAttributes, string propertyName)
    {
        if (!TryGetFirstPaymentAttributes(checkoutAttributes, out var paymentAttributes))
        {
            return null;
        }

        return GetOptionalInt64(paymentAttributes, propertyName);
    }

    private static DateTimeOffset? GetFirstPaymentDateTimeOffset(JsonElement checkoutAttributes, string propertyName)
    {
        if (!TryGetFirstPaymentAttributes(checkoutAttributes, out var paymentAttributes))
        {
            return null;
        }

        return GetOptionalDateTimeOffset(paymentAttributes, propertyName);
    }

    private static bool TryGetFirstPaymentAttributes(JsonElement checkoutAttributes, out JsonElement paymentAttributes)
    {
        paymentAttributes = default;

        if (!TryGetFirstPayment(checkoutAttributes, out var payment))
        {
            return false;
        }

        return payment.TryGetProperty("attributes", out paymentAttributes) &&
            paymentAttributes.ValueKind == JsonValueKind.Object;
    }

    private static bool TryGetFirstPayment(JsonElement checkoutAttributes, out JsonElement payment)
    {
        payment = default;

        if (!checkoutAttributes.TryGetProperty("payments", out var payments) ||
            payments.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var candidate in payments.EnumerateArray())
        {
            if (candidate.ValueKind == JsonValueKind.Object)
            {
                payment = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Builds the HTTP Basic authorization header required by PayMongo.
    /// </summary>
    /// <param name="secretKey">The PayMongo secret key.</param>
    /// <returns>The Basic authorization header.</returns>
    private static AuthenticationHeaderValue BuildBasicAuthorizationHeader(string secretKey)
    {
        var raw = $"{secretKey}:";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        return new AuthenticationHeaderValue("Basic", encoded);
    }

    private static string ResolveProviderFailureReason(string responseJson)
    {
        if (string.IsNullOrWhiteSpace(responseJson))
        {
            return "PAYMONGO_HTTP_ERROR";
        }

        try
        {
            using var document = JsonDocument.Parse(responseJson);
            if (document.RootElement.TryGetProperty("errors", out var errors) &&
                errors.ValueKind == JsonValueKind.Array)
            {
                foreach (var error in errors.EnumerateArray())
                {
                    if (error.ValueKind == JsonValueKind.Object &&
                        error.TryGetProperty("code", out var code) &&
                        code.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(code.GetString()))
                    {
                        return code.GetString()!;
                    }
                }
            }
        }
        catch (JsonException)
        {
            return "PAYMONGO_INVALID_ERROR_RESPONSE";
        }

        return "PAYMONGO_HTTP_ERROR";
    }

    private sealed record PayMongoCheckoutSessionRequest(
        [property: JsonPropertyName("data")] PayMongoCheckoutSessionData Data);

    private sealed record PayMongoCheckoutSessionData(
        [property: JsonPropertyName("attributes")] PayMongoCheckoutSessionAttributes Attributes);

    private sealed record PayMongoCheckoutSessionAttributes(
        [property: JsonPropertyName("billing")] object? Billing,
        [property: JsonPropertyName("cancel_url")] string CancelUrl,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("payment_method_types")] IReadOnlyList<string> PaymentMethodTypes,
        [property: JsonPropertyName("line_items")] IReadOnlyList<PayMongoCheckoutSessionLineItem> LineItems,
        [property: JsonPropertyName("metadata")] IReadOnlyDictionary<string, string> Metadata,
        [property: JsonPropertyName("reference_number")] string ReferenceNumber,
        [property: JsonPropertyName("send_email_receipt")] bool SendEmailReceipt,
        [property: JsonPropertyName("show_description")] bool ShowDescription,
        [property: JsonPropertyName("show_line_items")] bool ShowLineItems,
        [property: JsonPropertyName("success_url")] string SuccessUrl);

    private sealed record PayMongoCheckoutSessionLineItem(
        [property: JsonPropertyName("currency")] string Currency,
        [property: JsonPropertyName("amount")] long Amount,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("quantity")] int Quantity);
}
