using System;
using System.Threading.Tasks;
using ExitPass.CentralPms.IntegrationTests.Shared;
using Npgsql;
using Xunit;

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
                context,
                idempotencyKey: "idem-consume-success",
                requestedBy: "consume-auth-test");

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

            var consumed = await ConsumeExitAuthorizationAsync(
                exitAuthorizationId: authorization!.ExitAuthorizationId,
                requestedBy: context.RequestedByUserId,
                correlationId: context.CorrelationId);

            Assert.NotNull(consumed);
            Assert.Equal(authorization.ExitAuthorizationId, consumed!.ExitAuthorizationId);
            Assert.Equal("CONSUMED", consumed.AuthorizationStatus);
            Assert.NotNull(consumed.ConsumedAt);

            var persisted = await GetExitAuthorizationByIdAsync(authorization.ExitAuthorizationId);
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
                context,
                idempotencyKey: "idem-consume-replay",
                requestedBy: "consume-auth-test");

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

            var firstConsume = await ConsumeExitAuthorizationAsync(
                exitAuthorizationId: authorization!.ExitAuthorizationId,
                requestedBy: context.RequestedByUserId,
                correlationId: context.CorrelationId);

            Assert.NotNull(firstConsume);
            Assert.Equal("CONSUMED", firstConsume!.AuthorizationStatus);

            var ex = await Assert.ThrowsAnyAsync<PostgresException>(async () =>
            {
                await ConsumeExitAuthorizationAsync(
                    exitAuthorizationId: authorization.ExitAuthorizationId,
                    requestedBy: context.RequestedByUserId,
                    correlationId: context.CorrelationId);
            });

            Assert.False(string.IsNullOrWhiteSpace(ex.SqlState));

            var persisted = await GetExitAuthorizationByIdAsync(authorization.ExitAuthorizationId);
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
                context,
                idempotencyKey: "idem-consume-expired",
                requestedBy: "consume-auth-test");

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

            await ExpireAuthorizationAsync(
                authorization!.ExitAuthorizationId,
                context.RequestedByUserId);

            var ex = await Assert.ThrowsAnyAsync<PostgresException>(async () =>
            {
                await ConsumeExitAuthorizationAsync(
                    exitAuthorizationId: authorization.ExitAuthorizationId,
                    requestedBy: context.RequestedByUserId,
                    correlationId: context.CorrelationId);
            });

            Assert.False(string.IsNullOrWhiteSpace(ex.SqlState));

            var persisted = await GetExitAuthorizationByIdAsync(authorization.ExitAuthorizationId);
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
                    exitAuthorizationId: Guid.NewGuid(),
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

    private static async Task<ConsumeExitAuthorizationResult?> ConsumeExitAuthorizationAsync(
        Guid exitAuthorizationId,
        Guid requestedBy,
        Guid correlationId)
    {
        const string sql = """
            SELECT
                exit_authorization_id,
                authorization_status,
                consumed_at
            FROM core.consume_exit_authorization(
                @p_exit_authorization_id,
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

        command.Parameters.AddWithValue("p_exit_authorization_id", exitAuthorizationId);
        command.Parameters.AddWithValue("p_requested_by", requestedBy);
        command.Parameters.AddWithValue("p_correlation_id", correlationId);
        command.Parameters.AddWithValue("p_now", DateTimeOffset.UtcNow);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new ConsumeExitAuthorizationResult(
            ExitAuthorizationId: reader.GetGuid(reader.GetOrdinal("exit_authorization_id")),
            AuthorizationStatus: reader.GetString(reader.GetOrdinal("authorization_status")),
            ConsumedAt: ReadDateTimeOffsetNullable(reader, "consumed_at"));
    }

    private static async Task ExpireAuthorizationAsync(Guid exitAuthorizationId, Guid requestedBy)
    {
        const string sql = """
        UPDATE core.exit_authorizations
        SET
            issued_at = NOW() - INTERVAL '2 minutes',
            expiration_timestamp = NOW() - INTERVAL '1 minute',
            updated_at = NOW(),
            updated_by = @updated_by,
            row_version = row_version + 1
        WHERE exit_authorization_id = @exit_authorization_id;
        """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        command.Parameters.AddWithValue("exit_authorization_id", exitAuthorizationId);
        command.Parameters.AddWithValue("updated_by", requestedBy);

        await command.ExecuteNonQueryAsync();
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
                updated_by,
                consumed_at
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
            UpdatedBy: reader.GetGuid(reader.GetOrdinal("updated_by")),
            ConsumedAt: ReadDateTimeOffsetNullable(reader, "consumed_at"));
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

    private sealed record ConsumeExitAuthorizationResult(
        Guid ExitAuthorizationId,
        string AuthorizationStatus,
        DateTimeOffset? ConsumedAt);

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
        Guid UpdatedBy,
        DateTimeOffset? ConsumedAt);
}
