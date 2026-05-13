using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Infrastructure.Persistence.Routines;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Persistence;

/// <summary>
/// Verifies create-or-reuse payment-attempt DB routine gateway behavior under concurrent calls.
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
/// - Only one fresh active payment attempt may be created for a parking session.
/// - Concurrent competing idempotency keys must deterministically reject after one create.
/// - Concurrent matching idempotency keys must resolve to one created attempt and one idempotent reuse.
/// </summary>
public sealed class CreateOrReusePaymentAttemptDbRoutineGatewayConcurrencyTests
{
    /// <summary>
    /// Verifies BRD 10.7.4 and SDD 9.6 under parallel competing idempotency keys.
    /// </summary>
    [Fact]
    public async Task CreateOrReusePaymentAttemptAsync_allows_only_one_created_under_parallel_race()
    {
        var connectionString = GetConnectionString();
        var context = PaymentTestContext.Create(
            nameof(CreateOrReusePaymentAttemptAsync_allows_only_one_created_under_parallel_race));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            connectionString,
            context,
            "Seed data for create-or-reuse DB routine gateway concurrency tests");

        try
        {
        var gateway1 = new PaymentAttemptDbRoutineGateway(connectionString);
        var gateway2 = new PaymentAttemptDbRoutineGateway(connectionString);
        var idempotencyKey1 = $"race-idem-{Guid.NewGuid():N}";
        var idempotencyKey2 = $"race-idem-{Guid.NewGuid():N}";

        var request1 = new CreateOrReusePaymentAttemptDbRequest
        {
            ParkingSessionId = context.ParkingSessionId,
            TariffSnapshotId = context.TariffSnapshotId,
            PaymentProviderCode = "GCASH",
            IdempotencyKey = idempotencyKey1,
            RequestedBy = "integration-race-test",
            CorrelationId = context.CorrelationId,
            RequestedAt = DateTimeOffset.UtcNow
        };

        var request2 = new CreateOrReusePaymentAttemptDbRequest
        {
            ParkingSessionId = context.ParkingSessionId,
            TariffSnapshotId = context.TariffSnapshotId,
            PaymentProviderCode = "GCASH",
            IdempotencyKey = idempotencyKey2,
            RequestedBy = "integration-race-test",
            CorrelationId = context.CorrelationId,
            RequestedAt = DateTimeOffset.UtcNow
        };

        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<CreateOrReusePaymentAttemptDbResult> RunAsync(
            IPaymentAttemptDbRoutineGateway gateway,
            CreateOrReusePaymentAttemptDbRequest request)
        {
            await startGate.Task;
            return await gateway.CreateOrReusePaymentAttemptAsync(request, CancellationToken.None);
        }

        var task1 = Task.Run(() => RunAsync(gateway1, request1));
        var task2 = Task.Run(() => RunAsync(gateway2, request2));

        startGate.SetResult();

        var results = await Task.WhenAll(task1, task2);

        results.Should().HaveCount(2);

        results.Count(r => r.OutcomeCode == "CREATED").Should().Be(1);
        results.Count(r => r.OutcomeCode == "ACTIVE_ATTEMPT_EXISTS").Should().Be(1);

        var created = results.Single(r => r.OutcomeCode == "CREATED");
        var rejected = results.Single(r => r.OutcomeCode == "ACTIVE_ATTEMPT_EXISTS");

        created.PaymentAttemptId.Should().NotBe(Guid.Empty);
        created.AttemptStatus.Should().Be("REQUESTED");
        created.WasReused.Should().BeFalse();

        rejected.FailureCode.Should().BeNull();
        rejected.PaymentAttemptId.Should().NotBe(Guid.Empty);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var paymentAttemptCount = await CountPaymentAttemptsForScenarioAsync(
            connection,
            context,
            request1.IdempotencyKey,
            request2.IdempotencyKey);
        paymentAttemptCount.Should().Be(1);

        var consumedAt = await GetConsumedAtAsync(connection, context);
        consumedAt.Should().NotBeNull();
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(connectionString, context);
        }
    }

    /// <summary>
    /// Verifies BRD 9.9 and SDD 6.3 idempotent replay under parallel matching idempotency keys.
    /// </summary>
    [Fact]
    public async Task CreateOrReusePaymentAttemptAsync_returns_one_created_and_one_reused_under_parallel_same_idempotency_key_race()
    {
        var connectionString = GetConnectionString();
        var context = PaymentTestContext.Create(
            nameof(CreateOrReusePaymentAttemptAsync_returns_one_created_and_one_reused_under_parallel_same_idempotency_key_race));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            connectionString,
            context,
            "Seed data for create-or-reuse DB routine gateway concurrency tests");

        try
        {
        var gateway1 = new PaymentAttemptDbRoutineGateway(connectionString);
        var gateway2 = new PaymentAttemptDbRoutineGateway(connectionString);

        var sharedIdempotencyKey = $"race-idem-shared-{Guid.NewGuid():N}";

        var request1 = new CreateOrReusePaymentAttemptDbRequest
        {
            ParkingSessionId = context.ParkingSessionId,
            TariffSnapshotId = context.TariffSnapshotId,
            PaymentProviderCode = "GCASH",
            IdempotencyKey = sharedIdempotencyKey,
            RequestedBy = "integration-race-test",
            CorrelationId = context.CorrelationId,
            RequestedAt = DateTimeOffset.UtcNow
        };

        var request2 = new CreateOrReusePaymentAttemptDbRequest
        {
            ParkingSessionId = context.ParkingSessionId,
            TariffSnapshotId = context.TariffSnapshotId,
            PaymentProviderCode = "GCASH",
            IdempotencyKey = sharedIdempotencyKey,
            RequestedBy = "integration-race-test",
            CorrelationId = context.CorrelationId,
            RequestedAt = DateTimeOffset.UtcNow
        };

        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<CreateOrReusePaymentAttemptDbResult> RunAsync(
            IPaymentAttemptDbRoutineGateway gateway,
            CreateOrReusePaymentAttemptDbRequest request)
        {
            await startGate.Task;
            return await gateway.CreateOrReusePaymentAttemptAsync(request, CancellationToken.None);
        }

        var task1 = Task.Run(() => RunAsync(gateway1, request1));
        var task2 = Task.Run(() => RunAsync(gateway2, request2));

        startGate.SetResult();

        var results = await Task.WhenAll(task1, task2);

        results.Should().HaveCount(2);
        results.Count(r => r.OutcomeCode == "CREATED").Should().Be(1);
        results.Count(r => r.OutcomeCode == "REUSED_BY_IDEMPOTENCY_KEY").Should().Be(1);

        var created = results.Single(r => r.OutcomeCode == "CREATED");
        var reused = results.Single(r => r.OutcomeCode == "REUSED_BY_IDEMPOTENCY_KEY");

        created.PaymentAttemptId.Should().NotBe(Guid.Empty);
        reused.PaymentAttemptId.Should().Be(created.PaymentAttemptId);
        reused.WasReused.Should().BeTrue();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var paymentAttemptCount = await CountPaymentAttemptsForScenarioAsync(connection, context, sharedIdempotencyKey);
        paymentAttemptCount.Should().Be(1);

        var consumedAt = await GetConsumedAtAsync(connection, context);
        consumedAt.Should().NotBeNull();
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(connectionString, context);
        }
    }

    private static string GetConnectionString()
    {
        return Environment.GetEnvironmentVariable("EXITPASS_TEST_MAIN_DB")
            ?? Environment.GetEnvironmentVariable("EXITPASS_TEST_DB_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("EXITPASS_INTEGRATION_DB")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__MainDatabase")
            ?? throw new InvalidOperationException(
                "Missing DB connection string. Set EXITPASS_TEST_MAIN_DB, EXITPASS_TEST_DB_CONNECTION_STRING, " +
                "EXITPASS_INTEGRATION_DB, or ConnectionStrings__MainDatabase.");
    }

    private static async Task<int> CountPaymentAttemptsForScenarioAsync(
        NpgsqlConnection connection,
        PaymentTestContext context,
        params string[] idempotencyKeys)
    {
        const string sql = """
            SELECT COUNT(*)
              FROM core.payment_attempts AS pa
             WHERE pa.parking_session_id = @parking_session_id
               AND pa.tariff_snapshot_id = @tariff_snapshot_id
               AND pa.idempotency_key = ANY(@idempotency_keys);
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("parking_session_id", context.ParkingSessionId);
        command.Parameters.AddWithValue("tariff_snapshot_id", context.TariffSnapshotId);
        command.Parameters.AddWithValue("idempotency_keys", idempotencyKeys);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private static async Task<DateTimeOffset?> GetConsumedAtAsync(
        NpgsqlConnection connection,
        PaymentTestContext context)
    {
        const string sql = """
            SELECT ts.consumed_at
              FROM core.tariff_snapshots AS ts
             WHERE ts.tariff_snapshot_id = @tariff_snapshot_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tariff_snapshot_id", context.TariffSnapshotId);

        var result = await command.ExecuteScalarAsync();

        if (result is null || result is DBNull)
        {
            return null;
        }

        return result switch
        {
            DateTimeOffset dateTimeOffset => dateTimeOffset,
            DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
            _ => throw new InvalidOperationException(
                $"Unexpected consumed_at value type '{result.GetType().FullName}'.")
        };
    }
}
