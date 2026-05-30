/*
 * ExitPass v1.2 durable SQL patch.
 *
 * Jurisdiction and statutory discount policy registry schema support.
 *
 * References:
 * - docs/operator-console/statutory-discount-jurisdiction-policy-resolution-design.md
 * - docs/operator-console/Philippine_Parking_Statutory_Discount_Local_Ordinances_Detailed_List.docx
 *
 * System invariants:
 * - RA 9994 and RA 10754 are mandatory national fallback policies when no verified local
 *   jurisdiction-specific parking statutory discount policy is configured.
 * - National fallback is not automatic free parking.
 * - Local free parking, free duration, initial-rate exemption, residency scope, overnight exclusion,
 *   valet exclusion, standalone-parking exclusion, and driver/passenger conditions must come only
 *   from verified configured local ordinance policy rows.
 * - This patch does not create endpoints, payment attempts, payment confirmations, provider outcomes,
 *   exit authorizations, gate consumptions, coupon applications, settlement truth, reconciliation
 *   records, or AUB objects.
 */

DO $$ BEGIN
    CREATE TYPE sites.jurisdiction_type_enum AS ENUM (
        'NATIONAL',
        'PROVINCE',
        'CITY_MUNICIPALITY',
        'BARANGAY',
        'SITE_OVERRIDE'
    );
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    CREATE TYPE sites.jurisdiction_status_enum AS ENUM (
        'ACTIVE',
        'SUSPENDED',
        'RETIRED'
    );
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    CREATE TYPE discounts.policy_verification_status_enum AS ENUM (
        'VERIFIED_OFFICIAL',
        'VERIFIED_SECONDARY',
        'LEAD_UNVERIFIED',
        'PROPOSED',
        'NO_LOCAL_RULE_FOUND'
    );
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    CREATE TYPE discounts.beneficiary_residency_scope_enum AS ENUM (
        'RESIDENT_ONLY',
        'NON_RESIDENT_ALLOWED',
        'MIXED',
        'UNVERIFIED',
        'NOT_APPLICABLE'
    );
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    CREATE TYPE discounts.parking_benefit_type_enum AS ENUM (
        'STATUTORY_DISCOUNT_VAT_EXEMPT',
        'FREE_DURATION',
        'INITIAL_RATE_EXEMPTION',
        'FULL_FEE_EXEMPTION',
        'LOCAL_RULE',
        'MANUAL_REVIEW'
    );
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    CREATE TYPE discounts.free_period_application_enum AS ENUM (
        'BEFORE_DISCOUNT_COMPUTATION',
        'NOT_APPLICABLE'
    );
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    CREATE TYPE discounts.succeeding_hours_discount_rule_enum AS ENUM (
        'REGULAR_RATE',
        'APPLY_NATIONAL_STATUTORY_DISCOUNT',
        'APPLY_LOCAL_STATUTORY_DISCOUNT',
        'MANUAL_REVIEW'
    );
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    CREATE TYPE discounts.discount_base_scope_enum AS ENUM (
        'FULL_PARKING_FEE',
        'CHARGEABLE_PORTION_ONLY',
        'NOT_APPLICABLE'
    );
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    CREATE TYPE discounts.discount_stacking_policy_enum AS ENUM (
        'NO_STACKING_ON_FREE_PERIOD',
        'ALLOW_DISCOUNT_ON_SUCCEEDING_HOURS',
        'MANUAL_REVIEW'
    );
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    CREATE TYPE discounts.legal_basis_priority_enum AS ENUM (
        'LOCAL_ORDINANCE_FIRST',
        'NATIONAL_FALLBACK_ONLY_IF_NO_LOCAL_POLICY',
        'SITE_POLICY_REQUIRES_REVIEW'
    );
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

