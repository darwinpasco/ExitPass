using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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
    /// <param name="options">The PayMongo provider options.</param>
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

        if (string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            throw new InvalidOperationException("PayMongo secret key is required.");
        }

        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            throw new InvalidOperationException("PayMongo base URL is required.");
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

        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(responseJson);
        var data = document.RootElement.GetProperty("data");
        var attributes = data.GetProperty("attributes");

        var checkoutSessionId = data.GetProperty("id").GetString();
        var checkoutUrl = attributes.GetProperty("checkout_url").GetString();

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

    private object BuildCheckoutSessionRequest(CreateProviderPaymentSessionCommand command)
    {
        return new
        {
            data = new
            {
                attributes = new
                {
                    billing = (object?)null,
                    cancel_url = command.CancelUrl,
                    description = command.Description,
                    payment_method_types = _options.AllowedPaymentMethodTypes,
                    line_items = new[]
                    {
                        new
                        {
                            currency = command.Currency,
                            amount = command.AmountMinor,
                            name = command.Description,
                            quantity = 1
                        }
                    },
                    metadata = command.Metadata,
                    reference_number = command.PaymentAttemptId.ToString(),
                    send_email_receipt = false,
                    show_description = true,
                    show_line_items = true,
                    success_url = command.SuccessUrl
                }
            }
        };
    }

    private static AuthenticationHeaderValue BuildBasicAuthorizationHeader(string secretKey)
    {
        var raw = $"{secretKey}:";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        return new AuthenticationHeaderValue("Basic", encoded);
    }
}
