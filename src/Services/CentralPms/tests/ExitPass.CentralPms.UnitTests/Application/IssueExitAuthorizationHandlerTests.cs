using System.Diagnostics;
using ExitPass.CentralPms.Application.Observability;
using ExitPass.CentralPms.Application.Eventing;
using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using ExitPass.CentralPms.Domain.Common;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Unit tests for <see cref="IssueExitAuthorizationHandler"/>.
///
/// BRD:
/// - 9.12 Exit Authorization
/// - 9.13 Timeout, Retry, and Duplicate Handling
/// - 10.7.2 Payment Finality Invariant
/// - 10.7.7 Exit Token Integrity Invariant
///
/// SDD:
/// - 6.5 Issue Exit Authorization
/// - 8.5 ExitAuthorization State Machine
///
/// Invariants Enforced:
/// - ExitAuthorization issuance validates required identifiers before DB execution
/// - The handler maps the DB-authoritative issue result without mutation
/// - Observability dependencies must not affect business behavior under test
/// </summary>
public sealed class IssueExitAuthorizationHandlerTests
{
    private readonly IIssueExitAuthorizationGateway _gateway = Substitute.For<IIssueExitAuthorizationGateway>();
    private readonly IIntegrationEventPublisher _eventPublisher = Substitute.For<IIntegrationEventPublisher>();
    private readonly ISystemClock _systemClock = Substitute.For<ISystemClock>();
    private readonly CentralPmsMetrics _metrics = new();

