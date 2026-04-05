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
/// - Shared test helpers must call the same DB routines consistently.
/// - Test suites must not drift in SQL shape or return mapping.
/// - Exit authorization must not be issued without recorded payment confirmation evidence.
/// </summary>
public static class PaymentRoutineTestHelper
{
    /// <summary>
    /// Calls the canonical DB routine to create or reuse a payment attempt for the supplied test context.
    /// </summary>
    /// <param name="connectionString">Integration database connection string.</param>
    /// <param name="context">Per-test canonical data context.</param>
    /// <param name="idempotencyKey">Idempotency key used for the attempt creation call.</param>
    /// <param name="requestedBy">Audit actor string for the DB routine call.</param>
    /// <returns>
    /// The authoritative result returned by <c>core.create_or_reuse_payment_attempt(...)</c>.
    /// </returns>
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

    /// <summary>
    /// Calls the canonical DB routine to finalize a payment attempt.
    /// </summary>
    /// <param name="connectionString">Integration database connection string.</param>
    /// <param name="paymentAttemptId">Canonical payment-attempt identifier.</param>
    /// <param name="finalStatus">Target terminal attempt status.</param>
    /// <param name="requestedBy">Audit actor string for the DB routine call.</param>
    /// <param name="correlationId">Canonical correlation identifier for the scenario.</param>
    /// <returns>
    /// The authoritative result returned by <c>core.finalize_payment_attempt(...)</c>, or <see langword="null"/>
    /// if no row was returned.
    /// </returns>
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

