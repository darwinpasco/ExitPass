DO $$
BEGIN
    IF to_regclass('discounts.statutory_discount_validations') IS NULL THEN
        RAISE EXCEPTION 'missing table discounts.statutory_discount_validations';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'discounts'
          AND table_name = 'statutory_discount_validations'
          AND column_name IN (
              'id_document_type',
              'issuing_authority',
              'id_expiry_date',
              'masked_id_reference',
              'requester_attestation',
              'attestation_notes'
          )
        GROUP BY table_schema, table_name
        HAVING COUNT(*) = 6
    ) THEN
        RAISE EXCEPTION 'discounts.statutory_discount_validations is missing decision convergence columns';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname IN (
            'ck_stat_disc_validations__masked_id_reference_safe',
            'ck_stat_disc_validations__id_doc_type_supported'
        )
        GROUP BY connamespace
        HAVING COUNT(*) = 2
    ) THEN
        RAISE EXCEPTION 'discounts.statutory_discount_validations is missing decision convergence constraints';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_indexes
        WHERE schemaname = 'discounts'
          AND tablename = 'statutory_discount_validations'
          AND indexname = 'ix_stat_disc_validations__decision_v2_fact_presence'
    ) THEN
        RAISE EXCEPTION 'missing index ix_stat_disc_validations__decision_v2_fact_presence';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'discounts'
          AND table_name = 'statutory_discount_validations'
          AND (
              column_name LIKE '%raw%'
              OR column_name LIKE '%image%'
              OR column_name LIKE '%base64%'
              OR column_name LIKE '%unmasked%'
              OR column_name LIKE '%full_id%'
          )
    ) THEN
        RAISE EXCEPTION 'unsafe raw identity or evidence columns were introduced on discounts.statutory_discount_validations';
    END IF;
END $$;
