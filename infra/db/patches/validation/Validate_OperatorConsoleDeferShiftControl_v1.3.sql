DO $$
BEGIN
    IF to_regclass('operator_console.operator_session_contexts') IS NULL THEN
        RAISE EXCEPTION 'operator_console.operator_session_contexts is missing';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM pg_attribute
        WHERE attrelid = 'operator_console.operator_session_contexts'::regclass
          AND attname = 'operator_shift_id'
          AND attnotnull
          AND NOT attisdropped
    ) THEN
        RAISE EXCEPTION 'operator_shift_id remains NOT NULL';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'operator_console.operator_session_contexts'::regclass
          AND conname = 'fk_operator_session_contexts__operator_shift'
          AND contype = 'f'
    ) THEN
        RAISE EXCEPTION 'historical operator shift foreign key is missing';
    END IF;
END $$;
