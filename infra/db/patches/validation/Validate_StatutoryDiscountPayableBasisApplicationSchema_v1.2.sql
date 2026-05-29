/*
 * ExitPass v1.2 validation script.
 *
 * Validates database support for statutory discount payable-basis application.
 * This script is read-only except for temporary DO-block execution state.
 */

DO $$
DECLARE
    v_missing text[];
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_type t
        JOIN pg_namespace n ON n.oid = t.typnamespace
        WHERE n.nspname = 'discounts'
          AND t.typname = 'statutory_discount_payable_application_status_enum'
    ) THEN
        RAISE EXCEPTION 'missing enum discounts.statutory_discount_payable_application_status_enum';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_type t
        JOIN pg_namespace n ON n.oid = t.typnamespace
        WHERE n.nspname = 'discounts'
          AND t.typname = 'statutory_discount_payable_application_channel_enum'
    ) THEN
        RAISE EXCEPTION 'missing enum discounts.statutory_discount_payable_application_channel_enum';
    END IF;

    SELECT array_agg(expected.enum_value ORDER BY expected.enum_value)
    INTO v_missing
    FROM (
        VALUES ('REQUESTED'), ('APPLIED'), ('FAILED'), ('CANCELLED')
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
        RAISE EXCEPTION 'missing application status enum values: %', v_missing;
    END IF;

    SELECT array_agg(expected.enum_value ORDER BY expected.enum_value)
    INTO v_missing
    FROM (
        VALUES ('OPERATOR_CONSOLE'), ('OPERATOR_ASSISTED'), ('SYSTEM')
    ) AS expected(enum_value)
    WHERE NOT EXISTS (
        SELECT 1
        FROM pg_type t
        JOIN pg_enum e ON e.enumtypid = t.oid
        JOIN pg_namespace n ON n.oid = t.typnamespace
        WHERE n.nspname = 'discounts'
          AND t.typname = 'statutory_discount_payable_application_channel_enum'
          AND e.enumlabel = expected.enum_value
    );

    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'missing application channel enum values: %', v_missing;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.tables
        WHERE table_schema = 'discounts'
          AND table_name = 'statutory_discount_payable_basis_applications'
    ) THEN
        RAISE EXCEPTION 'missing table discounts.statutory_discount_payable_basis_applications';
    END IF;

    SELECT array_agg(expected.column_name ORDER BY expected.column_name)
    INTO v_missing
    FROM (
        VALUES
            ('statutory_discount_payable_basis_application_id'),
            ('statutory_discount_validation_id'),
            ('parking_session_id'),
            ('original_tariff_snapshot_id'),
            ('applied_tariff_snapshot_id'),
            ('application_status'),
            ('application_channel'),
            ('gross_amount_minor_units'),
            ('vat_amount_minor_units'),
            ('vat_exclusive_amount_minor_units'),
            ('statutory_discount_amount_minor_units'),
            ('final_payable_amount_minor_units'),
            ('currency_code'),
            ('computation_basis_json'),
            ('rounding_mode'),
            ('applied_at'),
            ('applied_by_user_id'),
            ('applied_by_service_identity_id'),
            ('idempotency_key'),
            ('correlation_id'),
            ('created_at'),
            ('created_by_user_id'),
            ('created_by_service_identity_id'),
            ('updated_at'),
            ('updated_by_user_id'),
            ('updated_by_service_identity_id'),
            ('row_version')
    ) AS expected(column_name)
    WHERE NOT EXISTS (
        SELECT 1
        FROM information_schema.columns c
        WHERE c.table_schema = 'discounts'
          AND c.table_name = 'statutory_discount_payable_basis_applications'
          AND c.column_name = expected.column_name
    );

    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'missing statutory discount payable-basis application columns: %', v_missing;
    END IF;

    SELECT array_agg(expected.constraint_name ORDER BY expected.constraint_name)
    INTO v_missing
    FROM (
        VALUES
            ('pk_statutory_discount_payable_basis_applications'),
            ('fk_sd_pba__validation'),
            ('fk_sd_pba__parking_session'),
            ('fk_sd_pba__original_tariff_snapshot'),
            ('fk_sd_pba__applied_tariff_snapshot'),
            ('ck_sd_pba__gross_non_negative'),
            ('ck_sd_pba__vat_non_negative'),
            ('ck_sd_pba__vat_exclusive_non_negative'),
            ('ck_sd_pba__discount_non_negative'),
            ('ck_sd_pba__final_non_negative'),
            ('ck_sd_pba__gross_components'),
            ('ck_sd_pba__final_not_greater_than_gross'),
            ('ck_sd_pba__discount_not_greater_than_vat_exclusive'),
            ('ck_sd_pba__currency_code'),
            ('ck_sd_pba__applied_fields'),
            ('ck_sd_pba__distinct_snapshots'),
            ('ck_sd_pba__row_version_positive')
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
        RAISE EXCEPTION 'missing statutory discount payable-basis application constraints: %', v_missing;
    END IF;

    SELECT array_agg(expected.index_name ORDER BY expected.index_name)
    INTO v_missing
    FROM (
        VALUES
            ('ux_sd_pba__validation_active'),
            ('ux_sd_pba__session_active'),
            ('ux_sd_pba__applied_tariff_snapshot'),
            ('ux_sd_pba__idempotency_key'),
            ('ix_sd_pba__parking_session'),
            ('ix_sd_pba__original_tariff_snapshot'),
            ('ix_sd_pba__status'),
            ('ix_sd_pba__correlation_id')
    ) AS expected(index_name)
    WHERE NOT EXISTS (
        SELECT 1
        FROM pg_indexes i
        WHERE i.schemaname = 'discounts'
          AND i.tablename = 'statutory_discount_payable_basis_applications'
          AND i.indexname = expected.index_name
    );

    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'missing statutory discount payable-basis application indexes: %', v_missing;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_proc p
        JOIN pg_namespace n ON n.oid = p.pronamespace
        WHERE n.nspname = 'discounts'
          AND p.proname = 'enforce_statutory_discount_payable_basis_application'
    ) THEN
        RAISE EXCEPTION 'missing trigger function discounts.enforce_statutory_discount_payable_basis_application';
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
        RAISE EXCEPTION 'missing trigger trg_sd_pba__enforce';
    END IF;

    RAISE NOTICE 'Statutory discount payable-basis application schema validation passed.';
END;
$$;
