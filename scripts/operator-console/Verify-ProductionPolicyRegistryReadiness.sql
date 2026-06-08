/*
    ExitPass Operator Console production policy registry readiness inspection.

    This script is for read-only readiness inspection of statutory discount policy
    configuration. It reports the live baseline table state without changing data.
*/

SELECT
    'policy_registry_table_availability' AS check_name,
    to_regclass('discounts.discount_policy_references') IS NOT NULL AS discount_policy_references_available,
    to_regclass('discounts.statutory_discount_policy_registry') IS NOT NULL AS statutory_discount_policy_registry_available,
    CASE
        WHEN to_regclass('discounts.statutory_discount_policy_registry') IS NOT NULL THEN 'DEDICATED_REGISTRY_AVAILABLE'
        WHEN to_regclass('discounts.discount_policy_references') IS NOT NULL THEN 'COMPATIBILITY_TABLE_ONLY'
        ELSE 'NOT_READY'
    END AS readiness_classification;

WITH policy_rows AS (
    SELECT
        p.discount_policy_reference_id,
        p.policy_code,
        p.policy_name,
        p.policy_description,
        p.entitlement_type::text AS entitlement_type,
        p.policy_status::text AS policy_status,
        p.policy_level::text AS policy_level,
        p.policy_type::text AS policy_type,
        p.lgu_code,
        p.jurisdiction_name,
        p.site_group_id,
        p.site_id,
        p.national_law_reference,
        p.local_ordinance_reference,
        p.requires_operator_validation,
        p.requires_evidence_capture,
        p.evidence_retention_policy_code,
        p.effective_from,
        p.effective_to,
        p.policy_version
    FROM discounts.discount_policy_references p
),
classified AS (
    SELECT
        pr.*,
        CASE
            WHEN pr.policy_code ILIKE '%SANDBOX%'
                OR pr.policy_code ILIKE '%TEST%'
                OR pr.policy_code ILIKE '%DEV%'
                OR pr.policy_name ILIKE '%SANDBOX%'
                OR pr.policy_name ILIKE '%TEST%'
                OR pr.policy_name ILIKE '%DEV%'
                OR pr.policy_description ILIKE '%sandbox%'
                OR pr.policy_description ILIKE '%test%'
                OR pr.policy_description ILIKE '%dev%'
                THEN 'SANDBOX_ONLY'
            WHEN pr.policy_status <> 'ACTIVE'
                OR pr.effective_from > now()
                OR (pr.effective_to IS NOT NULL AND pr.effective_to < now())
                THEN 'EXPIRED_OR_INACTIVE'
            WHEN pr.requires_evidence_capture IS NOT TRUE THEN 'MISSING_EVIDENCE_RULE'
            WHEN pr.requires_operator_validation IS NOT TRUE THEN 'NOT_READY'
            WHEN pr.policy_level IN ('LOCAL_ORDINANCE', 'SITE_POLICY', 'OPERATIONAL_POLICY')
                AND pr.site_id IS NULL
                AND pr.site_group_id IS NULL
                AND pr.lgu_code IS NULL
                THEN 'MISSING_SITE_MAPPING'
            WHEN pr.policy_level IN ('LOCAL_ORDINANCE', 'SITE_POLICY', 'OPERATIONAL_POLICY')
                AND (pr.local_ordinance_reference IS NULL OR pr.local_ordinance_reference ILIKE 'DEV_PLACEHOLDER%')
                THEN 'CONFIGURED_BUT_UNVERIFIED'
            WHEN pr.policy_level = 'NATIONAL_LAW'
                AND NOT (
                    (pr.entitlement_type = 'SENIOR_CITIZEN' AND pr.national_law_reference = 'RA 9994')
                    OR (pr.entitlement_type = 'PWD' AND pr.national_law_reference = 'RA 10754')
                )
                THEN 'CONFIGURED_BUT_UNVERIFIED'
            WHEN pr.policy_level = 'NATIONAL_LAW' THEN 'READY_WITH_MANUAL_REVIEW'
            ELSE 'READY_WITH_MANUAL_REVIEW'
        END AS readiness_classification
    FROM policy_rows pr
)
SELECT
    discount_policy_reference_id AS policy_id,
    policy_code,
    entitlement_type,
    policy_status,
    policy_level,
    policy_type,
    lgu_code,
    jurisdiction_name,
    site_group_id,
    site_id,
    national_law_reference,
    local_ordinance_reference,
    requires_operator_validation,
    requires_evidence_capture,
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
        ELSE 7
    END,
    policy_code;