    /// <summary>
    /// Calls the canonical DB routine to record payment confirmation evidence for a payment attempt.
    /// </summary>
    /// <param name="connectionString">Integration database connection string.</param>
    /// <param name="paymentAttemptId">Canonical payment-attempt identifier.</param>
    /// <param name="providerReference">Externally visible provider reference that must be unique for the scenario.</param>
    /// <param name="requestedBy">Audit actor string for the DB routine call.</param>
    /// <param name="correlationId">Canonical correlation identifier for the scenario.</param>
    /// <returns>
    /// The authoritative result returned by <c>core.record_payment_confirmation(...)</c>, or
    /// <see langword="null"/> if no row was returned.
    /// </returns>
    public static async Task<RecordPaymentConfirmationResult?> RecordPaymentConfirmationAsync(
        string connectionString,
        Guid paymentAttemptId,
        string providerReference,
        string requestedBy,
        Guid correlationId)
    {
        const string sql = """
            SELECT
                payment_confirmation_id,
                payment_attempt_id,
                provider_reference,
                provider_status,
                verified_timestamp
            FROM core.record_payment_confirmation(
                @p_payment_attempt_id,
                @p_provider_reference,
                @p_provider_status,
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
        command.Parameters.AddWithValue("p_provider_reference", providerReference);
        command.Parameters.AddWithValue("p_provider_status", "SUCCESS");
        command.Parameters.AddWithValue("p_requested_by", requestedBy);
        command.Parameters.AddWithValue("p_correlation_id", correlationId);
        command.Parameters.AddWithValue("p_now", DateTimeOffset.UtcNow);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new RecordPaymentConfirmationResult(
            PaymentConfirmationId: reader.GetGuid(reader.GetOrdinal("payment_confirmation_id")),
            PaymentAttemptId: reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
            ProviderReference: reader.GetString(reader.GetOrdinal("provider_reference")),
            ProviderStatus: reader.GetString(reader.GetOrdinal("provider_status")),
            VerifiedTimestamp: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("verified_timestamp")));
    }

    /// <summary>
    /// Calls the canonical DB routine to issue an exit authorization.
    /// </summary>
    /// <param name="connectionString">Integration database connection string.</param>
    /// <param name="parkingSessionId">Canonical parking-session identifier.</param>
    /// <param name="paymentAttemptId">Canonical payment-attempt identifier.</param>
    /// <param name="requestedBy">Valid seeded service identity identifier used for actor attribution.</param>
    /// <param name="correlationId">Canonical correlation identifier for the scenario.</param>
    /// <returns>
    /// The authoritative result returned by <c>core.issue_exit_authorization(...)</c>, or
    /// <see langword="null"/> if no row was returned.
    /// </returns>
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

    /// <summary>
    /// Calls the canonical DB routine to consume an exit authorization.
    /// </summary>
    /// <param name="connectionString">Integration database connection string.</param>
    /// <param name="exitAuthorizationId">Canonical exit-authorization identifier.</param>
    /// <param name="requestedBy">Valid seeded service identity identifier used for actor attribution.</param>
    /// <param name="correlationId">Canonical correlation identifier for the scenario.</param>
    /// <returns>
    /// The authoritative result returned by <c>core.consume_exit_authorization(...)</c>, or
    /// <see langword="null"/> if no row was returned.
    /// </returns>
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

    /// <summary>
    /// Reads the current persisted payment-attempt row by identifier.
    /// </summary>
    /// <param name="connectionString">Integration database connection string.</param>
    /// <param name="paymentAttemptId">Canonical payment-attempt identifier.</param>
    /// <returns>
    /// The current persisted payment-attempt row, or <see langword="null"/> when no row exists.
    /// </returns>
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

    /// <summary>
    /// Reads the current persisted payment-confirmation row by identifier.
    /// </summary>
    /// <param name="connectionString">Integration database connection string.</param>
    /// <param name="paymentConfirmationId">Canonical payment-confirmation identifier.</param>
    /// <returns>
    /// The current persisted payment-confirmation row, or <see langword="null"/> when no row exists.
    /// </returns>
    public static async Task<PaymentConfirmationRow?> GetPaymentConfirmationByIdAsync(
        string connectionString,
        Guid paymentConfirmationId)
    {
        const string sql = """
            SELECT
                payment_confirmation_id,
                payment_attempt_id,
                provider_reference,
                provider_status,
                confirmation_status,
                verified_timestamp,
                raw_callback_reference,
                provider_signature_valid,
                provider_payload_hash,
                amount_confirmed,
                currency_code,
                created_at,
                created_by
            FROM core.payment_confirmations
            WHERE payment_confirmation_id = @payment_confirmation_id;
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        command.Parameters.AddWithValue("payment_confirmation_id", paymentConfirmationId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new PaymentConfirmationRow(
            PaymentConfirmationId: reader.GetGuid(reader.GetOrdinal("payment_confirmation_id")),
            PaymentAttemptId: reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
            ProviderReference: reader.GetString(reader.GetOrdinal("provider_reference")),
            ProviderStatus: reader.GetString(reader.GetOrdinal("provider_status")),
            ConfirmationStatus: reader.GetString(reader.GetOrdinal("confirmation_status")),
            VerifiedTimestamp: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("verified_timestamp")),
            RawCallbackReference: ReadStringNullable(reader, "raw_callback_reference"),
            ProviderSignatureValid: ReadBooleanNullable(reader, "provider_signature_valid"),
            ProviderPayloadHash: ReadStringNullable(reader, "provider_payload_hash"),
            AmountConfirmed: reader.GetDecimal(reader.GetOrdinal("amount_confirmed")),
            CurrencyCode: reader.GetString(reader.GetOrdinal("currency_code")),
            CreatedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
            CreatedBy: reader.GetString(reader.GetOrdinal("created_by")));
    }

    /// <summary>
    /// Reads the current persisted exit-authorization row by identifier.
    /// </summary>
    /// <param name="connectionString">Integration database connection string.</param>
    /// <param name="exitAuthorizationId">Canonical exit-authorization identifier.</param>
    /// <returns>
    /// The current persisted exit-authorization row, or <see langword="null"/> when no row exists.
    /// </returns>
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

    /// <summary>
    /// Forces an issued exit authorization into an expired state for negative-path testing.
    /// </summary>
    /// <param name="connectionString">Integration database connection string.</param>
    /// <param name="exitAuthorizationId">Canonical exit-authorization identifier.</param>
    /// <param name="requestedBy">Valid seeded service identity identifier recorded as the updater.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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

