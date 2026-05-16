using System.Net.Http.Json;
using System.Text.Json;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ExitPass.PaymentOrchestrator.Infrastructure.Integrations;

/// <summary>
/// HTTP client for Central PMS APIs composed by the WebPay payment intent flow.
/// </summary>
public sealed class CentralPmsWebPayClient : ICentralPmsWebPayClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILogger<CentralPmsWebPayClient> _logger;
    private readonly Uri _vendorParkingResolveUri;
    private readonly Uri _createPaymentAttemptUri;

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
            payload.VendorSystemId,
            payload.CorrelationId));
    }

    /// <inheritdoc />
    public async Task<CentralPmsWebPayResult<CentralPmsPaymentAttempt>> CreateOrReusePaymentAttemptAsync(
        Guid parkingSessionId,
        Guid tariffSnapshotId,
        string paymentProvider,
        string idempotencyKey,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var body = new CreatePaymentAttemptRequest(
            ParkingSessionId: parkingSessionId,
            TariffSnapshotId: tariffSnapshotId,
            PaymentProvider: paymentProvider);

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
            return new CentralPmsWebPayError(
                statusCode,
                string.IsNullOrWhiteSpace(error?.ErrorCode) ? fallbackCode : error.ErrorCode,
                string.IsNullOrWhiteSpace(error?.Message) ? "Central PMS request failed." : error.Message,
                error?.Retryable ?? statusCode >= 500);
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
        string LookupOutcome,
        string? PlateNumber,
        string? TicketReference,
        long NetPayableMinorUnits,
        string Currency,
        DateTimeOffset TariffExpiresAt,
        string VendorSystemId,
        Guid CorrelationId);

    private sealed record CreatePaymentAttemptRequest(
        Guid ParkingSessionId,
        Guid TariffSnapshotId,
        string PaymentProvider);

    private sealed record CreatePaymentAttemptResponse(
        Guid PaymentAttemptId,
        string AttemptStatus,
        string PaymentProvider,
        bool WasReused);

    private sealed record ErrorResponse(
        string? ErrorCode,
        string? Message,
        Guid? CorrelationId,
        bool? Retryable,
        JsonElement? Details);
}
