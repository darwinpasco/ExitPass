-- ExitPass v1.3 WebPay statutory-discount local walkthrough prerequisites.
-- Apply only after the canonical DDL, payment compatibility patches,
-- Seed-WebPayLocalIntegrationWalkthrough.sql,
-- Seed-StatutoryDiscountPilotFixture.sql, and
-- the exact reviewer permission definitions in
-- Seed-ManagementPlatformUatIdentityRbac.sql.
--
-- Required psql variables are generated in memory by the startup script:
--   reviewer_challenge_reference, reviewer_challenge_hash,
--   placeholder_verifier_hex, placeholder_salt_hex.
-- Raw passwords and activation secrets are never stored by this script.

\set ON_ERROR_STOP on

BEGIN;

DO $$
BEGIN
    IF current_database() !~ '^exitpass_webpay_local_walkthrough_statutory(_[a-z0-9_]+)?$' THEN
        RAISE EXCEPTION
            'Refusing to seed WebPay statutory walkthrough data in database %. Use a disposable exitpass_webpay_local_walkthrough_statutory database.',
            current_database();
    END IF;

    IF EXISTS (
        SELECT 1
        FROM discounts.statutory_discount_decision_commands
        WHERE parking_session_id = (
            SELECT parking_session_id
            FROM core.parking_sessions
            WHERE ticket_number_masked = 'E2E-231-SESSION-001'
        )
    ) THEN
        RAISE EXCEPTION
            'The statutory walkthrough already contains decision state. Rebuild the disposable database instead of reseeding over workflow evidence.';
    END IF;

    IF (SELECT count(*) FROM sites.site_groups WHERE site_group_code = 'SANDBOX_OC_SD_PILOT_GROUP') <> 1
       OR (SELECT count(*) FROM sites.sites WHERE site_code = 'SANDBOX_OC_SD_PILOT_SITE') <> 1
       OR (SELECT count(*) FROM core.parking_sessions WHERE ticket_number_masked = 'E2E-231-SESSION-001') <> 1
       OR (SELECT count(*) FROM identity.users WHERE username_normalized = 'sandbox-oc-sd-pilot-reviewer') <> 1
       OR (SELECT count(*) FROM identity.roles WHERE role_code = 'OPERATIONS_SUPERVISOR') <> 1
       OR (SELECT count(*) FROM operator_console.operator_device_bindings WHERE device_binding_code = 'SANDBOX-OC-SD-235A-DEVICE') <> 1
       OR (SELECT count(*) FROM operator_console.operator_shifts WHERE external_shift_id_masked = 'SHIFT-SANDBOX-REVIEWER') <> 1 THEN
        RAISE EXCEPTION
            'A tracked local fixture prerequisite is missing or ambiguous. Rebuild and apply the current ordinary and Operator Console pilot fixtures.';
    END IF;
END
$$;

-- The tracked Operator Console pilot fixture is the business-data authority.
-- Enable public lookup and payment only inside this guarded disposable database.
UPDATE sites.site_groups
SET public_lookup_enabled = true,
    default_payment_enabled = true,
    updated_at = now()
WHERE site_group_code = 'SANDBOX_OC_SD_PILOT_GROUP';

UPDATE sites.sites
SET public_lookup_enabled = true,
    payment_enabled = true,
    updated_at = now()
WHERE site_code = 'SANDBOX_OC_SD_PILOT_SITE';

-- Dedicated service identity used by Payment Orchestrator for the WebPay channel.
INSERT INTO identity.service_identities (
    service_identity_id,
    service_identity_code,
    service_identity_name,
    identity_type,
    identity_status,
    owning_service_name,
    credential_type,
    effective_from,
    effective_to
)
VALUES (
    '78000000-0000-4000-8000-000000000003',
    'WEBPAY_STATUTORY_WALKTHROUGH_SERVICE',
    'WebPay Statutory Walkthrough Service',
    'INTERNAL_SERVICE',
    'ACTIVE',
    'ExitPass Local Walkthrough',
    'NONE',
    now() - interval '1 day',
    now() + interval '7 days'
)
ON CONFLICT (service_identity_id) DO UPDATE
SET identity_status = EXCLUDED.identity_status,
    effective_from = EXCLUDED.effective_from,
    effective_to = EXCLUDED.effective_to,
    updated_at = now();

-- Carry forward only the exact Operator Console review authority from the
-- tracked Management Platform UAT RBAC source. That source has a narrower
-- database-name guard and is not bypassed or executed against this database.
CREATE OR REPLACE FUNCTION pg_temp.exitpass_walkthrough_uuid(input text)
RETURNS uuid
LANGUAGE sql
IMMUTABLE
AS $$
    SELECT (
        substr(md5(input), 1, 8) || '-' ||
        substr(md5(input), 9, 4) || '-' ||
        substr(md5(input), 13, 4) || '-' ||
        substr(md5(input), 17, 4) || '-' ||
        substr(md5(input), 21, 12)
    )::uuid
