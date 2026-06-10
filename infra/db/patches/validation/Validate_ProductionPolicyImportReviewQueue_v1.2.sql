/*
 * ExitPass v1.2 validation script.
 *
 * Validates Operator Console production policy import review queue persistence.
 * This script is read-only except for temporary DO-block execution state.
 */

DO $$
DECLARE
    v_missing text[];
BEGIN
    SELECT array_agg(expected.table_name ORDER BY expected.table_name)
    INTO v_missing
    FROM (
        VALUES
            ('production_policy_import_review_submissions'),
            ('production_policy_import_review_decisions'),
            ('production_policy_import_review_history'),
            ('production_policy_import_review_findings')
    ) AS expected(table_name)
    WHERE NOT EXISTS (
        SELECT 1
        FROM information_schema.tables t
        WHERE t.table_schema = 'operator_console'
          AND t.table_name = expected.table_name
    );

    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'missing production policy import review queue tables: %', v_missing;
    END IF;

    SELECT array_agg(expected.column_name ORDER BY expected.column_name)
    INTO v_missing
    FROM (
        VALUES
            ('review_id'),
            ('maker_operator_id'),
            ('file_name'),
            ('submission_fingerprint'),
            ('review_status'),
            ('dry_run_result_json'),
            ('correlation_id'),
            ('created_at'),
            ('updated_at'),
            ('row_version')
    ) AS expected(column_name)
    WHERE NOT EXISTS (
        SELECT 1
        FROM information_schema.columns c
        WHERE c.table_schema = 'operator_console'
          AND c.table_name = 'production_policy_import_review_submissions'
          AND c.column_name = expected.column_name
    );

    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'missing production policy import review submission columns: %', v_missing;
    END IF;

    SELECT array_agg(expected.constraint_name ORDER BY expected.constraint_name)
    INTO v_missing
    FROM (
        VALUES
            ('pk_policy_import_review_submissions'),
            ('ck_policy_import_review_submissions__dry_run_only'),
            ('pk_policy_import_review_decisions'),
            ('fk_policy_import_review_decisions__review'),
            ('uq_policy_import_review_decisions__review_role'),
            ('ck_policy_import_review_decisions__action'),
            ('pk_policy_import_review_history'),
            ('fk_policy_import_review_history__review'),
            ('uq_policy_import_review_history__fingerprint'),
            ('pk_policy_import_review_findings'),
            ('fk_policy_import_review_findings__review'),
            ('uq_policy_import_review_findings__fingerprint')
    ) AS expected(constraint_name)
    WHERE NOT EXISTS (
        SELECT 1
        FROM pg_constraint con
        WHERE con.conname = expected.constraint_name
    );

    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'missing production policy import review queue constraints: %', v_missing;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_indexes i
        WHERE i.schemaname = 'operator_console'
          AND i.indexname = 'ux_policy_import_review_submissions__active_fingerprint'
    ) THEN
        RAISE EXCEPTION 'missing active review idempotency index';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM information_schema.tables
        WHERE table_schema = 'operator_console'
          AND table_name LIKE '%production_policy_import%job%'
    ) THEN
        RAISE EXCEPTION 'unexpected production policy import execution job table in operator_console schema';
    END IF;

    RAISE NOTICE 'Production policy import review queue schema validation passed.';
END;
$$;
