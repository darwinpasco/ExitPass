BEGIN;

DO $$
BEGIN
    IF to_regclass('core.tariff_snapshots') IS NULL THEN
        RAISE EXCEPTION 'core.tariff_snapshots is required';
    END IF;

    IF to_regclass('discounts.statutory_discount_payable_basis_applications') IS NULL THEN
        RAISE EXCEPTION 'discounts.statutory_discount_payable_basis_applications is required';
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS discounts.statutory_discount_payable_basis_recalculations (
    statutory_discount_payable_basis_recalculation_id uuid DEFAULT gen_random_uuid() NOT NULL,
    statutory_discount_payable_basis_application_id uuid NOT NULL,
    statutory_discount_validation_id uuid NOT NULL,
    parking_session_id uuid NOT NULL,
    previous_applied_tariff_snapshot_id uuid NOT NULL,
    original_tariff_snapshot_id uuid NOT NULL,
    applied_tariff_snapshot_id uuid NOT NULL,
    gross_amount_minor_units bigint NOT NULL,
    vat_amount_minor_units bigint NOT NULL,
    vat_exclusive_amount_minor_units bigint NOT NULL,
    statutory_discount_amount_minor_units bigint NOT NULL,
    final_payable_amount_minor_units bigint NOT NULL,
    currency_code char(3) NOT NULL,
    computation_basis_json jsonb DEFAULT '{}'::jsonb NOT NULL,
    correlation_id uuid NOT NULL,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_service_identity_id uuid NOT NULL,
    CONSTRAINT pk_statutory_discount_payable_basis_recalculations
        PRIMARY KEY (statutory_discount_payable_basis_recalculation_id),
    CONSTRAINT fk_sd_pbr__application
        FOREIGN KEY (statutory_discount_payable_basis_application_id)
        REFERENCES discounts.statutory_discount_payable_basis_applications(
            statutory_discount_payable_basis_application_id)
        DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_sd_pbr__validation
        FOREIGN KEY (statutory_discount_validation_id)
        REFERENCES discounts.statutory_discount_validations(statutory_discount_validation_id)
        DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_sd_pbr__parking_session
        FOREIGN KEY (parking_session_id)
        REFERENCES core.parking_sessions(parking_session_id)
        DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_sd_pbr__previous_applied_tariff
        FOREIGN KEY (previous_applied_tariff_snapshot_id)
        REFERENCES core.tariff_snapshots(tariff_snapshot_id)
        DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_sd_pbr__original_tariff
        FOREIGN KEY (original_tariff_snapshot_id)
        REFERENCES core.tariff_snapshots(tariff_snapshot_id)
        DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_sd_pbr__applied_tariff
        FOREIGN KEY (applied_tariff_snapshot_id)
        REFERENCES core.tariff_snapshots(tariff_snapshot_id)
        DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_sd_pbr__created_by_service_identity
        FOREIGN KEY (created_by_service_identity_id)
        REFERENCES identity.service_identities(service_identity_id)
        DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT ck_sd_pbr__distinct_tariffs
        CHECK (
            previous_applied_tariff_snapshot_id <> original_tariff_snapshot_id
            AND previous_applied_tariff_snapshot_id <> applied_tariff_snapshot_id
            AND original_tariff_snapshot_id <> applied_tariff_snapshot_id),
    CONSTRAINT ck_sd_pbr__gross_positive
        CHECK (gross_amount_minor_units > 0),
    CONSTRAINT ck_sd_pbr__vat_non_negative
        CHECK (vat_amount_minor_units >= 0),
    CONSTRAINT ck_sd_pbr__vat_exclusive_non_negative
        CHECK (vat_exclusive_amount_minor_units >= 0),
    CONSTRAINT ck_sd_pbr__gross_components
        CHECK (vat_exclusive_amount_minor_units + vat_amount_minor_units = gross_amount_minor_units),
    CONSTRAINT ck_sd_pbr__discount_positive
        CHECK (statutory_discount_amount_minor_units > 0),
    CONSTRAINT ck_sd_pbr__discount_not_greater_than_vat_exclusive
        CHECK (statutory_discount_amount_minor_units <= vat_exclusive_amount_minor_units),
    CONSTRAINT ck_sd_pbr__final_non_negative
        CHECK (final_payable_amount_minor_units >= 0),
    CONSTRAINT ck_sd_pbr__final_not_greater_than_gross
        CHECK (final_payable_amount_minor_units <= gross_amount_minor_units),
    CONSTRAINT ck_sd_pbr__currency_code
        CHECK (currency_code = upper(currency_code) AND currency_code ~ '^[A-Z]{3}$')
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_sd_pbr__previous_applied_tariff
    ON discounts.statutory_discount_payable_basis_recalculations (
        previous_applied_tariff_snapshot_id);

CREATE UNIQUE INDEX IF NOT EXISTS ux_sd_pbr__original_tariff
    ON discounts.statutory_discount_payable_basis_recalculations (
        original_tariff_snapshot_id);

CREATE UNIQUE INDEX IF NOT EXISTS ux_sd_pbr__applied_tariff
    ON discounts.statutory_discount_payable_basis_recalculations (
        applied_tariff_snapshot_id);

CREATE INDEX IF NOT EXISTS ix_sd_pbr__application_created
    ON discounts.statutory_discount_payable_basis_recalculations (
        statutory_discount_payable_basis_application_id,
        created_at DESC);

COMMENT ON TABLE discounts.statutory_discount_payable_basis_recalculations IS
    'Append-only evidence for recalculating an already applied statutory full-fee benefit from a changed authoritative vendor tariff.';

CREATE OR REPLACE FUNCTION discounts.reject_statutory_discount_payable_basis_recalculation_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $function$
BEGIN
    RAISE EXCEPTION 'statutory payable-basis recalculation evidence is immutable'
        USING ERRCODE = '23514';
END;
$function$;

DROP TRIGGER IF EXISTS trg_sd_pbr__immutable
    ON discounts.statutory_discount_payable_basis_recalculations;

CREATE TRIGGER trg_sd_pbr__immutable
BEFORE UPDATE OR DELETE
ON discounts.statutory_discount_payable_basis_recalculations
FOR EACH ROW
EXECUTE FUNCTION discounts.reject_statutory_discount_payable_basis_recalculation_mutation();

DROP INDEX IF EXISTS core.ux_tariff_snapshots__statutory_discount_validation_applied;

CREATE UNIQUE INDEX ux_tariff_snapshots__statutory_discount_validation_applied
    ON core.tariff_snapshots (statutory_discount_validation_id)
    WHERE statutory_discount_validation_id IS NOT NULL
      AND snapshot_status = 'ACTIVE'::core.tariff_snapshot_status_enum;

COMMENT ON INDEX core.ux_tariff_snapshots__statutory_discount_validation_applied IS
    'Allows immutable historical statutory-adjusted revisions while enforcing one ACTIVE adjusted snapshot per validation.';

COMMIT;