$$;

CREATE TEMP TABLE webpay_statutory_reviewer_permissions (
    permission_code varchar(96) PRIMARY KEY,
    permission_name varchar(128) NOT NULL,
    permission_description text NOT NULL,
    permission_domain varchar(64) NOT NULL,
    permission_action varchar(64) NOT NULL,
    is_sensitive boolean NOT NULL,
    requires_audit boolean NOT NULL
) ON COMMIT DROP;

INSERT INTO webpay_statutory_reviewer_permissions VALUES
    ('statutory-discounts.review.queue.read', 'Read statutory discount review queue', 'Read service-channel statutory discount review queue items.', 'statutory-discounts', 'read', true, true),
    ('statutory-discounts.review.detail.read', 'Read statutory discount review detail', 'Read service-channel statutory discount review detail records.', 'statutory-discounts', 'read', true, true),
    ('statutory-discounts.decision.review', 'Review statutory discount decision', 'Review statutory discount decision context.', 'statutory-discounts', 'review', true, true),
    ('statutory-discounts.decision.approve', 'Approve statutory discount', 'Approve statutory discount validation when prerequisites and segregation controls are satisfied.', 'statutory-discounts', 'approve', true, true),
    ('statutory-discounts.decision.reject', 'Reject statutory discount', 'Reject statutory discount validation when authorized.', 'statutory-discounts', 'reject', true, true),
    ('statutory-discounts.evidence.review.view', 'Review statutory evidence', 'Read review-safe statutory evidence metadata and inline JPEG or PNG preview through Central PMS.', 'statutory-discounts', 'review', true, true);

INSERT INTO identity.permissions (
    permission_id, permission_code, permission_name, permission_description,
    permission_domain, permission_action, permission_status, is_sensitive,
    requires_audit, created_by_service_identity_id,
    updated_by_service_identity_id
)
SELECT
    pg_temp.exitpass_walkthrough_uuid('management-platform-uat-permission:' || permission_code),
    permission_code, permission_name, permission_description,
    permission_domain, permission_action, 'ACTIVE', is_sensitive,
    requires_audit, '78000000-0000-4000-8000-000000000003',
    '78000000-0000-4000-8000-000000000003'
FROM webpay_statutory_reviewer_permissions
ON CONFLICT ON CONSTRAINT uq_permissions__permission_code DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    permission_description = EXCLUDED.permission_description,
    permission_domain = EXCLUDED.permission_domain,
    permission_action = EXCLUDED.permission_action,
    permission_status = EXCLUDED.permission_status,
    is_sensitive = EXCLUDED.is_sensitive,
    requires_audit = EXCLUDED.requires_audit,
    updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
    updated_at = now();

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM identity.roles
        WHERE role_code = 'OPERATIONS_SUPERVISOR'
          AND role_status::text = 'ACTIVE'
    ) THEN
        RAISE EXCEPTION 'Canonical OPERATIONS_SUPERVISOR role is unavailable.';
    END IF;
END
$$;

UPDATE identity.role_permissions rp
SET binding_status = 'ACTIVE',
    binding_reason_code = 'WEBPAY_STATUTORY_LOCAL_REVIEW',
    assigned_by_service_identity_id = '78000000-0000-4000-8000-000000000003',
    effective_from = now() - interval '1 day',
    effective_to = now() + interval '7 days',
    revoked_at = NULL,
    revoked_by_user_id = NULL,
    revoked_by_service_identity_id = NULL,
    revocation_reason_code = NULL,
    updated_by_service_identity_id = '78000000-0000-4000-8000-000000000003',
    updated_at = now()
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code IN (
    SELECT permission_code FROM webpay_statutory_reviewer_permissions
)
WHERE r.role_code = 'OPERATIONS_SUPERVISOR'
  AND rp.role_id = r.role_id
  AND rp.permission_id = p.permission_id;

