/* ExitPass v1.3 completion ancestry for Central PMS fiscal issuance references. */

ALTER TABLE core.fiscal_issuance_references
    ADD COLUMN IF NOT EXISTS completion_basis varchar(64),
    ADD COLUMN IF NOT EXISTS completion_authority_reference_id uuid,
    ADD COLUMN IF NOT EXISTS statutory_discount_decision_command_id uuid,
    ADD COLUMN IF NOT EXISTS statutory_discount_payable_basis_application_command_id uuid,
    ADD COLUMN IF NOT EXISTS statutory_discount_validation_id uuid,
    ADD COLUMN IF NOT EXISTS applied_policy_reference_id uuid,
    ADD COLUMN IF NOT EXISTS electronic_journal_event_reference varchar(192);

UPDATE core.fiscal_issuance_references
SET completion_basis = 'PAYMENT_FINALITY',
    completion_authority_reference_id = payment_confirmation_id
WHERE completion_basis IS NULL;

ALTER TABLE core.fiscal_issuance_references
    ALTER COLUMN payment_confirmation_id DROP NOT NULL,
    ALTER COLUMN payment_attempt_id DROP NOT NULL,
    ALTER COLUMN completion_basis SET NOT NULL,
    ALTER COLUMN completion_authority_reference_id SET NOT NULL;

DO $migration$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_fiscal_issuance_references__statutory_decision'
          AND conrelid = 'core.fiscal_issuance_references'::regclass
    ) THEN
        ALTER TABLE core.fiscal_issuance_references
            ADD CONSTRAINT fk_fiscal_issuance_references__statutory_decision
            FOREIGN KEY (statutory_discount_decision_command_id)
            REFERENCES discounts.statutory_discount_decision_commands(statutory_discount_decision_command_id);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_fiscal_issuance_references__statutory_application'
          AND conrelid = 'core.fiscal_issuance_references'::regclass
    ) THEN
        ALTER TABLE core.fiscal_issuance_references
            ADD CONSTRAINT fk_fiscal_issuance_references__statutory_application
            FOREIGN KEY (statutory_discount_payable_basis_application_command_id)
            REFERENCES discounts.statutory_discount_payable_basis_application_commands(
                statutory_discount_payable_basis_application_command_id);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_fiscal_issuance_references__statutory_validation'
          AND conrelid = 'core.fiscal_issuance_references'::regclass
    ) THEN
        ALTER TABLE core.fiscal_issuance_references
            ADD CONSTRAINT fk_fiscal_issuance_references__statutory_validation
            FOREIGN KEY (statutory_discount_validation_id)
            REFERENCES discounts.statutory_discount_validations(statutory_discount_validation_id);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_fiscal_issuance_references__applied_policy'
          AND conrelid = 'core.fiscal_issuance_references'::regclass
    ) THEN
        ALTER TABLE core.fiscal_issuance_references
            ADD CONSTRAINT fk_fiscal_issuance_references__applied_policy
            FOREIGN KEY (applied_policy_reference_id)
            REFERENCES discounts.discount_policy_references(discount_policy_reference_id);
    END IF;
END;
$migration$;

ALTER TABLE core.fiscal_issuance_references
    DROP CONSTRAINT IF EXISTS ck_fiscal_issuance_references__completion_ancestry;

ALTER TABLE core.fiscal_issuance_references
    ADD CONSTRAINT ck_fiscal_issuance_references__completion_ancestry CHECK (
        (
            completion_basis = 'PAYMENT_FINALITY'
            AND payment_attempt_id IS NOT NULL
            AND payment_confirmation_id IS NOT NULL
            AND completion_authority_reference_id = payment_confirmation_id
            AND statutory_discount_decision_command_id IS NULL
            AND statutory_discount_payable_basis_application_command_id IS NULL
            AND statutory_discount_validation_id IS NULL
            AND applied_policy_reference_id IS NULL
        ) OR (
            completion_basis = 'ZERO_PAYABLE_STATUTORY_FINALITY'
            AND payment_attempt_id IS NULL
            AND payment_confirmation_id IS NULL
            AND completion_authority_reference_id = statutory_discount_payable_basis_application_command_id
            AND statutory_discount_decision_command_id IS NOT NULL
            AND statutory_discount_payable_basis_application_command_id IS NOT NULL
            AND statutory_discount_validation_id IS NOT NULL
            AND applied_policy_reference_id IS NOT NULL
        )
    );

CREATE UNIQUE INDEX IF NOT EXISTS ux_fiscal_issuance_references__active_statutory_application
    ON core.fiscal_issuance_references (statutory_discount_payable_basis_application_command_id)
    WHERE is_active AND NOT is_superseded
      AND statutory_discount_payable_basis_application_command_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_fiscal_issuance_references__completion_basis
    ON core.fiscal_issuance_references (completion_basis);

COMMENT ON COLUMN core.fiscal_issuance_references.completion_basis IS
    'Canonical completion basis: PAYMENT_FINALITY or ZERO_PAYABLE_STATUTORY_FINALITY.';
COMMENT ON COLUMN core.fiscal_issuance_references.completion_authority_reference_id IS
    'Durable Central PMS source establishing transaction completion.';
