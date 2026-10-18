DO $validation$
DECLARE
    v_invalid_rows integer;
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'integration'
          AND table_name = 'vendor_payment_acknowledgments'
          AND column_name = 'completion_basis'
          AND is_nullable = 'NO'
    ) OR NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'integration'
          AND table_name = 'vendor_payment_acknowledgments'
          AND column_name = 'completion_authority_reference_id'
          AND is_nullable = 'NO'
    ) THEN
        RAISE EXCEPTION 'Vendor acknowledgment completion authority columns are missing or nullable';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_vendor_payment_ack__completion_ancestry'
          AND conrelid = 'integration.vendor_payment_acknowledgments'::regclass
    ) OR to_regclass('integration.uq_vendor_payment_ack__completion_authority_vendor') IS NULL THEN
        RAISE EXCEPTION 'Vendor acknowledgment completion ancestry constraint or unique index is missing';
    END IF;

    SELECT COUNT(*)
    INTO v_invalid_rows
    FROM integration.vendor_payment_acknowledgments
    WHERE completion_basis NOT IN ('PAYMENT_FINALITY', 'ZERO_PAYABLE_STATUTORY_FINALITY')
       OR (
            completion_basis = 'PAYMENT_FINALITY'
            AND (
                payment_attempt_id IS NULL
                OR payment_confirmation_id IS NULL
                OR completion_authority_reference_id IS DISTINCT FROM payment_confirmation_id
            )
       )
       OR (
            completion_basis = 'ZERO_PAYABLE_STATUTORY_FINALITY'
            AND (payment_attempt_id IS NOT NULL OR payment_confirmation_id IS NOT NULL)
       );

    IF v_invalid_rows <> 0 THEN
        RAISE EXCEPTION 'Vendor acknowledgment completion ancestry has % invalid rows', v_invalid_rows;
    END IF;
END;
$validation$;

SELECT 'VENDOR_ACKNOWLEDGMENT_COMPLETION_AUTHORITY_VALID' AS validation_result;
