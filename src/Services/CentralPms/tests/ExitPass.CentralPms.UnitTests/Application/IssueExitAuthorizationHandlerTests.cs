using ExitPass.CentralPms.Application.Observability;
using ExitPass.CentralPms.Application.Payments;
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
    private IssueExitAuthorizationHandler CreateSut()
    {
        return new IssueExitAuthorizationHandler(
            _gateway,
            _systemClock,
            _metrics,
            NullLogger<IssueExitAuthorizationHandler>.Instance);
    }
}
