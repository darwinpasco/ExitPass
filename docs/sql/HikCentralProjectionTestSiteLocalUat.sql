-- Idempotent local-UAT configuration for the synthetic HikCentral TEST SITE.
-- This file creates only non-secret database identities and a disabled target.
-- It does not configure an endpoint, activate polling, or contact HikCentral.

BEGIN;

DO $$
BEGIN
    IF current_database() <> 'exitpass_hikcentral_local_uat' THEN
        RAISE EXCEPTION 'TEST SITE local-UAT configuration refused for database %.', current_database();
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM identity.service_identities
        WHERE service_identity_id = '12000000-0000-0000-0000-000000000001'::uuid
          AND service_identity_code = 'CENTRAL_PMS_API'
          AND identity_status = 'ACTIVE'
    ) THEN
        RAISE EXCEPTION 'Active CENTRAL_PMS_API service identity is required.';
    END IF;

    IF EXISTS (
        SELECT 1 FROM sites.site_groups
        WHERE site_group_id = 'ce000000-0000-0000-0000-000000000001'::uuid
          AND (site_group_code <> 'HIKCENTRAL_TEST_SITE_UAT_GROUP'
               OR site_group_name <> 'HikCentral TEST SITE Local UAT')
    ) OR EXISTS (
        SELECT 1 FROM sites.site_groups
        WHERE site_group_code = 'HIKCENTRAL_TEST_SITE_UAT_GROUP'
          AND site_group_id <> 'ce000000-0000-0000-0000-000000000001'::uuid
    ) THEN
        RAISE EXCEPTION 'TEST SITE local-UAT Site Group identity conflicts with existing data.';
    END IF;

    IF EXISTS (
        SELECT 1 FROM sites.sites
        WHERE site_id = 'c9000000-0000-0000-0000-000000000001'::uuid
          AND (site_group_id <> 'ce000000-0000-0000-0000-000000000001'::uuid
               OR site_code <> 'TEST_SITE'
               OR site_name <> 'TEST SITE')
    ) OR EXISTS (
        SELECT 1 FROM sites.sites
        WHERE site_group_id = 'ce000000-0000-0000-0000-000000000001'::uuid
          AND site_code = 'TEST_SITE'
          AND site_id <> 'c9000000-0000-0000-0000-000000000001'::uuid
    ) THEN
        RAISE EXCEPTION 'TEST SITE local-UAT Site identity conflicts with existing data.';
    END IF;

    IF EXISTS (
        SELECT 1 FROM integration.vendor_systems
        WHERE vendor_system_id = '31bde78a-5dfc-45c3-a1f3-e48abaf90927'::uuid
          AND (vendor_code <> 'HIKCENTRAL'
               OR vendor_system_type <> 'VENDOR_PMS'
               OR environment_code <> 'UAT')
    ) OR EXISTS (
        SELECT 1 FROM integration.vendor_systems
        WHERE vendor_code = 'HIKCENTRAL'
          AND environment_code = 'UAT'
          AND vendor_system_id <> '31bde78a-5dfc-45c3-a1f3-e48abaf90927'::uuid
    ) THEN
        RAISE EXCEPTION 'TEST SITE local-UAT HikCentral Vendor System identity conflicts with existing data.';
    END IF;

    IF EXISTS (
        SELECT 1 FROM sessions.vendor_session_projection_sync_targets
        WHERE projection_sync_target_id = 'abe7da56-1198-4d51-901f-87e8fb7cd40d'::uuid
          AND (site_id <> 'c9000000-0000-0000-0000-000000000001'::uuid
               OR site_group_id <> 'ce000000-0000-0000-0000-000000000001'::uuid
               OR vendor_system_id <> '31bde78a-5dfc-45c3-a1f3-e48abaf90927'::uuid
               OR parking_lot_index_code <> '1')
    ) OR EXISTS (
        SELECT 1 FROM sessions.vendor_session_projection_sync_targets
        WHERE site_id = 'c9000000-0000-0000-0000-000000000001'::uuid
          AND vendor_system_id = '31bde78a-5dfc-45c3-a1f3-e48abaf90927'::uuid
          AND parking_lot_index_code = '1'
          AND projection_sync_target_id <> 'abe7da56-1198-4d51-901f-87e8fb7cd40d'::uuid
    ) THEN
        RAISE EXCEPTION 'TEST SITE local-UAT projection target identity conflicts with existing data.';
    END IF;
