using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Infrastructure.Persistence.Routines;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Persistence;

/// <summary>
/// Verifies the create-or-reuse payment-attempt DB routine gateway against a seeded ExitPass v1.2 database.
///
/// BRD:
/// - 9.9 Payment Initiation
/// - 10.7.4 One Active Payment Attempt Per Session
///
/// SDD:
/// - 6.3 Initiate Payment Attempt
/// - 9.6 Integrity Constraints and Concurrency Rules
///
/// Invariants Enforced:
/// - First call creates a payment attempt.
/// - Same idempotency key reuses the authoritative payment attempt.
/// - Different idempotency key is rejected while an active attempt exists for the same session.
/// </summary>
public sealed class CreateOrReusePaymentAttemptDbRoutineGatewayTests
{
    /// <summary>
    /// Verifies BRD 9.9 and SDD 6.3 creation through the v1.2 DB routine gateway.
    /// </summary>
    [Fact]
    public async Task CreateOrReusePaymentAttemptAsync_returns_created_on_first_call()
    {
        var context = PaymentTestContext.Create(
            nameof(CreateOrReusePaymentAttemptAsync_returns_created_on_first_call));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            GetConnectionString(),
            context,
            "Seed data for create-or-reuse DB routine gateway tests");

        try
        {
        var gateway = CreateGateway();

        var result = await gateway.CreateOrReusePaymentAttemptAsync(
            new CreateOrReusePaymentAttemptDbRequest
            {
                ParkingSessionId = context.ParkingSessionId,
                TariffSnapshotId = context.TariffSnapshotId,
                PaymentProviderCode = "GCASH",
                IdempotencyKey = $"itest-idem-{Guid.NewGuid():N}",
                RequestedBy = "integration-test",
                CorrelationId = context.CorrelationId,
                RequestedAt = DateTimeOffset.UtcNow
            },
            CancellationToken.None);

        result.OutcomeCode.Should().Be("CREATED");
        result.WasReused.Should().BeFalse();
        result.AttemptStatus.Should().Be("REQUESTED");
        result.PaymentAttemptId.Should().NotBe(Guid.Empty);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(GetConnectionString(), context);
        }
    }

    /// <summary>
    /// Verifies BRD 10.7.4 and SDD 9.6 idempotent replay through the authoritative v1.2 routine.
    /// </summary>
    [Fact]
    public async Task CreateOrReusePaymentAttemptAsync_returns_reused_on_same_idempotency_key()
    {
        var context = PaymentTestContext.Create(
            nameof(CreateOrReusePaymentAttemptAsync_returns_reused_on_same_idempotency_key));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            GetConnectionString(),
            context,
            "Seed data for create-or-reuse DB routine gateway tests");

        try
        {
        var gateway = CreateGateway();
        var idempotencyKey = $"itest-idem-{Guid.NewGuid():N}";

        var first = await gateway.CreateOrReusePaymentAttemptAsync(
            new CreateOrReusePaymentAttemptDbRequest
            {
                ParkingSessionId = context.ParkingSessionId,
                TariffSnapshotId = context.TariffSnapshotId,
                PaymentProviderCode = "GCASH",
                IdempotencyKey = idempotencyKey,
                RequestedBy = "integration-test",
                CorrelationId = context.CorrelationId,
                RequestedAt = DateTimeOffset.UtcNow
            },
            CancellationToken.None);

        var second = await gateway.CreateOrReusePaymentAttemptAsync(
            new CreateOrReusePaymentAttemptDbRequest
            {
                ParkingSessionId = context.ParkingSessionId,
                TariffSnapshotId = context.TariffSnapshotId,
                PaymentProviderCode = "GCASH",
                IdempotencyKey = idempotencyKey,
                RequestedBy = "integration-test",
                CorrelationId = context.CorrelationId,
                RequestedAt = DateTimeOffset.UtcNow
            },
            CancellationToken.None);

        first.OutcomeCode.Should().Be("CREATED");
        second.OutcomeCode.Should().Be("REUSED_BY_IDEMPOTENCY_KEY");
        second.WasReused.Should().BeTrue();
        second.PaymentAttemptId.Should().Be(first.PaymentAttemptId);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(GetConnectionString(), context);
        }
    }

    /// <summary>
    /// Verifies BRD 10.7.4 and SDD 9.6 active-attempt rejection for competing idempotency keys.
    /// </summary>
    [Fact]
    public async Task CreateOrReusePaymentAttemptAsync_returns_active_attempt_exists_on_different_idempotency_key()
    {
        var context = PaymentTestContext.Create(
            nameof(CreateOrReusePaymentAttemptAsync_returns_active_attempt_exists_on_different_idempotency_key));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            GetConnectionString(),
            context,
            "Seed data for create-or-reuse DB routine gateway tests");

        try
        {
        var gateway = CreateGateway();

        var first = await gateway.CreateOrReusePaymentAttemptAsync(
            new CreateOrReusePaymentAttemptDbRequest
            {
                ParkingSessionId = context.ParkingSessionId,
                TariffSnapshotId = context.TariffSnapshotId,
                PaymentProviderCode = "GCASH",
                IdempotencyKey = $"itest-idem-{Guid.NewGuid():N}",
                RequestedBy = "integration-test",
                CorrelationId = context.CorrelationId,
                RequestedAt = DateTimeOffset.UtcNow
            },
            CancellationToken.None);

        var second = await gateway.CreateOrReusePaymentAttemptAsync(
            new CreateOrReusePaymentAttemptDbRequest
            {
                ParkingSessionId = context.ParkingSessionId,
                TariffSnapshotId = context.TariffSnapshotId,
                PaymentProviderCode = "GCASH",
                IdempotencyKey = $"itest-idem-{Guid.NewGuid():N}",
                RequestedBy = "integration-test",
                CorrelationId = context.CorrelationId,
                RequestedAt = DateTimeOffset.UtcNow
            },
            CancellationToken.None);

        first.OutcomeCode.Should().Be("CREATED");
        second.OutcomeCode.Should().Be("ACTIVE_ATTEMPT_EXISTS");
        second.FailureCode.Should().BeNull();
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(GetConnectionString(), context);
        }
    }

    private static IPaymentAttemptDbRoutineGateway CreateGateway()
    {
        return new PaymentAttemptDbRoutineGateway(GetConnectionString());
    }

    private static string GetConnectionString()
    {
        return CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();
    }
}
