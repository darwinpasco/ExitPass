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
            errorCode = "PROVIDER_REFERENCE_ALREADY_RECORDED",
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
            ParkingSessionId: Guid.Parse("93e97f33-5849-4b9f-a83f-1080820103d8"),
            RequestedByUserId: Guid.Parse("9f2e5c61-4b6e-4d7d-9d2f-6b2a7a5f8c41"),
            CorrelationId: Guid.Parse("6de95bb4-8f5a-4170-9184-e8eb4cb15c57"),
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
        Assert.Equal("93e97f33-5849-4b9f-a83f-1080820103d8", document.RootElement.GetProperty("parkingSessionId").GetString());
        Assert.Equal("9f2e5c61-4b6e-4d7d-9d2f-6b2a7a5f8c41", document.RootElement.GetProperty("requestedByUserId").GetString());
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
        Assert.Equal("93e97f33-5849-4b9f-a83f-1080820103d8", document.RootElement.GetProperty("parkingSessionId").GetString());
        Assert.Equal("9f2e5c61-4b6e-4d7d-9d2f-6b2a7a5f8c41", document.RootElement.GetProperty("requestedByUserId").GetString());
    }

    /// <summary>
    /// Verifies that provider failure evidence is reported to Central PMS as failed finality,
    /// never as a successful payment confirmation.
    /// </summary>
    [Fact]
    public async Task ReportVerifiedOutcomeAsync_MapsTerminalFailureToFailedFinalAttemptStatus()
    {
        var handler = new StubHttpMessageHandler(_ => CreateJsonResponse(HttpStatusCode.OK, new { status = "ok" }));
        var reporter = CreateReporter(handler);

        var report = CreateReport(
            canonicalStatus: "FAILED",
            rawStatus: "failed",
            isTerminal: true,
            isSuccess: false);

        await reporter.ReportVerifiedOutcomeAsync(report, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(handler.LastRequestContent));

        using var document = JsonDocument.Parse(handler.LastRequestContent!);
        Assert.Equal("failed", document.RootElement.GetProperty("providerStatus").GetString());
        Assert.Equal("FAILED", document.RootElement.GetProperty("finalAttemptStatus").GetString());
    }

    /// <summary>
    /// Verifies that non-terminal provider evidence remains pending and does not create
    /// false payment finality inside Payment Orchestrator.
    /// </summary>
    [Fact]
    public async Task ReportVerifiedOutcomeAsync_MapsNonTerminalOutcomeToPendingProvider()
    {
        var handler = new StubHttpMessageHandler(_ => CreateJsonResponse(HttpStatusCode.OK, new { status = "ok" }));
        var reporter = CreateReporter(handler);

        var report = CreateReport(
            canonicalStatus: "PENDING_PROVIDER",
            rawStatus: "awaiting_payment",
            isTerminal: false,
            isSuccess: false);

        await reporter.ReportVerifiedOutcomeAsync(report, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(handler.LastRequestContent));

        using var document = JsonDocument.Parse(handler.LastRequestContent!);
        Assert.Equal("awaiting_payment", document.RootElement.GetProperty("providerStatus").GetString());
        Assert.Equal("PENDING_PROVIDER", document.RootElement.GetProperty("finalAttemptStatus").GetString());
    }

    /// <summary>
    /// Verifies that AUB outcomes are converted to the provider-neutral Central PMS payload shape.
    /// </summary>
    [Fact]
    public async Task AubOutcomeReport_UsesProviderNeutralCentralPmsPayload()
    {
        var handler = new StubHttpMessageHandler(_ => CreateJsonResponse(HttpStatusCode.OK, new { status = "ok" }));
        var reporter = CreateReporter(handler);

        var report = new VerifiedPaymentOutcomeReport(
            PaymentAttemptId: Guid.Parse("be88ff8e-90a7-45a7-bb7d-3505cfce9076"),
            ParkingSessionId: Guid.Parse("93e97f33-5849-4b9f-a83f-1080820103d8"),
            RequestedByUserId: Guid.Parse("9f2e5c61-4b6e-4d7d-9d2f-6b2a7a5f8c41"),
            CorrelationId: Guid.Parse("6de95bb4-8f5a-4170-9184-e8eb4cb15c57"),
            ProviderCode: "AUB",
            ProviderReference: "aub-ref-001",
            ProviderSessionId: "9c708f54-6daa-4835-a76b-6b166652dd02",
            CanonicalStatus: "SUCCEEDED",
            OccurredAtUtc: DateTimeOffset.Parse("2026-05-16T08:00:00Z"),
            AmountMinor: 12500,
            Currency: "PHP",
            EventId: "aub-ref-001",
            IsTerminal: true,
            IsSuccess: true,
            RawAttributes: new Dictionary<string, string>
            {
                ["provider_status"] = "SUCCESS",
                ["card_bin"] = "420000",
                ["last4_digits"] = "0000"
            });

        await reporter.ReportVerifiedOutcomeAsync(report, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(handler.LastRequestContent));

        using var document = JsonDocument.Parse(handler.LastRequestContent!);
        var root = document.RootElement;

        Assert.Equal(report.PaymentAttemptId.ToString(), root.GetProperty("paymentAttemptId").GetString());
        Assert.Equal(report.ParkingSessionId.ToString(), root.GetProperty("parkingSessionId").GetString());
        Assert.Equal("aub-ref-001", root.GetProperty("providerReference").GetString());
        Assert.Equal("SUCCESS", root.GetProperty("providerStatus").GetString());
        Assert.Equal("CONFIRMED", root.GetProperty("finalAttemptStatus").GetString());
        Assert.Equal("payment-orchestrator", root.GetProperty("requestedBy").GetString());
        Assert.False(root.TryGetProperty("cardBin", out _));
        Assert.False(root.TryGetProperty("last4Digits", out _));
        Assert.False(root.TryGetProperty("cashierUrl", out _));
        Assert.False(root.TryGetProperty("orderInformation", out _));
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

    private static VerifiedPaymentOutcomeReport CreateReport(
        string canonicalStatus = "SUCCEEDED",
        string rawStatus = "SUCCEEDED",
        bool isTerminal = true,
        bool isSuccess = true)
    {
        return new VerifiedPaymentOutcomeReport(
            PaymentAttemptId: Guid.Parse("be88ff8e-90a7-45a7-bb7d-3505cfce9076"),
            ParkingSessionId: Guid.Parse("93e97f33-5849-4b9f-a83f-1080820103d8"),
            RequestedByUserId: Guid.Parse("9f2e5c61-4b6e-4d7d-9d2f-6b2a7a5f8c41"),
            CorrelationId: Guid.Parse("6de95bb4-8f5a-4170-9184-e8eb4cb15c57"),
            ProviderCode: "PAYMONGO",
            ProviderReference: "cs_293285f3347f5496c48332d8",
            ProviderSessionId: "cs_293285f3347f5496c48332d8",
            CanonicalStatus: canonicalStatus,
            OccurredAtUtc: DateTimeOffset.Parse("2026-04-06T10:00:00Z"),
            AmountMinor: 5000,
            Currency: "PHP",
            EventId: "evt_test_009",
            IsTerminal: isTerminal,
            IsSuccess: isSuccess,
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
