using System.Net;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Persistence;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;
using ExitPass.PaymentOrchestrator.Application.UseCases.QueryProviderSessionStatus;
using ExitPass.PaymentOrchestrator.Application.UseCases.RecoverProviderPaymentOutcome;
using ExitPass.PaymentOrchestrator.Contracts.Payments;
using ExitPass.PaymentOrchestrator.Infrastructure.Providers.PayMongo;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace ExitPass.PaymentOrchestrator.UnitTests.Application.UseCases.RecoverProviderPaymentOutcome;

/// <summary>
/// Regression tests for governed provider-status recovery.
/// </summary>
public sealed class RecoverProviderPaymentOutcomeHandlerTests
{
    private static readonly Guid PaymentAttemptId = Guid.Parse("55b4dae2-686a-4285-98cb-ba8da5e263a1");
    private static readonly Guid ParkingSessionId = Guid.Parse("cbbff961-0e51-4885-86bb-05b1ce362957");
    private static readonly Guid CorrelationId = Guid.Parse("4bcb9991-94e1-45fc-9aa7-ee70ff4742f1");
    private const string CheckoutSessionId = "cs_3df5c88e1cb19438315937ea";
    private const string PaymentId = "pay_9vmG3UJAANaLWq22PKsZfurC";

    [Fact]
    public async Task HandleAsync_WhenPersistedCheckoutIsPaid_ReportsOneGovernedOutcomeKey()
    {
        var repository = Substitute.For<IProviderSessionRepository>();
        var providerHandler = new StubHttpMessageHandler(PaidCheckoutStatusResponse());
        var adapter = CreatePayMongoAdapter(providerHandler);
        var reporter = new DeduplicatingOutcomeReporter();

        repository
            .FindLatestByPaymentAttemptIdAsync(PaymentAttemptId, Arg.Any<CancellationToken>())
            .Returns(CreateProviderSession());
        repository
            .FindByProviderSessionIdAsync("PAYMONGO", CheckoutSessionId, Arg.Any<CancellationToken>())
            .Returns(CreateProviderSession());

        var handler = CreateHandler(repository, adapter, reporter);

        var result = await handler.HandleAsync(CreateCommand(), CancellationToken.None);

        Assert.True(result.Queried);
        Assert.True(result.Reported);
        Assert.Null(result.ErrorCode);
        Assert.Single(reporter.UniqueReports);

        var report = reporter.UniqueReports.Single();
        Assert.Equal(PaymentAttemptId, report.PaymentAttemptId);
        Assert.Equal(ParkingSessionId, report.ParkingSessionId);
        Assert.Equal("PAYMONGO", report.ProviderCode);
        Assert.Equal(CheckoutSessionId, report.ProviderSessionId);
        Assert.Equal(PaymentId, report.ProviderReference);
        Assert.Equal(5000, report.AmountMinor);
        Assert.Equal("PHP", report.Currency);
        Assert.Equal(
            $"status-query:PAYMONGO:{CheckoutSessionId}:{PaymentId}:{CanonicalPaymentOutcomeStatus.Succeeded}",
            report.EventId);

        Assert.Equal(1, providerHandler.RequestCount);
    }

    [Fact]
    public async Task HandleAsync_WhenPaidRecoveryIsReplayed_ReusesGovernedOutcomeKey()
    {
        var repository = Substitute.For<IProviderSessionRepository>();
        var providerHandler = new StubHttpMessageHandler(PaidCheckoutStatusResponse());
        var adapter = CreatePayMongoAdapter(providerHandler);
        var reporter = new DeduplicatingOutcomeReporter();

        repository
            .FindLatestByPaymentAttemptIdAsync(PaymentAttemptId, Arg.Any<CancellationToken>())
            .Returns(CreateProviderSession());
        repository
            .FindByProviderSessionIdAsync("PAYMONGO", CheckoutSessionId, Arg.Any<CancellationToken>())
            .Returns(CreateProviderSession());

        var handler = CreateHandler(repository, adapter, reporter);

        var first = await handler.HandleAsync(CreateCommand(), CancellationToken.None);
        var replay = await handler.HandleAsync(CreateCommand(), CancellationToken.None);

        Assert.True(first.Reported);
        Assert.True(replay.Reported);
        Assert.Equal(2, reporter.ReportAttempts);
        Assert.Single(reporter.UniqueReports);
        Assert.Single(reporter.EventIds);

        Assert.Equal(2, providerHandler.RequestCount);
    }