CREATE TABLE IF NOT EXISTS sites.jurisdictions (
    jurisdiction_id uuid DEFAULT gen_random_uuid() NOT NULL,
    country_code char(2) NOT NULL,
    province_name varchar(160),
    city_municipality_name varchar(160),
    barangay_name varchar(160),
    psgc_code varchar(32),
    lgu_code varchar(64),
    jurisdiction_type sites.jurisdiction_type_enum NOT NULL,
    jurisdiction_status sites.jurisdiction_status_enum NOT NULL,
    source_reference text,
    effective_from timestamptz DEFAULT now() NOT NULL,
    effective_to timestamptz,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_jurisdictions PRIMARY KEY (jurisdiction_id),
    CONSTRAINT fk_jurisdictions__created_by_user
        FOREIGN KEY (created_by_user_id)
        REFERENCES identity.users(user_id)
        DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_jurisdictions__created_by_service_identity
        FOREIGN KEY (created_by_service_identity_id)
        REFERENCES identity.service_identities(service_identity_id)
        DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_jurisdictions__updated_by_user
        FOREIGN KEY (updated_by_user_id)
        REFERENCES identity.users(user_id)
        DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_jurisdictions__updated_by_service_identity
        FOREIGN KEY (updated_by_service_identity_id)
        REFERENCES identity.service_identities(service_identity_id)
        DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT ck_jurisdictions__country_code
        CHECK (country_code::text = upper(country_code::text) AND country_code::text ~ '^[A-Z]{2}$'),
    CONSTRAINT ck_jurisdictions__effective_window
        CHECK (effective_to IS NULL OR effective_to > effective_from),
    CONSTRAINT ck_jurisdictions__row_version_positive
        CHECK (row_version > 0),
    CONSTRAINT ck_jurisdictions__national_scope
        CHECK (
            jurisdiction_type <> 'NATIONAL'
            OR (
                province_name IS NULL
                AND city_municipality_name IS NULL
                AND barangay_name IS NULL
                AND psgc_code IS NULL
                AND lgu_code IS NULL
            )
        )
);

COMMENT ON TABLE sites.jurisdictions IS
    'Canonical jurisdiction registry used to resolve site-scoped statutory discount policy.';

COMMENT ON COLUMN sites.jurisdictions.psgc_code IS
    'Optional Philippine Standard Geographic Code when available and verified.';

COMMENT ON COLUMN sites.jurisdictions.lgu_code IS
    'Optional internal or legacy LGU code used to bridge existing site policy references.';

CREATE UNIQUE INDEX IF NOT EXISTS ux_jurisdictions__psgc_code
    ON sites.jurisdictions (psgc_code)
    WHERE psgc_code IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_jurisdictions__lgu_code_type
    ON sites.jurisdictions (country_code, lgu_code, jurisdiction_type)
    WHERE lgu_code IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_jurisdictions__national_active
    ON sites.jurisdictions (country_code)
    WHERE jurisdiction_type = 'NATIONAL'::sites.jurisdiction_type_enum
      AND jurisdiction_status = 'ACTIVE'::sites.jurisdiction_status_enum;

CREATE INDEX IF NOT EXISTS ix_jurisdictions__status
    ON sites.jurisdictions (jurisdiction_status);

CREATE INDEX IF NOT EXISTS ix_jurisdictions__type
    ON sites.jurisdictions (jurisdiction_type);

ALTER TABLE sites.sites
    ADD COLUMN IF NOT EXISTS jurisdiction_id uuid;

DO $$
BEGIN
    ALTER TABLE sites.sites
        ADD CONSTRAINT fk_sites__jurisdiction_id
        FOREIGN KEY (jurisdiction_id)
        REFERENCES sites.jurisdictions(jurisdiction_id)
        DEFERRABLE INITIALLY IMMEDIATE;
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

COMMENT ON COLUMN sites.sites.jurisdiction_id IS
    'Optional governed jurisdiction mapping used by statutory discount policy resolution.';

CREATE INDEX IF NOT EXISTS ix_sites__jurisdiction_id
    ON sites.sites (jurisdiction_id);