    /// <summary>
    /// Verifies that a valid issue command returns the DB-authoritative mapped result.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenCommandIsValid_ReturnsMappedResult()
    {
        var now = new DateTimeOffset(2026, 4, 5, 10, 0, 0, TimeSpan.Zero);
        _systemClock.UtcNow.Returns(now);

        var parkingSessionId = Guid.NewGuid();
        var paymentAttemptId = Guid.NewGuid();
        var requestedByUserId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var exitAuthorizationId = Guid.NewGuid();

        _gateway.IssueAsync(
                Arg.Is<IssueExitAuthorizationDbRequest>(x =>
                    x.ParkingSessionId == parkingSessionId &&
                    x.PaymentAttemptId == paymentAttemptId &&
                    x.RequestedByUserId == requestedByUserId &&
                    x.CorrelationId == correlationId &&
                    x.RequestedAt == now),
                Arg.Any<CancellationToken>())
            .Returns(new IssueExitAuthorizationDbResult(
                ExitAuthorizationId: exitAuthorizationId,
                ParkingSessionId: parkingSessionId,
                PaymentAttemptId: paymentAttemptId,
                AuthorizationToken: "AUTH-TOKEN-001",
                AuthorizationStatus: "ISSUED",
                IssuedAt: now,
                ExpirationTimestamp: now.AddMinutes(15)));

        var sut = CreateSut();

        var result = await sut.ExecuteAsync(
            new IssueExitAuthorizationCommand(
                ParkingSessionId: parkingSessionId,
                PaymentAttemptId: paymentAttemptId,
                RequestedByUserId: requestedByUserId,
                CorrelationId: correlationId),
            CancellationToken.None);

        Assert.Equal(exitAuthorizationId, result.ExitAuthorizationId);
        Assert.Equal(parkingSessionId, result.ParkingSessionId);
        Assert.Equal(paymentAttemptId, result.PaymentAttemptId);
        Assert.Equal("AUTH-TOKEN-001", result.AuthorizationToken);
        Assert.Equal("ISSUED", result.AuthorizationStatus);
        Assert.Equal(now, result.IssuedAt);
        Assert.Equal(now.AddMinutes(15), result.ExpirationTimestamp);

        await _eventPublisher.Received(1).PublishAsync(
            Arg.Is<IntegrationEventEnvelope>(x =>
                x.EventType == IntegrationEventTypes.ExitAuthorizationIssued &&
                x.AggregateId == exitAuthorizationId.ToString() &&
                x.CorrelationId == correlationId),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies RabbitMQ/event-publisher failure cannot undo or block DB-authoritative issuance.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenEventPublishingFails_ReturnsMappedResult()
    {
        var now = new DateTimeOffset(2026, 4, 5, 10, 0, 0, TimeSpan.Zero);
        _systemClock.UtcNow.Returns(now);

        var parkingSessionId = Guid.NewGuid();
        var paymentAttemptId = Guid.NewGuid();
        var requestedByUserId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var exitAuthorizationId = Guid.NewGuid();

        _gateway.IssueAsync(Arg.Any<IssueExitAuthorizationDbRequest>(), Arg.Any<CancellationToken>())
            .Returns(new IssueExitAuthorizationDbResult(
                ExitAuthorizationId: exitAuthorizationId,
                ParkingSessionId: parkingSessionId,
                PaymentAttemptId: paymentAttemptId,
                AuthorizationToken: "AUTH-TOKEN-001",
                AuthorizationStatus: "ISSUED",
                IssuedAt: now,
                ExpirationTimestamp: now.AddMinutes(15)));
        _eventPublisher
            .PublishAsync(Arg.Any<IntegrationEventEnvelope>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("RabbitMQ unavailable"));

        var sut = CreateSut();

        var result = await sut.ExecuteAsync(
            new IssueExitAuthorizationCommand(
                ParkingSessionId: parkingSessionId,
                PaymentAttemptId: paymentAttemptId,
                RequestedByUserId: requestedByUserId,
                CorrelationId: correlationId),
            CancellationToken.None);

        Assert.Equal(exitAuthorizationId, result.ExitAuthorizationId);
        Assert.Equal("ISSUED", result.AuthorizationStatus);
    }

    [Fact]
    public async Task ExecuteAsync_WhenFiscalContextIsMissing_StillIssuesAndRecordsShadowDiagnostic()
    {
        using var listener = new ActivityCapture("ExitPass.CentralPms.Application.Payments");
        var now = new DateTimeOffset(2026, 4, 5, 10, 0, 0, TimeSpan.Zero);
        _systemClock.UtcNow.Returns(now);

        ConfigureGatewaySuccess(now);

        var sut = CreateSut();

        var result = await sut.ExecuteAsync(ValidCommand(), CancellationToken.None);

        Assert.Equal("ISSUED", result.AuthorizationStatus);

        var activity = Assert.Single(
            listener.StoppedActivities,
            x => x.OperationName == "IssueExitAuthorization" &&
                HasTag(
                    x,
                    "fiscal_gating_shadow.status",
                    FiscalGatingShadowEvaluationStatuses.NotEvaluatedMissingFiscalContext));
        AssertTag(
            activity,
            "fiscal_gating_shadow.status",
            FiscalGatingShadowEvaluationStatuses.NotEvaluatedMissingFiscalContext);
    }

    [Fact]
    public async Task ExecuteAsync_WhenShadowFiscalGatingIsBlocked_StillIssuesAuthorization()
    {
        var now = new DateTimeOffset(2026, 4, 5, 10, 0, 0, TimeSpan.Zero);
        _systemClock.UtcNow.Returns(now);
        ConfigureGatewaySuccess(now);

        var shadowEvaluator = Substitute.For<IExitAuthorizationFiscalGatingShadowEvaluator>();
        shadowEvaluator
            .EvaluateAsync(Arg.Any<ExitAuthorizationFiscalGatingShadowContext>(), Arg.Any<CancellationToken>())
            .Returns(FiscalGatingShadowEvaluation.FromGatingEvaluation(new FiscalIssuanceGatingEvaluation(
                IsReadyForNormalExitAuthorization: false,
                BlockedReason: "fiscal_issuance_unknown",
                State: FiscalIssuanceIntegrationState.FiscalIssuanceUnknown,
                RequiresManualReview: true,
                IsExceptionReleaseOnly: false)));

        var sut = CreateSut(shadowEvaluator);

        var result = await sut.ExecuteAsync(ValidCommand(), CancellationToken.None);

        Assert.Equal("ISSUED", result.AuthorizationStatus);
        await shadowEvaluator.Received(1).EvaluateAsync(
            Arg.Is<ExitAuthorizationFiscalGatingShadowContext>(context =>
                context.PaymentAttemptId == ValidCommand().PaymentAttemptId &&
                context.FiscalReference == null &&
                context.IsPaymentFinalityVerified),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenShadowFiscalGatingIsReady_StillReturnsExistingAuthorizationResult()
    {
        var now = new DateTimeOffset(2026, 4, 5, 10, 0, 0, TimeSpan.Zero);
        _systemClock.UtcNow.Returns(now);
        ConfigureGatewaySuccess(now);

        var shadowEvaluator = Substitute.For<IExitAuthorizationFiscalGatingShadowEvaluator>();
        shadowEvaluator
            .EvaluateAsync(Arg.Any<ExitAuthorizationFiscalGatingShadowContext>(), Arg.Any<CancellationToken>())
            .Returns(FiscalGatingShadowEvaluation.FromGatingEvaluation(new FiscalIssuanceGatingEvaluation(
                IsReadyForNormalExitAuthorization: true,
                BlockedReason: null,
                State: FiscalIssuanceIntegrationState.FiscalIssuanceRecorded,
                RequiresManualReview: false,
                IsExceptionReleaseOnly: false)));

        var sut = CreateSut(shadowEvaluator);

        var result = await sut.ExecuteAsync(ValidCommand(), CancellationToken.None);

        Assert.Equal("ISSUED", result.AuthorizationStatus);
        Assert.Equal("AUTH-TOKEN-001", result.AuthorizationToken);
    }

    [Fact]
    public async Task ExecuteAsync_WhenShadowFiscalGatingFails_StillIssuesAuthorization()
    {
        using var listener = new ActivityCapture("ExitPass.CentralPms.Application.Payments");
        var now = new DateTimeOffset(2026, 4, 5, 10, 0, 0, TimeSpan.Zero);
        _systemClock.UtcNow.Returns(now);
        ConfigureGatewaySuccess(now);

        var shadowEvaluator = Substitute.For<IExitAuthorizationFiscalGatingShadowEvaluator>();
        shadowEvaluator
            .EvaluateAsync(Arg.Any<ExitAuthorizationFiscalGatingShadowContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<FiscalGatingShadowEvaluation>>(_ => throw new InvalidOperationException("shadow unavailable"));

        var sut = CreateSut(shadowEvaluator);

        var result = await sut.ExecuteAsync(ValidCommand(), CancellationToken.None);

        Assert.Equal("ISSUED", result.AuthorizationStatus);

        var activity = Assert.Single(
            listener.StoppedActivities,
            x => x.OperationName == "IssueExitAuthorization" &&
                HasTag(
                    x,
                    "fiscal_gating_shadow.status",
                    FiscalGatingShadowEvaluationStatuses.EvaluationFailedNonBlocking));
        AssertTag(
            activity,
            "fiscal_gating_shadow.status",
            FiscalGatingShadowEvaluationStatuses.EvaluationFailedNonBlocking);
    }

    /// <summary>
    /// Verifies that an empty parking session identifier is rejected before DB execution.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenParkingSessionIdIsEmpty_Throws()
    {
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.ExecuteAsync(
                new IssueExitAuthorizationCommand(
                    ParkingSessionId: Guid.Empty,
                    PaymentAttemptId: Guid.NewGuid(),
                    RequestedByUserId: Guid.NewGuid(),
                    CorrelationId: Guid.NewGuid()),
                CancellationToken.None));

        Assert.Contains("ParkingSessionId", ex.Message);
    }

    /// <summary>
    /// Verifies that an empty payment attempt identifier is rejected before DB execution.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenPaymentAttemptIdIsEmpty_Throws()
    {
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.ExecuteAsync(
                new IssueExitAuthorizationCommand(
                    ParkingSessionId: Guid.NewGuid(),
                    PaymentAttemptId: Guid.Empty,
                    RequestedByUserId: Guid.NewGuid(),
                    CorrelationId: Guid.NewGuid()),
                CancellationToken.None));

        Assert.Contains("PaymentAttemptId", ex.Message);
    }

    /// <summary>
    /// Verifies that an empty requesting user identifier is rejected before DB execution.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenRequestedByUserIdIsEmpty_Throws()
    {
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.ExecuteAsync(
                new IssueExitAuthorizationCommand(
                    ParkingSessionId: Guid.NewGuid(),
                    PaymentAttemptId: Guid.NewGuid(),
                    RequestedByUserId: Guid.Empty,
                    CorrelationId: Guid.NewGuid()),
                CancellationToken.None));

        Assert.Contains("RequestedByUserId", ex.Message);
    }

    /// <summary>
    /// Verifies that an empty correlation identifier is rejected before DB execution.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenCorrelationIdIsEmpty_Throws()
    {
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.ExecuteAsync(
                new IssueExitAuthorizationCommand(
                    ParkingSessionId: Guid.NewGuid(),
                    PaymentAttemptId: Guid.NewGuid(),
                    RequestedByUserId: Guid.NewGuid(),
                    CorrelationId: Guid.Empty),
                CancellationToken.None));

        Assert.Contains("CorrelationId", ex.Message);
    }

    /// <summary>
    /// Creates the system under test with no-op logging and shared metrics dependencies.
    /// </summary>
    /// <returns>A configured <see cref="IssueExitAuthorizationHandler"/> instance.</returns>
    private IssueExitAuthorizationHandler CreateSut(
        IExitAuthorizationFiscalGatingShadowEvaluator? fiscalGatingShadowEvaluator = null)
    {
        return new IssueExitAuthorizationHandler(
            _gateway,
            _eventPublisher,
            _systemClock,
            _metrics,
            NullLogger<IssueExitAuthorizationHandler>.Instance,
            fiscalGatingShadowEvaluator);
    }

    private void ConfigureGatewaySuccess(DateTimeOffset now)
    {
        _gateway.IssueAsync(Arg.Any<IssueExitAuthorizationDbRequest>(), Arg.Any<CancellationToken>())
            .Returns(new IssueExitAuthorizationDbResult(
                ExitAuthorizationId: Guid.NewGuid(),
                ParkingSessionId: ValidCommand().ParkingSessionId,
                PaymentAttemptId: ValidCommand().PaymentAttemptId,
                AuthorizationToken: "AUTH-TOKEN-001",
                AuthorizationStatus: "ISSUED",
                IssuedAt: now,
                ExpirationTimestamp: now.AddMinutes(15)));
    }

    private static IssueExitAuthorizationCommand ValidCommand() =>
        new(
            ParkingSessionId: Guid.Parse("10000000-0000-0000-0000-000000000001"),
            PaymentAttemptId: Guid.Parse("10000000-0000-0000-0000-000000000002"),
            RequestedByUserId: Guid.Parse("10000000-0000-0000-0000-000000000003"),
            CorrelationId: Guid.Parse("10000000-0000-0000-0000-000000000004"));

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
        private readonly object _sync = new();
        private readonly List<Activity> _stoppedActivities = new();

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
