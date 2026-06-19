using ExitPass.CentralPms.Application.VendorPaymentAcknowledgments;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.VendorPaymentAcknowledgments;

/// <summary>
/// PostgreSQL-backed persistence for Vendor PMS payment acknowledgment status.
/// </summary>
public sealed class VendorPaymentAcknowledgmentRepository : IVendorPaymentAcknowledgmentRepository
{
    private readonly string _connectionString;
    private const string SelectRecordColumns = """
                vendor_payment_acknowledgment_id,
                payment_attempt_id,
                payment_confirmation_id,
                parking_session_id,
                vendor_system_code,
                vendor_session_ref,
                ticket_number,
                card_num,
                acknowledgment_status::text,
                vendor_code,
                vendor_message,
                request_fee_minor_units,
                request_currency_code,
                confirmed_fee_minor_units,
                vendor_confirmed_at,
                attempt_count,
                last_attempted_at,
                next_retry_at,
                idempotency_key,
                correlation_id,
                created_at,
                updated_at
            """;

    /// <summary>
    /// Creates a Vendor PMS acknowledgment repository.
    /// </summary>
    public VendorPaymentAcknowledgmentRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    /// <inheritdoc />
    public async Task<VendorPaymentAcknowledgmentBasis?> LoadBasisAsync(
        Guid paymentAttemptId,
        Guid paymentConfirmationId,
        Guid parkingSessionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                pa.payment_attempt_id,
                pc.payment_confirmation_id,
                ps.parking_session_id,
                vs.vendor_code AS vendor_system_code,
                ps.vendor_session_ref,
                ps.ticket_number_masked AS ticket_number,
                COALESCE(ps.ticket_number_masked, ps.vendor_session_ref) AS card_num,
                FLOOR(pa.amount * 100)::bigint AS request_fee_minor_units,
                pa.currency_code::text AS request_currency_code
            FROM core.payment_attempts AS pa
            INNER JOIN core.payment_confirmations AS pc
                ON pc.payment_attempt_id = pa.payment_attempt_id
               AND pc.payment_confirmation_id = @payment_confirmation_id
               AND pc.confirmation_status = 'RECORDED'::core.payment_confirmation_status_enum
            INNER JOIN core.parking_sessions AS ps
                ON ps.parking_session_id = pa.parking_session_id
               AND ps.parking_session_id = @parking_session_id
            INNER JOIN integration.vendor_systems AS vs
                ON vs.vendor_system_id = ps.vendor_system_id
            WHERE pa.payment_attempt_id = @payment_attempt_id
              AND pa.attempt_status = 'CONFIRMED'::core.payment_attempt_status_enum
              AND pa.finalized_at IS NOT NULL
            LIMIT 1;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(sql, connection);
        dbCommand.Parameters.Add("payment_attempt_id", NpgsqlDbType.Uuid).Value = paymentAttemptId;
        dbCommand.Parameters.Add("payment_confirmation_id", NpgsqlDbType.Uuid).Value = paymentConfirmationId;
        dbCommand.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = parkingSessionId;

        await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new VendorPaymentAcknowledgmentBasis(
            PaymentAttemptId: reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
            PaymentConfirmationId: reader.GetGuid(reader.GetOrdinal("payment_confirmation_id")),
            ParkingSessionId: reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            VendorSystemCode: reader.GetString(reader.GetOrdinal("vendor_system_code")),
            VendorSessionRef: GetNullableString(reader, "vendor_session_ref"),
            TicketNumber: GetNullableString(reader, "ticket_number"),
            CardNum: GetNullableString(reader, "card_num"),
            RequestFeeMinorUnits: reader.GetInt64(reader.GetOrdinal("request_fee_minor_units")),
            RequestCurrencyCode: reader.GetString(reader.GetOrdinal("request_currency_code")).Trim());
    }

