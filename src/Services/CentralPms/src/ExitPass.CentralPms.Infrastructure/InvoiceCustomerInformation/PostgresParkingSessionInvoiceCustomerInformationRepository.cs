using System.Data;
using ExitPass.CentralPms.Application.SalesInvoiceCustomerInformation;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.SalesInvoiceCustomerInformation;

public sealed class PostgresParkingSessionInvoiceCustomerInformationRepository(string connectionString)
    : IParkingSessionInvoiceCustomerInformationRepository
{
    private const string ReadRecordSql = """
        SELECT parking_session_id, customer_name, customer_address, customer_tin,
               business_style, row_version, created_at, updated_at
        FROM core.parking_session_invoice_customer_information
        WHERE parking_session_id = @parking_session_id;
        """;

    public async Task<InvoiceCustomerInformationReadResult> ReadAsync(
        Guid parkingSessionId,
        InvoiceCustomerInformationScope scope,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT ps.parking_session_id, ci.customer_name, ci.customer_address, ci.customer_tin,
                   ci.business_style, ci.row_version, ci.created_at, ci.updated_at
            FROM core.parking_sessions ps
            LEFT JOIN core.parking_session_invoice_customer_information ci
              ON ci.parking_session_id = ps.parking_session_id
            WHERE ps.parking_session_id = @parking_session_id
              AND ps.site_id = @site_id
              AND (@site_group_id IS NULL OR ps.site_group_id = @site_group_id);
            """;

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
            AddScopeParameters(command, parkingSessionId, scope);
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return new(InvoiceCustomerInformationReadStatus.ParkingSessionNotFound, null);
            }

            if (reader.IsDBNull(reader.GetOrdinal("row_version")))
            {
                return new(InvoiceCustomerInformationReadStatus.NotSupplied, null);
            }

            return new(InvoiceCustomerInformationReadStatus.Found, MapRecord(reader));
        }
        catch (NpgsqlException)
        {
            return new(InvoiceCustomerInformationReadStatus.SourceUnavailable, null);
        }
    }

    public async Task<InvoiceCustomerInformationSaveResult> SaveAsync(
        SaveParkingSessionInvoiceCustomerInformationCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

            if (!await LockAuthorizedParkingSessionAsync(connection, transaction, request, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result(InvoiceCustomerInformationSaveStatus.ParkingSessionNotFound);
            }

            if (await HasFiscalSnapshotLockAsync(connection, transaction, request.ParkingSessionId, cancellationToken))
            {
                await RecordAuditAsync(connection, transaction, request, "SNAPSHOT_LOCK_REJECTION", "REJECTED", "CUSTOMER_INFORMATION_FISCAL_SNAPSHOT_LOCKED", [], cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Result(InvoiceCustomerInformationSaveStatus.FiscalSnapshotLocked);
            }

            var current = await ReadRecordAsync(connection, transaction, request.ParkingSessionId, cancellationToken);
            if (current is null)
            {
                if (request.ExpectedVersion.HasValue)
                {
                    await RecordAuditAsync(connection, transaction, request, "VERSION_CONFLICT", "REJECTED", "CUSTOMER_INFORMATION_VERSION_CONFLICT", [], cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return Result(InvoiceCustomerInformationSaveStatus.VersionConflict);
                }

                var created = await InsertAsync(connection, transaction, request, cancellationToken);
                await RecordAuditAsync(connection, transaction, request, "CREATE", "SUCCESS", null, ChangedFields(null, created), cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Result(InvoiceCustomerInformationSaveStatus.Created, created);
            }

            var identical = HasSameValues(current, request);
            if (!request.ExpectedVersion.HasValue)
            {
                if (identical)
                {
                    await RecordAuditAsync(connection, transaction, request, "CREATE_REPLAY", "NO_OP", null, [], cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return Result(InvoiceCustomerInformationSaveStatus.Unchanged, current);
                }

                await RecordAuditAsync(connection, transaction, request, "VERSION_CONFLICT", "REJECTED", "CUSTOMER_INFORMATION_VERSION_CONFLICT", [], cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Result(InvoiceCustomerInformationSaveStatus.VersionConflict, current);
            }

            if (request.ExpectedVersion.Value != current.RowVersion)
            {
                await RecordAuditAsync(connection, transaction, request, "VERSION_CONFLICT", "REJECTED", "CUSTOMER_INFORMATION_VERSION_CONFLICT", [], cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Result(InvoiceCustomerInformationSaveStatus.VersionConflict, current);
            }

            if (identical)
            {
                await RecordAuditAsync(connection, transaction, request, "UPDATE_REPLAY", "NO_OP", null, [], cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Result(InvoiceCustomerInformationSaveStatus.Unchanged, current);
            }

            var updated = await UpdateAsync(connection, transaction, request, current.RowVersion, cancellationToken);
            await RecordAuditAsync(connection, transaction, request, "UPDATE", "SUCCESS", null, ChangedFields(current, updated), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result(InvoiceCustomerInformationSaveStatus.Updated, updated);
        }
        catch (NpgsqlException)
        {
            return Result(InvoiceCustomerInformationSaveStatus.SourceUnavailable);
        }
    }

    private static async Task<bool> LockAuthorizedParkingSessionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SaveParkingSessionInvoiceCustomerInformationCommand request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT 1
            FROM core.parking_sessions
            WHERE parking_session_id = @parking_session_id
              AND site_id = @site_id
              AND (@site_group_id IS NULL OR site_group_id = @site_group_id)
            FOR UPDATE;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddScopeParameters(command, request.ParkingSessionId, request.Scope);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<bool> HasFiscalSnapshotLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid parkingSessionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM core.fiscal_issuance_references
                WHERE parking_session_id = @parking_session_id
                  AND is_active = true
                  AND is_superseded = false
                  AND (
                      invoice_customer_information_snapshot_captured_at IS NOT NULL
                      OR (
                          fiscal_issuance_state IN (
                              'FISCAL_ISSUANCE_RECORDED',
                              'FISCAL_ISSUANCE_REPLAYED',
                              'FISCAL_ISSUANCE_RECONCILED')
                          AND pos_server_fiscal_document_id IS NOT NULL
                          AND fiscal_identity_id IS NOT NULL
                          AND fiscal_sequence_policy_id IS NOT NULL
                          AND fiscal_sequence_value IS NOT NULL
                          AND fiscal_document_number IS NOT NULL
                          AND fiscal_number_assigned_at IS NOT NULL
                          AND fiscal_issuance_evidence_status = 'FISCAL_DOCUMENT_NUMBER_ASSIGNED'
                          AND fiscal_number_assignment_state = 'ASSIGNED')));
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = parkingSessionId;
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<ParkingSessionInvoiceCustomerInformationRecord?> ReadRecordAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid parkingSessionId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(ReadRecordSql, connection, transaction);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = parkingSessionId;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapRecord(reader) : null;
    }

    private static async Task<ParkingSessionInvoiceCustomerInformationRecord> InsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SaveParkingSessionInvoiceCustomerInformationCommand request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO core.parking_session_invoice_customer_information (
                parking_session_id, customer_name, customer_address, customer_tin, business_style,
                created_by_user_id, created_by_service_identity_id, created_source_channel,
                updated_by_user_id, updated_by_service_identity_id, updated_source_channel)
            VALUES (
                @parking_session_id, @customer_name, @customer_address, @customer_tin, @business_style,
                @actor_user_id, @actor_service_identity_id, @source_channel,
                @actor_user_id, @actor_service_identity_id, @source_channel)
            RETURNING parking_session_id, customer_name, customer_address, customer_tin,
                      business_style, row_version, created_at, updated_at;
            """;
        await using var command = BuildMutationCommand(sql, connection, transaction, request);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Customer information insert returned no row.");
        return MapRecord(reader);
    }

    private static async Task<ParkingSessionInvoiceCustomerInformationRecord> UpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SaveParkingSessionInvoiceCustomerInformationCommand request,
        long currentVersion,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE core.parking_session_invoice_customer_information
            SET customer_name = @customer_name,
                customer_address = @customer_address,
                customer_tin = @customer_tin,
                business_style = @business_style,
                row_version = row_version + 1,
                updated_at = now(),
                updated_by_user_id = @actor_user_id,
                updated_by_service_identity_id = @actor_service_identity_id,
                updated_source_channel = @source_channel
            WHERE parking_session_id = @parking_session_id
              AND row_version = @current_version
            RETURNING parking_session_id, customer_name, customer_address, customer_tin,
                      business_style, row_version, created_at, updated_at;
            """;
        await using var command = BuildMutationCommand(sql, connection, transaction, request);
        command.Parameters.Add("current_version", NpgsqlDbType.Bigint).Value = currentVersion;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Customer information update lost its serialized version lock.");
        return MapRecord(reader);
    }

    private static NpgsqlCommand BuildMutationCommand(
        string sql,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SaveParkingSessionInvoiceCustomerInformationCommand request)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = request.ParkingSessionId;
        AddNullableText(command, "customer_name", request.CustomerName);
        AddNullableText(command, "customer_address", request.CustomerAddress);
        AddNullableText(command, "customer_tin", request.CustomerTin);
        AddNullableText(command, "business_style", request.BusinessStyle);
        command.Parameters.Add("actor_user_id", NpgsqlDbType.Uuid).Value = request.Actor.UserId.HasValue ? request.Actor.UserId.Value : DBNull.Value;
        command.Parameters.Add("actor_service_identity_id", NpgsqlDbType.Uuid).Value = request.Actor.ServiceIdentityId.HasValue ? request.Actor.ServiceIdentityId.Value : DBNull.Value;
        command.Parameters.Add("source_channel", NpgsqlDbType.Varchar).Value = request.Actor.SourceChannel;
        return command;
    }

    private static async Task RecordAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SaveParkingSessionInvoiceCustomerInformationCommand request,
        string operation,
        string result,
        string? reasonCode,
        IReadOnlyList<string> changedFields,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO audit.audit_events (
                event_type, event_category, event_result, event_reason_code,
                target_entity_type, target_entity_id, related_entity_type, related_entity_id,
                source_schema, source_service_name,
                source_channel, actor_user_id, actor_service_identity_id, summary,
                occurred_at, correlation_id)
            VALUES (
                @event_type, 'DOMAIN_STATE_CHANGE', @event_result::audit.audit_event_result_enum, @reason_code,
                'ParkingSessionInvoiceCustomerInformation', @parking_session_id, 'Site', @site_id,
                'core', 'central-pms',
                @source_channel, @actor_user_id, @actor_service_identity_id, @summary,
                now(), @correlation_id);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("event_type", $"INVOICE_CUSTOMER_INFORMATION_{operation}");
        command.Parameters.AddWithValue("event_result", result);
        command.Parameters.Add("reason_code", NpgsqlDbType.Varchar).Value = (object?)reasonCode ?? DBNull.Value;
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = request.ParkingSessionId;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = request.Scope.SiteId;
        command.Parameters.AddWithValue("source_channel", request.Actor.SourceChannel);
        command.Parameters.Add("actor_user_id", NpgsqlDbType.Uuid).Value = request.Actor.UserId.HasValue ? request.Actor.UserId.Value : DBNull.Value;
        command.Parameters.Add("actor_service_identity_id", NpgsqlDbType.Uuid).Value = request.Actor.ServiceIdentityId.HasValue ? request.Actor.ServiceIdentityId.Value : DBNull.Value;
        command.Parameters.AddWithValue("summary", changedFields.Count == 0
            ? $"Customer information {operation.ToLowerInvariant()}."
            : $"Customer information {operation.ToLowerInvariant()}; changed fields: {string.Join(",", changedFields)}.");
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = request.CorrelationId;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static IReadOnlyList<string> ChangedFields(
        ParkingSessionInvoiceCustomerInformationRecord? before,
        ParkingSessionInvoiceCustomerInformationRecord after)
    {
        var fields = new List<string>();
        if (before?.CustomerName != after.CustomerName) fields.Add("customer_name");
        if (before?.CustomerAddress != after.CustomerAddress) fields.Add("customer_address");
        if (before?.CustomerTin != after.CustomerTin) fields.Add("customer_tin");
        if (before?.BusinessStyle != after.BusinessStyle) fields.Add("business_style");
        return fields;
    }

    private static bool HasSameValues(ParkingSessionInvoiceCustomerInformationRecord current, SaveParkingSessionInvoiceCustomerInformationCommand request) =>
        current.CustomerName == request.CustomerName && current.CustomerAddress == request.CustomerAddress &&
        current.CustomerTin == request.CustomerTin && current.BusinessStyle == request.BusinessStyle;

    private static ParkingSessionInvoiceCustomerInformationRecord MapRecord(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            NullableString(reader, "customer_name"), NullableString(reader, "customer_address"),
            NullableString(reader, "customer_tin"), NullableString(reader, "business_style"),
            reader.GetInt64(reader.GetOrdinal("row_version")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at")));

    private static string? NullableString(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static void AddScopeParameters(NpgsqlCommand command, Guid parkingSessionId, InvoiceCustomerInformationScope scope)
    {
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = parkingSessionId;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = scope.SiteId;
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = scope.SiteGroupId.HasValue ? scope.SiteGroupId.Value : DBNull.Value;
    }

    private static void AddNullableText(NpgsqlCommand command, string name, string? value) =>
        command.Parameters.Add(name, NpgsqlDbType.Varchar).Value = (object?)value ?? DBNull.Value;

    private static InvoiceCustomerInformationSaveResult Result(InvoiceCustomerInformationSaveStatus status, ParkingSessionInvoiceCustomerInformationRecord? record = null) =>
        new(status, record, []);
}
