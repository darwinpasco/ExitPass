using ExitPass.CentralPms.IntegrationTests.Shared;
using Npgsql;
using System.Data;
using System.Diagnostics;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Payments;

/// <summary>
/// Verifies DB-backed concurrency behavior for create_or_reuse_payment_attempt().
///
/// BRD:
/// - 9.9 Payment Initiation
/// - 10.7.4 One Active Payment Attempt Per Session
/// - 18.11 Payment Attempt Integrity
///
/// SDD:
/// - 6.3 Initiate Payment Attempt
/// - 8.3 PaymentAttempt State Machine
/// - 9.6 Integrity Constraints and Concurrency Rules
///
/// Invariants Enforced:
/// - Only one active PaymentAttempt may exist per ParkingSession
/// - TariffSnapshot is the immutable financial basis for the surviving PaymentAttempt
/// - Concurrent callers must not produce duplicate active attempts
/// </summary>
public sealed class CreateOrReusePaymentAttemptConcurrencyTests
{
    private const string ConnectionStringEnvVar = "EXITPASS_INTEGRATION_DB";

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionStringEnvVar)
        ?? throw new InvalidOperationException(
            $"Missing environment variable '{ConnectionStringEnvVar}'. " +
            "Point it at the ExitPass integration database.");

    [Fact]
    public async Task CreateOrReusePaymentAttempt_WhenCalledConcurrently_AllowsOnlyOneActiveAttempt_AndSecondCallerWaits()
    {
        var context = PaymentTestContext.Create(
            nameof(CreateOrReusePaymentAttempt_WhenCalledConcurrently_AllowsOnlyOneActiveAttempt_AndSecondCallerWaits));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for payment-attempt race tests");

        try
        {
            var sessionAReadyToLetBStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            PaymentAttemptFunctionResult? sessionAResult = null;
            PaymentAttemptFunctionResult? sessionBResult = null;

            var sessionAElapsed = TimeSpan.Zero;
            var sessionBElapsed = TimeSpan.Zero;

            var sessionATask = Task.Run(async () =>
            {
                await using var connection = new NpgsqlConnection(ConnectionString);
                await connection.OpenAsync();

                await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);

                var stopwatch = Stopwatch.StartNew();

                sessionAResult = await CallCreateOrReusePaymentAttemptAsync(
                    connection,
                    transaction,
                    context,
                    idempotencyKey: "idem-race-a",
                    requestedBy: "race-test-session-a",
                    correlationId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

                sessionAReadyToLetBStart.TrySetResult();

                await ExecuteScalarAsync(connection, transaction, "SELECT pg_sleep(2.5);");
                await transaction.CommitAsync();

                stopwatch.Stop();
                sessionAElapsed = stopwatch.Elapsed;
            });

            var sessionBTask = Task.Run(async () =>
            {
                await sessionAReadyToLetBStart.Task;

                await using var connection = new NpgsqlConnection(ConnectionString);
                await connection.OpenAsync();

                await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);

                var stopwatch = Stopwatch.StartNew();

                sessionBResult = await CallCreateOrReusePaymentAttemptAsync(
                    connection,
                    transaction,
                    context,
                    idempotencyKey: "idem-race-b",
                    requestedBy: "race-test-session-b",
                    correlationId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

                await transaction.CommitAsync();

                stopwatch.Stop();
                sessionBElapsed = stopwatch.Elapsed;
            });

            await Task.WhenAll(sessionATask, sessionBTask);

            var attempts = await GetPaymentAttemptsForSessionAsync(context);
            var activeAttemptCount = CountActiveAttempts(attempts);

            Assert.NotNull(sessionAResult);

            Assert.Single(attempts);
            Assert.Equal(1, activeAttemptCount);

            Assert.Equal(context.ParkingSessionId, attempts[0].ParkingSessionId);
            Assert.Equal(context.TariffSnapshotId, attempts[0].TariffSnapshotId);
            Assert.Equal("GCASH", attempts[0].PaymentProviderCode);
            Assert.Equal("INITIATED", attempts[0].AttemptStatus);
            Assert.Equal("idem-race-a", attempts[0].IdempotencyKey);

            Assert.True(
                sessionBElapsed >= TimeSpan.FromSeconds(1.5),
                $"Expected Session B to wait on lock contention, but it completed in {sessionBElapsed.TotalMilliseconds:N0} ms.");

            if (sessionBResult is not null)
            {
                Assert.Equal(attempts[0].PaymentAttemptId, sessionAResult!.PaymentAttemptId);
                Assert.Equal(attempts[0].PaymentAttemptId, sessionBResult.PaymentAttemptId);
            }
            else
            {
                Assert.Equal(attempts[0].PaymentAttemptId, sessionAResult!.PaymentAttemptId);
            }
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact(Skip = "Enable this after the function contract is locked to true create-or-reuse semantics for both callers.")]
    public async Task CreateOrReusePaymentAttempt_WhenCalledConcurrently_ReturnsSameAttemptIdToBothCallers()
    {
        var context = PaymentTestContext.Create(
            nameof(CreateOrReusePaymentAttempt_WhenCalledConcurrently_ReturnsSameAttemptIdToBothCallers));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for payment-attempt race tests");

        try
        {
            var sessionAReadyToLetBStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            PaymentAttemptFunctionResult? sessionAResult = null;
            PaymentAttemptFunctionResult? sessionBResult = null;

            var sessionATask = Task.Run(async () =>
            {
                await using var connection = new NpgsqlConnection(ConnectionString);
                await connection.OpenAsync();

                await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);

                sessionAResult = await CallCreateOrReusePaymentAttemptAsync(
                    connection,
                    transaction,
                    context,
                    idempotencyKey: "idem-race-a",
                    requestedBy: "race-test-session-a",
                    correlationId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

                sessionAReadyToLetBStart.TrySetResult();

                await ExecuteScalarAsync(connection, transaction, "SELECT pg_sleep(2.5);");
                await transaction.CommitAsync();
            });

            var sessionBTask = Task.Run(async () =>
            {
                await sessionAReadyToLetBStart.Task;

                await using var connection = new NpgsqlConnection(ConnectionString);
                await connection.OpenAsync();

                await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);

                sessionBResult = await CallCreateOrReusePaymentAttemptAsync(
                    connection,
                    transaction,
                    context,
                    idempotencyKey: "idem-race-b",
                    requestedBy: "race-test-session-b",
                    correlationId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

                await transaction.CommitAsync();
            });

            await Task.WhenAll(sessionATask, sessionBTask);

            Assert.NotNull(sessionAResult);
            Assert.NotNull(sessionBResult);
            Assert.Equal(sessionAResult!.PaymentAttemptId, sessionBResult!.PaymentAttemptId);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Calls the DB routine under test.
    ///
    /// BRD:
    /// - 9.9 Payment Initiation
    ///
    /// SDD:
    /// - 6.3 Initiate Payment Attempt
    ///
    /// Invariants Enforced:
    /// - PaymentAttempt creation remains scoped to one ParkingSession and one TariffSnapshot
    /// </summary>
    private static async Task<PaymentAttemptFunctionResult?> CallCreateOrReusePaymentAttemptAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PaymentTestContext context,
        string idempotencyKey,
        string requestedBy,
        Guid correlationId)
    {
        const string sql = """
            SELECT
                payment_attempt_id,
                parking_session_id,
                tariff_snapshot_id,
                attempt_status,
                payment_provider_code,
                was_reused
            FROM core.create_or_reuse_payment_attempt(
                @p_parking_session_id,
                @p_tariff_snapshot_id,
                @p_payment_provider_code,
                @p_idempotency_key,
                @p_requested_by,
                @p_correlation_id,
                @p_now
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction)
        {
            CommandTimeout = 30
        };

        command.Parameters.AddWithValue("p_parking_session_id", context.ParkingSessionId);
        command.Parameters.AddWithValue("p_tariff_snapshot_id", context.TariffSnapshotId);
        command.Parameters.AddWithValue("p_payment_provider_code", "GCASH");
        command.Parameters.AddWithValue("p_idempotency_key", idempotencyKey);
        command.Parameters.AddWithValue("p_requested_by", requestedBy);
        command.Parameters.AddWithValue("p_correlation_id", correlationId);
        command.Parameters.AddWithValue("p_now", DateTimeOffset.UtcNow);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new PaymentAttemptFunctionResult(
            PaymentAttemptId: reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
            ParkingSessionId: reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            TariffSnapshotId: reader.GetGuid(reader.GetOrdinal("tariff_snapshot_id")),
            AttemptStatus: reader.GetString(reader.GetOrdinal("attempt_status")),
            PaymentProviderCode: reader.GetString(reader.GetOrdinal("payment_provider_code")),
            WasReused: ReadBooleanIfPresent(reader, "was_reused"));
    }

    private static bool? ReadBooleanIfPresent(IDataRecord record, string columnName)
    {
        for (var i = 0; i < record.FieldCount; i++)
        {
            if (!string.Equals(record.GetName(i), columnName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (record.IsDBNull(i))
            {
                return null;
            }

            return record.GetBoolean(i);
        }

        return null;
    }

    private static async Task ExecuteScalarAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction)
        {
            CommandTimeout = 30
        };

        _ = await command.ExecuteScalarAsync();
    }

    private static async Task<List<PaymentAttemptRow>> GetPaymentAttemptsForSessionAsync(PaymentTestContext context)
    {
        const string sql = """
            SELECT
                payment_attempt_id,
                parking_session_id,
                tariff_snapshot_id,
                payment_provider_code,
                idempotency_key,
                attempt_status,
                created_at,
                updated_at
            FROM core.payment_attempts
            WHERE parking_session_id = @parking_session_id
            ORDER BY created_at;
            """;

        var rows = new List<PaymentAttemptRow>();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };
        command.Parameters.AddWithValue("parking_session_id", context.ParkingSessionId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new PaymentAttemptRow(
                PaymentAttemptId: reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
                ParkingSessionId: reader.GetGuid(reader.GetOrdinal("parking_session_id")),
                TariffSnapshotId: reader.GetGuid(reader.GetOrdinal("tariff_snapshot_id")),
                PaymentProviderCode: reader.GetString(reader.GetOrdinal("payment_provider_code")),
                IdempotencyKey: reader.GetString(reader.GetOrdinal("idempotency_key")),
                AttemptStatus: reader.GetString(reader.GetOrdinal("attempt_status")),
                CreatedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
                UpdatedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at"))));
        }

        return rows;
    }

    private static int CountActiveAttempts(IEnumerable<PaymentAttemptRow> attempts)
    {
        var count = 0;

        foreach (var attempt in attempts)
        {
            if (attempt.AttemptStatus is "INITIATED" or "PENDING_PROVIDER")
            {
                count++;
            }
        }

        return count;
    }

    private sealed record PaymentAttemptFunctionResult(
        Guid PaymentAttemptId,
        Guid ParkingSessionId,
        Guid TariffSnapshotId,
        string AttemptStatus,
        string PaymentProviderCode,
        bool? WasReused);

    private sealed record PaymentAttemptRow(
        Guid PaymentAttemptId,
        Guid ParkingSessionId,
        Guid TariffSnapshotId,
        string PaymentProviderCode,
        string IdempotencyKey,
        string AttemptStatus,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
