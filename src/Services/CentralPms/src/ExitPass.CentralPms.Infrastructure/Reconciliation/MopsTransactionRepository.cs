using ExitPass.CentralPms.Application.Reconciliation;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ExitPass.CentralPms.Infrastructure.Reconciliation;

/// <summary>
/// PostgreSQL-backed repository for MoPS continuity evidence imports.
///
/// BRD v1.2 Reference:
/// - Section 9.16 Monitoring and Administration
/// - Section 9.21 Audit and Traceability
///
/// SDD v1.2 Reference:
/// - Section 9.7 Recommended Database Functions
/// - Section 10 API Architecture
/// - Section 14.4 Structured Logging
///
/// ExitPass v1.2 Invariants Enforced:
/// - MoPS evidence is persisted only in reconciliation-owned records.
/// - Import does not create PaymentAttempt, PaymentConfirmation, ExitAuthorization, or provider outcome truth.
/// </summary>
public sealed class MopsTransactionRepository : IMopsTransactionRepository
{
    private readonly string _connectionString;
    private readonly ILogger<MopsTransactionRepository> _logger;

    /// <summary>
    /// Creates a MoPS transaction repository.
    /// </summary>
    public MopsTransactionRepository(
        string connectionString,
        ILogger<MopsTransactionRepository> logger)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<MopsImportResult> ImportAsync(
        ImportMopsTransactionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var duplicate = await TryReadDuplicateImportAsync(command, cancellationToken);
        if (duplicate is not null)
        {
            return duplicate;
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var siteGroupId = await ValidateReferencesAsync(connection, transaction, command, cancellationToken);
        var runId = Guid.NewGuid();
        var mopsId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var runCode = $"MOPS-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{runId.ToString("N")[..8]}";

        try
        {
            await InsertRunAsync(connection, transaction, command, runId, runCode, siteGroupId, cancellationToken);
            await InsertMopsRecordAsync(connection, transaction, command, mopsId, cancellationToken);
            await InsertReconciliationItemAsync(connection, transaction, command, runId, mopsId, itemId, cancellationToken);
            await ReconciliationEventPersistence.PersistAsync(
                connection,
                transaction,
                ReconciliationEventPersistence.MopsTransactionImported,
                "mops_transaction_records",
                "MopsTransactionRecord",
                mopsId,
                runId,
                command.ActorUserId,
                command.ImportedByServiceIdentityId,
                command.CorrelationId,
                itemId,
                "MoPS transaction imported as reconciliation evidence.",
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new MopsImportResult(
                mopsId,
                runId,
                itemId,
                "IMPORTED",
                runCode,
                WasDuplicate: false,
                command.CorrelationId);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken);

            _logger.LogWarning(
                ex,
                "Duplicate MoPS import detected by database uniqueness. source_system_code={SourceSystemCode} source_transaction_ref={SourceTransactionRef} source_batch_ref={SourceBatchRef} collection_reference={CollectionReference}",
                command.SourceSystemCode,
                command.SourceTransactionRef,
                command.SourceBatchRef,
                command.CollectionReference);

            return await TryReadDuplicateImportAsync(command, cancellationToken)
                ?? throw new MopsImportRejectedException(
                    "MOPS_IMPORT_DUPLICATE",
                    "A duplicate MoPS transaction was detected, but the existing import could not be read.");
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MopsTransactionRecord>> ListAsync(
        ListMopsTransactionsQuery query,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                m.mops_transaction_record_id,
                ri.reconciliation_run_id,
                ri.reconciliation_item_id,
                m.site_id,
                rr.site_group_id,
                rr.payment_rail_id,
                rr.vendor_system_id,
                m.parking_session_id,
                m.lane_id,
                m.source_system_code,
                m.source_transaction_ref,
                m.source_batch_ref,
                m.collection_reference,
                m.currency_code,
                m.amount,
                m.payment_method_label,
                m.continuity_reason_code,
                m.record_status::text AS record_status,
                m.captured_at,
                m.imported_at,
                m.evidence_ref,
                m.correlation_id
            FROM reconciliation.mops_transaction_records m
            LEFT JOIN LATERAL (
                SELECT *
                FROM reconciliation.reconciliation_items item
                WHERE item.mops_transaction_record_id = m.mops_transaction_record_id
                ORDER BY item.created_at DESC
                LIMIT 1
            ) ri ON true
            LEFT JOIN reconciliation.reconciliation_runs rr
              ON rr.reconciliation_run_id = ri.reconciliation_run_id
            WHERE (@site_id IS NULL OR m.site_id = @site_id)
              AND (@source_system_code IS NULL OR m.source_system_code = @source_system_code)
            ORDER BY m.imported_at DESC NULLS LAST, m.created_at DESC
            LIMIT @limit;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(sql, connection);
        dbCommand.Parameters.AddWithValue("site_id", (object?)query.SiteId ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("source_system_code", (object?)query.SourceSystemCode?.ToUpperInvariant() ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("limit", query.Limit);

        var records = new List<MopsTransactionRecord>();
        await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(ReadMopsTransaction(reader));
        }

        return records;
    }

    /// <inheritdoc />
    public async Task<MopsTransactionRecord> ReadAsync(
        ReadMopsTransactionQuery query,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                m.mops_transaction_record_id,
                ri.reconciliation_run_id,
                ri.reconciliation_item_id,
                m.site_id,
                rr.site_group_id,
                rr.payment_rail_id,
                rr.vendor_system_id,
                m.parking_session_id,
                m.lane_id,
                m.source_system_code,
                m.source_transaction_ref,
                m.source_batch_ref,
                m.collection_reference,
                m.currency_code,
                m.amount,
                m.payment_method_label,
                m.continuity_reason_code,
                m.record_status::text AS record_status,
                m.captured_at,
                m.imported_at,
                m.evidence_ref,
                m.correlation_id
            FROM reconciliation.mops_transaction_records m
            LEFT JOIN LATERAL (
                SELECT *
                FROM reconciliation.reconciliation_items item
                WHERE item.mops_transaction_record_id = m.mops_transaction_record_id
                ORDER BY item.created_at DESC
                LIMIT 1
            ) ri ON true
            LEFT JOIN reconciliation.reconciliation_runs rr
              ON rr.reconciliation_run_id = ri.reconciliation_run_id
            WHERE m.mops_transaction_record_id = @mops_transaction_record_id;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(sql, connection);
        dbCommand.Parameters.AddWithValue("mops_transaction_record_id", query.MopsTransactionRecordId);

        await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new MopsTransactionNotFoundException(query.MopsTransactionRecordId);
        }

        return ReadMopsTransaction(reader);
    }

    private async Task<MopsImportResult?> TryReadDuplicateImportAsync(
        ImportMopsTransactionCommand command,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                m.mops_transaction_record_id,
                ri.reconciliation_run_id,
                ri.reconciliation_item_id,
                m.record_status::text AS record_status,
                rr.run_code,
                m.correlation_id
            FROM reconciliation.mops_transaction_records m
            LEFT JOIN LATERAL (
                SELECT *
                FROM reconciliation.reconciliation_items item
                WHERE item.mops_transaction_record_id = m.mops_transaction_record_id
                ORDER BY item.created_at DESC
                LIMIT 1
            ) ri ON true
            LEFT JOIN reconciliation.reconciliation_runs rr
              ON rr.reconciliation_run_id = ri.reconciliation_run_id
            WHERE m.source_system_code = @source_system_code
              AND (
                    (@source_transaction_ref IS NOT NULL AND m.source_transaction_ref = @source_transaction_ref)
                 OR (@source_batch_ref IS NOT NULL AND @collection_reference IS NOT NULL
                     AND m.source_batch_ref = @source_batch_ref
                     AND m.collection_reference = @collection_reference)
              )
            ORDER BY m.created_at DESC
            LIMIT 1;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(sql, connection);
        AddNaturalKeyParameters(dbCommand, command);

        await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        if (reader.IsDBNull(reader.GetOrdinal("reconciliation_run_id")) ||
            reader.IsDBNull(reader.GetOrdinal("reconciliation_item_id")) ||
            reader.IsDBNull(reader.GetOrdinal("run_code")))
        {
            throw new MopsImportRejectedException(
                "MOPS_IMPORT_DUPLICATE_WITHOUT_RECONCILIATION_ITEM",
                "A duplicate MoPS transaction exists but is not linked to a reconciliation item.");
        }

        return new MopsImportResult(
            MopsTransactionRecordId: reader.GetGuid("mops_transaction_record_id"),
            ReconciliationRunId: reader.GetGuid("reconciliation_run_id"),
            ReconciliationItemId: reader.GetGuid("reconciliation_item_id"),
            RecordStatus: reader.GetString("record_status"),
            RunCode: reader.GetString("run_code"),
            WasDuplicate: true,
            CorrelationId: reader.GetNullableGuid("correlation_id") ?? command.CorrelationId);
    }

    private static async Task<Guid> ValidateReferencesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ImportMopsTransactionCommand command,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                s.site_group_id,
                EXISTS (
                    SELECT 1
                    FROM sites.site_groups sg
                    WHERE sg.site_group_id = @site_group_id
                ) AS site_group_exists,
                EXISTS (
                    SELECT 1
                    FROM payments.payment_rails pr
                    WHERE pr.payment_rail_id = @payment_rail_id
                ) AS payment_rail_exists,
                EXISTS (
                    SELECT 1
                    FROM integration.vendor_systems vs
                    WHERE vs.vendor_system_id = @vendor_system_id
                ) AS vendor_system_exists
            FROM sites.sites s
            WHERE s.site_id = @site_id;
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction);
        dbCommand.Parameters.AddWithValue("site_id", command.SiteId);
        dbCommand.Parameters.AddWithValue("site_group_id", (object?)command.SiteGroupId ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("payment_rail_id", (object?)command.PaymentRailId ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("vendor_system_id", (object?)command.VendorSystemId ?? DBNull.Value);

        await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new MopsImportRejectedException("SITE_NOT_FOUND", "The supplied site_id does not exist.");
        }

        var actualSiteGroupId = reader.GetGuid("site_group_id");
        var siteGroupExists = reader.GetBoolean(reader.GetOrdinal("site_group_exists"));
        var paymentRailExists = reader.GetBoolean(reader.GetOrdinal("payment_rail_exists"));
        var vendorSystemExists = reader.GetBoolean(reader.GetOrdinal("vendor_system_exists"));

        if (command.SiteGroupId.HasValue &&
            (!siteGroupExists || command.SiteGroupId.Value != actualSiteGroupId))
        {
            throw new MopsImportRejectedException(
                "SITE_GROUP_REFERENCE_INVALID",
                "The supplied site_group_id does not exist or does not own the supplied site_id.");
        }

        if (command.PaymentRailId.HasValue && !paymentRailExists)
        {
            throw new MopsImportRejectedException(
                "PAYMENT_RAIL_NOT_FOUND",
                "The supplied payment_rail_id does not exist.");
        }

        if (command.VendorSystemId.HasValue && !vendorSystemExists)
        {
            throw new MopsImportRejectedException(
                "VENDOR_SYSTEM_NOT_FOUND",
                "The supplied vendor_system_id does not exist.");
        }

        return command.SiteGroupId ?? actualSiteGroupId;
    }

    private static async Task InsertRunAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ImportMopsTransactionCommand command,
        Guid runId,
        string runCode,
        Guid siteGroupId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO reconciliation.reconciliation_runs (
                reconciliation_run_id,
                run_code,
                run_type,
                run_status,
                scope_type,
                site_group_id,
                site_id,
                payment_rail_id,
                vendor_system_id,
                source_batch_ref,
                window_start_at,
                window_end_at,
                completed_at,
                item_count,
                matched_count,
                exception_count,
                rejected_count,
                disputed_count,
                initiated_by_user_id,
                correlation_id,
                created_by_user_id,
                updated_by_user_id
            )
            VALUES (
                @run_id,
                @run_code,
                'MOPS_RECONCILIATION'::reconciliation.reconciliation_run_type_enum,
                'COMPLETED'::reconciliation.reconciliation_run_status_enum,
                CASE
                    WHEN @source_batch_ref IS NOT NULL THEN 'SOURCE_BATCH'::reconciliation.reconciliation_scope_type_enum
                    ELSE 'SITE'::reconciliation.reconciliation_scope_type_enum
                END,
                @site_group_id,
                @site_id,
                @payment_rail_id,
                @vendor_system_id,
                @source_batch_ref,
                @captured_at,
                @captured_at,
                now(),
                1,
                0,
                0,
                0,
                0,
                @actor_user_id,
                @correlation_id,
                @actor_user_id,
                @actor_user_id
            );
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction);
        dbCommand.Parameters.AddWithValue("run_id", runId);
        dbCommand.Parameters.AddWithValue("run_code", runCode);
        dbCommand.Parameters.AddWithValue("site_group_id", siteGroupId);
        dbCommand.Parameters.AddWithValue("site_id", command.SiteId);
        dbCommand.Parameters.AddWithValue("payment_rail_id", (object?)command.PaymentRailId ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("vendor_system_id", (object?)command.VendorSystemId ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("source_batch_ref", (object?)command.SourceBatchRef ?? (object?)command.SourceTransactionRef ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("captured_at", command.CapturedAt);
        dbCommand.Parameters.AddWithValue("actor_user_id", (object?)command.ActorUserId ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("correlation_id", command.CorrelationId);

        await dbCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertMopsRecordAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ImportMopsTransactionCommand command,
        Guid mopsId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO reconciliation.mops_transaction_records (
                mops_transaction_record_id,
                parking_session_id,
                site_id,
                lane_id,
                source_system_code,
                source_transaction_ref,
                source_batch_ref,
                collection_reference,
                currency_code,
                amount,
                payment_method_label,
                continuity_reason_code,
                record_status,
                captured_at,
                imported_at,
                evidence_ref,
                evidence_hash,
                captured_by_user_id,
                imported_by_service_identity_id,
                correlation_id,
                created_by_user_id,
                updated_by_user_id
            )
            VALUES (
                @mops_id,
                @parking_session_id,
                @site_id,
                @lane_id,
                @source_system_code,
                @source_transaction_ref,
                @source_batch_ref,
                @collection_reference,
                @currency_code,
                @amount,
                @payment_method_label,
                @continuity_reason_code,
                'IMPORTED'::reconciliation.mops_transaction_record_status_enum,
                @captured_at,
                now(),
                @evidence_ref,
                @evidence_hash,
                @actor_user_id,
                @imported_by_service_identity_id,
                @correlation_id,
                @actor_user_id,
                @actor_user_id
            );
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction);
        AddMopsParameters(dbCommand, command);
        dbCommand.Parameters.AddWithValue("mops_id", mopsId);

        await dbCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertReconciliationItemAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ImportMopsTransactionCommand command,
        Guid runId,
        Guid mopsId,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO reconciliation.reconciliation_items (
                reconciliation_item_id,
                reconciliation_run_id,
                mops_transaction_record_id,
                target_entity_type,
                target_entity_id,
                comparison_basis,
                item_status,
                match_status,
                actual_amount,
                currency_code,
                exception_reason_code,
                created_by_user_id,
                updated_by_user_id,
                correlation_id
            )
            VALUES (
                @item_id,
                @run_id,
                @mops_id,
                'MOPS_TRANSACTION',
                @mops_id,
                'MOPS_TO_CORE'::reconciliation.reconciliation_comparison_basis_enum,
                'PENDING'::reconciliation.reconciliation_item_status_enum,
                'NOT_EVALUATED'::reconciliation.reconciliation_match_status_enum,
                @amount,
                @currency_code,
                @continuity_reason_code,
                @actor_user_id,
                @actor_user_id,
                @correlation_id
            );
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction);
        dbCommand.Parameters.AddWithValue("item_id", itemId);
        dbCommand.Parameters.AddWithValue("run_id", runId);
        dbCommand.Parameters.AddWithValue("mops_id", mopsId);
        dbCommand.Parameters.AddWithValue("amount", (object?)command.Amount ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("currency_code", (object?)command.CurrencyCode ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("continuity_reason_code", command.ContinuityReasonCode);
        dbCommand.Parameters.AddWithValue("actor_user_id", (object?)command.ActorUserId ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("correlation_id", command.CorrelationId);

        await dbCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static void AddNaturalKeyParameters(
        NpgsqlCommand dbCommand,
        ImportMopsTransactionCommand command)
    {
        dbCommand.Parameters.AddWithValue("source_system_code", command.SourceSystemCode);
        dbCommand.Parameters.AddWithValue("source_transaction_ref", (object?)command.SourceTransactionRef ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("source_batch_ref", (object?)command.SourceBatchRef ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("collection_reference", (object?)command.CollectionReference ?? DBNull.Value);
    }

    private static void AddMopsParameters(
        NpgsqlCommand dbCommand,
        ImportMopsTransactionCommand command)
    {
        dbCommand.Parameters.AddWithValue("parking_session_id", (object?)command.ParkingSessionId ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("site_id", command.SiteId);
        dbCommand.Parameters.AddWithValue("lane_id", (object?)command.LaneId ?? DBNull.Value);
        AddNaturalKeyParameters(dbCommand, command);
        dbCommand.Parameters.AddWithValue("currency_code", (object?)command.CurrencyCode ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("amount", (object?)command.Amount ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("payment_method_label", (object?)command.PaymentMethodLabel ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("continuity_reason_code", command.ContinuityReasonCode);
        dbCommand.Parameters.AddWithValue("captured_at", command.CapturedAt);
        dbCommand.Parameters.AddWithValue("evidence_ref", (object?)command.EvidenceRef ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("evidence_hash", (object?)command.EvidenceHash ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("actor_user_id", (object?)command.ActorUserId ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("imported_by_service_identity_id", (object?)command.ImportedByServiceIdentityId ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("correlation_id", command.CorrelationId);
    }

    private static MopsTransactionRecord ReadMopsTransaction(NpgsqlDataReader reader)
    {
        return new MopsTransactionRecord(
            MopsTransactionRecordId: reader.GetGuid("mops_transaction_record_id"),
            ReconciliationRunId: reader.GetNullableGuid("reconciliation_run_id"),
            ReconciliationItemId: reader.GetNullableGuid("reconciliation_item_id"),
            SiteId: reader.GetGuid("site_id"),
            SiteGroupId: reader.GetNullableGuid("site_group_id"),
            PaymentRailId: reader.GetNullableGuid("payment_rail_id"),
            VendorSystemId: reader.GetNullableGuid("vendor_system_id"),
            ParkingSessionId: reader.GetNullableGuid("parking_session_id"),
            LaneId: reader.GetNullableGuid("lane_id"),
            SourceSystemCode: reader.GetString("source_system_code"),
            SourceTransactionRef: reader.GetNullableString("source_transaction_ref"),
            SourceBatchRef: reader.GetNullableString("source_batch_ref"),
            CollectionReference: reader.GetNullableString("collection_reference"),
            CurrencyCode: reader.GetNullableString("currency_code"),
            Amount: reader.GetNullableDecimal("amount"),
            PaymentMethodLabel: reader.GetNullableString("payment_method_label"),
            ContinuityReasonCode: reader.GetString("continuity_reason_code"),
            RecordStatus: reader.GetString("record_status"),
            CapturedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("captured_at")),
            ImportedAt: reader.GetNullableDateTimeOffset("imported_at"),
            EvidenceRef: reader.GetNullableString("evidence_ref"),
            CorrelationId: reader.GetNullableGuid("correlation_id"));
    }
}
