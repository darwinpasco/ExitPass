/*
 * ExitPass v1.2 durable SQL patch.
 *
 * Statutory discount payable-basis application schema support.
 *
 * References:
 * - docs/operator-console/statutory-discount-payable-basis-application-design.md
 *
 * System invariants:
 * - Approved Operator Console statutory discount validations may be applied to payable basis only by a future backend routine.
 * - Application evidence is stored separately from statutory validation decision state.
 * - The original tariff snapshot remains immutable; a future implementation should create a superseding tariff snapshot.
 * - This patch does not create payment attempts, payment confirmations, provider outcomes, exit authorizations,
 *   gate consumptions, coupon applications, settlement truth, reconciliation records, or AUB objects.
 */

DO $$ BEGIN
    CREATE TYPE discounts.statutory_discount_payable_application_status_enum AS ENUM (
        'REQUESTED',
        'APPLIED',
        'FAILED',
        'CANCELLED'
    );
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    CREATE TYPE discounts.statutory_discount_payable_application_channel_enum AS ENUM (
        'OPERATOR_CONSOLE',
        'OPERATOR_ASSISTED',
        'SYSTEM'
    );
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

CREATE TABLE IF NOT EXISTS discounts.statutory_discount_payable_basis_applications (
    statutory_discount_payable_basis_application_id uuid DEFAULT gen_random_uuid() NOT NULL,
    statutory_discount_validation_id uuid NOT NULL,
    parking_session_id uuid NOT NULL,
    original_tariff_snapshot_id uuid NOT NULL,
    applied_tariff_snapshot_id uuid,
    application_status discounts.statutory_discount_payable_application_status_enum NOT NULL,
    application_channel discounts.statutory_discount_payable_application_channel_enum NOT NULL,
    gross_amount_minor_units bigint NOT NULL,
    vat_amount_minor_units bigint NOT NULL,
    vat_exclusive_amount_minor_units bigint NOT NULL,
    statutory_discount_amount_minor_units bigint NOT NULL,
    final_payable_amount_minor_units bigint NOT NULL,
    currency_code char(3) NOT NULL,
    computation_basis_json jsonb DEFAULT '{}'::jsonb NOT NULL,
    rounding_mode varchar(64) DEFAULT 'HALF_AWAY_FROM_ZERO' NOT NULL,
    applied_at timestamptz,
    applied_by_user_id uuid,
    applied_by_service_identity_id uuid,
    idempotency_key varchar(128),
    correlation_id uuid NOT NULL,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_statutory_discount_payable_basis_applications
        PRIMARY KEY (statutory_discount_payable_basis_application_id),
    CONSTRAINT fk_sd_pba__validation
        FOREIGN KEY (statutory_discount_validation_id)
        REFERENCES discounts.statutory_discount_validations(statutory_discount_validation_id)
        DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_sd_pba__parking_session
        FOREIGN KEY (parking_session_id)
        REFERENCES core.parking_sessions(parking_session_id)
        DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_sd_pba__original_tariff_snapshot
        FOREIGN KEY (original_tariff_snapshot_id)
        REFERENCES core.tariff_snapshots(tariff_snapshot_id)
        DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_sd_pba__applied_tariff_snapshot
        FOREIGN KEY (applied_tariff_snapshot_id)
        REFERENCES core.tariff_snapshots(tariff_snapshot_id)
        DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_sd_pba__applied_by_user
        FOREIGN KEY (applied_by_user_id)
        REFERENCES identity.users(user_id)
        DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_sd_pba__applied_by_service_identity
        FOREIGN KEY (applied_by_service_identity_id)
        REFERENCES identity.service_identities(service_identity_id)
        DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_sd_pba__created_by_user
        FOREIGN KEY (created_by_user_id)
        REFERENCES identity.users(user_id)
        DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_sd_pba__created_by_service_identity
        FOREIGN KEY (created_by_service_identity_id)
        REFERENCES identity.service_identities(service_identity_id)
        DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_sd_pba__updated_by_user
        FOREIGN KEY (updated_by_user_id)
        REFERENCES identity.users(user_id)
        DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_sd_pba__updated_by_service_identity
        FOREIGN KEY (updated_by_service_identity_id)
        REFERENCES identity.service_identities(service_identity_id)
        DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT ck_sd_pba__gross_non_negative
        CHECK (gross_amount_minor_units >= 0),
    CONSTRAINT ck_sd_pba__vat_non_negative
        CHECK (vat_amount_minor_units >= 0),
    CONSTRAINT ck_sd_pba__vat_exclusive_non_negative
        CHECK (vat_exclusive_amount_minor_units >= 0),
    CONSTRAINT ck_sd_pba__discount_non_negative
        CHECK (statutory_discount_amount_minor_units >= 0),
    CONSTRAINT ck_sd_pba__final_non_negative
        CHECK (final_payable_amount_minor_units >= 0),
    CONSTRAINT ck_sd_pba__gross_components
        CHECK (vat_exclusive_amount_minor_units + vat_amount_minor_units = gross_amount_minor_units),
    CONSTRAINT ck_sd_pba__final_not_greater_than_gross
        CHECK (final_payable_amount_minor_units <= gross_amount_minor_units),
    CONSTRAINT ck_sd_pba__discount_not_greater_than_vat_exclusive
        CHECK (statutory_discount_amount_minor_units <= vat_exclusive_amount_minor_units),
    CONSTRAINT ck_sd_pba__currency_code
        CHECK (currency_code = upper(currency_code) AND currency_code ~ '^[A-Z]{3}$'),
    CONSTRAINT ck_sd_pba__applied_fields
        CHECK (
            application_status <> 'APPLIED'
            OR (applied_tariff_snapshot_id IS NOT NULL AND applied_at IS NOT NULL)
        ),
    CONSTRAINT ck_sd_pba__distinct_snapshots
        CHECK (applied_tariff_snapshot_id IS NULL OR applied_tariff_snapshot_id <> original_tariff_snapshot_id),
    CONSTRAINT ck_sd_pba__row_version_positive
        CHECK (row_version > 0)
);

