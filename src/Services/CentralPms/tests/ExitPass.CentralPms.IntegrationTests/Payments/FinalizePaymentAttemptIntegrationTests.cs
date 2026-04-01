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
    private const string ConnectionStringEnvVar = "EXITPASS_INTEGRATION_DB";

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionStringEnvVar)
        ?? throw new InvalidOperationException(
            $"Missing environment variable '{ConnectionStringEnvVar}'. " +
            "Point it at the ExitPass integration database.");

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

    [Fact(Skip = "Enable after finalize_payment_attempt() contract is locked to idempotent same-status replay behavior.")]
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
}
