DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'core.fiscal_issuance_references'::regclass
          AND conname = 'ck_fiscal_refs__invoice_customer_snapshot_state'
          AND convalidated
          AND pg_get_constraintdef(oid) LIKE '%invoice_customer_name IS NOT NULL%'
          AND pg_get_constraintdef(oid) LIKE '%invoice_customer_address IS NULL%'
          AND pg_get_constraintdef(oid) LIKE '%invoice_customer_tin IS NULL%'
          AND pg_get_constraintdef(oid) LIKE '%invoice_business_style IS NULL%'
    ) THEN
        RAISE EXCEPTION 'Statutory beneficiary invoice-customer snapshot constraint is missing or invalid.';
    END IF;
END;
$$;
