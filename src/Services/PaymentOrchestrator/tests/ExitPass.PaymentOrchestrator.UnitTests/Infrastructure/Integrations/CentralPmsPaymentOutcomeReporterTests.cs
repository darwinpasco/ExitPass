using System.Net;
using System.Text;
using System.Text.Json;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;
using ExitPass.PaymentOrchestrator.Infrastructure.Integrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ExitPass.PaymentOrchestrator.UnitTests.Infrastructure.Integrations;

/// <summary>
/// Unit tests for <see cref="CentralPmsPaymentOutcomeReporter"/>.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.13 Timeout, Retry, and Duplicate Handling
/// - 12 Payment Orchestration
///
/// SDD:
/// - 10.5.3 Report Verified Payment Outcome
/// - 10.7 Idempotency and Concurrency Rules
///
/// Invariants Enforced:
/// - Only verified provider outcomes may be reported into Central PMS.
/// - POA reports verified outcomes but does not finalize PaymentAttempt state.
/// - Duplicate confirmation reporting must be handled idempotently.
/// </summary>
public sealed class CentralPmsPaymentOutcomeReporterTests
{
    [Fact]
    public async Task ReportVerifiedOutcomeAsync_WhenCentralPmsReturnsOk_CompletesSuccessfully()
    {
        var handler = new StubHttpMessageHandler(_ => CreateJsonResponse(HttpStatusCode.OK, new { status = "ok" }));
        var reporter = CreateReporter(handler);

        var report = CreateReport();

        await reporter.ReportVerifiedOutcomeAsync(report, CancellationToken.None);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("http://central-pms:8080/v1/internal/payments/outcome", handler.LastRequest.RequestUri!.ToString());
        Assert.True(handler.LastRequest.Headers.Contains("X-Correlation-Id"));
        Assert.True(handler.LastRequest.Headers.Contains("Idempotency-Key"));
        Assert.Equal(report.EventId, handler.LastRequest.Headers.GetValues("Idempotency-Key").Single());
    }

    [Fact]
    public async Task ReportVerifiedOutcomeAsync_WhenCentralPmsReturnsCreated_CompletesSuccessfully()
    {
        var handler = new StubHttpMessageHandler(_ => CreateJsonResponse(HttpStatusCode.Created, new { status = "created" }));
        var reporter = CreateReporter(handler);

        await reporter.ReportVerifiedOutcomeAsync(CreateReport(), CancellationToken.None);
    }

    [Fact]
    public async Task ReportVerifiedOutcomeAsync_WhenCentralPmsReturnsNoContent_CompletesSuccessfully()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var reporter = CreateReporter(handler);

