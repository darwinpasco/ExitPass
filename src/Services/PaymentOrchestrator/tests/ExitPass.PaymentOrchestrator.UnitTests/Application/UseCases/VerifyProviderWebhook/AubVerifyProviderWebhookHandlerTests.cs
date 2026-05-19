using System.Text.Json;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Persistence;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;
using ExitPass.PaymentOrchestrator.Application.UseCases.VerifyProviderWebhook;
using ExitPass.PaymentOrchestrator.Contracts.Payments;
using ExitPass.PaymentOrchestrator.Infrastructure.Providers.Aub;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace ExitPass.PaymentOrchestrator.UnitTests.Application.UseCases.VerifyProviderWebhook;

/// <summary>
/// Unit tests proving AUB-shaped provider outcomes use the provider-neutral webhook reporting path.
/// </summary>
public sealed class AubVerifyProviderWebhookHandlerTests
{
    private static readonly Guid PaymentAttemptId = Guid.Parse("9c708f54-6daa-4835-a76b-6b166652dd02");
    private static readonly Guid ParkingSessionId = Guid.Parse("58931a43-eef2-43fa-887a-38a9874d72e7");
    private static readonly Guid RequestedByUserId = Guid.Parse("9f2e5c61-4b6e-4d7d-9d2f-6b2a7a5f8c41");
    private static readonly Guid CorrelationId = Guid.Parse("6de95bb4-8f5a-4170-9184-e8eb4cb15c57");

