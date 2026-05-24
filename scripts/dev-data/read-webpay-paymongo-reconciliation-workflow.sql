-- scripts/dev-data/read-webpay-paymongo-reconciliation-workflow.sql
-- Read-only workflow history for reconciliation exceptions/resolution requests.

\if :{?exception_id}
\else
\set exception_id ''
\endif

\if :{?request_id}
\else
\set request_id ''
\endif

WITH requested AS (
    SELECT
        NULLIF(:'exception_id', '')::uuid AS exception_id,
        NULLIF(:'request_id', '')::uuid AS request_id
),
target_exception AS (
    SELECT DISTINCT re.*
    FROM reconciliation.reconciliation_exceptions re
    CROSS JOIN requested req
    LEFT JOIN reconciliation.reconciliation_exception_resolution_requests rr
      ON rr.reconciliation_exception_id = re.reconciliation_exception_id
    WHERE (req.exception_id IS NOT NULL AND re.reconciliation_exception_id = req.exception_id)
       OR (req.request_id IS NOT NULL AND rr.reconciliation_exception_resolution_request_id = req.request_id)
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
        re.created_by_user_id AS actor_user_id,
        re.created_at AS occurred_at,
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
        COALESCE(approval.approved_at, approval.rejected_at, approval.created_at),
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
UNION ALL
SELECT
    CASE
        WHEN req.exception_id IS NOT NULL THEN 'RECONCILIATION_EXCEPTION_NOT_FOUND'
        WHEN req.request_id IS NOT NULL THEN 'RECONCILIATION_RESOLUTION_REQUEST_NOT_FOUND'
        ELSE 'MISSING_WORKFLOW_SCOPE'
    END AS record_type,
    req.exception_id,
    NULL::uuid,
    req.request_id,
    NULL::uuid,
    NULL::uuid,
    NULL::uuid,
    NULL::uuid,
    NULL::text,
    NULL::text,
    NULL::text,
    NULL::text,
    NULL::uuid,
    NULL::timestamptz,
    NULL::uuid
FROM requested req
WHERE NOT EXISTS (SELECT 1 FROM target_exception)
ORDER BY occurred_at NULLS LAST, record_type;
