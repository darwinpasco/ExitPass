DO $$
BEGIN
    IF to_regclass('discounts.statutory_discount_decision_commands') IS NULL THEN
        RAISE EXCEPTION 'missing table discounts.statutory_discount_decision_commands';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'discounts'
          AND table_name = 'statutory_discount_decision_commands'
          AND column_name IN (
              'statutory_discount_decision_command_id',
              'request_reference',
              'parking_session_id',
              'source_channel',
              'entitlement_type',
              'idempotency_scope',
              'idempotency_key',
              'semantic_request_hash',
              'semantic_hash_source_version',
              'statutory_discount_validation_id',
              'payable_basis_application_id',
              'original_tariff_snapshot_id',
              'applied_tariff_snapshot_id',
              'decision_status',
              'result_classification',
              'original_correlation_id'
          )
        GROUP BY table_schema, table_name
        HAVING COUNT(*) = 16
    ) THEN
        RAISE EXCEPTION 'discounts.statutory_discount_decision_commands is missing required columns';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_indexes
        WHERE schemaname = 'discounts'
          AND tablename = 'statutory_discount_decision_commands'
          AND indexname = 'ux_statutory_discount_decision_commands__idempotency'
    ) THEN
        RAISE EXCEPTION 'missing idempotency unique index';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_indexes
        WHERE schemaname = 'discounts'
          AND tablename = 'statutory_discount_decision_commands'
          AND indexname = 'ux_statutory_discount_decision_commands__business_identity'
    ) THEN
        RAISE EXCEPTION 'missing business identity unique index';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_indexes
        WHERE schemaname = 'discounts'
          AND tablename = 'statutory_discount_decision_commands'
          AND indexname = 'ux_statutory_discount_decision_commands__request_reference'
    ) THEN
        RAISE EXCEPTION 'missing request reference unique index';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname IN (
            'fk_statutory_discount_decision_commands__parking_session',
            'fk_statutory_discount_decision_commands__validation',
            'fk_statutory_discount_decision_commands__payable_basis_application',
            'fk_statutory_discount_decision_commands__original_tariff_snapshot',
            'fk_statutory_discount_decision_commands__applied_tariff_snapshot',
            'ck_statutory_discount_decision_commands__source_channel',
            'ck_statutory_discount_decision_commands__entitlement_type',
            'ck_statutory_discount_decision_commands__hash',
            'ck_statutory_discount_decision_commands__semantic_version',
            'ck_statutory_discount_decision_commands__decision_status',
            'ck_statutory_discount_decision_commands__result_classification'
        )
        GROUP BY conrelid
        HAVING COUNT(*) = 11
    ) THEN
        RAISE EXCEPTION 'discounts.statutory_discount_decision_commands is missing required constraints';
    END IF;
END $$;
