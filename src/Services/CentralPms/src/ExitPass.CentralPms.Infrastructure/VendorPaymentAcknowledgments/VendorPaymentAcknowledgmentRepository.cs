using ExitPass.CentralPms.Application.VendorPaymentAcknowledgments;
using Npgsql;
using NpgsqlTypes;
using System.Text;

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
                ps.site_id,
                ps.site_group_id,
                ps.vendor_system_id,
                ps.source_adapter_identity_id,
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
              AND ps.source_adapter_identity_id IS NOT NULL
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
            RequestCurrencyCode: reader.GetString(reader.GetOrdinal("request_currency_code")).Trim())
        {
            SiteId = reader.GetGuid(reader.GetOrdinal("site_id")),
            SiteGroupId = reader.GetGuid(reader.GetOrdinal("site_group_id")),
            VendorSystemId = reader.GetGuid(reader.GetOrdinal("vendor_system_id")),
            SourceAdapterIdentityId = reader.GetGuid(reader.GetOrdinal("source_adapter_identity_id"))
        };
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
    public async Task<IReadOnlyList<VendorPaymentAcknowledgmentRecord>> FindDueRetryPendingAsync(
        DateTimeOffset utcNow,
        int limit,
        CancellationToken cancellationToken)
    {
        var boundedLimit = limit <= 0 ? 25 : Math.Min(limit, 100);
        var sql = $$"""
            SELECT
                {{SelectRecordColumns}}
            FROM integration.vendor_payment_acknowledgments
            WHERE acknowledgment_status = 'RETRY_PENDING'::integration.vendor_payment_acknowledgment_status_enum
              AND (next_retry_at IS NULL OR next_retry_at <= @utc_now)
            ORDER BY COALESCE(next_retry_at, created_at), updated_at, vendor_payment_acknowledgment_id
            LIMIT @limit;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(sql, connection);
        dbCommand.Parameters.Add("utc_now", NpgsqlDbType.TimestampTz).Value = ToUtc(utcNow);
        dbCommand.Parameters.Add("limit", NpgsqlDbType.Integer).Value = boundedLimit;

        await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);
        var records = new List<VendorPaymentAcknowledgmentRecord>(boundedLimit);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(ReadRecord(reader));
        }

        return records;
    }

    /// <inheritdoc />
    public async Task<VendorPaymentAcknowledgmentSearchResult> SearchAsync(
        SearchVendorPaymentAcknowledgmentsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var pageIndex = query.PageIndex < 0 ? 0 : Math.Min(query.PageIndex, 10000);
        var pageSize = query.PageSize <= 0 ? 25 : Math.Min(query.PageSize, 100);
        var limitPlusOne = pageSize + 1;
        var offset = pageIndex * pageSize;
        var whereClause = BuildSearchWhereClause(query);

        var listSql = $$"""
            SELECT
                {{SelectRecordColumns}}
            FROM integration.vendor_payment_acknowledgments
            {{whereClause}}
            ORDER BY created_at DESC, updated_at DESC, vendor_payment_acknowledgment_id DESC
            LIMIT @limit
            OFFSET @offset;
            """;

        var bucketSql = $"""
            SELECT
                COUNT(*) FILTER (WHERE acknowledgment_status = 'PENDING'::integration.vendor_payment_acknowledgment_status_enum) AS pending_count,
                COUNT(*) FILTER (WHERE acknowledgment_status = 'RETRY_PENDING'::integration.vendor_payment_acknowledgment_status_enum) AS retry_pending_count,
                COUNT(*) FILTER (WHERE acknowledgment_status = 'FAILED'::integration.vendor_payment_acknowledgment_status_enum) AS failed_count,
                COUNT(*) FILTER (WHERE acknowledgment_status = 'CONFIRMED'::integration.vendor_payment_acknowledgment_status_enum) AS confirmed_count,
                COUNT(*) FILTER (WHERE acknowledgment_status = 'SKIPPED_DISABLED'::integration.vendor_payment_acknowledgment_status_enum) AS skipped_disabled_count,
                COUNT(*) FILTER (WHERE acknowledgment_status = 'CANCELLED'::integration.vendor_payment_acknowledgment_status_enum) AS cancelled_count
            FROM integration.vendor_payment_acknowledgments
            {whereClause};
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);

        await using var listCommand = new NpgsqlCommand(listSql, connection);
        AddSearchParameters(listCommand, query);
        listCommand.Parameters.Add("limit", NpgsqlDbType.Integer).Value = limitPlusOne;
        listCommand.Parameters.Add("offset", NpgsqlDbType.Integer).Value = offset;

        var records = new List<VendorPaymentAcknowledgmentRecord>(limitPlusOne);
        await using (var reader = await listCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                records.Add(ReadRecord(reader));
            }
        }

        await using var bucketCommand = new NpgsqlCommand(bucketSql, connection);
        AddSearchParameters(bucketCommand, query);

        VendorPaymentAcknowledgmentStatusBucketCounts buckets;
        await using (var reader = await bucketCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                buckets = new VendorPaymentAcknowledgmentStatusBucketCounts(0, 0, 0, 0, 0, 0);
            }
            else
            {
                buckets = new VendorPaymentAcknowledgmentStatusBucketCounts(
                    Pending: ToInt32(reader, "pending_count"),
                    RetryPending: ToInt32(reader, "retry_pending_count"),
                    Failed: ToInt32(reader, "failed_count"),
                    Confirmed: ToInt32(reader, "confirmed_count"),
                    SkippedDisabled: ToInt32(reader, "skipped_disabled_count"),
                    Cancelled: ToInt32(reader, "cancelled_count"));
            }
        }

        var hasMore = records.Count > pageSize;
        if (hasMore)
        {
            records.RemoveAt(records.Count - 1);
        }

        return new VendorPaymentAcknowledgmentSearchResult(records, buckets, pageIndex, pageSize, hasMore);
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

    private static string BuildSearchWhereClause(SearchVendorPaymentAcknowledgmentsQuery query)
    {
        var where = new StringBuilder("WHERE 1 = 1");

        if (!string.IsNullOrWhiteSpace(query.AcknowledgmentStatus))
        {
            where.AppendLine();
            where.Append("  AND acknowledgment_status = @acknowledgment_status::integration.vendor_payment_acknowledgment_status_enum");
        }

        if (!string.IsNullOrWhiteSpace(query.VendorSystemCode))
        {
            where.AppendLine();
            where.Append("  AND UPPER(vendor_system_code) = UPPER(@vendor_system_code)");
        }

        if (query.PaymentAttemptId.HasValue)
        {
            where.AppendLine();
            where.Append("  AND payment_attempt_id = @payment_attempt_id");
        }

        if (query.PaymentConfirmationId.HasValue)
        {
            where.AppendLine();
            where.Append("  AND payment_confirmation_id = @payment_confirmation_id");
        }

        if (query.ParkingSessionId.HasValue)
        {
            where.AppendLine();
            where.Append("  AND parking_session_id = @parking_session_id");
        }

        if (!string.IsNullOrWhiteSpace(query.TicketNumber))
        {
            where.AppendLine();
            where.Append("  AND ticket_number = @ticket_number");
        }

        if (!string.IsNullOrWhiteSpace(query.CardNum))
        {
            where.AppendLine();
            where.Append("  AND card_num = @card_num");
        }

        if (query.CorrelationId.HasValue)
        {
            where.AppendLine();
            where.Append("  AND correlation_id = @correlation_id");
        }

        if (query.CreatedFrom.HasValue)
        {
            where.AppendLine();
            where.Append("  AND created_at >= @created_from");
        }

        if (query.CreatedTo.HasValue)
        {
            where.AppendLine();
            where.Append("  AND created_at <= @created_to");
        }

        if (query.LastAttemptedFrom.HasValue)
        {
            where.AppendLine();
            where.Append("  AND last_attempted_at >= @last_attempted_from");
        }

        if (query.LastAttemptedTo.HasValue)
        {
            where.AppendLine();
            where.Append("  AND last_attempted_at <= @last_attempted_to");
        }

        if (query.NextRetryDueOnly)
        {
            where.AppendLine();
            where.Append("  AND acknowledgment_status = 'RETRY_PENDING'::integration.vendor_payment_acknowledgment_status_enum");
            where.AppendLine();
            where.Append("  AND (next_retry_at IS NULL OR next_retry_at <= @utc_now)");
        }

        return where.ToString();
    }

    private static void AddSearchParameters(
        NpgsqlCommand dbCommand,
        SearchVendorPaymentAcknowledgmentsQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.AcknowledgmentStatus))
        {
            dbCommand.Parameters.Add("acknowledgment_status", NpgsqlDbType.Text).Value = query.AcknowledgmentStatus.Trim();
        }

        if (!string.IsNullOrWhiteSpace(query.VendorSystemCode))
        {
            dbCommand.Parameters.Add("vendor_system_code", NpgsqlDbType.Text).Value = query.VendorSystemCode.Trim();
        }

        if (query.PaymentAttemptId.HasValue)
        {
            dbCommand.Parameters.Add("payment_attempt_id", NpgsqlDbType.Uuid).Value = query.PaymentAttemptId.Value;
        }

        if (query.PaymentConfirmationId.HasValue)
        {
            dbCommand.Parameters.Add("payment_confirmation_id", NpgsqlDbType.Uuid).Value = query.PaymentConfirmationId.Value;
        }

        if (query.ParkingSessionId.HasValue)
        {
            dbCommand.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = query.ParkingSessionId.Value;
        }

        if (!string.IsNullOrWhiteSpace(query.TicketNumber))
        {
            dbCommand.Parameters.Add("ticket_number", NpgsqlDbType.Text).Value = query.TicketNumber.Trim();
        }

        if (!string.IsNullOrWhiteSpace(query.CardNum))
        {
            dbCommand.Parameters.Add("card_num", NpgsqlDbType.Text).Value = query.CardNum.Trim();
        }

        if (query.CorrelationId.HasValue)
        {
            dbCommand.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = query.CorrelationId.Value;
        }

        if (query.CreatedFrom.HasValue)
        {
            dbCommand.Parameters.Add("created_from", NpgsqlDbType.TimestampTz).Value = ToUtc(query.CreatedFrom.Value);
        }

        if (query.CreatedTo.HasValue)
        {
            dbCommand.Parameters.Add("created_to", NpgsqlDbType.TimestampTz).Value = ToUtc(query.CreatedTo.Value);
        }

        if (query.LastAttemptedFrom.HasValue)
        {
            dbCommand.Parameters.Add("last_attempted_from", NpgsqlDbType.TimestampTz).Value = ToUtc(query.LastAttemptedFrom.Value);
        }

        if (query.LastAttemptedTo.HasValue)
        {
            dbCommand.Parameters.Add("last_attempted_to", NpgsqlDbType.TimestampTz).Value = ToUtc(query.LastAttemptedTo.Value);
        }

        if (query.NextRetryDueOnly)
        {
            dbCommand.Parameters.Add("utc_now", NpgsqlDbType.TimestampTz).Value = ToUtc(query.UtcNow);
        }
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

    private static int ToInt32(NpgsqlDataReader reader, string columnName)
    {
        var value = reader.GetInt64(reader.GetOrdinal(columnName));
        return checked((int)value);
    }
}