COMMENT ON TABLE discounts.statutory_discount_payable_basis_applications IS
    'Immutable audit/control record for applying an approved statutory discount validation to a superseding payable-basis tariff snapshot.';

COMMENT ON COLUMN discounts.statutory_discount_payable_basis_applications.statutory_discount_validation_id IS
    'Approved discounts.statutory_discount_validations row that authorizes the payable-basis application.';

COMMENT ON COLUMN discounts.statutory_discount_payable_basis_applications.original_tariff_snapshot_id IS
    'Original active tariff snapshot used as the immutable input basis before statutory discount application.';

COMMENT ON COLUMN discounts.statutory_discount_payable_basis_applications.applied_tariff_snapshot_id IS
    'Superseding tariff snapshot created by the future apply-payable-basis implementation.';

COMMENT ON COLUMN discounts.statutory_discount_payable_basis_applications.computation_basis_json IS
    'Structured non-sensitive computation metadata such as VAT rate, source policy, formula version, and rounding inputs.';

COMMENT ON COLUMN discounts.statutory_discount_payable_basis_applications.idempotency_key IS
    'Caller-provided idempotency key for deterministic replay of payable-basis application.';

CREATE UNIQUE INDEX IF NOT EXISTS ux_sd_pba__validation_active
    ON discounts.statutory_discount_payable_basis_applications (statutory_discount_validation_id)
    WHERE application_status IN (
        'REQUESTED'::discounts.statutory_discount_payable_application_status_enum,
        'APPLIED'::discounts.statutory_discount_payable_application_status_enum
    );

CREATE UNIQUE INDEX IF NOT EXISTS ux_sd_pba__session_active
    ON discounts.statutory_discount_payable_basis_applications (parking_session_id)
    WHERE application_status IN (
        'REQUESTED'::discounts.statutory_discount_payable_application_status_enum,
        'APPLIED'::discounts.statutory_discount_payable_application_status_enum
    );

CREATE UNIQUE INDEX IF NOT EXISTS ux_sd_pba__applied_tariff_snapshot
    ON discounts.statutory_discount_payable_basis_applications (applied_tariff_snapshot_id)
    WHERE applied_tariff_snapshot_id IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_sd_pba__idempotency_key
    ON discounts.statutory_discount_payable_basis_applications (idempotency_key)
    WHERE idempotency_key IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_sd_pba__parking_session
    ON discounts.statutory_discount_payable_basis_applications (parking_session_id);

CREATE INDEX IF NOT EXISTS ix_sd_pba__original_tariff_snapshot
    ON discounts.statutory_discount_payable_basis_applications (original_tariff_snapshot_id);

CREATE INDEX IF NOT EXISTS ix_sd_pba__status
    ON discounts.statutory_discount_payable_basis_applications (application_status);

CREATE INDEX IF NOT EXISTS ix_sd_pba__correlation_id
    ON discounts.statutory_discount_payable_basis_applications (correlation_id);

CREATE OR REPLACE FUNCTION discounts.enforce_statutory_discount_payable_basis_application()
RETURNS trigger
LANGUAGE plpgsql
AS $function$
DECLARE
    v_validation discounts.statutory_discount_validations%ROWTYPE;
    v_original_tariff core.tariff_snapshots%ROWTYPE;
    v_applied_tariff core.tariff_snapshots%ROWTYPE;
    v_payment_attempt_exists boolean;
