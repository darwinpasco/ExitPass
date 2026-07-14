using System.Diagnostics;
using System.Text.Json;
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
    public async Task ExecuteAsync_WhenFiscalContextIsMissing_BlocksBeforeDbIssueAndRecordsDiagnostic()
    {
        using var listener = new ActivityCapture("ExitPass.CentralPms.Application.Payments");
        var now = new DateTimeOffset(2026, 4, 5, 10, 0, 0, TimeSpan.Zero);
        _systemClock.UtcNow.Returns(now);

        ConfigureGatewaySuccess(now);
        var shadowEvaluator = Substitute.For<IExitAuthorizationFiscalGatingShadowEvaluator>();
        shadowEvaluator
            .EvaluateAsync(Arg.Any<ExitAuthorizationFiscalGatingShadowContext>(), Arg.Any<CancellationToken>())
            .Returns(FiscalGatingShadowEvaluation.NotEvaluatedMissingFiscalContext());

        var sut = CreateSut(shadowEvaluator);

        var ex = await Assert.ThrowsAsync<ExitAuthorizationIssuanceConflictException>(() =>
            sut.ExecuteAsync(ValidCommand(), CancellationToken.None));
        Assert.Equal("fiscal_reference_not_recorded", ex.ErrorCode);

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
        AssertTag(
            activity,
            "fiscal_gating_shadow.enforcement_decision",
            FiscalIssuanceExitAuthorizationEnforcementDecisions.Block);
        AssertTag(activity, "fiscal_gating_shadow.enforcement_enabled", true);
        AssertTag(activity, "fiscal_gating_shadow.enforcement_wired_for_blocking", true);
        _ = _gateway.DidNotReceiveWithAnyArgs().IssueAsync(default!, default);
        await _eventPublisher.Received(1).PublishAsync(
            Arg.Is<IntegrationEventEnvelope>(x => IsMissingContextShadowObservation(x)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenFiscalGatingIsBlocked_BlocksBeforeDbIssue()
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

        var ex = await Assert.ThrowsAsync<ExitAuthorizationIssuanceConflictException>(() =>
            sut.ExecuteAsync(ValidCommand(), CancellationToken.None));

        Assert.Equal("fiscal_issuance_unknown", ex.ErrorCode);
        await shadowEvaluator.Received(1).EvaluateAsync(
            Arg.Is<ExitAuthorizationFiscalGatingShadowContext>(context =>
                context.PaymentAttemptId == ValidCommand().PaymentAttemptId &&
                context.FiscalReference == null &&
                context.IsPaymentFinalityVerified),
            Arg.Any<CancellationToken>());
        _ = _gateway.DidNotReceiveWithAnyArgs().IssueAsync(default!, default);
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
    public async Task ExecuteAsync_WhenPaymentFinalityIsMissing_BlocksBeforeDbIssue()
    {
        var now = new DateTimeOffset(2026, 4, 5, 10, 0, 0, TimeSpan.Zero);
        _systemClock.UtcNow.Returns(now);
        ConfigureGatewaySuccess(now);

        var sut = CreateSut(isPaymentFinalityVerified: false);

        var ex = await Assert.ThrowsAsync<ExitAuthorizationIssuanceConflictException>(() =>
            sut.ExecuteAsync(ValidCommand(), CancellationToken.None));

        Assert.Equal("payment_finality_not_verified", ex.ErrorCode);
        _ = _gateway.DidNotReceiveWithAnyArgs().IssueAsync(default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_WhenFiscalGatingFails_BlocksBeforeDbIssue()
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

        var ex = await Assert.ThrowsAsync<ExitAuthorizationIssuanceConflictException>(() =>
            sut.ExecuteAsync(ValidCommand(), CancellationToken.None));

        Assert.Equal(nameof(InvalidOperationException), ex.ErrorCode);

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
        await _eventPublisher.Received(1).PublishAsync(
            Arg.Is<IntegrationEventEnvelope>(x => IsFailureShadowObservation(x, nameof(InvalidOperationException))),
            Arg.Any<CancellationToken>());
        _ = _gateway.DidNotReceiveWithAnyArgs().IssueAsync(default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_WhenFiscalLookupFindsReadyReference_StillIssuesAndRecordsReadyDiagnostic()
    {
        using var listener = new ActivityCapture("ExitPass.CentralPms.Application.Payments");
        var now = new DateTimeOffset(2026, 4, 5, 10, 0, 0, TimeSpan.Zero);
        _systemClock.UtcNow.Returns(now);
        ConfigureGatewaySuccess(now);
        var command = ValidCommand();
        var repository = Substitute.For<IFiscalIssuanceReferenceRepository>();
        var fiscalReference = CompleteFiscalReference(command, FiscalIssuanceIntegrationState.FiscalIssuanceRecorded);
        repository
            .FindLatestByPaymentAttemptIdAsync(command.PaymentAttemptId, Arg.Any<CancellationToken>())
            .Returns(fiscalReference);
        var sut = CreateSut(new ExitAuthorizationFiscalGatingShadowEvaluator(repository));

        var result = await sut.ExecuteAsync(command, CancellationToken.None);

        Assert.Equal("ISSUED", result.AuthorizationStatus);
        var activity = AssertShadowActivity(
            listener,
            FiscalGatingShadowEvaluationStatuses.EvaluatedReady);
        AssertTag(activity, "fiscal_gating_shadow.payment_confirmation_id", fiscalReference.PaymentConfirmationId);
        AssertTag(activity, "fiscal_gating_shadow.fiscal_issuance_reference_id", fiscalReference.FiscalIssuanceReferenceId);
        AssertTag(activity, "fiscal_gating_shadow.fiscal_document_number", fiscalReference.FiscalDocumentNumber!);
        AssertTag(activity, "fiscal_gating_shadow.enforcement_decision", FiscalIssuanceExitAuthorizationEnforcementDecisions.Allow);
        AssertTag(activity, "fiscal_gating_shadow.would_allow_normal_exit_authorization", true);
        AssertTag(activity, "fiscal_gating_shadow.enforcement_wired_for_blocking", true);
        await repository.Received(1).FindLatestByPaymentAttemptIdAsync(
            command.PaymentAttemptId,
            Arg.Any<CancellationToken>());
        await _eventPublisher.Received(1).PublishAsync(
            Arg.Is<IntegrationEventEnvelope>(x => IsReadyShadowObservation(x, command, fiscalReference)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenFiscalLookupFindsBlockedReference_BlocksBeforeDbIssue()
    {
        using var listener = new ActivityCapture("ExitPass.CentralPms.Application.Payments");
        var now = new DateTimeOffset(2026, 4, 5, 10, 0, 0, TimeSpan.Zero);
        _systemClock.UtcNow.Returns(now);
        ConfigureGatewaySuccess(now);
        var command = ValidCommand();
        var repository = Substitute.For<IFiscalIssuanceReferenceRepository>();
        repository
            .FindLatestByPaymentAttemptIdAsync(command.PaymentAttemptId, Arg.Any<CancellationToken>())
            .Returns(MinimalFiscalReference(command, FiscalIssuanceIntegrationState.FiscalIssuanceUnknown));
        var sut = CreateSut(new ExitAuthorizationFiscalGatingShadowEvaluator(repository));

        var ex = await Assert.ThrowsAsync<ExitAuthorizationIssuanceConflictException>(() =>
            sut.ExecuteAsync(command, CancellationToken.None));

        Assert.Equal("fiscal_issuance_unknown", ex.ErrorCode);
        var activity = AssertShadowActivity(
            listener,
            FiscalGatingShadowEvaluationStatuses.EvaluatedBlocked);
        AssertTag(activity, "fiscal_gating_shadow.blocked_reason", "fiscal_issuance_unknown");
        _ = _gateway.DidNotReceiveWithAnyArgs().IssueAsync(default!, default);
        await _eventPublisher.Received(1).PublishAsync(
            Arg.Is<IntegrationEventEnvelope>(x => IsBlockedShadowObservation(
                x,
                "fiscal_issuance_unknown",
                FiscalIssuanceIntegrationState.FiscalIssuanceUnknown.ToString())),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenFiscalLookupFails_BlocksBeforeDbIssue()
    {
        using var listener = new ActivityCapture("ExitPass.CentralPms.Application.Payments");
        var now = new DateTimeOffset(2026, 4, 5, 10, 0, 0, TimeSpan.Zero);
        _systemClock.UtcNow.Returns(now);
        ConfigureGatewaySuccess(now);
        var command = ValidCommand();
        var repository = Substitute.For<IFiscalIssuanceReferenceRepository>();
        repository
            .FindLatestByPaymentAttemptIdAsync(command.PaymentAttemptId, Arg.Any<CancellationToken>())
            .Returns<Task<FiscalIssuanceReferenceRecord?>>(_ => throw new InvalidOperationException("lookup failed"));
        var sut = CreateSut(new ExitAuthorizationFiscalGatingShadowEvaluator(repository));

        var ex = await Assert.ThrowsAsync<ExitAuthorizationIssuanceConflictException>(() =>
            sut.ExecuteAsync(command, CancellationToken.None));

        Assert.Equal(nameof(InvalidOperationException), ex.ErrorCode);
        var activity = AssertShadowActivity(
            listener,
            FiscalGatingShadowEvaluationStatuses.EvaluationFailedNonBlocking);
        AssertTag(activity, "fiscal_gating_shadow.blocked_reason", nameof(InvalidOperationException));
        _ = _gateway.DidNotReceiveWithAnyArgs().IssueAsync(default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_WhenFiscalReferenceHasFailureMetadata_ShadowObservationIncludesExceptionAndErrorPosture()
    {
        var now = new DateTimeOffset(2026, 4, 5, 10, 0, 0, TimeSpan.Zero);
        _systemClock.UtcNow.Returns(now);
        ConfigureGatewaySuccess(now);
        var command = ValidCommand();
        var fiscalReference = MinimalFiscalReference(command, FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration) with
        {
            LatestExceptionReason = FiscalIssuanceExceptionReason.FiscalSequencePolicyNotFound,
            LatestErrorPosture = FiscalIssuanceErrorPosture.RetryAfterConfigurationCorrection
        };
        var repository = Substitute.For<IFiscalIssuanceReferenceRepository>();
        repository
            .FindLatestByPaymentAttemptIdAsync(command.PaymentAttemptId, Arg.Any<CancellationToken>())
            .Returns(fiscalReference);
        var sut = CreateSut(new ExitAuthorizationFiscalGatingShadowEvaluator(repository));

        var ex = await Assert.ThrowsAsync<ExitAuthorizationIssuanceConflictException>(() =>
            sut.ExecuteAsync(command, CancellationToken.None));

        Assert.Equal("fiscal_issuance_failed_configuration", ex.ErrorCode);
        await _eventPublisher.Received(1).PublishAsync(
            Arg.Is<IntegrationEventEnvelope>(x => IsFailureMetadataShadowObservation(x)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenFiscalGatingWouldBlock_ShadowObservationIncludesBlockDecision()
    {
        var now = new DateTimeOffset(2026, 4, 5, 10, 0, 0, TimeSpan.Zero);
        _systemClock.UtcNow.Returns(now);
        ConfigureGatewaySuccess(now);
        var shadowEvaluator = Substitute.For<IExitAuthorizationFiscalGatingShadowEvaluator>();
        shadowEvaluator
            .EvaluateAsync(Arg.Any<ExitAuthorizationFiscalGatingShadowContext>(), Arg.Any<CancellationToken>())
            .Returns(FiscalGatingShadowEvaluation.FromGatingEvaluation(new FiscalIssuanceGatingEvaluation(
                IsReadyForNormalExitAuthorization: false,
                BlockedReason: "fiscal_issuance_pending",
                State: FiscalIssuanceIntegrationState.PendingFiscalIssuance,
                RequiresManualReview: false,
                IsExceptionReleaseOnly: false)));
        var sut = CreateSut(shadowEvaluator);

        var ex = await Assert.ThrowsAsync<ExitAuthorizationIssuanceConflictException>(() =>
            sut.ExecuteAsync(ValidCommand(), CancellationToken.None));

        Assert.Equal("fiscal_issuance_pending", ex.ErrorCode);
        await _eventPublisher.Received(1).PublishAsync(
            Arg.Is<IntegrationEventEnvelope>(x => ShadowObservationHasDecision(
                x,
                FiscalIssuanceExitAuthorizationEnforcementDecisions.Block,
                false,
                true,
                false,
                false,
                false,
                false)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenShadowFiscalGatingIsNotRequired_ShadowObservationIncludesNotRequiredDecision()
    {
        var now = new DateTimeOffset(2026, 4, 5, 10, 0, 0, TimeSpan.Zero);
        _systemClock.UtcNow.Returns(now);
        ConfigureGatewaySuccess(now);
        var shadowEvaluator = Substitute.For<IExitAuthorizationFiscalGatingShadowEvaluator>();
        shadowEvaluator
            .EvaluateAsync(Arg.Any<ExitAuthorizationFiscalGatingShadowContext>(), Arg.Any<CancellationToken>())
            .Returns(FiscalGatingShadowEvaluation.NotEvaluatedNotRequired());
        var sut = CreateSut(shadowEvaluator);

        var result = await sut.ExecuteAsync(ValidCommand(), CancellationToken.None);

        Assert.Equal("ISSUED", result.AuthorizationStatus);
        await _eventPublisher.Received(1).PublishAsync(
            Arg.Is<IntegrationEventEnvelope>(x => ShadowObservationHasDecision(
                x,
                FiscalIssuanceExitAuthorizationEnforcementDecisions.NotRequiredByPolicy,
                true,
                false,
                true,
                false,
                false,
                false)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenFiscalGatingIsExceptionReleaseOnly_BlocksAndRecordsDecision()
    {
        var now = new DateTimeOffset(2026, 4, 5, 10, 0, 0, TimeSpan.Zero);
        _systemClock.UtcNow.Returns(now);
        ConfigureGatewaySuccess(now);
        var shadowEvaluator = Substitute.For<IExitAuthorizationFiscalGatingShadowEvaluator>();
        shadowEvaluator
            .EvaluateAsync(Arg.Any<ExitAuthorizationFiscalGatingShadowContext>(), Arg.Any<CancellationToken>())
            .Returns(FiscalGatingShadowEvaluation.FromGatingEvaluation(new FiscalIssuanceGatingEvaluation(
                IsReadyForNormalExitAuthorization: false,
                BlockedReason: "fiscal_issuance_exception_release_only",
                State: FiscalIssuanceIntegrationState.FiscalIssuanceExceptionReleased,
                RequiresManualReview: false,
                IsExceptionReleaseOnly: true)));
        var sut = CreateSut(shadowEvaluator);

        var ex = await Assert.ThrowsAsync<ExitAuthorizationIssuanceConflictException>(() =>
            sut.ExecuteAsync(ValidCommand(), CancellationToken.None));

        Assert.Equal("fiscal_issuance_exception_release_only", ex.ErrorCode);
        await _eventPublisher.Received(1).PublishAsync(
            Arg.Is<IntegrationEventEnvelope>(x => ShadowObservationHasDecision(
                x,
                FiscalIssuanceExitAuthorizationEnforcementDecisions.ExceptionReleaseOnly,
                false,
                true,
                false,
                true,
                false,
                false)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenFiscalGatingRequiresManualReview_BlocksAndRecordsDecision()
    {
        var now = new DateTimeOffset(2026, 4, 5, 10, 0, 0, TimeSpan.Zero);
        _systemClock.UtcNow.Returns(now);
        ConfigureGatewaySuccess(now);
        var shadowEvaluator = Substitute.For<IExitAuthorizationFiscalGatingShadowEvaluator>();
        shadowEvaluator
            .EvaluateAsync(Arg.Any<ExitAuthorizationFiscalGatingShadowContext>(), Arg.Any<CancellationToken>())
            .Returns(FiscalGatingShadowEvaluation.FromGatingEvaluation(new FiscalIssuanceGatingEvaluation(
                IsReadyForNormalExitAuthorization: false,
                BlockedReason: "fiscal_issuance_manual_review",
                State: FiscalIssuanceIntegrationState.FiscalIssuanceManualReview,
                RequiresManualReview: true,
                IsExceptionReleaseOnly: false)));
        var sut = CreateSut(shadowEvaluator);

        var ex = await Assert.ThrowsAsync<ExitAuthorizationIssuanceConflictException>(() =>
            sut.ExecuteAsync(ValidCommand(), CancellationToken.None));

        Assert.Equal("fiscal_issuance_manual_review", ex.ErrorCode);
        await _eventPublisher.Received(1).PublishAsync(
            Arg.Is<IntegrationEventEnvelope>(x => ShadowObservationHasDecision(
                x,
                FiscalIssuanceExitAuthorizationEnforcementDecisions.ManualReviewRequired,
                false,
                true,
                false,
                false,
                true,
                false)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenShadowObservationPublicationFails_StillIssuesAuthorization()
    {
        var now = new DateTimeOffset(2026, 4, 5, 10, 0, 0, TimeSpan.Zero);
        _systemClock.UtcNow.Returns(now);
        ConfigureGatewaySuccess(now);
        _eventPublisher
            .PublishAsync(
                Arg.Is<IntegrationEventEnvelope>(x =>
                    x.EventType == IntegrationEventTypes.ExitAuthorizationFiscalGatingShadowObserved),
                Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("shadow observation unavailable"));

        var sut = CreateSut();

        var result = await sut.ExecuteAsync(ValidCommand(), CancellationToken.None);

        Assert.Equal("ISSUED", result.AuthorizationStatus);
        await _eventPublisher.Received(1).PublishAsync(
            Arg.Is<IntegrationEventEnvelope>(x => x.EventType == IntegrationEventTypes.ExitAuthorizationIssued),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShadowObservationPayload_ExcludesSensitiveRawPayloadFields()
    {
        var now = new DateTimeOffset(2026, 4, 5, 10, 0, 0, TimeSpan.Zero);
        _systemClock.UtcNow.Returns(now);
        ConfigureGatewaySuccess(now);
        var command = ValidCommand();
        var fiscalReference = CompleteFiscalReference(command, FiscalIssuanceIntegrationState.FiscalIssuanceRecorded);
        var repository = Substitute.For<IFiscalIssuanceReferenceRepository>();
        repository
            .FindLatestByPaymentAttemptIdAsync(command.PaymentAttemptId, Arg.Any<CancellationToken>())
            .Returns(fiscalReference);
        var sut = CreateSut(new ExitAuthorizationFiscalGatingShadowEvaluator(repository));

        await sut.ExecuteAsync(command, CancellationToken.None);

        var shadowEvent = _eventPublisher
            .ReceivedCalls()
            .Select(call => call.GetArguments().FirstOrDefault())
            .OfType<IntegrationEventEnvelope>()
            .Single(envelope => envelope.EventType == IntegrationEventTypes.ExitAuthorizationFiscalGatingShadowObserved);
        var serializedPayload = JsonSerializer.Serialize(shadowEvent.Payload).ToLowerInvariant();

        Assert.DoesNotContain("raw_payload", serializedPayload);
        Assert.DoesNotContain("callback_payload", serializedPayload);
        Assert.DoesNotContain("pan", serializedPayload);
        Assert.DoesNotContain("cvv", serializedPayload);
        Assert.DoesNotContain("secret", serializedPayload);
        Assert.DoesNotContain("token", serializedPayload);
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
        IExitAuthorizationFiscalGatingShadowEvaluator? fiscalGatingShadowEvaluator = null,
        bool isPaymentFinalityVerified = true,
        FiscalIssuanceExitAuthorizationGatingOptions? fiscalGatingOptions = null)
    {
        var effectiveFiscalGatingEvaluator = fiscalGatingShadowEvaluator ?? ReadyFiscalGatingEvaluator();
        var paymentFinalityReader = Substitute.For<IExitAuthorizationPaymentFinalityReadRepository>();
        paymentFinalityReader
            .IsPaymentFinalityVerifiedAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(isPaymentFinalityVerified);

        return new IssueExitAuthorizationHandler(
            _gateway,
            _eventPublisher,
            _systemClock,
            _metrics,
            NullLogger<IssueExitAuthorizationHandler>.Instance,
            effectiveFiscalGatingEvaluator,
            paymentFinalityReader,
            fiscalGatingOptions ?? new FiscalIssuanceExitAuthorizationGatingOptions());
    }

    private static IExitAuthorizationFiscalGatingShadowEvaluator ReadyFiscalGatingEvaluator()
    {
        var command = ValidCommand();
        var evaluator = Substitute.For<IExitAuthorizationFiscalGatingShadowEvaluator>();
        evaluator
            .EvaluateAsync(Arg.Any<ExitAuthorizationFiscalGatingShadowContext>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var context = call.Arg<ExitAuthorizationFiscalGatingShadowContext>();
                return FiscalGatingShadowEvaluation.FromGatingEvaluation(
                    FiscalIssuanceExitAuthorizationGateEvaluator.Evaluate(
                        CompleteFiscalReference(command, FiscalIssuanceIntegrationState.FiscalIssuanceRecorded) with
                        {
                            ParkingSessionId = context.ParkingSessionId,
                            PaymentAttemptId = context.PaymentAttemptId,
                            CorrelationId = context.CorrelationId
                        },
                        new FiscalIssuanceGatingEvaluationContext(context.IsPaymentFinalityVerified)),
                    CompleteFiscalReference(command, FiscalIssuanceIntegrationState.FiscalIssuanceRecorded) with
                    {
                        ParkingSessionId = context.ParkingSessionId,
                        PaymentAttemptId = context.PaymentAttemptId,
                        CorrelationId = context.CorrelationId
                    });
            });
        return evaluator;
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

    private static FiscalIssuanceReferenceRecord CompleteFiscalReference(
        IssueExitAuthorizationCommand command,
        FiscalIssuanceIntegrationState state) =>
        MinimalFiscalReference(command, state) with
        {
            PosServerFiscalDocumentId = Guid.NewGuid(),
            FiscalIdentityId = Guid.NewGuid(),
            FiscalSequencePolicyId = Guid.NewGuid(),
            FiscalSequenceValue = 101,
            FiscalDocumentNumber = "SI-000101",
            FiscalSeries = "SI",
            FiscalNumberPrefixText = "SI-",
            FiscalNumberAssignedAt = DateTimeOffset.Parse("2026-07-02T10:30:00+08:00"),
            FiscalNumberAssignedByRef = "pos-server",
            FiscalDocumentStatusCodeId = Guid.NewGuid(),
            ResultClassification = state == FiscalIssuanceIntegrationState.FiscalIssuanceReplayed
                ? FiscalIssuanceResultClassification.IdempotentReplay
                : FiscalIssuanceResultClassification.NewlyCreated,
            FiscalIssuanceEvidenceStatus = FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned,
            FiscalNumberAssignmentState = FiscalNumberAssignmentState.Assigned
        };

    private static FiscalIssuanceReferenceRecord MinimalFiscalReference(
        IssueExitAuthorizationCommand command,
        FiscalIssuanceIntegrationState state) =>
        new(
            FiscalIssuanceReferenceId: Guid.NewGuid(),
            PaymentConfirmationId: Guid.NewGuid(),
            PaymentAttemptId: command.PaymentAttemptId,
            ParkingSessionId: command.ParkingSessionId,
            TariffSnapshotId: Guid.NewGuid(),
            SiteId: Guid.NewGuid(),
            SitePosServerId: Guid.NewGuid(),
            SitePosServerRef: "site-pos-server-main",
            PayableBasisRef: "tariff-snapshot-ref",
            UpstreamFinalityReference: $"pay-final-{Guid.NewGuid():N}",
            PosServerFiscalDocumentId: null,
            FiscalIdentityId: null,
            FiscalSequencePolicyId: null,
            FiscalSequenceValue: null,
            FiscalDocumentNumber: null,
            FiscalSeries: null,
            FiscalNumberPrefixText: null,
            FiscalNumberSuffixText: null,
            FiscalNumberAssignedAt: null,
            FiscalNumberAssignedByRef: null,
            FiscalDocumentStatusCodeId: null,
            ResultClassification: null,
            FiscalIssuanceEvidenceStatus: null,
            FiscalNumberAssignmentState: FiscalNumberAssignmentState.NotAssigned,
            FiscalIssuanceState: state,
            LatestExceptionReason: null,
            LatestErrorCode: null,
            LatestErrorPosture: null,
            CorrelationId: command.CorrelationId,
            PosServerResponseTimestamp: null,
            FirstRecordedAt: DateTimeOffset.Parse("2026-07-02T10:30:01+08:00"),
            LastUpdatedAt: DateTimeOffset.Parse("2026-07-02T10:30:02+08:00"),
            RecordedByServiceIdentityId: command.RequestedByUserId);

    private static Activity AssertShadowActivity(ActivityCapture listener, string expectedStatus)
    {
        var activity = Assert.Single(
            listener.StoppedActivities,
            x => x.OperationName == "IssueExitAuthorization" &&
                HasTag(x, "fiscal_gating_shadow.status", expectedStatus));
        AssertTag(activity, "fiscal_gating_shadow.status", expectedStatus);
        return activity;
    }

    private static bool IsMissingContextShadowObservation(IntegrationEventEnvelope envelope)
    {
        var command = ValidCommand();
        var payload = GetShadowPayload(envelope);

        return payload is not null &&
            envelope.AggregateId == command.PaymentAttemptId.ToString() &&
            envelope.CorrelationId == command.CorrelationId &&
            payload.ShadowEvaluationStatus == FiscalGatingShadowEvaluationStatuses.NotEvaluatedMissingFiscalContext &&
            payload.PaymentAttemptId == command.PaymentAttemptId &&
            payload.ParkingSessionId == command.ParkingSessionId &&
            payload.PaymentConfirmationId == null &&
            payload.BlockedReason == "fiscal_reference_not_recorded" &&
            payload.EnforcementDecision == FiscalIssuanceExitAuthorizationEnforcementDecisions.Block &&
            payload.EnforcementEnabled &&
            payload.EnforcementWiredForBlocking &&
            !payload.IsNotEvaluable;
    }

    private static bool IsFailureShadowObservation(IntegrationEventEnvelope envelope, string blockedReason)
    {
        var payload = GetShadowPayload(envelope);

        return payload is not null &&
            payload.ShadowEvaluationStatus == FiscalGatingShadowEvaluationStatuses.EvaluationFailedNonBlocking &&
            payload.BlockedReason == blockedReason &&
            payload.EnforcementDecision == FiscalIssuanceExitAuthorizationEnforcementDecisions.NotEvaluable &&
            payload.IsNotEvaluable &&
            payload.EnforcementEnabled &&
            payload.EnforcementWiredForBlocking;
    }

    private static bool IsReadyShadowObservation(
        IntegrationEventEnvelope envelope,
        IssueExitAuthorizationCommand command,
        FiscalIssuanceReferenceRecord fiscalReference)
    {
        var payload = GetShadowPayload(envelope);

        return payload is not null &&
            envelope.AggregateId == command.PaymentAttemptId.ToString() &&
            envelope.AggregateType == "PaymentAttempt" &&
            envelope.CorrelationId == command.CorrelationId &&
            payload.ShadowEvaluationStatus == FiscalGatingShadowEvaluationStatuses.EvaluatedReady &&
            payload.EnforcementDecision == FiscalIssuanceExitAuthorizationEnforcementDecisions.Allow &&
            payload.WouldAllowNormalExitAuthorization &&
            !payload.WouldBlockNormalExitAuthorization &&
            payload.EnforcementEnabled &&
            payload.EnforcementWiredForBlocking &&
            payload.PaymentAttemptId == command.PaymentAttemptId &&
            payload.ParkingSessionId == command.ParkingSessionId &&
            payload.PaymentConfirmationId == fiscalReference.PaymentConfirmationId &&
            payload.FiscalIssuanceReferenceId == fiscalReference.FiscalIssuanceReferenceId &&
            payload.PosServerFiscalDocumentId == fiscalReference.PosServerFiscalDocumentId &&
            payload.FiscalDocumentNumber == fiscalReference.FiscalDocumentNumber &&
            payload.FiscalIssuanceState == fiscalReference.FiscalIssuanceState.ToString() &&
            payload.FiscalIssuanceEvidenceStatus == fiscalReference.FiscalIssuanceEvidenceStatus.ToString() &&
            payload.FiscalNumberAssignmentState == fiscalReference.FiscalNumberAssignmentState.ToString() &&
            payload.SiteId == fiscalReference.SiteId &&
            payload.SitePosServerId == fiscalReference.SitePosServerId &&
            payload.SitePosServerRef == fiscalReference.SitePosServerRef &&
            payload.Source == nameof(IssueExitAuthorizationHandler);
    }

    private static bool IsBlockedShadowObservation(
        IntegrationEventEnvelope envelope,
        string blockedReason,
        string fiscalIssuanceState)
    {
        var payload = GetShadowPayload(envelope);

        return payload is not null &&
            payload.ShadowEvaluationStatus == FiscalGatingShadowEvaluationStatuses.EvaluatedBlocked &&
            payload.BlockedReason == blockedReason &&
            payload.FiscalIssuanceState == fiscalIssuanceState;
    }

    private static bool IsFailureMetadataShadowObservation(IntegrationEventEnvelope envelope)
    {
        var payload = GetShadowPayload(envelope);

        return payload is not null &&
            payload.ShadowEvaluationStatus == FiscalGatingShadowEvaluationStatuses.EvaluatedBlocked &&
            payload.ExceptionReason == FiscalIssuanceExceptionReason.FiscalSequencePolicyNotFound.ToString() &&
            payload.ErrorPosture == FiscalIssuanceErrorPosture.RetryAfterConfigurationCorrection.ToString();
    }

    private static bool ShadowObservationHasDecision(
        IntegrationEventEnvelope envelope,
        string decision,
        bool wouldAllow,
        bool wouldBlock,
        bool isNotRequired,
        bool isExceptionReleaseOnly,
        bool requiresManualReview,
        bool isNotEvaluable)
    {
        var payload = GetShadowPayload(envelope);

        return payload is not null &&
            payload.EnforcementDecision == decision &&
            payload.WouldAllowNormalExitAuthorization == wouldAllow &&
            payload.WouldBlockNormalExitAuthorization == wouldBlock &&
            payload.IsNotRequiredByPolicy == isNotRequired &&
            payload.IsExceptionReleaseOnly == isExceptionReleaseOnly &&
            payload.RequiresManualReview == requiresManualReview &&
            payload.IsNotEvaluable == isNotEvaluable &&
            payload.EnforcementEnabled &&
            payload.EnforcementWiredForBlocking;
    }

    private static ExitAuthorizationFiscalGatingShadowObservedPayload? GetShadowPayload(IntegrationEventEnvelope envelope)
    {
        if (envelope.EventType != IntegrationEventTypes.ExitAuthorizationFiscalGatingShadowObserved)
        {
            return null;
        }

        return envelope.Payload as ExitAuthorizationFiscalGatingShadowObservedPayload;
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
