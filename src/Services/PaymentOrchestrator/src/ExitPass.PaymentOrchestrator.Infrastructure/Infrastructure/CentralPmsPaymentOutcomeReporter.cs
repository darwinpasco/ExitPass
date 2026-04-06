using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ExitPass.PaymentOrchestrator.Infrastructure.Integrations;

/// <summary>
/// Reports verified provider payment outcomes from POA to Central PMS.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.13 Timeout, Retry, and Duplicate Handling
/// - 10.7.10 Idempotent Payment Confirmation Invariant
/// - 12 Payment Orchestration
///
/// SDD:
/// - 6.4 Finalize Payment
/// - 10.5.3 Report Verified Payment Outcome
/// - 10.7 Idempotency and Concurrency Rules
/// - 12.1.4 Idempotent Recovery
///
/// Invariants Enforced:
/// - Only verified provider outcomes may be reported into Central PMS.
/// - POA reports verified outcomes but does not finalize PaymentAttempt state.
/// - Duplicate provider confirmation reporting must be treated as idempotent success, not as a fatal failure.
/// </summary>
public sealed class CentralPmsPaymentOutcomeReporter : ICentralPmsPaymentOutcomeReporter
{
    private const string DuplicateProviderReferenceErrorCode = "PAYMENT_CONFIRMATION_DUPLICATE_PROVIDER_REFERENCE";
    private const string RequestedByValue = "payment-orchestrator";
    private const string UnknownProviderStatus = "unknown";

    private static readonly JsonSerializerOptions ErrorSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILogger<CentralPmsPaymentOutcomeReporter> _logger;
    private readonly Uri _outcomeReportUri;

    /// <summary>
    /// Initializes a new instance of the <see cref="CentralPmsPaymentOutcomeReporter"/> class.
    /// </summary>
    /// <param name="httpClient">HTTP client used to call Central PMS.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="logger">Logger instance.</param>
    public CentralPmsPaymentOutcomeReporter(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<CentralPmsPaymentOutcomeReporter> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        var baseUrl = configuration["Integrations:CentralPms:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("Configuration value 'Integrations:CentralPms:BaseUrl' is required.");
        }

        _httpClient = httpClient;
        _logger = logger;
        _outcomeReportUri = new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), "v1/internal/payments/outcome");
    }

    /// <inheritdoc />
    public async Task ReportVerifiedOutcomeAsync(
        VerifiedPaymentOutcomeReport report,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);

        var correlationId = Guid.NewGuid().ToString();
        var requestBody = BuildCentralPmsRequest(report);

        using var request = new HttpRequestMessage(HttpMethod.Post, _outcomeReportUri)
        {
            Content = JsonContent.Create(requestBody),
        };

        request.Headers.Add("X-Correlation-Id", correlationId);
        request.Headers.Add("Idempotency-Key", report.EventId);

        _logger.LogInformation(
            "Reporting verified provider outcome to Central PMS. PaymentAttemptId {PaymentAttemptId}, EventId {EventId}, ProviderReference {ProviderReference}, ProviderStatus {ProviderStatus}, CorrelationId {CorrelationId}",
            report.PaymentAttemptId,
            report.EventId,
            requestBody.ProviderReference,
            requestBody.ProviderStatus,
            correlationId);

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation(
                "Central PMS outcome report succeeded. StatusCode {StatusCode}, PaymentAttemptId {PaymentAttemptId}, EventId {EventId}, CorrelationId {CorrelationId}",
                (int)response.StatusCode,
                report.PaymentAttemptId,
                report.EventId,
                correlationId);

            return;
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (IsDuplicateProviderReferenceIdempotentSuccess(response.StatusCode, responseBody))
        {
            _logger.LogInformation(
                "Central PMS reported duplicate provider reference. Treating as idempotent success. PaymentAttemptId {PaymentAttemptId}, EventId {EventId}, ProviderReference {ProviderReference}, CorrelationId {CorrelationId}, ResponseBody {ResponseBody}",
                report.PaymentAttemptId,
                report.EventId,
                requestBody.ProviderReference,
                correlationId,
                responseBody);

            return;
        }

        _logger.LogError(
            "Central PMS outcome report failed. StatusCode {StatusCode}, PaymentAttemptId {PaymentAttemptId}, EventId {EventId}, ProviderReference {ProviderReference}, CorrelationId {CorrelationId}, ResponseBody {ResponseBody}",
            (int)response.StatusCode,
            report.PaymentAttemptId,
            report.EventId,
            requestBody.ProviderReference,
            correlationId,
            responseBody);

        response.EnsureSuccessStatusCode();
    }

    private static CentralPmsPaymentOutcomeRequest BuildCentralPmsRequest(VerifiedPaymentOutcomeReport report)
    {
        return new CentralPmsPaymentOutcomeRequest(
            PaymentAttemptId: report.PaymentAttemptId,
            ProviderReference: report.ProviderReference ?? string.Empty,
            ProviderStatus: ResolveProviderStatus(report),
            RequestedBy: RequestedByValue);
    }

    private static string ResolveProviderStatus(VerifiedPaymentOutcomeReport report)
    {
        if (TryGetString(report.RawAttributes, "status", out var providerStatus))
        {
            return providerStatus;
        }

        if (!string.IsNullOrWhiteSpace(report.CanonicalStatus))
        {
            return report.CanonicalStatus;
        }

        return UnknownProviderStatus;
    }

    private static bool IsDuplicateProviderReferenceIdempotentSuccess(HttpStatusCode statusCode, string responseBody)
    {
        if (statusCode != HttpStatusCode.Conflict)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return false;
        }

        try
        {
            var error = JsonSerializer.Deserialize<CentralPmsErrorResponse>(responseBody, ErrorSerializerOptions);
            return string.Equals(
                error?.ErrorCode,
                DuplicateProviderReferenceErrorCode,
                StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetString(
        IReadOnlyDictionary<string, string>? rawAttributes,
        string key,
        out string value)
    {
        value = string.Empty;

        if (rawAttributes is null || rawAttributes.Count == 0 || string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (!rawAttributes.TryGetValue(key, out var found) || string.IsNullOrWhiteSpace(found))
        {
            return false;
        }

        value = found;
        return true;
    }

    private sealed record CentralPmsErrorResponse(
        string? ErrorCode,
        string? Message,
        string? CorrelationId,
        bool? Retryable,
        JsonElement? Details);
}
