-- ExitPass v1.3 immutable Sales Invoice customer-information fiscal snapshot.

ALTER TABLE core.fiscal_issuance_references
    ADD COLUMN IF NOT EXISTS invoice_customer_name varchar(160),
    ADD COLUMN IF NOT EXISTS invoice_customer_address varchar(300),
    ADD COLUMN IF NOT EXISTS invoice_customer_tin varchar(40),
    ADD COLUMN IF NOT EXISTS invoice_business_style varchar(160),
    ADD COLUMN IF NOT EXISTS invoice_customer_information_row_version bigint,
    ADD COLUMN IF NOT EXISTS invoice_customer_information_snapshot_captured_at timestamptz;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'core.fiscal_issuance_references'::regclass
          AND conname = 'ck_fiscal_refs__invoice_customer_fields_normalized'
    ) THEN
        ALTER TABLE core.fiscal_issuance_references
            ADD CONSTRAINT ck_fiscal_refs__invoice_customer_fields_normalized CHECK (
                (invoice_customer_name IS NULL OR (
                    char_length(invoice_customer_name) BETWEEN 1 AND 160
                    AND invoice_customer_name = btrim(invoice_customer_name)))
                AND (invoice_customer_address IS NULL OR (
                    char_length(invoice_customer_address) BETWEEN 1 AND 300
                    AND invoice_customer_address = btrim(invoice_customer_address)))
                AND (invoice_customer_tin IS NULL OR (
                    char_length(invoice_customer_tin) BETWEEN 1 AND 40
                    AND invoice_customer_tin = btrim(invoice_customer_tin)))
                AND (invoice_business_style IS NULL OR (
                    char_length(invoice_business_style) BETWEEN 1 AND 160
                    AND invoice_business_style = btrim(invoice_business_style)))
            );
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'core.fiscal_issuance_references'::regclass
          AND conname = 'ck_fiscal_refs__invoice_customer_snapshot_state'
    ) THEN
        ALTER TABLE core.fiscal_issuance_references
            ADD CONSTRAINT ck_fiscal_refs__invoice_customer_snapshot_state CHECK (
                (
                    invoice_customer_information_snapshot_captured_at IS NULL
                    AND invoice_customer_information_row_version IS NULL
                    AND invoice_customer_name IS NULL
                    AND invoice_customer_address IS NULL
                    AND invoice_customer_tin IS NULL
                    AND invoice_business_style IS NULL
                )
                OR
                (
                    invoice_customer_information_snapshot_captured_at IS NOT NULL
                    AND (
                        (
                            invoice_customer_information_row_version IS NULL
                            AND invoice_customer_name IS NULL
                            AND invoice_customer_address IS NULL
                            AND invoice_customer_tin IS NULL
                            AND invoice_business_style IS NULL
                        )
                        OR
                        (
                            invoice_customer_information_row_version > 0
                            AND (
                                invoice_customer_name IS NOT NULL
                                OR invoice_customer_address IS NOT NULL
                                OR invoice_customer_tin IS NOT NULL
                                OR invoice_business_style IS NOT NULL
                            )
                        )
                    )
                )
            );
    END IF;
END;
$$;

COMMENT ON COLUMN core.fiscal_issuance_references.invoice_customer_information_snapshot_captured_at IS
    'Non-null when the immutable parking-session Sales Invoice customer-information basis, including an intentional empty basis, was captured.';
COMMENT ON COLUMN core.fiscal_issuance_references.invoice_customer_information_row_version IS
    'Authoritative parking-session customer-information row version captured for this fiscal issuance; null when the captured basis was empty.';
