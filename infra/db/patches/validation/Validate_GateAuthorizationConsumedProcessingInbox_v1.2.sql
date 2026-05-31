/*
 * ExitPass v1.2 validation script.
 *
 * Validates durable Gate Integration Service processing support for GateAuthorizationConsumed handoffs.
 * This script is read-only except for temporary DO-block execution state.
 */

DO $$
DECLARE
    v_missing text[];
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.tables
        WHERE table_schema = 'gates'
          AND table_name = 'gate_authorization_consumed_processing'
    ) THEN
        RAISE EXCEPTION 'missing gates.gate_authorization_consumed_processing';
    END IF;

    SELECT array_agg(expected.column_name ORDER BY expected.column_name)
    INTO v_missing
    FROM (
        VALUES
            ('processing_id'),
            ('processing_key'),
            ('event_id'),
            ('event_type'),
            ('source_event_ref'),
            ('gate_authorization_consumption_id'),
            ('exit_authorization_id'),
            ('parking_session_id'),
            ('payment_attempt_id'),
            ('tariff_snapshot_id'),
            ('gate_device_id'),
            ('gate_device_identifier'),
            ('lane_id'),
            ('site_id'),
            ('vendor_system_id'),
            ('consumed_at_utc'),
            ('correlation_id'),
            ('processing_status'),
            ('result_code'),
            ('attempt_count'),
            ('first_seen_at'),
            ('last_attempted_at'),
            ('processed_at'),
            ('last_failure_code'),
            ('last_failure_reason'),
            ('created_at'),
            ('updated_at')
    ) AS expected(column_name)
    WHERE NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'gates'
          AND table_name = 'gate_authorization_consumed_processing'
          AND column_name = expected.column_name
    );

    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'missing gate_authorization_consumed_processing columns: %', v_missing;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_indexes
        WHERE schemaname = 'gates'
          AND tablename = 'gate_authorization_consumed_processing'
          AND indexname = 'ux_gate_auth_consumed_processing__key_event_type'
    ) THEN
        RAISE EXCEPTION 'missing unique processing key guard ux_gate_auth_consumed_processing__key_event_type';
    END IF;

    SELECT array_agg(expected.constraint_name ORDER BY expected.constraint_name)
    INTO v_missing
    FROM (
        VALUES
            ('ck_gate_auth_consumed_processing__status'),
            ('ck_gate_auth_consumed_processing__attempt_count'),
            ('ck_gate_auth_consumed_processing__processed_at'),
            ('ck_gate_auth_consumed_processing__gate_device_identity')
    ) AS expected(constraint_name)
    WHERE NOT EXISTS (
        SELECT 1
        FROM pg_constraint con
        JOIN pg_class cls ON cls.oid = con.conrelid
        JOIN pg_namespace n ON n.oid = cls.relnamespace
        WHERE n.nspname = 'gates'
          AND cls.relname = 'gate_authorization_consumed_processing'
          AND con.conname = expected.constraint_name
    );

    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'missing gate_authorization_consumed_processing constraints: %', v_missing;
    END IF;

    RAISE NOTICE 'GateAuthorizationConsumed processing inbox validation passed.';
END;
$$;
