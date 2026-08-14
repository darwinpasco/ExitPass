BEGIN TRANSACTION READ ONLY;

DO $$
DECLARE
    v_enabled_count integer;
BEGIN
    IF current_database() <> 'exitpass_hikcentral_local_uat' THEN
        RAISE EXCEPTION 'TEST SITE local-UAT validation refused for database %.', current_database();
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM sites.site_groups
        WHERE site_group_id = 'ce000000-0000-0000-0000-000000000001'::uuid
          AND site_group_code = 'HIKCENTRAL_TEST_SITE_UAT_GROUP'
          AND site_group_name = 'HikCentral TEST SITE Local UAT'
          AND site_group_status = 'ACTIVE'
    ) THEN
        RAISE EXCEPTION 'Expected synthetic TEST SITE Site Group is missing or inconsistent.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM sites.sites
        WHERE site_id = 'c9000000-0000-0000-0000-000000000001'::uuid
          AND site_group_id = 'ce000000-0000-0000-0000-000000000001'::uuid
          AND site_code = 'TEST_SITE'
          AND site_name = 'TEST SITE'
          AND site_status = 'ACTIVE'
    ) THEN
        RAISE EXCEPTION 'Expected synthetic TEST SITE Site is missing or inconsistent.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM integration.vendor_systems
        WHERE vendor_system_id = '31bde78a-5dfc-45c3-a1f3-e48abaf90927'::uuid
          AND vendor_code = 'HIKCENTRAL'
          AND vendor_system_type = 'VENDOR_PMS'
          AND vendor_system_status = 'ACTIVE'
          AND environment_code = 'UAT'
    ) THEN
        RAISE EXCEPTION 'Expected local-UAT HikCentral Vendor System is missing or inconsistent.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM sessions.vendor_session_projection_sync_targets
        WHERE projection_sync_target_id = 'abe7da56-1198-4d51-901f-87e8fb7cd40d'::uuid
          AND site_id = 'c9000000-0000-0000-0000-000000000001'::uuid
          AND site_group_id = 'ce000000-0000-0000-0000-000000000001'::uuid
          AND vendor_system_id = '31bde78a-5dfc-45c3-a1f3-e48abaf90927'::uuid
          AND parking_lot_index_code = '1'
          AND parking_lot_name = 'TEST SITE'
          AND poll_interval_seconds = 60
    ) THEN
        RAISE EXCEPTION 'Expected target-scoped TEST SITE projection configuration is missing or inconsistent.';
    END IF;

    SELECT count(*) INTO v_enabled_count
    FROM sessions.vendor_session_projection_sync_targets
    WHERE enabled_flag;

    IF v_enabled_count > 1 THEN
        RAISE EXCEPTION 'More than one projection target is enabled in the local-UAT database.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM sessions.vendor_session_projection_sync_targets
        WHERE enabled_flag
          AND projection_sync_target_id <> 'abe7da56-1198-4d51-901f-87e8fb7cd40d'::uuid
    ) THEN
        RAISE EXCEPTION 'An unexpected projection target is enabled in the local-UAT database.';
    END IF;
END $$;

SELECT
    target.projection_sync_target_id,
    site_group.site_group_code,
    site.site_code,
    site.site_name,
    vendor.vendor_code,
    vendor.environment_code,
    target.parking_lot_index_code,
    target.enabled_flag,
    target.poll_interval_seconds,
    target.health_status,
    target.last_attempt_at,
    target.last_success_at,
    target.failure_count,
    target.lock_contention_count
FROM sessions.vendor_session_projection_sync_targets target
JOIN sites.site_groups site_group ON site_group.site_group_id = target.site_group_id
JOIN sites.sites site ON site.site_id = target.site_id
JOIN integration.vendor_systems vendor ON vendor.vendor_system_id = target.vendor_system_id
WHERE target.projection_sync_target_id = 'abe7da56-1198-4d51-901f-87e8fb7cd40d'::uuid;

ROLLBACK;