    private static PayMongoCheckoutAdapter CreatePayMongoAdapter(HttpMessageHandler handler)
    {
        var options = Options.Create(new PayMongoOptions
        {
            BaseUrl = "https://api.paymongo.test",
            SecretKey = "sk_test_unit",
            PublicKey = "pk_test_unit",
            WebhookSecretKey = "whsec_unit",
            IsLiveMode = false,
            AllowedPaymentMethodTypes = new[] { "qrph" }
        });

        return new PayMongoCheckoutAdapter(
            new PayMongoClient(new HttpClient(handler), options),
            options);
    }

    private static RecoverProviderPaymentOutcomeHandler CreateHandler(
        IProviderSessionRepository repository,
        IProviderStatusQueryAdapter adapter,
        ICentralPmsPaymentOutcomeReporter reporter)
    {
        var queryHandler = new QueryProviderSessionStatusHandler(
            NullLogger<QueryProviderSessionStatusHandler>.Instance,
            repository,
            new[] { adapter });

        return new RecoverProviderPaymentOutcomeHandler(
            NullLogger<RecoverProviderPaymentOutcomeHandler>.Instance,
            repository,
            queryHandler,
            reporter);
    }

    private static RecoverProviderPaymentOutcomeCommand CreateCommand() =>
        new(PaymentAttemptId, ParkingSessionId, 5000, "PHP", CorrelationId);

    private static ProviderSessionRecord CreateProviderSession() =>
        new(
            Guid.Parse("df7e9805-c355-49b5-a775-bcded171c6ae"),
            PaymentAttemptId,
            "PAYMONGO",
            "PAYMONGO_CHECKOUT_SESSION",
            CheckoutSessionId,
            CheckoutSessionId,
            "PENDING",
            "https://checkout.paymongo.test/session",
            null,
            DateTimeOffset.Parse("2026-11-07T05:25:37Z"),
            "webpay:test:recovery",
            CorrelationId,
            "{}",
            "{}",
            DateTimeOffset.Parse("2026-11-07T05:10:37Z"),
            5000,
            "PHP");

    private static string PaidCheckoutStatusResponse() =>
        $$"""
          {
            "data": {
              "id": "{{CheckoutSessionId}}",
              "type": "checkout_session",
              "attributes": {
                "status": "active",
                "updated_at": 1794028244,
                "payments": [
                  {
                    "id": "{{PaymentId}}",
                    "type": "payment",
                    "attributes": {
                      "amount": 5000,
                      "currency": "PHP",
                      "status": "paid",
                      "paid_at": 1794028244
                    }
                  }
                ]
              }
            }
          }
          """;

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseJson;

        public StubHttpMessageHandler(string responseJson)
        {
            _responseJson = responseJson;
        }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson)
            });
        }
    }

    private sealed class DeduplicatingOutcomeReporter : ICentralPmsPaymentOutcomeReporter
    {
        private readonly Dictionary<string, VerifiedPaymentOutcomeReport> _reports = new(StringComparer.Ordinal);

        public int ReportAttempts { get; private set; }

        public IReadOnlyCollection<string> EventIds => _reports.Keys;

        public IReadOnlyCollection<VerifiedPaymentOutcomeReport> UniqueReports => _reports.Values;

        public Task ReportVerifiedOutcomeAsync(
            VerifiedPaymentOutcomeReport report,
            CancellationToken cancellationToken)
        {
            ReportAttempts++;
            _reports.TryAdd(report.EventId, report);
            return Task.CompletedTask;
        }
    }
}