        await reporter.ReportVerifiedOutcomeAsync(CreateReport(), CancellationToken.None);
    }

    [Fact]
    public async Task ReportVerifiedOutcomeAsync_WhenCentralPmsReturnsDuplicateProviderReferenceConflict_TreatsAsSuccess()
    {
        var responseBody = new
        {
            errorCode = "PAYMENT_CONFIRMATION_DUPLICATE_PROVIDER_REFERENCE",
            message = "Provider reference has already been recorded.",
            correlationId = Guid.NewGuid().ToString(),
            retryable = false,
            details = new
            {
                payment_attempt_id = "be88ff8e-90a7-45a7-bb7d-3505cfce9076",
                provider_reference = "cs_293285f3347f5496c48332d8"
            }
        };

        var handler = new StubHttpMessageHandler(_ => CreateJsonResponse(HttpStatusCode.Conflict, responseBody));
        var reporter = CreateReporter(handler);

        var exception = await Record.ExceptionAsync(() =>
            reporter.ReportVerifiedOutcomeAsync(CreateReport(), CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task ReportVerifiedOutcomeAsync_WhenCentralPmsReturnsOtherConflict_ThrowsHttpRequestException()
    {
        var responseBody = new
        {
            errorCode = "SOME_OTHER_CONFLICT",
            message = "Some other conflict.",
            correlationId = Guid.NewGuid().ToString(),
            retryable = false
        };

        var handler = new StubHttpMessageHandler(_ => CreateJsonResponse(HttpStatusCode.Conflict, responseBody));
        var reporter = CreateReporter(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            reporter.ReportVerifiedOutcomeAsync(CreateReport(), CancellationToken.None));
    }

    [Fact]
    public async Task ReportVerifiedOutcomeAsync_WhenCentralPmsReturnsServerError_ThrowsHttpRequestException()
    {
        var handler = new StubHttpMessageHandler(_ => CreateJsonResponse(
            HttpStatusCode.InternalServerError,
            new { errorCode = "INTERNAL_ERROR", message = "Boom." }));

        var reporter = CreateReporter(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            reporter.ReportVerifiedOutcomeAsync(CreateReport(), CancellationToken.None));
    }

    [Fact]
    public async Task ReportVerifiedOutcomeAsync_UsesCanonicalStatusWhenRawStatusIsMissing()
    {
        var handler = new StubHttpMessageHandler(_ => CreateJsonResponse(HttpStatusCode.OK, new { status = "ok" }));
        var reporter = CreateReporter(handler);

        var report = new VerifiedPaymentOutcomeReport(
            PaymentAttemptId: Guid.Parse("be88ff8e-90a7-45a7-bb7d-3505cfce9076"),
            ProviderCode: "PAYMONGO",
            ProviderReference: "cs_293285f3347f5496c48332d8",
            ProviderSessionId: "cs_293285f3347f5496c48332d8",
            CanonicalStatus: "SUCCEEDED",
            OccurredAtUtc: DateTimeOffset.Parse("2026-04-06T10:00:00Z"),
            AmountMinor: 5000,
            Currency: "PHP",
            EventId: "evt_test_009",
            IsTerminal: true,
            IsSuccess: true,
            RawAttributes: new Dictionary<string, string>());

        await reporter.ReportVerifiedOutcomeAsync(report, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(handler.LastRequestContent));

        using var document = JsonDocument.Parse(handler.LastRequestContent!);
        Assert.Equal("SUCCEEDED", document.RootElement.GetProperty("providerStatus").GetString());
    }

    [Fact]
    public async Task ReportVerifiedOutcomeAsync_UsesRawStatusWhenAvailable()
    {
        var handler = new StubHttpMessageHandler(_ => CreateJsonResponse(HttpStatusCode.OK, new { status = "ok" }));
        var reporter = CreateReporter(handler);

        var report = CreateReport(rawStatus: "paid");

        await reporter.ReportVerifiedOutcomeAsync(report, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(handler.LastRequestContent));

        using var document = JsonDocument.Parse(handler.LastRequestContent!);
        Assert.Equal("paid", document.RootElement.GetProperty("providerStatus").GetString());
    }

    private static CentralPmsPaymentOutcomeReporter CreateReporter(HttpMessageHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Integrations:CentralPms:BaseUrl"] = "http://central-pms:8080"
            })
            .Build();

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://central-pms:8080")
        };

        return new CentralPmsPaymentOutcomeReporter(
            httpClient,
            configuration,
            NullLogger<CentralPmsPaymentOutcomeReporter>.Instance);
    }

    private static VerifiedPaymentOutcomeReport CreateReport(string rawStatus = "SUCCEEDED")
    {
        return new VerifiedPaymentOutcomeReport(
            PaymentAttemptId: Guid.Parse("be88ff8e-90a7-45a7-bb7d-3505cfce9076"),
            ProviderCode: "PAYMONGO",
            ProviderReference: "cs_293285f3347f5496c48332d8",
            ProviderSessionId: "cs_293285f3347f5496c48332d8",
            CanonicalStatus: "SUCCEEDED",
            OccurredAtUtc: DateTimeOffset.Parse("2026-04-06T10:00:00Z"),
            AmountMinor: 5000,
            Currency: "PHP",
            EventId: "evt_test_009",
            IsTerminal: true,
            IsSuccess: true,
            RawAttributes: new Dictionary<string, string>
            {
                ["status"] = rawStatus
            });
    }

    private static HttpResponseMessage CreateJsonResponse(HttpStatusCode statusCode, object body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json")
        };
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory ?? throw new ArgumentNullException(nameof(responseFactory));
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastRequestContent { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestContent = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            LastRequest = CloneRequestWithoutDisposedContent(request, LastRequestContent);

            return _responseFactory(request);
        }

        private static HttpRequestMessage CloneRequestWithoutDisposedContent(
            HttpRequestMessage request,
            string? content)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);

            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (content is not null)
            {
                var mediaType = request.Content?.Headers.ContentType?.MediaType ?? "application/json";
                var charSet = request.Content?.Headers.ContentType?.CharSet;
                var encoding = string.IsNullOrWhiteSpace(charSet)
                    ? Encoding.UTF8
                    : Encoding.GetEncoding(charSet);

                clone.Content = new StringContent(content, encoding, mediaType);

                if (request.Content is not null)
                {
                    foreach (var header in request.Content.Headers)
                    {
                        clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }
            }

            return clone;
        }
    }
}
