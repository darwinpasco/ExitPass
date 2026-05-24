using System.Data;
using ExitPass.CentralPms.Application.Reconciliation;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ExitPass.CentralPms.Infrastructure.Reconciliation;

/// <summary>
/// PostgreSQL-backed reconciliation workflow repository.
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
/// - Reconciliation workflow writes are limited to reconciliation-owned tables.
/// - Payment attempts, provider sessions, payment confirmations, exit authorizations, and gate consumptions remain DB-authoritative payment/exit truth.
/// </summary>
public sealed class ReconciliationWorkflowRepository : IReconciliationWorkflowRepository
{
    private readonly string _connectionString;
    private readonly ILogger<ReconciliationWorkflowRepository> _logger;

    /// <summary>
    /// Creates a reconciliation workflow repository.
    /// </summary>
    public ReconciliationWorkflowRepository(
        string connectionString,
        ILogger<ReconciliationWorkflowRepository> logger)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ReconciliationNoteResult> AddNoteAsync(
        AddReconciliationNoteCommand command,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH target_exception AS (
                SELECT
                    re.reconciliation_exception_id,
                    re.reconciliation_run_id,
                    re.reconciliation_item_id
                FROM reconciliation.reconciliation_exceptions re
                WHERE re.reconciliation_item_id = @reconciliation_item_id
                ORDER BY re.detected_at DESC, re.created_at DESC
                LIMIT 1
            ),
            inserted_note AS (
                INSERT INTO reconciliation.reconciliation_exception_notes (
                    reconciliation_exception_id,
                    reconciliation_run_id,
                    reconciliation_item_id,
                    note_type,
                    note_summary,
                    note_detail,
                    created_by_user_id,
                    correlation_id
                )
                SELECT
                    te.reconciliation_exception_id,
                    te.reconciliation_run_id,
                    te.reconciliation_item_id,
                    @note_type::reconciliation.reconciliation_exception_note_type_enum,
                    left(@note_text, 256),
                    @note_text,
                    @actor_user_id,
                    @correlation_id
                FROM target_exception te
                RETURNING
                    reconciliation_exception_note_id,
                    reconciliation_exception_id,
                    reconciliation_item_id,
                    note_type::text,
                    created_at,
                    correlation_id
            )
            SELECT
                reconciliation_exception_note_id,
                reconciliation_exception_id,
                reconciliation_item_id,
                note_type,
                created_at,
                correlation_id
            FROM inserted_note;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(sql, connection);
        AddCommonParameters(dbCommand, command.ReconciliationItemId, command.ActorUserId, command.CorrelationId);
        dbCommand.Parameters.AddWithValue("note_type", command.NoteType);
        dbCommand.Parameters.AddWithValue("note_text", command.NoteText);

        await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new ReconciliationExceptionNotFoundException(command.ReconciliationItemId);
        }