INSERT INTO identity.role_permissions (
    role_permission_id, role_id, permission_id, binding_status,
    binding_reason_code, assigned_by_service_identity_id, effective_from,
    effective_to, created_by_service_identity_id,
    updated_by_service_identity_id
)
SELECT
    pg_temp.exitpass_walkthrough_uuid('management-platform-uat-role-permission:OPERATIONS_SUPERVISOR:' || p.permission_code),
    r.role_id,
    p.permission_id,
    'ACTIVE',
    'WEBPAY_STATUTORY_LOCAL_REVIEW',
    '78000000-0000-4000-8000-000000000003',
    now() - interval '1 day',
    now() + interval '7 days',
    '78000000-0000-4000-8000-000000000003',
    '78000000-0000-4000-8000-000000000003'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code IN (
    SELECT permission_code FROM webpay_statutory_reviewer_permissions
)
WHERE r.role_code = 'OPERATIONS_SUPERVISOR'
  AND NOT EXISTS (
      SELECT 1
      FROM identity.role_permissions existing
      WHERE existing.role_id = r.role_id
        AND existing.permission_id = p.permission_id
        AND existing.binding_status::text = 'ACTIVE'
  );

-- The current channel service resolves only approved server-side retention data.
INSERT INTO discounts.statutory_evidence_retention_policies (
    retention_class_code,
    retention_policy_version,
    policy_status,
    environment_scope,
    purpose_code,
    effective_from,
    effective_to,
    created_by_service_identity_id,
    updated_by_service_identity_id
)
VALUES (
    'WEBPAY_STATUTORY_LOCAL_REVIEW',
    '1',
    'APPROVED_ENABLED',
    'LOCAL_TEST',
    'STATUTORY_DISCOUNT_REVIEW',
    now() - interval '1 day',
    now() + interval '7 days',
    '78000000-0000-4000-8000-000000000003',
    '78000000-0000-4000-8000-000000000003'
)
ON CONFLICT (retention_class_code, retention_policy_version) DO UPDATE
SET policy_status = EXCLUDED.policy_status,
    environment_scope = EXCLUDED.environment_scope,
    purpose_code = EXCLUDED.purpose_code,
    effective_from = EXCLUDED.effective_from,
    effective_to = EXCLUDED.effective_to,
    updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
    updated_at = now();

-- Current ordinance authority for the selected Senior Citizen scenario.
INSERT INTO sites.jurisdictions (
    jurisdiction_id,
    jurisdiction_code,
    jurisdiction_type,
    display_name,
    province_name,
    region_name,
    jurisdiction_status,
    effective_from,
    source_reference
)
VALUES
    ('78000000-0000-4000-8000-000000000201', 'PH_WEBPAY_STAT_LOCAL', 'CITY', 'WebPay Statutory Local City', 'Metro Manila', 'NCR', 'ACTIVE', now() - interval '1 day', 'LOCAL_WALKTHROUGH_AUTHORITY'),
    ('78000000-0000-4000-8000-000000000202', 'PH_WEBPAY_AMBIG_A', 'CITY', 'WebPay Ambiguous City A', 'Metro Manila', 'NCR', 'ACTIVE', now() - interval '1 day', 'LOCAL_WALKTHROUGH_NEGATIVE'),
    ('78000000-0000-4000-8000-000000000203', 'PH_WEBPAY_AMBIG_B', 'CITY', 'WebPay Ambiguous City B', 'Metro Manila', 'NCR', 'ACTIVE', now() - interval '1 day', 'LOCAL_WALKTHROUGH_NEGATIVE'),
    ('78000000-0000-4000-8000-000000000204', 'PH_WEBPAY_NO_POLICY', 'CITY', 'WebPay No Policy City', 'Metro Manila', 'NCR', 'ACTIVE', now() - interval '1 day', 'LOCAL_WALKTHROUGH_NEGATIVE')
ON CONFLICT (jurisdiction_id) DO NOTHING;

INSERT INTO sites.site_jurisdiction_assignments (
    site_jurisdiction_assignment_id,
    site_id,
    jurisdiction_id,
    assignment_status,
    effective_from,
    source_reference,
    approval_reference
)
VALUES (
    '78000000-0000-4000-8000-000000000211',
    (SELECT site_id FROM sites.sites WHERE site_code = 'SANDBOX_OC_SD_PILOT_SITE'),
    '78000000-0000-4000-8000-000000000201',
    'ACTIVE',
    now() - interval '1 day',
    'LOCAL_WALKTHROUGH_AUTHORITY',
    'LOCAL_WALKTHROUGH_APPROVAL'
)
ON CONFLICT (site_jurisdiction_assignment_id) DO NOTHING;