    /// <inheritdoc />
    public async Task<VendorPaymentAcknowledgmentRecord> CreatePendingAsync(
        CreateVendorPaymentAcknowledgmentCommand command,
        CancellationToken cancellationToken)
    {
        var sql = $$"""
            INSERT INTO integration.vendor_payment_acknowledgments (
                vendor_payment_acknowledgment_id,
                payment_attempt_id,
                payment_confirmation_id,
                parking_session_id,
                vendor_system_code,
                vendor_session_ref,
                ticket_number,
                card_num,
                acknowledgment_status,
                request_fee_minor_units,
                request_currency_code,
                idempotency_key,
                correlation_id,
                created_at,
                updated_at
            )
            VALUES (
                gen_random_uuid(),
                @payment_attempt_id,
                @payment_confirmation_id,
                @parking_session_id,
                @vendor_system_code,
                @vendor_session_ref,
                @ticket_number,
                @card_num,
                'PENDING'::integration.vendor_payment_acknowledgment_status_enum,
                @request_fee_minor_units,
                @request_currency_code,
                @idempotency_key,
                @correlation_id,
                @created_at,
                @created_at
            )
            RETURNING
                {{SelectRecordColumns}}
                ;
            """;

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var dbCommand = new NpgsqlCommand(sql, connection);
            AddCreateParameters(dbCommand, command);

            await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("Vendor PMS acknowledgment insert returned no rows.");
            }

