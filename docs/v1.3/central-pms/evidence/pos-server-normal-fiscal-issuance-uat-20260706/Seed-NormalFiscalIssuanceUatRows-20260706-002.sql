BEGIN;

DO $$
DECLARE
    db_name text := current_database();
BEGIN
    IF db_name <> 'centralpms_feq_retry_uat_local' THEN
        RAISE EXCEPTION 'Refusing to seed non-UAT database: %', db_name;
    END IF;

    IF db_name !~* 'centralpms'
       OR db_name !~* 'feq'
       OR db_name !~* 'retry'
       OR db_name !~* 'uat'
       OR db_name !~* 'local' THEN
        RAISE EXCEPTION 'Refusing to seed database without required disposable markers: %', db_name;
    END IF;

    IF db_name ~* '(prod|production|shared|live|exitpass_v12_dev|exitpass)' THEN
        RAISE EXCEPTION 'Refusing to seed unsafe database name: %', db_name;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM identity.service_identities
        WHERE service_identity_id = '00000000-0000-4000-8000-000000000901'
    ) THEN
        RAISE EXCEPTION 'Required disposable parent service identity is missing.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM sites.site_groups
        WHERE site_group_id = '00000000-0000-4000-8000-000000000401'
    ) THEN
        RAISE EXCEPTION 'Required disposable parent site group is missing.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM sites.sites
        WHERE site_id = '00000000-0000-4000-8000-000000000402'
    ) THEN
        RAISE EXCEPTION 'Required disposable parent site is missing.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM integration.vendor_systems
        WHERE vendor_system_id = '00000000-0000-4000-8000-000000000501'
    ) THEN
        RAISE EXCEPTION 'Required disposable parent vendor system is missing.';
    END IF;
END $$;

INSERT INTO core.parking_sessions (
    parking_session_id,
    site_group_id,
    site_id,
    vendor_system_id,
    vendor_session_ref,
    plate_number_hash,
    plate_number_masked,
    ticket_number_hash,
    ticket_number_masked,
    entry_at,
    vendor_session_status,
    session_status,
    correlation_id,
    created_at,
    created_by_service_identity_id,
    updated_at,
    updated_by_service_identity_id,
    row_version
)
VALUES (
    '00000000-0000-4000-8000-000000000313',
    '00000000-0000-4000-8000-000000000401',
    '00000000-0000-4000-8000-000000000402',
    '00000000-0000-4000-8000-000000000501',
    'CPS-POS-UAT-PARKING-SESSION-002',
    NULL,
    'UAT-002',
    NULL,
    'UAT-TICKET-002',
    TIMESTAMPTZ '2026-07-06 09:00:00+08',
    'PAYMENT_REQUIRED',
    'ACTIVE',
    '00000000-0000-4000-8000-000000000102',
    NOW(),
    '00000000-0000-4000-8000-000000000901',
    NOW(),
    '00000000-0000-4000-8000-000000000901',
    1
)
ON CONFLICT (parking_session_id) DO UPDATE SET
    vendor_session_ref = EXCLUDED.vendor_session_ref,
    session_status = EXCLUDED.session_status,
    correlation_id = EXCLUDED.correlation_id,
    updated_at = NOW(),
    updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
    row_version = core.parking_sessions.row_version + 1;

INSERT INTO core.tariff_snapshots (
    tariff_snapshot_id,
    parking_session_id,
    superseded_by_tariff_snapshot_id,
    vendor_system_id,
    vendor_tariff_ref,
    tariff_version_reference,
    currency_code,
    gross_amount,
    statutory_discount_amount,
    coupon_discount_amount,
    net_amount,
    statutory_discount_validation_id,
    coupon_application_id,
    snapshot_status,
    calculated_at,
    expires_at,
    consumed_at,
    correlation_id,
    created_at,
    created_by_service_identity_id,
    updated_at,
    updated_by_service_identity_id,
    row_version
)
VALUES (
    '00000000-0000-4000-8000-000000000612',
    '00000000-0000-4000-8000-000000000313',
    NULL,
    '00000000-0000-4000-8000-000000000501',
    'CPS-POS-UAT-TARIFF-002',
    'CPS-POS-UAT-TARIFF-V1-002',
    'PHP',
    100.00,
    0.00,
    0.00,
    100.00,
    NULL,
    NULL,
    'ACTIVE',
    TIMESTAMPTZ '2026-07-06 09:05:00+08',
    TIMESTAMPTZ '2026-07-07 09:05:00+08',
    NULL,
    '00000000-0000-4000-8000-000000000102',
    NOW(),
    '00000000-0000-4000-8000-000000000901',
    NOW(),
    '00000000-0000-4000-8000-000000000901',
    1
)
ON CONFLICT (tariff_snapshot_id) DO UPDATE SET
    parking_session_id = EXCLUDED.parking_session_id,
    vendor_tariff_ref = EXCLUDED.vendor_tariff_ref,
    tariff_version_reference = EXCLUDED.tariff_version_reference,
    snapshot_status = EXCLUDED.snapshot_status,
    updated_at = NOW(),
    updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
    row_version = core.tariff_snapshots.row_version + 1;

