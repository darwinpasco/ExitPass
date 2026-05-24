-- scripts/dev-data/decide-webpay-paymongo-reconciliation-resolution-request.sql
-- Approves or rejects a reconciliation exception resolution request.
-- Mutates only reconciliation workflow/request/exception status rows.

\if :{?request_id}
\else
\set request_id ''
\endif

\if :{?decision}
\else
\set decision 'APPROVED'
\endif

\if :{?decision_reason}
\else
\set decision_reason ''
\endif

\if :{?actor_user_id}
\else
\set actor_user_id ''
\endif

\if :{?correlation_id}
\else
\set correlation_id ''
\endif

WITH requested AS (
    SELECT
        NULLIF(:'request_id', '')::uuid AS request_id,
        COALESCE(NULLIF(:'decision', ''), 'APPROVED') AS decision,
        NULLIF(:'decision_reason', '') AS decision_reason,
        NULLIF(:'actor_user_id', '')::uuid AS actor_user_id,
        NULLIF(:'correlation_id', '')::uuid AS correlation_id
),
target_request AS (
    SELECT
        rr.*,
        re.exception_status AS current_exception_status,
        ri.item_status AS current_item_status
    FROM reconciliation.reconciliation_exception_resolution_requests rr
    JOIN reconciliation.reconciliation_exceptions re
      ON re.reconciliation_exception_id = rr.reconciliation_exception_id
    LEFT JOIN reconciliation.reconciliation_items ri
      ON ri.reconciliation_item_id = rr.reconciliation_item_id
    JOIN requested req
      ON rr.reconciliation_exception_resolution_request_id = req.request_id
),
existing_decision AS (
    SELECT approval.reconciliation_exception_resolution_approval_id
    FROM reconciliation.reconciliation_exception_resolution_approvals approval
    JOIN requested req
      ON approval.reconciliation_exception_resolution_request_id = req.request_id
    LIMIT 1
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
        causation_id
    )
    SELECT
        tr.reconciliation_exception_resolution_request_id,
        tr.reconciliation_exception_id,
        tr.reconciliation_run_id,
        tr.reconciliation_item_id,
        req.decision::reconciliation.reconciliation_resolution_approval_decision_enum,
        CASE WHEN req.decision = 'APPROVED' THEN left(req.decision_reason, 128) ELSE NULL END,
        CASE WHEN req.decision = 'REJECTED' THEN left(req.decision_reason, 128) ELSE NULL END,
        left(req.decision_reason, 256),
        req.decision_reason,
        CASE WHEN req.decision = 'APPROVED' THEN now() ELSE NULL END,
        CASE WHEN req.decision = 'REJECTED' THEN now() ELSE NULL END,
        req.actor_user_id,
        tr.maker_user_id,
        COALESCE(req.correlation_id, tr.correlation_id),
        tr.correlation_id
    FROM target_request tr
    CROSS JOIN requested req
    WHERE req.decision_reason IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM existing_decision)
    RETURNING *
),
updated_request AS (
    UPDATE reconciliation.reconciliation_exception_resolution_requests rr
       SET request_status = ia.approval_decision::text::reconciliation.reconciliation_resolution_request_status_enum,
           rejection_reason_code = CASE WHEN ia.approval_decision = 'REJECTED' THEN ia.rejection_reason_code ELSE rr.rejection_reason_code END,
           closed_at = now(),
           updated_at = now(),
           updated_by_user_id = ia.checker_user_id,
           correlation_id = COALESCE(ia.correlation_id, rr.correlation_id),
           row_version = rr.row_version + 1
    FROM inserted_approval ia
    WHERE rr.reconciliation_exception_resolution_request_id = ia.reconciliation_exception_resolution_request_id
    RETURNING rr.*
),
updated_exception AS (
    UPDATE reconciliation.reconciliation_exceptions re
       SET exception_status = CASE
               WHEN ia.approval_decision = 'APPROVED' THEN ur.proposed_exception_status
               ELSE 'REJECTED'::reconciliation.reconciliation_exception_status_enum
           END,
           resolved_at = CASE
               WHEN ia.approval_decision = 'APPROVED' AND ur.proposed_exception_status = 'RESOLVED' THEN now()
               ELSE re.resolved_at
           END,
           resolved_by_user_id = CASE
               WHEN ia.approval_decision = 'APPROVED' AND ur.proposed_exception_status = 'RESOLVED' THEN ia.checker_user_id
               ELSE re.resolved_by_user_id
           END,
           closed_at = CASE
               WHEN ia.approval_decision = 'APPROVED' AND ur.proposed_exception_status = 'CLOSED' THEN now()
               ELSE re.closed_at
           END,
           closed_by_user_id = CASE
               WHEN ia.approval_decision = 'APPROVED' AND ur.proposed_exception_status = 'CLOSED' THEN ia.checker_user_id
               ELSE re.closed_by_user_id
           END,
           resolution_reason_code = CASE
               WHEN ia.approval_decision = 'APPROVED' THEN ur.resolution_reason_code
               ELSE re.resolution_reason_code
           END,
           updated_at = now(),
           updated_by_user_id = ia.checker_user_id,
           correlation_id = COALESCE(ia.correlation_id, re.correlation_id),
           row_version = re.row_version + 1
    FROM inserted_approval ia
    JOIN updated_request ur
      ON ur.reconciliation_exception_resolution_request_id = ia.reconciliation_exception_resolution_request_id
    JOIN target_request tr
      ON tr.reconciliation_exception_resolution_request_id = ur.reconciliation_exception_resolution_request_id
    WHERE re.reconciliation_exception_id = ia.reconciliation_exception_id
      AND re.exception_status <> CASE
               WHEN ia.approval_decision = 'APPROVED' THEN ur.proposed_exception_status
               ELSE 'REJECTED'::reconciliation.reconciliation_exception_status_enum
           END
    RETURNING
        re.reconciliation_exception_id,
        re.reconciliation_run_id,
        re.reconciliation_item_id,
        tr.current_exception_status AS previous_exception_status,
        re.exception_status AS new_exception_status,
        tr.current_item_status AS previous_item_status,
        ur.proposed_item_status AS new_item_status,
        ia.reconciliation_exception_resolution_request_id,
        ia.reconciliation_exception_resolution_approval_id,
        ia.checker_user_id,
        ia.correlation_id,
        ia.approval_detail,
        ia.approval_decision
),
history AS (
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
        correlation_id
    )
    SELECT
        ue.reconciliation_exception_id,
        ue.reconciliation_run_id,
        ue.reconciliation_item_id,
        ue.reconciliation_exception_resolution_request_id,
        ue.reconciliation_exception_resolution_approval_id,
        ue.previous_exception_status,
        ue.new_exception_status,
        ue.previous_item_status,
        ue.new_item_status,
        CASE WHEN ue.approval_decision = 'APPROVED' THEN 'RESOLUTION_REQUEST_APPROVED' ELSE 'RESOLUTION_REQUEST_REJECTED' END,
        CASE WHEN ue.approval_decision = 'APPROVED' THEN 'Resolution request approved' ELSE 'Resolution request rejected' END,
        ue.approval_detail,
        now(),
        ue.checker_user_id,
        ue.correlation_id
    FROM updated_exception ue
    RETURNING reconciliation_exception_status_history_id
)
SELECT
    CASE WHEN ia.approval_decision = 'APPROVED' THEN 'RESOLUTION_REQUEST_APPROVED' ELSE 'RESOLUTION_REQUEST_REJECTED' END AS result_status,
    ia.reconciliation_exception_id,
    NULL::uuid AS reconciliation_exception_note_id,
    ia.reconciliation_exception_resolution_request_id,
    ia.reconciliation_exception_resolution_approval_id,
    ia.reconciliation_run_id,
    ia.reconciliation_item_id,
    ia.approval_decision::text AS workflow_status,
    ia.approval_summary AS summary,
    ia.correlation_id
FROM inserted_approval ia
UNION ALL
SELECT
    CASE
        WHEN req.request_id IS NULL THEN 'MISSING_REQUEST_ID'
        WHEN req.decision_reason IS NULL THEN 'MISSING_DECISION_REASON'
        WHEN EXISTS (SELECT 1 FROM existing_decision) THEN 'RECONCILIATION_RESOLUTION_REQUEST_ALREADY_DECIDED'
        ELSE 'RECONCILIATION_RESOLUTION_REQUEST_NOT_FOUND'
    END AS result_status,
    NULL::uuid,
    NULL::uuid,
    req.request_id,
    (SELECT reconciliation_exception_resolution_approval_id FROM existing_decision),
    NULL::uuid,
    NULL::uuid,
    NULL::text,
    NULL::text,
    req.correlation_id
FROM requested req
WHERE NOT EXISTS (SELECT 1 FROM inserted_approval);
