DO $validation$
DECLARE
    v_missing_columns integer;
    v_invalid_rows integer;
BEGIN
    SELECT COUNT(*)
    INTO v_missing_columns
    FROM (VALUES
        ('tariff_snapshot_id'),
        ('completion_basis'),
        ('statutory_discount_decision_command_id'),
        ('statutory_discount_payable_basis_application_command_id'),
        ('statutory_discount_validation_id'),
        ('applied_policy_reference_id')
    ) AS expected(column_name)
    WHERE NOT EXISTS (
        SELECT 1
        FROM information_schema.columns AS actual
        WHERE actual.table_schema = 'core'
          AND actual.table_name = 'exit_authorizations'
          AND actual.column_name = expected.column_name
    );

    IF v_missing_columns <> 0 THEN
        RAISE EXCEPTION 'ExitAuthorization completion authority is missing % required columns',
            v_missing_columns;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_exit_authorizations__completion_ancestry'
          AND conrelid = 'core.exit_authorizations'::regclass
    ) THEN
        RAISE EXCEPTION 'ExitAuthorization completion ancestry check constraint is missing';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_trigger
        WHERE tgname = 'trg_exit_authorizations_completion_ancestry'
          AND tgrelid = 'core.exit_authorizations'::regclass
          AND NOT tgisinternal
    ) THEN
        RAISE EXCEPTION 'ExitAuthorization completion ancestry trigger is missing';
    END IF;

    SELECT COUNT(*)
    INTO v_invalid_rows
    FROM core.exit_authorizations
    WHERE tariff_snapshot_id IS NULL
       OR completion_basis IS NULL
       OR (
            completion_basis = 'PAYMENT_FINALITY' AND
            (payment_attempt_id IS NULL OR payment_confirmation_id IS NULL)
       )
       OR (
            completion_basis = 'ZERO_PAYABLE_STATUTORY_FINALITY' AND
            (payment_attempt_id IS NOT NULL OR payment_confirmation_id IS NOT NULL)
       );

    IF v_invalid_rows <> 0 THEN
        RAISE EXCEPTION 'ExitAuthorization completion ancestry has % invalid rows', v_invalid_rows;
    END IF;
END;
$validation$;

SELECT 'EXIT_AUTHORIZATION_COMPLETION_AUTHORITY_VALID' AS validation_result;
