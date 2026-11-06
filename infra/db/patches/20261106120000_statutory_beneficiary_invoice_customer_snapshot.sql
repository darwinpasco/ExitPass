BEGIN;

ALTER TABLE core.fiscal_issuance_references
    DROP CONSTRAINT IF EXISTS ck_fiscal_refs__invoice_customer_snapshot_state;

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
                    AND (
                        (
                            invoice_customer_name IS NULL
                            AND invoice_customer_address IS NULL
                            AND invoice_customer_tin IS NULL
                            AND invoice_business_style IS NULL
                        )
                        OR
                        (
                            invoice_customer_name IS NOT NULL
                            AND invoice_customer_address IS NULL
                            AND invoice_customer_tin IS NULL
                            AND invoice_business_style IS NULL
                        )
                    )
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
    ) NOT VALID;

ALTER TABLE core.fiscal_issuance_references
    VALIDATE CONSTRAINT ck_fiscal_refs__invoice_customer_snapshot_state;

COMMIT;
