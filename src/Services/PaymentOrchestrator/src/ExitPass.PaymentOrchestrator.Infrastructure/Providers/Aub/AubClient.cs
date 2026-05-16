using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;
using Microsoft.Extensions.Options;

namespace ExitPass.PaymentOrchestrator.Infrastructure.Providers.Aub;

/// <summary>
/// Thin HTTP client for AUB provider operations used by the Payment Orchestrator adapter boundary.
/// </summary>
public sealed class AubClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly AubOptions _options;
    private readonly IAubRequestSigner _requestSigner;

    /// <summary>
    /// Initializes a new instance of the <see cref="AubClient"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client.</param>
    /// <param name="options">The bound AUB provider options.</param>
    /// <param name="requestSigner">The AUB request signer.</param>
    public AubClient(HttpClient httpClient, IOptions<AubOptions> options, IAubRequestSigner requestSigner)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _requestSigner = requestSigner ?? throw new ArgumentNullException(nameof(requestSigner));
    }

    /// <summary>
    /// Creates an AUB payment session.
    /// </summary>
    /// <param name="command">The provider-neutral session creation command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The normalized AUB session response.</returns>
    public async Task<AubPaymentSessionResponse> CreatePaymentSessionAsync(
        CreateProviderPaymentSessionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            throw new InvalidOperationException("AUB base URL is required.");
        }

        var requestPayload = BuildPaymentSessionRequest(command);
        var requestJson = JsonSerializer.Serialize(requestPayload, JsonOptions);

        var requestUri = new Uri($"{_options.BaseUrl.TrimEnd('/')}/cashier/v1/payment", UriKind.Absolute);
        var requestDate = DateTimeOffset.UtcNow;
        var customerRequestId = command.IdempotencyKey;
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.AcceptLanguage.ParseAdd("en-US");
        request.Headers.TryAddWithoutValidation("Customer-Request-Id", customerRequestId);
        request.Headers.Date = requestDate;
        request.Headers.TryAddWithoutValidation(
            "Authorization",
            _requestSigner.CreateAuthorizationHeader(new AubSignedRequest(
                request.Method.Method,
                requestUri.PathAndQuery,
                requestJson,
                customerRequestId,
                requestDate)));
        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

        response.EnsureSuccessStatusCode();

        return ParsePaymentSessionResponse(responseJson);
    }

    private object BuildPaymentSessionRequest(CreateProviderPaymentSessionCommand command)
    {
        return new
        {
            orderInformation = new
            {
                amount = command.AmountMinor,
                orderId = command.PaymentAttemptId.ToString(),
                goodsDetail = command.Description,
                attach = CreateAttachValue(command),
                callbackUrl = command.SuccessUrl,
                notifyUrl = command.WebhookUrl,
                validityPeriod = _options.DefaultValidityPeriodMinutes,
            }
        };
    }

    private static string? CreateAttachValue(CreateProviderPaymentSessionCommand command)
    {
        if (command.Metadata.TryGetValue("attach", out var explicitAttach) &&
            !string.IsNullOrWhiteSpace(explicitAttach))
        {
            return explicitAttach;
        }

        return string.IsNullOrWhiteSpace(command.IdempotencyKey)
            ? null
            : command.IdempotencyKey;
    }

    private static AubPaymentSessionResponse ParsePaymentSessionResponse(string responseJson)
    {
        try
        {
            using var document = JsonDocument.Parse(responseJson);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("AUB response root was not an object.");
            }

            var code = ReadRequiredString(root, "code");
            var message = ReadRequiredString(root, "message");

            if (!string.Equals(code, "00", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"AUB response code '{code}' was not successful: {message}");
            }

            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("AUB response did not contain 'data'.");
            }

            var cashierUrl = ReadRequiredString(data, "cashierUrl");
            if (!data.TryGetProperty("orderInformation", out var orderInformation) ||
                orderInformation.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("AUB response did not contain 'data.orderInformation'.");
            }

            var orderId = ReadRequiredString(orderInformation, "orderId");

            return new AubPaymentSessionResponse(
                orderId,
                code,
                message,
                cashierUrl,
                null,
                responseJson);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("AUB response was not valid JSON.", ex);
        }
    }

    private static string ReadRequiredString(JsonElement element, string propertyName)
    {
        var value = ReadOptionalString(element, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"AUB response did not contain '{propertyName}'.");
        }

        return value;
    }

    private static string? ReadOptionalString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
}
