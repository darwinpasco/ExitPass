-- HikCentral vendor session projection live UAT helper.
--
-- This file contains confirmed non-secret UAT identifiers. Keep HikCentral
-- credentials and database passwords outside this file.
--
-- Authority boundary:
-- - Projection rows are continuity snapshots/read models only.
-- - Projection rows are not parking-session truth, tariff truth, payment truth,
--   payment finality, or exit authority.

-- Confirmed UAT values:
-- - site_id = c9000000-0000-0000-0000-000000000001
-- - site_group_id = ce000000-0000-0000-0000-000000000001
-- - vendor_system_id = 31bde78a-5dfc-45c3-a1f3-e48abaf90927
-- - vendor_code = HIKCENTRAL
-- - environment_code = UAT
-- - parking_lot_index_code = 1
-- - parking_lot_name = TEST SITE
-- - service_identity_code = CENTRAL_PMS_API
--
-- Before running the sync-target upsert, verify that the projection schema
-- exists. If either required table is missing, run:
--   docs/sql/HikCentralProjectionSchemaPatch.sql

-- 0. Verify required projection schema objects exist.
SELECT table_schema, table_name
FROM information_schema.tables
WHERE table_schema = 'sessions'
  AND table_name IN (
      'vendor_session_projections',
      'vendor_session_projection_sync_targets'
  )
ORDER BY table_name;

-- Expected: 2 rows. If fewer than 2 rows are returned, stop and apply
-- docs/sql/HikCentralProjectionSchemaPatch.sql before continuing.
DO $$
DECLARE
    missing_table_count integer;
BEGIN
    SELECT 2 - count(*)
    INTO missing_table_count
    FROM information_schema.tables
    WHERE table_schema = 'sessions'
      AND table_name IN (
          'vendor_session_projections',
          'vendor_session_projection_sync_targets'
      );

    IF missing_table_count <> 0 THEN
        RAISE EXCEPTION
            'Missing HikCentral projection schema objects. Apply docs/sql/HikCentralProjectionSchemaPatch.sql before running this UAT helper.';
    END IF;
END $$;

BEGIN;

-- 1. Preflight: confirm existing site/vendor/service identity records.
SELECT
    site_group_id,
    site_group_code,
    site_group_name,
    site_group_status,
    timezone_name
FROM sites.site_groups
WHERE site_group_id = 'ce000000-0000-0000-0000-000000000001'::uuid
ORDER BY created_at DESC
LIMIT 25;

SELECT
    s.site_id,
    s.site_group_id,
    sg.site_group_code,
    s.site_code,
    s.site_name,
    s.site_status,
    s.timezone_name
FROM sites.sites s
JOIN sites.site_groups sg
  ON sg.site_group_id = s.site_group_id
WHERE s.site_id = 'c9000000-0000-0000-0000-000000000001'::uuid
  AND s.site_group_id = 'ce000000-0000-0000-0000-000000000001'::uuid
ORDER BY s.created_at DESC
LIMIT 25;

SELECT
    vendor_system_id,
    vendor_code,
    vendor_name,
    vendor_system_type,
    vendor_system_status,
    environment_code,
    base_url_ref
FROM integration.vendor_systems
WHERE vendor_system_id = '31bde78a-5dfc-45c3-a1f3-e48abaf90927'::uuid
  AND vendor_code = 'HIKCENTRAL'
  AND environment_code = 'UAT'
ORDER BY created_at DESC
LIMIT 25;

SELECT
    service_identity_id,
    service_identity_code,
    service_identity_name,
    identity_status
FROM identity.service_identities
WHERE service_identity_code = 'CENTRAL_PMS_API'
ORDER BY created_at DESC
LIMIT 25;

-- Confirm parking lot index code and name from HikCentral before running the
-- upsert. Source API:
--   POST /artemis/api/vehicle/v1/parkinglot/list
--
-- Expected live UAT mapping:
--   parkingLotIndexCode = 1
--   parkingLotName = TEST SITE

-- 2. Idempotent sync-target upsert for the confirmed UAT site/parking lot.
WITH desired AS (
    SELECT
        'c9000000-0000-0000-0000-000000000001'::uuid AS site_id,
        'ce000000-0000-0000-0000-000000000001'::uuid AS site_group_id,
        '31bde78a-5dfc-45c3-a1f3-e48abaf90927'::uuid AS vendor_system_id,
        '1'::text AS parking_lot_index_code,
        'TEST SITE'::text AS parking_lot_name,
        true::boolean AS enabled_flag,
        60::integer AS poll_interval_seconds,
        180::integer AS lookback_window_minutes,
        100::integer AS page_size,
        'UNKNOWN'::text AS health_status
)
INSERT INTO sessions.vendor_session_projection_sync_targets (
    site_id,
    site_group_id,
    vendor_system_id,
    parking_lot_index_code,
    parking_lot_name,
    enabled_flag,
    poll_interval_seconds,
    lookback_window_minutes,
    page_size,
    health_status,
    failure_count,
    created_at,
    updated_at
)
SELECT
    site_id,
    site_group_id,
    vendor_system_id,
    parking_lot_index_code,
    parking_lot_name,
    enabled_flag,
    poll_interval_seconds,
    lookback_window_minutes,
    page_size,
    health_status,
    0,
    now(),
    now()
