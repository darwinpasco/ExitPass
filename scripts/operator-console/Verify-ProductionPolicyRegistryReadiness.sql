/*
    ExitPass Operator Console production policy registry readiness inspection.

    This script is for read-only readiness inspection of statutory discount policy
    configuration. It reports the live baseline table state without changing data.
*/

WITH registry_capability AS (
    SELECT
        to_regclass('discounts.discount_policy_references') IS NOT NULL AS discount_policy_references_available,
        to_regclass('discounts.statutory_discount_policy_registry') IS NOT NULL AS statutory_discount_policy_registry_available
)
SELECT
    'policy_registry_table_availability' AS check_name,
    discount_policy_references_available,
    statutory_discount_policy_registry_available,
    CASE
        WHEN statutory_discount_policy_registry_available THEN 'DEDICATED_REGISTRY_PRESENT'
        WHEN discount_policy_references_available THEN 'COMPATIBILITY_TABLE_ONLY'
        ELSE 'NOT_READY'
    END AS readiness_classification
FROM registry_capability;

WITH registry_capability AS (
    SELECT
        to_regclass('discounts.discount_policy_references') IS NOT NULL AS discount_policy_references_available,
        to_regclass('discounts.statutory_discount_policy_registry') IS NOT NULL AS statutory_discount_policy_registry_available
),
policy_query AS (
    SELECT
        CASE
            WHEN statutory_discount_policy_registry_available THEN $query$
                SELECT
                    'DEDICATED_REGISTRY'::text AS registry_source,
                    p.statutory_discount_policy_registry_id::text AS policy_id,
                    p.policy_code::text,
                    p.policy_name::text,
                    p.policy_description::text,
                    p.entitlement_type::text,
                    p.policy_status::text,
                    p.verification_status::text,
                    p.policy_level::text,
                    p.policy_type::text,
                    p.policy_resolution_basis::text,
                    p.benefit_type::text,
                    p.discount_base_scope::text,
                    p.jurisdiction_code::text AS lgu_code,
                    p.jurisdiction_name::text,
                    p.site_group_id::text,
                    p.site_id::text,
                    p.national_law_reference::text,
                    p.ordinance_reference::text AS local_ordinance_reference,
                    p.legal_basis_reference::text,
                    p.requires_operator_validation::text,
                    p.requires_evidence::text AS requires_evidence_capture,
                    p.required_evidence_type::text,
                    p.source_reference::text,
                    p.reviewed_at::text,
                    p.approved_at::text,
                    p.effective_from::text,
                    p.effective_to::text,
                    NULL::text AS policy_version
                FROM discounts.statutory_discount_policy_registry p
            $query$
            WHEN discount_policy_references_available THEN $query$
                SELECT
                    'COMPATIBILITY_TABLE'::text AS registry_source,
                    p.discount_policy_reference_id::text AS policy_id,
                    p.policy_code::text,
                    p.policy_name::text,
                    p.policy_description::text,
                    p.entitlement_type::text,
                    p.policy_status::text,
                    p.policy_status::text AS verification_status,
                    p.policy_level::text,
                    p.policy_type::text,
                    NULL::text AS policy_resolution_basis,
                    'STATUTORY_DISCOUNT_VAT_EXEMPT'::text AS benefit_type,
                    'VAT_EXCLUSIVE'::text AS discount_base_scope,
                    p.lgu_code::text,
                    p.jurisdiction_name::text,
                    p.site_group_id::text,
                    p.site_id::text,
                    p.national_law_reference::text,
                    p.local_ordinance_reference::text,
                    COALESCE(p.local_ordinance_reference, p.national_law_reference)::text AS legal_basis_reference,
                    p.requires_operator_validation::text,
                    p.requires_evidence_capture::text,
                    NULL::text AS required_evidence_type,
                    p.policy_version::text AS source_reference,
                    NULL::text AS reviewed_at,
                    NULL::text AS approved_at,
                    p.effective_from::text,
                    p.effective_to::text,
                    p.policy_version::text
                FROM discounts.discount_policy_references p
            $query$
            ELSE $query$
                SELECT
                    'NO_POLICY_SOURCE'::text AS registry_source,
                    NULL::text AS policy_id,
                    NULL::text AS policy_code,
                    NULL::text AS policy_name,
                    NULL::text AS policy_description,
                    NULL::text AS entitlement_type,
                    NULL::text AS policy_status,
                    NULL::text AS verification_status,
                    NULL::text AS policy_level,
                    NULL::text AS policy_type,
                    NULL::text AS policy_resolution_basis,
                    NULL::text AS benefit_type,
                    NULL::text AS discount_base_scope,
                    NULL::text AS lgu_code,
                    NULL::text AS jurisdiction_name,
                    NULL::text AS site_group_id,
                    NULL::text AS site_id,
                    NULL::text AS national_law_reference,
                    NULL::text AS local_ordinance_reference,
                    NULL::text AS legal_basis_reference,
                    NULL::text AS requires_operator_validation,
                    NULL::text AS requires_evidence_capture,
                    NULL::text AS required_evidence_type,
                    NULL::text AS source_reference,
                    NULL::text AS reviewed_at,
                    NULL::text AS approved_at,
                    NULL::text AS effective_from,
                    NULL::text AS effective_to,
                    NULL::text AS policy_version
                WHERE false
            $query$
        END AS sql_text
    FROM registry_capability
),
policy_rows AS (
    SELECT x.*
    FROM policy_query q
    CROSS JOIN XMLTABLE(
        '/table/row'
        PASSING query_to_xml(q.sql_text, false, false, '')
        COLUMNS
            registry_source text PATH 'registry_source',
            policy_id text PATH 'policy_id',
            policy_code text PATH 'policy_code',
            policy_name text PATH 'policy_name',
            policy_description text PATH 'policy_description',
            entitlement_type text PATH 'entitlement_type',
            policy_status text PATH 'policy_status',
            verification_status text PATH 'verification_status',
            policy_level text PATH 'policy_level',
            policy_type text PATH 'policy_type',
            policy_resolution_basis text PATH 'policy_resolution_basis',
            benefit_type text PATH 'benefit_type',
            discount_base_scope text PATH 'discount_base_scope',
            lgu_code text PATH 'lgu_code',
            jurisdiction_name text PATH 'jurisdiction_name',
            site_group_id text PATH 'site_group_id',
            site_id text PATH 'site_id',
            national_law_reference text PATH 'national_law_reference',
            local_ordinance_reference text PATH 'local_ordinance_reference',
            legal_basis_reference text PATH 'legal_basis_reference',
            requires_operator_validation text PATH 'requires_operator_validation',
            requires_evidence_capture text PATH 'requires_evidence_capture',
            required_evidence_type text PATH 'required_evidence_type',
            source_reference text PATH 'source_reference',
            reviewed_at text PATH 'reviewed_at',
            approved_at text PATH 'approved_at',
            effective_from timestamptz PATH 'effective_from',
            effective_to timestamptz PATH 'effective_to',
            policy_version text PATH 'policy_version'
    ) AS x
),
classified AS (
    SELECT
        pr.*,
        CASE
            WHEN pr.policy_code ILIKE '%SANDBOX%'
                OR pr.policy_code ILIKE '%TEST%'
                OR pr.policy_code ILIKE '%DEV%'
                OR pr.policy_code ILIKE '%E2E%'
                OR pr.policy_name ILIKE '%SANDBOX%'
                OR pr.policy_name ILIKE '%TEST%'
                OR pr.policy_name ILIKE '%DEV%'
                OR pr.policy_name ILIKE '%E2E%'
                OR pr.policy_description ILIKE '%sandbox%'
                OR pr.policy_description ILIKE '%test%'
                OR pr.policy_description ILIKE '%dev%'
                OR pr.source_reference ILIKE '%sandbox%'
                OR pr.source_reference ILIKE '%test%'
                OR pr.source_reference ILIKE '%dev%'
                THEN 'SANDBOX_ONLY'
            WHEN pr.policy_status <> 'ACTIVE'
                OR pr.effective_from > now()
                OR (pr.effective_to IS NOT NULL AND pr.effective_to < now())
                THEN 'EXPIRED_OR_INACTIVE'
            WHEN pr.requires_evidence_capture IS DISTINCT FROM 'true' THEN 'MISSING_EVIDENCE_RULE'
            WHEN pr.registry_source = 'DEDICATED_REGISTRY'
                AND pr.requires_evidence_capture = 'true'
                AND pr.required_evidence_type IS NULL
                THEN 'MISSING_EVIDENCE_RULE'
            WHEN pr.requires_operator_validation IS DISTINCT FROM 'true' THEN 'NOT_READY'
            WHEN pr.policy_level IN ('LOCAL_ORDINANCE', 'SITE_POLICY', 'OPERATIONAL_POLICY')
                AND pr.site_id IS NULL
                AND pr.site_group_id IS NULL
                AND pr.lgu_code IS NULL
                THEN 'MISSING_SITE_MAPPING'
            WHEN pr.policy_level IN ('LOCAL_ORDINANCE', 'SITE_POLICY', 'OPERATIONAL_POLICY')
                AND (
                    COALESCE(pr.local_ordinance_reference, pr.legal_basis_reference) IS NULL
                    OR COALESCE(pr.local_ordinance_reference, pr.legal_basis_reference) ILIKE 'DEV_PLACEHOLDER%'
                )
                THEN 'CONFIGURED_BUT_UNVERIFIED'
            WHEN pr.policy_level = 'NATIONAL_LAW'
                AND NOT (
                    (pr.entitlement_type = 'SENIOR_CITIZEN' AND pr.national_law_reference = 'RA 9994')
                    OR (pr.entitlement_type = 'PWD' AND pr.national_law_reference = 'RA 10754')
                )
                THEN 'CONFIGURED_BUT_UNVERIFIED'
            WHEN pr.registry_source = 'DEDICATED_REGISTRY'
                AND pr.verification_status IN ('LEAD_UNVERIFIED', 'VERIFIED_SECONDARY', 'PROPOSED_ONLY', 'REJECTED')
                THEN 'CONFIGURED_BUT_UNVERIFIED'
            WHEN pr.registry_source = 'DEDICATED_REGISTRY'
                AND pr.verification_status IN ('ACTIVE_APPROVED', 'VERIFIED_OFFICIAL')
                THEN 'READY_VERIFIED'
            ELSE 'READY_WITH_MANUAL_REVIEW'
        END AS readiness_classification
    FROM policy_rows pr
)
SELECT
    registry_source,
    policy_id,
    policy_code,
    entitlement_type,
    policy_status,
    verification_status,
    policy_level,
    policy_type,
    policy_resolution_basis,
    benefit_type,
    discount_base_scope,
    lgu_code,
    jurisdiction_name,
    site_group_id,
    site_id,
    national_law_reference,
    local_ordinance_reference,
    legal_basis_reference,
    requires_operator_validation,
    requires_evidence_capture,
    required_evidence_type,
    source_reference,
    reviewed_at,
    approved_at,
    effective_from,
    effective_to,
    readiness_classification