CREATE TABLE IF NOT EXISTS discounts.statutory_discount_policy_registry (
    statutory_discount_policy_id uuid DEFAULT gen_random_uuid() NOT NULL,
    jurisdiction_id uuid,
    policy_code varchar(128) NOT NULL,
    policy_name varchar(256) NOT NULL,
    policy_description text,
    entitlement_type discounts.statutory_entitlement_type_enum NOT NULL,
    policy_resolution_basis discounts.policy_resolution_basis_enum NOT NULL,
    policy_level discounts.discount_policy_level_enum NOT NULL,
    policy_type discounts.discount_policy_type_enum NOT NULL,
    ordinance_reference varchar(256),
    legal_basis_reference varchar(256),
    national_law_reference varchar(128),
    verification_status discounts.policy_verification_status_enum NOT NULL,
    beneficiary_residency_scope discounts.beneficiary_residency_scope_enum NOT NULL,
    benefit_type discounts.parking_benefit_type_enum NOT NULL,
    free_duration_minutes integer,
    initial_rate_exempt_flag boolean DEFAULT false NOT NULL,
    full_fee_exempt_flag boolean DEFAULT false NOT NULL,
    overnight_excluded_flag boolean DEFAULT false NOT NULL,
    valet_excluded_flag boolean DEFAULT false NOT NULL,
    standalone_parking_excluded_flag boolean DEFAULT false NOT NULL,
    driver_or_passenger_required_flag boolean DEFAULT false NOT NULL,
    free_period_application discounts.free_period_application_enum DEFAULT 'NOT_APPLICABLE' NOT NULL,
    succeeding_hours_discount_rule discounts.succeeding_hours_discount_rule_enum NOT NULL,
    discount_base_scope discounts.discount_base_scope_enum NOT NULL,
    stacking_policy discounts.discount_stacking_policy_enum NOT NULL,
    legal_basis_priority discounts.legal_basis_priority_enum NOT NULL,
    requires_operator_validation boolean DEFAULT true NOT NULL,
    requires_evidence boolean DEFAULT false NOT NULL,
    effective_from date NOT NULL,
    effective_to date,
    policy_status discounts.discount_policy_status_enum NOT NULL,
    source_reference text,
    reviewed_by_user_id uuid,
    reviewed_at timestamptz,
    policy_snapshot_json jsonb DEFAULT '{}'::jsonb NOT NULL,
    created_at timestamptz DEFAULT now() NOT NULL,
    created_by_user_id uuid,
    created_by_service_identity_id uuid,
    updated_at timestamptz DEFAULT now() NOT NULL,
    updated_by_user_id uuid,
    updated_by_service_identity_id uuid,
    row_version bigint DEFAULT 1 NOT NULL,
    CONSTRAINT pk_statutory_discount_policy_registry
        PRIMARY KEY (statutory_discount_policy_id),
    CONSTRAINT fk_sd_policy_registry__jurisdiction
        FOREIGN KEY (jurisdiction_id)
        REFERENCES sites.jurisdictions(jurisdiction_id)
        DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_sd_policy_registry__reviewed_by_user
        FOREIGN KEY (reviewed_by_user_id)
        REFERENCES identity.users(user_id)
        DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_sd_policy_registry__created_by_user
        FOREIGN KEY (created_by_user_id)
        REFERENCES identity.users(user_id)
        DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_sd_policy_registry__created_by_service_identity
        FOREIGN KEY (created_by_service_identity_id)
        REFERENCES identity.service_identities(service_identity_id)
        DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_sd_policy_registry__updated_by_user
        FOREIGN KEY (updated_by_user_id)
        REFERENCES identity.users(user_id)
        DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT fk_sd_policy_registry__updated_by_service_identity
        FOREIGN KEY (updated_by_service_identity_id)
        REFERENCES identity.service_identities(service_identity_id)
        DEFERRABLE INITIALLY IMMEDIATE,
    CONSTRAINT ck_sd_policy_registry__policy_code_not_blank
        CHECK (btrim(policy_code) <> ''),
    CONSTRAINT ck_sd_policy_registry__effective_window
        CHECK (effective_to IS NULL OR effective_to > effective_from),
    CONSTRAINT ck_sd_policy_registry__free_duration_non_negative
        CHECK (free_duration_minutes IS NULL OR free_duration_minutes >= 0),
    CONSTRAINT ck_sd_policy_registry__row_version_positive
        CHECK (row_version > 0),
    CONSTRAINT ck_sd_policy_registry__free_benefit_requires_marker
        CHECK (
            benefit_type NOT IN (
                'FREE_DURATION'::discounts.parking_benefit_type_enum,
                'INITIAL_RATE_EXEMPTION'::discounts.parking_benefit_type_enum
            )
            OR free_duration_minutes IS NOT NULL
            OR initial_rate_exempt_flag
        ),
    CONSTRAINT ck_sd_policy_registry__national_fallback_entitlement_law
        CHECK (
            policy_resolution_basis <> 'NATIONAL_LAW_FALLBACK'::discounts.policy_resolution_basis_enum
            OR (
                (entitlement_type = 'SENIOR_CITIZEN'::discounts.statutory_entitlement_type_enum AND national_law_reference = 'RA 9994')
                OR (entitlement_type = 'PWD'::discounts.statutory_entitlement_type_enum AND national_law_reference = 'RA 10754')
            )
        ),
    CONSTRAINT ck_sd_policy_registry__national_fallback_no_free_parking
        CHECK (
            policy_resolution_basis <> 'NATIONAL_LAW_FALLBACK'::discounts.policy_resolution_basis_enum
            OR (
                national_law_reference IN ('RA 9994', 'RA 10754')
                AND benefit_type = 'STATUTORY_DISCOUNT_VAT_EXEMPT'::discounts.parking_benefit_type_enum
                AND free_duration_minutes IS NULL
                AND initial_rate_exempt_flag = false
                AND full_fee_exempt_flag = false
                AND free_period_application = 'NOT_APPLICABLE'::discounts.free_period_application_enum
                AND legal_basis_priority = 'NATIONAL_FALLBACK_ONLY_IF_NO_LOCAL_POLICY'::discounts.legal_basis_priority_enum
            )
        ),
    CONSTRAINT ck_sd_policy_registry__local_ordinance
        CHECK (
            policy_resolution_basis <> 'LOCAL_ORDINANCE_APPLIED'::discounts.policy_resolution_basis_enum
            OR (jurisdiction_id IS NOT NULL AND ordinance_reference IS NOT NULL)
        ),
    CONSTRAINT ck_sd_policy_registry__unverified_not_active
        CHECK (
            verification_status NOT IN (
                'LEAD_UNVERIFIED'::discounts.policy_verification_status_enum,
                'PROPOSED'::discounts.policy_verification_status_enum
            )
            OR policy_status <> 'ACTIVE'::discounts.discount_policy_status_enum
        )
);

