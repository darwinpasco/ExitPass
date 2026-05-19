-- scripts/dev-data/webpay-20260519-diagnostics.sql
-- Read-only diagnostics for WEBPAY-20260519 WebPay test tickets.

WITH test_sessions AS (
    SELECT
        ps.parking_session_id,
        ps.site_group_id,
        sg.site_group_code,
        ps.site_id,
        s.site_code,
        ps.vendor_system_id,
        vs.vendor_code,
        ps.vendor_session_ref,
        ps.ticket_number_masked,
        ps.plate_number_masked,
        ps.vendor_session_status,
        ps.session_status,
        ps.entry_at,
        ps.created_at
    FROM core.parking_sessions ps
    JOIN sites.site_groups sg
      ON sg.site_group_id = ps.site_group_id
    JOIN sites.sites s
      ON s.site_id = ps.site_id
    JOIN integration.vendor_systems vs
      ON vs.vendor_system_id = ps.vendor_system_id
    WHERE ps.vendor_session_ref LIKE 'WEBPAY-20260519%'
),
attempts AS (
    SELECT
        pa.payment_attempt_id,
        pa.parking_session_id,
        pa.tariff_snapshot_id,
        pa.payment_rail_id,
        pr.rail_code,
        pr.provider_code,
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
    LEFT JOIN payments.payment_rails pr
      ON pr.payment_rail_id = pa.payment_rail_id
    INNER JOIN test_sessions ts
      ON ts.parking_session_id = pa.parking_session_id
),
provider_sessions AS (
    SELECT
        prv.provider_session_id,
        prv.payment_attempt_id,
        prv.payment_rail_id,
        prv.provider_session_ref,
        prv.provider_transaction_ref,
        prv.idempotency_key,
        prv.session_status,
        prv.currency_code,
        prv.amount,
        prv.checkout_url,
        prv.qr_payload,
        prv.expires_at,
        prv.provider_created_at,
        prv.provider_expires_at,
        prv.created_at,
        prv.updated_at
    FROM payments.provider_sessions prv
    INNER JOIN attempts a
      ON a.payment_attempt_id = prv.payment_attempt_id
)
SELECT
    ts.vendor_session_ref AS ticket_number,
    CASE
        WHEN ts.vendor_session_ref LIKE 'WEBPAY-20260519-FRESH-%' THEN 'FRESH'
        WHEN ts.vendor_session_ref LIKE 'WEBPAY-20260519-RESUME-%' THEN 'RESUME'
        WHEN ts.vendor_session_ref LIKE 'WEBPAY-20260519-ORPHAN-NOSESSION-%' THEN 'ORPHAN_NOSESSION'
        WHEN ts.vendor_session_ref LIKE 'WEBPAY-20260519-ORPHAN-NOURL-%' THEN 'ORPHAN_NOURL'
        ELSE 'UNKNOWN'
    END AS seeded_scenario,
    ts.site_group_code,
    ts.site_code,
    ts.vendor_code,
    ts.parking_session_id,
    ts.session_status,
    ts.entry_at,
    t.tariff_snapshot_id,
    t.snapshot_status,
    t.currency_code AS tariff_currency,
    t.net_amount AS tariff_net_amount,
    a.payment_attempt_id,
    a.attempt_status,
    a.rail_code,
    a.provider_code,
    a.rail_type,
    a.amount AS attempt_amount,
    a.requested_at,
    a.expires_at AS attempt_expires_at,
    a.finalized_at,
    a.failure_reason_code,
    prv.provider_session_id,
    prv.session_status AS provider_session_status,
    CASE
        WHEN a.payment_attempt_id IS NULL THEN 'NO_PAYMENT_ATTEMPT_YET'
        WHEN prv.provider_session_id IS NULL THEN 'ORPHAN_NO_PROVIDER_SESSION'
        WHEN NULLIF(BTRIM(prv.checkout_url), '') IS NULL THEN 'ORPHAN_NO_CHECKOUT_URL'
        ELSE 'RESUMABLE'
    END AS webpay_attempt_classification,
    prv.checkout_url,
    prv.expires_at AS provider_session_expires_at
FROM test_sessions ts
LEFT JOIN core.tariff_snapshots t
  ON t.parking_session_id = ts.parking_session_id
LEFT JOIN attempts a
  ON a.parking_session_id = ts.parking_session_id
LEFT JOIN provider_sessions prv
  ON prv.payment_attempt_id = a.payment_attempt_id
ORDER BY
    seeded_scenario,
    ticket_number,
    a.created_at,
    prv.created_at;