INSERT INTO discounts.statutory_discount_policy_registry (
    statutory_discount_policy_registry_id,
    policy_code,
    policy_name,
    entitlement_type,
    policy_status,
    verification_status,
    policy_level,
    policy_type,
    policy_resolution_basis,
    benefit_type,
    discount_base_scope,
    jurisdiction_id,
    jurisdiction_code,
    jurisdiction_name,
    beneficiary_residency_scope,
    full_fee_exempt,
    requires_evidence,
    required_evidence_type,
    legal_basis_reference,
    source_reference,
    reviewed_by,
    reviewed_at,
    approved_by,
    approved_at,
    effective_from,
    correlation_id
)
VALUES (
    '78000000-0000-4000-8000-000000000301',
    'WEBPAY_STAT_LOCAL_SC',
    'WebPay Local Senior Citizen Parking Policy',
    'SENIOR_CITIZEN',
    'ACTIVE',
    'VERIFIED_ACTIVE_OPERATIONAL',
    'LOCAL_ORDINANCE',
    'LOCAL_ORDINANCE',
    'LOCAL_ORDINANCE_APPLIED',
    'STATUTORY_DISCOUNT_VAT_EXEMPT',
    'VAT_EXCLUSIVE',
    '78000000-0000-4000-8000-000000000201',
    'PH_WEBPAY_STAT_LOCAL',
    'WebPay Statutory Local City',
    'NON_RESIDENT_ALLOWED',
    false,
    true,
    'SENIOR_CITIZEN_ID',
    'SYNTHETIC_LOCAL_POLICY_AUTHORITY',
    'LOCAL_WALKTHROUGH_AUTHORITY',
    'local-fixture-reviewer',
    now() - interval '2 hours',
    'local-fixture-approver',
    now() - interval '1 hour',
    now() - interval '1 day',
    gen_random_uuid()
)
ON CONFLICT (statutory_discount_policy_registry_id) DO NOTHING;

INSERT INTO discounts.statutory_discount_policy_versions (
    statutory_discount_policy_version_id,
    statutory_discount_policy_registry_id,
    policy_code,
    policy_version,
    policy_version_label,
    entitlement_type,
    jurisdiction_id,
    jurisdiction_code,
    jurisdiction_display_name,
    policy_scope_type,
    policy_level,
    policy_type,
    policy_resolution_basis,
    source_verification_status,
    transaction_publication_status,
    detailed_rule_verification_status,
    parking_service_applicability,
    benefit_type,
    policy_effect_support_status,
    discount_base_scope,
    beneficiary_residency_scope,
    official_source_identified,
    official_source_available,
    ordinance_text_available,
    ordinance_number_available,
    ordinance_title_available,
    legal_basis_reference,
    source_type,
    source_reference,
    safe_channel_summary,
    safe_reviewer_guidance,
    full_fee_exempt,
    transaction_use_effective_from,
    precedence_rank,
    policy_semantic_hash,
    reviewed_by,
    reviewed_at,
    approved_by,
    approved_at,
    correlation_id
)
VALUES (
    '78000000-0000-4000-8000-000000000302',
    '78000000-0000-4000-8000-000000000301',
    'WEBPAY_STAT_LOCAL_SC',
    'v1',
    'WebPay statutory local v1',
    'SENIOR_CITIZEN',
    '78000000-0000-4000-8000-000000000201',
    'PH_WEBPAY_STAT_LOCAL',
    'WebPay Statutory Local City',
    'JURISDICTION',
    'LOCAL_ORDINANCE',
    'LOCAL_ORDINANCE',
    'LOCAL_ORDINANCE_APPLIED',
    'VERIFIED_ACTIVE_OPERATIONAL',
    'ACTIVE_FOR_TRANSACTION_USE',
    'PARTIALLY_VERIFIED',
    'COVERED',
    'STATUTORY_DISCOUNT_VAT_EXEMPT',
    'SUPPORTED_BY_CURRENT_CALCULATION',
    'VAT_EXCLUSIVE',
    'NON_RESIDENT_ALLOWED',
    true,
    true,
    true,
    true,
    true,
    'SYNTHETIC_LOCAL_POLICY_AUTHORITY',
    'CONTROLLED_OFFLINE_AUTHORITY',
    'LOCAL_WALKTHROUGH_AUTHORITY',
    'Synthetic local Senior Citizen parking policy.',
    'Review only synthetic evidence submitted through the WebPay walkthrough.',
    false,
    now() - interval '1 day',
    100,
    'sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc',
    'local-fixture-reviewer',
    now() - interval '2 hours',
    'local-fixture-approver',
    now() - interval '1 hour',
    gen_random_uuid()
)
ON CONFLICT (statutory_discount_policy_version_id) DO NOTHING;

