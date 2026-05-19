using System.Diagnostics;
using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Application.Eventing;
using ExitPass.CentralPms.Application.Observability;
using ExitPass.CentralPms.Application.PaymentAttempts;
using ExitPass.CentralPms.Application.PaymentAttempts.Commands;
using ExitPass.CentralPms.Application.PaymentAttempts.Results;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Domain.Common;
using ExitPass.CentralPms.Domain.PaymentAttempts;
using ExitPass.CentralPms.Domain.PaymentAttempts.Policies;
using ExitPass.CentralPms.Domain.Sessions;
using ExitPass.CentralPms.Domain.Tariffs;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

#pragma warning disable CS1591

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Tests operational evidence emitted by the Central PMS payment-to-exit application chain.
/// </summary>
public sealed class PaymentToExitOperationalEvidenceTests
{
    private static readonly Guid ParkingSessionId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid TariffSnapshotId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid PaymentAttemptId = Guid.Parse("10000000-0000-0000-0000-000000000003");
    private static readonly Guid CorrelationId = Guid.Parse("10000000-0000-0000-0000-000000000004");
    private static readonly Guid RequestedByUserId = Guid.Parse("10000000-0000-0000-0000-000000000005");
    private static readonly Guid ExitAuthorizationId = Guid.Parse("10000000-0000-0000-0000-000000000006");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-14T08:00:00Z");

    [Fact]
    public async Task CreatePaymentAttempt_WhenCreated_EmitsOperationalEvidence()
    {
        using var listener = new ActivityCapture("ExitPass.CentralPms.Application.PaymentAttempts");
        var fixture = CreatePaymentAttemptFixture(wasReused: false, outcomeCode: "CREATED");

        await fixture.Sut.ExecuteAsync(CreatePaymentAttemptCommand("idem-created"), CancellationToken.None);

        var activity = Assert.Single(listener.StoppedActivities, x => HasTag(x, "idempotency_key", "idem-created"));
        Assert.Equal("CreateOrReusePaymentAttempt", activity.OperationName);
        AssertTag(activity, "correlation_id", CorrelationId);
        AssertTag(activity, "parking_session_id", ParkingSessionId);
        AssertTag(activity, "tariff_snapshot_id", TariffSnapshotId);
        AssertTag(activity, "payment_attempt_id", PaymentAttemptId);
        AssertTag(activity, "outcome", "created");
    }

    [Fact]
    public async Task CreatePaymentAttempt_WhenReused_EmitsOperationalEvidence()
    {
        using var listener = new ActivityCapture("ExitPass.CentralPms.Application.PaymentAttempts");
        var fixture = CreatePaymentAttemptFixture(wasReused: true, outcomeCode: "REUSED_BY_IDEMPOTENCY_KEY");

        await fixture.Sut.ExecuteAsync(CreatePaymentAttemptCommand("idem-reused"), CancellationToken.None);

        var activity = Assert.Single(listener.StoppedActivities, x => HasTag(x, "idempotency_key", "idem-reused"));
        AssertTag(activity, "correlation_id", CorrelationId);
        AssertTag(activity, "payment_attempt_id", PaymentAttemptId);
        AssertTag(activity, "was_reused", true);
        AssertTag(activity, "outcome", "reused");
    }

    [Fact]
    public async Task CreatePaymentAttempt_WhenActiveAttemptExists_EmitsConflictEvidence()
    {
        using var listener = new ActivityCapture("ExitPass.CentralPms.Application.PaymentAttempts");
        var fixture = CreatePaymentAttemptFixture(wasReused: false, outcomeCode: "REJECTED_ACTIVE_ATTEMPT_EXISTS");

        await Assert.ThrowsAsync<ExitPass.CentralPms.Domain.PaymentAttempts.Exceptions.ActivePaymentAttemptAlreadyExistsException>(() =>
            fixture.Sut.ExecuteAsync(CreatePaymentAttemptCommand("idem-conflict"), CancellationToken.None));

        var activity = Assert.Single(listener.StoppedActivities, x => HasTag(x, "idempotency_key", "idem-conflict"));
        AssertTag(activity, "correlation_id", CorrelationId);
        AssertTag(activity, "parking_session_id", ParkingSessionId);
        AssertTag(activity, "tariff_snapshot_id", TariffSnapshotId);
        AssertTag(activity, "payment_attempt_id", PaymentAttemptId);
        AssertTag(activity, "outcome", "conflict");
        AssertTag(activity, "db_outcome_code", "REJECTED_ACTIVE_ATTEMPT_EXISTS");
    }

