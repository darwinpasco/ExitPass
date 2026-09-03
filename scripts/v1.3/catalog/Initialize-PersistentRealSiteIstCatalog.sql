\set ON_ERROR_STOP on

BEGIN;

CREATE SCHEMA IF NOT EXISTS ist_configuration;

CREATE TABLE IF NOT EXISTS ist_configuration.real_site_catalog_members (
    site_id uuid PRIMARY KEY REFERENCES sites.sites (site_id),
    site_code text NOT NULL UNIQUE,
    source_workbook text NOT NULL,
    source_sheet text NOT NULL,
    source_row integer NOT NULL,
    source_manifest_sha256 text NOT NULL,
    initialized_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_real_site_catalog_members__site_code_not_blank CHECK (btrim(site_code) <> ''),
    CONSTRAINT ck_real_site_catalog_members__manifest_hash CHECK (source_manifest_sha256 ~ '^[0-9a-f]{64}$')
);

CREATE TABLE IF NOT EXISTS ist_configuration.statutory_coverage_register (
    jurisdiction_id uuid NOT NULL REFERENCES sites.jurisdictions (jurisdiction_id),
    jurisdiction_code text NOT NULL,
    jurisdiction_display_name text NOT NULL,
    entitlement_type text NOT NULL,
    parking_policy_identified boolean NOT NULL,
    benefit_type text NULL,
    free_period_minutes integer NULL,
    discount_percent numeric(7,4) NULL,
    residency_scope text NULL,
    ordinance_or_authority_reference text NULL,
    ordinance_number_status text NOT NULL,
    source_quality_classification text NOT NULL,
    operational_verification_status text NOT NULL,
    legal_review_status text NOT NULL,
    runtime_publication_eligibility text NOT NULL,
    manual_review_required boolean NOT NULL,
    source_reference text NOT NULL,
    notes text NULL,
    source_manifest_sha256 text NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (jurisdiction_id, entitlement_type),
    CONSTRAINT ck_statutory_coverage_register__entitlement CHECK (entitlement_type IN ('SENIOR_CITIZEN', 'PWD')),
    CONSTRAINT ck_statutory_coverage_register__unknown_free_period CHECK (free_period_minutes IS NULL OR free_period_minutes > 0),
    CONSTRAINT ck_statutory_coverage_register__unknown_discount CHECK (discount_percent IS NULL OR discount_percent > 0),
    CONSTRAINT ck_statutory_coverage_register__manifest_hash CHECK (source_manifest_sha256 ~ '^[0-9a-f]{64}$')
);

CREATE TABLE IF NOT EXISTS ist_configuration.site_operational_capabilities (
    site_id uuid PRIMARY KEY REFERENCES ist_configuration.real_site_catalog_members (site_id),
    hikcentral_target_configured boolean NOT NULL DEFAULT false,
    hikcentral_connectivity_verified boolean NOT NULL DEFAULT false,
    fiscal_merchant_configured boolean NOT NULL DEFAULT false,
    fiscal_supplier_configured boolean NOT NULL DEFAULT false,
    fiscal_profile_approved boolean NOT NULL DEFAULT false,
    paymongo_enabled boolean NOT NULL DEFAULT false,
    last_verified_at timestamptz NULL,
    verification_reference text NULL,
    CONSTRAINT ck_site_operational_capabilities__connectivity_requires_target
        CHECK (NOT hikcentral_connectivity_verified OR hikcentral_target_configured),
    CONSTRAINT ck_site_operational_capabilities__approved_fiscal_complete
        CHECK (NOT fiscal_profile_approved OR (fiscal_merchant_configured AND fiscal_supplier_configured))
);

