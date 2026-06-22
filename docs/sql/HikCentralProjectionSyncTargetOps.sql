-- HikCentral projection sync target operational helper.
--
-- Purpose:
-- - List, create, enable, disable, and verify site-scoped projection sync targets.
-- - Support production and UAT operations without storing secrets or creating global targets.
--
-- Authority boundary:
-- - Projection rows are continuity snapshots/read models only.
-- - Projection rows are not parking-session truth, tariff truth, payment truth,
--   payment finality, or exit authority.
--
-- Replace the psql variables below before running write sections.
-- For pgAdmin, replace :'variable_name' tokens with quoted literals.
--
-- Example UAT identifiers only. Do not use these as production defaults:
-- - projection_sync_target_id = abe7da56-1198-4d51-901f-87e8fb7cd40d
-- - site_id = c9000000-0000-0000-0000-000000000001
-- - site_group_id = ce000000-0000-0000-0000-000000000001
-- - vendor_system_id = 31bde78a-5dfc-45c3-a1f3-e48abaf90927
-- - parking_lot_index_code = 1
-- - parking_lot_name = TEST SITE
--
-- psql variable template:
-- \set site_id '<site uuid>'
-- \set site_group_id '<site group uuid>'
-- \set vendor_system_id '<vendor system uuid>'
-- \set parking_lot_index_code '<hikcentral parking lot index code>'
-- \set parking_lot_name '<hikcentral parking lot name>'
-- \set poll_interval_seconds 300
-- \set lookback_window_minutes 180
-- \set page_size 100

-- 1. List current sync targets.
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
ORDER BY enabled_flag DESC, site_id, vendor_system_id, parking_lot_index_code;

-- 2. Verify no global or weakly scoped target exists.
SELECT
    projection_sync_target_id,
    site_id,
    site_group_id,
    vendor_system_id,
    parking_lot_index_code,
    enabled_flag,
    health_status
FROM sessions.vendor_session_projection_sync_targets
WHERE site_id IS NULL
   OR site_group_id IS NULL
   OR vendor_system_id IS NULL
   OR parking_lot_index_code IS NULL
   OR btrim(parking_lot_index_code) = '';

-- Expected: 0 rows.

-- 3. Idempotently create or update one site-scoped target.
-- The helper creates the target disabled by default. Enable it separately in section 4.
BEGIN;

WITH desired AS (
    SELECT
        :'site_id'::uuid AS site_id,
        :'site_group_id'::uuid AS site_group_id,
        :'vendor_system_id'::uuid AS vendor_system_id,
        :'parking_lot_index_code'::text AS parking_lot_index_code,
        :'parking_lot_name'::text AS parking_lot_name,
        false::boolean AS enabled_flag,
        :poll_interval_seconds::integer AS poll_interval_seconds,
        :lookback_window_minutes::integer AS lookback_window_minutes,
        :page_size::integer AS page_size
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
    'DISABLED',
    0,
    now(),
    now()
FROM desired
ON CONFLICT (site_id, vendor_system_id, parking_lot_index_code)
DO UPDATE
SET site_group_id = EXCLUDED.site_group_id,
    parking_lot_name = EXCLUDED.parking_lot_name,
    poll_interval_seconds = EXCLUDED.poll_interval_seconds,
    lookback_window_minutes = EXCLUDED.lookback_window_minutes,
    page_size = EXCLUDED.page_size,
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
    health_status;

COMMIT;

-- 4. Enable one expected target.
UPDATE sessions.vendor_session_projection_sync_targets
SET enabled_flag = true,
    health_status = CASE
        WHEN health_status = 'DISABLED' THEN 'UNKNOWN'
        ELSE health_status
    END,
    updated_at = now(),
    row_version = row_version + 1
WHERE site_id = :'site_id'::uuid
  AND vendor_system_id = :'vendor_system_id'::uuid
  AND parking_lot_index_code = :'parking_lot_index_code'::text
RETURNING
    projection_sync_target_id,
    site_id,
    vendor_system_id,
    parking_lot_index_code,
    enabled_flag,
    health_status,
    updated_at;

-- 5. Disable one target.
UPDATE sessions.vendor_session_projection_sync_targets
SET enabled_flag = false,
    health_status = 'DISABLED',
    updated_at = now(),
    row_version = row_version + 1
WHERE site_id = :'site_id'::uuid
  AND vendor_system_id = :'vendor_system_id'::uuid
  AND parking_lot_index_code = :'parking_lot_index_code'::text
RETURNING
    projection_sync_target_id,
    site_id,
    vendor_system_id,
    parking_lot_index_code,
    enabled_flag,
    health_status,
    updated_at;

-- 6. Verify only expected targets are enabled.
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
    last_success_at,
    last_failure_at,
    last_error_code,
    last_error_message
FROM sessions.vendor_session_projection_sync_targets
WHERE enabled_flag = true
ORDER BY site_id, vendor_system_id, parking_lot_index_code;

-- 7. Verify one target health and freshness.
SELECT
    projection_sync_target_id,
    enabled_flag,
    health_status,
    failure_count,
    last_attempt_at,
    last_success_at,
    last_failure_at,
    now() - last_success_at AS success_age,
    last_error_code,
    last_error_message
FROM sessions.vendor_session_projection_sync_targets
WHERE site_id = :'site_id'::uuid
  AND vendor_system_id = :'vendor_system_id'::uuid
  AND parking_lot_index_code = :'parking_lot_index_code'::text;

-- 8. Verify projection freshness for one parking lot.
SELECT
    parking_lot_index_code,
    projection_status,
    count(*) AS projection_count,
    max(last_refreshed_at) AS latest_refreshed_at,
    now() - max(last_refreshed_at) AS latest_projection_age
FROM sessions.vendor_session_projections
WHERE site_id = :'site_id'::uuid
  AND vendor_system_id = :'vendor_system_id'::uuid
  AND parking_lot_index_code = :'parking_lot_index_code'::text
GROUP BY parking_lot_index_code, projection_status
ORDER BY projection_status;

-- 9. Verify stable identity uniqueness behavior.
SELECT
    stable_identity_type,
    stable_identity_key,
    count(*) AS duplicate_count
FROM sessions.vendor_session_projections
WHERE site_id = :'site_id'::uuid
  AND vendor_system_id = :'vendor_system_id'::uuid
  AND parking_lot_index_code = :'parking_lot_index_code'::text
GROUP BY stable_identity_type, stable_identity_key
HAVING count(*) > 1
ORDER BY duplicate_count DESC;

-- Expected: 0 rows.
