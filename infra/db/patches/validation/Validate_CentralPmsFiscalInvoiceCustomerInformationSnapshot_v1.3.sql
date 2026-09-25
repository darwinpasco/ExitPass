DO $$
DECLARE
    required_columns integer;
BEGIN
    SELECT count(*) INTO required_columns
    FROM information_schema.columns
    WHERE table_schema = 'core'
      AND table_name = 'fiscal_issuance_references'
      AND (
          (column_name = 'invoice_customer_name' AND data_type = 'character varying' AND character_maximum_length = 160)
          OR (column_name = 'invoice_customer_address' AND data_type = 'character varying' AND character_maximum_length = 300)
          OR (column_name = 'invoice_customer_tin' AND data_type = 'character varying' AND character_maximum_length = 40)
          OR (column_name = 'invoice_business_style' AND data_type = 'character varying' AND character_maximum_length = 160)
          OR (column_name = 'invoice_customer_information_row_version' AND data_type = 'bigint')
          OR (column_name = 'invoice_customer_information_snapshot_captured_at' AND data_type = 'timestamp with time zone')
      );

    IF required_columns <> 6 THEN
        RAISE EXCEPTION 'fiscal invoice customer-information snapshot columns are missing or invalid';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'core.fiscal_issuance_references'::regclass
          AND conname = 'ck_fiscal_refs__invoice_customer_fields_normalized'
    ) OR NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'core.fiscal_issuance_references'::regclass
          AND conname = 'ck_fiscal_refs__invoice_customer_snapshot_state'
    ) THEN
        RAISE EXCEPTION 'fiscal invoice customer-information snapshot constraints are missing';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM core.fiscal_issuance_references
        WHERE invoice_customer_information_snapshot_captured_at IS NULL
          AND (invoice_customer_information_row_version IS NOT NULL
               OR invoice_customer_name IS NOT NULL
               OR invoice_customer_address IS NOT NULL
               OR invoice_customer_tin IS NOT NULL
               OR invoice_business_style IS NOT NULL)
    ) THEN
        RAISE EXCEPTION 'legacy fiscal reference contains snapshot values without a capture boundary';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM core.fiscal_issuance_references
        WHERE invoice_customer_information_snapshot_captured_at IS NOT NULL
          AND invoice_customer_information_row_version IS NULL
          AND (invoice_customer_name IS NOT NULL
               OR invoice_customer_address IS NOT NULL
               OR invoice_customer_tin IS NOT NULL
               OR invoice_business_style IS NOT NULL)
    ) THEN
        RAISE EXCEPTION 'captured fiscal customer-information values lack an authoritative row version';
    END IF;
END;
$$;

SELECT 'PASS' AS fiscal_invoice_customer_information_snapshot_validation;
