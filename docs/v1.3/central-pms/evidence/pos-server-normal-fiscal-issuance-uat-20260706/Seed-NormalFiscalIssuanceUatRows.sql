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

INSERT INTO identity.service_identities (
    service_identity_id,
    service_identity_code,
    service_identity_name,
    identity_type,
    identity_status,
    owning_service_name,
    credential_reference,
    credential_type,
    effective_from,
    created_at,
    updated_at,
    row_version
)
VALUES (
    '00000000-0000-4000-8000-000000000901',
    'CENTRAL-PMS-UAT-FISCAL-ISSUANCE',
    'Central PMS UAT Fiscal Issuance Service',
    'INTERNAL_SERVICE',
    'ACTIVE',
    'ExitPass.CentralPms',
    NULL,
    NULL,
    TIMESTAMPTZ '2026-07-06 00:00:00+08',
    NOW(),
    NOW(),
    1
)
ON CONFLICT (service_identity_id) DO UPDATE SET
    service_identity_code = EXCLUDED.service_identity_code,
    service_identity_name = EXCLUDED.service_identity_name,
    identity_type = EXCLUDED.identity_type,
    identity_status = EXCLUDED.identity_status,
    owning_service_name = EXCLUDED.owning_service_name,
    updated_at = NOW(),
    row_version = identity.service_identities.row_version + 1;

INSERT INTO sites.site_groups (
    site_group_id,
    site_group_code,
    site_group_name,
    business_label,
    description,
    operator_entity_name,
    timezone_name,
    default_currency_code,
    site_group_status,
    public_lookup_enabled,
    default_payment_enabled,
    effective_from,
    created_at,
    created_by_service_identity_id,
    updated_at,
    updated_by_service_identity_id,
    row_version
)
VALUES (
    '00000000-0000-4000-8000-000000000401',
    'DEV-UAT-SITE-GROUP-ATC',
    'Disposable UAT Site Group ATC',
    'Disposable UAT',
    'Disposable local-only Central PMS to POS Server UAT seed data.',
    'ExitPass Local UAT',
    'Asia/Manila',
    'PHP',
    'ACTIVE',
    FALSE,
    TRUE,
    TIMESTAMPTZ '2026-07-06 00:00:00+08',
    NOW(),
    '00000000-0000-4000-8000-000000000901',
    NOW(),
    '00000000-0000-4000-8000-000000000901',
    1
)
ON CONFLICT (site_group_id) DO UPDATE SET
    site_group_code = EXCLUDED.site_group_code,
    site_group_name = EXCLUDED.site_group_name,
    site_group_status = EXCLUDED.site_group_status,
    updated_at = NOW(),
    updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
    row_version = sites.site_groups.row_version + 1;

INSERT INTO sites.sites (
    site_id,
    site_group_id,
    site_code,
    site_name,
    site_description,
    site_type,
    timezone_name,
    city,
    province,
    country_code,
    site_status,
    public_lookup_enabled,
    payment_enabled,
    effective_from,
    created_at,
    created_by_service_identity_id,
    updated_at,
    updated_by_service_identity_id,
    row_version
)
VALUES (
    '00000000-0000-4000-8000-000000000402',
    '00000000-0000-4000-8000-000000000401',
    'DEV-SITE-ATC-001',
    'Disposable UAT Site ATC 001',
    'Disposable local-only Central PMS to POS Server UAT site.',
    'MALL_PARKING',
    'Asia/Manila',
    'Makati',
    'Metro Manila',
    'PH',
    'ACTIVE',
    FALSE,
    TRUE,
    TIMESTAMPTZ '2026-07-06 00:00:00+08',
    NOW(),
    '00000000-0000-4000-8000-000000000901',
    NOW(),
    '00000000-0000-4000-8000-000000000901',
    1
)
ON CONFLICT (site_id) DO UPDATE SET
    site_group_id = EXCLUDED.site_group_id,
    site_code = EXCLUDED.site_code,
    site_name = EXCLUDED.site_name,
    site_status = EXCLUDED.site_status,
    payment_enabled = EXCLUDED.payment_enabled,
    updated_at = NOW(),
    updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
    row_version = sites.sites.row_version + 1;

