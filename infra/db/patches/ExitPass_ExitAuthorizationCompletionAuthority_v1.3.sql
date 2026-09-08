/*
 * ExitPass v1.3 completion-authority ancestry for ExitAuthorization.
 *
 * Completion is derived from canonical financial/statutory records. This patch does not
 * create payment or statutory finality; it records which durable basis an authorization used.
 */

ALTER TABLE core.exit_authorizations
    ADD COLUMN IF NOT EXISTS tariff_snapshot_id uuid,
    ADD COLUMN IF NOT EXISTS completion_basis varchar(64),
    ADD COLUMN IF NOT EXISTS statutory_discount_decision_command_id uuid,
    ADD COLUMN IF NOT EXISTS statutory_discount_payable_basis_application_command_id uuid,
    ADD COLUMN IF NOT EXISTS statutory_discount_validation_id uuid,
    ADD COLUMN IF NOT EXISTS applied_policy_reference_id uuid;

UPDATE core.exit_authorizations AS exit_auth
SET tariff_snapshot_id = attempt.tariff_snapshot_id,
    completion_basis = 'PAYMENT_FINALITY'
FROM core.payment_attempts AS attempt
WHERE attempt.payment_attempt_id = exit_auth.payment_attempt_id
  AND (exit_auth.tariff_snapshot_id IS NULL OR exit_auth.completion_basis IS NULL);

ALTER TABLE core.exit_authorizations
    ALTER COLUMN payment_attempt_id DROP NOT NULL,
    ALTER COLUMN payment_confirmation_id DROP NOT NULL,
    ALTER COLUMN tariff_snapshot_id SET NOT NULL,
    ALTER COLUMN completion_basis SET NOT NULL;

DO $migration$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_exit_authorizations__tariff_snapshot_id'
          AND conrelid = 'core.exit_authorizations'::regclass
    ) THEN
        ALTER TABLE core.exit_authorizations
            ADD CONSTRAINT fk_exit_authorizations__tariff_snapshot_id
            FOREIGN KEY (tariff_snapshot_id)
            REFERENCES core.tariff_snapshots(tariff_snapshot_id)
            DEFERRABLE INITIALLY IMMEDIATE;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_exit_authorizations__statutory_decision_command_id'
          AND conrelid = 'core.exit_authorizations'::regclass
    ) THEN
        ALTER TABLE core.exit_authorizations
            ADD CONSTRAINT fk_exit_authorizations__statutory_decision_command_id
            FOREIGN KEY (statutory_discount_decision_command_id)
            REFERENCES discounts.statutory_discount_decision_commands(statutory_discount_decision_command_id)
            DEFERRABLE INITIALLY IMMEDIATE;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_exit_authorizations__statutory_application_command_id'
          AND conrelid = 'core.exit_authorizations'::regclass
    ) THEN
        ALTER TABLE core.exit_authorizations
            ADD CONSTRAINT fk_exit_authorizations__statutory_application_command_id
            FOREIGN KEY (statutory_discount_payable_basis_application_command_id)
            REFERENCES discounts.statutory_discount_payable_basis_application_commands(
                statutory_discount_payable_basis_application_command_id)
            DEFERRABLE INITIALLY IMMEDIATE;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_exit_authorizations__statutory_validation_id'
          AND conrelid = 'core.exit_authorizations'::regclass
    ) THEN
        ALTER TABLE core.exit_authorizations
            ADD CONSTRAINT fk_exit_authorizations__statutory_validation_id
            FOREIGN KEY (statutory_discount_validation_id)
            REFERENCES discounts.statutory_discount_validations(statutory_discount_validation_id)
            DEFERRABLE INITIALLY IMMEDIATE;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_exit_authorizations__applied_policy_reference_id'
          AND conrelid = 'core.exit_authorizations'::regclass
    ) THEN
        ALTER TABLE core.exit_authorizations
            ADD CONSTRAINT fk_exit_authorizations__applied_policy_reference_id
            FOREIGN KEY (applied_policy_reference_id)
            REFERENCES discounts.discount_policy_references(discount_policy_reference_id)
            DEFERRABLE INITIALLY IMMEDIATE;
    END IF;
END;
$migration$;

CREATE OR REPLACE FUNCTION core.enforce_exit_authorization_completion_ancestry()
RETURNS trigger
LANGUAGE plpgsql
AS $function$
DECLARE
    v_attempt_tariff_snapshot_id uuid;
    v_confirmation_attempt_id uuid;