INSERT INTO discounts.statutory_discount_policy_version_evidence_requirements (
    statutory_discount_policy_version_id,
    evidence_type,
    requirement_status,
    safe_requirement_label,
    safe_requirement_notes
)
VALUES (
    '78000000-0000-4000-8000-000000000302',
    'SENIOR_CITIZEN_ID',
    'REQUIRED',
    'Synthetic Senior Citizen ID image',
    'Use only the generated local walkthrough image. Never use a real identity document.'
)
ON CONFLICT (statutory_discount_policy_version_id, evidence_type) DO UPDATE
SET requirement_status = EXCLUDED.requirement_status,
    safe_requirement_label = EXCLUDED.safe_requirement_label,
    safe_requirement_notes = EXCLUDED.safe_requirement_notes;

-- Negative availability fixtures share the pilot Site Group and vendor but have
-- independent Site authority. They are prerequisites only; no workflow result is forced.
INSERT INTO sites.sites (
    site_id, site_group_id, site_code, site_name, site_description, site_type,
    timezone_name, city, province, country_code, lgu_code, site_status,
    public_lookup_enabled, payment_enabled, effective_from, effective_to,
    created_by_service_identity_id, updated_by_service_identity_id
)
VALUES
    ('78000000-0000-4000-8000-000000000101', '77000000-0000-0000-0000-000000000001', 'WEBPAY_STAT_MISSING_JUR', 'WebPay Missing Jurisdiction Site', 'Negative local fixture.', 'OTHER', 'Asia/Manila', 'Pasig', 'Metro Manila', 'PH', NULL, 'ACTIVE', true, true, now() - interval '1 day', now() + interval '7 days', '78000000-0000-4000-8000-000000000003', '78000000-0000-4000-8000-000000000003'),
    ('78000000-0000-4000-8000-000000000102', '77000000-0000-0000-0000-000000000001', 'WEBPAY_STAT_AMBIG_JUR', 'WebPay Ambiguous Jurisdiction Site', 'Negative local fixture.', 'OTHER', 'Asia/Manila', 'Pasig', 'Metro Manila', 'PH', NULL, 'ACTIVE', true, true, now() - interval '1 day', now() + interval '7 days', '78000000-0000-4000-8000-000000000003', '78000000-0000-4000-8000-000000000003'),
    ('78000000-0000-4000-8000-000000000103', '77000000-0000-0000-0000-000000000001', 'WEBPAY_STAT_NO_POLICY', 'WebPay No Policy Site', 'Negative local fixture.', 'OTHER', 'Asia/Manila', 'Pasig', 'Metro Manila', 'PH', NULL, 'ACTIVE', true, true, now() - interval '1 day', now() + interval '7 days', '78000000-0000-4000-8000-000000000003', '78000000-0000-4000-8000-000000000003')
ON CONFLICT (site_id) DO NOTHING;

INSERT INTO sites.site_jurisdiction_assignments (
    site_jurisdiction_assignment_id, site_id, jurisdiction_id, assignment_status,
    effective_from, source_reference, approval_reference
)
VALUES
    ('78000000-0000-4000-8000-000000000212', '78000000-0000-4000-8000-000000000102', '78000000-0000-4000-8000-000000000202', 'ACTIVE', now() - interval '1 day', 'LOCAL_WALKTHROUGH_NEGATIVE', 'LOCAL_WALKTHROUGH_APPROVAL'),
    ('78000000-0000-4000-8000-000000000213', '78000000-0000-4000-8000-000000000102', '78000000-0000-4000-8000-000000000203', 'ACTIVE', now() - interval '1 day', 'LOCAL_WALKTHROUGH_NEGATIVE', 'LOCAL_WALKTHROUGH_APPROVAL'),
    ('78000000-0000-4000-8000-000000000214', '78000000-0000-4000-8000-000000000103', '78000000-0000-4000-8000-000000000204', 'ACTIVE', now() - interval '1 day', 'LOCAL_WALKTHROUGH_NEGATIVE', 'LOCAL_WALKTHROUGH_APPROVAL')
ON CONFLICT (site_jurisdiction_assignment_id) DO NOTHING;