INSERT INTO integration.vendor_systems (
    vendor_system_id,
    vendor_code,
    vendor_name,
    vendor_system_type,
    vendor_system_status,
    environment_code,
    base_url_ref,
    api_version,
    owner_team,
    support_contact_ref,
    effective_from,
    created_at,
    created_by_service_identity_id,
    updated_at,
    updated_by_service_identity_id,
    row_version
)
VALUES (
    '00000000-0000-4000-8000-000000000501',
    'DEV-UAT-VENDOR-PMS',
    'Disposable UAT Vendor PMS',
    'VENDOR_PMS',
    'ACTIVE',
    'LOCAL-UAT',
    'local-only',
    'v1',
    'Central PMS UAT',
    'local-only',
    TIMESTAMPTZ '2026-07-06 00:00:00+08',
    NOW(),
    '00000000-0000-4000-8000-000000000901',
    NOW(),
    '00000000-0000-4000-8000-000000000901',
    1
)
ON CONFLICT (vendor_system_id) DO UPDATE SET
    vendor_code = EXCLUDED.vendor_code,
    vendor_name = EXCLUDED.vendor_name,
    vendor_system_status = EXCLUDED.vendor_system_status,
    environment_code = EXCLUDED.environment_code,
    updated_at = NOW(),
    updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
    row_version = integration.vendor_systems.row_version + 1;

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
    '00000000-0000-4000-8000-000000000303',
    '00000000-0000-4000-8000-000000000401',
    '00000000-0000-4000-8000-000000000402',
    '00000000-0000-4000-8000-000000000501',
    'CPS-POS-UAT-PARKING-SESSION-001',
    NULL,
    'UAT-001',
    NULL,
    'UAT-TICKET-001',
    TIMESTAMPTZ '2026-07-06 08:00:00+08',
    'PAYMENT_REQUIRED',
    'ACTIVE',
    '00000000-0000-4000-8000-000000000101',
    NOW(),
    '00000000-0000-4000-8000-000000000901',
    NOW(),
    '00000000-0000-4000-8000-000000000901',
    1
)
ON CONFLICT (parking_session_id) DO UPDATE SET
    site_group_id = EXCLUDED.site_group_id,
    site_id = EXCLUDED.site_id,
    vendor_system_id = EXCLUDED.vendor_system_id,
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
    '00000000-0000-4000-8000-000000000601',
    '00000000-0000-4000-8000-000000000303',
    NULL,
    '00000000-0000-4000-8000-000000000501',
    'CPS-POS-UAT-TARIFF-001',
    'CPS-POS-UAT-TARIFF-V1',
    'PHP',
    100.00,
    0.00,
    0.00,
    100.00,
    NULL,
    NULL,
    'ACTIVE',
    TIMESTAMPTZ '2026-07-06 08:05:00+08',
    TIMESTAMPTZ '2026-07-07 08:05:00+08',
    NULL,
    '00000000-0000-4000-8000-000000000101',
    NOW(),
    '00000000-0000-4000-8000-000000000901',
    NOW(),
    '00000000-0000-4000-8000-000000000901',
    1
)
ON CONFLICT (tariff_snapshot_id) DO UPDATE SET
    parking_session_id = EXCLUDED.parking_session_id,
    vendor_system_id = EXCLUDED.vendor_system_id,
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
    '00000000-0000-4000-8000-000000000302',
    '00000000-0000-4000-8000-000000000303',
    '00000000-0000-4000-8000-000000000601',
    'CPS-POS-UAT-PAYMENT-ATTEMPT-001',
    NULL,
    'PHP',
    100.00,
    'CONFIRMED',
    TIMESTAMPTZ '2026-07-06 08:10:00+08',
    TIMESTAMPTZ '2026-07-07 08:10:00+08',
    TIMESTAMPTZ '2026-07-06 08:15:00+08',
    NULL,
    '00000000-0000-4000-8000-000000000101',
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
    '00000000-0000-4000-8000-000000000301',
    '00000000-0000-4000-8000-000000000302',
    NULL,
    NULL,
    'CPS-POS-UAT:CPS-POS-UAT-20260703-DEV-ATC-001:newly_created:001',
    'PHP',
    100.00,
    'RECORDED',
    TIMESTAMPTZ '2026-07-06 08:16:00+08',
    TIMESTAMPTZ '2026-07-06 08:16:00+08',
    '00000000-0000-4000-8000-000000000101',
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
