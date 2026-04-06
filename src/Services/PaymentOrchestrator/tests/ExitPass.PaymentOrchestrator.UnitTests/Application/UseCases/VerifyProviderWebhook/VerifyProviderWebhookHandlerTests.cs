using ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Persistence;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;
using ExitPass.PaymentOrchestrator.Application.UseCases.VerifyProviderWebhook;
using ExitPass.PaymentOrchestrator.Contracts.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ExitPass.PaymentOrchestrator.UnitTests.Application.UseCases.VerifyProviderWebhook;

/// <summary>
/// Unit tests for <see cref="VerifyProviderWebhookHandler" />.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.13 Timeout, Retry, and Duplicate Handling
/// - 14 Audit, Logging, and Reporting
///
/// SDD:
/// - 10.5.2 Payment Provider Webhook
/// - 10.5.3 Report Verified Payment Outcome
/// - 10.7 Idempotency and Concurrency Rules
///
/// Invariants Enforced:
/// - Only verified provider outcomes may enter the platform.
/// - Duplicate provider callbacks must not create duplicate control transitions.
/// - Only Central PMS may finalize PaymentAttempt state.
/// </summary>
public sealed class VerifyProviderWebhookHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenWebhookIsNotAuthentic_ReturnsRejected()
    {
        var adapter = new Mock<IPaymentProviderAdapter>(MockBehavior.Strict);
        var repository = new Mock<IProviderWebhookEventRepository>(MockBehavior.Strict);
        var reporter = new Mock<ICentralPmsPaymentOutcomeReporter>(MockBehavior.Strict);

        adapter.SetupGet(x => x.ProviderCode).Returns("PAYMONGO");
        adapter.SetupGet(x => x.ProviderProduct).Returns("CHECKOUT");
        adapter
            .Setup(x => x.VerifyWebhookAsync(It.IsAny<ProviderWebhookRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateNotAuthenticVerificationResult());

        var handler = CreateHandler(adapter, repository, reporter);

        var result = await handler.HandleAsync(CreateRequest(), CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.False(result.Duplicate);
        Assert.Equal("WEBHOOK_NOT_AUTHENTIC", result.Code);

        repository.Verify(
            x => x.ExistsByProviderEventIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        repository.Verify(
            x => x.AddAsync(It.IsAny<ProviderWebhookEventRecord>(), It.IsAny<CancellationToken>()),
            Times.Never);

        reporter.Verify(
            x => x.ReportVerifiedOutcomeAsync(It.IsAny<VerifiedPaymentOutcomeReport>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenDuplicateExistsBeforePersistence_ReturnsAcceptedDuplicate()
    {
        var adapter = new Mock<IPaymentProviderAdapter>(MockBehavior.Strict);
        var repository = new Mock<IProviderWebhookEventRepository>(MockBehavior.Strict);
        var reporter = new Mock<ICentralPmsPaymentOutcomeReporter>(MockBehavior.Strict);

        adapter.SetupGet(x => x.ProviderCode).Returns("PAYMONGO");
        adapter.SetupGet(x => x.ProviderProduct).Returns("CHECKOUT");
        adapter
            .Setup(x => x.VerifyWebhookAsync(It.IsAny<ProviderWebhookRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAuthenticVerificationResult());

        repository
            .Setup(x => x.ExistsByProviderEventIdAsync("PAYMONGO", "evt_test_009", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = CreateHandler(adapter, repository, reporter);

        var result = await handler.HandleAsync(CreateRequest(), CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.True(result.Duplicate);
        Assert.Equal("evt_test_009", result.Code);

        repository.Verify(
            x => x.AddAsync(It.IsAny<ProviderWebhookEventRecord>(), It.IsAny<CancellationToken>()),
            Times.Never);

        reporter.Verify(
            x => x.ReportVerifiedOutcomeAsync(It.IsAny<VerifiedPaymentOutcomeReport>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenDuplicateDetectedDuringPersistence_ReturnsAcceptedDuplicate()
    {
        var adapter = new Mock<IPaymentProviderAdapter>(MockBehavior.Strict);
        var repository = new Mock<IProviderWebhookEventRepository>(MockBehavior.Strict);
        var reporter = new Mock<ICentralPmsPaymentOutcomeReporter>(MockBehavior.Strict);

        adapter.SetupGet(x => x.ProviderCode).Returns("PAYMONGO");
        adapter.SetupGet(x => x.ProviderProduct).Returns("CHECKOUT");
        adapter
            .Setup(x => x.VerifyWebhookAsync(It.IsAny<ProviderWebhookRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAuthenticVerificationResult());

        repository
            .Setup(x => x.ExistsByProviderEventIdAsync("PAYMONGO", "evt_test_009", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        repository
            .Setup(x => x.AddAsync(It.IsAny<ProviderWebhookEventRecord>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DuplicateProviderWebhookEventException("duplicate"));

        var handler = CreateHandler(adapter, repository, reporter);

        var result = await handler.HandleAsync(CreateRequest(), CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.True(result.Duplicate);
        Assert.Equal("evt_test_009", result.Code);

        reporter.Verify(
            x => x.ReportVerifiedOutcomeAsync(It.IsAny<VerifiedPaymentOutcomeReport>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenWebhookIsFirstSeen_PersistsEventReportsOutcomeAndReturnsAccepted()
    {
        var adapter = new Mock<IPaymentProviderAdapter>(MockBehavior.Strict);
        var repository = new Mock<IProviderWebhookEventRepository>(MockBehavior.Strict);
        var reporter = new Mock<ICentralPmsPaymentOutcomeReporter>(MockBehavior.Strict);

        adapter.SetupGet(x => x.ProviderCode).Returns("PAYMONGO");
        adapter.SetupGet(x => x.ProviderProduct).Returns("CHECKOUT");
        adapter
            .Setup(x => x.VerifyWebhookAsync(It.IsAny<ProviderWebhookRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAuthenticVerificationResult());

        repository
            .Setup(x => x.ExistsByProviderEventIdAsync("PAYMONGO", "evt_test_009", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        repository
            .Setup(x => x.AddAsync(It.IsAny<ProviderWebhookEventRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        reporter
            .Setup(x => x.ReportVerifiedOutcomeAsync(It.IsAny<VerifiedPaymentOutcomeReport>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = CreateHandler(adapter, repository, reporter);

        var result = await handler.HandleAsync(CreateRequest(), CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.False(result.Duplicate);
        Assert.Equal("evt_test_009", result.Code);

        repository.Verify(
            x => x.AddAsync(
                It.Is<ProviderWebhookEventRecord>(r =>
                    r.ProviderCode == "PAYMONGO" &&
                    r.ProviderEventId == "evt_test_009" &&
                    r.ProviderEventType == "payment.paid" &&
                    r.ProviderReference == "cs_293285f3347f5496c48332d8" &&
                    r.ProviderSessionId == "cs_293285f3347f5496c48332d8" &&
                    r.PaymentAttemptId == Guid.Parse("be88ff8e-90a7-45a7-bb7d-3505cfce9076") &&
                    r.IsAuthentic &&
                    !r.IsDuplicate &&
                    !string.IsNullOrWhiteSpace(r.RawHeadersJson) &&
                    !string.IsNullOrWhiteSpace(r.RawBodyJson)),
                It.IsAny<CancellationToken>()),
            Times.Once);

        reporter.Verify(
            x => x.ReportVerifiedOutcomeAsync(
                It.Is<VerifiedPaymentOutcomeReport>(r =>
                    r.PaymentAttemptId == Guid.Parse("be88ff8e-90a7-45a7-bb7d-3505cfce9076") &&
                    r.ProviderCode == "PAYMONGO" &&
                    r.ProviderReference == "cs_293285f3347f5496c48332d8" &&
                    r.ProviderSessionId == "cs_293285f3347f5496c48332d8" &&
                    r.CanonicalStatus == "SUCCEEDED" &&
                    r.EventId == "evt_test_009" &&
                    r.IsTerminal &&
                    r.IsSuccess),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static VerifyProviderWebhookHandler CreateHandler(
        Mock<IPaymentProviderAdapter> adapter,
        Mock<IProviderWebhookEventRepository> repository,
        Mock<ICentralPmsPaymentOutcomeReporter> reporter)
    {
        return new VerifyProviderWebhookHandler(
            NullLogger<VerifyProviderWebhookHandler>.Instance,
            adapter.Object,
            repository.Object,
            reporter.Object);
    }

    private static ProviderWebhookRequest CreateRequest()
    {
        return new ProviderWebhookRequest(
            Headers: new Dictionary<string, string>
            {
                ["Paymongo-Signature"] = "t=123,v1=test"
            },
            RawBody: "{ \"data\": { \"id\": \"evt_test_009\" } }");
    }

    private static ProviderWebhookVerificationResult CreateNotAuthenticVerificationResult()
    {
        return new ProviderWebhookVerificationResult(
            IsAuthentic: false,
            EventId: "evt_test_009",
            EventType: "payment.paid",
            PaymentAttemptId: Guid.Parse("be88ff8e-90a7-45a7-bb7d-3505cfce9076"),
            ProviderReference: "cs_293285f3347f5496c48332d8",
            ProviderSessionId: "cs_293285f3347f5496c48332d8",
            CanonicalStatus: CanonicalPaymentOutcomeStatus.Succeeded,
            OccurredAtUtc: DateTimeOffset.Parse("2026-04-06T10:00:00Z"),
            AmountMinor: 5000,
            Currency: "PHP",
            IsTerminal: true,
            IsSuccess: true,
            RawAttributes: new Dictionary<string, string>
            {
                ["status"] = "SUCCEEDED"
            });
    }

    private static ProviderWebhookVerificationResult CreateAuthenticVerificationResult()
    {
        return new ProviderWebhookVerificationResult(
            IsAuthentic: true,
            EventId: "evt_test_009",
            EventType: "payment.paid",
            PaymentAttemptId: Guid.Parse("be88ff8e-90a7-45a7-bb7d-3505cfce9076"),
            ProviderReference: "cs_293285f3347f5496c48332d8",
            ProviderSessionId: "cs_293285f3347f5496c48332d8",
            CanonicalStatus: CanonicalPaymentOutcomeStatus.Succeeded,
            OccurredAtUtc: DateTimeOffset.Parse("2026-04-06T10:00:00Z"),
            AmountMinor: 5000,
            Currency: "PHP",
            IsTerminal: true,
            IsSuccess: true,
            RawAttributes: new Dictionary<string, string>
            {
                ["status"] = "SUCCEEDED"
            });
    }
}
