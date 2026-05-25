using ExitPass.CentralPms.Application.Reconciliation;
using Npgsql;

namespace ExitPass.CentralPms.Infrastructure.Reconciliation;

/// <summary>
/// PostgreSQL-backed repository for conservative reconciliation item evaluation.
///
/// BRD v1.2 Reference:
/// - Section 9.16 Monitoring and Administration
/// - Section 9.21 Audit and Traceability
///
/// SDD v1.2 Reference:
/// - Section 9.7 Recommended Database Functions
/// - Section 10 API Architecture
///
/// ExitPass v1.2 Invariants Enforced:
/// - Evaluation writes are limited to reconciliation item evaluation fields.
/// - Payment attempts, provider outcomes, payment confirmations, exit authorizations, gate consumptions, settlement, and payout truth are not mutated.
/// </summary>
public sealed class ReconciliationEvaluationRepository : IReconciliationEvaluationRepository
{
    private const string ExceptionHandlingDeferred =
        "Exception creation/update is deferred in this slice because the live schema has no uniqueness constraint for one evaluation exception per item.";

    private readonly string _connectionString;

    /// <summary>
    /// Creates a reconciliation evaluation repository.
    /// </summary>
    public ReconciliationEvaluationRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    /// <inheritdoc />
    public async Task<ReconciliationItemRecord> ReadItemAsync(
        Guid reconciliationItemId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(ItemSql("WHERE reconciliation_item_id = @reconciliation_item_id"), connection);
        dbCommand.Parameters.AddWithValue("reconciliation_item_id", reconciliationItemId);

        await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new ReconciliationItemNotFoundException(reconciliationItemId);
        }