INSERT INTO core.parking_sessions (
    parking_session_id, site_group_id, site_id, vendor_system_id, vendor_session_ref,
    plate_number_hash, plate_number_masked, ticket_number_hash, ticket_number_masked,
    entry_at, vendor_session_status, session_status, correlation_id,
    created_by_service_identity_id, updated_by_service_identity_id
)
VALUES
    ('78000000-0000-4000-8000-000000000401', '77000000-0000-0000-0000-000000000001', '78000000-0000-4000-8000-000000000101', '77000000-0000-0000-0000-000000000004', 'WEBPAY-STAT-MISSING-JURISDICTION', repeat('1',64), 'SYNTH-MJ', repeat('2',64), 'WEBPAY-STAT-MISSING-JURISDICTION', now() - interval '1 hour', 'ACTIVE', 'ACTIVE', gen_random_uuid(), '78000000-0000-4000-8000-000000000003', '78000000-0000-4000-8000-000000000003'),
    ('78000000-0000-4000-8000-000000000402', '77000000-0000-0000-0000-000000000001', '78000000-0000-4000-8000-000000000102', '77000000-0000-0000-0000-000000000004', 'WEBPAY-STAT-AMBIGUOUS-JURISDICTION', repeat('3',64), 'SYNTH-AJ', repeat('4',64), 'WEBPAY-STAT-AMBIGUOUS-JURISDICTION', now() - interval '1 hour', 'ACTIVE', 'ACTIVE', gen_random_uuid(), '78000000-0000-4000-8000-000000000003', '78000000-0000-4000-8000-000000000003'),
    ('78000000-0000-4000-8000-000000000403', '77000000-0000-0000-0000-000000000001', '78000000-0000-4000-8000-000000000103', '77000000-0000-0000-0000-000000000004', 'WEBPAY-STAT-NO-POLICY', repeat('5',64), 'SYNTH-NP', repeat('6',64), 'WEBPAY-STAT-NO-POLICY', now() - interval '1 hour', 'ACTIVE', 'ACTIVE', gen_random_uuid(), '78000000-0000-4000-8000-000000000003', '78000000-0000-4000-8000-000000000003');

INSERT INTO core.tariff_snapshots (
    tariff_snapshot_id, parking_session_id, vendor_system_id, vendor_tariff_ref,
    tariff_version_reference, currency_code, gross_amount, statutory_discount_amount,
    coupon_discount_amount, net_amount, snapshot_status, calculated_at, expires_at,
    correlation_id, created_by_service_identity_id, updated_by_service_identity_id
)
VALUES
    ('78000000-0000-4000-8000-000000000411', '78000000-0000-4000-8000-000000000401', '77000000-0000-0000-0000-000000000004', 'WEBPAY-STAT-MJ-ORIGINAL', 'LOCAL-V1', 'PHP', 125.00, 0, 0, 125.00, 'ACTIVE', now(), now() + interval '4 hours', gen_random_uuid(), '78000000-0000-4000-8000-000000000003', '78000000-0000-4000-8000-000000000003'),
    ('78000000-0000-4000-8000-000000000412', '78000000-0000-4000-8000-000000000402', '77000000-0000-0000-0000-000000000004', 'WEBPAY-STAT-AJ-ORIGINAL', 'LOCAL-V1', 'PHP', 125.00, 0, 0, 125.00, 'ACTIVE', now(), now() + interval '4 hours', gen_random_uuid(), '78000000-0000-4000-8000-000000000003', '78000000-0000-4000-8000-000000000003'),
    ('78000000-0000-4000-8000-000000000413', '78000000-0000-4000-8000-000000000403', '77000000-0000-0000-0000-000000000004', 'WEBPAY-STAT-NP-ORIGINAL', 'LOCAL-V1', 'PHP', 125.00, 0, 0, 125.00, 'ACTIVE', now(), now() + interval '4 hours', gen_random_uuid(), '78000000-0000-4000-8000-000000000003', '78000000-0000-4000-8000-000000000003');

-- Reconfirm that the bounded permission promotion above remains complete.
DO $$
DECLARE
    v_required text[] := ARRAY[
        'statutory-discounts.review.queue.read',
        'statutory-discounts.review.detail.read',
        'statutory-discounts.decision.review',
        'statutory-discounts.decision.approve',
        'statutory-discounts.decision.reject',
        'statutory-discounts.evidence.review.view'
    ];
BEGIN
    IF (SELECT count(*) FROM identity.permissions WHERE permission_status = 'ACTIVE' AND permission_code = ANY(v_required)) <> cardinality(v_required) THEN
        RAISE EXCEPTION 'Current Operator Console review permissions are incomplete after bounded local promotion.';
    END IF;

    IF (SELECT count(*)
        FROM identity.roles r
        JOIN identity.role_permissions rp ON rp.role_id = r.role_id AND rp.binding_status = 'ACTIVE'
        JOIN identity.permissions p ON p.permission_id = rp.permission_id AND p.permission_status = 'ACTIVE'
        WHERE r.role_code = 'OPERATIONS_SUPERVISOR' AND p.permission_code = ANY(v_required)) <> cardinality(v_required) THEN
        RAISE EXCEPTION 'OPERATIONS_SUPERVISOR does not have the current bounded review permission bundle.';
    END IF;
END
$$;

UPDATE identity.users
SET user_status = 'INVITED',
    locked_at = NULL,
    suspended_at = NULL,
    retired_at = NULL,
    effective_from = now() - interval '1 day',
    effective_to = now() + interval '7 days',
    updated_by_service_identity_id = '78000000-0000-4000-8000-000000000003',
    updated_at = now(),
    row_version = row_version + 1
