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

COMMIT;
