using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Infrastructure.Persistence.Routines;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Persistence;

public sealed class CreateOrReusePaymentAttemptDbRoutineGatewayTests
{
    /*
      BRD:
      - 9.9 Payment Initiation
      - 10.7.4 One Active Payment Attempt Per Session

      SDD:
      - 6.3 Initiate Payment Attempt
      - 9.6 Integrity Constraints and Concurrency Rules

      Invariants to prove in integration tests:
      - first call creates a payment attempt
      - second call with same idempotency key reuses the original attempt
      - second call with different idempotency key rejects with active attempt exists
    */

    [Fact(Skip = "Requires seeded PostgreSQL integration environment.")]
    public async Task CreateOrReusePaymentAttemptAsync_returns_created_on_first_call()
    {
        var gateway = CreateGateway();

        var result = await gateway.CreateOrReusePaymentAttemptAsync(
            new CreateOrReusePaymentAttemptDbRequest
            {
                ParkingSessionId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                TariffSnapshotId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                PaymentProviderCode = "GCASH",
                IdempotencyKey = "itest-idem-001",
                RequestedBy = "integration-test",
                CorrelationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                RequestedAt = DateTimeOffset.UtcNow
            },
            CancellationToken.None);

        result.OutcomeCode.Should().Be("CREATED");
        result.WasReused.Should().BeFalse();
        result.AttemptStatus.Should().Be("INITIATED");
        result.PaymentAttemptId.Should().NotBe(Guid.Empty);
    }

    [Fact(Skip = "Requires seeded PostgreSQL integration environment.")]
    public async Task CreateOrReusePaymentAttemptAsync_returns_reused_on_same_idempotency_key()
    {
        var gateway = CreateGateway();

        var first = await gateway.CreateOrReusePaymentAttemptAsync(
            new CreateOrReusePaymentAttemptDbRequest
            {
                ParkingSessionId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                TariffSnapshotId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                PaymentProviderCode = "GCASH",
                IdempotencyKey = "itest-idem-002",
                RequestedBy = "integration-test",
                CorrelationId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                RequestedAt = DateTimeOffset.UtcNow
            },
            CancellationToken.None);

        var second = await gateway.CreateOrReusePaymentAttemptAsync(
            new CreateOrReusePaymentAttemptDbRequest
            {
                ParkingSessionId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                TariffSnapshotId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                PaymentProviderCode = "GCASH",
                IdempotencyKey = "itest-idem-002",
                RequestedBy = "integration-test",
                CorrelationId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                RequestedAt = DateTimeOffset.UtcNow
            },
            CancellationToken.None);

        first.OutcomeCode.Should().Be("CREATED");
        second.OutcomeCode.Should().Be("REUSED");
        second.WasReused.Should().BeTrue();
        second.PaymentAttemptId.Should().Be(first.PaymentAttemptId);
    }

    [Fact(Skip = "Requires seeded PostgreSQL integration environment.")]
    public async Task CreateOrReusePaymentAttemptAsync_returns_active_attempt_exists_on_different_idempotency_key()
    {
        var gateway = CreateGateway();

        var first = await gateway.CreateOrReusePaymentAttemptAsync(
            new CreateOrReusePaymentAttemptDbRequest
            {
                ParkingSessionId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                TariffSnapshotId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                PaymentProviderCode = "GCASH",
                IdempotencyKey = "itest-idem-003a",
                RequestedBy = "integration-test",
                CorrelationId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                RequestedAt = DateTimeOffset.UtcNow
            },
            CancellationToken.None);

        var second = await gateway.CreateOrReusePaymentAttemptAsync(
            new CreateOrReusePaymentAttemptDbRequest
            {
                ParkingSessionId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                TariffSnapshotId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                PaymentProviderCode = "GCASH",
                IdempotencyKey = "itest-idem-003b",
                RequestedBy = "integration-test",
                CorrelationId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                RequestedAt = DateTimeOffset.UtcNow
            },
            CancellationToken.None);

        first.OutcomeCode.Should().Be("CREATED");
        second.OutcomeCode.Should().Be("REJECTED_ACTIVE_ATTEMPT_EXISTS");
        second.FailureCode.Should().Be("ACTIVE_PAYMENT_ATTEMPT_EXISTS");
    }

    private static IPaymentAttemptDbRoutineGateway CreateGateway()
    {
        var connectionString = Environment.GetEnvironmentVariable("EXITPASS_TEST_MAIN_DB")
            ?? throw new InvalidOperationException("EXITPASS_TEST_MAIN_DB environment variable is missing.");

        return new PaymentAttemptDbRoutineGateway(connectionString);
    }
}
