using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Infrastructure.Persistence.Routines;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Persistence;

public sealed class CreateOrReusePaymentAttemptDbRoutineGatewayConcurrencyTests
{
    private static readonly Guid ParkingSessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TariffSnapshotId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /*
      BRD:
      - 9.9 Payment Initiation
      - 10.7.4 One Active Payment Attempt Per Session

      SDD:
      - 6.3 Initiate Payment Attempt
      - 9.6 Integrity Constraints and Concurrency Rules

      Invariants proved:
      - only one fresh payment attempt may be created for a session at a time
      - concurrent competing requests must not both create attempts
      - row locking and DB-backed orchestration must deterministically resolve the race
    */

    [Fact(Skip = "Requires seeded PostgreSQL integration environment.")]
    public async Task CreateOrReusePaymentAttemptAsync_allows_only_one_created_under_parallel_race()
    {
        var connectionString = GetConnectionString();

        await ResetScenarioStateAsync(connectionString);

        var gateway1 = new PaymentAttemptDbRoutineGateway(connectionString);
        var gateway2 = new PaymentAttemptDbRoutineGateway(connectionString);

        var request1 = new CreateOrReusePaymentAttemptDbRequest
        {
            ParkingSessionId = ParkingSessionId,
            TariffSnapshotId = TariffSnapshotId,
            PaymentProviderCode = "GCASH",
            IdempotencyKey = "race-idem-001",
            RequestedBy = "integration-race-test",
            CorrelationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1"),
            RequestedAt = DateTimeOffset.UtcNow
        };

        var request2 = new CreateOrReusePaymentAttemptDbRequest
        {
            ParkingSessionId = ParkingSessionId,
            TariffSnapshotId = TariffSnapshotId,
            PaymentProviderCode = "GCASH",
            IdempotencyKey = "race-idem-002",
            RequestedBy = "integration-race-test",
            CorrelationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2"),
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
        results.Count(r => r.OutcomeCode == "REJECTED_ACTIVE_ATTEMPT_EXISTS").Should().Be(1);

        var created = results.Single(r => r.OutcomeCode == "CREATED");
        var rejected = results.Single(r => r.OutcomeCode == "REJECTED_ACTIVE_ATTEMPT_EXISTS");

        created.PaymentAttemptId.Should().NotBe(Guid.Empty);
        created.AttemptStatus.Should().Be("INITIATED");
        created.WasReused.Should().BeFalse();

        rejected.FailureCode.Should().Be("ACTIVE_PAYMENT_ATTEMPT_EXISTS");
        rejected.PaymentAttemptId.Should().NotBe(Guid.Empty);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var paymentAttemptCount = await CountPaymentAttemptsForScenarioAsync(connection, request1.IdempotencyKey, request2.IdempotencyKey);
        paymentAttemptCount.Should().Be(1);

        var consumedByPaymentAttemptId = await GetConsumedByPaymentAttemptIdAsync(connection);
        consumedByPaymentAttemptId.Should().Be(created.PaymentAttemptId);
    }

    [Fact(Skip = "Requires seeded PostgreSQL integration environment.")]
    public async Task CreateOrReusePaymentAttemptAsync_returns_one_created_and_one_reused_under_parallel_same_idempotency_key_race()
    {
        var connectionString = GetConnectionString();

        await ResetScenarioStateAsync(connectionString);

        var gateway1 = new PaymentAttemptDbRoutineGateway(connectionString);
        var gateway2 = new PaymentAttemptDbRoutineGateway(connectionString);

        var sharedIdempotencyKey = "race-idem-shared-001";

        var request1 = new CreateOrReusePaymentAttemptDbRequest
        {
            ParkingSessionId = ParkingSessionId,
            TariffSnapshotId = TariffSnapshotId,
            PaymentProviderCode = "GCASH",
            IdempotencyKey = sharedIdempotencyKey,
            RequestedBy = "integration-race-test",
            CorrelationId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1"),
            RequestedAt = DateTimeOffset.UtcNow
        };

        var request2 = new CreateOrReusePaymentAttemptDbRequest
        {
            ParkingSessionId = ParkingSessionId,
            TariffSnapshotId = TariffSnapshotId,
            PaymentProviderCode = "GCASH",
            IdempotencyKey = sharedIdempotencyKey,
            RequestedBy = "integration-race-test",
            CorrelationId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2"),
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
        results.Count(r => r.OutcomeCode == "REUSED").Should().Be(1);

        var created = results.Single(r => r.OutcomeCode == "CREATED");
        var reused = results.Single(r => r.OutcomeCode == "REUSED");

        created.PaymentAttemptId.Should().NotBe(Guid.Empty);
        reused.PaymentAttemptId.Should().Be(created.PaymentAttemptId);
        reused.WasReused.Should().BeTrue();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var paymentAttemptCount = await CountPaymentAttemptsForScenarioAsync(connection, sharedIdempotencyKey);
        paymentAttemptCount.Should().Be(1);

        var consumedByPaymentAttemptId = await GetConsumedByPaymentAttemptIdAsync(connection);
        consumedByPaymentAttemptId.Should().Be(created.PaymentAttemptId);
    }

    private static string GetConnectionString()
    {
        return Environment.GetEnvironmentVariable("EXITPASS_TEST_MAIN_DB")
            ?? throw new InvalidOperationException("EXITPASS_TEST_MAIN_DB environment variable is missing.");
    }

    private static async Task ResetScenarioStateAsync(string connectionString)
    {
        const string sql = """
            UPDATE core.tariff_snapshots AS ts
               SET consumed_by_payment_attempt_id = NULL,
                   updated_at = NOW(),
                   updated_by = 'integration-race-reset',
                   row_version = ts.row_version + 1
             WHERE ts.tariff_snapshot_id = @tariff_snapshot_id;

            DELETE FROM core.payment_attempts AS pa
             WHERE pa.tariff_snapshot_id = @tariff_snapshot_id
                OR pa.parking_session_id = @parking_session_id;
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tariff_snapshot_id", TariffSnapshotId);
        command.Parameters.AddWithValue("parking_session_id", ParkingSessionId);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountPaymentAttemptsForScenarioAsync(NpgsqlConnection connection, params string[] idempotencyKeys)
    {
        const string sql = """
            SELECT COUNT(*)
              FROM core.payment_attempts AS pa
             WHERE pa.parking_session_id = @parking_session_id
               AND pa.tariff_snapshot_id = @tariff_snapshot_id
               AND pa.idempotency_key = ANY(@idempotency_keys);
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("parking_session_id", ParkingSessionId);
        command.Parameters.AddWithValue("tariff_snapshot_id", TariffSnapshotId);
        command.Parameters.AddWithValue("idempotency_keys", idempotencyKeys);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private static async Task<Guid?> GetConsumedByPaymentAttemptIdAsync(NpgsqlConnection connection)
    {
        const string sql = """
            SELECT ts.consumed_by_payment_attempt_id
              FROM core.tariff_snapshots AS ts
             WHERE ts.tariff_snapshot_id = @tariff_snapshot_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tariff_snapshot_id", TariffSnapshotId);

        var result = await command.ExecuteScalarAsync();

        if (result is null || result is DBNull)
        {
            return null;
        }

        return (Guid)result;
    }
}
