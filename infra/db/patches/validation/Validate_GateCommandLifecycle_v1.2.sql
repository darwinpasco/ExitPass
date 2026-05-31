/*
 * ExitPass v1.2 validation script.
 *
 * Validates durable vendor-neutral Gate Integration Service command lifecycle support.
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
          AND table_name = 'gate_commands'
    ) THEN
        RAISE EXCEPTION 'missing gates.gate_commands';
    END IF;

    SELECT array_agg(expected.column_name ORDER BY expected.column_name)
    INTO v_missing
    FROM (
        VALUES
            ('command_id'),
            ('command_type'),
            ('source_processing_id'),
            ('source_event_id'),
            ('exit_authorization_id'),
            ('gate_authorization_consumption_id'),
            ('parking_session_id'),
            ('payment_attempt_id'),
            ('tariff_snapshot_id'),
            ('gate_device_id'),
            ('gate_device_identifier'),
            ('lane_id'),
            ('site_id'),
            ('vendor_system_id'),
            ('command_status'),
            ('attempt_count'),
            ('requested_at'),
            ('started_at'),
            ('completed_at'),
            ('failure_code'),
            ('failure_reason'),
            ('correlation_id'),
            ('created_at'),
            ('updated_at')
    ) AS expected(column_name)
    WHERE NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'gates'
          AND table_name = 'gate_commands'
          AND column_name = expected.column_name
    );

    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'missing gate_commands columns: %', v_missing;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_indexes
        WHERE schemaname = 'gates'
          AND tablename = 'gate_commands'
          AND indexname = 'ux_gate_commands__source_processing_command_type'
    ) THEN
        RAISE EXCEPTION 'missing unique command guard ux_gate_commands__source_processing_command_type';
    END IF;

    SELECT array_agg(expected.constraint_name ORDER BY expected.constraint_name)
    INTO v_missing
    FROM (
        VALUES
            ('ck_gate_commands__status'),
            ('ck_gate_commands__attempt_count'),
            ('ck_gate_commands__gate_device_identity'),
            ('ck_gate_commands__started_at'),
            ('ck_gate_commands__completed_at')
    ) AS expected(constraint_name)
    WHERE NOT EXISTS (
        SELECT 1
        FROM pg_constraint con
        JOIN pg_class cls ON cls.oid = con.conrelid
        JOIN pg_namespace n ON n.oid = cls.relnamespace
        WHERE n.nspname = 'gates'
          AND cls.relname = 'gate_commands'
          AND con.conname = expected.constraint_name
    );

    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'missing gate_commands constraints: %', v_missing;
    END IF;

    RAISE NOTICE 'Gate command lifecycle validation passed.';
END;
$$;