FROM classified
ORDER BY
    CASE readiness_classification
        WHEN 'SANDBOX_ONLY' THEN 0
        WHEN 'CONFIGURED_BUT_UNVERIFIED' THEN 1
        WHEN 'MISSING_REQUIRED_POLICY' THEN 2
        WHEN 'MISSING_SITE_MAPPING' THEN 3
        WHEN 'MISSING_EVIDENCE_RULE' THEN 4
        WHEN 'EXPIRED_OR_INACTIVE' THEN 5
        WHEN 'READY_WITH_MANUAL_REVIEW' THEN 6
        WHEN 'READY_VERIFIED' THEN 7
        ELSE 8
    END,
    policy_code;

WITH expected_entitlements AS (
    SELECT 'SENIOR_CITIZEN' AS entitlement_type, 'RA 9994' AS expected_national_law_reference
    UNION ALL
    SELECT 'PWD', 'RA 10754'
),
registry_capability AS (
    SELECT
        to_regclass('discounts.discount_policy_references') IS NOT NULL AS discount_policy_references_available,
        to_regclass('discounts.statutory_discount_policy_registry') IS NOT NULL AS statutory_discount_policy_registry_available
),
policy_query AS (
    SELECT
        CASE
            WHEN statutory_discount_policy_registry_available THEN $query$
                SELECT
                    'DEDICATED_REGISTRY'::text AS registry_source,
                    p.policy_code::text,
                    p.policy_name::text,
                    p.policy_description::text,
                    p.entitlement_type::text,
                    p.policy_status::text,
                    p.verification_status::text,
                    p.policy_level::text,
                    p.site_group_id::text,
                    p.site_id::text,
                    p.jurisdiction_code::text AS lgu_code,
                    p.national_law_reference::text,
                    p.requires_evidence::text AS requires_evidence_capture,
                    p.required_evidence_type::text,
                    p.requires_operator_validation::text,
                    p.effective_from::text,
                    p.effective_to::text
                FROM discounts.statutory_discount_policy_registry p
            $query$
            WHEN discount_policy_references_available THEN $query$
                SELECT
                    'COMPATIBILITY_TABLE'::text AS registry_source,
                    p.policy_code::text,
                    p.policy_name::text,
                    p.policy_description::text,
                    p.entitlement_type::text,
                    p.policy_status::text,
                    p.policy_status::text AS verification_status,
                    p.policy_level::text,
                    p.site_group_id::text,
                    p.site_id::text,
                    p.lgu_code::text,
                    p.national_law_reference::text,
                    p.requires_evidence_capture::text,
                    NULL::text AS required_evidence_type,
                    p.requires_operator_validation::text,
                    p.effective_from::text,
                    p.effective_to::text
                FROM discounts.discount_policy_references p
            $query$
            ELSE $query$
                SELECT
                    'NO_POLICY_SOURCE'::text AS registry_source,
                    NULL::text AS policy_code,
                    NULL::text AS policy_name,
                    NULL::text AS policy_description,
                    NULL::text AS entitlement_type,
                    NULL::text AS policy_status,
                    NULL::text AS verification_status,
                    NULL::text AS policy_level,
                    NULL::text AS site_group_id,
                    NULL::text AS site_id,
                    NULL::text AS lgu_code,
                    NULL::text AS national_law_reference,
                    NULL::text AS requires_evidence_capture,
                    NULL::text AS required_evidence_type,
                    NULL::text AS requires_operator_validation,
                    NULL::text AS effective_from,
                    NULL::text AS effective_to
                WHERE false
            $query$
        END AS sql_text
    FROM registry_capability
),
active_policies AS (
    SELECT x.*
    FROM policy_query q
    CROSS JOIN XMLTABLE(
        '/table/row'
        PASSING query_to_xml(q.sql_text, false, false, '')
        COLUMNS
            registry_source text PATH 'registry_source',
            policy_code text PATH 'policy_code',
            policy_name text PATH 'policy_name',
            policy_description text PATH 'policy_description',
            entitlement_type text PATH 'entitlement_type',
            policy_status text PATH 'policy_status',
            verification_status text PATH 'verification_status',
            policy_level text PATH 'policy_level',
            site_group_id text PATH 'site_group_id',
            site_id text PATH 'site_id',
            lgu_code text PATH 'lgu_code',
            national_law_reference text PATH 'national_law_reference',
            requires_evidence_capture text PATH 'requires_evidence_capture',
            required_evidence_type text PATH 'required_evidence_type',
            requires_operator_validation text PATH 'requires_operator_validation',
            effective_from timestamptz PATH 'effective_from',
            effective_to timestamptz PATH 'effective_to'
    ) AS x
    WHERE x.policy_status = 'ACTIVE'
      AND x.effective_from <= now()
      AND (x.effective_to IS NULL OR x.effective_to >= now())
      AND x.policy_code NOT ILIKE '%SANDBOX%'
      AND x.policy_code NOT ILIKE '%TEST%'
      AND x.policy_code NOT ILIKE '%DEV%'
      AND x.policy_code NOT ILIKE '%E2E%'
      AND x.policy_name NOT ILIKE '%SANDBOX%'
      AND x.policy_name NOT ILIKE '%TEST%'
      AND x.policy_name NOT ILIKE '%DEV%'
      AND x.policy_name NOT ILIKE '%E2E%'
),
coverage AS (
    SELECT
        e.entitlement_type,
        e.expected_national_law_reference,
        COUNT(a.policy_code) AS active_non_sandbox_policy_count,
        COUNT(a.policy_code) FILTER (WHERE a.policy_level = 'NATIONAL_LAW') AS national_fallback_count,
        COUNT(a.policy_code) FILTER (WHERE a.policy_level IN ('LOCAL_ORDINANCE', 'SITE_POLICY', 'OPERATIONAL_POLICY')) AS local_or_site_policy_count,
        BOOL_OR(a.national_law_reference = e.expected_national_law_reference) AS expected_national_fallback_reference_present,
        BOOL_AND(COALESCE(a.requires_evidence_capture = 'true', false)) AS all_rows_require_evidence,
        BOOL_AND(
            CASE
                WHEN a.registry_source = 'DEDICATED_REGISTRY' AND a.requires_evidence_capture = 'true'
                    THEN a.required_evidence_type IS NOT NULL
                ELSE true
            END
        ) AS all_dedicated_rows_have_required_evidence_type,
        BOOL_AND(COALESCE(a.requires_operator_validation = 'true', false)) AS all_rows_require_operator_validation,
        BOOL_OR(a.registry_source = 'DEDICATED_REGISTRY' AND a.verification_status IN ('ACTIVE_APPROVED', 'VERIFIED_OFFICIAL')) AS dedicated_verified_policy_present
    FROM expected_entitlements e
    LEFT JOIN active_policies a
      ON a.entitlement_type = e.entitlement_type
    GROUP BY e.entitlement_type, e.expected_national_law_reference
)
SELECT
    entitlement_type,
    active_non_sandbox_policy_count,
    national_fallback_count,
    local_or_site_policy_count,
    expected_national_fallback_reference_present,
    all_rows_require_evidence,
    all_dedicated_rows_have_required_evidence_type,
    all_rows_require_operator_validation,
    dedicated_verified_policy_present,
    CASE
        WHEN active_non_sandbox_policy_count = 0 THEN 'MISSING_REQUIRED_POLICY'
        WHEN expected_national_fallback_reference_present IS NOT TRUE THEN 'CONFIGURED_BUT_UNVERIFIED'
        WHEN all_rows_require_evidence IS NOT TRUE THEN 'MISSING_EVIDENCE_RULE'
        WHEN all_dedicated_rows_have_required_evidence_type IS NOT TRUE THEN 'MISSING_EVIDENCE_RULE'
        WHEN all_rows_require_operator_validation IS NOT TRUE THEN 'NOT_READY'
        WHEN dedicated_verified_policy_present IS TRUE THEN 'READY_VERIFIED'
        ELSE 'READY_WITH_MANUAL_REVIEW'
    END AS readiness_classification
