using ExitPass.CentralPms.IntegrationTests.Shared;
using Npgsql;
using Xunit;
using static ExitPass.CentralPms.IntegrationTests.Shared.PaymentRoutineTestHelper;

namespace ExitPass.CentralPms.IntegrationTests.Payments;

/// <summary>
/// Verifies DB-backed consume rules for <c>core.consume_exit_authorization(...)</c>.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.12 Exit Authorization
/// - 10.7.7 Exit Token Integrity Invariant
/// - 10.7.8 Single-Use Consume Invariant
///
/// SDD:
/// - 6.4 Finalize Payment
/// - 6.5 Issue Exit Authorization
/// - 6.6 Consume Exit Authorization
/// - 8.5 ExitAuthorization State Machine
/// - 9.6 Integrity Constraints and Concurrency Rules
///
/// Invariants Enforced:
/// - ExitAuthorization may be consumed only once.
/// - Replay of a consumed authorization must fail closed.
/// - Expired authorization must not be consumed.
/// - ExitAuthorization issuance requires recorded payment confirmation evidence.
/// </summary>
public sealed class ConsumeExitAuthorizationIntegrationTests
{
    private const string ConnectionStringEnvVar = "EXITPASS_INTEGRATION_DB";

    /// <summary>
    /// Gets the integration database connection string from the environment.
    /// </summary>
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionStringEnvVar)
        ?? throw new InvalidOperationException(
            $"Missing environment variable '{ConnectionStringEnvVar}'. Point it at the ExitPass integration database.");

    /// <summary>
    /// Verifies that an issued authorization can be consumed successfully.
    /// </summary>
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
            var authorization = await CreateConfirmedIssuedAuthorizationAsync(
                context,
                "idem-consume-success");

            var consumed = await ConsumeExitAuthorizationAsync(
                ConnectionString,
                authorization.ExitAuthorizationId,
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

    /// <summary>
    /// Verifies that replaying consume for an already consumed authorization fails closed.
    /// </summary>
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
            var authorization = await CreateConfirmedIssuedAuthorizationAsync(
                context,
                "idem-consume-replay");

            var firstConsume = await ConsumeExitAuthorizationAsync(
                ConnectionString,
                authorization.ExitAuthorizationId,
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

    /// <summary>
    /// Verifies that an expired authorization cannot be consumed.
    /// </summary>
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
            var authorization = await CreateConfirmedIssuedAuthorizationAsync(
                context,
                "idem-consume-expired");

            await ExpireAuthorizationAsync(
                ConnectionString,
                authorization.ExitAuthorizationId,
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

    /// <summary>
    /// Verifies that a random invalid authorization identifier is rejected.
    /// </summary>
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

    /// <summary>
    /// Creates a confirmed payment attempt with recorded payment confirmation and a successfully issued exit authorization.
    /// </summary>
    /// <param name="context">Per-test canonical data context.</param>
    /// <param name="idempotencyKey">Idempotency key to use for payment-attempt creation.</param>
    /// <returns>
    /// The authoritative exit authorization issued for the test scenario.
    /// </returns>
    private static async Task<IssueExitAuthorizationResult> CreateConfirmedIssuedAuthorizationAsync(
        PaymentTestContext context,
        string idempotencyKey)
    {
        var attempt = await CreateAttemptAsync(
            ConnectionString,
            context,
            idempotencyKey,
            "consume-auth-test");

        var finalized = await FinalizeAttemptAsync(
            ConnectionString,
            attempt.PaymentAttemptId,
            "CONFIRMED",
            "central-pms-finalizer",
            context.CorrelationId);

        Assert.NotNull(finalized);
        Assert.Equal("CONFIRMED", finalized!.AttemptStatus);

        var confirmation = await RecordPaymentConfirmationAsync(
            ConnectionString,
            attempt.PaymentAttemptId,
            providerReference: $"prov-{Guid.NewGuid():N}",
            requestedBy: "integration-test",
            correlationId: context.CorrelationId);

        Assert.NotNull(confirmation);
        Assert.Equal(attempt.PaymentAttemptId, confirmation!.PaymentAttemptId);
        Assert.Equal("SUCCESS", confirmation.ProviderStatus);
        Assert.True(confirmation.VerifiedTimestamp <= DateTimeOffset.UtcNow);

        var persistedConfirmation = await GetPaymentConfirmationByIdAsync(
            ConnectionString,
            confirmation.PaymentConfirmationId);

        Assert.NotNull(persistedConfirmation);
        Assert.Equal(confirmation.PaymentConfirmationId, persistedConfirmation!.PaymentConfirmationId);
        Assert.Equal("SUCCESS", persistedConfirmation.ProviderStatus);
        Assert.Equal(confirmation.ProviderReference, persistedConfirmation.ProviderReference);
        Assert.Equal(confirmation.VerifiedTimestamp, persistedConfirmation.VerifiedTimestamp);

        var authorization = await IssueExitAuthorizationAsync(
            ConnectionString,
            attempt.ParkingSessionId,
            attempt.PaymentAttemptId,
            context.RequestedByUserId,
            context.CorrelationId);

        Assert.NotNull(authorization);
        Assert.Equal("ISSUED", authorization!.AuthorizationStatus);

        return authorization;
    }
}
