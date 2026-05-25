using ExitPass.CentralPms.Application.Reconciliation;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ExitPass.CentralPms.Infrastructure.Reconciliation;

/// <summary>
/// PostgreSQL-backed repository for reconciliation run and item operations.
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
/// - Writes are limited to reconciliation run headers in this slice.
/// - Payment attempts, payment confirmations, exit authorizations, provider outcomes, and gate consumptions remain untouched payment/exit truth.
/// </summary>
public sealed class ReconciliationRunItemRepository : IReconciliationRunItemRepository
{
    private const string ItemGenerationDeferredMessage =
        "Automatic reconciliation item generation is not performed in this slice; source-specific evidence flows such as MoPS import create items explicitly.";

    private readonly string _connectionString;
    private readonly ILogger<ReconciliationRunItemRepository> _logger;

    /// <summary>
    /// Creates a reconciliation run and item repository.
    /// </summary>
    public ReconciliationRunItemRepository(
        string connectionString,
        ILogger<ReconciliationRunItemRepository> logger)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ReconciliationRunCreateResult> CreateRunAsync(
        CreateReconciliationRunCommand command,
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
                incident_record_id,
                payment_rail_id,
                vendor_system_id,
                source_batch_ref,
                window_start_at,
                window_end_at,
                completed_at,
                failed_at,
                item_count,
                matched_count,
                exception_count,
                rejected_count,
                disputed_count,
                initiated_by_user_id,
                initiated_by_service_identity_id,
                correlation_id,
                created_by_user_id,
                created_by_service_identity_id,
                updated_by_user_id,
                updated_by_service_identity_id
            )
            VALUES (
                @reconciliation_run_id,
                @run_code,
                @run_type::reconciliation.reconciliation_run_type_enum,
                @run_status::reconciliation.reconciliation_run_status_enum,
                @scope_type::reconciliation.reconciliation_scope_type_enum,
                @site_group_id,
                @site_id,
                @incident_record_id,
                @payment_rail_id,
                @vendor_system_id,
                @source_batch_ref,
                @window_start_at,
                @window_end_at,
                CASE WHEN @run_status = 'COMPLETED' THEN now() ELSE NULL END,
                CASE WHEN @run_status = 'FAILED' THEN now() ELSE NULL END,
                0,
                0,
                0,
                0,
                0,
                @actor_user_id,
                @service_identity_id,
                @correlation_id,
                @actor_user_id,
                @service_identity_id,
                @actor_user_id,
                @service_identity_id
            )
            RETURNING
                reconciliation_run_id,
                run_code,
                run_type::text AS run_type,
                run_status::text AS run_status,
                scope_type::text AS scope_type,
                item_count,
                correlation_id;
            """;

        var runId = Guid.NewGuid();
        var runCode = command.RunCode ?? $"RECON-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{runId.ToString("N")[..8]}";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(sql, connection);
        dbCommand.Parameters.AddWithValue("reconciliation_run_id", runId);
        dbCommand.Parameters.AddWithValue("run_code", runCode);
        dbCommand.Parameters.AddWithValue("run_type", command.RunType);
        dbCommand.Parameters.AddWithValue("run_status", command.RunStatus);
        dbCommand.Parameters.AddWithValue("scope_type", command.ScopeType);
        dbCommand.Parameters.AddWithValue("site_group_id", (object?)command.SiteGroupId ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("site_id", (object?)command.SiteId ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("incident_record_id", (object?)command.IncidentRecordId ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("payment_rail_id", (object?)command.PaymentRailId ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("vendor_system_id", (object?)command.VendorSystemId ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("source_batch_ref", (object?)command.SourceBatchRef ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("window_start_at", (object?)command.WindowStartAt ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("window_end_at", (object?)command.WindowEndAt ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("actor_user_id", (object?)command.ActorUserId ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("service_identity_id", (object?)command.ServiceIdentityId ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("correlation_id", command.CorrelationId);

        try
        {
            await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);

            return new ReconciliationRunCreateResult(
                reader.GetGuid("reconciliation_run_id"),
                reader.GetString("run_code"),
                reader.GetString("run_type"),
                reader.GetString("run_status"),
                reader.GetString("scope_type"),
                reader.GetInt32("item_count"),
                ItemGenerationPerformed: false,
                ItemGenerationDeferredMessage,
                reader.GetGuid("correlation_id"));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            _logger.LogWarning(ex, "Duplicate reconciliation run code rejected. run_code={RunCode}", runCode);
            throw new ReconciliationRunItemRejectedException(
                "RECONCILIATION_RUN_CODE_ALREADY_EXISTS",
                "A reconciliation run with the supplied run_code already exists.");
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            _logger.LogWarning(ex, "Reconciliation run reference validation failed. constraint={ConstraintName}", ex.ConstraintName);
            throw new ReconciliationRunItemRejectedException(
                MapForeignKeyErrorCode(ex.ConstraintName),
                "One or more supplied reconciliation run references do not exist.");
        }
    }

    /// <inheritdoc />
    public async Task<ReconciliationRunDetailRecord> ReadRunAsync(
        ReadReconciliationRunQuery query,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(RunDetailSql("WHERE rr.reconciliation_run_id = @reconciliation_run_id"), connection);
        dbCommand.Parameters.AddWithValue("reconciliation_run_id", query.ReconciliationRunId);

        await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new ReconciliationRunNotFoundException(query.ReconciliationRunId);
        }

        return ReadRun(reader);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReconciliationItemRecord>> ListRunItemsAsync(
        ListReconciliationRunItemsQuery query,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                reconciliation_item_id,
                reconciliation_run_id,
                mops_transaction_record_id,
                manual_gate_log_id,
                payment_attempt_id,
                payment_confirmation_id,
                provider_outcome_id,
                target_entity_type,
                target_entity_id,
                comparison_basis::text AS comparison_basis,
                item_status::text AS item_status,
                match_status::text AS match_status,
                expected_amount,
                actual_amount,
                currency_code,
                variance_amount,
                exception_reason_code,
                resolved_at,
                resolved_by_user_id,
                created_at,
                updated_at,
                correlation_id
            FROM reconciliation.reconciliation_items
            WHERE reconciliation_run_id = @reconciliation_run_id
            ORDER BY created_at, reconciliation_item_id
            LIMIT @limit;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        if (!await RunExistsAsync(connection, query.ReconciliationRunId, cancellationToken))
        {
            throw new ReconciliationRunNotFoundException(query.ReconciliationRunId);
        }

        await using var dbCommand = new NpgsqlCommand(sql, connection);
        dbCommand.Parameters.AddWithValue("reconciliation_run_id", query.ReconciliationRunId);
        dbCommand.Parameters.AddWithValue("limit", query.Limit);

        var records = new List<ReconciliationItemRecord>();
        await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(ReadItem(reader));
        }

        return records;
    }

    /// <inheritdoc />
    public async Task<ReconciliationItemRecord> ReadItemAsync(
        ReadReconciliationItemQuery query,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                reconciliation_item_id,
                reconciliation_run_id,
                mops_transaction_record_id,
                manual_gate_log_id,
                payment_attempt_id,
                payment_confirmation_id,
                provider_outcome_id,
                target_entity_type,
                target_entity_id,
                comparison_basis::text AS comparison_basis,
                item_status::text AS item_status,
                match_status::text AS match_status,
                expected_amount,
                actual_amount,
                currency_code,
                variance_amount,
                exception_reason_code,
                resolved_at,
                resolved_by_user_id,
                created_at,
                updated_at,
                correlation_id
            FROM reconciliation.reconciliation_items
            WHERE reconciliation_item_id = @reconciliation_item_id;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(sql, connection);
        dbCommand.Parameters.AddWithValue("reconciliation_item_id", query.ReconciliationItemId);

        await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new ReconciliationItemNotFoundException(query.ReconciliationItemId);
        }

        return ReadItem(reader);
    }

    private static string RunDetailSql(string whereClause) =>
        $"""
        SELECT
            rr.reconciliation_run_id,
            rr.run_code,
            rr.run_type::text AS run_type,
            rr.run_status::text AS run_status,
            rr.scope_type::text AS scope_type,
            rr.site_group_id,
            rr.site_id,
            rr.incident_record_id,
            rr.payment_rail_id,
            rr.vendor_system_id,
            rr.source_batch_ref,
            rr.window_start_at,
            rr.window_end_at,
            rr.started_at,
            rr.completed_at,
            rr.failed_at,
            rr.failure_reason_code,
            rr.item_count,
            rr.matched_count,
            rr.exception_count,
            rr.rejected_count,
            rr.disputed_count,
            rr.initiated_by_user_id,
            rr.initiated_by_service_identity_id,
            rr.correlation_id
        FROM reconciliation.reconciliation_runs rr
        {whereClause};
        """;

    private static async Task<bool> RunExistsAsync(
        NpgsqlConnection connection,
        Guid reconciliationRunId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM reconciliation.reconciliation_runs
                WHERE reconciliation_run_id = @reconciliation_run_id
            );
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection);
        dbCommand.Parameters.AddWithValue("reconciliation_run_id", reconciliationRunId);
        return (bool)(await dbCommand.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static ReconciliationRunDetailRecord ReadRun(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid("reconciliation_run_id"),
            reader.GetString("run_code"),
            reader.GetString("run_type"),
            reader.GetString("run_status"),
            reader.GetString("scope_type"),
            reader.GetNullableGuid("site_group_id"),
            reader.GetNullableGuid("site_id"),
            reader.GetNullableGuid("incident_record_id"),
            reader.GetNullableGuid("payment_rail_id"),
            reader.GetNullableGuid("vendor_system_id"),
            reader.GetNullableString("source_batch_ref"),
            reader.GetNullableDateTimeOffset("window_start_at"),
            reader.GetNullableDateTimeOffset("window_end_at"),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("started_at")),
            reader.GetNullableDateTimeOffset("completed_at"),
            reader.GetNullableDateTimeOffset("failed_at"),
            reader.GetNullableString("failure_reason_code"),
            reader.GetInt32("item_count"),
            reader.GetInt32("matched_count"),
            reader.GetInt32("exception_count"),
            reader.GetInt32("rejected_count"),
            reader.GetInt32("disputed_count"),
            reader.GetNullableGuid("initiated_by_user_id"),
            reader.GetNullableGuid("initiated_by_service_identity_id"),
            reader.GetNullableGuid("correlation_id"));

    private static ReconciliationItemRecord ReadItem(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid("reconciliation_item_id"),
            reader.GetGuid("reconciliation_run_id"),
            reader.GetNullableGuid("mops_transaction_record_id"),
            reader.GetNullableGuid("manual_gate_log_id"),
            reader.GetNullableGuid("payment_attempt_id"),
            reader.GetNullableGuid("payment_confirmation_id"),
            reader.GetNullableGuid("provider_outcome_id"),
            reader.GetNullableString("target_entity_type"),
            reader.GetNullableGuid("target_entity_id"),
            reader.GetString("comparison_basis"),
            reader.GetString("item_status"),
            reader.GetString("match_status"),
            reader.GetNullableDecimal("expected_amount"),
            reader.GetNullableDecimal("actual_amount"),
            reader.GetNullableString("currency_code"),
            reader.GetNullableDecimal("variance_amount"),
            reader.GetNullableString("exception_reason_code"),
            reader.GetNullableDateTimeOffset("resolved_at"),
            reader.GetNullableGuid("resolved_by_user_id"),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at")),
            reader.GetNullableGuid("correlation_id"));

    private static string MapForeignKeyErrorCode(string? constraintName) =>
        constraintName switch
        {
            "fk_reconciliation_runs__site_group_id" => "SITE_GROUP_NOT_FOUND",
            "fk_reconciliation_runs__site_id" => "SITE_NOT_FOUND",
            "fk_reconciliation_runs__incident_record_id" => "INCIDENT_RECORD_NOT_FOUND",
            "fk_reconciliation_runs__payment_rail_id" => "PAYMENT_RAIL_NOT_FOUND",
            "fk_reconciliation_runs__vendor_system_id" => "VENDOR_SYSTEM_NOT_FOUND",
            "fk_reconciliation_runs__initiated_by_user_id" or
            "fk_reconciliation_runs__created_by_user_id" or
            "fk_reconciliation_runs__updated_by_user_id" => "USER_NOT_FOUND",
            "fk_reconciliation_runs__initiated_by_service_identity_id" or
            "fk_reconciliation_runs__created_by_service_identity_id" or
            "fk_reconciliation_runs__updated_by_service_identity_id" => "SERVICE_IDENTITY_NOT_FOUND",
            _ => "RECONCILIATION_RUN_REFERENCE_NOT_FOUND"
        };
}
