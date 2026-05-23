-- scripts/dev-data/webpay-20260523-payment-finalization-diagnostics.sql
-- Read-only diagnostics for WebPay PayMongo webhook finalization on the May 23 runtime batch.
-- Column names and enum values are verified against ExitPass_Full_Database_Creation_DDL_v1.2.sql
-- and live psql \d / \dT inspection before this script was authored.
-- Override target with:
--   psql -v ticket_ref="'WEBPAY-20260523-FRESH-002'" -f scripts/dev-data/webpay-20260523-payment-finalization-diagnostics.sql

\if :{?ticket_ref}
\else
\set ticket_ref '''WEBPAY-20260523-FRESH-002'''
\endif

WITH test_sessions AS (
    SELECT
        ps.parking_session_id,
        ps.vendor_session_ref
    FROM core.parking_sessions ps
    WHERE ps.vendor_session_ref LIKE 'WEBPAY-20260523%'
)
SELECT
    CASE
        WHEN vendor_session_ref LIKE 'WEBPAY-20260523-FRESH-%' THEN 'FRESH'
        WHEN vendor_session_ref LIKE 'WEBPAY-20260523-RESUME-%' THEN 'RESUME'
        WHEN vendor_session_ref LIKE 'WEBPAY-20260523-ORPHAN-NOSESSION-%' THEN 'ORPHAN_NOSESSION'
        WHEN vendor_session_ref LIKE 'WEBPAY-20260523-ORPHAN-NOURL-%' THEN 'ORPHAN_NOURL'
        ELSE 'UNKNOWN'
    END AS scenario,
    COUNT(*) AS parking_session_count
FROM test_sessions
GROUP BY 1
ORDER BY 1;

SELECT
    COUNT(*) AS duplicate_active_tariff_snapshot_count
FROM (
    SELECT ts.parking_session_id
    FROM core.tariff_snapshots ts
    JOIN core.parking_sessions ps
      ON ps.parking_session_id = ts.parking_session_id
    WHERE ps.vendor_session_ref LIKE 'WEBPAY-20260523%'
      AND ts.snapshot_status = 'ACTIVE'
    GROUP BY ts.parking_session_id
    HAVING COUNT(*) > 1
) duplicates;

SELECT
    COUNT(*) AS active_tariff_snapshots_outside_may_23_count
FROM core.tariff_snapshots ts
JOIN core.parking_sessions ps
  ON ps.parking_session_id = ts.parking_session_id
WHERE ps.vendor_session_ref LIKE 'WEBPAY-20260523%'
  AND ts.snapshot_status = 'ACTIVE'
  AND (
      ts.expires_at < TIMESTAMPTZ '2026-05-22 16:00:00+00'
      OR ts.expires_at > TIMESTAMPTZ '2026-05-23 15:59:59+00'
  );

SELECT
    payment_method_code,
    currency_code,
    primary_provider_code,
    fallback_provider_code,
    is_enabled,
    primary_provider_enabled,
    fallback_provider_enabled
FROM payments.payment_provider_routing_policies
WHERE payment_method_code = 'QRPH'
  AND currency_code = 'PHP'
ORDER BY site_id NULLS FIRST, site_group_id NULLS FIRST, created_at DESC;

SELECT
    rail_code,
    provider_code,
    rail_type,
    supported_currency_code,
    rail_status,
    is_primary,
    is_fallback,
    effective_from,
    effective_to
FROM payments.payment_rails
WHERE provider_code = 'PAYMONGO'
  AND rail_type = 'QRPH'
ORDER BY rail_code;

