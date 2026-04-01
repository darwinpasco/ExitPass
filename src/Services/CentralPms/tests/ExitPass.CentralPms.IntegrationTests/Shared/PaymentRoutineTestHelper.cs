using System;
using System.Threading.Tasks;
using Npgsql;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Shared;

/// <summary>
/// Shared DB routine caller helper for payment integration tests.
///
/// BRD:
/// - 9.9 Payment Initiation
/// - 9.10 Payment Processing and Confirmation
/// - 9.12 Exit Authorization
///
/// SDD:
/// - 6.3 Initiate Payment Attempt
/// - 6.4 Finalize Payment
/// - 6.5 Issue Exit Authorization
/// - 6.6 Consume Exit Authorization
///
/// Invariants Enforced:
/// - Shared test helpers must call the same DB routines consistently
/// - Test suites must not drift in SQL shape or return mapping
/// </summary>
public static class PaymentRoutineTestHelper
{
    public static async Task<CreateAttemptResult> CreateAttemptAsync(
        string connectionString,
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

        await using var connection = new NpgsqlConnection(connectionString);
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

    public static async Task<FinalizeAttemptResult?> FinalizeAttemptAsync(
        string connectionString,
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

        await using var connection = new NpgsqlConnection(connectionString);
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

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new FinalizeAttemptResult(
            PaymentAttemptId: reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
            AttemptStatus: reader.GetString(reader.GetOrdinal("attempt_status")));
    }

    public static async Task<IssueExitAuthorizationResult?> IssueExitAuthorizationAsync(
        string connectionString,
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

        await using var connection = new NpgsqlConnection(connectionString);
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

    public static async Task<ConsumeExitAuthorizationResult?> ConsumeExitAuthorizationAsync(
        string connectionString,
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

        await using var connection = new NpgsqlConnection(connectionString);
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

    public static async Task<PaymentAttemptRow?> GetPaymentAttemptAsync(
        string connectionString,
        Guid paymentAttemptId)
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
                updated_at,
                finalized_at
            FROM core.payment_attempts
            WHERE payment_attempt_id = @payment_attempt_id;
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };
        command.Parameters.AddWithValue("payment_attempt_id", paymentAttemptId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new PaymentAttemptRow(
            PaymentAttemptId: reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
            ParkingSessionId: reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            TariffSnapshotId: reader.GetGuid(reader.GetOrdinal("tariff_snapshot_id")),
            PaymentProviderCode: reader.GetString(reader.GetOrdinal("payment_provider_code")),
            IdempotencyKey: reader.GetString(reader.GetOrdinal("idempotency_key")),
            AttemptStatus: reader.GetString(reader.GetOrdinal("attempt_status")),
            CreatedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
            UpdatedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at")),
            FinalizedAt: ReadDateTimeOffsetNullable(reader, "finalized_at"));
    }

    public static async Task<ExitAuthorizationRow?> GetExitAuthorizationByIdAsync(
        string connectionString,
        Guid exitAuthorizationId)
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

        await using var connection = new NpgsqlConnection(connectionString);
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

    public static async Task ExpireAuthorizationAsync(
        string connectionString,
        Guid exitAuthorizationId,
        Guid requestedBy)
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

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        command.Parameters.AddWithValue("exit_authorization_id", exitAuthorizationId);
        command.Parameters.AddWithValue("updated_by", requestedBy);

        await command.ExecuteNonQueryAsync();
    }

    public static DateTimeOffset? ReadDateTimeOffsetNullable(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    public sealed record CreateAttemptResult(
        Guid PaymentAttemptId,
        Guid ParkingSessionId,
        Guid TariffSnapshotId,
        string AttemptStatus,
        string PaymentProviderCode);

    public sealed record FinalizeAttemptResult(
        Guid PaymentAttemptId,
        string AttemptStatus);

    public sealed record IssueExitAuthorizationResult(
        Guid ExitAuthorizationId,
        Guid ParkingSessionId,
        Guid PaymentAttemptId,
        string AuthorizationToken,
        string AuthorizationStatus,
        DateTimeOffset IssuedAt,
        DateTimeOffset ExpirationTimestamp);

    public sealed record ConsumeExitAuthorizationResult(
        Guid ExitAuthorizationId,
        string AuthorizationStatus,
        DateTimeOffset? ConsumedAt);

    public sealed record PaymentAttemptRow(
        Guid PaymentAttemptId,
        Guid ParkingSessionId,
        Guid TariffSnapshotId,
        string PaymentProviderCode,
        string IdempotencyKey,
        string AttemptStatus,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? FinalizedAt);

    public sealed record ExitAuthorizationRow(
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
