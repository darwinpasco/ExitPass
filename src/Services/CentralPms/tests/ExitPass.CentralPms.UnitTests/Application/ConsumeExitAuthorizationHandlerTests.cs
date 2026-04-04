using ExitPass.CentralPms.Application.Observability;
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

        var sut = CreateSut();

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
            _systemClock,
            _metrics,
            NullLogger<ConsumeExitAuthorizationHandler>.Instance);
    }
}
