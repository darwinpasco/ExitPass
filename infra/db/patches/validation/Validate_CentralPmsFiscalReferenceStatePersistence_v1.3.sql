-- Validation for ExitPass v1.3 Central PMS fiscal reference state persistence scaffold.

DO $$
DECLARE
    v_missing text[];
BEGIN
    SELECT array_agg(required_name)
    INTO v_missing
    FROM (
        VALUES
            ('core.fiscal_issuance_references'),
            ('core.fiscal_issuance_attempt_history'),
            ('core.fiscal_issuance_exception_reviews'),
            ('core.fiscal_issuance_readback_reconciliations'),
            ('core.fiscal_issuance_retry_command_preparations')
    ) AS required(required_name)
    WHERE to_regclass(required_name) IS NULL;

    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'Missing Central PMS fiscal reference state tables: %', array_to_string(v_missing, ', ');
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'core'
          AND table_name = 'fiscal_issuance_references'
          AND column_name = 'upstream_finality_reference'
    ) THEN
        RAISE EXCEPTION 'Missing upstream_finality_reference on core.fiscal_issuance_references.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'core'
          AND table_name = 'fiscal_issuance_references'
          AND column_name = 'fiscal_issuance_state'
    ) THEN
        RAISE EXCEPTION 'Missing fiscal_issuance_state on core.fiscal_issuance_references.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_indexes
        WHERE schemaname = 'core'
          AND tablename = 'fiscal_issuance_references'
          AND indexname = 'ux_fiscal_issuance_references__active_payment_confirmation'
    ) THEN
        RAISE EXCEPTION 'Missing active payment confirmation uniqueness guard.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_indexes
        WHERE schemaname = 'core'
          AND tablename = 'fiscal_issuance_references'
          AND indexname = 'ux_fiscal_issuance_references__active_idempotency_scope'
    ) THEN
        RAISE EXCEPTION 'Missing active fiscal issuance idempotency-scope uniqueness guard.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'core'
          AND table_name IN (
              'fiscal_issuance_references',
              'fiscal_issuance_attempt_history',
              'fiscal_issuance_exception_reviews',
              'fiscal_issuance_readback_reconciliations',
              'fiscal_issuance_retry_command_preparations'
          )
          AND (
              column_name ILIKE '%raw_payload%'
              OR column_name ILIKE '%callback_payload%'
              OR column_name ILIKE '%pan%'
              OR column_name ILIKE '%cvv%'
              OR column_name ILIKE '%secret%'
              OR column_name ILIKE '%token%'
          )
    ) THEN
        RAISE EXCEPTION 'Fiscal reference state scaffold contains a prohibited sensitive-payload column.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_indexes
        WHERE schemaname = 'core'
          AND tablename = 'fiscal_issuance_retry_command_preparations'
          AND indexname = 'ix_fiscal_issuance_retry_command_preparations__reference_attempted'
    ) THEN
        RAISE EXCEPTION 'Missing retry command preparation reference audit index.';
    END IF;
END $$;
