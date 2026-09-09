DO $validation$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_fiscal_issuance_references__completion_ancestry'
          AND conrelid = 'core.fiscal_issuance_references'::regclass
    ) THEN
        RAISE EXCEPTION 'Fiscal issuance completion ancestry constraint is missing';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'core'
          AND table_name = 'fiscal_issuance_references'
          AND column_name = 'electronic_journal_event_reference'
    ) THEN
        RAISE EXCEPTION 'Fiscal issuance Electronic Journal readback column is missing';
    END IF;

    IF EXISTS (
        SELECT 1 FROM core.fiscal_issuance_references
        WHERE NOT (
            (completion_basis = 'PAYMENT_FINALITY'
             AND payment_attempt_id IS NOT NULL
             AND payment_confirmation_id IS NOT NULL
             AND completion_authority_reference_id = payment_confirmation_id)
            OR
            (completion_basis = 'ZERO_PAYABLE_STATUTORY_FINALITY'
             AND payment_attempt_id IS NULL
             AND payment_confirmation_id IS NULL
             AND completion_authority_reference_id = statutory_discount_payable_basis_application_command_id)
        )
    ) THEN
        RAISE EXCEPTION 'Fiscal issuance completion ancestry contains invalid rows';
    END IF;
END;
$validation$;

SELECT 'CENTRAL_PMS_ZERO_PAYABLE_FISCAL_COMPLETION_VALID' AS validation_result;
