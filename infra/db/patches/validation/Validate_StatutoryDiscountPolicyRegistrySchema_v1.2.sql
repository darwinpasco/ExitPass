/*
 * ExitPass v1.2 validation script.
 *
 * Validates database support for jurisdiction-based statutory discount policy resolution.
 * This script is read-only except for temporary DO-block execution state.
 */

DO $$
DECLARE
    v_missing text[];
BEGIN
    SELECT array_agg(expected.enum_name ORDER BY expected.enum_name)
    INTO v_missing
    FROM (
        VALUES
            ('sites', 'jurisdiction_type_enum'),
            ('sites', 'jurisdiction_status_enum'),
            ('discounts', 'policy_verification_status_enum'),
            ('discounts', 'beneficiary_residency_scope_enum'),
            ('discounts', 'parking_benefit_type_enum'),
            ('discounts', 'free_period_application_enum'),
            ('discounts', 'succeeding_hours_discount_rule_enum'),
            ('discounts', 'discount_base_scope_enum'),
            ('discounts', 'discount_stacking_policy_enum'),
            ('discounts', 'legal_basis_priority_enum')
    ) AS expected(schema_name, enum_name)
    WHERE NOT EXISTS (
        SELECT 1
        FROM pg_type t
        JOIN pg_namespace n ON n.oid = t.typnamespace
        WHERE n.nspname = expected.schema_name
          AND t.typname = expected.enum_name
    );

    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'missing policy registry enum types: %', v_missing;
    END IF;

    SELECT array_agg(expected.enum_value ORDER BY expected.enum_value)
    INTO v_missing
    FROM (
        VALUES
            ('VERIFIED_OFFICIAL'),
            ('VERIFIED_SECONDARY'),
            ('LEAD_UNVERIFIED'),
            ('PROPOSED'),
            ('NO_LOCAL_RULE_FOUND')
    ) AS expected(enum_value)
    WHERE NOT EXISTS (
        SELECT 1
        FROM pg_type t
        JOIN pg_enum e ON e.enumtypid = t.oid
        JOIN pg_namespace n ON n.oid = t.typnamespace
        WHERE n.nspname = 'discounts'
          AND t.typname = 'policy_verification_status_enum'
          AND e.enumlabel = expected.enum_value
    );

    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'missing policy verification status enum values: %', v_missing;
    END IF;

    SELECT array_agg(expected.enum_value ORDER BY expected.enum_value)
    INTO v_missing
    FROM (
        VALUES
            ('REGULAR_RATE'),
            ('APPLY_NATIONAL_STATUTORY_DISCOUNT'),
            ('APPLY_LOCAL_STATUTORY_DISCOUNT'),
            ('MANUAL_REVIEW')
    ) AS expected(enum_value)
    WHERE NOT EXISTS (
        SELECT 1
        FROM pg_type t
        JOIN pg_enum e ON e.enumtypid = t.oid
        JOIN pg_namespace n ON n.oid = t.typnamespace
        WHERE n.nspname = 'discounts'
          AND t.typname = 'succeeding_hours_discount_rule_enum'
          AND e.enumlabel = expected.enum_value
    );

    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'missing succeeding-hours enum values: %', v_missing;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.tables
        WHERE table_schema = 'sites'
          AND table_name = 'jurisdictions'
    ) THEN
        RAISE EXCEPTION 'missing table sites.jurisdictions';
    END IF;

    SELECT array_agg(expected.column_name ORDER BY expected.column_name)
    INTO v_missing
    FROM (
        VALUES
            ('jurisdiction_id'),
            ('country_code'),
            ('province_name'),
            ('city_municipality_name'),
            ('barangay_name'),
            ('psgc_code'),
            ('lgu_code'),
            ('jurisdiction_type'),
            ('jurisdiction_status'),
            ('source_reference'),
            ('effective_from'),
            ('effective_to'),
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
        WHERE c.table_schema = 'sites'
          AND c.table_name = 'jurisdictions'
          AND c.column_name = expected.column_name
    );

    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'missing jurisdiction columns: %', v_missing;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'sites'
          AND table_name = 'sites'
          AND column_name = 'jurisdiction_id'
    ) THEN
        RAISE EXCEPTION 'missing sites.sites.jurisdiction_id';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.tables
        WHERE table_schema = 'discounts'
          AND table_name = 'statutory_discount_policy_registry'
    ) THEN
        RAISE EXCEPTION 'missing table discounts.statutory_discount_policy_registry';
    END IF;

    SELECT array_agg(expected.column_name ORDER BY expected.column_name)
    INTO v_missing
    FROM (
        VALUES
            ('statutory_discount_policy_id'),
            ('jurisdiction_id'),
            ('policy_code'),
            ('policy_name'),
            ('entitlement_type'),
            ('policy_resolution_basis'),
            ('policy_level'),
            ('policy_type'),
            ('ordinance_reference'),
            ('legal_basis_reference'),
            ('national_law_reference'),
            ('verification_status'),
            ('beneficiary_residency_scope'),
            ('benefit_type'),
            ('free_duration_minutes'),
            ('initial_rate_exempt_flag'),
            ('full_fee_exempt_flag'),
            ('overnight_excluded_flag'),
            ('valet_excluded_flag'),
            ('standalone_parking_excluded_flag'),
            ('driver_or_passenger_required_flag'),
            ('free_period_application'),
            ('succeeding_hours_discount_rule'),
            ('discount_base_scope'),
            ('stacking_policy'),
            ('legal_basis_priority'),
            ('requires_operator_validation'),
            ('requires_evidence'),
            ('effective_from'),
            ('effective_to'),
            ('policy_status'),
            ('source_reference'),
            ('reviewed_by_user_id'),
            ('reviewed_at'),
            ('policy_snapshot_json'),
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
          AND c.table_name = 'statutory_discount_policy_registry'
          AND c.column_name = expected.column_name
    );

    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'missing statutory discount policy registry columns: %', v_missing;
    END IF;

    SELECT array_agg(expected.constraint_name ORDER BY expected.constraint_name)
    INTO v_missing
    FROM (
        VALUES
            ('pk_jurisdictions'),
            ('ck_jurisdictions__country_code'),
            ('ck_jurisdictions__effective_window'),
            ('ck_jurisdictions__national_scope'),
            ('fk_sites__jurisdiction_id'),
            ('pk_statutory_discount_policy_registry'),
            ('fk_sd_policy_registry__jurisdiction'),
            ('ck_sd_policy_registry__national_fallback_entitlement_law'),
            ('ck_sd_policy_registry__national_fallback_no_free_parking'),
            ('ck_sd_policy_registry__local_ordinance'),
            ('ck_sd_policy_registry__unverified_not_active')
    ) AS expected(constraint_name)
    WHERE NOT EXISTS (
        SELECT 1
        FROM pg_constraint con
        WHERE con.conname = expected.constraint_name
    );

    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'missing policy registry constraints: %', v_missing;
    END IF;

    SELECT array_agg(expected.index_name ORDER BY expected.index_name)
    INTO v_missing
    FROM (
        VALUES
            ('ux_jurisdictions__psgc_code'),
            ('ux_jurisdictions__lgu_code_type'),
            ('ux_jurisdictions__national_active'),
            ('ix_sites__jurisdiction_id'),
            ('ux_sd_policy_registry__policy_code'),
            ('ux_sd_policy_registry__national_fallback_active'),
            ('ux_sd_policy_registry__active_verified_scope'),
            ('ix_sd_policy_registry__jurisdiction_entitlement'),
            ('ix_sd_policy_registry__status_verification'),
            ('ix_sd_policy_registry__effective_window'),
            ('ix_sd_policy_registry__national_law_reference')
    ) AS expected(index_name)
    WHERE NOT EXISTS (
        SELECT 1
        FROM pg_indexes i
        WHERE i.indexname = expected.index_name
    );

    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'missing policy registry indexes: %', v_missing;
    END IF;

    SELECT array_agg(expected.column_name ORDER BY expected.column_name)
    INTO v_missing
    FROM (
        VALUES
            ('statutory_discount_policy_id'),
            ('resolved_jurisdiction_id'),
            ('resolved_policy_snapshot_json')
    ) AS expected(column_name)
    WHERE NOT EXISTS (
        SELECT 1
        FROM information_schema.columns c
        WHERE c.table_schema = 'discounts'
          AND c.table_name = 'statutory_discount_validations'
          AND c.column_name = expected.column_name
    );

    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'missing statutory discount validation policy linkage columns: %', v_missing;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM discounts.statutory_discount_policy_registry p
        WHERE p.policy_code = 'PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK'
          AND p.entitlement_type = 'SENIOR_CITIZEN'::discounts.statutory_entitlement_type_enum
          AND p.policy_resolution_basis = 'NATIONAL_LAW_FALLBACK'::discounts.policy_resolution_basis_enum
          AND p.national_law_reference = 'RA 9994'
          AND p.benefit_type = 'STATUTORY_DISCOUNT_VAT_EXEMPT'::discounts.parking_benefit_type_enum
          AND p.free_duration_minutes IS NULL
          AND p.initial_rate_exempt_flag = false
          AND p.full_fee_exempt_flag = false
          AND p.policy_status = 'ACTIVE'::discounts.discount_policy_status_enum
    ) THEN
        RAISE EXCEPTION 'missing or invalid RA 9994 national fallback policy row';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM discounts.statutory_discount_policy_registry p
        WHERE p.policy_code = 'PH_RA10754_PWD_NATIONAL_FALLBACK'
          AND p.entitlement_type = 'PWD'::discounts.statutory_entitlement_type_enum
          AND p.policy_resolution_basis = 'NATIONAL_LAW_FALLBACK'::discounts.policy_resolution_basis_enum
          AND p.national_law_reference = 'RA 10754'
          AND p.benefit_type = 'STATUTORY_DISCOUNT_VAT_EXEMPT'::discounts.parking_benefit_type_enum
          AND p.free_duration_minutes IS NULL
          AND p.initial_rate_exempt_flag = false
          AND p.full_fee_exempt_flag = false
          AND p.policy_status = 'ACTIVE'::discounts.discount_policy_status_enum
    ) THEN
        RAISE EXCEPTION 'missing or invalid RA 10754 national fallback policy row';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM discounts.statutory_discount_policy_registry p
        WHERE p.policy_resolution_basis = 'NATIONAL_LAW_FALLBACK'::discounts.policy_resolution_basis_enum
          AND (
              p.free_duration_minutes IS NOT NULL
              OR p.initial_rate_exempt_flag
              OR p.full_fee_exempt_flag
              OR p.benefit_type <> 'STATUTORY_DISCOUNT_VAT_EXEMPT'::discounts.parking_benefit_type_enum
          )
    ) THEN
        RAISE EXCEPTION 'national fallback policy row grants parking-specific free/exemption benefits';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM information_schema.tables
        WHERE table_schema IN ('payments', 'gate', 'reconciliation', 'settlement', 'coupons')
          AND table_name LIKE '%statutory_discount_policy%'
    ) THEN
        RAISE EXCEPTION 'unexpected statutory discount policy objects in payment/gate/coupon/reconciliation schemas';
    END IF;

    RAISE NOTICE 'Statutory discount policy registry schema validation passed.';
END;
$$;