COMMENT ON TABLE discounts.statutory_discount_policy_registry IS
    'Governed statutory discount policy registry for national fallback and verified local parking ordinance resolution.';

COMMENT ON COLUMN discounts.statutory_discount_policy_registry.policy_snapshot_json IS
    'Governed policy snapshot payload to persist into future statutory discount validation rows at resolution time.';

COMMENT ON COLUMN discounts.statutory_discount_policy_registry.verification_status IS
    'Source verification status. Only verified official/approved secondary rows may be used for automatic production resolution.';

COMMENT ON COLUMN discounts.statutory_discount_policy_registry.free_duration_minutes IS
    'Configured local free initial parking duration. Must not be inferred from national fallback rows.';

CREATE UNIQUE INDEX IF NOT EXISTS ux_sd_policy_registry__policy_code
    ON discounts.statutory_discount_policy_registry (policy_code);

CREATE UNIQUE INDEX IF NOT EXISTS ux_sd_policy_registry__national_fallback_active
    ON discounts.statutory_discount_policy_registry (entitlement_type)
    WHERE policy_resolution_basis = 'NATIONAL_LAW_FALLBACK'::discounts.policy_resolution_basis_enum
      AND policy_status = 'ACTIVE'::discounts.discount_policy_status_enum;

CREATE UNIQUE INDEX IF NOT EXISTS ux_sd_policy_registry__active_verified_scope
    ON discounts.statutory_discount_policy_registry (
        jurisdiction_id,
        entitlement_type,
        policy_resolution_basis,
        policy_level
    )
    WHERE policy_status = 'ACTIVE'::discounts.discount_policy_status_enum
      AND verification_status IN (
          'VERIFIED_OFFICIAL'::discounts.policy_verification_status_enum,
          'VERIFIED_SECONDARY'::discounts.policy_verification_status_enum
      )
      AND jurisdiction_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_sd_policy_registry__jurisdiction_entitlement
    ON discounts.statutory_discount_policy_registry (jurisdiction_id, entitlement_type);

