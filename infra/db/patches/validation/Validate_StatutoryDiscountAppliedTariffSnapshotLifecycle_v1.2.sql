/*
 * ExitPass v1.2 validation script.
 *
 * Validates database support for final APPLIED statutory discount tariff snapshot lifecycle.
 * This script is read-only except for temporary DO-block execution state.
 */

DO $$
DECLARE
    v_missing text[];
    v_function_identity text;
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_type t
        JOIN pg_namespace n ON n.oid = t.typnamespace
        JOIN pg_enum e ON e.enumtypid = t.oid
        WHERE n.nspname = 'core'
          AND t.typname = 'tariff_snapshot_status_enum'
          AND e.enumlabel = 'SUPERSEDED'
    ) THEN
        RAISE EXCEPTION 'missing tariff snapshot status enum value core.tariff_snapshot_status_enum.SUPERSEDED';
    END IF;

    SELECT array_agg(expected.enum_value ORDER BY expected.enum_value)
    INTO v_missing
    FROM (
        VALUES ('REQUESTED'), ('APPLIED')
    ) AS expected(enum_value)
    WHERE NOT EXISTS (
        SELECT 1
        FROM pg_type t
        JOIN pg_enum e ON e.enumtypid = t.oid
        JOIN pg_namespace n ON n.oid = t.typnamespace
        WHERE n.nspname = 'discounts'
          AND t.typname = 'statutory_discount_payable_application_status_enum'
          AND e.enumlabel = expected.enum_value
    );

    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'missing required payable application status values: %', v_missing;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_indexes
        WHERE schemaname = 'core'
          AND tablename = 'tariff_snapshots'
          AND indexname = 'ux_tariff_snapshots__active_by_session'
          AND indexdef LIKE '%snapshot_status = ''ACTIVE''%'
    ) THEN
        RAISE EXCEPTION 'missing active tariff snapshot uniqueness guard ux_tariff_snapshots__active_by_session';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_indexes
        WHERE schemaname = 'core'
          AND tablename = 'tariff_snapshots'
          AND indexname = 'ux_tariff_snapshots__statutory_discount_validation_applied'
          AND indexdef LIKE '%statutory_discount_validation_id IS NOT NULL%'
    ) THEN
        RAISE EXCEPTION 'missing statutory discount validation applied tariff snapshot uniqueness guard';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_indexes
        WHERE schemaname = 'discounts'
          AND tablename = 'statutory_discount_payable_basis_applications'
          AND indexname = 'ux_sd_pba__applied_tariff_snapshot'
    ) THEN
        RAISE EXCEPTION 'missing applied tariff snapshot uniqueness guard ux_sd_pba__applied_tariff_snapshot';
    END IF;

    SELECT array_agg(expected.constraint_name ORDER BY expected.constraint_name)
    INTO v_missing
    FROM (
        VALUES
            ('ck_sd_pba__applied_fields'),
            ('ck_sd_pba__distinct_snapshots')
    ) AS expected(constraint_name)
    WHERE NOT EXISTS (
        SELECT 1
        FROM pg_constraint con
        JOIN pg_class cls ON cls.oid = con.conrelid
        JOIN pg_namespace n ON n.oid = cls.relnamespace
        WHERE n.nspname = 'discounts'
          AND cls.relname = 'statutory_discount_payable_basis_applications'
          AND con.conname = expected.constraint_name
    );

    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'missing payable-basis application lifecycle constraints: %', v_missing;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint con
        JOIN pg_class cls ON cls.oid = con.conrelid
        JOIN pg_namespace n ON n.oid = cls.relnamespace
        WHERE n.nspname = 'core'
          AND cls.relname = 'tariff_snapshots'
          AND con.conname = 'ck_tariff_snapshots__statutory_discount_link_has_discount'
    ) THEN
        RAISE EXCEPTION 'missing statutory discount linked tariff snapshot positive discount constraint';
    END IF;

    SELECT p.oid::regprocedure::text
    INTO v_function_identity
    FROM pg_proc p
    JOIN pg_namespace n ON n.oid = p.pronamespace
    WHERE n.nspname = 'discounts'
      AND p.proname = 'apply_statutory_discount_payable_basis'
      AND pg_get_function_arguments(p.oid) =
          'p_statutory_discount_payable_basis_application_id uuid, p_actor_user_id uuid, p_correlation_id uuid';

    IF v_function_identity IS NULL THEN
        RAISE EXCEPTION 'missing routine discounts.apply_statutory_discount_payable_basis(uuid, uuid, uuid)';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_proc p
        JOIN pg_namespace n ON n.oid = p.pronamespace
        WHERE n.nspname = 'discounts'
          AND p.proname = 'enforce_statutory_discount_payable_basis_application'
    ) THEN
        RAISE EXCEPTION 'missing payable-basis application trigger enforcement function';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_trigger t
        JOIN pg_class cls ON cls.oid = t.tgrelid
        JOIN pg_namespace n ON n.oid = cls.relnamespace
        WHERE n.nspname = 'discounts'
          AND cls.relname = 'statutory_discount_payable_basis_applications'
          AND t.tgname = 'trg_sd_pba__enforce'
          AND NOT t.tgisinternal
    ) THEN
        RAISE EXCEPTION 'missing payable-basis application trigger trg_sd_pba__enforce';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema IN ('payments', 'gates', 'coupons', 'reconciliation')
          AND column_name LIKE '%statutory_discount_payable_basis_application%'
    ) THEN
        RAISE EXCEPTION 'unexpected statutory discount payable-basis columns found in payment/gate/coupon/reconciliation schemas';
    END IF;

    RAISE NOTICE 'Statutory discount applied tariff snapshot lifecycle validation passed. Routine: %', v_function_identity;
END;
$$;
