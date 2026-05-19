using System.Diagnostics;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Persistence;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;
using ExitPass.PaymentOrchestrator.Application.UseCases.VerifyProviderWebhook;
using ExitPass.PaymentOrchestrator.Contracts.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

#pragma warning disable CS1591

namespace ExitPass.PaymentOrchestrator.UnitTests.Application.UseCases.VerifyProviderWebhook;

/// <summary>
/// Tests operational evidence emitted by verified provider webhook processing.
/// </summary>
public sealed class VerifyProviderWebhookObservabilityTests
{
    private static readonly Guid PaymentAttemptId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid ParkingSessionId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid RequestedByUserId = Guid.Parse("20000000-0000-0000-0000-000000000003");
    private static readonly Guid CorrelationId = Guid.Parse("20000000-0000-0000-0000-000000000004");

    [Fact]
    public async Task ReportVerifiedPaymentOutcome_WhenSucceeded_EmitsProviderOutcomeEvidence()
    {
        using var listener = new ActivityCapture("ExitPass.PaymentOrchestrator.Application");
        var adapter = new Mock<IPaymentProviderAdapter>(MockBehavior.Strict);
        var repository = new Mock<IProviderWebhookEventRepository>(MockBehavior.Strict);
        var reporter = new Mock<ICentralPmsPaymentOutcomeReporter>(MockBehavior.Strict);

        adapter.SetupGet(x => x.ProviderCode).Returns("PAYMONGO");
        adapter.SetupGet(x => x.ProviderProduct).Returns("PAYMONGO_CHECKOUT_SESSION");
        adapter.Setup(x => x.VerifyWebhookAsync(It.IsAny<ProviderWebhookRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderWebhookVerificationResult(
                IsAuthentic: true,
                EventId: "evt_observability_001",
                EventType: "checkout_session.payment.paid",
                PaymentAttemptId: PaymentAttemptId,
                ProviderReference: "cs_observability_001",
                ProviderSessionId: "cs_observability_001",
                CanonicalStatus: CanonicalPaymentOutcomeStatus.Succeeded,
                OccurredAtUtc: DateTimeOffset.Parse("2026-05-14T08:00:00Z"),
                AmountMinor: 10000,
                Currency: "PHP",
                IsTerminal: true,
                IsSuccess: true,
                RawAttributes: new Dictionary<string, string>
                {
                    ["status"] = "paid",
                    ["parking_session_id"] = ParkingSessionId.ToString(),
                    ["requested_by_user_id"] = RequestedByUserId.ToString(),
                    ["correlation_id"] = CorrelationId.ToString()
                }));

        repository.Setup(x => x.ExistsByProviderEventIdAsync("PAYMONGO", "evt_observability_001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        repository.Setup(x => x.AddAsync(It.IsAny<ProviderWebhookEventRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        reporter.Setup(x => x.ReportVerifiedOutcomeAsync(It.IsAny<VerifiedPaymentOutcomeReport>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new VerifyProviderWebhookHandler(
            NullLogger<VerifyProviderWebhookHandler>.Instance,
            adapter.Object,
            repository.Object,
            reporter.Object);

        await sut.HandleAsync(
            new ProviderWebhookRequest(
                new Dictionary<string, string>
                {
                    ["Paymongo-Signature"] = "t=123,v1=test"
                },
                "{ \"data\": { \"id\": \"evt_observability_001\" } }"),
            CancellationToken.None);

        var activity = Assert.Single(
            listener.StoppedActivities,
            x => HasTag(x, "provider_event.id", "evt_observability_001"));
        AssertTag(activity, "provider.code", "PAYMONGO");
        AssertTag(activity, "provider_event.id", "evt_observability_001");
        AssertTag(activity, "provider_reference", "cs_observability_001");
        AssertTag(activity, "payment_attempt.id", PaymentAttemptId);
        AssertTag(activity, "payment.canonical_status", "SUCCEEDED");
        AssertTag(activity, "correlation_id", CorrelationId);
        AssertTag(activity, "parking_session_id", ParkingSessionId);
    }

    private static void AssertTag(Activity activity, string key, object expected)
    {
        Assert.Equal(expected.ToString(), activity.TagObjects.Single(x => x.Key == key).Value?.ToString());
    }

    private static bool HasTag(Activity activity, string key, object expected)
    {
        return activity.TagObjects.Any(x => x.Key == key && x.Value?.ToString() == expected.ToString());
    }

    private sealed class ActivityCapture : IDisposable
    {
        private readonly ActivityListener _listener;

        public ActivityCapture(string sourceName)
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == sourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = Capture
            };

            ActivitySource.AddActivityListener(_listener);
        }

        private readonly object _sync = new();

        private readonly List<Activity> _stoppedActivities = new();

        public IReadOnlyCollection<Activity> StoppedActivities
        {
            get
            {
                lock (_sync)
                {
                    return _stoppedActivities.ToArray();
                }
            }
        }

        public void Dispose()
        {
            _listener.Dispose();
        }

        private void Capture(Activity activity)
        {
            lock (_sync)
            {
                _stoppedActivities.Add(activity);
            }
        }
    }
}

#pragma warning restore CS1591
