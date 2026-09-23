-- ExitPass v1.3 dedicated global statutory-discount processor role.
BEGIN;

INSERT INTO identity.roles (
    role_id, role_code, role_name, role_description, role_type, role_status,
    is_privileged, requires_elevated_approval, role_provenance,
    direct_add_user_eligible, human_assignable, effective_from,
    created_by_service_identity_id, updated_by_service_identity_id)
VALUES (
    gen_random_uuid(),
    'STATUTORY_DISCOUNT_PROCESSOR',
    'Statutory Discount Processor',
    'Head-office processing of statutory discount and statutory privilege requests across all Sites.',
    'COMPLIANCE',
    'ACTIVE',
    true,
    false,
    'CANONICAL_ROLE',
    true,
    true,
    now(),
    '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978',
    '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978')
ON CONFLICT ON CONSTRAINT uq_roles__role_code DO UPDATE
SET role_name = EXCLUDED.role_name,
    role_description = EXCLUDED.role_description,
    role_type = EXCLUDED.role_type,
    role_status = 'ACTIVE',
    is_privileged = EXCLUDED.is_privileged,
    requires_elevated_approval = EXCLUDED.requires_elevated_approval,
    role_provenance = 'CANONICAL_ROLE',
    direct_add_user_eligible = true,
    human_assignable = true,
    effective_to = NULL,
    updated_at = now(),
    row_version = identity.roles.row_version + 1;

INSERT INTO identity.role_user_type_compatibility (role_id, user_type)
SELECT role_id, 'COMPLIANCE_USER'::identity.user_type_enum
FROM identity.roles r
WHERE r.role_code = 'STATUTORY_DISCOUNT_PROCESSOR'
  AND NOT EXISTS (
      SELECT 1
      FROM identity.role_user_type_compatibility existing
      WHERE existing.role_id = r.role_id
        AND existing.user_type = 'COMPLIANCE_USER'::identity.user_type_enum);

CREATE TEMP TABLE statutory_processor_permissions (
    permission_code varchar(96) PRIMARY KEY
) ON COMMIT DROP;

INSERT INTO statutory_processor_permissions VALUES
('statutory-discounts.review.queue.read'),
('statutory-discounts.review.detail.read'),
('statutory-discounts.evidence.review.view'),
('statutory-discounts.decision.approve'),
('statutory-discounts.decision.reject');

UPDATE identity.role_permissions rp
SET binding_status = 'REVOKED',
    effective_to = now(),
    revoked_at = now(),
    revocation_reason_code = 'V13_STATUTORY_PROCESSOR_BUNDLE',
    updated_at = now(),
    row_version = rp.row_version + 1
FROM identity.roles r, identity.permissions p
WHERE rp.role_id = r.role_id
  AND rp.permission_id = p.permission_id
  AND r.role_code = 'STATUTORY_DISCOUNT_PROCESSOR'
  AND rp.binding_status = 'ACTIVE'
  AND NOT EXISTS (
      SELECT 1
      FROM statutory_processor_permissions approved
      WHERE approved.permission_code = p.permission_code);

INSERT INTO identity.role_permissions (
    role_permission_id, role_id, permission_id, binding_status,
    binding_reason_code, assigned_by_service_identity_id, effective_from,
    created_by_service_identity_id, updated_by_service_identity_id)
SELECT
    gen_random_uuid(), r.role_id, p.permission_id, 'ACTIVE',
    'V13_STATUTORY_PROCESSOR_BUNDLE',
    '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(),
    '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978',
    '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN statutory_processor_permissions approved ON true
JOIN identity.permissions p ON p.permission_code = approved.permission_code
WHERE r.role_code = 'STATUTORY_DISCOUNT_PROCESSOR'
  AND NOT EXISTS (
      SELECT 1
      FROM identity.role_permissions existing
      WHERE existing.role_id = r.role_id
        AND existing.permission_id = p.permission_id
        AND existing.binding_status = 'ACTIVE');

COMMIT;
