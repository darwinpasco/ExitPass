-- scripts/dev-data/webpay-20260521-payment-finalization-diagnostics.sql
-- Read-only diagnostics for WebPay PayMongo webhook finalization on the May 21 runtime batch.

\set ticket_ref '''WEBPAY-20260521-FRESH-005'''

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
)
SELECT
    s.vendor_session_ref AS ticket_reference,
    s.plate_number_masked AS plate_number,
    s.session_status AS parking_session_status,
    s.entry_at,
    t.tariff_snapshot_id,
    t.snapshot_status AS tariff_snapshot_status,
    t.net_amount AS tariff_amount_minor_units,
    t.currency_code AS tariff_currency,
    t.expires_at AS tariff_expires_at,
    a.payment_attempt_id,
    a.attempt_status AS payment_attempt_status,
    a.amount AS payment_attempt_amount_minor_units,
    a.currency_code AS payment_attempt_currency,
    a.provider_code,
    a.rail_code,
    ps.provider_session_id,
    ps.provider_session_ref,
    ps.provider_transaction_ref AS provider_payment_reference,
    ps.session_status AS provider_session_status,
    ps.checkout_url,
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
