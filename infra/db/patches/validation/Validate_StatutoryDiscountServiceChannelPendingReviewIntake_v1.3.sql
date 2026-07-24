DO $$
DECLARE
    v_unsafe_column_count int;
BEGIN
    IF to_regclass('discounts.statutory_discount_decision_commands') IS NULL THEN
        RAISE EXCEPTION 'missing table discounts.statutory_discount_decision_commands';
    END IF;

    IF to_regclass('discounts.statutory_discount_payable_basis_application_commands') IS NULL THEN
        RAISE EXCEPTION 'missing table discounts.statutory_discount_payable_basis_application_commands';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_statutory_discount_decision_commands__command_status'
          AND pg_get_constraintdef(oid) LIKE '%AWAITING_REVIEW%'
          AND pg_get_constraintdef(oid) LIKE '%COMPLETED%'
          AND pg_get_constraintdef(oid) LIKE '%FAILED_RETRYABLE%'
    ) THEN
        RAISE EXCEPTION 'decision command-status constraint does not allow AWAITING_REVIEW while preserving terminal states';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_stat_disc_decision_cmds__recovery'
          AND pg_get_constraintdef(oid) LIKE '%AWAITING_REVIEW%'
          AND pg_get_constraintdef(oid) LIKE '%RETRY_ORIGINAL_IDEMPOTENCY_KEY%'
          AND pg_get_constraintdef(oid) LIKE '%NOT_RECOVERABLE%'
    ) THEN
        RAISE EXCEPTION 'decision recovery constraint does not allow AWAITING_REVIEW while preserving existing recovery values';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_statutory_discount_decision_commands__result_classification'
          AND pg_get_constraintdef(oid) LIKE '%AWAITING_REVIEW%'
          AND pg_get_constraintdef(oid) LIKE '%ACCEPTED%'
          AND pg_get_constraintdef(oid) LIKE '%IDEMPOTENT_REPLAY%'
    ) THEN
        RAISE EXCEPTION 'decision result-classification constraint does not allow AWAITING_REVIEW while preserving existing values';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_statutory_discount_decision_commands__decision_result_status'
          AND pg_get_constraintdef(oid) LIKE '%NOT_DECIDED%'
          AND pg_get_constraintdef(oid) LIKE '%APPROVED%'
          AND pg_get_constraintdef(oid) LIKE '%REJECTED%'
    ) THEN
        RAISE EXCEPTION 'decision result-status constraint does not preserve NOT_DECIDED, APPROVED, and REJECTED';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_indexes
        WHERE schemaname = 'discounts'
          AND tablename = 'statutory_discount_decision_commands'
          AND indexname = 'ux_statutory_discount_decision_commands__business_identity_text'
    ) THEN
        RAISE EXCEPTION 'decision business-identity uniqueness is missing';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_indexes
        WHERE schemaname = 'discounts'
          AND tablename = 'statutory_discount_payable_basis_application_commands'
          AND indexname = 'ux_stat_discount_pba_commands__decision_command'
    ) THEN
        RAISE EXCEPTION 'application uniqueness by canonical decision is missing';
    END IF;

    SELECT COUNT(*)::int
    INTO v_unsafe_column_count
    FROM information_schema.columns
    WHERE table_schema = 'discounts'
      AND table_name IN (
          'statutory_discount_decision_commands',
          'statutory_discount_payable_basis_application_commands'
      )
      AND (
          column_name LIKE '%raw%'
          OR column_name LIKE '%image%'
          OR column_name LIKE '%base64%'
          OR column_name LIKE '%payload%'
          OR column_name LIKE '%full_id%'
          OR column_name LIKE '%unmasked%'
      );

    IF v_unsafe_column_count <> 0 THEN
        RAISE EXCEPTION 'pending-review intake introduced unsafe raw evidence or identity columns';
    END IF;
END $$;