DO $$
BEGIN
    IF (SELECT count(*) FROM ep_ist_groups) <> 39 THEN
        RAISE EXCEPTION 'Persistent IST initializer requires exactly 39 reviewed Site Groups.';
    END IF;
    IF (SELECT count(*) FROM ep_ist_sites) <> 46 THEN
        RAISE EXCEPTION 'Persistent IST initializer requires exactly 46 reviewed Sites.';
    END IF;
    IF (SELECT count(*) FROM ep_ist_assignments) <> 46 THEN
        RAISE EXCEPTION 'Persistent IST initializer requires exactly 46 reviewed jurisdiction assignments.';
    END IF;
    IF (SELECT count(*) FROM ep_ist_coverage) <> 26 THEN
        RAISE EXCEPTION 'Persistent IST initializer requires exactly 26 jurisdiction/entitlement coverage rows.';
    END IF;
    IF EXISTS (SELECT 1 FROM ep_ist_groups GROUP BY site_group_id HAVING count(*) > 1)
       OR EXISTS (SELECT 1 FROM ep_ist_groups GROUP BY site_group_code HAVING count(*) > 1)
       OR EXISTS (SELECT 1 FROM ep_ist_sites GROUP BY site_id HAVING count(*) > 1)
       OR EXISTS (SELECT 1 FROM ep_ist_sites GROUP BY site_code HAVING count(*) > 1)
       OR EXISTS (SELECT 1 FROM ep_ist_assignments GROUP BY site_id HAVING count(*) > 1)
       OR EXISTS (SELECT 1 FROM ep_ist_coverage GROUP BY jurisdiction_id, entitlement_type HAVING count(*) > 1) THEN
        RAISE EXCEPTION 'Persistent IST initializer input contains duplicate stable identities.';
    END IF;
    IF EXISTS (
        SELECT 1
        FROM ep_ist_sites expected
        LEFT JOIN sites.sites actual ON actual.site_id = expected.site_id
        WHERE actual.site_id IS NULL
           OR actual.site_code <> expected.site_code
           OR actual.site_group_id <> expected.site_group_id
           OR actual.timezone_name <> 'Asia/Manila'
           OR actual.country_code <> 'PH'
    ) THEN
        RAISE EXCEPTION 'Canonical Site seed is missing or differs from the reviewed manifest.';
    END IF;
    IF EXISTS (
        SELECT 1
        FROM ep_ist_assignments expected
        LEFT JOIN sites.site_jurisdiction_assignments actual
          ON actual.site_jurisdiction_assignment_id = expected.assignment_id
        WHERE actual.site_jurisdiction_assignment_id IS NULL
           OR actual.site_id <> expected.site_id
           OR actual.jurisdiction_id <> expected.jurisdiction_id
    ) THEN
        RAISE EXCEPTION 'Canonical jurisdiction seed is missing or differs from the reviewed manifest.';
    END IF;
END $$;

UPDATE sites.site_groups actual
SET site_group_status = 'ACTIVE',
    updated_at = now(),
    row_version = CASE WHEN actual.site_group_status = 'ACTIVE' THEN actual.row_version ELSE actual.row_version + 1 END
FROM ep_ist_groups expected
WHERE actual.site_group_id = expected.site_group_id
  AND actual.site_group_status <> 'ACTIVE';

UPDATE sites.sites actual
SET site_status = 'ACTIVE',
    updated_at = now(),
    row_version = CASE WHEN actual.site_status = 'ACTIVE' THEN actual.row_version ELSE actual.row_version + 1 END
FROM ep_ist_sites expected
WHERE actual.site_id = expected.site_id
  AND actual.site_status <> 'ACTIVE';

UPDATE sites.site_jurisdiction_assignments actual
SET assignment_status = 'ACTIVE',
    approval_reference = 'PERSISTENT_REAL_SITE_IST_CATALOG_V1',
    updated_at = now(),
    row_version = CASE WHEN actual.assignment_status = 'ACTIVE' AND actual.approval_reference = 'PERSISTENT_REAL_SITE_IST_CATALOG_V1'
                       THEN actual.row_version ELSE actual.row_version + 1 END
FROM ep_ist_assignments expected
WHERE actual.site_jurisdiction_assignment_id = expected.assignment_id
  AND (actual.assignment_status <> 'ACTIVE'
       OR actual.approval_reference IS DISTINCT FROM 'PERSISTENT_REAL_SITE_IST_CATALOG_V1');

