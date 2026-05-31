using ExitPass.CentralPms.Application.Observability;
using ExitPass.CentralPms.Application.Eventing;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Domain.Common;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Unit tests for <see cref="ConsumeExitAuthorizationHandler"/>.
///
/// BRD:
/// - 9.12 Exit Authorization
/// - 10.7.7 Exit Token Integrity Invariant
///
/// SDD:
/// - 6.6 Consume Exit Authorization
/// - 8.5 ExitAuthorization State Machine
///
/// Invariants Enforced:
/// - ExitAuthorization consumption validates required identifiers before DB execution
/// - The handler maps the DB-authoritative consume result without mutation
/// - Observability dependencies must not affect business behavior under test
/// </summary>
public sealed class ConsumeExitAuthorizationHandlerTests
{
    private readonly IConsumeExitAuthorizationGateway _gateway = Substitute.For<IConsumeExitAuthorizationGateway>();
    private readonly IIntegrationEventPublisher _eventPublisher = Substitute.For<IIntegrationEventPublisher>();
    private readonly ISystemClock _systemClock = Substitute.For<ISystemClock>();
    private readonly CentralPmsMetrics _metrics = new();

    /// <summary>
    /// Verifies that a valid consume command returns the DB-authoritative mapped result.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenCommandIsValid_ReturnsMappedResult()
    {
        var now = new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero);
        _systemClock.UtcNow.Returns(now);

        var exitAuthorizationId = Guid.NewGuid();
        var gateAuthorizationConsumptionId = Guid.NewGuid();
        var parkingSessionId = Guid.NewGuid();
        var paymentAttemptId = Guid.NewGuid();
        var tariffSnapshotId = Guid.NewGuid();
        var gateDeviceId = Guid.NewGuid();
        var gateDeviceIdentifier = "GATE-EXIT-01";
        var laneId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var vendorSystemId = Guid.NewGuid();
        var requestedByUserId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        _gateway.ConsumeAsync(
                Arg.Is<ConsumeExitAuthorizationDbRequest>(x =>
                    x.ExitAuthorizationId == exitAuthorizationId &&
                    x.RequestedByUserId == requestedByUserId &&
                    x.CorrelationId == correlationId &&
                    x.RequestedAt == now &&
                    x.GateDeviceId == gateDeviceId &&
                    x.GateDeviceIdentifier == gateDeviceIdentifier &&
                    x.LaneId == laneId &&
                    x.SiteId == siteId),
                Arg.Any<CancellationToken>())
            .Returns(new ConsumeExitAuthorizationDbResult(
                ExitAuthorizationId: exitAuthorizationId,
                AuthorizationStatus: "CONSUMED",
                ConsumedAt: now,
                GateAuthorizationConsumptionId: gateAuthorizationConsumptionId,
                ParkingSessionId: parkingSessionId,
                PaymentAttemptId: paymentAttemptId,
                TariffSnapshotId: tariffSnapshotId,
                GateDeviceId: gateDeviceId,
                GateDeviceIdentifier: gateDeviceIdentifier,
                LaneId: laneId,
                SiteId: siteId,
                VendorSystemId: vendorSystemId));

        var sut = CreateSut();

        var result = await sut.ExecuteAsync(
            new ConsumeExitAuthorizationCommand(
                ExitAuthorizationId: exitAuthorizationId,
                RequestedByUserId: requestedByUserId,
                CorrelationId: correlationId,
                GateDeviceId: gateDeviceId,
                GateDeviceIdentifier: gateDeviceIdentifier,
                LaneId: laneId,
                SiteId: siteId),
            CancellationToken.None);

        Assert.Equal(exitAuthorizationId, result.ExitAuthorizationId);
        Assert.Equal("CONSUMED", result.AuthorizationStatus);
        Assert.Equal(now, result.ConsumedAt);

        await _eventPublisher.Received(1).PublishAsync(
            Arg.Is<IntegrationEventEnvelope>(x =>
                x.EventType == IntegrationEventTypes.GateAuthorizationConsumed &&
                x.AggregateId == exitAuthorizationId.ToString() &&
                x.CorrelationId == correlationId &&
                PayloadMatches(
                    x.Payload,
                    exitAuthorizationId,
                    gateAuthorizationConsumptionId,
                    parkingSessionId,
                    paymentAttemptId,
                    tariffSnapshotId,
                    gateDeviceId,
                    gateDeviceIdentifier,
                    laneId,
                    siteId,
                    vendorSystemId,
                    now,
                    correlationId)),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies RabbitMQ/event-publisher failure cannot undo or block DB-authoritative gate consumption.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenEventPublishingFails_ReturnsMappedResult()
    {
        var now = new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero);
        _systemClock.UtcNow.Returns(now);

        var exitAuthorizationId = Guid.NewGuid();
        var requestedByUserId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        _gateway.ConsumeAsync(Arg.Any<ConsumeExitAuthorizationDbRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ConsumeExitAuthorizationDbResult(
                ExitAuthorizationId: exitAuthorizationId,
                AuthorizationStatus: "CONSUMED",
                ConsumedAt: now));
        _eventPublisher
            .PublishAsync(Arg.Any<IntegrationEventEnvelope>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("RabbitMQ unavailable"));

        var sut = CreateSut();

        var result = await sut.ExecuteAsync(
            new ConsumeExitAuthorizationCommand(
                ExitAuthorizationId: exitAuthorizationId,
                RequestedByUserId: requestedByUserId,
                CorrelationId: correlationId),
            CancellationToken.None);

        Assert.Equal(exitAuthorizationId, result.ExitAuthorizationId);
        Assert.Equal("CONSUMED", result.AuthorizationStatus);
    }