CREATE INDEX IF NOT EXISTS ix_sd_policy_registry__status_verification
    ON discounts.statutory_discount_policy_registry (policy_status, verification_status);

CREATE INDEX IF NOT EXISTS ix_sd_policy_registry__effective_window
    ON discounts.statutory_discount_policy_registry (effective_from, effective_to);

CREATE INDEX IF NOT EXISTS ix_sd_policy_registry__national_law_reference
    ON discounts.statutory_discount_policy_registry (national_law_reference)
    WHERE national_law_reference IS NOT NULL;

ALTER TABLE discounts.statutory_discount_validations
    ADD COLUMN IF NOT EXISTS statutory_discount_policy_id uuid,
    ADD COLUMN IF NOT EXISTS resolved_jurisdiction_id uuid,
    ADD COLUMN IF NOT EXISTS resolved_policy_snapshot_json jsonb;

DO $$
BEGIN
    ALTER TABLE discounts.statutory_discount_validations
        ADD CONSTRAINT fk_statutory_discount_validations__statutory_discount_policy
        FOREIGN KEY (statutory_discount_policy_id)
        REFERENCES discounts.statutory_discount_policy_registry(statutory_discount_policy_id)
        DEFERRABLE INITIALLY IMMEDIATE;
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

DO $$
BEGIN
    ALTER TABLE discounts.statutory_discount_validations
        ADD CONSTRAINT fk_statutory_discount_validations__resolved_jurisdiction
        FOREIGN KEY (resolved_jurisdiction_id)
        REFERENCES sites.jurisdictions(jurisdiction_id)
        DEFERRABLE INITIALLY IMMEDIATE;
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

COMMENT ON COLUMN discounts.statutory_discount_validations.statutory_discount_policy_id IS
    'Future resolved statutory discount policy registry row used by the validation.';

COMMENT ON COLUMN discounts.statutory_discount_validations.resolved_jurisdiction_id IS
    'Future resolved site jurisdiction used by the validation policy resolver.';

COMMENT ON COLUMN discounts.statutory_discount_validations.resolved_policy_snapshot_json IS
    'Future immutable policy snapshot captured at validation/draft time.';

CREATE INDEX IF NOT EXISTS ix_statutory_discount_validations__statutory_discount_policy_id
    ON discounts.statutory_discount_validations (statutory_discount_policy_id);

CREATE INDEX IF NOT EXISTS ix_statutory_discount_validations__resolved_jurisdiction_id
    ON discounts.statutory_discount_validations (resolved_jurisdiction_id);

INSERT INTO sites.jurisdictions (
    jurisdiction_id,
    country_code,
    jurisdiction_type,
    jurisdiction_status,
    source_reference
)
VALUES (
    '12000000-0000-0000-0000-000000000b00',
    'PH',
    'NATIONAL',
    'ACTIVE',
    'ExitPass statutory discount national fallback jurisdiction'
)
ON CONFLICT (jurisdiction_id)
DO UPDATE SET
    country_code = EXCLUDED.country_code,
    jurisdiction_type = EXCLUDED.jurisdiction_type,
    jurisdiction_status = EXCLUDED.jurisdiction_status,
    source_reference = EXCLUDED.source_reference,
    updated_at = now();