    [Fact]
    public async Task FinalizePaymentAttempt_WhenConfirmed_EmitsFinalizationEvidence()
    {
        using var listener = new ActivityCapture("ExitPass.CentralPms.Application.Payments");
        var gateway = Substitute.For<IFinalizePaymentAttemptGateway>();
        var clock = Substitute.For<ISystemClock>();
        clock.UtcNow.Returns(Now);
        gateway.FinalizeAsync(Arg.Any<FinalizePaymentAttemptDbRequest>(), Arg.Any<CancellationToken>())
            .Returns(new FinalizePaymentAttemptDbResult
            {
                PaymentAttemptId = PaymentAttemptId,
                AttemptStatus = "CONFIRMED"
            });

        var sut = new FinalizePaymentAttemptHandler(
            gateway,
            clock,
            new CentralPmsMetrics(),
            NullLogger<FinalizePaymentAttemptHandler>.Instance);

        await sut.ExecuteAsync(
            new FinalizePaymentAttemptCommand(
                PaymentAttemptId,
                "CONFIRMED",
                "payment-orchestrator",
                CorrelationId),
            CancellationToken.None);

        var activity = Assert.Single(listener.StoppedActivities, x => x.OperationName == "FinalizePaymentAttempt");
        AssertTag(activity, "correlation_id", CorrelationId);
        AssertTag(activity, "payment_attempt_id", PaymentAttemptId);
        AssertTag(activity, "final_status", "CONFIRMED");
        AssertTag(activity, "outcome", "finalized");
    }

    [Fact]
    public async Task ReportVerifiedPaymentOutcome_WhenSucceeded_EmitsProviderOutcomeEvidence()
    {
        using var listener = new ActivityCapture("ExitPass.CentralPms.Application.Payments");
        var recordGateway = Substitute.For<IRecordPaymentConfirmationGateway>();
        var finalizeUseCase = Substitute.For<IFinalizePaymentAttemptUseCase>();
        var issueUseCase = Substitute.For<IIssueExitAuthorizationUseCase>();
        var clock = Substitute.For<ISystemClock>();
        clock.UtcNow.Returns(Now);

        recordGateway.RecordAsync(Arg.Any<RecordPaymentConfirmationCommand>(), Now, Arg.Any<CancellationToken>())
            .Returns(new RecordPaymentConfirmationResult(
                Guid.Parse("10000000-0000-0000-0000-000000000007"),
                PaymentAttemptId,
                "evt-provider-001",
                "SUCCEEDED",
                "VERIFIED",
                Now));
        finalizeUseCase.ExecuteAsync(Arg.Any<FinalizePaymentAttemptCommand>(), Arg.Any<CancellationToken>())
            .Returns(new FinalizePaymentAttemptResult(PaymentAttemptId, "CONFIRMED"));
        issueUseCase.ExecuteAsync(Arg.Any<IssueExitAuthorizationCommand>(), Arg.Any<CancellationToken>())
            .Returns(new IssueExitAuthorizationResult(
                ExitAuthorizationId,
                ParkingSessionId,
                PaymentAttemptId,
                "AUTH-001",
                "ISSUED",
                Now,
                Now.AddMinutes(15)));

        var sut = new ReportVerifiedPaymentOutcomeHandler(
            recordGateway,
            finalizeUseCase,
            issueUseCase,
            clock,
            NullLogger<ReportVerifiedPaymentOutcomeHandler>.Instance);

        await sut.ExecuteAsync(
            new ReportVerifiedPaymentOutcomeCommand(
                PaymentAttemptId,
                ParkingSessionId,
                "evt-provider-001",
                "SUCCEEDED",
                "CONFIRMED",
                "payment-orchestrator",
                RequestedByUserId,
                CorrelationId),
            CancellationToken.None);

        var activity = Assert.Single(listener.StoppedActivities, x => x.OperationName == "ReportVerifiedPaymentOutcome");
        AssertTag(activity, "correlation_id", CorrelationId);
        AssertTag(activity, "payment_attempt_id", PaymentAttemptId);
        AssertTag(activity, "provider_reference", "evt-provider-001");
        AssertTag(activity, "provider_status", "SUCCEEDED");
        AssertTag(activity, "final_status", "CONFIRMED");
        AssertTag(activity, "exit_authorization_id", ExitAuthorizationId);
        AssertTag(activity, "outcome", "exit_authorization_issued");
    }

