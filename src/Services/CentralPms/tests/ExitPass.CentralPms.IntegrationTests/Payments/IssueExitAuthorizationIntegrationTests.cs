using System;
using System.Threading.Tasks;
using ExitPass.CentralPms.IntegrationTests.Shared;
using Npgsql;
using Xunit;

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
                context,
                idempotencyKey: "idem-issue-auth-success",
                requestedBy: "issue-auth-test");

            await FinalizeAttemptAsync(
                paymentAttemptId: attempt.PaymentAttemptId,
                finalStatus: "CONFIRMED",
                requestedBy: "central-pms-finalizer",
                correlationId: context.CorrelationId);

            var authorization = await IssueExitAuthorizationAsync(
                parkingSessionId: attempt.ParkingSessionId,
                paymentAttemptId: attempt.PaymentAttemptId,
                requestedBy: context.RequestedByUserId,
                correlationId: context.CorrelationId);

            Assert.NotNull(authorization);
            Assert.Equal(attempt.ParkingSessionId, authorization!.ParkingSessionId);
            Assert.Equal(attempt.PaymentAttemptId, authorization.PaymentAttemptId);
            Assert.Equal("ISSUED", authorization.AuthorizationStatus);
            Assert.False(string.IsNullOrWhiteSpace(authorization.AuthorizationToken));
            Assert.True(authorization.ExpirationTimestamp > authorization.IssuedAt);

            var persisted = await GetExitAuthorizationByIdAsync(authorization.ExitAuthorizationId);
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
                context,
                idempotencyKey: "idem-issue-auth-initiated",
                requestedBy: "issue-auth-test");

            var ex = await Assert.ThrowsAnyAsync<PostgresException>(async () =>
            {
                await IssueExitAuthorizationAsync(
                    parkingSessionId: attempt.ParkingSessionId,
                    paymentAttemptId: attempt.PaymentAttemptId,
                    requestedBy: context.RequestedByUserId,
                    correlationId: context.CorrelationId);
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
                context,
                idempotencyKey: "idem-issue-auth-failed",
                requestedBy: "issue-auth-test");

            await FinalizeAttemptAsync(
                paymentAttemptId: attempt.PaymentAttemptId,
                finalStatus: "FAILED",
                requestedBy: "central-pms-finalizer",
                correlationId: context.CorrelationId);

            var ex = await Assert.ThrowsAnyAsync<PostgresException>(async () =>
            {
                await IssueExitAuthorizationAsync(
                    parkingSessionId: attempt.ParkingSessionId,
                    paymentAttemptId: attempt.PaymentAttemptId,
                    requestedBy: context.RequestedByUserId,
                    correlationId: context.CorrelationId);
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
                context,
                idempotencyKey: "idem-issue-auth-replay",
                requestedBy: "issue-auth-test");

            await FinalizeAttemptAsync(
                paymentAttemptId: attempt.PaymentAttemptId,
                finalStatus: "CONFIRMED",
                requestedBy: "central-pms-finalizer",
                correlationId: context.CorrelationId);

            var first = await IssueExitAuthorizationAsync(
                parkingSessionId: attempt.ParkingSessionId,
                paymentAttemptId: attempt.PaymentAttemptId,
                requestedBy: context.RequestedByUserId,
                correlationId: context.CorrelationId);

            var second = await IssueExitAuthorizationAsync(
                parkingSessionId: attempt.ParkingSessionId,
                paymentAttemptId: attempt.PaymentAttemptId,
                requestedBy: context.RequestedByUserId,
                correlationId: context.CorrelationId);

            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.Equal(first!.ExitAuthorizationId, second!.ExitAuthorizationId);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Creates the canonical PaymentAttempt to be finalized and authorized.
    ///
    /// BRD:
    /// - 9.9 Payment Initiation
    ///
    /// SDD:
    /// - 6.3 Initiate Payment Attempt
    ///
    /// Invariants Enforced:
    /// - PaymentAttempt remains bound to one immutable TariffSnapshot
    /// </summary>
    private static async Task<CreateAttemptResult> CreateAttemptAsync(
        PaymentTestContext context,
        string idempotencyKey,
        string requestedBy)
    {
        const string sql = """
            SELECT
                payment_attempt_id,
                parking_session_id,
                tariff_snapshot_id,
                attempt_status,
                payment_provider_code
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

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        command.Parameters.AddWithValue("p_parking_session_id", context.ParkingSessionId);
        command.Parameters.AddWithValue("p_tariff_snapshot_id", context.TariffSnapshotId);
        command.Parameters.AddWithValue("p_payment_provider_code", "GCASH");
        command.Parameters.AddWithValue("p_idempotency_key", idempotencyKey);
        command.Parameters.AddWithValue("p_requested_by", requestedBy);
        command.Parameters.AddWithValue("p_correlation_id", context.CorrelationId);
        command.Parameters.AddWithValue("p_now", DateTimeOffset.UtcNow);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "Expected create_or_reuse_payment_attempt() to return a row.");

        return new CreateAttemptResult(
            PaymentAttemptId: reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
            ParkingSessionId: reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            TariffSnapshotId: reader.GetGuid(reader.GetOrdinal("tariff_snapshot_id")),
            AttemptStatus: reader.GetString(reader.GetOrdinal("attempt_status")),
            PaymentProviderCode: reader.GetString(reader.GetOrdinal("payment_provider_code")));
    }

    /// <summary>
    /// Finalizes the created PaymentAttempt into a terminal state before issuance.
    ///
    /// BRD:
    /// - 9.10 Payment Processing and Confirmation
    ///
    /// SDD:
    /// - 6.4 Finalize Payment
    ///
    /// Invariants Enforced:
    /// - Only confirmed attempts may support downstream authorization issuance
    /// </summary>
    private static async Task FinalizeAttemptAsync(
        Guid paymentAttemptId,
        string finalStatus,
        string requestedBy,
        Guid correlationId)
    {
        const string sql = """
            SELECT
                payment_attempt_id,
                attempt_status
            FROM core.finalize_payment_attempt(
                @p_payment_attempt_id,
                @p_final_attempt_status,
                @p_requested_by,
                @p_correlation_id,
                @p_now
            );
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        command.Parameters.AddWithValue("p_payment_attempt_id", paymentAttemptId);
        command.Parameters.AddWithValue("p_final_attempt_status", finalStatus);
        command.Parameters.AddWithValue("p_requested_by", requestedBy);
        command.Parameters.AddWithValue("p_correlation_id", correlationId);
        command.Parameters.AddWithValue("p_now", DateTimeOffset.UtcNow);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "Expected finalize_payment_attempt() to return a row.");
    }

    /// <summary>
    /// Calls the DB routine under test.
    ///
    /// BRD:
    /// - 9.12 Exit Authorization
    ///
    /// SDD:
    /// - 6.5 Issue Exit Authorization
    /// - 8.5 ExitAuthorization State Machine
    ///
    /// Invariants Enforced:
    /// - Authorization issuance is downstream of confirmed payment finality
    /// </summary>
    private static async Task<IssueExitAuthorizationResult?> IssueExitAuthorizationAsync(
        Guid parkingSessionId,
        Guid paymentAttemptId,
        Guid requestedBy,
        Guid correlationId)
    {
        const string sql = """
            SELECT
                exit_authorization_id,
                parking_session_id,
                payment_attempt_id,
                authorization_token,
                authorization_status,
                issued_at,
                expiration_timestamp
            FROM core.issue_exit_authorization(
                @p_parking_session_id,
                @p_payment_attempt_id,
                @p_requested_by,
                @p_correlation_id,
                @p_now
            );
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        command.Parameters.AddWithValue("p_parking_session_id", parkingSessionId);
        command.Parameters.AddWithValue("p_payment_attempt_id", paymentAttemptId);
        command.Parameters.AddWithValue("p_requested_by", requestedBy);
        command.Parameters.AddWithValue("p_correlation_id", correlationId);
        command.Parameters.AddWithValue("p_now", DateTimeOffset.UtcNow);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new IssueExitAuthorizationResult(
            ExitAuthorizationId: reader.GetGuid(reader.GetOrdinal("exit_authorization_id")),
            ParkingSessionId: reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            PaymentAttemptId: reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
            AuthorizationToken: reader.GetString(reader.GetOrdinal("authorization_token")),
            AuthorizationStatus: reader.GetString(reader.GetOrdinal("authorization_status")),
            IssuedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("issued_at")),
            ExpirationTimestamp: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("expiration_timestamp")));
    }

    private static async Task<ExitAuthorizationRow?> GetExitAuthorizationByIdAsync(Guid exitAuthorizationId)
    {
        const string sql = """
            SELECT
                exit_authorization_id,
                parking_session_id,
                payment_attempt_id,
                authorization_token,
                authorization_status,
                issued_at,
                expiration_timestamp,
                invalidated_at,
                updated_at,
                updated_by
            FROM core.exit_authorizations
            WHERE exit_authorization_id = @exit_authorization_id;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };
        command.Parameters.AddWithValue("exit_authorization_id", exitAuthorizationId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new ExitAuthorizationRow(
            ExitAuthorizationId: reader.GetGuid(reader.GetOrdinal("exit_authorization_id")),
            ParkingSessionId: reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            PaymentAttemptId: reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
            AuthorizationToken: reader.GetString(reader.GetOrdinal("authorization_token")),
            AuthorizationStatus: reader.GetString(reader.GetOrdinal("authorization_status")),
            IssuedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("issued_at")),
            ExpirationTimestamp: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("expiration_timestamp")),
            InvalidatedAt: ReadDateTimeOffsetNullable(reader, "invalidated_at"),
            UpdatedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at")),
            UpdatedBy: reader.GetGuid(reader.GetOrdinal("updated_by")));
    }

    private static DateTimeOffset? ReadDateTimeOffsetNullable(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    private sealed record CreateAttemptResult(
        Guid PaymentAttemptId,
        Guid ParkingSessionId,
        Guid TariffSnapshotId,
        string AttemptStatus,
        string PaymentProviderCode);

    private sealed record IssueExitAuthorizationResult(
        Guid ExitAuthorizationId,
        Guid ParkingSessionId,
        Guid PaymentAttemptId,
        string AuthorizationToken,
        string AuthorizationStatus,
        DateTimeOffset IssuedAt,
        DateTimeOffset ExpirationTimestamp);

    private sealed record ExitAuthorizationRow(
        Guid ExitAuthorizationId,
        Guid ParkingSessionId,
        Guid PaymentAttemptId,
        string AuthorizationToken,
        string AuthorizationStatus,
        DateTimeOffset IssuedAt,
        DateTimeOffset ExpirationTimestamp,
        DateTimeOffset? InvalidatedAt,
        DateTimeOffset UpdatedAt,
        Guid UpdatedBy);
}