            return ReadRecord(reader);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new VendorPaymentAcknowledgmentConflictException(
                "VENDOR_PAYMENT_ACKNOWLEDGMENT_ALREADY_EXISTS",
                ex.MessageText,
                ex);
        }
    }

    /// <inheritdoc />
    public Task<VendorPaymentAcknowledgmentRecord> MarkConfirmedAsync(
        MarkVendorPaymentAcknowledgmentConfirmedCommand command,
        CancellationToken cancellationToken)
    {
        var sql = $$"""
            UPDATE integration.vendor_payment_acknowledgments
            SET
                acknowledgment_status = 'CONFIRMED'::integration.vendor_payment_acknowledgment_status_enum,
                vendor_code = @vendor_code,
                vendor_message = @vendor_message,
                confirmed_fee_minor_units = @confirmed_fee_minor_units,
                vendor_confirmed_at = @vendor_confirmed_at,
                last_attempted_at = @updated_at,
                next_retry_at = NULL,
                attempt_count = attempt_count + 1,
                updated_at = @updated_at
            WHERE vendor_payment_acknowledgment_id = @vendor_payment_acknowledgment_id
            RETURNING
                {{SelectRecordColumns}}
                ;
            """;

        return UpdateAsync(sql, command.VendorPaymentAcknowledgmentId, dbCommand =>
        {
            dbCommand.Parameters.Add("vendor_code", NpgsqlDbType.Text).Value = DbValue(command.VendorCode);
            dbCommand.Parameters.Add("vendor_message", NpgsqlDbType.Text).Value = DbValue(command.VendorMessage);
            dbCommand.Parameters.Add("confirmed_fee_minor_units", NpgsqlDbType.Bigint).Value = DbValue(command.ConfirmedFeeMinorUnits);
            dbCommand.Parameters.Add("vendor_confirmed_at", NpgsqlDbType.TimestampTz).Value = DbValue(command.VendorConfirmedAt);
            dbCommand.Parameters.Add("updated_at", NpgsqlDbType.TimestampTz).Value = ToUtc(command.UpdatedAt);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<VendorPaymentAcknowledgmentRecord> MarkFailedAsync(
        MarkVendorPaymentAcknowledgmentFailedCommand command,
        CancellationToken cancellationToken)
    {
        var sql = $$"""
            UPDATE integration.vendor_payment_acknowledgments
            SET
                acknowledgment_status = CASE
                    WHEN @next_retry_at IS NULL THEN 'FAILED'::integration.vendor_payment_acknowledgment_status_enum
                    ELSE 'RETRY_PENDING'::integration.vendor_payment_acknowledgment_status_enum
                END,
                vendor_code = @vendor_code,
                vendor_message = @vendor_message,
                last_attempted_at = @last_attempted_at,
                next_retry_at = @next_retry_at,
                attempt_count = attempt_count + 1,
                updated_at = @updated_at
            WHERE vendor_payment_acknowledgment_id = @vendor_payment_acknowledgment_id
            RETURNING
                {{SelectRecordColumns}}
                ;
            """;

        return UpdateAsync(sql, command.VendorPaymentAcknowledgmentId, dbCommand =>
        {
            dbCommand.Parameters.Add("vendor_code", NpgsqlDbType.Text).Value = DbValue(command.VendorCode);
            dbCommand.Parameters.Add("vendor_message", NpgsqlDbType.Text).Value = DbValue(command.VendorMessage);
            dbCommand.Parameters.Add("last_attempted_at", NpgsqlDbType.TimestampTz).Value = ToUtc(command.LastAttemptedAt);
            dbCommand.Parameters.Add("next_retry_at", NpgsqlDbType.TimestampTz).Value = DbValue(command.NextRetryAt);
            dbCommand.Parameters.Add("updated_at", NpgsqlDbType.TimestampTz).Value = ToUtc(command.UpdatedAt);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<VendorPaymentAcknowledgmentRecord> MarkSkippedDisabledAsync(
        MarkVendorPaymentAcknowledgmentSkippedDisabledCommand command,
        CancellationToken cancellationToken)
    {
        var sql = $$"""
            UPDATE integration.vendor_payment_acknowledgments
            SET
                acknowledgment_status = 'SKIPPED_DISABLED'::integration.vendor_payment_acknowledgment_status_enum,
                vendor_code = 'CONFIRM_DISABLED',
                vendor_message = @vendor_message,
                updated_at = @updated_at
            WHERE vendor_payment_acknowledgment_id = @vendor_payment_acknowledgment_id
            RETURNING
                {{SelectRecordColumns}}
                ;
            """;

        return UpdateAsync(sql, command.VendorPaymentAcknowledgmentId, dbCommand =>
        {
            dbCommand.Parameters.Add("vendor_message", NpgsqlDbType.Text).Value = DbValue(command.VendorMessage);
            dbCommand.Parameters.Add("updated_at", NpgsqlDbType.TimestampTz).Value = ToUtc(command.UpdatedAt);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<VendorPaymentAcknowledgmentRecord?> ReadAsync(
        Guid vendorPaymentAcknowledgmentId,
        CancellationToken cancellationToken)
    {
        var sql = $$"""
            SELECT
                {{SelectRecordColumns}}
            FROM integration.vendor_payment_acknowledgments
            WHERE vendor_payment_acknowledgment_id = @vendor_payment_acknowledgment_id;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(sql, connection);
        dbCommand.Parameters.Add("vendor_payment_acknowledgment_id", NpgsqlDbType.Uuid).Value = vendorPaymentAcknowledgmentId;

        await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadRecord(reader)
            : null;
    }

    /// <inheritdoc />
    public async Task<VendorPaymentAcknowledgmentRecord?> ReadByPaymentConfirmationAsync(
        Guid paymentConfirmationId,
        string vendorSystemCode,
        CancellationToken cancellationToken)
    {
        var sql = $$"""
            SELECT
                {{SelectRecordColumns}}
            FROM integration.vendor_payment_acknowledgments
            WHERE payment_confirmation_id = @payment_confirmation_id
              AND vendor_system_code = @vendor_system_code
            LIMIT 1;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(sql, connection);
        dbCommand.Parameters.Add("payment_confirmation_id", NpgsqlDbType.Uuid).Value = paymentConfirmationId;
        dbCommand.Parameters.Add("vendor_system_code", NpgsqlDbType.Text).Value = vendorSystemCode.Trim();

        await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadRecord(reader)
            : null;
    }

    /// <inheritdoc />
    public async Task<VendorPaymentAcknowledgmentRecord?> ReadLatestByPaymentAttemptAsync(
        Guid paymentAttemptId,
        CancellationToken cancellationToken)
    {
        var sql = $$"""
            SELECT
                {{SelectRecordColumns}}
            FROM integration.vendor_payment_acknowledgments
            WHERE payment_attempt_id = @payment_attempt_id
            ORDER BY updated_at DESC, created_at DESC, vendor_payment_acknowledgment_id DESC
            LIMIT 1;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(sql, connection);
        dbCommand.Parameters.Add("payment_attempt_id", NpgsqlDbType.Uuid).Value = paymentAttemptId;

        await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadRecord(reader)
            : null;
    }

    private async Task<VendorPaymentAcknowledgmentRecord> UpdateAsync(
        string sql,
        Guid vendorPaymentAcknowledgmentId,
        Action<NpgsqlCommand> addParameters,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(sql, connection);
        dbCommand.Parameters.Add("vendor_payment_acknowledgment_id", NpgsqlDbType.Uuid).Value = vendorPaymentAcknowledgmentId;
        addParameters(dbCommand);

        await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new KeyNotFoundException(
                $"Vendor PMS acknowledgment '{vendorPaymentAcknowledgmentId}' was not found.");
        }

        return ReadRecord(reader);
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static void AddCreateParameters(
        NpgsqlCommand dbCommand,
        CreateVendorPaymentAcknowledgmentCommand command)
    {
        dbCommand.Parameters.Add("payment_attempt_id", NpgsqlDbType.Uuid).Value = command.PaymentAttemptId;
        dbCommand.Parameters.Add("payment_confirmation_id", NpgsqlDbType.Uuid).Value = command.PaymentConfirmationId;
        dbCommand.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = DbValue(command.ParkingSessionId);
        dbCommand.Parameters.Add("vendor_system_code", NpgsqlDbType.Text).Value = command.VendorSystemCode.Trim();
        dbCommand.Parameters.Add("vendor_session_ref", NpgsqlDbType.Text).Value = DbValue(command.VendorSessionRef);
        dbCommand.Parameters.Add("ticket_number", NpgsqlDbType.Text).Value = DbValue(command.TicketNumber);
        dbCommand.Parameters.Add("card_num", NpgsqlDbType.Text).Value = DbValue(command.CardNum);
        dbCommand.Parameters.Add("request_fee_minor_units", NpgsqlDbType.Bigint).Value = DbValue(command.RequestFeeMinorUnits);
        dbCommand.Parameters.Add("request_currency_code", NpgsqlDbType.Text).Value = DbValue(command.RequestCurrencyCode);
        dbCommand.Parameters.Add("idempotency_key", NpgsqlDbType.Text).Value = DbValue(command.IdempotencyKey);
        dbCommand.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = DbValue(command.CorrelationId);
        dbCommand.Parameters.Add("created_at", NpgsqlDbType.TimestampTz).Value = ToUtc(command.CreatedAt);
    }

    private static VendorPaymentAcknowledgmentRecord ReadRecord(NpgsqlDataReader reader)
    {
        return new VendorPaymentAcknowledgmentRecord(
            VendorPaymentAcknowledgmentId: reader.GetGuid(reader.GetOrdinal("vendor_payment_acknowledgment_id")),
            PaymentAttemptId: reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
            PaymentConfirmationId: reader.GetGuid(reader.GetOrdinal("payment_confirmation_id")),
            ParkingSessionId: GetNullableGuid(reader, "parking_session_id"),
            VendorSystemCode: reader.GetString(reader.GetOrdinal("vendor_system_code")),
            VendorSessionRef: GetNullableString(reader, "vendor_session_ref"),
            TicketNumber: GetNullableString(reader, "ticket_number"),
            CardNum: GetNullableString(reader, "card_num"),
            AcknowledgmentStatus: reader.GetString(reader.GetOrdinal("acknowledgment_status")),
            VendorCode: GetNullableString(reader, "vendor_code"),
            VendorMessage: GetNullableString(reader, "vendor_message"),
            RequestFeeMinorUnits: GetNullableInt64(reader, "request_fee_minor_units"),
            RequestCurrencyCode: GetNullableString(reader, "request_currency_code"),
            ConfirmedFeeMinorUnits: GetNullableInt64(reader, "confirmed_fee_minor_units"),
            VendorConfirmedAt: GetNullableDateTimeOffset(reader, "vendor_confirmed_at"),
            AttemptCount: reader.GetInt32(reader.GetOrdinal("attempt_count")),
            LastAttemptedAt: GetNullableDateTimeOffset(reader, "last_attempted_at"),
            NextRetryAt: GetNullableDateTimeOffset(reader, "next_retry_at"),
            IdempotencyKey: GetNullableString(reader, "idempotency_key"),
            CorrelationId: GetNullableGuid(reader, "correlation_id"),
            CreatedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
            UpdatedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at")));
    }

    private static object DbValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    }

    private static object DbValue(Guid? value)
    {
        return value.HasValue ? value.Value : DBNull.Value;
    }

    private static object DbValue(long? value)
    {
        return value.HasValue ? value.Value : DBNull.Value;
    }

    private static object DbValue(DateTimeOffset? value)
    {
        return value.HasValue ? ToUtc(value.Value) : DBNull.Value;
    }

    private static DateTimeOffset ToUtc(DateTimeOffset value)
    {
        return value.ToUniversalTime();
    }

    private static string? GetNullableString(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static Guid? GetNullableGuid(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static long? GetNullableInt64(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    private static DateTimeOffset? GetNullableDateTimeOffset(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }
}
