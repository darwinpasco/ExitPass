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
        FROM information_schema.columns
        WHERE table_schema = 'discounts'
          AND table_name = 'statutory_discount_decision_commands'
          AND column_name IN (
              'business_identity',
              'command_status',
              'decision_result_status',
              'retryable',
              'recovery_classification',
              'vat_exclusive_amount_minor_units',
              'vat_amount_minor_units',
              'processing_started_at',
              'failed_at'
          )
        GROUP BY table_schema, table_name
        HAVING COUNT(*) = 9
    ) THEN
        RAISE EXCEPTION 'decision command table is missing staged decision-v2 columns';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname IN (
            'ck_statutory_discount_decision_commands__semantic_version',
            'ck_statutory_discount_decision_commands__command_status',
            'ck_statutory_discount_decision_commands__decision_result_status',
            'ck_stat_disc_decision_cmds__recovery'
        )
        GROUP BY conrelid
        HAVING COUNT(*) = 4
    ) THEN
        RAISE EXCEPTION 'decision command table is missing staged decision-v2 constraints';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_indexes
        WHERE schemaname = 'discounts'
          AND tablename = 'statutory_discount_decision_commands'
          AND indexname = 'ux_statutory_discount_decision_commands__business_identity_text'
    ) THEN
        RAISE EXCEPTION 'decision command table is missing staged business-identity index';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'discounts'
          AND table_name = 'statutory_discount_payable_basis_application_commands'
          AND column_name IN (
              'statutory_discount_payable_basis_application_command_id',
              'request_reference',
              'statutory_discount_decision_command_id',
              'parking_session_id',
              'site_id',
              'entitlement_type',
              'business_identity',
              'idempotency_scope',
              'idempotency_key',
              'semantic_request_hash',
              'semantic_hash_source_version',
              'command_status',
              'result_classification',
              'retryable',
              'recovery_classification',
              'safe_error_code',
              'statutory_discount_validation_id',
              'statutory_discount_payable_basis_application_id',
              'original_tariff_snapshot_id',
              'target_tariff_snapshot_id',
              'applied_tariff_snapshot_id',
              'applied_policy_reference_id',
              'policy_resolution_basis',
              'approved_discount_amount_minor_units',
              'approved_vat_exclusive_amount_minor_units',
              'approved_vat_amount_minor_units',
              'approved_final_payable_amount_minor_units',
              'currency_code',
              'source_channel',
              'original_correlation_id',
              'created_at',
              'processing_started_at',
              'applied_at',
              'completed_at',
              'failed_at',
              'updated_at'
          )
        GROUP BY table_schema, table_name
        HAVING COUNT(*) = 36
    ) THEN
        RAISE EXCEPTION 'application command table is missing required columns';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname IN (
            'pk_statutory_discount_payable_basis_application_commands',
            'fk_stat_discount_pba_commands__decision_command',
            'fk_stat_discount_pba_commands__parking_session',
            'fk_stat_discount_pba_commands__site',
            'fk_stat_discount_pba_commands__validation',
            'fk_stat_discount_pba_commands__payable_basis_application',
            'fk_stat_discount_pba_commands__original_tariff_snapshot',
            'fk_stat_discount_pba_commands__target_tariff_snapshot',
            'fk_stat_discount_pba_commands__applied_tariff_snapshot',
            'ck_stat_discount_pba_commands__source_channel',
            'ck_stat_discount_pba_commands__entitlement_type',
            'ck_stat_discount_pba_commands__hash',
            'ck_stat_discount_pba_commands__semantic_version',
            'ck_stat_discount_pba_commands__command_status',
            'ck_stat_discount_pba_commands__result_classification',
            'ck_stat_discount_pba_commands__recovery_classification',
            'ck_stat_discount_pba_commands__amounts_non_negative'
        )
        GROUP BY conrelid
        HAVING COUNT(*) = 17
    ) THEN
        RAISE EXCEPTION 'application command table is missing required constraints';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_indexes
        WHERE schemaname = 'discounts'
          AND tablename = 'statutory_discount_payable_basis_application_commands'
          AND indexname IN (
              'ux_stat_discount_pba_commands__business_identity',
              'ux_stat_discount_pba_commands__decision_command',
              'ux_stat_discount_pba_commands__idempotency',
              'ux_stat_discount_pba_commands__request_reference'
          )
        GROUP BY schemaname, tablename
        HAVING COUNT(*) = 4
    ) THEN
        RAISE EXCEPTION 'application command table is missing required unique indexes';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_statutory_discount_decision_commands__semantic_version'
          AND pg_get_constraintdef(oid) LIKE '%statutory-discount-decision:sha256:v1%'
          AND pg_get_constraintdef(oid) LIKE '%statutory-discount-decision:sha256:v2%'
    ) THEN
        RAISE EXCEPTION 'decision semantic source-version constraint does not preserve v1 and allow v2';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_stat_discount_pba_commands__semantic_version'
          AND pg_get_constraintdef(oid) LIKE '%statutory-discount-payable-basis-application:sha256:v1%'
    ) THEN
        RAISE EXCEPTION 'application semantic source-version constraint is missing v1';
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
        RAISE EXCEPTION 'staged statutory-discount command tables introduced unsafe raw evidence or identity columns';
    END IF;
END $$;