FROM desired
ON CONFLICT (site_id, vendor_system_id, parking_lot_index_code)
DO UPDATE
SET site_group_id = EXCLUDED.site_group_id,
    parking_lot_name = EXCLUDED.parking_lot_name,
    enabled_flag = EXCLUDED.enabled_flag,
    poll_interval_seconds = EXCLUDED.poll_interval_seconds,
    lookback_window_minutes = EXCLUDED.lookback_window_minutes,
    page_size = EXCLUDED.page_size,
    health_status = CASE
        WHEN sessions.vendor_session_projection_sync_targets.enabled_flag IS DISTINCT FROM EXCLUDED.enabled_flag
             AND EXCLUDED.enabled_flag = false
            THEN 'DISABLED'
        ELSE sessions.vendor_session_projection_sync_targets.health_status
    END,
    updated_at = now(),
    row_version = sessions.vendor_session_projection_sync_targets.row_version + 1
RETURNING
    projection_sync_target_id,
    site_id,
    site_group_id,
    vendor_system_id,
    parking_lot_index_code,
    parking_lot_name,
    enabled_flag,
    poll_interval_seconds,
    lookback_window_minutes,
    page_size,
    health_status,
    failure_count,
    last_success_at,
    last_failure_at,
    last_attempt_at;

COMMIT;

-- 3. Verify sync target exists and is enabled.
SELECT
    projection_sync_target_id,
    site_id,
    site_group_id,
    vendor_system_id,
    parking_lot_index_code,
    parking_lot_name,
    enabled_flag,
    poll_interval_seconds,
    lookback_window_minutes,
    page_size,
    health_status,
    failure_count,
    last_attempt_at,
    last_success_at,
    last_failure_at,
    last_error_code,
    last_error_message,
    updated_at
FROM sessions.vendor_session_projection_sync_targets
WHERE site_id = 'c9000000-0000-0000-0000-000000000001'::uuid
  AND vendor_system_id = '31bde78a-5dfc-45c3-a1f3-e48abaf90927'::uuid
  AND parking_lot_index_code = '1';

-- 4. Verify projection rows for the parking lot.
SELECT
    vendor_session_projection_id,
    parking_lot_index_code,
    parking_lot_name,
    vendor_record_guid,
    card_num,
    plate_license,
    enter_time,
    exit_time,
    projection_status,
    stable_identity_type,
    stable_identity_key,
    source_api,
    last_seen_at,
    last_refreshed_at,
    now() - last_refreshed_at AS freshness_age,
    correlation_id
FROM sessions.vendor_session_projections
WHERE parking_lot_index_code = '1'
ORDER BY last_refreshed_at DESC
LIMIT 50;

-- 5. Verify cardNum coverage for ticket/card lookup.
SELECT
    card_num,
    count(*) AS projection_count,
    max(last_refreshed_at) AS latest_refreshed_at,
    max(enter_time) AS latest_enter_time,
    max(exit_time) AS latest_exit_time
FROM sessions.vendor_session_projections
WHERE parking_lot_index_code = '1'
  AND card_num IS NOT NULL
GROUP BY card_num
ORDER BY latest_refreshed_at DESC
LIMIT 50;

-- 6. Verify active and exited projection counts.
SELECT
    projection_status,
    count(*) AS projection_count,
    max(last_refreshed_at) AS latest_refreshed_at
FROM sessions.vendor_session_projections
WHERE parking_lot_index_code = '1'
GROUP BY projection_status
ORDER BY projection_status;

-- 7. Candidate ticket/card lookup checks.
SELECT
    vendor_session_projection_id,
    card_num,
    plate_license,
    enter_time,
    exit_time,
    projection_status,
    last_refreshed_at,
    now() - last_refreshed_at AS freshness_age,
    stable_identity_type,
    stable_identity_key
FROM sessions.vendor_session_projections
WHERE parking_lot_index_code = '1'
  AND card_num IN (
      '3518855073102',
      '3518855085105',
      '3519278781100',
      '3519281044100'
  )
ORDER BY last_refreshed_at DESC;

-- 8. Verify stable identity/idempotency behavior.
SELECT
    stable_identity_type,
    stable_identity_key,
    count(*) AS duplicate_count,
    min(first_seen_at) AS first_seen_at,
    max(last_seen_at) AS last_seen_at,
    max(last_refreshed_at) AS last_refreshed_at
FROM sessions.vendor_session_projections
WHERE parking_lot_index_code = '1'
GROUP BY stable_identity_type, stable_identity_key
HAVING count(*) > 1
ORDER BY duplicate_count DESC, last_refreshed_at DESC;

-- Expected for query 8 after repeated sync: zero rows.
