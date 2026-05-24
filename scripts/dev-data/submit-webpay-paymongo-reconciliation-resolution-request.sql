-- scripts/dev-data/submit-webpay-paymongo-reconciliation-resolution-request.sql
-- Submits a reconciliation exception resolution request and moves the exception to UNDER_REVIEW when needed.
-- Does not mutate payment/provider/exit/gate state.

\if :{?exception_id}
\else
\set exception_id ''
\endif

\if :{?resolution_action}
\else
\set resolution_action ''
\endif

\if :{?resolution_reason}
\else
\set resolution_reason ''
\endif

\if :{?financial_impact}
\else
\set financial_impact 'NONE'
\endif

\if :{?adjustment_required}
\else
\set adjustment_required 'false'
\endif

\if :{?actor_user_id}
\else
\set actor_user_id ''
\endif

\if :{?correlation_id}
\else
\set correlation_id ''
\endif

\if :{?proposed_exception_status}
\else
\set proposed_exception_status 'RESOLVED'
\endif

WITH requested AS (
    SELECT
        NULLIF(:'exception_id', '')::uuid AS exception_id,
        NULLIF(:'resolution_action', '') AS resolution_action,
        NULLIF(:'resolution_reason', '') AS resolution_reason,
        COALESCE(NULLIF(:'financial_impact', ''), 'NONE') AS financial_impact,
        COALESCE(NULLIF(:'adjustment_required', '')::boolean, false) AS adjustment_required,
        NULLIF(:'actor_user_id', '')::uuid AS actor_user_id,
        NULLIF(:'correlation_id', '')::uuid AS correlation_id,
        COALESCE(NULLIF(:'proposed_exception_status', ''), 'RESOLVED') AS proposed_exception_status
),
target_exception AS (
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
    JOIN requested req
      ON re.reconciliation_exception_id = req.exception_id
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
        correlation_id
    )
    SELECT
        te.reconciliation_exception_id,
        te.reconciliation_run_id,
        te.reconciliation_item_id,
        req.resolution_action::reconciliation.reconciliation_resolution_action_enum,
        'SUBMITTED',
        te.exception_status,
        req.proposed_exception_status::reconciliation.reconciliation_exception_status_enum,
        te.item_status,
        CASE
            WHEN req.proposed_exception_status IN ('RESOLVED', 'CLOSED') THEN 'RESOLVED'::reconciliation.reconciliation_item_status_enum
            ELSE te.item_status
        END,
        te.match_status,
        te.match_status,
        req.financial_impact::reconciliation.reconciliation_financial_impact_enum,
        (req.financial_impact IN ('POSSIBLE', 'DEFINITE') OR req.adjustment_required),
        req.adjustment_required,
        left(req.resolution_reason, 128),
        left(req.resolution_reason, 256),
        req.resolution_reason,
        now(),
        req.actor_user_id,
        req.correlation_id
    FROM target_exception te
    CROSS JOIN requested req
    WHERE req.resolution_action IS NOT NULL
      AND req.resolution_reason IS NOT NULL
    RETURNING *
),
updated_exception AS (
    UPDATE reconciliation.reconciliation_exceptions re
       SET exception_status = 'UNDER_REVIEW',
           updated_at = now(),
           updated_by_user_id = (SELECT actor_user_id FROM requested),
           correlation_id = COALESCE((SELECT correlation_id FROM requested), re.correlation_id),
           row_version = re.row_version + 1
    FROM target_exception te
    WHERE re.reconciliation_exception_id = te.reconciliation_exception_id
      AND te.exception_status <> 'UNDER_REVIEW'
      AND EXISTS (SELECT 1 FROM inserted_request)
    RETURNING
        re.reconciliation_exception_id,
        re.reconciliation_run_id,
        re.reconciliation_item_id,
        te.exception_status AS previous_exception_status,
        re.exception_status AS new_exception_status
),
history AS (
    INSERT INTO reconciliation.reconciliation_exception_status_history (
        reconciliation_exception_id,
        reconciliation_run_id,
        reconciliation_item_id,
        reconciliation_exception_resolution_request_id,
        previous_exception_status,
        new_exception_status,
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
        ir.reconciliation_exception_resolution_request_id,
        ue.previous_exception_status,
        ue.new_exception_status,
        'RESOLUTION_REQUEST_SUBMITTED',
        'Resolution request submitted',
        ir.request_detail,
        now(),
        ir.maker_user_id,
        ir.correlation_id
    FROM updated_exception ue
    CROSS JOIN inserted_request ir
    RETURNING reconciliation_exception_status_history_id
)
SELECT
    'RESOLUTION_REQUEST_SUBMITTED' AS result_status,
    ir.reconciliation_exception_id,
    NULL::uuid AS reconciliation_exception_note_id,
    ir.reconciliation_exception_resolution_request_id,
    NULL::uuid AS reconciliation_exception_resolution_approval_id,
    ir.reconciliation_run_id,
    ir.reconciliation_item_id,
    ir.request_status::text AS workflow_status,
    ir.request_summary AS summary,
    ir.correlation_id
FROM inserted_request ir
UNION ALL
SELECT
    CASE
        WHEN req.exception_id IS NULL THEN 'MISSING_EXCEPTION_ID'
        WHEN req.resolution_action IS NULL THEN 'MISSING_RESOLUTION_ACTION'
        WHEN req.resolution_reason IS NULL THEN 'MISSING_RESOLUTION_REASON'
        ELSE 'RECONCILIATION_EXCEPTION_NOT_FOUND'
    END AS result_status,
    req.exception_id,
    NULL::uuid,
    NULL::uuid,
    NULL::uuid,
    NULL::uuid,
    NULL::uuid,
    NULL::text,
    NULL::text,
    req.correlation_id
FROM requested req
WHERE NOT EXISTS (SELECT 1 FROM inserted_request);