WITH expected_entitlements AS (
    SELECT 'SENIOR_CITIZEN' AS entitlement_type, 'RA 9994' AS expected_national_law_reference
    UNION ALL
    SELECT 'PWD', 'RA 10754'
),
active_policies AS (
    SELECT
        p.entitlement_type::text AS entitlement_type,
        p.policy_level::text AS policy_level,
        p.policy_code,
        p.national_law_reference,
        p.local_ordinance_reference,
        p.requires_evidence_capture,
        p.requires_operator_validation,
        p.effective_from,
        p.effective_to
    FROM discounts.discount_policy_references p
    WHERE p.policy_status = 'ACTIVE'
      AND p.effective_from <= now()
      AND (p.effective_to IS NULL OR p.effective_to >= now())
      AND p.policy_code NOT ILIKE '%SANDBOX%'
      AND p.policy_code NOT ILIKE '%TEST%'
      AND p.policy_code NOT ILIKE '%DEV%'
)
SELECT
    e.entitlement_type,
    COUNT(a.policy_code) AS active_non_sandbox_policy_count,
    COUNT(a.policy_code) FILTER (WHERE a.policy_level = 'NATIONAL_LAW') AS national_fallback_count,
    COUNT(a.policy_code) FILTER (WHERE a.policy_level IN ('LOCAL_ORDINANCE', 'SITE_POLICY', 'OPERATIONAL_POLICY')) AS local_or_site_policy_count,
    BOOL_OR(a.national_law_reference = e.expected_national_law_reference) AS expected_national_fallback_reference_present,
    BOOL_AND(COALESCE(a.requires_evidence_capture, false)) AS all_rows_require_evidence,
    BOOL_AND(COALESCE(a.requires_operator_validation, false)) AS all_rows_require_operator_validation,
    CASE
        WHEN COUNT(a.policy_code) = 0 THEN 'MISSING_REQUIRED_POLICY'
        WHEN BOOL_OR(a.national_law_reference = e.expected_national_law_reference) IS NOT TRUE THEN 'CONFIGURED_BUT_UNVERIFIED'
        WHEN BOOL_AND(COALESCE(a.requires_evidence_capture, false)) IS NOT TRUE THEN 'MISSING_EVIDENCE_RULE'
        WHEN BOOL_AND(COALESCE(a.requires_operator_validation, false)) IS NOT TRUE THEN 'NOT_READY'
        ELSE 'READY_WITH_MANUAL_REVIEW'
    END AS readiness_classification
FROM expected_entitlements e
LEFT JOIN active_policies a
  ON a.entitlement_type = e.entitlement_type
GROUP BY e.entitlement_type, e.expected_national_law_reference
ORDER BY e.entitlement_type;

SELECT
    'policy_readiness_summary' AS check_name,
    COUNT(*) AS total_rows,
    COUNT(*) FILTER (WHERE policy_status = 'ACTIVE') AS active_rows,
    COUNT(*) FILTER (
        WHERE policy_code ILIKE '%SANDBOX%'
           OR policy_code ILIKE '%TEST%'
           OR policy_code ILIKE '%DEV%'
           OR policy_name ILIKE '%SANDBOX%'
           OR policy_name ILIKE '%TEST%'
           OR policy_name ILIKE '%DEV%'
           OR policy_description ILIKE '%sandbox%'
           OR policy_description ILIKE '%test%'
           OR policy_description ILIKE '%dev%'
    ) AS sandbox_or_dev_rows,
    COUNT(*) FILTER (WHERE entitlement_type = 'SENIOR_CITIZEN') AS senior_citizen_rows,
    COUNT(*) FILTER (WHERE entitlement_type = 'PWD') AS pwd_rows,
    COUNT(*) FILTER (WHERE requires_evidence_capture IS NOT TRUE) AS rows_missing_evidence_rule,
    COUNT(*) FILTER (WHERE requires_operator_validation IS NOT TRUE) AS rows_missing_operator_validation,
    COUNT(*) FILTER (
        WHERE policy_level IN ('LOCAL_ORDINANCE', 'SITE_POLICY', 'OPERATIONAL_POLICY')
          AND site_id IS NULL
          AND site_group_id IS NULL
          AND lgu_code IS NULL
    ) AS local_or_site_rows_missing_scope,
    COUNT(*) FILTER (
        WHERE COALESCE(local_ordinance_reference, national_law_reference) IS NULL
           OR COALESCE(local_ordinance_reference, national_law_reference) ILIKE 'DEV_PLACEHOLDER%'
    ) AS rows_missing_production_legal_reference,
    COUNT(*) FILTER (
        WHERE policy_status <> 'ACTIVE'
           OR effective_from > now()
           OR (effective_to IS NOT NULL AND effective_to < now())
    ) AS inactive_or_expired_rows
FROM discounts.discount_policy_references;