        return new ReconciliationNoteResult(
            ReconciliationItemId: reader.GetGuid("reconciliation_item_id"),
            ReconciliationExceptionId: reader.GetGuid("reconciliation_exception_id"),
            ReconciliationExceptionNoteId: reader.GetGuid("reconciliation_exception_note_id"),
            NoteType: reader.GetString("note_type"),
            CreatedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
            CorrelationId: reader.GetGuid("correlation_id"));
    }

    /// <inheritdoc />
    public async Task<ReconciliationResolutionRequestResult> SubmitResolutionRequestAsync(
        SubmitReconciliationResolutionCommand command,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH target_exception AS (
                SELECT
                    re.reconciliation_exception_id,
                    re.reconciliation_run_id,
                    re.reconciliation_item_id,
                    re.exception_status,
                    ri.item_status,
                    ri.match_status
                FROM reconciliation.reconciliation_exceptions re
                LEFT JOIN reconciliation.reconciliation_items ri
                  ON ri.reconciliation_item_id = re.reconciliation_item_id
                WHERE re.reconciliation_item_id = @reconciliation_item_id
                ORDER BY re.detected_at DESC, re.created_at DESC
                LIMIT 1
            ),
            inserted_request AS (
                INSERT INTO reconciliation.reconciliation_exception_resolution_requests (
                    reconciliation_exception_id,
                    reconciliation_run_id,
                    reconciliation_item_id,
                    requested_action,
                    request_status,
                    previous_exception_status,
                    proposed_exception_status,
                    previous_item_status,
                    proposed_item_status,
                    previous_match_status,
                    proposed_match_status,
                    financial_impact,
                    financial_impact_flag,
                    adjustment_required_flag,
                    resolution_reason_code,
                    request_summary,
                    request_detail,
                    submitted_at,
                    maker_user_id,
                    correlation_id,
                    created_by_user_id,
                    updated_by_user_id
                )
                SELECT
                    te.reconciliation_exception_id,
                    te.reconciliation_run_id,
                    te.reconciliation_item_id,
                    @resolution_action::reconciliation.reconciliation_resolution_action_enum,
                    'SUBMITTED'::reconciliation.reconciliation_resolution_request_status_enum,
                    te.exception_status,
                    @proposed_exception_status::reconciliation.reconciliation_exception_status_enum,
                    te.item_status,
                    te.item_status,
                    te.match_status,
                    te.match_status,
                    @financial_impact::reconciliation.reconciliation_financial_impact_enum,
                    (@financial_impact IN ('POSSIBLE', 'DEFINITE')),
                    @adjustment_required,
                    @resolution_reason,
                    left(@request_summary, 256),
                    @request_detail,
                    now(),
                    @actor_user_id,
                    @correlation_id,
                    @actor_user_id,
                    @actor_user_id
                FROM target_exception te
                RETURNING *
            ),
            updated_exception AS (
                UPDATE reconciliation.reconciliation_exceptions re
                   SET exception_status = 'UNDER_REVIEW'::reconciliation.reconciliation_exception_status_enum,
                       updated_at = now(),
                       updated_by_user_id = @actor_user_id,
                       correlation_id = @correlation_id,
                       row_version = row_version + 1
                FROM inserted_request ir
                WHERE re.reconciliation_exception_id = ir.reconciliation_exception_id
                  AND re.exception_status <> 'UNDER_REVIEW'::reconciliation.reconciliation_exception_status_enum
                RETURNING
                    re.reconciliation_exception_id,
                    re.reconciliation_run_id,
                    re.reconciliation_item_id,
                    ir.reconciliation_exception_resolution_request_id,
                    ir.previous_exception_status,
                    re.exception_status AS new_exception_status,
                    ir.previous_item_status,
                    ir.maker_user_id,
                    ir.correlation_id
            ),
            inserted_history AS (
                INSERT INTO reconciliation.reconciliation_exception_status_history (
                    reconciliation_exception_id,
                    reconciliation_run_id,
                    reconciliation_item_id,
                    reconciliation_exception_resolution_request_id,
                    previous_exception_status,
                    new_exception_status,
                    previous_item_status,
                    new_item_status,
                    reason_code,
                    transition_summary,
                    transition_detail,
                    changed_at,
                    changed_by_user_id,
                    correlation_id,
                    causation_id
                )
                SELECT
                    ue.reconciliation_exception_id,
                    ue.reconciliation_run_id,
                    ue.reconciliation_item_id,
                    ue.reconciliation_exception_resolution_request_id,
                    ue.previous_exception_status,
                    ue.new_exception_status,
                    ue.previous_item_status,
                    ue.previous_item_status,
                    'RESOLUTION_REQUEST_SUBMITTED',
                    'Resolution request submitted',
                    @request_detail,
                    now(),
                    ue.maker_user_id,
                    ue.correlation_id,
                    ue.reconciliation_exception_resolution_request_id
                FROM updated_exception ue
                RETURNING reconciliation_exception_status_history_id
            )
            SELECT
                ir.reconciliation_item_id,
                ir.reconciliation_exception_id,
                ir.reconciliation_exception_resolution_request_id,
                ir.request_status::text AS request_status,
                ir.previous_exception_status::text AS previous_exception_status,
                ir.proposed_exception_status::text AS proposed_exception_status,
                ir.submitted_at,
                ir.correlation_id
            FROM inserted_request ir;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(sql, connection);
        AddCommonParameters(dbCommand, command.ReconciliationItemId, command.ActorUserId, command.CorrelationId);
        dbCommand.Parameters.AddWithValue("resolution_action", command.ResolutionAction);
        dbCommand.Parameters.AddWithValue("resolution_reason", command.ResolutionReason);
        dbCommand.Parameters.AddWithValue("financial_impact", command.FinancialImpact);
        dbCommand.Parameters.AddWithValue("adjustment_required", command.AdjustmentRequired);
        dbCommand.Parameters.AddWithValue("request_summary", command.RequestSummary);
        dbCommand.Parameters.AddWithValue("request_detail", (object?)command.RequestDetail ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("proposed_exception_status", command.ProposedExceptionStatus);

        try
        {
            await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new ReconciliationExceptionNotFoundException(command.ReconciliationItemId);
            }

            return new ReconciliationResolutionRequestResult(
                ReconciliationItemId: reader.GetGuid("reconciliation_item_id"),
                ReconciliationExceptionId: reader.GetGuid("reconciliation_exception_id"),
                ResolutionRequestId: reader.GetGuid("reconciliation_exception_resolution_request_id"),
                RequestStatus: reader.GetString("request_status"),
                PreviousExceptionStatus: reader.GetString("previous_exception_status"),
                ProposedExceptionStatus: reader.GetString("proposed_exception_status"),
                SubmittedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("submitted_at")),
                CorrelationId: reader.GetGuid("correlation_id"));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            _logger.LogWarning(
                ex,
                "Duplicate active reconciliation resolution request rejected. reconciliation_item_id={ReconciliationItemId}",
                command.ReconciliationItemId);

            throw new ReconciliationWorkflowConflictException(
                "RECONCILIATION_RESOLUTION_REQUEST_ALREADY_ACTIVE",
                "An active reconciliation resolution request already exists for this exception.");
        }
    }

    /// <inheritdoc />
    public async Task<ReconciliationResolutionDecisionResult> DecideResolutionRequestAsync(
        DecideReconciliationResolutionCommand command,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH target_request AS (
                SELECT
                    rr.*,
                    re.exception_status AS current_exception_status
                FROM reconciliation.reconciliation_exception_resolution_requests rr
                JOIN reconciliation.reconciliation_exceptions re
                  ON re.reconciliation_exception_id = rr.reconciliation_exception_id
                WHERE rr.reconciliation_exception_resolution_request_id = @resolution_request_id
            ),
            existing_approval AS (
                SELECT approval.reconciliation_exception_resolution_approval_id
                FROM reconciliation.reconciliation_exception_resolution_approvals approval
                WHERE approval.reconciliation_exception_resolution_request_id = @resolution_request_id
            ),
            inserted_approval AS (
                INSERT INTO reconciliation.reconciliation_exception_resolution_approvals (
                    reconciliation_exception_resolution_request_id,
                    reconciliation_exception_id,
                    reconciliation_run_id,
                    reconciliation_item_id,
                    approval_decision,
                    approval_reason_code,
                    rejection_reason_code,
                    approval_summary,
                    approval_detail,
                    approved_at,
                    rejected_at,
                    checker_user_id,
                    maker_user_id,
                    correlation_id,
                    causation_id,
                    created_by_user_id
                )
                SELECT
                    tr.reconciliation_exception_resolution_request_id,
                    tr.reconciliation_exception_id,
                    tr.reconciliation_run_id,
                    tr.reconciliation_item_id,
                    @decision::reconciliation.reconciliation_resolution_approval_decision_enum,
                    CASE WHEN @decision = 'APPROVED' THEN @reason ELSE NULL END,
                    CASE WHEN @decision = 'REJECTED' THEN @reason ELSE NULL END,
                    left(@comment, 256),
                    @comment,
                    CASE WHEN @decision = 'APPROVED' THEN now() ELSE NULL END,
                    CASE WHEN @decision = 'REJECTED' THEN now() ELSE NULL END,
                    @actor_user_id,
                    tr.maker_user_id,
                    @correlation_id,
                    tr.reconciliation_exception_resolution_request_id,
                    @actor_user_id
                FROM target_request tr
                WHERE NOT EXISTS (SELECT 1 FROM existing_approval)
                RETURNING *
            ),
            updated_request AS (
                UPDATE reconciliation.reconciliation_exception_resolution_requests rr
                   SET request_status = ia.approval_decision::text::reconciliation.reconciliation_resolution_request_status_enum,
                       closed_at = now(),
                       updated_at = now(),
                       updated_by_user_id = ia.checker_user_id,
                       correlation_id = ia.correlation_id,
                       row_version = row_version + 1
                FROM inserted_approval ia
                WHERE rr.reconciliation_exception_resolution_request_id = ia.reconciliation_exception_resolution_request_id
                RETURNING rr.*
            ),
            updated_exception AS (
                UPDATE reconciliation.reconciliation_exceptions re
                   SET exception_status =
                       CASE
                           WHEN ia.approval_decision = 'APPROVED'
                               THEN ur.proposed_exception_status
                           ELSE 'REJECTED'::reconciliation.reconciliation_exception_status_enum
                       END,
                       resolved_at =
                       CASE
                           WHEN ia.approval_decision = 'APPROVED'
                            AND ur.proposed_exception_status = 'RESOLVED'::reconciliation.reconciliation_exception_status_enum
                               THEN now()
                           ELSE re.resolved_at
                       END,
                       closed_at =
                       CASE
                           WHEN ia.approval_decision = 'APPROVED'
                            AND ur.proposed_exception_status = 'CLOSED'::reconciliation.reconciliation_exception_status_enum
                               THEN now()
                           ELSE re.closed_at
                       END,
                       resolution_reason_code =
                       CASE
                           WHEN ia.approval_decision = 'APPROVED' THEN ur.resolution_reason_code
                           ELSE re.resolution_reason_code
                       END,
                       resolved_by_user_id =
                       CASE
                           WHEN ia.approval_decision = 'APPROVED'
                            AND ur.proposed_exception_status = 'RESOLVED'::reconciliation.reconciliation_exception_status_enum
                               THEN ia.checker_user_id
                           ELSE re.resolved_by_user_id
                       END,
                       closed_by_user_id =
                       CASE
                           WHEN ia.approval_decision = 'APPROVED'
                            AND ur.proposed_exception_status = 'CLOSED'::reconciliation.reconciliation_exception_status_enum
                               THEN ia.checker_user_id
                           ELSE re.closed_by_user_id
                       END,
                       updated_at = now(),
                       updated_by_user_id = ia.checker_user_id,
                       correlation_id = ia.correlation_id,
                       row_version = row_version + 1
                FROM inserted_approval ia
                JOIN updated_request ur
                  ON ur.reconciliation_exception_resolution_request_id = ia.reconciliation_exception_resolution_request_id
                WHERE re.reconciliation_exception_id = ia.reconciliation_exception_id
                RETURNING
                    re.reconciliation_exception_id,
                    re.reconciliation_run_id,
                    re.reconciliation_item_id,
                    re.exception_status,
                    ur.current_exception_status AS previous_exception_status,
                    ur.previous_item_status,
                    ia.reconciliation_exception_resolution_request_id,
                    ia.reconciliation_exception_resolution_approval_id,
                    ia.approval_decision,
                    ia.checker_user_id,
                    ia.correlation_id,
                    ur.request_status,
                    COALESCE(ia.approved_at, ia.rejected_at) AS decided_at
            ),
            inserted_history AS (
                INSERT INTO reconciliation.reconciliation_exception_status_history (
                    reconciliation_exception_id,
                    reconciliation_run_id,
                    reconciliation_item_id,
                    reconciliation_exception_resolution_request_id,
                    reconciliation_exception_resolution_approval_id,
                    previous_exception_status,
                    new_exception_status,
                    previous_item_status,
                    new_item_status,
                    reason_code,
                    transition_summary,
                    transition_detail,
                    changed_at,
                    changed_by_user_id,
                    correlation_id,
                    causation_id
                )
                SELECT
                    ue.reconciliation_exception_id,
                    ue.reconciliation_run_id,
                    ue.reconciliation_item_id,
                    ue.reconciliation_exception_resolution_request_id,
                    ue.reconciliation_exception_resolution_approval_id,
                    ue.previous_exception_status,
                    ue.exception_status,
                    ue.previous_item_status,
                    ue.previous_item_status,
                    CASE
                        WHEN ue.approval_decision = 'APPROVED' THEN 'RESOLUTION_REQUEST_APPROVED'
                        ELSE 'RESOLUTION_REQUEST_REJECTED'
                    END,
                    CASE
                        WHEN ue.approval_decision = 'APPROVED' THEN 'Resolution request approved'
                        ELSE 'Resolution request rejected'
                    END,
                    @comment,
                    now(),
                    ue.checker_user_id,
                    ue.correlation_id,
                    ue.reconciliation_exception_resolution_approval_id
                FROM updated_exception ue
                WHERE ue.previous_exception_status <> ue.exception_status
                RETURNING reconciliation_exception_status_history_id
            )
            SELECT
                ue.reconciliation_exception_resolution_request_id,
                ue.reconciliation_exception_id,
                ue.reconciliation_exception_resolution_approval_id,
                ue.approval_decision::text AS decision,
                ue.request_status::text AS request_status,
                ue.exception_status::text AS exception_status,
                ue.decided_at,
                ue.correlation_id
            FROM updated_exception ue;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(sql, connection);
        dbCommand.Parameters.AddWithValue("resolution_request_id", command.ResolutionRequestId);
        dbCommand.Parameters.AddWithValue("decision", command.Decision);
        dbCommand.Parameters.AddWithValue("reason", command.Reason);
        dbCommand.Parameters.AddWithValue("comment", (object?)command.Comment ?? command.Reason);
        dbCommand.Parameters.AddWithValue("actor_user_id", (object?)command.ActorUserId ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("correlation_id", command.CorrelationId);

        await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            var exists = await ResolutionRequestExistsAsync(command.ResolutionRequestId, cancellationToken);
            if (!exists)
            {
                throw new ReconciliationResolutionRequestNotFoundException(command.ResolutionRequestId);
            }

            throw new ReconciliationWorkflowConflictException(
                "RECONCILIATION_RESOLUTION_REQUEST_ALREADY_DECIDED",
                "The reconciliation resolution request already has a recorded decision.");
        }

        return new ReconciliationResolutionDecisionResult(
            ResolutionRequestId: reader.GetGuid("reconciliation_exception_resolution_request_id"),
            ReconciliationExceptionId: reader.GetGuid("reconciliation_exception_id"),
            ResolutionApprovalId: reader.GetGuid("reconciliation_exception_resolution_approval_id"),
            Decision: reader.GetString("decision"),
            RequestStatus: reader.GetString("request_status"),
            ExceptionStatus: reader.GetString("exception_status"),
            DecidedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("decided_at")),
            CorrelationId: reader.GetGuid("correlation_id"));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReconciliationWorkflowHistoryRecord>> ReadWorkflowHistoryAsync(
        ReadReconciliationWorkflowHistoryQuery query,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH target_exception AS (
                SELECT *
                FROM reconciliation.reconciliation_exceptions re
                WHERE re.reconciliation_item_id = @reconciliation_item_id
            ),
            workflow_rows AS (
                SELECT
                    'EXCEPTION'::text AS record_type,
                    re.reconciliation_exception_id,
                    NULL::uuid AS reconciliation_exception_note_id,
                    NULL::uuid AS reconciliation_exception_resolution_request_id,
                    NULL::uuid AS reconciliation_exception_resolution_approval_id,
                    NULL::uuid AS reconciliation_exception_status_history_id,
                    re.reconciliation_run_id,
                    re.reconciliation_item_id,
                    re.exception_status::text AS status,
                    re.exception_reason_code AS reason_code,
                    re.exception_summary AS summary,
                    re.exception_detail AS detail,
                    re.updated_by_user_id AS actor_user_id,
                    re.updated_at AS occurred_at,
                    re.correlation_id
                FROM target_exception re
                UNION ALL
                SELECT
                    'NOTE',
                    note.reconciliation_exception_id,
                    note.reconciliation_exception_note_id,
                    NULL::uuid,
                    NULL::uuid,
                    NULL::uuid,
                    note.reconciliation_run_id,
                    note.reconciliation_item_id,
                    note.note_type::text,
                    note.note_type::text,
                    note.note_summary,
                    note.note_detail,
                    note.created_by_user_id,
                    note.created_at,
                    note.correlation_id
                FROM reconciliation.reconciliation_exception_notes note
                JOIN target_exception re
                  ON re.reconciliation_exception_id = note.reconciliation_exception_id
                UNION ALL
                SELECT
                    'REQUEST',
                    request.reconciliation_exception_id,
                    NULL::uuid,
                    request.reconciliation_exception_resolution_request_id,
                    NULL::uuid,
                    NULL::uuid,
                    request.reconciliation_run_id,
                    request.reconciliation_item_id,
                    request.request_status::text,
                    request.resolution_reason_code,
                    request.request_summary,
                    request.request_detail,
                    request.maker_user_id,
                    request.created_at,
                    request.correlation_id
                FROM reconciliation.reconciliation_exception_resolution_requests request
                JOIN target_exception re
                  ON re.reconciliation_exception_id = request.reconciliation_exception_id
                UNION ALL
                SELECT
                    'APPROVAL',
                    approval.reconciliation_exception_id,
                    NULL::uuid,
                    approval.reconciliation_exception_resolution_request_id,
                    approval.reconciliation_exception_resolution_approval_id,
                    NULL::uuid,
                    approval.reconciliation_run_id,
                    approval.reconciliation_item_id,
                    approval.approval_decision::text,
                    COALESCE(approval.approval_reason_code, approval.rejection_reason_code),
                    approval.approval_summary,
                    approval.approval_detail,
                    approval.checker_user_id,
                    approval.created_at,
                    approval.correlation_id
                FROM reconciliation.reconciliation_exception_resolution_approvals approval
                JOIN target_exception re
                  ON re.reconciliation_exception_id = approval.reconciliation_exception_id
                UNION ALL
                SELECT
                    'STATUS_HISTORY',
                    history.reconciliation_exception_id,
                    NULL::uuid,
                    history.reconciliation_exception_resolution_request_id,
                    history.reconciliation_exception_resolution_approval_id,
                    history.reconciliation_exception_status_history_id,
                    history.reconciliation_run_id,
                    history.reconciliation_item_id,
                    history.new_exception_status::text,
                    history.reason_code,
                    history.transition_summary,
                    history.transition_detail,
                    history.changed_by_user_id,
                    history.changed_at,
                    history.correlation_id
                FROM reconciliation.reconciliation_exception_status_history history
                JOIN target_exception re
                  ON re.reconciliation_exception_id = history.reconciliation_exception_id
            )
            SELECT *
            FROM workflow_rows
            ORDER BY occurred_at, record_type;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(sql, connection);
        dbCommand.Parameters.AddWithValue("reconciliation_item_id", query.ReconciliationItemId);

        var records = new List<ReconciliationWorkflowHistoryRecord>();
        await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new ReconciliationWorkflowHistoryRecord(
                RecordType: reader.GetString("record_type"),
                ReconciliationExceptionId: reader.GetNullableGuid("reconciliation_exception_id"),
                ReconciliationExceptionNoteId: reader.GetNullableGuid("reconciliation_exception_note_id"),
                ResolutionRequestId: reader.GetNullableGuid("reconciliation_exception_resolution_request_id"),
                ResolutionApprovalId: reader.GetNullableGuid("reconciliation_exception_resolution_approval_id"),
                StatusHistoryId: reader.GetNullableGuid("reconciliation_exception_status_history_id"),
                ReconciliationRunId: reader.GetNullableGuid("reconciliation_run_id"),
                ReconciliationItemId: reader.GetNullableGuid("reconciliation_item_id"),
                Status: reader.GetNullableString("status"),
                ReasonCode: reader.GetNullableString("reason_code"),
                Summary: reader.GetNullableString("summary"),
                Detail: reader.GetNullableString("detail"),
                ActorUserId: reader.GetNullableGuid("actor_user_id"),
                OccurredAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("occurred_at")),
                CorrelationId: reader.GetNullableGuid("correlation_id")));
        }

        if (records.Count == 0)
        {
            throw new ReconciliationExceptionNotFoundException(query.ReconciliationItemId);
        }

        return records;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReconciliationRunRecord>> ListRunsAsync(
        ListReconciliationRunsQuery query,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                reconciliation_run_id,
                run_code,
                run_type::text AS run_type,
                run_status::text AS run_status,
                scope_type::text AS scope_type,
                source_batch_ref,
                started_at,
                completed_at,
                item_count,
                matched_count,
                exception_count,
                correlation_id
            FROM reconciliation.reconciliation_runs
            ORDER BY started_at DESC, reconciliation_run_id DESC
            LIMIT @limit;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(sql, connection);
        dbCommand.Parameters.AddWithValue("limit", query.Limit);

        var records = new List<ReconciliationRunRecord>();
        await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new ReconciliationRunRecord(
                ReconciliationRunId: reader.GetGuid("reconciliation_run_id"),
                RunCode: reader.GetString("run_code"),
                RunType: reader.GetString("run_type"),
                RunStatus: reader.GetString("run_status"),
                ScopeType: reader.GetString("scope_type"),
                SourceBatchRef: reader.GetNullableString("source_batch_ref"),
                StartedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("started_at")),
                CompletedAt: reader.GetNullableDateTimeOffset("completed_at"),
                ItemCount: reader.GetInt32("item_count"),
                MatchedCount: reader.GetInt32("matched_count"),
                ExceptionCount: reader.GetInt32("exception_count"),
                CorrelationId: reader.GetNullableGuid("correlation_id")));
        }

        return records;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReconciliationExceptionRecord>> ListExceptionsAsync(
        ListReconciliationExceptionsQuery query,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                re.reconciliation_exception_id,
                re.reconciliation_run_id,
                re.reconciliation_item_id,
                rr.run_code,
                re.exception_type::text AS exception_type,
                re.exception_severity::text AS exception_severity,
                re.exception_status::text AS exception_status,
                re.exception_reason_code,
                re.exception_summary,
                ri.payment_attempt_id,
                ri.payment_confirmation_id,
                ri.target_entity_type,
                ri.target_entity_id,
                re.detected_at,
                re.correlation_id
            FROM reconciliation.reconciliation_exceptions re
            JOIN reconciliation.reconciliation_runs rr
              ON rr.reconciliation_run_id = re.reconciliation_run_id
            LEFT JOIN reconciliation.reconciliation_items ri
              ON ri.reconciliation_item_id = re.reconciliation_item_id
            WHERE (@run_id IS NULL OR re.reconciliation_run_id = @run_id)
              AND (@status IS NULL OR re.exception_status = @status::reconciliation.reconciliation_exception_status_enum)
              AND (@severity IS NULL OR re.exception_severity::text = @severity)
            ORDER BY re.detected_at DESC, re.reconciliation_exception_id DESC
            LIMIT @limit;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(sql, connection);
        dbCommand.Parameters.AddWithValue("run_id", (object?)query.RunId ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("status", (object?)query.Status?.ToUpperInvariant() ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("severity", (object?)query.Severity?.ToUpperInvariant() ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("limit", query.Limit);

        var records = new List<ReconciliationExceptionRecord>();
        await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new ReconciliationExceptionRecord(
                ReconciliationExceptionId: reader.GetGuid("reconciliation_exception_id"),
                ReconciliationRunId: reader.GetGuid("reconciliation_run_id"),
                ReconciliationItemId: reader.GetNullableGuid("reconciliation_item_id"),
                RunCode: reader.GetString("run_code"),
                ExceptionType: reader.GetString("exception_type"),
                ExceptionSeverity: reader.GetString("exception_severity"),
                ExceptionStatus: reader.GetString("exception_status"),
                ExceptionReasonCode: reader.GetString("exception_reason_code"),
                ExceptionSummary: reader.GetString("exception_summary"),
                PaymentAttemptId: reader.GetNullableGuid("payment_attempt_id"),
                PaymentConfirmationId: reader.GetNullableGuid("payment_confirmation_id"),
                TargetEntityType: reader.GetNullableString("target_entity_type"),
                TargetEntityId: reader.GetNullableGuid("target_entity_id"),
                DetectedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("detected_at")),
                CorrelationId: reader.GetNullableGuid("correlation_id")));
        }

        return records;
    }

    private async Task<bool> ResolutionRequestExistsAsync(
        Guid resolutionRequestId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM reconciliation.reconciliation_exception_resolution_requests
                WHERE reconciliation_exception_resolution_request_id = @resolution_request_id
            );
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(sql, connection);
        dbCommand.Parameters.AddWithValue("resolution_request_id", resolutionRequestId);

        return (bool)(await dbCommand.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static void AddCommonParameters(
        NpgsqlCommand dbCommand,
        Guid reconciliationItemId,
        Guid? actorUserId,
        Guid correlationId)
    {
        dbCommand.Parameters.AddWithValue("reconciliation_item_id", reconciliationItemId);
        dbCommand.Parameters.AddWithValue("actor_user_id", (object?)actorUserId ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("correlation_id", correlationId);
    }
}

internal static class NpgsqlDataReaderExtensions
{
    public static Guid GetGuid(this NpgsqlDataReader reader, string columnName) =>
        reader.GetGuid(reader.GetOrdinal(columnName));

    public static int GetInt32(this NpgsqlDataReader reader, string columnName) =>
        reader.GetInt32(reader.GetOrdinal(columnName));

    public static string GetString(this NpgsqlDataReader reader, string columnName) =>
        reader.GetString(reader.GetOrdinal(columnName));

    public static string? GetNullableString(this NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    public static Guid? GetNullableGuid(this NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    public static DateTimeOffset? GetNullableDateTimeOffset(this NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }
}