    /// <summary>
    /// Verifies that rejected consume attempts do not emit the gate-integration handoff event.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenGatewayRejectsConsume_DoesNotPublishGateAuthorizationConsumed()
    {
        var now = new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero);
        _systemClock.UtcNow.Returns(now);

        _gateway.ConsumeAsync(Arg.Any<ConsumeExitAuthorizationDbRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<ConsumeExitAuthorizationDbResult>>(_ =>
                throw new ExitAuthorizationConsumeConflictException(
                    "EXIT_AUTHORIZATION_ALREADY_CONSUMED",
                    "Exit authorization has already been consumed."));

        var sut = CreateSut();

        await Assert.ThrowsAsync<ExitAuthorizationConsumeConflictException>(() =>
            sut.ExecuteAsync(
                new ConsumeExitAuthorizationCommand(
                    ExitAuthorizationId: Guid.NewGuid(),
                    RequestedByUserId: Guid.NewGuid(),
                    CorrelationId: Guid.NewGuid()),
                CancellationToken.None));

        await _eventPublisher.DidNotReceive().PublishAsync(
            Arg.Is<IntegrationEventEnvelope>(x =>
                x.EventType == IntegrationEventTypes.GateAuthorizationConsumed),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that an empty exit authorization identifier is rejected before DB execution.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenExitAuthorizationIdIsEmpty_Throws()
    {
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.ExecuteAsync(
                new ConsumeExitAuthorizationCommand(
                    ExitAuthorizationId: Guid.Empty,
                    RequestedByUserId: Guid.NewGuid(),
                    CorrelationId: Guid.NewGuid()),
                CancellationToken.None));

        Assert.Contains("ExitAuthorizationId", ex.Message);
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
                new ConsumeExitAuthorizationCommand(
                    ExitAuthorizationId: Guid.NewGuid(),
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
                new ConsumeExitAuthorizationCommand(
                    ExitAuthorizationId: Guid.NewGuid(),
                    RequestedByUserId: Guid.NewGuid(),
                    CorrelationId: Guid.Empty),
                CancellationToken.None));

        Assert.Contains("CorrelationId", ex.Message);
    }

    /// <summary>
    /// Creates the system under test with no-op logging and shared metrics dependencies.
    /// </summary>
    /// <returns>A configured <see cref="ConsumeExitAuthorizationHandler"/> instance.</returns>
    private ConsumeExitAuthorizationHandler CreateSut()
    {
        return new ConsumeExitAuthorizationHandler(
            _gateway,
            _eventPublisher,
            _systemClock,
            _metrics,
            NullLogger<ConsumeExitAuthorizationHandler>.Instance);
    }

    private static bool PayloadMatches(
        object payloadObject,
        Guid exitAuthorizationId,
        Guid gateAuthorizationConsumptionId,
        Guid parkingSessionId,
        Guid paymentAttemptId,
        Guid tariffSnapshotId,
        Guid gateDeviceId,
        string gateDeviceIdentifier,
        Guid laneId,
        Guid siteId,
        Guid vendorSystemId,
        DateTimeOffset consumedAt,
        Guid correlationId)
    {
        if (payloadObject is not GateAuthorizationConsumedPayload payload)
        {
            return false;
        }

        return payload.ExitAuthorizationId == exitAuthorizationId &&
               payload.GateAuthorizationConsumptionId == gateAuthorizationConsumptionId &&
               payload.ParkingSessionId == parkingSessionId &&
               payload.PaymentAttemptId == paymentAttemptId &&
               payload.TariffSnapshotId == tariffSnapshotId &&
               payload.GateDeviceId == gateDeviceId &&
               payload.GateDeviceIdentifier == gateDeviceIdentifier &&
               payload.LaneId == laneId &&
               payload.SiteId == siteId &&
               payload.VendorSystemId == vendorSystemId &&
               payload.AuthorizationStatus == "CONSUMED" &&
               payload.ConsumedAtUtc == consumedAt &&
               payload.CorrelationId == correlationId;
    }
}
