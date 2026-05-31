using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Infrastructure.Payments;
using ExitPass.CentralPms.IntegrationTests.Shared;
using Npgsql;
using Xunit;
using static ExitPass.CentralPms.IntegrationTests.Shared.PaymentRoutineTestHelper;

namespace ExitPass.CentralPms.IntegrationTests.Payments;

/// <summary>
/// Verifies DB-backed terminal-state behavior for finalize_payment_attempt().
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 10.7.2 Payment Finality Invariant
/// - 10.7.10 Idempotent Payment Confirmation Invariant
///
/// SDD:
/// - 6.4 Finalize Payment
/// - 8.3 PaymentAttempt State Machine
/// - 9.6 Integrity Constraints and Concurrency Rules
///
/// Invariants Enforced:
/// - Only Central PMS may finalize PaymentAttempt state
/// - A terminal PaymentAttempt must not transition again
/// - A confirmed PaymentAttempt must not be re-finalized to FAILED
/// </summary>
public sealed class FinalizePaymentAttemptIntegrationTests
{
    private static string ConnectionString =>
        CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();

    /// <summary>
    /// Verifies that Central PMS finalizes an initiated payment attempt to confirmed after verified provider finality.
    /// </summary>
    [Fact]
    public async Task FinalizePaymentAttempt_WhenAttemptIsInitiated_TransitionsToConfirmed()
    {
        var context = PaymentTestContext.Create(
            nameof(FinalizePaymentAttempt_WhenAttemptIsInitiated_TransitionsToConfirmed));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for finalize-payment tests");

        try
        {
            var created = await CreateAttemptAsync(
                ConnectionString,
                context,
                "idem-finalize-success",
                "finalize-test");

            var finalized = await FinalizeAttemptAsync(
                ConnectionString,
                created.PaymentAttemptId,
                "CONFIRMED",
                "central-pms-finalizer",
                context.CorrelationId);

            Assert.NotNull(finalized);
            Assert.Equal(created.PaymentAttemptId, finalized!.PaymentAttemptId);
            Assert.Equal("CONFIRMED", finalized.AttemptStatus);

            var row = await GetPaymentAttemptAsync(ConnectionString, created.PaymentAttemptId);
            Assert.NotNull(row);
            Assert.Equal("CONFIRMED", row!.AttemptStatus);
            Assert.NotNull(row.FinalizedAt);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that a confirmed payment attempt cannot transition to another terminal status.
    /// </summary>
    [Fact]
    public async Task FinalizePaymentAttempt_WhenAttemptAlreadyConfirmed_DoesNotTransitionAgain()
    {
        var context = PaymentTestContext.Create(
            nameof(FinalizePaymentAttempt_WhenAttemptAlreadyConfirmed_DoesNotTransitionAgain));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for finalize-payment tests");

        try
        {
            var created = await CreateAttemptAsync(
                ConnectionString,
                context,
                "idem-finalize-terminal",
                "finalize-test");

            var firstFinalize = await FinalizeAttemptAsync(
                ConnectionString,
                created.PaymentAttemptId,
                "CONFIRMED",
                "central-pms-finalizer",
                context.CorrelationId);

            Assert.NotNull(firstFinalize);
            Assert.Equal("CONFIRMED", firstFinalize!.AttemptStatus);

            var ex = await Assert.ThrowsAnyAsync<PostgresException>(async () =>
            {
                await FinalizeAttemptAsync(
                    ConnectionString,
                    created.PaymentAttemptId,
                    "FAILED",
                    "central-pms-finalizer",
                    context.CorrelationId);
            });

            Assert.False(string.IsNullOrWhiteSpace(ex.SqlState));

            var row = await GetPaymentAttemptAsync(ConnectionString, created.PaymentAttemptId);
            Assert.NotNull(row);
            Assert.Equal("CONFIRMED", row!.AttemptStatus);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that a failed payment attempt cannot later become confirmed for exit authorization.
    /// </summary>
    [Fact]
    public async Task FinalizePaymentAttempt_WhenAttemptAlreadyFailed_DoesNotTransitionToConfirmed()
    {
        var context = PaymentTestContext.Create(
            nameof(FinalizePaymentAttempt_WhenAttemptAlreadyFailed_DoesNotTransitionToConfirmed));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for finalize-payment tests");

        try
        {
            var created = await CreateAttemptAsync(
                ConnectionString,
                context,
                "idem-finalize-failed-first",
                "finalize-test");

            var firstFinalize = await FinalizeAttemptAsync(
                ConnectionString,
                created.PaymentAttemptId,
                "FAILED",
                "central-pms-finalizer",
                context.CorrelationId);

            Assert.NotNull(firstFinalize);
            Assert.Equal("FAILED", firstFinalize!.AttemptStatus);

            var ex = await Assert.ThrowsAnyAsync<PostgresException>(async () =>
            {
                await FinalizeAttemptAsync(
                    ConnectionString,
                    created.PaymentAttemptId,
                    "CONFIRMED",
                    "central-pms-finalizer",
                    context.CorrelationId);
            });

            Assert.False(string.IsNullOrWhiteSpace(ex.SqlState));

            var row = await GetPaymentAttemptAsync(ConnectionString, created.PaymentAttemptId);
            Assert.NotNull(row);
            Assert.Equal("FAILED", row!.AttemptStatus);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies ExitPass v1.2 BRD 9.13, BRD 10.7.2, SDD 6.4, and SDD 9.6 by enforcing
    /// the invariant that same-terminal provider outcome retries return the existing finalized state.
    /// </summary>
    [Fact]
    public async Task FinalizePaymentAttempt_WhenSameTerminalStatusIsReplayed_IsIdempotent()
    {
        var context = PaymentTestContext.Create(
            nameof(FinalizePaymentAttempt_WhenSameTerminalStatusIsReplayed_IsIdempotent));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for finalize-payment tests");

        try
        {
            var created = await CreateAttemptAsync(
                ConnectionString,
                context,
                "idem-finalize-idempotent",
                "finalize-test");

            var firstFinalize = await FinalizeAttemptAsync(
                ConnectionString,
                created.PaymentAttemptId,
                "CONFIRMED",
                "central-pms-finalizer",
                context.CorrelationId);

            var replayFinalize = await FinalizeAttemptAsync(
                ConnectionString,
                created.PaymentAttemptId,
                "CONFIRMED",
                "central-pms-finalizer",
                context.CorrelationId);

            Assert.NotNull(firstFinalize);
            Assert.NotNull(replayFinalize);
            Assert.Equal(firstFinalize!.PaymentAttemptId, replayFinalize!.PaymentAttemptId);
            Assert.Equal("CONFIRMED", replayFinalize.AttemptStatus);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task FinalizePaymentAttemptGateway_WhenTariffSnapshotWasConsumed_FinalizesAgainstAttemptSnapshot()
    {
        var context = PaymentTestContext.Create(
            nameof(FinalizePaymentAttemptGateway_WhenTariffSnapshotWasConsumed_FinalizesAgainstAttemptSnapshot));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for consumed tariff finality tests");

        try
        {
            var created = await CreateAttemptAsync(
                ConnectionString,
                context,
                "idem-finalize-consumed-snapshot",
                "finalize-test");

            Assert.Equal("CONSUMED", await ReadTariffSnapshotStatusAsync(created.TariffSnapshotId));

            var gateway = new FinalizePaymentAttemptGateway(ConnectionString);
            var finalized = await gateway.FinalizeAsync(
                new FinalizePaymentAttemptDbRequest
                {
                    PaymentAttemptId = created.PaymentAttemptId,
                    FinalAttemptStatus = "CONFIRMED",
                    RequestedBy = "central-pms-finalizer",
                    CorrelationId = context.CorrelationId,
                    RequestedAt = DateTimeOffset.UtcNow
                },
                CancellationToken.None);

            var row = await GetPaymentAttemptAsync(ConnectionString, created.PaymentAttemptId);

            Assert.Equal(created.PaymentAttemptId, finalized.PaymentAttemptId);
            Assert.Equal("CONFIRMED", finalized.AttemptStatus);
            Assert.NotNull(row);
            Assert.Equal(created.TariffSnapshotId, row!.TariffSnapshotId);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task FinalizePaymentAttemptGateway_WhenAttemptAmountDriftsFromTariffSnapshot_RejectsFinality()
    {
        var context = PaymentTestContext.Create(
            nameof(FinalizePaymentAttemptGateway_WhenAttemptAmountDriftsFromTariffSnapshot_RejectsFinality));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for finality amount drift tests");

        try
        {
            var created = await CreateAttemptAsync(
                ConnectionString,
                context,
                "idem-finalize-amount-drift",
                "finalize-test");

            await ForcePaymentAttemptAmountAsync(created.PaymentAttemptId, 99.99m);

            var gateway = new FinalizePaymentAttemptGateway(ConnectionString);
            var ex = await Assert.ThrowsAsync<PaymentFinalityConflictException>(() =>
                gateway.FinalizeAsync(
                    new FinalizePaymentAttemptDbRequest
                    {
                        PaymentAttemptId = created.PaymentAttemptId,
                        FinalAttemptStatus = "CONFIRMED",
                        RequestedBy = "central-pms-finalizer",
                        CorrelationId = context.CorrelationId,
                        RequestedAt = DateTimeOffset.UtcNow
                    },
                    CancellationToken.None));

            var row = await GetPaymentAttemptAsync(ConnectionString, created.PaymentAttemptId);

            Assert.Equal("PAYMENT_AMOUNT_MISMATCH", ex.ErrorCode);
            Assert.NotNull(row);
            Assert.Equal("REQUESTED", row!.AttemptStatus);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    private static async Task<string> ReadTariffSnapshotStatusAsync(Guid tariffSnapshotId)
    {
        const string sql = """
            SELECT snapshot_status::text
            FROM core.tariff_snapshots
            WHERE tariff_snapshot_id = @tariff_snapshot_id;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tariff_snapshot_id", tariffSnapshotId);

        return (string)(await command.ExecuteScalarAsync() ?? string.Empty);
    }

    private static async Task ForcePaymentAttemptAmountAsync(Guid paymentAttemptId, decimal amount)
    {
        const string sql = """
            UPDATE core.payment_attempts
            SET amount = @amount,
                updated_at = NOW(),
                row_version = row_version + 1
            WHERE payment_attempt_id = @payment_attempt_id;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("payment_attempt_id", paymentAttemptId);
        command.Parameters.AddWithValue("amount", amount);
        await command.ExecuteNonQueryAsync();
    }
}