BEGIN
    SELECT *
    INTO v_validation
    FROM discounts.statutory_discount_validations
    WHERE statutory_discount_validation_id = NEW.statutory_discount_validation_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'statutory discount validation % was not found', NEW.statutory_discount_validation_id
            USING ERRCODE = '23503';
    END IF;

    IF v_validation.parking_session_id <> NEW.parking_session_id THEN
        RAISE EXCEPTION 'statutory discount validation % belongs to parking session %, not %',
            NEW.statutory_discount_validation_id,
            v_validation.parking_session_id,
            NEW.parking_session_id
            USING ERRCODE = '23514';
    END IF;

    SELECT *
    INTO v_original_tariff
    FROM core.tariff_snapshots
    WHERE tariff_snapshot_id = NEW.original_tariff_snapshot_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'original tariff snapshot % was not found', NEW.original_tariff_snapshot_id
            USING ERRCODE = '23503';
    END IF;

    IF v_original_tariff.parking_session_id <> NEW.parking_session_id THEN
        RAISE EXCEPTION 'original tariff snapshot % belongs to parking session %, not %',
            NEW.original_tariff_snapshot_id,
            v_original_tariff.parking_session_id,
            NEW.parking_session_id
            USING ERRCODE = '23514';
    END IF;

    IF NEW.application_status = 'APPLIED' THEN
        IF v_validation.validation_status <> 'APPROVED' THEN
            RAISE EXCEPTION 'statutory discount validation % must be APPROVED before payable-basis application',
                NEW.statutory_discount_validation_id
                USING ERRCODE = '23514';
        END IF;

        IF v_validation.evidence_required AND NOT v_validation.evidence_captured THEN
            RAISE EXCEPTION 'statutory discount validation % requires captured evidence before payable-basis application',
                NEW.statutory_discount_validation_id
                USING ERRCODE = '23514';
        END IF;

        SELECT *
        INTO v_applied_tariff
        FROM core.tariff_snapshots
        WHERE tariff_snapshot_id = NEW.applied_tariff_snapshot_id;

        IF NOT FOUND THEN
            RAISE EXCEPTION 'applied tariff snapshot % was not found', NEW.applied_tariff_snapshot_id
                USING ERRCODE = '23503';
        END IF;

        IF v_applied_tariff.parking_session_id <> NEW.parking_session_id THEN
            RAISE EXCEPTION 'applied tariff snapshot % belongs to parking session %, not %',
                NEW.applied_tariff_snapshot_id,
                v_applied_tariff.parking_session_id,
                NEW.parking_session_id
                USING ERRCODE = '23514';
        END IF;

        IF v_applied_tariff.statutory_discount_validation_id IS DISTINCT FROM NEW.statutory_discount_validation_id THEN
            RAISE EXCEPTION 'applied tariff snapshot % must reference statutory discount validation %',
                NEW.applied_tariff_snapshot_id,
                NEW.statutory_discount_validation_id
                USING ERRCODE = '23514';
        END IF;

        IF v_applied_tariff.snapshot_status <> 'ACTIVE' THEN
            RAISE EXCEPTION 'applied tariff snapshot % must be ACTIVE, not %',
                NEW.applied_tariff_snapshot_id,
                v_applied_tariff.snapshot_status
                USING ERRCODE = '23514';
        END IF;

        IF v_applied_tariff.statutory_discount_amount <= 0 THEN
            RAISE EXCEPTION 'applied tariff snapshot % must contain a positive statutory discount amount',
                NEW.applied_tariff_snapshot_id
                USING ERRCODE = '23514';
        END IF;

        SELECT EXISTS (
            SELECT 1
            FROM core.payment_attempts
            WHERE parking_session_id = NEW.parking_session_id
        )
        INTO v_payment_attempt_exists;

        IF v_payment_attempt_exists THEN
            RAISE EXCEPTION 'parking session % already has a payment attempt and cannot receive statutory payable-basis application',
                NEW.parking_session_id
                USING ERRCODE = '23514';
        END IF;
    END IF;

    RETURN NEW;
END;
$function$;

DROP TRIGGER IF EXISTS trg_sd_pba__enforce
    ON discounts.statutory_discount_payable_basis_applications;

CREATE TRIGGER trg_sd_pba__enforce
BEFORE INSERT OR UPDATE OF
    statutory_discount_validation_id,
    parking_session_id,
    original_tariff_snapshot_id,
    applied_tariff_snapshot_id,
    application_status
ON discounts.statutory_discount_payable_basis_applications
FOR EACH ROW
EXECUTE FUNCTION discounts.enforce_statutory_discount_payable_basis_application();