-- The reviewed v1.0 CSV/seed artifacts preserve stable IDs but contain two UTF-8
-- Philippine names decoded as Latin-1. Normalize display text without changing identity.
UPDATE sites.jurisdictions
SET display_name = convert_from(convert_to(display_name, 'LATIN1'), 'UTF8'),
    updated_at = now()
WHERE position('Ã' in display_name) > 0;

UPDATE sites.sites site
SET city = jurisdiction.display_name,
    updated_at = now()
FROM ep_ist_assignments expected
JOIN sites.jurisdictions jurisdiction ON jurisdiction.jurisdiction_id = expected.jurisdiction_id
WHERE site.site_id = expected.site_id
  AND site.city IS DISTINCT FROM jurisdiction.display_name;

INSERT INTO ist_configuration.real_site_catalog_members (
    site_id, site_code, source_workbook, source_sheet, source_row, source_manifest_sha256)
SELECT site_id, site_code, source_workbook, source_sheet, source_row, source_manifest_sha256
FROM ep_ist_sites
ON CONFLICT (site_id) DO UPDATE
SET site_code = EXCLUDED.site_code,
    source_workbook = EXCLUDED.source_workbook,
    source_sheet = EXCLUDED.source_sheet,
    source_row = EXCLUDED.source_row,
    source_manifest_sha256 = EXCLUDED.source_manifest_sha256;

INSERT INTO ist_configuration.site_operational_capabilities (site_id)
SELECT site_id FROM ep_ist_sites
ON CONFLICT (site_id) DO NOTHING;

INSERT INTO ist_configuration.statutory_coverage_register (
    jurisdiction_id, jurisdiction_code, jurisdiction_display_name, entitlement_type,
    parking_policy_identified, benefit_type, free_period_minutes, discount_percent,
    residency_scope, ordinance_or_authority_reference, ordinance_number_status,
    source_quality_classification, operational_verification_status, legal_review_status,
    runtime_publication_eligibility, manual_review_required, source_reference, notes,
    source_manifest_sha256)
SELECT jurisdiction_id, jurisdiction_code,
       CASE WHEN position('Ã' in jurisdiction_display_name) > 0
            THEN convert_from(convert_to(jurisdiction_display_name, 'LATIN1'), 'UTF8')
            ELSE jurisdiction_display_name END,
       entitlement_type,
       parking_policy_identified, NULLIF(benefit_type, ''), NULLIF(free_period_minutes, '')::integer,
       NULLIF(discount_percent, '')::numeric, NULLIF(residency_scope, ''), NULLIF(ordinance_or_authority_reference, ''),
       ordinance_number_status, source_quality_classification, operational_verification_status,
       legal_review_status, runtime_publication_eligibility, manual_review_required,
       source_reference, NULLIF(notes, ''), source_manifest_sha256
FROM ep_ist_coverage
ON CONFLICT (jurisdiction_id, entitlement_type) DO UPDATE
SET jurisdiction_code = EXCLUDED.jurisdiction_code,
    jurisdiction_display_name = EXCLUDED.jurisdiction_display_name,
    parking_policy_identified = EXCLUDED.parking_policy_identified,
    benefit_type = EXCLUDED.benefit_type,
    free_period_minutes = EXCLUDED.free_period_minutes,
    discount_percent = EXCLUDED.discount_percent,
    residency_scope = EXCLUDED.residency_scope,
    ordinance_or_authority_reference = EXCLUDED.ordinance_or_authority_reference,
    ordinance_number_status = EXCLUDED.ordinance_number_status,
    source_quality_classification = EXCLUDED.source_quality_classification,
    operational_verification_status = EXCLUDED.operational_verification_status,
    legal_review_status = EXCLUDED.legal_review_status,
    runtime_publication_eligibility = EXCLUDED.runtime_publication_eligibility,
    manual_review_required = EXCLUDED.manual_review_required,
    source_reference = EXCLUDED.source_reference,
    notes = EXCLUDED.notes,
    source_manifest_sha256 = EXCLUDED.source_manifest_sha256,
    updated_at = now();