BEGIN
    IF NEW.completion_basis IS NULL AND
       NEW.payment_attempt_id IS NOT NULL AND
       NEW.payment_confirmation_id IS NOT NULL THEN
        NEW.completion_basis := 'PAYMENT_FINALITY';
    END IF;

    IF NEW.completion_basis = 'PAYMENT_FINALITY' THEN
        IF NEW.payment_attempt_id IS NULL OR NEW.payment_confirmation_id IS NULL OR
           NEW.statutory_discount_decision_command_id IS NOT NULL OR
           NEW.statutory_discount_payable_basis_application_command_id IS NOT NULL OR
           NEW.statutory_discount_validation_id IS NOT NULL OR
           NEW.applied_policy_reference_id IS NOT NULL THEN
            RAISE EXCEPTION 'PAYMENT_FINALITY exit authorization has invalid completion ancestry'
                USING ERRCODE = '23514';
        END IF;

        SELECT tariff_snapshot_id
        INTO v_attempt_tariff_snapshot_id
        FROM core.payment_attempts
        WHERE payment_attempt_id = NEW.payment_attempt_id;

        SELECT payment_attempt_id
        INTO v_confirmation_attempt_id
        FROM core.payment_confirmations
        WHERE payment_confirmation_id = NEW.payment_confirmation_id
          AND confirmation_status = 'RECORDED';

        IF v_attempt_tariff_snapshot_id IS NULL OR
           v_confirmation_attempt_id IS DISTINCT FROM NEW.payment_attempt_id OR
           (NEW.tariff_snapshot_id IS NOT NULL AND
            NEW.tariff_snapshot_id IS DISTINCT FROM v_attempt_tariff_snapshot_id) THEN
            RAISE EXCEPTION 'PAYMENT_FINALITY exit authorization ancestry does not reconcile'
                USING ERRCODE = '23514';
        END IF;

        NEW.tariff_snapshot_id := v_attempt_tariff_snapshot_id;
    ELSIF NEW.completion_basis = 'ZERO_PAYABLE_STATUTORY_FINALITY' THEN
        IF NEW.payment_attempt_id IS NOT NULL OR NEW.payment_confirmation_id IS NOT NULL OR
           NEW.tariff_snapshot_id IS NULL OR
           NEW.statutory_discount_decision_command_id IS NULL OR
           NEW.statutory_discount_payable_basis_application_command_id IS NULL OR
           NEW.statutory_discount_validation_id IS NULL OR
           NEW.applied_policy_reference_id IS NULL THEN
            RAISE EXCEPTION 'ZERO_PAYABLE_STATUTORY_FINALITY exit authorization has invalid completion ancestry'
                USING ERRCODE = '23514';
        END IF;
    ELSE
        RAISE EXCEPTION 'unsupported ExitAuthorization completion basis: %', NEW.completion_basis
            USING ERRCODE = '23514';
    END IF;

    RETURN NEW;
END;
$function$;

DROP TRIGGER IF EXISTS trg_exit_authorizations_completion_ancestry
    ON core.exit_authorizations;

CREATE TRIGGER trg_exit_authorizations_completion_ancestry
BEFORE INSERT OR UPDATE OF
    tariff_snapshot_id,
    completion_basis,
    payment_attempt_id,
    payment_confirmation_id,
    statutory_discount_decision_command_id,
    statutory_discount_payable_basis_application_command_id,
    statutory_discount_validation_id,
    applied_policy_reference_id
ON core.exit_authorizations
FOR EACH ROW
EXECUTE FUNCTION core.enforce_exit_authorization_completion_ancestry();

ALTER TABLE core.exit_authorizations
    DROP CONSTRAINT IF EXISTS ck_exit_authorizations__completion_ancestry;

ALTER TABLE core.exit_authorizations
    ADD CONSTRAINT ck_exit_authorizations__completion_ancestry
    CHECK (
        (
            completion_basis = 'PAYMENT_FINALITY' AND
            payment_attempt_id IS NOT NULL AND
            payment_confirmation_id IS NOT NULL AND
            statutory_discount_decision_command_id IS NULL AND
            statutory_discount_payable_basis_application_command_id IS NULL AND
            statutory_discount_validation_id IS NULL AND
            applied_policy_reference_id IS NULL
        ) OR
        (
            completion_basis = 'ZERO_PAYABLE_STATUTORY_FINALITY' AND
            payment_attempt_id IS NULL AND
            payment_confirmation_id IS NULL AND
            statutory_discount_decision_command_id IS NOT NULL AND
            statutory_discount_payable_basis_application_command_id IS NOT NULL AND
            statutory_discount_validation_id IS NOT NULL AND
            applied_policy_reference_id IS NOT NULL
        )
    );

CREATE UNIQUE INDEX IF NOT EXISTS ux_exit_authorizations__statutory_application_command
    ON core.exit_authorizations (statutory_discount_payable_basis_application_command_id)
    WHERE statutory_discount_payable_basis_application_command_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_exit_authorizations__tariff_snapshot_id
    ON core.exit_authorizations (tariff_snapshot_id);

CREATE INDEX IF NOT EXISTS ix_exit_authorizations__completion_basis
    ON core.exit_authorizations (completion_basis);

COMMENT ON COLUMN core.exit_authorizations.tariff_snapshot_id IS
    'Immutable payable basis for which completion was established.';
COMMENT ON COLUMN core.exit_authorizations.completion_basis IS
    'Canonical completion basis: PAYMENT_FINALITY or ZERO_PAYABLE_STATUTORY_FINALITY.';
COMMENT ON COLUMN core.exit_authorizations.payment_attempt_id IS
    'Confirmed payment attempt supporting PAYMENT_FINALITY; null for statutory zero-payable completion.';
COMMENT ON COLUMN core.exit_authorizations.payment_confirmation_id IS
    'Recorded confirmation supporting PAYMENT_FINALITY; null for statutory zero-payable completion.';
COMMENT ON COLUMN core.exit_authorizations.statutory_discount_decision_command_id IS
    'Approved statutory decision backing zero-payable completion.';
COMMENT ON COLUMN core.exit_authorizations.statutory_discount_payable_basis_application_command_id IS
    'Applied zero-payable basis command backing statutory completion.';
COMMENT ON COLUMN core.exit_authorizations.statutory_discount_validation_id IS
    'Approved statutory validation backing zero-payable completion.';
COMMENT ON COLUMN core.exit_authorizations.applied_policy_reference_id IS
    'Applied governed policy reference backing zero-payable completion.';