WITH target_session AS (
    SELECT
        ps.parking_session_id,
        ps.vendor_session_ref,
        ps.ticket_number_masked,
        ps.plate_number_masked,
        ps.session_status,
        ps.entry_at,
        ps.site_group_id,
        ps.site_id,
        ps.vendor_system_id
    FROM core.parking_sessions ps
    WHERE ps.vendor_session_ref = :ticket_ref
),
latest_tariff AS (
    SELECT DISTINCT ON (ts.parking_session_id)
        ts.parking_session_id,
        ts.tariff_snapshot_id,
        ts.snapshot_status,
        ts.currency_code,
        ts.net_amount,
        ts.expires_at,
        ts.created_at,
        ts.updated_at
    FROM core.tariff_snapshots ts
    JOIN target_session s
      ON s.parking_session_id = ts.parking_session_id
    ORDER BY ts.parking_session_id,
        CASE WHEN ts.snapshot_status = 'ACTIVE' THEN 0 ELSE 1 END,
        ts.created_at DESC
),
attempts AS (
    SELECT
        pa.payment_attempt_id,
        pa.parking_session_id,
        pa.tariff_snapshot_id,
        pa.payment_rail_id,
        pr.provider_code,
        pr.rail_code,
        pr.rail_type,
        pa.idempotency_key,
        pa.currency_code,
        pa.amount,
        pa.attempt_status,
        pa.requested_at,
        pa.expires_at,
        pa.finalized_at,
        pa.failure_reason_code,
        pa.created_at,
        pa.updated_at
    FROM core.payment_attempts pa
    JOIN target_session s
      ON s.parking_session_id = pa.parking_session_id
    LEFT JOIN payments.payment_rails pr
      ON pr.payment_rail_id = pa.payment_rail_id
),
provider_sessions AS (
    SELECT
        ps.provider_session_id,
        ps.payment_attempt_id,
        ps.payment_rail_id,
        ps.provider_session_ref,
        ps.provider_transaction_ref,
        ps.idempotency_key,
        ps.session_status,
        ps.currency_code,
        ps.amount,
        ps.checkout_url,
        ps.expires_at,
        ps.provider_created_at,
        ps.provider_expires_at,
        ps.created_at,
        ps.updated_at
    FROM payments.provider_sessions ps
    JOIN attempts a
      ON a.payment_attempt_id = ps.payment_attempt_id
),
confirmations AS (
    SELECT
        pc.payment_confirmation_id,
        pc.payment_attempt_id,
        pc.payment_rail_id,
        pc.provider_transaction_ref,
        pc.currency_code,
        pc.confirmed_amount,
        pc.confirmation_status,
        pc.verified_at,
        pc.confirmed_at,
        pc.correlation_id,
        pc.created_at
    FROM core.payment_confirmations pc
    JOIN attempts a
      ON a.payment_attempt_id = pc.payment_attempt_id
),
callbacks AS (
    SELECT
        cb.provider_callback_id,
        cb.provider_session_id,
        cb.payment_rail_id,
        cb.provider_event_ref,
        cb.provider_transaction_ref,
        cb.callback_type,
        cb.signature_valid,
        cb.verification_status,
        cb.payload_hash,
        cb.received_at,
        cb.processed_at,
        cb.processing_status,
        cb.failure_reason_code,
        cb.created_at
    FROM payments.provider_callbacks cb
    JOIN provider_sessions ps
      ON ps.provider_session_id = cb.provider_session_id
),
target_counts AS (
    SELECT
        s.parking_session_id,
        COUNT(DISTINCT a.payment_attempt_id) AS payment_attempts_count,
        STRING_AGG(DISTINCT a.attempt_status::text, ', ' ORDER BY a.attempt_status::text) AS payment_attempt_statuses,
        COUNT(DISTINCT ps.provider_session_id) AS provider_sessions_count,
        STRING_AGG(DISTINCT ps.session_status::text, ', ' ORDER BY ps.session_status::text) AS provider_session_statuses,
        STRING_AGG(DISTINCT ps.provider_session_ref, ', ' ORDER BY ps.provider_session_ref) AS provider_session_refs,
        STRING_AGG(DISTINCT ps.provider_transaction_ref, ', ' ORDER BY ps.provider_transaction_ref) AS provider_payment_references,
        STRING_AGG(DISTINCT a.provider_code, ', ' ORDER BY a.provider_code) AS provider_codes,
        COUNT(DISTINCT c.payment_confirmation_id) AS payment_confirmations_count,
        STRING_AGG(DISTINCT c.confirmation_status::text, ', ' ORDER BY c.confirmation_status::text) AS payment_confirmation_statuses
    FROM target_session s
    LEFT JOIN attempts a
      ON a.parking_session_id = s.parking_session_id
    LEFT JOIN provider_sessions ps
      ON ps.payment_attempt_id = a.payment_attempt_id
    LEFT JOIN confirmations c
      ON c.payment_attempt_id = a.payment_attempt_id
    GROUP BY s.parking_session_id
)
SELECT
    s.vendor_session_ref AS ticket_reference,
    s.parking_session_id,
    s.vendor_session_ref,
    s.plate_number_masked AS plate_number,
    s.session_status AS parking_session_status,
    s.entry_at,
    t.tariff_snapshot_id,
    t.snapshot_status AS tariff_snapshot_status,
    t.net_amount AS tariff_amount_minor_units,
    t.currency_code AS tariff_currency,
    t.expires_at AS tariff_expires_at,
    tc.payment_attempts_count,
    tc.payment_attempt_statuses,
    a.payment_attempt_id,
    a.attempt_status AS payment_attempt_status,
    a.amount AS payment_attempt_amount_minor_units,
    a.currency_code AS payment_attempt_currency,
    a.provider_code,
    a.rail_code,
    tc.provider_sessions_count,
    tc.provider_session_statuses,
    tc.provider_codes,
    ps.provider_session_id,
    ps.provider_session_ref,
    ps.provider_transaction_ref AS provider_payment_reference,
    ps.session_status AS provider_session_status,
    ps.checkout_url,
    tc.payment_confirmations_count,
    tc.payment_confirmation_statuses,
    c.payment_confirmation_id,
    c.provider_transaction_ref AS confirmed_provider_reference,
    c.confirmed_amount,
    c.currency_code AS confirmation_currency,
    c.confirmation_status,
    c.verified_at,
    cb.provider_callback_id,
    cb.provider_event_ref AS provider_event_id,
    cb.provider_transaction_ref AS callback_provider_reference,
    cb.callback_type,
    cb.signature_valid,
    cb.verification_status,
    cb.processing_status,
    cb.processed_at
