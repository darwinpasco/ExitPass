using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
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
/// - 12 Payment Orchestration
///
/// SDD:
/// - 10.5.3 Report Verified Payment Outcome
///
/// Invariants Enforced:
/// - Only verified provider outcomes may be reported into Central PMS.
/// - POA reports verified outcomes but does not finalize PaymentAttempt state.
/// </summary>
public sealed class CentralPmsPaymentOutcomeReporter : ICentralPmsPaymentOutcomeReporter
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CentralPmsPaymentOutcomeReporter> _logger;
    private readonly string _baseUrl;

    /// <summary>
    /// Initializes a new instance of the <see cref="CentralPmsPaymentOutcomeReporter"/> class.
    /// </summary>
    public CentralPmsPaymentOutcomeReporter(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<CentralPmsPaymentOutcomeReporter> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _baseUrl = configuration["Integrations:CentralPms:BaseUrl"]
            ?? throw new InvalidOperationException("Configuration value 'Integrations:CentralPms:BaseUrl' is required.");
    }

    /// <inheritdoc />
    public async Task ReportVerifiedOutcomeAsync(
        VerifiedPaymentOutcomeReport report,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);

        var requestUri = new Uri(new Uri(_baseUrl.TrimEnd('/') + "/"), "v1/internal/payments/outcome");
        var correlationId = Guid.NewGuid().ToString();

        var requestBody = BuildCentralPmsRequest(report);

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(requestBody)
        };

        request.Headers.Add("X-Correlation-Id", correlationId);
        request.Headers.Add("Idempotency-Key", report.EventId);

        _logger.LogInformation(
            "Reporting verified provider outcome to Central PMS. PaymentAttemptId {PaymentAttemptId}, EventId {EventId}, ProviderStatus {ProviderStatus}, CorrelationId {CorrelationId}",
            report.PaymentAttemptId,
            report.EventId,
            requestBody.ProviderStatus,
            correlationId);

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            _logger.LogError(
                "Central PMS outcome report failed. StatusCode {StatusCode}, PaymentAttemptId {PaymentAttemptId}, EventId {EventId}, CorrelationId {CorrelationId}, ResponseBody {ResponseBody}",
                (int)response.StatusCode,
                report.PaymentAttemptId,
                report.EventId,
                correlationId,
                body);

            response.EnsureSuccessStatusCode();
        }

        _logger.LogInformation(
            "Central PMS outcome report succeeded. PaymentAttemptId {PaymentAttemptId}, EventId {EventId}, CorrelationId {CorrelationId}",
            report.PaymentAttemptId,
            report.EventId,
            correlationId);
    }

    private static CentralPmsPaymentOutcomeRequest BuildCentralPmsRequest(VerifiedPaymentOutcomeReport report)
    {
        return new CentralPmsPaymentOutcomeRequest(
            PaymentAttemptId: report.PaymentAttemptId,
            ProviderReference: report.ProviderReference ?? string.Empty,
            ProviderStatus: ResolveProviderStatus(report),
            RequestedBy: "payment-orchestrator",
            RawCallbackReference: report.EventId,
            ProviderSignatureValid: true,
            ProviderPayloadHash: ComputePayloadHash(report.RawAttributes),
            AmountConfirmed: report.AmountMinor,
            CurrencyCode: report.Currency);
    }

    private static string ResolveProviderStatus(VerifiedPaymentOutcomeReport report)
    {
        if (TryGetNestedString(report.RawAttributes, out var status, "status"))
        {
            return status;
        }

        if (!string.IsNullOrWhiteSpace(report.CanonicalStatus))
        {
            return report.CanonicalStatus;
        }

        return "unknown";
    }

    private static string ComputePayloadHash(IReadOnlyDictionary<string, string> rawAttributes)
    {
        var normalized = NormalizeJson(rawAttributes);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes);
    }

    private static string NormalizeJson(IReadOnlyDictionary<string, string> rawAttributes)
    {
        if (rawAttributes is null || rawAttributes.Count == 0)
        {
            return "{}";
        }

        return JsonSerializer.Serialize(rawAttributes);
    }

    private static bool TryGetNestedString(
        IReadOnlyDictionary<string, string> rawAttributes,
        out string value,
        params object[] path)
    {
        value = string.Empty;

        if (rawAttributes is null || rawAttributes.Count == 0 || path.Length == 0)
        {
            return false;
        }

        if (path[0] is not string key)
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
}
