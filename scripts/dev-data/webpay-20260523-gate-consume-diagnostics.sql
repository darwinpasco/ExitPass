-- scripts/dev-data/webpay-20260523-gate-consume-diagnostics.sql
-- Read-only diagnostics for May 23 WebPay gate consume / exit authorization validation.
-- Schema shape verified against live psql \d+ inspection before this script was authored.
-- Override target with:
--   psql -v ticket_ref="'WEBPAY-20260523-FRESH-003'" -f scripts/dev-data/webpay-20260523-gate-consume-diagnostics.sql

\if :{?ticket_ref}
\else
\set ticket_ref '''WEBPAY-20260523-FRESH-003'''
\endif

WITH target_session AS (
    SELECT
        ps.parking_session_id,
        ps.site_id,
        ps.vendor_session_ref,
        ps.plate_number_masked,
        ps.session_status,
        ps.entry_at
    FROM core.parking_sessions ps
    WHERE ps.vendor_session_ref = :ticket_ref
),
attempts AS (
    SELECT
        pa.payment_attempt_id,
        pa.parking_session_id,
        pa.payment_rail_id,
        pa.attempt_status,
        pa.amount,
        pa.currency_code,
        pa.finalized_at,
        pr.provider_code,
        pr.rail_code
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
        ps.provider_session_ref,
        ps.provider_transaction_ref,
        ps.session_status,
        ps.updated_at
    FROM payments.provider_sessions ps
    JOIN attempts a
      ON a.payment_attempt_id = ps.payment_attempt_id
),
confirmations AS (
    SELECT
        pc.payment_confirmation_id,
        pc.payment_attempt_id,
        pc.provider_transaction_ref,
        pc.confirmation_status,
        pc.verified_at,
        pc.confirmed_at
    FROM core.payment_confirmations pc
    JOIN attempts a
      ON a.payment_attempt_id = pc.payment_attempt_id
),
exit_authorizations AS (
    SELECT
        ea.exit_authorization_id,
        ea.parking_session_id,
        ea.payment_attempt_id,
        ea.payment_confirmation_id,
        ea.authorization_status,
        ea.issued_at,
        ea.expires_at,
        ea.invalidated_at,
        ea.updated_at,
        ea.updated_by_service_identity_id
    FROM core.exit_authorizations ea
    JOIN attempts a
      ON a.payment_attempt_id = ea.payment_attempt_id
),
consumptions AS (
    SELECT
        gac.gate_authorization_consumption_id,
        gac.exit_authorization_id,
        gac.gate_device_id,
        gd.device_code AS gate_device_code,
        gac.site_id,
        gac.lane_id,
        l.lane_code,
        gac.consume_status,
        gac.consume_reason_code,
        gac.requested_at,
        gac.validated_at,
        gac.consumed_at,
        gac.command_requested,
        gac.command_result_status,
        gac.command_result_at,
        gac.failure_detail,
        gac.correlation_id,
        gac.created_by_service_identity_id,
        si.service_identity_code AS consumed_by_service_identity_code,
        gac.created_at
    FROM gates.gate_authorization_consumptions gac
    JOIN exit_authorizations ea
      ON ea.exit_authorization_id = gac.exit_authorization_id
    LEFT JOIN gates.gate_devices gd
      ON gd.gate_device_id = gac.gate_device_id
    LEFT JOIN sites.lanes l
      ON l.lane_id = gac.lane_id
    LEFT JOIN identity.service_identities si
      ON si.service_identity_id = gac.created_by_service_identity_id
),
counts AS (
    SELECT
        s.parking_session_id,
        COUNT(DISTINCT a.payment_attempt_id) AS payment_attempt_count,
        STRING_AGG(DISTINCT a.attempt_status::text, ', ' ORDER BY a.attempt_status::text) AS payment_attempt_statuses,
        STRING_AGG(DISTINCT ps.session_status::text, ', ' ORDER BY ps.session_status::text) AS provider_session_statuses,
        COUNT(DISTINCT c.payment_confirmation_id) AS payment_confirmation_count,
        COUNT(DISTINCT ea.exit_authorization_id) AS exit_authorization_count,
        STRING_AGG(DISTINCT ea.authorization_status::text, ', ' ORDER BY ea.authorization_status::text) AS exit_authorization_statuses,
        COUNT(DISTINCT gac.gate_authorization_consumption_id) AS consume_attempt_count,
        COUNT(DISTINCT gac.gate_authorization_consumption_id) FILTER (WHERE gac.consume_status = 'CONSUMED') AS successful_consume_count
    FROM target_session s
    LEFT JOIN attempts a
      ON a.parking_session_id = s.parking_session_id
    LEFT JOIN provider_sessions ps
      ON ps.payment_attempt_id = a.payment_attempt_id
    LEFT JOIN confirmations c
      ON c.payment_attempt_id = a.payment_attempt_id
    LEFT JOIN exit_authorizations ea
      ON ea.payment_attempt_id = a.payment_attempt_id
    LEFT JOIN consumptions gac
      ON gac.exit_authorization_id = ea.exit_authorization_id
    GROUP BY s.parking_session_id
)
SELECT
    s.vendor_session_ref AS ticket_reference,
    s.parking_session_id,
    s.plate_number_masked AS plate_number,
    s.session_status AS parking_session_status,
    a.payment_attempt_id,
    a.attempt_status AS payment_attempt_status,
    a.provider_code,
    a.rail_code,
    ps.provider_session_id,
    ps.session_status AS provider_session_status,
    c.payment_confirmation_id,
    cnt.payment_confirmation_count,
    ea.exit_authorization_id,
    ea.authorization_status AS final_exit_authorization_status,
    ea.issued_at AS exit_authorization_issued_at,
    ea.expires_at AS exit_authorization_expires_at,
    ea.invalidated_at AS exit_authorization_invalidated_at,
    cnt.exit_authorization_count,
    cnt.exit_authorization_statuses,
    cnt.consume_attempt_count,
    cnt.successful_consume_count,
    gac.gate_authorization_consumption_id,
    gac.consume_status AS consume_result,
    gac.consume_reason_code,
    gac.gate_device_id,
    gac.gate_device_code,
    gac.lane_id,
    gac.lane_code,
    gac.requested_at AS consume_requested_at,
    gac.validated_at AS consume_validated_at,
    gac.consumed_at,
    gac.created_by_service_identity_id AS consumed_by,
    gac.consumed_by_service_identity_code AS consumed_by_code,
    gac.command_requested,
    gac.command_result_status,
    gac.command_result_at,
    gac.failure_detail
