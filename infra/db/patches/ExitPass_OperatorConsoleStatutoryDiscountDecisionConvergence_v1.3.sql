/*
 * ExitPass v1.3 app-local additive SQL patch.
 *
 * Scope:
 * - Persist privacy-minimized Operator Console draft facts needed to reconstruct
 *   canonical statutory-discount decision-v2 semantics during legacy decision convergence.
 *
 * Non-goals:
 * - No payable-basis application convergence.
 * - No payment finality, fiscal issuance, ExitAuthorization, gate, HikCentral, payment-provider,
 *   statutory calculation, VAT, ordinance, WebPay, APT, POS Server, or Operator Console UI change.
 */

ALTER TABLE discounts.statutory_discount_validations
    ADD COLUMN IF NOT EXISTS id_document_type varchar(64) NULL,
    ADD COLUMN IF NOT EXISTS issuing_authority varchar(128) NULL,
    ADD COLUMN IF NOT EXISTS id_expiry_date date NULL,
    ADD COLUMN IF NOT EXISTS masked_id_reference varchar(64) NULL,
    ADD COLUMN IF NOT EXISTS requester_attestation boolean NULL,
    ADD COLUMN IF NOT EXISTS attestation_notes varchar(512) NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_stat_disc_validations__masked_id_reference_safe'
    ) THEN
        ALTER TABLE discounts.statutory_discount_validations
            ADD CONSTRAINT ck_stat_disc_validations__masked_id_reference_safe
            CHECK (
                masked_id_reference IS NULL
                OR masked_id_reference LIKE '%*%'
                OR masked_id_reference ~* '^sha256:[0-9a-f]{64}$'
                OR masked_id_reference !~ '[0-9]{6,}'
            );
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_stat_disc_validations__id_doc_type_supported'
    ) THEN
        ALTER TABLE discounts.statutory_discount_validations
            ADD CONSTRAINT ck_stat_disc_validations__id_doc_type_supported
            CHECK (
                id_document_type IS NULL
                OR id_document_type IN ('SENIOR_CITIZEN_ID', 'PWD_ID', 'OTHER_SUPPORTING_DOCUMENT')
            );
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_stat_disc_validations__decision_v2_fact_presence
    ON discounts.statutory_discount_validations (statutory_discount_validation_id)
    WHERE id_document_type IS NOT NULL
       OR masked_id_reference IS NOT NULL
       OR requester_attestation IS NOT NULL;

COMMENT ON COLUMN discounts.statutory_discount_validations.id_document_type IS
    'Safe document-type metadata used to reconstruct canonical statutory-discount decision-v2 semantics for legacy Operator Console decisions.';

COMMENT ON COLUMN discounts.statutory_discount_validations.issuing_authority IS
    'Safe issuing-authority metadata used for canonical decision-v2 reconstruction. Do not store full identity numbers here.';

COMMENT ON COLUMN discounts.statutory_discount_validations.id_expiry_date IS
    'Optional document expiry date used for canonical decision-v2 reconstruction.';

COMMENT ON COLUMN discounts.statutory_discount_validations.masked_id_reference IS
    'Masked or hashed statutory ID reference only. Raw or full statutory ID values are prohibited.';

COMMENT ON COLUMN discounts.statutory_discount_validations.requester_attestation IS
    'Requester/operator attestation captured at legacy draft time for canonical decision-v2 reconstruction.';

COMMENT ON COLUMN discounts.statutory_discount_validations.attestation_notes IS
    'Safe free-form attestation notes. Must not contain raw evidence payloads or full statutory ID values.';