WHERE username_normalized = 'sandbox-oc-sd-pilot-reviewer';

INSERT INTO identity.user_roles (
    user_role_id, user_id, role_id, assignment_status, assignment_reason_code,
    assigned_by_service_identity_id, effective_from, effective_to,
    created_by_service_identity_id, updated_by_service_identity_id
)
SELECT
    '78000000-0000-4000-8000-000000000501',
    u.user_id,
    r.role_id,
    'ACTIVE',
    'WEBPAY_STATUTORY_LOCAL_REVIEW',
    '78000000-0000-4000-8000-000000000003',
    now() - interval '1 day',
    now() + interval '7 days',
    '78000000-0000-4000-8000-000000000003',
    '78000000-0000-4000-8000-000000000003'
FROM identity.users u
JOIN identity.roles r ON r.role_code = 'OPERATIONS_SUPERVISOR'
WHERE u.username_normalized = 'sandbox-oc-sd-pilot-reviewer'
ON CONFLICT (user_role_id) DO UPDATE
SET assignment_status = 'ACTIVE',
    effective_from = EXCLUDED.effective_from,
    effective_to = EXCLUDED.effective_to,
    revoked_at = NULL,
    revocation_reason_code = NULL,
    updated_at = now();

INSERT INTO identity.user_role_scope_grants (
    user_role_scope_grant_id, user_role_id, scope_type, site_id, site_group_id,
    grant_status, grant_reason_code, effective_from, effective_to,
    granted_by_service_identity_id, created_by_service_identity_id,
    updated_by_service_identity_id
)
VALUES
    ('78000000-0000-4000-8000-000000000511', '78000000-0000-4000-8000-000000000501', 'SITE', (SELECT site_id FROM sites.sites WHERE site_code = 'SANDBOX_OC_SD_PILOT_SITE'), NULL, 'ACTIVE', 'WEBPAY_STATUTORY_LOCAL_REVIEW', now() - interval '1 day', now() + interval '7 days', '78000000-0000-4000-8000-000000000003', '78000000-0000-4000-8000-000000000003', '78000000-0000-4000-8000-000000000003'),
    ('78000000-0000-4000-8000-000000000512', '78000000-0000-4000-8000-000000000501', 'SITE_GROUP', NULL, (SELECT site_group_id FROM sites.site_groups WHERE site_group_code = 'SANDBOX_OC_SD_PILOT_GROUP'), 'ACTIVE', 'WEBPAY_STATUTORY_LOCAL_REVIEW', now() - interval '1 day', now() + interval '7 days', '78000000-0000-4000-8000-000000000003', '78000000-0000-4000-8000-000000000003', '78000000-0000-4000-8000-000000000003')
ON CONFLICT (user_role_scope_grant_id) DO UPDATE
SET grant_status = 'ACTIVE',
    effective_from = EXCLUDED.effective_from,
    effective_to = EXCLUDED.effective_to,
    revoked_at = NULL,
    revocation_reason_code = NULL,
    updated_at = now();

-- Channel-safe capture authority and privacy-safe Operator Console evidence scope.
INSERT INTO discounts.statutory_evidence_principal_scope_grants (
    statutory_evidence_principal_scope_grant_id, actor_user_id,
    actor_service_identity_id, source_channel, site_id, site_group_id,
    capture_allowed, view_allowed, review_lock_allowed, hold_allowed,
    deletion_request_allowed, grant_status, effective_from, effective_to,
    reason_code, created_by_service_identity_id, updated_by_service_identity_id
)
VALUES
    ('78000000-0000-4000-8000-000000000521', NULL, '78000000-0000-4000-8000-000000000003', 'WEBPAY', (SELECT site_id FROM sites.sites WHERE site_code = 'SANDBOX_OC_SD_PILOT_SITE'), (SELECT site_group_id FROM sites.site_groups WHERE site_group_code = 'SANDBOX_OC_SD_PILOT_GROUP'), true, true, false, false, false, 'ACTIVE', now() - interval '1 day', now() + interval '7 days', 'WEBPAY_STATUTORY_LOCAL_CAPTURE', '78000000-0000-4000-8000-000000000003', '78000000-0000-4000-8000-000000000003'),
    ('78000000-0000-4000-8000-000000000522', (SELECT user_id FROM identity.users WHERE username_normalized = 'sandbox-oc-sd-pilot-reviewer'), NULL, 'OPERATOR_CONSOLE', (SELECT site_id FROM sites.sites WHERE site_code = 'SANDBOX_OC_SD_PILOT_SITE'), (SELECT site_group_id FROM sites.site_groups WHERE site_group_code = 'SANDBOX_OC_SD_PILOT_GROUP'), false, true, true, false, false, 'ACTIVE', now() - interval '1 day', now() + interval '7 days', 'WEBPAY_STATUTORY_LOCAL_REVIEW', '78000000-0000-4000-8000-000000000003', '78000000-0000-4000-8000-000000000003')