END $$;

INSERT INTO sites.site_groups (
    site_group_id, site_group_code, site_group_name, business_label, description,
    operator_entity_name, timezone_name, default_currency_code, site_group_status,
    public_lookup_enabled, default_payment_enabled, effective_from,
    created_by_service_identity_id, updated_by_service_identity_id
)
SELECT
    'ce000000-0000-0000-0000-000000000001',
    'HIKCENTRAL_TEST_SITE_UAT_GROUP',
    'HikCentral TEST SITE Local UAT',
    'SYNTHETIC_TEST',
    'Synthetic local-UAT Site Group for permanent HikCentral projection validation; not an actual operated carpark.',
    'ExitPass Engineering',
    'Asia/Manila',
    'PHP',
    'ACTIVE',
    false,
    false,
    '2026-01-01T00:00:00Z',
    '12000000-0000-0000-0000-000000000001',
    '12000000-0000-0000-0000-000000000001'
WHERE NOT EXISTS (
    SELECT 1 FROM sites.site_groups
    WHERE site_group_id = 'ce000000-0000-0000-0000-000000000001'::uuid
);

INSERT INTO sites.sites (
    site_id, site_group_id, site_code, site_name, site_description, site_type,
    timezone_name, address_line1, city, province, country_code, site_status,
    public_lookup_enabled, payment_enabled, effective_from,
    created_by_service_identity_id, updated_by_service_identity_id
)
SELECT
    'c9000000-0000-0000-0000-000000000001',
    'ce000000-0000-0000-0000-000000000001',
    'TEST_SITE',
    'TEST SITE',
    'Synthetic HikCentral parking lot used only for controlled local-UAT projection validation; not actual PITX.',
    'MIXED_USE_PROPERTY',
    'Asia/Manila',
    'Local HikCentral TEST SITE',
    'Paranaque City',
    'Metro Manila',
    'PH',
    'ACTIVE',
    false,
    false,
    '2026-01-01T00:00:00Z',
    '12000000-0000-0000-0000-000000000001',
    '12000000-0000-0000-0000-000000000001'
WHERE NOT EXISTS (
    SELECT 1 FROM sites.sites
    WHERE site_id = 'c9000000-0000-0000-0000-000000000001'::uuid
);

INSERT INTO integration.vendor_systems (
    vendor_system_id, vendor_code, vendor_name, vendor_system_type,
    vendor_system_status, environment_code, base_url_ref, api_version,
    owner_team, support_contact_ref, effective_from,
    created_by_service_identity_id, updated_by_service_identity_id
)
SELECT
    '31bde78a-5dfc-45c3-a1f3-e48abaf90927',
    'HIKCENTRAL',
    'HikCentral Local UAT',
    'VENDOR_PMS',
    'ACTIVE',
    'UAT',
    'secret://integration/hikcentral/base-url/local-uat',
    'v3.1.0',
    'ExitPass Engineering',
    'local-uat',
    '2026-01-01T00:00:00Z',
    '12000000-0000-0000-0000-000000000001',
    '12000000-0000-0000-0000-000000000001'
WHERE NOT EXISTS (
    SELECT 1 FROM integration.vendor_systems
    WHERE vendor_system_id = '31bde78a-5dfc-45c3-a1f3-e48abaf90927'::uuid
);

INSERT INTO sessions.vendor_session_projection_sync_targets (
    projection_sync_target_id, site_id, site_group_id, vendor_system_id,
    parking_lot_index_code, parking_lot_name, enabled_flag,
    poll_interval_seconds, lookback_window_minutes, page_size,
    health_status, failure_count, created_at, updated_at
)
SELECT
    'abe7da56-1198-4d51-901f-87e8fb7cd40d',
    'c9000000-0000-0000-0000-000000000001',
    'ce000000-0000-0000-0000-000000000001',
    '31bde78a-5dfc-45c3-a1f3-e48abaf90927',
    '1',
    'TEST SITE',
    false,
    60,
    2880,
    100,
    'DISABLED',
    0,
    now(),
    now()
WHERE NOT EXISTS (
    SELECT 1 FROM sessions.vendor_session_projection_sync_targets
    WHERE projection_sync_target_id = 'abe7da56-1198-4d51-901f-87e8fb7cd40d'::uuid
);

COMMIT;
