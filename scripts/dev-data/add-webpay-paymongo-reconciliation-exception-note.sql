-- scripts/dev-data/add-webpay-paymongo-reconciliation-exception-note.sql
-- Inserts a reconciliation exception note only. Does not mutate payment/provider/exit/gate state.

\if :{?exception_id}
\else
\set exception_id ''
\endif

\if :{?note_text}
\else
\set note_text ''
\endif

\if :{?note_type}
\else
\set note_type 'REVIEW_NOTE'
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
        NULLIF(:'exception_id', '')::uuid AS exception_id,
        NULLIF(:'note_text', '') AS note_text,
        COALESCE(NULLIF(:'note_type', ''), 'REVIEW_NOTE') AS note_type,
        NULLIF(:'actor_user_id', '')::uuid AS actor_user_id,
        NULLIF(:'correlation_id', '')::uuid AS correlation_id
),
target_exception AS (
    SELECT
        re.reconciliation_exception_id,
        re.reconciliation_run_id,
        re.reconciliation_item_id
    FROM reconciliation.reconciliation_exceptions re
    JOIN requested req
      ON re.reconciliation_exception_id = req.exception_id
),
inserted AS (
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
        req.note_type::reconciliation.reconciliation_exception_note_type_enum,
        left(req.note_text, 256),
        req.note_text,
        req.actor_user_id,
        req.correlation_id
    FROM target_exception te
    CROSS JOIN requested req
    WHERE req.note_text IS NOT NULL
    RETURNING *
)
SELECT
    'NOTE_ADDED' AS result_status,
    inserted.reconciliation_exception_id,
    inserted.reconciliation_exception_note_id,
    NULL::uuid AS reconciliation_exception_resolution_request_id,
    NULL::uuid AS reconciliation_exception_resolution_approval_id,
    inserted.reconciliation_run_id,
    inserted.reconciliation_item_id,
    inserted.note_type::text AS workflow_status,
    inserted.note_summary AS summary,
    inserted.correlation_id
FROM inserted
UNION ALL
SELECT
    CASE
        WHEN req.exception_id IS NULL THEN 'MISSING_EXCEPTION_ID'
        WHEN req.note_text IS NULL THEN 'MISSING_NOTE_TEXT'
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
WHERE NOT EXISTS (SELECT 1 FROM inserted);
