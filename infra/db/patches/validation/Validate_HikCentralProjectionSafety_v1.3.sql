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

    IF EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'sessions.vendor_session_projections'::regclass
          AND conname = 'uq_vendor_session_projections__stable_identity_key'
    ) OR NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'sessions.vendor_session_projections'::regclass
          AND conname = 'uq_vendor_session_projections__target_stable_identity'
          AND pg_get_constraintdef(oid) LIKE '%vendor_system_id%'
          AND pg_get_constraintdef(oid) LIKE '%site_group_id%'
          AND pg_get_constraintdef(oid) LIKE '%site_id%'
          AND pg_get_constraintdef(oid) LIKE '%parking_lot_index_code%'
          AND pg_get_constraintdef(oid) LIKE '%stable_identity_key%'
    ) THEN
        RAISE EXCEPTION 'Projection idempotency is not isolated by the complete target scope.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_indexes
        WHERE schemaname = 'sessions'
          AND indexname = 'ux_vendor_session_projections__target_vendor_record_guid'
          AND indexdef LIKE '%vendor_system_id%'
          AND indexdef LIKE '%site_group_id%'
          AND indexdef LIKE '%site_id%'
          AND indexdef LIKE '%parking_lot_index_code%'
          AND indexdef LIKE '%vendor_record_guid%'
    ) THEN
        RAISE EXCEPTION 'Vendor record GUID uniqueness is not isolated by the complete target scope.';
    END IF;
END $$;