FROM target_session s
LEFT JOIN attempts a
  ON a.parking_session_id = s.parking_session_id
LEFT JOIN provider_sessions ps
  ON ps.payment_attempt_id = a.payment_attempt_id
LEFT JOIN confirmations c
  ON c.payment_attempt_id = a.payment_attempt_id
LEFT JOIN exit_authorizations ea
  ON ea.payment_attempt_id = a.payment_attempt_id
LEFT JOIN consumptions gac
  ON gac.exit_authorization_id = ea.exit_authorization_id
LEFT JOIN counts cnt
  ON cnt.parking_session_id = s.parking_session_id
ORDER BY gac.created_at DESC NULLS LAST, ea.issued_at DESC NULLS LAST;

SELECT
    ea.exit_authorization_id,
    COUNT(*) FILTER (WHERE gac.consume_status = 'CONSUMED') AS successful_consume_count,
    COUNT(*) FILTER (WHERE gac.consume_status <> 'CONSUMED') AS rejected_or_failed_consume_count
FROM core.exit_authorizations ea
JOIN core.parking_sessions ps
  ON ps.parking_session_id = ea.parking_session_id
LEFT JOIN gates.gate_authorization_consumptions gac
  ON gac.exit_authorization_id = ea.exit_authorization_id
WHERE ps.vendor_session_ref = :ticket_ref
GROUP BY ea.exit_authorization_id
HAVING COUNT(*) FILTER (WHERE gac.consume_status = 'CONSUMED') > 1;
