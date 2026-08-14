-- ExitPass v1.3 HikCentral projection safety hardening.
-- Existing target enablement is preserved; newly registered targets remain disabled.

BEGIN;

ALTER TABLE sessions.vendor_session_projection_sync_targets
    ALTER COLUMN enabled_flag SET DEFAULT false,
    ALTER COLUMN poll_interval_seconds SET DEFAULT 60,
    ALTER COLUMN health_status SET DEFAULT 'DISABLED';

ALTER TABLE sessions.vendor_session_projection_sync_targets
    ADD COLUMN IF NOT EXISTS last_lock_contention_at timestamptz NULL,
    ADD COLUMN IF NOT EXISTS lock_contention_count integer DEFAULT 0 NOT NULL;

ALTER TABLE sessions.vendor_session_projection_sync_targets
    DROP CONSTRAINT IF EXISTS ck_vendor_session_projection_sync_targets__health_status;

ALTER TABLE sessions.vendor_session_projection_sync_targets
    ADD CONSTRAINT ck_vendor_session_projection_sync_targets__health_status
    CHECK (health_status IN ('HEALTHY', 'DEGRADED', 'FAILING', 'DISABLED', 'DEFERRED', 'UNKNOWN'));

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'sessions.vendor_session_projection_sync_targets'::regclass
          AND conname = 'ck_vendor_projection_targets__lock_contention_non_negative'
    ) THEN
        ALTER TABLE sessions.vendor_session_projection_sync_targets
            ADD CONSTRAINT ck_vendor_projection_targets__lock_contention_non_negative
            CHECK (lock_contention_count >= 0);
    END IF;
END $$;

COMMENT ON COLUMN sessions.vendor_session_projection_sync_targets.last_lock_contention_at IS
    'Last cycle deferred because another scheduler held the target-scoped advisory lock.';
COMMENT ON COLUMN sessions.vendor_session_projection_sync_targets.lock_contention_count IS
    'Cumulative target-scoped advisory lock contention count.';

ALTER TABLE sessions.vendor_session_projections
    DROP CONSTRAINT IF EXISTS uq_vendor_session_projections__stable_identity_key;

DROP INDEX IF EXISTS sessions.uq_vendor_session_projections__stable_identity_key;

CREATE UNIQUE INDEX IF NOT EXISTS uq_vendor_session_projections__target_stable_identity
ON sessions.vendor_session_projections (
    vendor_system_id,
    site_group_id,
    site_id,
    parking_lot_index_code,
    stable_identity_key
);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'sessions.vendor_session_projections'::regclass
          AND conname = 'uq_vendor_session_projections__target_stable_identity'
    ) THEN
        ALTER TABLE sessions.vendor_session_projections
            ADD CONSTRAINT uq_vendor_session_projections__target_stable_identity
            UNIQUE USING INDEX uq_vendor_session_projections__target_stable_identity;
    END IF;
END $$;

DROP INDEX IF EXISTS sessions.ux_vendor_session_projections__vendor_record_guid;

CREATE UNIQUE INDEX IF NOT EXISTS ux_vendor_session_projections__target_vendor_record_guid
ON sessions.vendor_session_projections (
    vendor_system_id,
    site_group_id,
    site_id,
    parking_lot_index_code,
    vendor_record_guid
)
WHERE vendor_system_id IS NOT NULL
  AND site_group_id IS NOT NULL
  AND site_id IS NOT NULL
  AND parking_lot_index_code IS NOT NULL
  AND vendor_record_guid IS NOT NULL;

COMMENT ON CONSTRAINT uq_vendor_session_projections__target_stable_identity
ON sessions.vendor_session_projections IS
    'Idempotent projection identity isolated by Vendor System, Site Group, Site, and parking lot.';

COMMIT;