INSERT INTO discounts.statutory_discount_policy_registry (
    statutory_discount_policy_id,
    jurisdiction_id,
    policy_code,
    policy_name,
    policy_description,
    entitlement_type,
    policy_resolution_basis,
    policy_level,
    policy_type,
    legal_basis_reference,
    national_law_reference,
    verification_status,
    beneficiary_residency_scope,
    benefit_type,
    free_duration_minutes,
    initial_rate_exempt_flag,
    full_fee_exempt_flag,
    overnight_excluded_flag,
    valet_excluded_flag,
    standalone_parking_excluded_flag,
    driver_or_passenger_required_flag,
    free_period_application,
    succeeding_hours_discount_rule,
    discount_base_scope,
    stacking_policy,
    legal_basis_priority,
    requires_operator_validation,
    requires_evidence,
    effective_from,
    policy_status,
    source_reference,
    reviewed_at,
    policy_snapshot_json
)
VALUES (
    '12000000-0000-0000-0000-000000000b01',
    '12000000-0000-0000-0000-000000000b00',
    'PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK',
    'RA 9994 Senior Citizen National Fallback',
    'Mandatory national fallback policy for Senior Citizen statutory discount resolution when no verified local parking policy is configured.',
    'SENIOR_CITIZEN',
    'NATIONAL_LAW_FALLBACK',
    'NATIONAL_LAW',
    'LEGAL_REFERENCE',
    'Expanded Senior Citizens Act of 2010',
    'RA 9994',
    'VERIFIED_OFFICIAL',
    'NON_RESIDENT_ALLOWED',
    'STATUTORY_DISCOUNT_VAT_EXEMPT',
    NULL,
    false,
    false,
    false,
    false,
    false,
    false,
    'NOT_APPLICABLE',
    'APPLY_NATIONAL_STATUTORY_DISCOUNT',
    'CHARGEABLE_PORTION_ONLY',
    'NO_STACKING_ON_FREE_PERIOD',
    'NATIONAL_FALLBACK_ONLY_IF_NO_LOCAL_POLICY',
    true,
    true,
    DATE '2026-01-01',
    'ACTIVE',
    'National fallback seed from #192; not an automatic free-parking policy.',
    now(),
    jsonb_build_object(
        'policyCode', 'PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK',
        'nationalLawReference', 'RA 9994',
        'entitlementType', 'SENIOR_CITIZEN',
        'policyResolutionBasis', 'NATIONAL_LAW_FALLBACK',
        'benefitType', 'STATUTORY_DISCOUNT_VAT_EXEMPT',
        'freeParking', false,
        'freeDurationMinutes', NULL,
        'initialRateExempt', false,
        'fullFeeExempt', false,
        'succeedingHoursDiscountRule', 'APPLY_NATIONAL_STATUTORY_DISCOUNT',
        'discountBaseScope', 'CHARGEABLE_PORTION_ONLY',
        'stackingPolicy', 'NO_STACKING_ON_FREE_PERIOD',
        'legalBasisPriority', 'NATIONAL_FALLBACK_ONLY_IF_NO_LOCAL_POLICY'
    )
),
(
    '12000000-0000-0000-0000-000000000b02',
    '12000000-0000-0000-0000-000000000b00',
    'PH_RA10754_PWD_NATIONAL_FALLBACK',
    'RA 10754 PWD National Fallback',
    'Mandatory national fallback policy for PWD statutory discount resolution when no verified local parking policy is configured.',
    'PWD',
    'NATIONAL_LAW_FALLBACK',
    'NATIONAL_LAW',
    'LEGAL_REFERENCE',
    'Act Expanding the Benefits and Privileges of Persons with Disability',
    'RA 10754',
    'VERIFIED_OFFICIAL',
    'NON_RESIDENT_ALLOWED',
    'STATUTORY_DISCOUNT_VAT_EXEMPT',
    NULL,
    false,
    false,
    false,
    false,
    false,
    false,
    'NOT_APPLICABLE',
    'APPLY_NATIONAL_STATUTORY_DISCOUNT',
    'CHARGEABLE_PORTION_ONLY',
    'NO_STACKING_ON_FREE_PERIOD',
    'NATIONAL_FALLBACK_ONLY_IF_NO_LOCAL_POLICY',
    true,
    true,
    DATE '2026-01-01',
    'ACTIVE',
    'National fallback seed from #192; not an automatic free-parking policy.',
    now(),
    jsonb_build_object(
        'policyCode', 'PH_RA10754_PWD_NATIONAL_FALLBACK',
        'nationalLawReference', 'RA 10754',
        'entitlementType', 'PWD',
        'policyResolutionBasis', 'NATIONAL_LAW_FALLBACK',
        'benefitType', 'STATUTORY_DISCOUNT_VAT_EXEMPT',
        'freeParking', false,
        'freeDurationMinutes', NULL,
        'initialRateExempt', false,
        'fullFeeExempt', false,
        'succeedingHoursDiscountRule', 'APPLY_NATIONAL_STATUTORY_DISCOUNT',
        'discountBaseScope', 'CHARGEABLE_PORTION_ONLY',
        'stackingPolicy', 'NO_STACKING_ON_FREE_PERIOD',
        'legalBasisPriority', 'NATIONAL_FALLBACK_ONLY_IF_NO_LOCAL_POLICY'
    )
)
ON CONFLICT (policy_code)
DO UPDATE SET
    jurisdiction_id = EXCLUDED.jurisdiction_id,
    policy_name = EXCLUDED.policy_name,
    policy_description = EXCLUDED.policy_description,
    entitlement_type = EXCLUDED.entitlement_type,
    policy_resolution_basis = EXCLUDED.policy_resolution_basis,
    policy_level = EXCLUDED.policy_level,
    policy_type = EXCLUDED.policy_type,
    legal_basis_reference = EXCLUDED.legal_basis_reference,
    national_law_reference = EXCLUDED.national_law_reference,
    verification_status = EXCLUDED.verification_status,
    beneficiary_residency_scope = EXCLUDED.beneficiary_residency_scope,
    benefit_type = EXCLUDED.benefit_type,
    free_duration_minutes = EXCLUDED.free_duration_minutes,
    initial_rate_exempt_flag = EXCLUDED.initial_rate_exempt_flag,
    full_fee_exempt_flag = EXCLUDED.full_fee_exempt_flag,
    overnight_excluded_flag = EXCLUDED.overnight_excluded_flag,
    valet_excluded_flag = EXCLUDED.valet_excluded_flag,
    standalone_parking_excluded_flag = EXCLUDED.standalone_parking_excluded_flag,
    driver_or_passenger_required_flag = EXCLUDED.driver_or_passenger_required_flag,
    free_period_application = EXCLUDED.free_period_application,
    succeeding_hours_discount_rule = EXCLUDED.succeeding_hours_discount_rule,
    discount_base_scope = EXCLUDED.discount_base_scope,
    stacking_policy = EXCLUDED.stacking_policy,
    legal_basis_priority = EXCLUDED.legal_basis_priority,
    requires_operator_validation = EXCLUDED.requires_operator_validation,
    requires_evidence = EXCLUDED.requires_evidence,
    effective_from = EXCLUDED.effective_from,
    effective_to = EXCLUDED.effective_to,
    policy_status = EXCLUDED.policy_status,
    source_reference = EXCLUDED.source_reference,
    reviewed_at = EXCLUDED.reviewed_at,
    policy_snapshot_json = EXCLUDED.policy_snapshot_json,
    updated_at = now();
