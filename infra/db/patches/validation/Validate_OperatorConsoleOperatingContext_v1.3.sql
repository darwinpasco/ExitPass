DO $$
BEGIN
    IF to_regclass('operator_console.operator_session_contexts') IS NULL THEN
        RAISE EXCEPTION 'operator_console.operator_session_contexts is missing';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'operator_console.operator_session_contexts'::regclass
          AND conname = 'ux_operator_session_contexts__human_session'
    ) THEN
        RAISE EXCEPTION 'one-context-per-human-session constraint is missing';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'operator_console.operator_session_contexts'::regclass
          AND conname = 'fk_operator_session_contexts__device_binding'
    ) OR NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'operator_console.operator_session_contexts'::regclass
          AND conname = 'fk_operator_session_contexts__operator_shift'
    ) THEN
        RAISE EXCEPTION 'canonical device/shift foreign keys are missing';
    END IF;
END $$;