    /// <summary>
    /// Verifies that a verified AUB success reports a provider-neutral succeeded outcome to Central PMS.
    /// </summary>
    [Fact]
    public async Task AubWebhook_WhenSuccess_ReportsVerifiedSucceededOutcomeToCentralPms()
    {
        var fixture = CreateFixture();

        var result = await fixture.Handler.HandleAsync(CreateAubWebhookRequest("SUCCESS"), CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.False(result.Duplicate);
        Assert.False(result.Ignored);
        Assert.Equal("aub-ref-001", result.Code);

        fixture.Reporter.Verify(
            x => x.ReportVerifiedOutcomeAsync(
                It.Is<VerifiedPaymentOutcomeReport>(report =>
                    report.ProviderCode == "AUB" &&
                    report.PaymentAttemptId == PaymentAttemptId &&
                    report.ParkingSessionId == ParkingSessionId &&
                    report.RequestedByUserId == RequestedByUserId &&
                    report.CorrelationId == CorrelationId &&
                    report.ProviderReference == "aub-ref-001" &&
                    report.ProviderSessionId == PaymentAttemptId.ToString() &&
                    report.CanonicalStatus == "SUCCEEDED" &&
                    report.IsTerminal &&
                    report.IsSuccess),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that a verified AUB pending outcome is persisted as evidence but is not reported for Central PMS finalization.
    /// </summary>
    [Fact]
    public async Task AubWebhook_WhenPending_DoesNotReportFinalSuccess()
    {
        var fixture = CreateFixture();

        var result = await fixture.Handler.HandleAsync(CreateAubWebhookRequest("PENDING"), CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.False(result.Duplicate);
        Assert.False(result.Ignored);

        fixture.Repository.Verify(
            x => x.AddAsync(
                It.Is<ProviderWebhookEventRecord>(record =>
                    record.ProviderCode == "AUB" &&
                    record.ProviderEventId == "aub-ref-001" &&
                    record.PaymentAttemptId == PaymentAttemptId &&
                    record.IsAuthentic),
                It.IsAny<CancellationToken>()),
            Times.Once);
        fixture.Reporter.Verify(
            x => x.ReportVerifiedOutcomeAsync(It.IsAny<VerifiedPaymentOutcomeReport>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that terminal non-success AUB outcomes are reported as failed finality, not success.
    /// </summary>
    /// <param name="aubStatus">AUB transaction result.</param>
    /// <param name="canonicalStatus">Expected provider-neutral status.</param>
    [Theory]
    [InlineData("FAILED", "FAILED")]
    [InlineData("CANCELLED", "CANCELLED")]
    [InlineData("EXPIRED", "EXPIRED")]
    public async Task AubWebhook_WhenTerminalNonSuccess_ReportsFailedOutcomeWithoutExitAuthorization(
        string aubStatus,
        string canonicalStatus)
    {
        var fixture = CreateFixture();

        var result = await fixture.Handler.HandleAsync(CreateAubWebhookRequest(aubStatus), CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.False(result.Duplicate);
        Assert.False(result.Ignored);

        fixture.Reporter.Verify(
            x => x.ReportVerifiedOutcomeAsync(
                It.Is<VerifiedPaymentOutcomeReport>(report =>
                    report.ProviderCode == "AUB" &&
                    report.CanonicalStatus == canonicalStatus &&
                    report.IsTerminal &&
                    !report.IsSuccess),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that malformed AUB payloads fail closed and are never reported as successful payment finality.
    /// </summary>
    [Fact]
    public async Task AubWebhook_WhenMalformed_FailsClosedAndDoesNotReportSuccess()
    {
        var fixture = CreateFixture();

        var result = await fixture.Handler.HandleAsync(
            new ProviderWebhookRequest(
                Headers: new Dictionary<string, string>(),
                RawBody: "{\"code\":\"00\",\"message\":\"Success\"}"),
            CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Equal("WEBHOOK_NOT_AUTHENTIC", result.Code);

        fixture.Repository.Verify(
            x => x.AddAsync(It.IsAny<ProviderWebhookEventRecord>(), It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.Reporter.Verify(
            x => x.ReportVerifiedOutcomeAsync(It.IsAny<VerifiedPaymentOutcomeReport>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static TestFixture CreateFixture()
    {
        var adapter = CreateAubAdapter();
        var repository = new Mock<IProviderWebhookEventRepository>(MockBehavior.Strict);
        var reporter = new Mock<ICentralPmsPaymentOutcomeReporter>(MockBehavior.Strict);

        repository
            .Setup(x => x.ExistsByProviderEventIdAsync("AUB", "aub-ref-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        repository
            .Setup(x => x.AddAsync(It.IsAny<ProviderWebhookEventRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        reporter
            .Setup(x => x.ReportVerifiedOutcomeAsync(It.IsAny<VerifiedPaymentOutcomeReport>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new VerifyProviderWebhookHandler(
            NullLogger<VerifyProviderWebhookHandler>.Instance,
            adapter,
            repository.Object,
            reporter.Object);

        return new TestFixture(handler, repository, reporter);
    }

    private static AubPaymentAdapter CreateAubAdapter()
    {
        var client = new AubClient(
            new HttpClient(new ThrowingHttpMessageHandler()),
            Options.Create(new AubOptions { BaseUrl = "https://aub.test/gateway/payment" }),
            new FakeAubRequestSigner());

        return new AubPaymentAdapter(client);
    }

    private static ProviderWebhookRequest CreateAubWebhookRequest(string status)
    {
        var body = new
        {
            code = "00",
            message = "Success",
            data = new
            {
                orderInformation = new
                {
                    referencedId = "aub-ref-001",
                    orderId = PaymentAttemptId.ToString(),
                    goodsDetail = "ExitPass parking payment",
                    attach = $"parking_session_id={ParkingSessionId};requested_by_user_id={RequestedByUserId};correlation_id={CorrelationId}",
                    currency = "PHP",
                    amount = 12500,
                    responseDate = "2026-05-16T08:00:00Z",
                    transactionResult = status,
                    paymentType = "PAY",
                    paymentBrand = "VISA"
                },
                card = new
                {
                    paymentBrand = "VISA",
                    cardBin = "420000",
                    last4Digits = "0000"
                }
            }
        };

        return new ProviderWebhookRequest(
            Headers: new Dictionary<string, string>(),
            RawBody: JsonSerializer.Serialize(body));
    }

    private sealed class FakeAubRequestSigner : IAubRequestSigner
    {
        public string CreateAuthorizationHeader(AubSignedRequest request) => "AUB-TEST-SIGNATURE";
    }

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("AUB webhook handler tests must not call provider HTTP APIs.");
        }
    }

    private sealed record TestFixture(
        VerifyProviderWebhookHandler Handler,
        Mock<IProviderWebhookEventRepository> Repository,
        Mock<ICentralPmsPaymentOutcomeReporter> Reporter);
}