    [Fact]
    public async Task IssueExitAuthorization_WhenIssued_EmitsAuthorizationEvidence()
    {
        using var listener = new ActivityCapture("ExitPass.CentralPms.Application.Payments");
        var gateway = Substitute.For<IIssueExitAuthorizationGateway>();
        var clock = Substitute.For<ISystemClock>();
        clock.UtcNow.Returns(Now);
        gateway.IssueAsync(Arg.Any<IssueExitAuthorizationDbRequest>(), Arg.Any<CancellationToken>())
            .Returns(new IssueExitAuthorizationDbResult(
                ExitAuthorizationId,
                ParkingSessionId,
                PaymentAttemptId,
                "AUTH-001",
                "ISSUED",
                Now,
                Now.AddMinutes(15)));

        var sut = new IssueExitAuthorizationHandler(
            gateway,
            clock,
            new CentralPmsMetrics(),
            NullLogger<IssueExitAuthorizationHandler>.Instance);

        await sut.ExecuteAsync(
            new IssueExitAuthorizationCommand(
                ParkingSessionId,
                PaymentAttemptId,
                RequestedByUserId,
                CorrelationId),
            CancellationToken.None);

        var activity = Assert.Single(listener.StoppedActivities, x => x.OperationName == "IssueExitAuthorization");
        AssertTag(activity, "correlation_id", CorrelationId);
        AssertTag(activity, "payment_attempt_id", PaymentAttemptId);
        AssertTag(activity, "exit_authorization_id", ExitAuthorizationId);
        AssertTag(activity, "authorization_status", "ISSUED");
    }

    private static PaymentAttemptFixture CreatePaymentAttemptFixture(bool wasReused, string outcomeCode)
    {
        var parkingRepository = Substitute.For<IParkingSessionReadRepository>();
        var tariffRepository = Substitute.For<ITariffSnapshotReadRepository>();
        var gateway = Substitute.For<IPaymentAttemptDbRoutineGateway>();
        var handoffFactory = Substitute.For<IProviderHandoffFactory>();
        var clock = Substitute.For<ISystemClock>();
        clock.UtcNow.Returns(Now);

        parkingRepository.GetByIdAsync(ParkingSessionId, Arg.Any<CancellationToken>())
            .Returns(ParkingSession.Rehydrate(
                ParkingSessionId,
                "SG-OBS-001",
                "SITE-OBS-001",
                "VENDOR-OBS-001",
                "SESSION-OBS-001",
                "PLATE",
                "ABC1234",
                null,
                Now.AddHours(-1),
                ParkingSessionStatus.PaymentRequired));

        tariffRepository.GetByIdAsync(TariffSnapshotId, Arg.Any<CancellationToken>())
            .Returns(TariffSnapshot.Rehydrate(
                TariffSnapshotId,
                ParkingSessionId,
                TariffSnapshotSourceType.Base,
                100m,
                0m,
                0m,
                100m,
                "PHP",
                100m,
                "TVR-OBS-001",
                null,
                Now,
                Now.AddMinutes(30),
                TariffSnapshotStatus.Active,
                null,
                null));

        gateway.CreateOrReusePaymentAttemptAsync(Arg.Any<CreateOrReusePaymentAttemptDbRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreateOrReusePaymentAttemptDbResult
            {
                PaymentAttemptId = PaymentAttemptId,
                ParkingSessionId = ParkingSessionId,
                TariffSnapshotId = TariffSnapshotId,
                AttemptStatus = "INITIATED",
                PaymentProviderCode = "GCASH",
                WasReused = wasReused,
                OutcomeCode = outcomeCode,
                NetAmountDueSnapshot = 100m,
                CurrencyCode = "PHP",
                IdempotencyKey = "idem-observability"
            });

        handoffFactory.CreatePlaceholder(Arg.Any<PaymentProvider>(), PaymentAttemptId)
            .Returns(new ProviderHandoffResult
            {
                Type = "REDIRECT",
                Url = "/payments/gcash/observability",
                ExpiresAt = Now.AddMinutes(15)
            });

        var sut = new CreateOrReusePaymentAttemptHandler(
            parkingRepository,
            tariffRepository,
            gateway,
            Substitute.For<IPaymentAttemptCreationPolicy>(),
            handoffFactory,
            Substitute.For<IIntegrationEventPublisher>(),
            clock,
            new CentralPmsMetrics(),
            NullLogger<CreateOrReusePaymentAttemptHandler>.Instance);

        return new PaymentAttemptFixture(sut);
    }

    private static CreateOrReusePaymentAttemptCommand CreatePaymentAttemptCommand(string idempotencyKey)
    {
        return new CreateOrReusePaymentAttemptCommand
        {
            ParkingSessionId = ParkingSessionId,
            TariffSnapshotId = TariffSnapshotId,
            PaymentProviderCode = "GCASH",
            IdempotencyKey = idempotencyKey,
            RequestedBy = "webpay-api",
            CorrelationId = CorrelationId
        };
    }

    private static void AssertTag(Activity activity, string key, object expected)
    {
        Assert.Equal(expected.ToString(), activity.TagObjects.Single(x => x.Key == key).Value?.ToString());
    }

    private static bool HasTag(Activity activity, string key, object expected)
    {
        return activity.TagObjects.Any(x => x.Key == key && x.Value?.ToString() == expected.ToString());
    }

    private sealed record PaymentAttemptFixture(CreateOrReusePaymentAttemptHandler Sut);

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