        return ReadItem(reader);
    }

    /// <inheritdoc />
    public async Task<ReconciliationItemEvaluationRecord> SaveEvaluationAsync(
        EvaluateReconciliationItemCommand command,
        ReconciliationEvaluationDecision decision,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH target_item AS (
                SELECT
                    *,
                    (
                        item_status::text IS DISTINCT FROM @item_status
                     OR match_status::text IS DISTINCT FROM @match_status
                     OR variance_amount IS DISTINCT FROM @variance_amount
                     OR exception_reason_code IS DISTINCT FROM @exception_reason_code
                    ) AS evaluation_changed
                FROM reconciliation.reconciliation_items
                WHERE reconciliation_item_id = @reconciliation_item_id
                FOR UPDATE
            ),
            updated_item AS (
                UPDATE reconciliation.reconciliation_items ri
               SET item_status = @item_status::reconciliation.reconciliation_item_status_enum,
                   match_status = @match_status::reconciliation.reconciliation_match_status_enum,
                   variance_amount = @variance_amount,
                   exception_reason_code = @exception_reason_code,
                   updated_at = now(),
                   updated_by_user_id = @actor_user_id,
                   updated_by_service_identity_id = @service_identity_id,
                   correlation_id = @correlation_id,
                   row_version = row_version + 1
            FROM target_item ti
             WHERE ri.reconciliation_item_id = ti.reconciliation_item_id
            RETURNING
                ri.reconciliation_item_id,
                ri.reconciliation_run_id,
                ri.mops_transaction_record_id,
                ri.manual_gate_log_id,
                ri.payment_attempt_id,
                ri.payment_confirmation_id,
                ri.provider_outcome_id,
                ri.target_entity_type,
                ri.target_entity_id,
                ri.comparison_basis::text AS comparison_basis,
                ri.item_status::text AS item_status,
                ri.match_status::text AS match_status,
                ri.expected_amount,
                ri.actual_amount,
                ri.currency_code,
                ri.variance_amount,
                ri.exception_reason_code,
                ri.resolved_at,
                ri.resolved_by_user_id,
                ri.created_at,
                ri.updated_at,
                ri.correlation_id,
                ti.evaluation_changed
            )
            SELECT *
            FROM updated_item;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction);
        dbCommand.Parameters.AddWithValue("reconciliation_item_id", command.ReconciliationItemId);
        dbCommand.Parameters.AddWithValue("item_status", decision.ItemStatus);
        dbCommand.Parameters.AddWithValue("match_status", decision.MatchStatus);
        dbCommand.Parameters.AddWithValue("variance_amount", (object?)decision.VarianceAmount ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("exception_reason_code", (object?)decision.ExceptionReasonCode ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("actor_user_id", (object?)command.ActorUserId ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("service_identity_id", (object?)command.ServiceIdentityId ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("correlation_id", command.CorrelationId);

        ReconciliationItemRecord item;
        bool evaluationChanged;
        await using (var reader = await dbCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new ReconciliationItemNotFoundException(command.ReconciliationItemId);
            }

            item = ReadItem(reader);
            evaluationChanged = reader.GetBoolean(reader.GetOrdinal("evaluation_changed"));
        }

        if (evaluationChanged)
        {
            await ReconciliationEventPersistence.PersistAsync(
                connection,
                transaction,
                ReconciliationEventPersistence.ReconciliationItemEvaluated,
                "reconciliation_items",
                "ReconciliationItem",
                item.ReconciliationItemId,
                item.ReconciliationRunId,
                command.ActorUserId,
                command.ServiceIdentityId,
                command.CorrelationId,
                null,
                $"Reconciliation item evaluated as {decision.MatchStatus}.",
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return ToEvaluation(item, decision.EvaluationClassification, decision.EvaluationReason);
    }

    /// <inheritdoc />
    public async Task<bool> RunExistsAsync(
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

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(sql, connection);
        dbCommand.Parameters.AddWithValue("reconciliation_run_id", reconciliationRunId);

        var result = await dbCommand.ExecuteScalarAsync(cancellationToken);
        return result is bool exists && exists;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListRunItemIdsAsync(
        Guid reconciliationRunId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT reconciliation_item_id
            FROM reconciliation.reconciliation_items
            WHERE reconciliation_run_id = @reconciliation_run_id
            ORDER BY created_at, reconciliation_item_id;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(sql, connection);
        dbCommand.Parameters.AddWithValue("reconciliation_run_id", reconciliationRunId);

        var itemIds = new List<Guid>();
        await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            itemIds.Add(reader.GetGuid(0));
        }

        return itemIds;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReconciliationItemRecord>> ListRunItemsAsync(
        Guid reconciliationRunId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(ItemSql("WHERE reconciliation_run_id = @reconciliation_run_id ORDER BY created_at, reconciliation_item_id"), connection);
        dbCommand.Parameters.AddWithValue("reconciliation_run_id", reconciliationRunId);

        var items = new List<ReconciliationItemRecord>();
        await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadItem(reader));
        }

        return items;
    }

    /// <inheritdoc />
    public async Task PersistRunEvaluatedEventAsync(
        EvaluateReconciliationRunCommand command,
        ReconciliationRunEvaluationSummaryRecord summary,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await ReconciliationEventPersistence.PersistAsync(
            connection,
            transaction: null,
            ReconciliationEventPersistence.ReconciliationRunEvaluated,
            "reconciliation_runs",
            "ReconciliationRun",
            command.ReconciliationRunId,
            null,
            command.ActorUserId,
            command.ServiceIdentityId,
            command.CorrelationId,
            null,
            $"Reconciliation run evaluated. total_items={summary.TotalItems}; evaluated_items={summary.EvaluatedItems}.",
            cancellationToken);
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static string ItemSql(string whereClause) =>
        $"""
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
        {whereClause};
        """;

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

    private static ReconciliationItemEvaluationRecord ToEvaluation(
        ReconciliationItemRecord item,
        string classification,
        string reason) =>
        new(
            item.ReconciliationItemId,
            item.ReconciliationRunId,
            item.ComparisonBasis,
            item.ItemStatus,
            item.MatchStatus,
            classification,
            reason,
            item.ExpectedAmount,
            item.ActualAmount,
            item.VarianceAmount,
            item.ExceptionReasonCode,
            ExceptionCreatedOrUpdated: false,
            ExceptionHandlingDeferred,
            item.UpdatedAt,
            item.CorrelationId);
}
