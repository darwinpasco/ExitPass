-- Validation for ExitPass v1.3 HikCentral projection safety hardening.

DO $$
DECLARE
    v_enabled_default text;
    v_poll_default text;
    v_health_default text;
BEGIN
    SELECT column_default
    INTO v_enabled_default
    FROM information_schema.columns
    WHERE table_schema = 'sessions'
      AND table_name = 'vendor_session_projection_sync_targets'
      AND column_name = 'enabled_flag';

    IF v_enabled_default IS NULL OR lower(v_enabled_default) NOT LIKE '%false%' THEN
        RAISE EXCEPTION 'Projection target enabled_flag does not default to false.';
    END IF;

    SELECT column_default
    INTO v_poll_default
    FROM information_schema.columns
    WHERE table_schema = 'sessions'
      AND table_name = 'vendor_session_projection_sync_targets'
      AND column_name = 'poll_interval_seconds';

    IF v_poll_default IS NULL OR v_poll_default NOT LIKE '%60%' THEN
        RAISE EXCEPTION 'Projection target poll_interval_seconds does not default to 60.';
    END IF;

    SELECT column_default
    INTO v_health_default
    FROM information_schema.columns
    WHERE table_schema = 'sessions'
      AND table_name = 'vendor_session_projection_sync_targets'
      AND column_name = 'health_status';

    IF v_health_default IS NULL OR v_health_default NOT LIKE '%DISABLED%' THEN
        RAISE EXCEPTION 'Projection target health_status does not default to DISABLED.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'sessions'
          AND table_name = 'vendor_session_projection_sync_targets'
          AND column_name = 'last_lock_contention_at'
    ) OR NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'sessions'
          AND table_name = 'vendor_session_projection_sync_targets'
          AND column_name = 'lock_contention_count'
    ) THEN
        RAISE EXCEPTION 'Projection lock contention health columns are missing.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'sessions.vendor_session_projection_sync_targets'::regclass
          AND conname = 'ck_vendor_session_projection_sync_targets__health_status'
          AND pg_get_constraintdef(oid) LIKE '%DEFERRED%'
    ) THEN
        RAISE EXCEPTION 'Projection health constraint does not allow DEFERRED.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'sessions.vendor_session_projection_sync_targets'::regclass
          AND conname = 'ck_vendor_projection_targets__lock_contention_non_negative'
    ) THEN
        RAISE EXCEPTION 'Projection lock contention count constraint is missing.';
    END IF;
END $$;
