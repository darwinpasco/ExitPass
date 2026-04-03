using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Domain.Common;
using FluentAssertions.Equivalency;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
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
/// </summary>
public sealed class ConsumeExitAuthorizationHandlerTests
{
    private readonly IConsumeExitAuthorizationGateway _gateway = Substitute.For<IConsumeExitAuthorizationGateway>();
    private readonly ISystemClock _systemClock = Substitute.For<ISystemClock>();

    [Fact]
    public async Task ExecuteAsync_WhenCommandIsValid_ReturnsMappedResult()
    {
        var now = new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero);
        _systemClock.UtcNow.Returns(now);

        var exitAuthorizationId = Guid.NewGuid();
        var requestedByUserId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        _gateway.ConsumeAsync(
                Arg.Is<ConsumeExitAuthorizationDbRequest>(x =>
                    x.ExitAuthorizationId == exitAuthorizationId &&
                    x.RequestedByUserId == requestedByUserId &&
                    x.CorrelationId == correlationId &&
                    x.RequestedAt == now),
                Arg.Any<CancellationToken>())
            .Returns(new ConsumeExitAuthorizationDbResult(
                ExitAuthorizationId: exitAuthorizationId,
                AuthorizationStatus: "CONSUMED",
                ConsumedAt: now));

        var sut = new ConsumeExitAuthorizationHandler(
                        _gateway,_systemClock, NullLogger<ConsumeExitAuthorizationHandler>.Instance);

        var result = await sut.ExecuteAsync(
            new ConsumeExitAuthorizationCommand(
                ExitAuthorizationId: exitAuthorizationId,
                RequestedByUserId: requestedByUserId,
                CorrelationId: correlationId),
            CancellationToken.None);

        Assert.Equal(exitAuthorizationId, result.ExitAuthorizationId);
        Assert.Equal("CONSUMED", result.AuthorizationStatus);
        Assert.Equal(now, result.ConsumedAt);
    }

    [Fact]
    public async Task ExecuteAsync_WhenExitAuthorizationIdIsEmpty_Throws()
    {
        var sut = new ConsumeExitAuthorizationHandler(
                        _gateway, _systemClock, NullLogger<ConsumeExitAuthorizationHandler>.Instance);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.ExecuteAsync(
                new ConsumeExitAuthorizationCommand(
                    ExitAuthorizationId: Guid.Empty,
                    RequestedByUserId: Guid.NewGuid(),
                    CorrelationId: Guid.NewGuid()),
                CancellationToken.None));

        Assert.Contains("ExitAuthorizationId", ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRequestedByUserIdIsEmpty_Throws()
    {
        var sut = new ConsumeExitAuthorizationHandler(
                        _gateway, _systemClock, NullLogger<ConsumeExitAuthorizationHandler>.Instance);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.ExecuteAsync(
                new ConsumeExitAuthorizationCommand(
                    ExitAuthorizationId: Guid.NewGuid(),
                    RequestedByUserId: Guid.Empty,
                    CorrelationId: Guid.NewGuid()),
                CancellationToken.None));

        Assert.Contains("RequestedByUserId", ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCorrelationIdIsEmpty_Throws()
    {
        var sut = new ConsumeExitAuthorizationHandler(
                        _gateway, _systemClock, NullLogger<ConsumeExitAuthorizationHandler>.Instance);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.ExecuteAsync(
                new ConsumeExitAuthorizationCommand(
                    ExitAuthorizationId: Guid.NewGuid(),
                    RequestedByUserId: Guid.NewGuid(),
                    CorrelationId: Guid.Empty),
                CancellationToken.None));

        Assert.Contains("CorrelationId", ex.Message);
    }
}
