/*
 * ExitPass v1.2 validation script.
 *
 * Validates durable vendor-neutral Gate Integration Service command retry/failure policy support.
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
            ('max_attempts'),
            ('retry_policy_code'),
            ('last_attempted_at'),
            ('next_attempt_at'),
            ('terminal_failure_at'),
            ('last_failure_code'),
            ('last_failure_reason')
    ) AS expected(column_name)
    WHERE NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'gates'
          AND table_name = 'gate_commands'
          AND column_name = expected.column_name
    );

    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'missing gate command retry policy columns: %', v_missing;
    END IF;

    SELECT array_agg(expected.constraint_name ORDER BY expected.constraint_name)
    INTO v_missing
    FROM (
        VALUES
            ('ck_gate_commands__max_attempts'),
            ('ck_gate_commands__attempt_policy'),
            ('ck_gate_commands__retryable_next_attempt'),
            ('ck_gate_commands__terminal_failure_at')
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
        RAISE EXCEPTION 'missing gate command retry policy constraints: %', v_missing;
    END IF;

    SELECT array_agg(expected.index_name ORDER BY expected.index_name)
    INTO v_missing
    FROM (
        VALUES
            ('ix_gate_commands__next_attempt_at'),
            ('ix_gate_commands__terminal_failure_at')
    ) AS expected(index_name)
    WHERE NOT EXISTS (
        SELECT 1
        FROM pg_indexes
        WHERE schemaname = 'gates'
          AND tablename = 'gate_commands'
          AND indexname = expected.index_name
    );

    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'missing gate command retry policy indexes: %', v_missing;
    END IF;

    RAISE NOTICE 'Gate command retry/failure policy validation passed.';
END;
$$;