FROM coverage
ORDER BY entitlement_type;

WITH registry_capability AS (
    SELECT
        to_regclass('discounts.discount_policy_references') IS NOT NULL AS discount_policy_references_available,
        to_regclass('discounts.statutory_discount_policy_registry') IS NOT NULL AS statutory_discount_policy_registry_available
),
policy_query AS (
    SELECT
        CASE
            WHEN statutory_discount_policy_registry_available THEN $query$
                SELECT
                    'DEDICATED_REGISTRY'::text AS registry_source,
                    p.policy_code::text,
                    p.policy_name::text,
                    p.policy_description::text,
                    p.entitlement_type::text,
                    p.policy_status::text,
                    p.policy_level::text,
                    p.site_group_id::text,
                    p.site_id::text,
                    p.jurisdiction_code::text AS lgu_code,
                    COALESCE(p.ordinance_reference, p.national_law_reference, p.legal_basis_reference)::text AS legal_reference,
                    p.requires_evidence::text AS requires_evidence_capture,
                    p.required_evidence_type::text,
                    p.requires_operator_validation::text,
                    p.effective_from::text,
                    p.effective_to::text
                FROM discounts.statutory_discount_policy_registry p
            $query$
            WHEN discount_policy_references_available THEN $query$
                SELECT
                    'COMPATIBILITY_TABLE'::text AS registry_source,
                    p.policy_code::text,
                    p.policy_name::text,
                    p.policy_description::text,
                    p.entitlement_type::text,
                    p.policy_status::text,
                    p.policy_level::text,
                    p.site_group_id::text,
                    p.site_id::text,
                    p.lgu_code::text,
                    COALESCE(p.local_ordinance_reference, p.national_law_reference)::text AS legal_reference,
                    p.requires_evidence_capture::text,
                    NULL::text AS required_evidence_type,
                    p.requires_operator_validation::text,
                    p.effective_from::text,
                    p.effective_to::text
                FROM discounts.discount_policy_references p
            $query$
            ELSE $query$
                SELECT
                    'NO_POLICY_SOURCE'::text AS registry_source,
                    NULL::text AS policy_code,
                    NULL::text AS policy_name,
                    NULL::text AS policy_description,
                    NULL::text AS entitlement_type,
                    NULL::text AS policy_status,
                    NULL::text AS policy_level,
                    NULL::text AS site_group_id,
                    NULL::text AS site_id,
                    NULL::text AS lgu_code,
                    NULL::text AS legal_reference,
                    NULL::text AS requires_evidence_capture,
                    NULL::text AS required_evidence_type,
                    NULL::text AS requires_operator_validation,
                    NULL::text AS effective_from,
                    NULL::text AS effective_to
                WHERE false
            $query$
        END AS sql_text
    FROM registry_capability
),
policy_rows AS (
    SELECT x.*
    FROM policy_query q
    CROSS JOIN XMLTABLE(
        '/table/row'
        PASSING query_to_xml(q.sql_text, false, false, '')
        COLUMNS
            registry_source text PATH 'registry_source',
            policy_code text PATH 'policy_code',
            policy_name text PATH 'policy_name',
            policy_description text PATH 'policy_description',
            entitlement_type text PATH 'entitlement_type',
            policy_status text PATH 'policy_status',
            policy_level text PATH 'policy_level',
            site_group_id text PATH 'site_group_id',
            site_id text PATH 'site_id',
            lgu_code text PATH 'lgu_code',
            legal_reference text PATH 'legal_reference',
            requires_evidence_capture text PATH 'requires_evidence_capture',
            required_evidence_type text PATH 'required_evidence_type',
            requires_operator_validation text PATH 'requires_operator_validation',
            effective_from timestamptz PATH 'effective_from',
            effective_to timestamptz PATH 'effective_to'
    ) AS x
)
SELECT
    'policy_readiness_summary' AS check_name,
    COALESCE((SELECT registry_source FROM policy_rows LIMIT 1), 'NO_POLICY_SOURCE') AS active_registry_source,
    COUNT(*) AS total_rows,
    COUNT(*) FILTER (WHERE policy_status = 'ACTIVE') AS active_rows,
    COUNT(*) FILTER (
        WHERE policy_code ILIKE '%SANDBOX%'
           OR policy_code ILIKE '%TEST%'
           OR policy_code ILIKE '%DEV%'
           OR policy_code ILIKE '%E2E%'
           OR policy_name ILIKE '%SANDBOX%'
           OR policy_name ILIKE '%TEST%'
           OR policy_name ILIKE '%DEV%'
           OR policy_name ILIKE '%E2E%'
           OR policy_description ILIKE '%sandbox%'
           OR policy_description ILIKE '%test%'
           OR policy_description ILIKE '%dev%'
    ) AS sandbox_or_dev_rows,
    COUNT(*) FILTER (WHERE entitlement_type = 'SENIOR_CITIZEN') AS senior_citizen_rows,
    COUNT(*) FILTER (WHERE entitlement_type = 'PWD') AS pwd_rows,
    COUNT(*) FILTER (WHERE requires_evidence_capture IS DISTINCT FROM 'true') AS rows_missing_evidence_rule,
    COUNT(*) FILTER (
        WHERE registry_source = 'DEDICATED_REGISTRY'
          AND requires_evidence_capture = 'true'
          AND required_evidence_type IS NULL
    ) AS dedicated_rows_missing_required_evidence_type,
    COUNT(*) FILTER (WHERE requires_operator_validation IS DISTINCT FROM 'true') AS rows_missing_operator_validation,
    COUNT(*) FILTER (
        WHERE policy_level IN ('LOCAL_ORDINANCE', 'SITE_POLICY', 'OPERATIONAL_POLICY')
          AND site_id IS NULL
          AND site_group_id IS NULL
          AND lgu_code IS NULL
    ) AS local_or_site_rows_missing_scope,
    COUNT(*) FILTER (
        WHERE legal_reference IS NULL
           OR legal_reference ILIKE 'DEV_PLACEHOLDER%'
    ) AS rows_missing_production_legal_reference,
    COUNT(*) FILTER (
        WHERE policy_status <> 'ACTIVE'
           OR effective_from > now()
           OR (effective_to IS NOT NULL AND effective_to < now())
    ) AS inactive_or_expired_rows
FROM policy_rows;
