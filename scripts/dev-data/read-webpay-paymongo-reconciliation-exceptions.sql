-- scripts/dev-data/read-webpay-paymongo-reconciliation-exceptions.sql
-- Read-only exception review for persisted WebPay PayMongo reconciliation runs.
--
-- Usage:
--   psql -v run_code="PMWPR-..." -f scripts/dev-data/read-webpay-paymongo-reconciliation-exceptions.sql
--   psql -v run_id="<reconciliation_run_id>" -f scripts/dev-data/read-webpay-paymongo-reconciliation-exceptions.sql

\if :{?run_id}
\else
\set run_id ''
\endif

\if :{?run_code}
\else
\set run_code ''
\endif

\if :{?provider_code}
\else
\set provider_code 'PAYMONGO'
\endif

\if :{?classification}
\else
\set classification ''
\endif

\if :{?ticket_reference}
\else
\set ticket_reference ''
\endif

\if :{?exception_status}
\else
\set exception_status ''
\endif

\if :{?severity}
\else
\set severity ''
\endif

WITH requested AS (
    SELECT
        NULLIF(:'run_id', '')::uuid AS reconciliation_run_id,
        NULLIF(:'run_code', '') AS run_code,
        NULLIF(:'provider_code', '') AS provider_code,
        NULLIF(:'classification', '') AS classification,
        NULLIF(:'ticket_reference', '') AS ticket_reference,
        NULLIF(:'exception_status', '') AS exception_status,
        NULLIF(:'severity', '') AS severity
),
target_run AS (
    SELECT rr.*
    FROM requested req
    JOIN reconciliation.reconciliation_runs rr
      ON (
          (req.reconciliation_run_id IS NOT NULL AND rr.reconciliation_run_id = req.reconciliation_run_id)
          OR
          (req.reconciliation_run_id IS NULL AND req.run_code IS NOT NULL AND rr.run_code = req.run_code)
      )
     AND rr.source_batch_ref LIKE (req.provider_code || ';%')
    LIMIT 1
),
exception_rows AS (
    SELECT
        'EXCEPTION'::text AS result_status,
        rr.reconciliation_run_id,
        rr.run_code,
        split_part(rr.source_batch_ref, ';', 1) AS provider_code,
        rr.run_status::text AS run_status,
        rr.scope_type::text AS scope_type,
        rr.source_batch_ref,
        rr.item_count,
        rr.matched_count,
        rr.exception_count,
        rr.created_at AS run_created_at,
        rr.started_at,
        rr.completed_at,
        re.reconciliation_exception_id,
        ri.reconciliation_item_id,
        ps.vendor_session_ref AS ticket_reference,
        ri.exception_reason_code AS classification,
        re.exception_reason_code,
        re.exception_summary,
        re.exception_detail,
        re.exception_type::text AS exception_type,
        re.exception_severity::text AS exception_severity,
        re.exception_status::text AS exception_status,
        ri.payment_attempt_id,
        prov.provider_session_id,
        prov.session_status::text AS provider_session_status,
        ri.payment_confirmation_id,
        pc.confirmation_status::text AS payment_confirmation_status,
        ri.expected_amount,
        ri.actual_amount,
        ri.currency_code,
        ri.variance_amount,
        ea.exit_authorization_id,
        ea.authorization_status::text AS exit_authorization_status,
        gac.gate_consume_status,
        COALESCE(gac.gate_consume_count, 0)::integer AS gate_consume_count,
        re.detected_at,
        re.assigned_at,
        re.resolved_at,
        re.closed_at,
        re.correlation_id,
        re.created_at AS exception_created_at
    FROM target_run rr
    JOIN reconciliation.reconciliation_items ri
      ON ri.reconciliation_run_id = rr.reconciliation_run_id
    JOIN reconciliation.reconciliation_exceptions re
      ON re.reconciliation_item_id = ri.reconciliation_item_id
    LEFT JOIN core.parking_sessions ps
      ON ps.parking_session_id = ri.target_entity_id
    LEFT JOIN core.payment_confirmations pc
      ON pc.payment_confirmation_id = ri.payment_confirmation_id
    LEFT JOIN LATERAL (
        SELECT provider_session_id, session_status
        FROM payments.provider_sessions provider_session
        WHERE provider_session.payment_attempt_id = ri.payment_attempt_id
        ORDER BY provider_session.created_at DESC, provider_session.provider_session_id DESC
        LIMIT 1
    ) prov ON TRUE
    LEFT JOIN LATERAL (
        SELECT exit_authorization_id, authorization_status
        FROM core.exit_authorizations exit_authorization
        WHERE exit_authorization.payment_attempt_id = ri.payment_attempt_id
           OR exit_authorization.payment_confirmation_id = ri.payment_confirmation_id
           OR exit_authorization.parking_session_id = ri.target_entity_id
        ORDER BY exit_authorization.created_at DESC, exit_authorization.exit_authorization_id DESC
        LIMIT 1
    ) ea ON TRUE
    LEFT JOIN LATERAL (
        SELECT
            max(consume_status::text) AS gate_consume_status,
            count(*) AS gate_consume_count
        FROM gates.gate_authorization_consumptions gate_consumption
        WHERE gate_consumption.exit_authorization_id = ea.exit_authorization_id
    ) gac ON TRUE
    CROSS JOIN requested req
    WHERE (req.classification IS NULL OR ri.exception_reason_code = req.classification)
      AND (req.ticket_reference IS NULL OR ps.vendor_session_ref = req.ticket_reference)
      AND (req.exception_status IS NULL OR re.exception_status::text = req.exception_status)
      AND (req.severity IS NULL OR re.exception_severity::text = req.severity)
),
status_rows AS (
    SELECT
        'RECONCILIATION_RUN_NOT_FOUND'::text AS result_status,
        NULL::uuid AS reconciliation_run_id,
        NULL::varchar AS run_code,
        req.provider_code,
        NULL::text AS run_status,
        NULL::text AS scope_type,
        NULL::varchar AS source_batch_ref,
        0::integer AS item_count,
        0::integer AS matched_count,
        0::integer AS exception_count,
        NULL::timestamptz AS run_created_at,
        NULL::timestamptz AS started_at,
        NULL::timestamptz AS completed_at,
        NULL::uuid AS reconciliation_exception_id,
        NULL::uuid AS reconciliation_item_id,
        NULL::varchar AS ticket_reference,
        NULL::varchar AS classification,
        'RECONCILIATION_RUN_NOT_FOUND'::varchar AS exception_reason_code,
        'Reconciliation run was not found; no fallback run was selected.'::varchar AS exception_summary,
        NULL::text AS exception_detail,
        NULL::text AS exception_type,
        NULL::text AS exception_severity,
        NULL::text AS exception_status,
        NULL::uuid AS payment_attempt_id,
        NULL::uuid AS provider_session_id,
        NULL::text AS provider_session_status,
        NULL::uuid AS payment_confirmation_id,
        NULL::text AS payment_confirmation_status,
        NULL::numeric AS expected_amount,
        NULL::numeric AS actual_amount,
        NULL::character(3) AS currency_code,
        NULL::numeric AS variance_amount,
        NULL::uuid AS exit_authorization_id,
        NULL::text AS exit_authorization_status,
        NULL::text AS gate_consume_status,
        0::integer AS gate_consume_count,
        NULL::timestamptz AS detected_at,
        NULL::timestamptz AS assigned_at,
        NULL::timestamptz AS resolved_at,
        NULL::timestamptz AS closed_at,
        NULL::uuid AS correlation_id,
        NULL::timestamptz AS exception_created_at
    FROM requested req
    WHERE NOT EXISTS (SELECT 1 FROM target_run)
    UNION ALL
    SELECT
        'NO_RECONCILIATION_EXCEPTIONS'::text AS result_status,
        rr.reconciliation_run_id,
        rr.run_code,
        split_part(rr.source_batch_ref, ';', 1) AS provider_code,
        rr.run_status::text AS run_status,
        rr.scope_type::text AS scope_type,
        rr.source_batch_ref,
        rr.item_count,
        rr.matched_count,
        rr.exception_count,
        rr.created_at AS run_created_at,
        rr.started_at,
        rr.completed_at,
        NULL::uuid AS reconciliation_exception_id,
        NULL::uuid AS reconciliation_item_id,
        NULL::varchar AS ticket_reference,
        NULL::varchar AS classification,
        'NO_RECONCILIATION_EXCEPTIONS'::varchar AS exception_reason_code,
        'Run exists and has no reconciliation exceptions for the supplied filters.'::varchar AS exception_summary,
        NULL::text AS exception_detail,
        NULL::text AS exception_type,
        NULL::text AS exception_severity,
        NULL::text AS exception_status,
        NULL::uuid AS payment_attempt_id,
        NULL::uuid AS provider_session_id,
        NULL::text AS provider_session_status,
        NULL::uuid AS payment_confirmation_id,
        NULL::text AS payment_confirmation_status,
        NULL::numeric AS expected_amount,
        NULL::numeric AS actual_amount,
        NULL::character(3) AS currency_code,
        NULL::numeric AS variance_amount,
        NULL::uuid AS exit_authorization_id,
        NULL::text AS exit_authorization_status,
        NULL::text AS gate_consume_status,
        0::integer AS gate_consume_count,
        NULL::timestamptz AS detected_at,
        NULL::timestamptz AS assigned_at,
        NULL::timestamptz AS resolved_at,
        NULL::timestamptz AS closed_at,
        rr.correlation_id,
        NULL::timestamptz AS exception_created_at
    FROM target_run rr
    WHERE NOT EXISTS (SELECT 1 FROM exception_rows)
)
SELECT *
FROM exception_rows
UNION ALL
SELECT *
FROM status_rows
ORDER BY exception_created_at NULLS LAST, reconciliation_exception_id NULLS LAST;
