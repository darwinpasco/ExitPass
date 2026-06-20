-- HikCentral vendor session projection live UAT helper.
--
-- This file is intentionally placeholder-based. Replace every <...> value in a
-- local copy or psql session before running. Do not commit real credentials,
-- local-only IDs, or vendor secrets.
--
-- Authority boundary:
-- - Projection rows are continuity snapshots/read models only.
-- - Projection rows are not parking-session truth, tariff truth, payment truth,
--   payment finality, or exit authority.

BEGIN;

-- 1. Preflight: identify existing site/vendor records.
-- Run these first if the UAT IDs are unknown.
SELECT
    site_group_id,
    site_group_code,
    site_group_name,
    site_group_status,
    timezone_name
FROM sites.site_groups
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
WHERE vendor_code ILIKE '%hik%'
   OR vendor_name ILIKE '%hik%'
ORDER BY created_at DESC
LIMIT 25;

-- 2. Idempotent sync-target upsert.
-- Replace placeholders before running this statement.
WITH desired AS (
    SELECT
        '<site-id>'::uuid AS site_id,
        '<site-group-id>'::uuid AS site_group_id,
        '<vendor-system-id>'::uuid AS vendor_system_id,
        '<parking-lot-index-code>'::text AS parking_lot_index_code,
        NULLIF('<parking-lot-name>', '')::text AS parking_lot_name,
        true::boolean AS enabled_flag,
        300::integer AS poll_interval_seconds,
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
WHERE site_id = '<site-id>'::uuid
  AND vendor_system_id = '<vendor-system-id>'::uuid
  AND parking_lot_index_code = '<parking-lot-index-code>';

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
WHERE parking_lot_index_code = '<parking-lot-index-code>'
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
WHERE parking_lot_index_code = '<parking-lot-index-code>'
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
WHERE parking_lot_index_code = '<parking-lot-index-code>'
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
WHERE parking_lot_index_code = '<parking-lot-index-code>'
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
WHERE parking_lot_index_code = '<parking-lot-index-code>'
GROUP BY stable_identity_type, stable_identity_key
HAVING count(*) > 1
ORDER BY duplicate_count DESC, last_refreshed_at DESC;

-- Expected for query 8 after repeated sync: zero rows.
