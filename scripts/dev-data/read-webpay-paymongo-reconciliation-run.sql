-- scripts/dev-data/read-webpay-paymongo-reconciliation-run.sql
-- Read-only readback for persisted WebPay PayMongo reconciliation runs.
--
-- Usage:
--   psql -v run_id="<reconciliation_run_id>" -f scripts/dev-data/read-webpay-paymongo-reconciliation-run.sql

\if :{?run_id}
\else
\set run_id ''
\endif

WITH requested AS (
    SELECT NULLIF(:'run_id', '')::uuid AS reconciliation_run_id
)
SELECT
    rr.reconciliation_run_id,
    rr.run_code,
    rr.run_type,
    rr.run_status,
    rr.scope_type,
    rr.source_batch_ref,
    rr.window_start_at,
    rr.window_end_at,
    rr.started_at,
    rr.completed_at,
    rr.item_count,
    rr.matched_count,
    rr.exception_count,
    rr.rejected_count,
    rr.disputed_count,
    rr.correlation_id,
    ri.reconciliation_item_id,
    ri.target_entity_type,
    ri.target_entity_id,
    ri.payment_attempt_id,
    ri.payment_confirmation_id,
    ri.provider_outcome_id,
    ri.comparison_basis,
    ri.item_status,
    ri.match_status,
    ri.expected_amount,
    ri.actual_amount,
    ri.currency_code,
    ri.variance_amount,
    ri.exception_reason_code AS item_classification,
    re.reconciliation_exception_id,
    re.exception_type,
    re.exception_severity,
    re.exception_status,
    re.exception_reason_code,
    re.exception_summary,
    re.exception_detail
FROM requested req
JOIN reconciliation.reconciliation_runs rr
  ON rr.reconciliation_run_id = req.reconciliation_run_id
LEFT JOIN reconciliation.reconciliation_items ri
  ON ri.reconciliation_run_id = rr.reconciliation_run_id
LEFT JOIN reconciliation.reconciliation_exceptions re
  ON re.reconciliation_item_id = ri.reconciliation_item_id
ORDER BY ri.created_at, ri.reconciliation_item_id, re.created_at, re.reconciliation_exception_id;
