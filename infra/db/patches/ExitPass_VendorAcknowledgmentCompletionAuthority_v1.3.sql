/*
 * ExitPass v1.3 vendor acknowledgment completion ancestry.
 *
 * Existing payment-finality rows are preserved. Zero-payable statutory finality may
 * now acknowledge the completed parking charge without creating payment evidence.
 */

BEGIN;

ALTER TABLE integration.vendor_payment_acknowledgments
    ADD COLUMN IF NOT EXISTS completion_basis varchar(64),
    ADD COLUMN IF NOT EXISTS completion_authority_reference_id uuid;

UPDATE integration.vendor_payment_acknowledgments
SET completion_basis = 'PAYMENT_FINALITY',
    completion_authority_reference_id = payment_confirmation_id
WHERE completion_basis IS NULL
   OR completion_authority_reference_id IS NULL;

DO $migration$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM integration.vendor_payment_acknowledgments
        WHERE payment_attempt_id IS NULL
           OR payment_confirmation_id IS NULL
           OR completion_authority_reference_id IS DISTINCT FROM payment_confirmation_id
    ) THEN
        RAISE EXCEPTION 'Existing vendor acknowledgment completion ancestry is incomplete';
    END IF;
END;
$migration$;

ALTER TABLE integration.vendor_payment_acknowledgments
    ALTER COLUMN payment_attempt_id DROP NOT NULL,
    ALTER COLUMN payment_confirmation_id DROP NOT NULL,
    ALTER COLUMN completion_basis SET NOT NULL,
    ALTER COLUMN completion_authority_reference_id SET NOT NULL,
    DROP CONSTRAINT IF EXISTS ck_vendor_payment_ack__completion_ancestry,
    ADD CONSTRAINT ck_vendor_payment_ack__completion_ancestry CHECK (
        (
            completion_basis = 'PAYMENT_FINALITY'
            AND payment_attempt_id IS NOT NULL
            AND payment_confirmation_id IS NOT NULL
            AND completion_authority_reference_id = payment_confirmation_id
        )
        OR
        (
            completion_basis = 'ZERO_PAYABLE_STATUTORY_FINALITY'
            AND payment_attempt_id IS NULL
            AND payment_confirmation_id IS NULL
        )
    );

CREATE UNIQUE INDEX IF NOT EXISTS uq_vendor_payment_ack__completion_authority_vendor
    ON integration.vendor_payment_acknowledgments (
        completion_authority_reference_id,
        vendor_system_code);

COMMIT;