-- Parañaque availability is project-confirmed, but entitlement remains operator-reviewed.
UPDATE discounts.statutory_discount_policy_registry
SET policy_status = 'ACTIVE',
    auto_application_allowed = false,
    requires_operator_validation = true,
    updated_at = now()
WHERE local_government_unit_id = 'f7a1b4b9-17a9-89de-5059-f72779616f23'::uuid
  AND entitlement_type IN ('SENIOR_CITIZEN', 'PWD')
  AND verification_status = 'VERIFIED_ACTIVE_OPERATIONAL';

UPDATE discounts.statutory_discount_policy_registry policy
SET jurisdiction_name = jurisdiction.display_name,
    updated_at = now()
FROM sites.jurisdictions jurisdiction
WHERE policy.local_government_unit_id = jurisdiction.jurisdiction_id
  AND policy.jurisdiction_name IS DISTINCT FROM jurisdiction.display_name;

UPDATE discounts.statutory_discount_policy_registry_lgu_scopes scope
SET scope_status = 'ACTIVE',
    auto_application_allowed = false,
    updated_at = now()
FROM discounts.statutory_discount_policy_registry policy
WHERE policy.statutory_discount_policy_registry_id = scope.statutory_discount_policy_registry_id
  AND policy.local_government_unit_id = 'f7a1b4b9-17a9-89de-5059-f72779616f23'::uuid
  AND policy.entitlement_type IN ('SENIOR_CITIZEN', 'PWD')
  AND policy.verification_status = 'VERIFIED_ACTIVE_OPERATIONAL';

CREATE OR REPLACE VIEW ist_configuration.real_site_readiness AS
WITH policy AS (
    SELECT jurisdiction_id,
           max(CASE WHEN entitlement_type = 'SENIOR_CITIZEN' THEN
               CASE WHEN operational_verification_status = 'VERIFIED_ACTIVE_OPERATIONAL' THEN 'ACTIVE_MANUAL_REVIEW'
                    WHEN parking_policy_identified THEN 'RESEARCH_COVERAGE_REVIEW_REQUIRED'
                    ELSE 'NO_LOCAL_POLICY_IDENTIFIED' END END) AS senior_policy_status,
           max(CASE WHEN entitlement_type = 'PWD' THEN
               CASE WHEN operational_verification_status = 'VERIFIED_ACTIVE_OPERATIONAL' THEN 'ACTIVE_MANUAL_REVIEW'
                    WHEN parking_policy_identified THEN 'RESEARCH_COVERAGE_REVIEW_REQUIRED'
                    ELSE 'NO_LOCAL_POLICY_IDENTIFIED' END END) AS pwd_policy_status
    FROM ist_configuration.statutory_coverage_register
    GROUP BY jurisdiction_id
)
SELECT member.site_id,
       member.site_code,
       site.site_name,
       site.site_group_id,
       site_group.site_group_name,
       jurisdiction.jurisdiction_code,
       jurisdiction.display_name AS jurisdiction,
       site.site_status = 'ACTIVE' AS site_exists_active,
       site_group.site_group_status = 'ACTIVE' AS site_group_exists_active,
       assignment.assignment_status = 'ACTIVE' AS jurisdiction_active,
       policy.senior_policy_status,
       policy.pwd_policy_status,
       capability.hikcentral_target_configured,
       capability.hikcentral_connectivity_verified,
       site.public_lookup_enabled AS webpay_public_lookup_enabled,
       site.payment_enabled AS webpay_payment_enabled,
       capability.fiscal_merchant_configured,
       capability.fiscal_supplier_configured,
       capability.fiscal_profile_approved,
       capability.paymongo_enabled,
       CASE
         WHEN site.site_status = 'ACTIVE'
          AND site_group.site_group_status = 'ACTIVE'
          AND assignment.assignment_status = 'ACTIVE'
          AND capability.hikcentral_connectivity_verified
          AND site.public_lookup_enabled
          AND site.payment_enabled
          AND capability.fiscal_profile_approved
          AND capability.paymongo_enabled THEN 'READY'
         WHEN site.site_status = 'ACTIVE'
          AND site_group.site_group_status = 'ACTIVE'
          AND assignment.assignment_status = 'ACTIVE' THEN 'PARTIALLY_CONFIGURED'
         ELSE 'CONFIGURATION_REQUIRED'
       END AS final_test_readiness