ON CONFLICT (statutory_evidence_principal_scope_grant_id) DO UPDATE
SET grant_status = 'ACTIVE',
    effective_from = EXCLUDED.effective_from,
    effective_to = EXCLUDED.effective_to,
    updated_at = now();

-- Recreate only this synthetic reviewer's pending activation authority. This is
-- safe because the database guard and no-existing-workflow guard have passed.
DELETE FROM identity.credential_challenges
WHERE user_id = (SELECT user_id FROM identity.users WHERE username_normalized = 'sandbox-oc-sd-pilot-reviewer');

DELETE FROM identity.local_credentials
WHERE user_id = (SELECT user_id FROM identity.users WHERE username_normalized = 'sandbox-oc-sd-pilot-reviewer');

INSERT INTO identity.local_credentials (
    local_credential_id, user_id, credential_status, password_verifier,
    verifier_salt, verifier_algorithm_code, verifier_algorithm_version,
    verifier_work_factor, verifier_memory_kib, verifier_parallelism,
    status_reason_code, created_by_service_identity_id,
    updated_by_service_identity_id
)
VALUES (
    '78000000-0000-4000-8000-000000000531',
    (SELECT user_id FROM identity.users WHERE username_normalized = 'sandbox-oc-sd-pilot-reviewer'),
    'PENDING_ACTIVATION',
    decode(:'placeholder_verifier_hex', 'hex'),
    decode(:'placeholder_salt_hex', 'hex'),
    'ARGON2ID',
    1,
    3,
    65536,
    1,
    'LOCAL_WALKTHROUGH_ACTIVATION_PENDING',
    '78000000-0000-4000-8000-000000000003',
    '78000000-0000-4000-8000-000000000003'
);

INSERT INTO identity.credential_challenges (
    credential_challenge_id, challenge_reference, user_id, challenge_purpose,
    challenge_status, challenge_secret_hash, issued_at, expires_at,
    requested_by_service_identity_id, reason_code, correlation_id
)
VALUES (
    '78000000-0000-4000-8000-000000000532',
    :'reviewer_challenge_reference'::uuid,
    (SELECT user_id FROM identity.users WHERE username_normalized = 'sandbox-oc-sd-pilot-reviewer'),
    'ACCOUNT_ACTIVATION',
    'ISSUED',
    :'reviewer_challenge_hash',
    now(),
    now() + interval '30 minutes',
    '78000000-0000-4000-8000-000000000003',
    'LOCAL_WALKTHROUGH_ACTIVATION',
    gen_random_uuid()
);

-- There is deliberately no GLOBAL grant and no seeded decision, evidence item,
-- review result, payable-basis application, payment attempt, or provider session.

COMMIT;

SELECT json_build_object(
    'ticketReference', ps.ticket_number_masked,
    'parkingSessionId', ps.parking_session_id,
    'siteId', ps.site_id,
    'siteGroupId', ps.site_group_id,
    'vendorSystemId', ps.vendor_system_id,
    'webPayServiceIdentityId', '78000000-0000-4000-8000-000000000003'::uuid,
    'reviewerUsername', u.username,
    'reviewerUserId', u.user_id,
    'operatorDeviceBindingId', (SELECT operator_device_binding_id FROM operator_console.operator_device_bindings WHERE device_binding_code = 'SANDBOX-OC-SD-235A-DEVICE'),
    'operatorShiftId', (SELECT operator_shift_id FROM operator_console.operator_shifts WHERE external_shift_id_masked = 'SHIFT-SANDBOX-REVIEWER'),
    'requiredEvidenceType', 'SENIOR_CITIZEN_ID',
    'ordinaryTicketReference', 'WEBPAY-LOCAL-ORDINARY-001',
    'missingJurisdictionTicket', 'WEBPAY-STAT-MISSING-JURISDICTION',
    'ambiguousJurisdictionTicket', 'WEBPAY-STAT-AMBIGUOUS-JURISDICTION',
    'noPolicyTicket', 'WEBPAY-STAT-NO-POLICY'
) AS walkthrough_fixture
FROM core.parking_sessions ps
JOIN identity.users u ON u.username_normalized = 'sandbox-oc-sd-pilot-reviewer'
WHERE ps.ticket_number_masked = 'E2E-231-SESSION-001';