INSERT INTO core.payment_attempts (
    payment_attempt_id,
    parking_session_id,
    tariff_snapshot_id,
    idempotency_key,
    payment_rail_id,
    currency_code,
    amount,
    attempt_status,
    requested_at,
    expires_at,
    finalized_at,
    failure_reason_code,
    correlation_id,
    created_at,
    created_by_service_identity_id,
    updated_at,
    updated_by_service_identity_id,
    row_version
)
VALUES (
    '00000000-0000-4000-8000-000000000312',
    '00000000-0000-4000-8000-000000000313',
    '00000000-0000-4000-8000-000000000612',
    'CPS-POS-UAT-PAYMENT-ATTEMPT-002',
    NULL,
    'PHP',
    100.00,
    'CONFIRMED',
    TIMESTAMPTZ '2026-07-06 09:10:00+08',
    TIMESTAMPTZ '2026-07-07 09:10:00+08',
    TIMESTAMPTZ '2026-07-06 09:15:00+08',
    NULL,
    '00000000-0000-4000-8000-000000000102',
    NOW(),
    '00000000-0000-4000-8000-000000000901',
    NOW(),
    '00000000-0000-4000-8000-000000000901',
    1
)
ON CONFLICT (payment_attempt_id) DO UPDATE SET
    parking_session_id = EXCLUDED.parking_session_id,
    tariff_snapshot_id = EXCLUDED.tariff_snapshot_id,
    idempotency_key = EXCLUDED.idempotency_key,
    currency_code = EXCLUDED.currency_code,
    amount = EXCLUDED.amount,
    attempt_status = EXCLUDED.attempt_status,
    finalized_at = EXCLUDED.finalized_at,
    correlation_id = EXCLUDED.correlation_id,
    updated_at = NOW(),
    updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
    row_version = core.payment_attempts.row_version + 1;

INSERT INTO core.payment_confirmations (
    payment_confirmation_id,
    payment_attempt_id,
    provider_outcome_id,
    payment_rail_id,
    provider_transaction_ref,
    currency_code,
    confirmed_amount,
    confirmation_status,
    verified_at,
    confirmed_at,
    correlation_id,
    created_at,
    created_by_service_identity_id
)
VALUES (
    '00000000-0000-4000-8000-000000000311',
    '00000000-0000-4000-8000-000000000312',
    NULL,
    NULL,
    'CPS-POS-UAT:CPS-POS-UAT-20260706-DEV-ATC-002:newly_created:001',
    'PHP',
    100.00,
    'RECORDED',
    TIMESTAMPTZ '2026-07-06 09:16:00+08',
    TIMESTAMPTZ '2026-07-06 09:16:00+08',
    '00000000-0000-4000-8000-000000000102',
    NOW(),
    '00000000-0000-4000-8000-000000000901'
)
ON CONFLICT (payment_confirmation_id) DO UPDATE SET
    payment_attempt_id = EXCLUDED.payment_attempt_id,
    provider_transaction_ref = EXCLUDED.provider_transaction_ref,
    currency_code = EXCLUDED.currency_code,
    confirmed_amount = EXCLUDED.confirmed_amount,
    confirmation_status = EXCLUDED.confirmation_status,
    verified_at = EXCLUDED.verified_at,
    confirmed_at = EXCLUDED.confirmed_at,
    correlation_id = EXCLUDED.correlation_id,
    created_by_service_identity_id = EXCLUDED.created_by_service_identity_id;

COMMIT;
