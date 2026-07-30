-- Read-only verification for the WebPay ordinary-payment local walkthrough.

\set ON_ERROR_STOP on

SELECT
    ps.parking_session_id,
    ps.vendor_session_ref,
    ps.ticket_number_masked AS ticket_reference,
    ps.plate_number_masked AS plate_number,
    ps.session_status,
    sg.site_group_code,
    sg.public_lookup_enabled AS site_group_public_lookup_enabled,
    sg.default_payment_enabled AS site_group_payment_enabled,
    s.site_code,
    s.public_lookup_enabled AS site_public_lookup_enabled,
    s.payment_enabled AS site_payment_enabled,
    ts.tariff_snapshot_id,
    ts.snapshot_status,
    ts.currency_code,
    (ts.net_amount * 100)::bigint AS amount_minor_units,
    ts.expires_at AS tariff_expires_at
FROM core.parking_sessions ps
JOIN sites.site_groups sg ON sg.site_group_id = ps.site_group_id
JOIN sites.sites s ON s.site_id = ps.site_id
JOIN core.tariff_snapshots ts ON ts.parking_session_id = ps.parking_session_id
JOIN integration.vendor_systems vs ON vs.vendor_system_id = ps.vendor_system_id
WHERE sg.site_group_code = 'WEBPAY_LOCAL_GROUP'
  AND s.site_code = 'WEBPAY_LOCAL_SITE'
  AND vs.vendor_code = 'WEBPAY_LOCAL_MOCK_PMS'
  AND vs.environment_code = 'LOCAL'
  AND ps.ticket_number_masked = 'WEBPAY-LOCAL-ORDINARY-001'
  AND ps.plate_number_masked = 'LOCALPAY001'
ORDER BY ts.created_at DESC;

SELECT
    pa.payment_attempt_id,
    pa.idempotency_key,
    pa.attempt_status,
    pa.currency_code,
    (pa.amount * 100)::bigint AS amount_minor_units,
    pa.requested_at,
    pa.expires_at,
    ps.provider_session_ref,
    ps.session_status AS provider_session_status,
    ps.checkout_url,
    ps.qr_payload
FROM core.payment_attempts pa
LEFT JOIN payments.provider_sessions ps ON ps.payment_attempt_id = pa.payment_attempt_id
WHERE pa.parking_session_id = (
    SELECT parking_session_id
    FROM core.parking_sessions
    WHERE ticket_number_masked = 'WEBPAY-LOCAL-ORDINARY-001'
      AND plate_number_masked = 'LOCALPAY001'
    LIMIT 1
)
ORDER BY pa.created_at DESC;

DO $$
DECLARE
    v_site_group_count integer;
    v_site_count integer;
    v_vendor_system_count integer;
    v_session_count integer;
    v_walkthrough_tariff_count integer;
    v_statutory_decision_count integer;
    v_statutory_application_count integer;
BEGIN
    SELECT COUNT(*)
    INTO v_site_group_count
    FROM sites.site_groups
    WHERE site_group_code = 'WEBPAY_LOCAL_GROUP'
      AND site_group_status = 'ACTIVE'
      AND public_lookup_enabled = true
      AND default_payment_enabled = true;

    SELECT COUNT(*)
    INTO v_site_count
    FROM sites.sites s
    INNER JOIN sites.site_groups sg ON sg.site_group_id = s.site_group_id
    WHERE sg.site_group_code = 'WEBPAY_LOCAL_GROUP'
      AND s.site_code = 'WEBPAY_LOCAL_SITE'
      AND s.site_status = 'ACTIVE'
      AND s.public_lookup_enabled = true
      AND s.payment_enabled = true;

    SELECT COUNT(*)
    INTO v_vendor_system_count
    FROM integration.vendor_systems
    WHERE vendor_code = 'WEBPAY_LOCAL_MOCK_PMS'
      AND environment_code = 'LOCAL'
      AND vendor_system_status = 'ACTIVE';

    SELECT COUNT(*)
    INTO v_session_count
    FROM core.parking_sessions ps
    INNER JOIN sites.site_groups sg ON sg.site_group_id = ps.site_group_id
    INNER JOIN sites.sites s ON s.site_id = ps.site_id
    INNER JOIN integration.vendor_systems vs ON vs.vendor_system_id = ps.vendor_system_id
    WHERE sg.site_group_code = 'WEBPAY_LOCAL_GROUP'
      AND s.site_code = 'WEBPAY_LOCAL_SITE'
      AND vs.vendor_code = 'WEBPAY_LOCAL_MOCK_PMS'
      AND vs.environment_code = 'LOCAL'
      AND ps.ticket_number_masked = 'WEBPAY-LOCAL-ORDINARY-001'
      AND plate_number_masked = 'LOCALPAY001'
      AND session_status = 'ACTIVE';

    SELECT COUNT(*)
    INTO v_walkthrough_tariff_count
    FROM core.tariff_snapshots ts
    INNER JOIN core.parking_sessions ps ON ps.parking_session_id = ts.parking_session_id
    WHERE ps.ticket_number_masked = 'WEBPAY-LOCAL-ORDINARY-001'
      AND ps.plate_number_masked = 'LOCALPAY001'
      AND ts.snapshot_status = 'ACTIVE'
      AND ts.currency_code = 'PHP'
      AND (ts.net_amount * 100)::bigint = 13750;

    SELECT COUNT(*)
    INTO v_statutory_decision_count
    FROM discounts.statutory_discount_decision_commands d
    INNER JOIN core.parking_sessions ps ON ps.parking_session_id = d.parking_session_id
    WHERE ps.ticket_number_masked = 'WEBPAY-LOCAL-ORDINARY-001'
      AND ps.plate_number_masked = 'LOCALPAY001';

    SELECT COUNT(*)
    INTO v_statutory_application_count
    FROM discounts.statutory_discount_payable_basis_application_commands a
    INNER JOIN core.parking_sessions ps ON ps.parking_session_id = a.parking_session_id
    WHERE ps.ticket_number_masked = 'WEBPAY-LOCAL-ORDINARY-001'
      AND ps.plate_number_masked = 'LOCALPAY001';

    IF v_site_group_count <> 1 THEN
        RAISE EXCEPTION 'Expected one active lookup/payment-enabled WEBPAY_LOCAL_GROUP site group, found %.', v_site_group_count;
    END IF;

    IF v_site_count <> 1 THEN
        RAISE EXCEPTION 'Expected one active lookup/payment-enabled WEBPAY_LOCAL_SITE under WEBPAY_LOCAL_GROUP, found %.', v_site_count;
    END IF;

    IF v_vendor_system_count <> 1 THEN
        RAISE EXCEPTION 'Expected one active WEBPAY_LOCAL_MOCK_PMS/LOCAL vendor system, found %.', v_vendor_system_count;
    END IF;

    IF v_session_count <> 1 THEN
        RAISE EXCEPTION 'Expected one active ordinary WebPay parking session, found %.', v_session_count;
    END IF;

    IF v_walkthrough_tariff_count <> 1 THEN
        RAISE EXCEPTION 'Expected one ordinary WebPay tariff snapshot for PHP 137.50 in ACTIVE or CONSUMED state, found %.', v_walkthrough_tariff_count;
    END IF;

    IF v_statutory_decision_count <> 0 THEN
        RAISE EXCEPTION 'Ordinary walkthrough fixture must not create statutory decision rows; found %.', v_statutory_decision_count;
    END IF;

    IF v_statutory_application_count <> 0 THEN
        RAISE EXCEPTION 'Ordinary walkthrough fixture must not create statutory application rows; found %.', v_statutory_application_count;
    END IF;
END $$;
