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

    /// <summary>
    /// Initializes a new instance of the <see cref="AubClient"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client.</param>
    /// <param name="options">The bound AUB provider options.</param>
    public AubClient(HttpClient httpClient, IOptions<AubOptions> options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
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

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_options.BaseUrl.TrimEnd('/')}/v1/payments");

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Idempotency-Key", command.IdempotencyKey);
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
            merchant_id = string.IsNullOrWhiteSpace(_options.MerchantId) ? null : _options.MerchantId,
            reference_id = command.PaymentAttemptId.ToString(),
            amount = command.AmountMinor,
            currency = command.Currency,
            description = command.Description,
            callback_url = command.WebhookUrl,
            redirect_urls = new
            {
                success = command.SuccessUrl,
                failure = command.FailureUrl,
                cancel = command.CancelUrl,
            },
            metadata = command.Metadata,
        };
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

            var paymentSessionId = ReadRequiredString(root, "payment_session_id");
            var status = ReadRequiredString(root, "status");
            var providerReference = ReadOptionalString(root, "reference");
            var redirectUrl = ReadOptionalString(root, "redirect_url");
            DateTimeOffset? expiresAtUtc = null;

            if (root.TryGetProperty("expires_at", out var expiresAt) &&
                expiresAt.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(expiresAt.GetString(), out var parsedExpiry))
            {
                expiresAtUtc = parsedExpiry.ToUniversalTime();
            }

            return new AubPaymentSessionResponse(
                paymentSessionId,
                providerReference,
                status,
                redirectUrl,
                expiresAtUtc,
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