FROM ist_configuration.real_site_catalog_members member
JOIN sites.sites site ON site.site_id = member.site_id
JOIN sites.site_groups site_group ON site_group.site_group_id = site.site_group_id
JOIN sites.site_jurisdiction_assignments assignment
  ON assignment.site_id = site.site_id
 AND assignment.assignment_status = 'ACTIVE'
 AND assignment.effective_to IS NULL
JOIN sites.jurisdictions jurisdiction ON jurisdiction.jurisdiction_id = assignment.jurisdiction_id
JOIN ist_configuration.site_operational_capabilities capability ON capability.site_id = site.site_id
LEFT JOIN policy ON policy.jurisdiction_id = jurisdiction.jurisdiction_id;

CREATE OR REPLACE FUNCTION ist_configuration.resolve_real_site(p_site_code text)
RETURNS TABLE (
    site_id uuid,
    site_code text,
    site_name text,
    site_group_id uuid,
    site_group_name text,
    jurisdiction_id uuid,
    jurisdiction_code text,
    jurisdiction_name text
)
LANGUAGE sql
STABLE
STRICT
AS $$
    SELECT site.site_id, site.site_code::text, site.site_name::text,
           site_group.site_group_id, site_group.site_group_name::text,
           jurisdiction.jurisdiction_id, jurisdiction.jurisdiction_code::text,
           jurisdiction.display_name::text
    FROM ist_configuration.real_site_catalog_members member
    JOIN sites.sites site ON site.site_id = member.site_id
    JOIN sites.site_groups site_group ON site_group.site_group_id = site.site_group_id
    JOIN sites.site_jurisdiction_assignments assignment
      ON assignment.site_id = site.site_id
     AND assignment.assignment_status = 'ACTIVE'
     AND assignment.effective_to IS NULL
    JOIN sites.jurisdictions jurisdiction ON jurisdiction.jurisdiction_id = assignment.jurisdiction_id
    WHERE site.site_code = upper(btrim(p_site_code));
$$;

DO $$
BEGIN
    IF (SELECT count(*) FROM ist_configuration.real_site_catalog_members) <> 46
       OR (SELECT count(*) FROM ist_configuration.real_site_readiness) <> 46
       OR (SELECT count(*) FROM sites.site_groups group_row JOIN ep_ist_groups expected USING (site_group_id) WHERE group_row.site_group_status = 'ACTIVE') <> 39
       OR (SELECT count(*) FROM sites.site_jurisdiction_assignments assignment JOIN ep_ist_assignments expected ON expected.assignment_id = assignment.site_jurisdiction_assignment_id WHERE assignment.assignment_status = 'ACTIVE' AND assignment.effective_to IS NULL) <> 46
       OR (SELECT count(DISTINCT jurisdiction_id) FROM ep_ist_assignments) <> 13 THEN
        RAISE EXCEPTION 'Persistent real-Site IST catalog failed final cardinality validation.';
    END IF;
    IF EXISTS (
        SELECT 1 FROM ist_configuration.real_site_readiness
        WHERE NOT site_exists_active OR NOT site_group_exists_active OR NOT jurisdiction_active
    ) THEN
        RAISE EXCEPTION 'Persistent real-Site IST catalog contains an inactive catalog member.';
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM ist_configuration.resolve_real_site('PITX-LEVEL-3')
        WHERE site_id = '2d1dcdf8-f563-537c-8542-0bde7cc9da97'::uuid
          AND jurisdiction_id = 'f7a1b4b9-17a9-89de-5059-f72779616f23'::uuid
    ) THEN
        RAISE EXCEPTION 'PITX Level 3 stable identity or jurisdiction changed.';
    END IF;
END $$;

COMMIT;