FROM target_session s
LEFT JOIN latest_tariff t
  ON t.parking_session_id = s.parking_session_id
LEFT JOIN attempts a
  ON a.parking_session_id = s.parking_session_id
LEFT JOIN provider_sessions ps
  ON ps.payment_attempt_id = a.payment_attempt_id
LEFT JOIN confirmations c
  ON c.payment_attempt_id = a.payment_attempt_id
LEFT JOIN callbacks cb
  ON cb.provider_session_id = ps.provider_session_id
LEFT JOIN target_counts tc
  ON tc.parking_session_id = s.parking_session_id
ORDER BY a.created_at DESC, ps.created_at DESC, cb.created_at DESC;

SELECT
    cb.provider_event_ref,
    COUNT(*) AS callback_count
FROM payments.provider_callbacks cb
JOIN payments.provider_sessions ps
  ON ps.provider_session_id = cb.provider_session_id
JOIN core.payment_attempts pa
  ON pa.payment_attempt_id = ps.payment_attempt_id
JOIN core.parking_sessions parking
  ON parking.parking_session_id = pa.parking_session_id
WHERE parking.vendor_session_ref = :ticket_ref
GROUP BY cb.provider_event_ref
HAVING COUNT(*) > 1;

SELECT
    pc.provider_transaction_ref,
    COUNT(*) AS confirmation_count
FROM core.payment_confirmations pc
JOIN core.payment_attempts pa
  ON pa.payment_attempt_id = pc.payment_attempt_id
JOIN core.parking_sessions parking
  ON parking.parking_session_id = pa.parking_session_id
WHERE parking.vendor_session_ref = :ticket_ref
GROUP BY pc.provider_transaction_ref
HAVING COUNT(*) > 1;
