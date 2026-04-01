using ExitPass.CentralPms.IntegrationTests.Shared;
using Npgsql;
using Xunit;
using static ExitPass.CentralPms.IntegrationTests.Shared.PaymentRoutineTestHelper;

namespace ExitPass.CentralPms.IntegrationTests.Payments;

/// <summary>
/// Verifies DB-backed consume rules for consume_exit_authorization().
///
/// BRD:
/// - 9.12 Exit Authorization
/// - 10.7.7 Exit Token Integrity Invariant
/// - 10.7.8 Single-Use Consume Invariant
///
/// SDD:
/// - 6.6 Consume Exit Authorization
/// - 8.5 ExitAuthorization State Machine
/// - 9.6 Integrity Constraints and Concurrency Rules
///
/// Invariants Enforced:
/// - ExitAuthorization may be consumed only once
/// - Replay of a consumed authorization must fail closed
/// - Expired authorization must not be consumed
/// </summary>
public sealed class ConsumeExitAuthorizationIntegrationTests
{
    private const string ConnectionStringEnvVar = "EXITPASS_INTEGRATION_DB";

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionStringEnvVar)
        ?? throw new InvalidOperationException(
            $"Missing environment variable '{ConnectionStringEnvVar}'. " +
            "Point it at the ExitPass integration database.");

    [Fact]
    public async Task ConsumeExitAuthorization_WhenAuthorizationIsIssued_ConsumesSuccessfully()
    {
        var context = PaymentTestContext.Create(
            nameof(ConsumeExitAuthorization_WhenAuthorizationIsIssued_ConsumesSuccessfully));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for consume-exit-authorization tests");

        try
        {
            var attempt = await CreateAttemptAsync(
                ConnectionString,
                context,
                "idem-consume-success",
                "consume-auth-test");

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

            var consumed = await ConsumeExitAuthorizationAsync(
                ConnectionString,
                authorization!.ExitAuthorizationId,
                context.RequestedByUserId,
                context.CorrelationId);

            Assert.NotNull(consumed);
            Assert.Equal(authorization.ExitAuthorizationId, consumed!.ExitAuthorizationId);
            Assert.Equal("CONSUMED", consumed.AuthorizationStatus);
            Assert.NotNull(consumed.ConsumedAt);

            var persisted = await GetExitAuthorizationByIdAsync(ConnectionString, authorization.ExitAuthorizationId);
            Assert.NotNull(persisted);
            Assert.Equal("CONSUMED", persisted!.AuthorizationStatus);
            Assert.NotNull(persisted.ConsumedAt);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task ConsumeExitAuthorization_WhenAuthorizationAlreadyConsumed_RejectsReplay()
    {
        var context = PaymentTestContext.Create(
            nameof(ConsumeExitAuthorization_WhenAuthorizationAlreadyConsumed_RejectsReplay));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for consume-exit-authorization tests");

        try
        {
            var attempt = await CreateAttemptAsync(
                ConnectionString,
                context,
                "idem-consume-replay",
                "consume-auth-test");

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

            var firstConsume = await ConsumeExitAuthorizationAsync(
                ConnectionString,
                authorization!.ExitAuthorizationId,
                context.RequestedByUserId,
                context.CorrelationId);

            Assert.NotNull(firstConsume);
            Assert.Equal("CONSUMED", firstConsume!.AuthorizationStatus);

            var ex = await Assert.ThrowsAnyAsync<PostgresException>(async () =>
            {
                await ConsumeExitAuthorizationAsync(
                    ConnectionString,
                    authorization.ExitAuthorizationId,
                    context.RequestedByUserId,
                    context.CorrelationId);
            });

            Assert.False(string.IsNullOrWhiteSpace(ex.SqlState));

            var persisted = await GetExitAuthorizationByIdAsync(ConnectionString, authorization.ExitAuthorizationId);
            Assert.NotNull(persisted);
            Assert.Equal("CONSUMED", persisted!.AuthorizationStatus);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task ConsumeExitAuthorization_WhenAuthorizationExpired_RejectsConsume()
    {
        var context = PaymentTestContext.Create(
            nameof(ConsumeExitAuthorization_WhenAuthorizationExpired_RejectsConsume));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for consume-exit-authorization tests");

        try
        {
            var attempt = await CreateAttemptAsync(
                ConnectionString,
                context,
                "idem-consume-expired",
                "consume-auth-test");

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

            await ExpireAuthorizationAsync(
                ConnectionString,
                authorization!.ExitAuthorizationId,
                context.RequestedByUserId);

            var ex = await Assert.ThrowsAnyAsync<PostgresException>(async () =>
            {
                await ConsumeExitAuthorizationAsync(
                    ConnectionString,
                    authorization.ExitAuthorizationId,
                    context.RequestedByUserId,
                    context.CorrelationId);
            });

            Assert.False(string.IsNullOrWhiteSpace(ex.SqlState));

            var persisted = await GetExitAuthorizationByIdAsync(ConnectionString, authorization.ExitAuthorizationId);
            Assert.NotNull(persisted);
            Assert.Equal("ISSUED", persisted!.AuthorizationStatus);
            Assert.Null(persisted.ConsumedAt);
            Assert.True(persisted.ExpirationTimestamp <= DateTimeOffset.UtcNow);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task ConsumeExitAuthorization_WhenAuthorizationIsInvalid_RejectsConsume()
    {
        var context = PaymentTestContext.Create(
            nameof(ConsumeExitAuthorization_WhenAuthorizationIsInvalid_RejectsConsume));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for consume-exit-authorization tests");

        try
        {
            var ex = await Assert.ThrowsAnyAsync<PostgresException>(async () =>
            {
                await ConsumeExitAuthorizationAsync(
                    ConnectionString,
                    Guid.NewGuid(),
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
}