    /// <summary>
    /// Reads a nullable timestamp column as a nullable <see cref="DateTimeOffset"/>.
    /// </summary>
    /// <param name="reader">Data reader positioned on the current row.</param>
    /// <param name="columnName">Column name to inspect.</param>
    /// <returns>
    /// The timestamp value when present, otherwise <see langword="null"/>.
    /// </returns>
    public static DateTimeOffset? ReadDateTimeOffsetNullable(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    /// <summary>
    /// Reads a nullable boolean column as a nullable <see cref="bool"/>.
    /// </summary>
    /// <param name="reader">Data reader positioned on the current row.</param>
    /// <param name="columnName">Column name to inspect.</param>
    /// <returns>
    /// The boolean value when present, otherwise <see langword="null"/>.
    /// </returns>
    public static bool? ReadBooleanNullable(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return reader.GetBoolean(ordinal);
    }

    /// <summary>
    /// Reads a nullable text column as a nullable <see cref="string"/>.
    /// </summary>
    /// <param name="reader">Data reader positioned on the current row.</param>
    /// <param name="columnName">Column name to inspect.</param>
    /// <returns>
    /// The string value when present, otherwise <see langword="null"/>.
    /// </returns>
    public static string? ReadStringNullable(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return reader.GetString(ordinal);
    }

    /// <summary>
    /// Result returned from the create-or-reuse payment-attempt DB routine.
    /// </summary>
    /// <param name="PaymentAttemptId">Canonical payment-attempt identifier.</param>
    /// <param name="ParkingSessionId">Canonical parking-session identifier.</param>
    /// <param name="TariffSnapshotId">Canonical tariff-snapshot identifier.</param>
    /// <param name="AttemptStatus">Current payment-attempt status.</param>
    /// <param name="PaymentProviderCode">Bound payment provider code.</param>
    public sealed record CreateAttemptResult(
        Guid PaymentAttemptId,
        Guid ParkingSessionId,
        Guid TariffSnapshotId,
        string AttemptStatus,
        string PaymentProviderCode);

    /// <summary>
    /// Result returned from the finalize-payment-attempt DB routine.
    /// </summary>
    /// <param name="PaymentAttemptId">Canonical payment-attempt identifier.</param>
    /// <param name="AttemptStatus">Current payment-attempt status after finalization.</param>
    public sealed record FinalizeAttemptResult(
        Guid PaymentAttemptId,
        string AttemptStatus);

    /// <summary>
    /// Result returned from the record-payment-confirmation DB routine.
    /// </summary>
    /// <param name="PaymentConfirmationId">Canonical payment-confirmation identifier.</param>
    /// <param name="PaymentAttemptId">Canonical payment-attempt identifier.</param>
    /// <param name="ProviderReference">Provider-native payment reference.</param>
    /// <param name="ProviderStatus">Provider-native payment status recorded by the routine.</param>
    /// <param name="VerifiedTimestamp">Timestamp when confirmation evidence was verified and recorded.</param>
    public sealed record RecordPaymentConfirmationResult(
        Guid PaymentConfirmationId,
        Guid PaymentAttemptId,
        string ProviderReference,
        string ProviderStatus,
        DateTimeOffset VerifiedTimestamp);

    /// <summary>
    /// Result returned from the issue-exit-authorization DB routine.
    /// </summary>
    /// <param name="ExitAuthorizationId">Canonical exit-authorization identifier.</param>
    /// <param name="ParkingSessionId">Canonical parking-session identifier.</param>
    /// <param name="PaymentAttemptId">Canonical payment-attempt identifier.</param>
    /// <param name="AuthorizationToken">Single-use authorization token.</param>
    /// <param name="AuthorizationStatus">Authorization status after issuance.</param>
    /// <param name="IssuedAt">Authorization issue timestamp.</param>
    /// <param name="ExpirationTimestamp">Authorization expiration timestamp.</param>
    public sealed record IssueExitAuthorizationResult(
        Guid ExitAuthorizationId,
        Guid ParkingSessionId,
        Guid PaymentAttemptId,
        string AuthorizationToken,
        string AuthorizationStatus,
        DateTimeOffset IssuedAt,
        DateTimeOffset ExpirationTimestamp);

    /// <summary>
    /// Result returned from the consume-exit-authorization DB routine.
    /// </summary>
    /// <param name="ExitAuthorizationId">Canonical exit-authorization identifier.</param>
    /// <param name="AuthorizationStatus">Authorization status after consumption.</param>
    /// <param name="ConsumedAt">Authorization consumed timestamp, when present.</param>
    public sealed record ConsumeExitAuthorizationResult(
        Guid ExitAuthorizationId,
        string AuthorizationStatus,
        DateTimeOffset? ConsumedAt);

    /// <summary>
    /// Projection of a persisted payment-attempt row used by integration tests.
    /// </summary>
    /// <param name="PaymentAttemptId">Canonical payment-attempt identifier.</param>
    /// <param name="ParkingSessionId">Canonical parking-session identifier.</param>
    /// <param name="TariffSnapshotId">Canonical tariff-snapshot identifier.</param>
    /// <param name="PaymentProviderCode">Bound payment provider code.</param>
    /// <param name="IdempotencyKey">Persisted idempotency key.</param>
    /// <param name="AttemptStatus">Current payment-attempt status.</param>
    /// <param name="CreatedAt">Row creation timestamp.</param>
    /// <param name="UpdatedAt">Last row update timestamp.</param>
    /// <param name="FinalizedAt">Terminal-finalization timestamp, when present.</param>
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

    /// <summary>
    /// Projection of a persisted payment-confirmation row used by integration tests.
    /// </summary>
    /// <param name="PaymentConfirmationId">Canonical payment-confirmation identifier.</param>
    /// <param name="PaymentAttemptId">Canonical payment-attempt identifier.</param>
    /// <param name="ProviderReference">Provider-native payment reference.</param>
    /// <param name="ProviderStatus">Provider-native payment status.</param>
    /// <param name="ConfirmationStatus">Internal confirmation lifecycle status.</param>
    /// <param name="VerifiedTimestamp">Timestamp when confirmation evidence was verified and recorded.</param>
    /// <param name="RawCallbackReference">Raw callback reference, when present.</param>
    /// <param name="ProviderSignatureValid">Whether provider signature validation succeeded, when present.</param>
    /// <param name="ProviderPayloadHash">Provider payload hash, when present.</param>
    /// <param name="AmountConfirmed">Amount confirmed by the provider.</param>
    /// <param name="CurrencyCode">Currency code of the confirmed amount.</param>
    /// <param name="CreatedAt">Row creation timestamp.</param>
    /// <param name="CreatedBy">Actor recorded as the creator of the row.</param>
    public sealed record PaymentConfirmationRow(
        Guid PaymentConfirmationId,
        Guid PaymentAttemptId,
        string ProviderReference,
        string ProviderStatus,
        string ConfirmationStatus,
        DateTimeOffset VerifiedTimestamp,
        string? RawCallbackReference,
        bool? ProviderSignatureValid,
        string? ProviderPayloadHash,
        decimal AmountConfirmed,
        string CurrencyCode,
        DateTimeOffset CreatedAt,
        string CreatedBy);

    /// <summary>
    /// Projection of a persisted exit-authorization row used by integration tests.
    /// </summary>
    /// <param name="ExitAuthorizationId">Canonical exit-authorization identifier.</param>
    /// <param name="ParkingSessionId">Canonical parking-session identifier.</param>
    /// <param name="PaymentAttemptId">Canonical payment-attempt identifier.</param>
    /// <param name="AuthorizationToken">Single-use authorization token.</param>
    /// <param name="AuthorizationStatus">Current authorization status.</param>
    /// <param name="IssuedAt">Authorization issue timestamp.</param>
    /// <param name="ExpirationTimestamp">Authorization expiration timestamp.</param>
    /// <param name="InvalidatedAt">Authorization invalidation timestamp, when present.</param>
    /// <param name="UpdatedAt">Last row update timestamp.</param>
    /// <param name="UpdatedBy">Actor who last updated the row.</param>
    /// <param name="ConsumedAt">Authorization consumed timestamp, when present.</param>
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
