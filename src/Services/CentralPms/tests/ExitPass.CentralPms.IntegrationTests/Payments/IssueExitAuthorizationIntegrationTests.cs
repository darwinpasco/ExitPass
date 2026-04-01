using System;
using System.Threading.Tasks;
using ExitPass.CentralPms.IntegrationTests.Shared;
using Npgsql;
using Xunit;
using static ExitPass.CentralPms.IntegrationTests.Shared.PaymentRoutineTestHelper;

namespace ExitPass.CentralPms.IntegrationTests.Payments;

/// <summary>
/// Verifies DB-backed issuance rules for issue_exit_authorization().
///
/// BRD:
/// - 9.12 Exit Authorization
/// - 10.7.2 Payment Finality Invariant
/// - 10.7.7 Exit Token Integrity Invariant
///
/// SDD:
/// - 6.5 Issue Exit Authorization
/// - 8.5 ExitAuthorization State Machine
/// - 9.6 Integrity Constraints and Concurrency Rules
///
/// Invariants Enforced:
/// - ExitAuthorization may only be issued after confirmed payment finality
/// - ExitAuthorization must not be issued from non-confirmed attempts
/// - ExitAuthorization issuance must be deterministic for the same confirmed attempt
/// </summary>
public sealed class IssueExitAuthorizationIntegrationTests
{
    private const string ConnectionStringEnvVar = "EXITPASS_INTEGRATION_DB";

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionStringEnvVar)
        ?? throw new InvalidOperationException(
            $"Missing environment variable '{ConnectionStringEnvVar}'. " +
            "Point it at the ExitPass integration database.");

    [Fact]
    public async Task IssueExitAuthorization_WhenPaymentAttemptIsConfirmed_IssuesAuthorization()
    {
        var context = PaymentTestContext.Create(
            nameof(IssueExitAuthorization_WhenPaymentAttemptIsConfirmed_IssuesAuthorization));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for issue-exit-authorization tests");

        try
        {
            var attempt = await CreateAttemptAsync(
                ConnectionString,
                context,
                "idem-issue-auth-success",
                "issue-auth-test");

            await FinalizeAttemptAsync(
                ConnectionString,
                attempt.PaymentAttemptId,
                "CONFIRMED",
                "central-pms-finalizer",
                context.CorrelationId);

            var authorization = await IssueExitAuthorizationAsync(
                ConnectionString,
                attempt.ParkingSessionId,
                attempt.PaymentAttemptId,
                context.RequestedByUserId,
                context.CorrelationId);

            Assert.NotNull(authorization);
            Assert.Equal(attempt.ParkingSessionId, authorization!.ParkingSessionId);
            Assert.Equal(attempt.PaymentAttemptId, authorization.PaymentAttemptId);
            Assert.Equal("ISSUED", authorization.AuthorizationStatus);
            Assert.False(string.IsNullOrWhiteSpace(authorization.AuthorizationToken));
            Assert.True(authorization.ExpirationTimestamp > authorization.IssuedAt);

            var persisted = await GetExitAuthorizationByIdAsync(ConnectionString, authorization.ExitAuthorizationId);
            Assert.NotNull(persisted);
            Assert.Equal("ISSUED", persisted!.AuthorizationStatus);
            Assert.Equal(attempt.PaymentAttemptId, persisted.PaymentAttemptId);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task IssueExitAuthorization_WhenPaymentAttemptIsNotConfirmed_RejectsIssuance()
    {
        var context = PaymentTestContext.Create(
            nameof(IssueExitAuthorization_WhenPaymentAttemptIsNotConfirmed_RejectsIssuance));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for issue-exit-authorization tests");

        try
        {
            var attempt = await CreateAttemptAsync(
                ConnectionString,
                context,
                "idem-issue-auth-initiated",
                "issue-auth-test");

            var ex = await Assert.ThrowsAnyAsync<PostgresException>(async () =>
            {
                await IssueExitAuthorizationAsync(
                    ConnectionString,
                    attempt.ParkingSessionId,
                    attempt.PaymentAttemptId,
                    context.RequestedByUserId,
                    context.CorrelationId);
            });

            Assert.False(string.IsNullOrWhiteSpace(ex.SqlState));
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task IssueExitAuthorization_WhenPaymentAttemptHasFailed_RejectsIssuance()
    {
        var context = PaymentTestContext.Create(
            nameof(IssueExitAuthorization_WhenPaymentAttemptHasFailed_RejectsIssuance));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for issue-exit-authorization tests");

        try
        {
            var attempt = await CreateAttemptAsync(
                ConnectionString,
                context,
                "idem-issue-auth-failed",
                "issue-auth-test");

            await FinalizeAttemptAsync(
                ConnectionString,
                attempt.PaymentAttemptId,
                "FAILED",
                "central-pms-finalizer",
                context.CorrelationId);

            var ex = await Assert.ThrowsAnyAsync<PostgresException>(async () =>
            {
                await IssueExitAuthorizationAsync(
                    ConnectionString,
                    attempt.ParkingSessionId,
                    attempt.PaymentAttemptId,
                    context.RequestedByUserId,
                    context.CorrelationId);
            });

            Assert.False(string.IsNullOrWhiteSpace(ex.SqlState));
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact(Skip = "Enable after issue_exit_authorization() contract is locked for same-attempt replay behavior.")]
    public async Task IssueExitAuthorization_WhenReplayedForSameConfirmedAttempt_IsDeterministic()
    {
        var context = PaymentTestContext.Create(
            nameof(IssueExitAuthorization_WhenReplayedForSameConfirmedAttempt_IsDeterministic));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for issue-exit-authorization tests");

        try
        {
            var attempt = await CreateAttemptAsync(
                ConnectionString,
                context,
                "idem-issue-auth-replay",
                "issue-auth-test");

            await FinalizeAttemptAsync(
                ConnectionString,
                attempt.PaymentAttemptId,
                "CONFIRMED",
                "central-pms-finalizer",
                context.CorrelationId);

            var first = await IssueExitAuthorizationAsync(
                ConnectionString,
                attempt.ParkingSessionId,
                attempt.PaymentAttemptId,
                context.RequestedByUserId,
                context.CorrelationId);

            var second = await IssueExitAuthorizationAsync(
                ConnectionString,
                attempt.ParkingSessionId,
                attempt.PaymentAttemptId,
                context.RequestedByUserId,
                context.CorrelationId);

            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.Equal(first!.ExitAuthorizationId, second!.ExitAuthorizationId);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }
}
