/*
 * ExitPass v1.3 app-local durable SQL patch.
 *
 * Scope:
 * - Link service-channel Operator Console review rows to the statutory validation
 *   row created during actual Operator Console approval.
 *
 * Non-goals:
 * - No new decision table.
 * - No new application table.
 * - No payable-basis mutation routine change.
 * - No payment finality, fiscal issuance, ExitAuthorization, gate behavior,
 *   statutory calculation, VAT, WebPay, APT, POS Server, or Operator Console UI change.
 */

BEGIN;

ALTER TABLE operator_console.statutory_discount_service_channel_reviews
    ADD COLUMN IF NOT EXISTS statutory_discount_validation_id uuid NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_stat_disc_svc_reviews__validation'
    ) THEN
        ALTER TABLE operator_console.statutory_discount_service_channel_reviews
            ADD CONSTRAINT fk_stat_disc_svc_reviews__validation
            FOREIGN KEY (statutory_discount_validation_id)
            REFERENCES discounts.statutory_discount_validations(statutory_discount_validation_id);
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_stat_disc_svc_reviews__validation
    ON operator_console.statutory_discount_service_channel_reviews (statutory_discount_validation_id)
    WHERE statutory_discount_validation_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_stat_disc_svc_reviews__decision_validation
    ON operator_console.statutory_discount_service_channel_reviews (
        statutory_discount_decision_command_id,
        statutory_discount_validation_id
    )
    WHERE statutory_discount_validation_id IS NOT NULL;

COMMENT ON COLUMN operator_console.statutory_discount_service_channel_reviews.statutory_discount_validation_id IS
    'Approved discounts.statutory_discount_validations row created during actual Operator Console review completion. Null while awaiting review or rejected without payable-basis authority.';

COMMIT;
