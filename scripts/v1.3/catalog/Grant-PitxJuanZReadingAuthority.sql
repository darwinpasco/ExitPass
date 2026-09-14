\set ON_ERROR_STOP on

BEGIN;

CREATE OR REPLACE FUNCTION pg_temp.exitpass_pitx_z_authority_uuid(input text)
RETURNS uuid
LANGUAGE sql
IMMUTABLE
STRICT
AS $$
    SELECT (
        substr(md5(input), 1, 8) || '-' ||
        substr(md5(input), 9, 4) || '-' ||
        substr(md5(input), 13, 4) || '-' ||
        substr(md5(input), 17, 4) || '-' ||
        substr(md5(input), 21, 12)
    )::uuid
$$;

DO $$
DECLARE
    target_site constant uuid := '2d1dcdf8-f563-537c-8542-0bde7cc9da97';
    target_role constant text := 'FISCAL_Z_READING_CLOSER';
    target_role_id constant uuid := pg_temp.exitpass_pitx_z_authority_uuid(
        'persistent-ist:rbac:role:FISCAL_Z_READING_CLOSER');
BEGIN
    IF (SELECT count(*) FROM sites.sites
        WHERE site_id = target_site AND site_name = 'PITX Level 3') <> 1 THEN
        RAISE EXCEPTION 'Canonical PITX Level 3 Site identity is unavailable.';
    END IF;
    IF (SELECT count(*) FROM identity.users
        WHERE username_normalized = 'juandc01'
          AND display_name = 'Juan Dela Cruz'
          AND user_status::text = 'ACTIVE') <> 1 THEN
        RAISE EXCEPTION 'Canonical active JuanDC01 identity is unavailable or ambiguous.';
    END IF;
    IF (SELECT count(*) FROM identity.permissions
        WHERE permission_code = 'fiscal-reporting.z.generate'
          AND permission_status::text = 'ACTIVE') <> 1 THEN
        RAISE EXCEPTION 'Canonical fiscal-reporting.z.generate permission is unavailable or ambiguous.';
    END IF;
    IF (SELECT count(*) FROM identity.service_identities
        WHERE service_identity_code = 'seed.reference-data'
          AND identity_status::text = 'ACTIVE') <> 1 THEN
        RAISE EXCEPTION 'Canonical reference-data service identity is unavailable or ambiguous.';
    END IF;
    IF NOT EXISTS (
        SELECT 1
        FROM identity.users u
        JOIN identity.user_roles ur ON ur.user_id = u.user_id
        JOIN identity.roles r ON r.role_id = ur.role_id
        JOIN identity.user_role_scope_grants g ON g.user_role_id = ur.user_role_id
        WHERE u.username_normalized = 'juandc01'
          AND r.role_code = 'SITE_OPERATOR'
          AND r.role_status::text = 'ACTIVE'
          AND ur.assignment_status::text = 'ACTIVE'
          AND ur.revoked_at IS NULL
          AND g.scope_type::text = 'SITE'
          AND g.site_id = target_site
          AND g.grant_status::text = 'ACTIVE'
          AND g.revoked_at IS NULL
    ) THEN
        RAISE EXCEPTION 'JuanDC01 must retain an active SITE_OPERATOR assignment scoped to PITX Level 3.';
    END IF;
    IF EXISTS (
        SELECT 1
        FROM identity.role_permissions rp
        JOIN identity.roles r ON r.role_id = rp.role_id
        JOIN identity.permissions p ON p.permission_id = rp.permission_id
        WHERE r.role_code = 'SITE_OPERATOR'
          AND p.permission_code = 'fiscal-reporting.z.generate'
          AND rp.binding_status::text = 'ACTIVE'
          AND rp.revoked_at IS NULL
    ) THEN
        RAISE EXCEPTION 'fiscal-reporting.z.generate must not be granted to the shared SITE_OPERATOR role.';
    END IF;
    IF EXISTS (SELECT 1 FROM identity.roles WHERE role_id = target_role_id AND role_code <> target_role) THEN
        RAISE EXCEPTION 'The deterministic fiscal Z closer role identity conflicts with another role.';
    END IF;
    IF EXISTS (SELECT 1 FROM identity.roles WHERE role_code = target_role AND role_id <> target_role_id) THEN
        RAISE EXCEPTION 'The fiscal Z closer role code uses a non-canonical identity.';
    END IF;
END $$;

INSERT INTO identity.roles (
    role_id, role_code, role_name, role_description, role_type, role_status,
    is_privileged, requires_elevated_approval, effective_from,
    created_by_service_identity_id, updated_by_service_identity_id
)
SELECT pg_temp.exitpass_pitx_z_authority_uuid('persistent-ist:rbac:role:FISCAL_Z_READING_CLOSER'),
       'FISCAL_Z_READING_CLOSER', 'Fiscal Z Reading Closer',
       'Site-scoped authority to request a governed Z Reading close. The POS Server retains period, sequence, counter, GTA, and total authority.',
       'OPERATIONS', 'ACTIVE', true, true, '2026-09-15T00:00:00Z',
       service.service_identity_id, service.service_identity_id
FROM identity.service_identities service
WHERE service.service_identity_code = 'seed.reference-data'
  AND service.identity_status::text = 'ACTIVE'
ON CONFLICT ON CONSTRAINT uq_roles__role_code DO UPDATE
SET role_name = EXCLUDED.role_name,
    role_description = EXCLUDED.role_description,
    role_status = 'ACTIVE',
    is_privileged = true,
    requires_elevated_approval = true,
    effective_to = NULL,
    updated_at = now(),
    updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;

INSERT INTO identity.role_permissions (
    role_permission_id, role_id, permission_id, binding_status,
    binding_reason_code, assigned_by_service_identity_id, effective_from,
    created_by_service_identity_id, updated_by_service_identity_id
)
SELECT pg_temp.exitpass_pitx_z_authority_uuid(
           'persistent-ist:rbac:role-permission:FISCAL_Z_READING_CLOSER:fiscal-reporting.z.generate'),
       role.role_id, permission.permission_id, 'ACTIVE',
       'PITX_FISCAL_Z_CLOSE_AUTHORITY', service.service_identity_id,
       '2026-09-15T00:00:00Z', service.service_identity_id, service.service_identity_id
FROM identity.roles role
JOIN identity.permissions permission
  ON permission.permission_code = 'fiscal-reporting.z.generate'
JOIN identity.service_identities service
  ON service.service_identity_code = 'seed.reference-data'
WHERE role.role_code = 'FISCAL_Z_READING_CLOSER'
  AND NOT EXISTS (
      SELECT 1 FROM identity.role_permissions existing
      WHERE existing.role_id = role.role_id
        AND existing.permission_id = permission.permission_id
        AND existing.binding_status::text = 'ACTIVE')
ON CONFLICT (role_permission_id) DO UPDATE
SET role_id = EXCLUDED.role_id,
    permission_id = EXCLUDED.permission_id,
    binding_status = 'ACTIVE',
    binding_reason_code = EXCLUDED.binding_reason_code,
    effective_to = NULL,
    revoked_at = NULL,
    revoked_by_user_id = NULL,
    revoked_by_service_identity_id = NULL,
    revocation_reason_code = NULL,
    updated_at = now(),
    updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;

INSERT INTO identity.user_roles (
    user_role_id, user_id, role_id, assignment_status, assignment_reason_code,
    assigned_by_service_identity_id, effective_from,
    created_by_service_identity_id, updated_by_service_identity_id
)
SELECT pg_temp.exitpass_pitx_z_authority_uuid(
           'persistent-ist:rbac:user-role:JuanDC01:FISCAL_Z_READING_CLOSER'),
       user_record.user_id, role.role_id, 'ACTIVE',
       'PITX_FISCAL_Z_CLOSE_AUTHORITY', service.service_identity_id,
       '2026-09-15T00:00:00Z', service.service_identity_id, service.service_identity_id
FROM identity.users user_record
JOIN identity.roles role ON role.role_code = 'FISCAL_Z_READING_CLOSER'
JOIN identity.service_identities service
  ON service.service_identity_code = 'seed.reference-data'
WHERE user_record.username_normalized = 'juandc01'
  AND NOT EXISTS (
      SELECT 1 FROM identity.user_roles existing
      WHERE existing.user_id = user_record.user_id
        AND existing.role_id = role.role_id
        AND existing.assignment_status::text = 'ACTIVE')
ON CONFLICT (user_role_id) DO UPDATE
SET user_id = EXCLUDED.user_id,
    role_id = EXCLUDED.role_id,
    assignment_status = 'ACTIVE',
    assignment_reason_code = EXCLUDED.assignment_reason_code,
    effective_to = NULL,
    revoked_at = NULL,
    revoked_by_user_id = NULL,
    revoked_by_service_identity_id = NULL,
    revocation_reason_code = NULL,
    updated_at = now(),
    updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;

INSERT INTO identity.user_role_scope_grants (
    user_role_scope_grant_id, user_role_id, scope_type, site_id, site_group_id,
    grant_status, grant_reason_code, effective_from,
    granted_by_service_identity_id, created_by_service_identity_id,
    updated_by_service_identity_id
)
SELECT pg_temp.exitpass_pitx_z_authority_uuid(
           'persistent-ist:rbac:user-role-scope:JuanDC01:FISCAL_Z_READING_CLOSER:PITX-LEVEL-3'),
       assignment.user_role_id, 'SITE', '2d1dcdf8-f563-537c-8542-0bde7cc9da97', NULL,
       'ACTIVE', 'PITX_FISCAL_Z_CLOSE_AUTHORITY', '2026-09-15T00:00:00Z',
       service.service_identity_id, service.service_identity_id, service.service_identity_id
FROM identity.user_roles assignment
JOIN identity.users user_record ON user_record.user_id = assignment.user_id
JOIN identity.roles role ON role.role_id = assignment.role_id
JOIN identity.service_identities service
  ON service.service_identity_code = 'seed.reference-data'
WHERE user_record.username_normalized = 'juandc01'
  AND role.role_code = 'FISCAL_Z_READING_CLOSER'
  AND assignment.assignment_status::text = 'ACTIVE'
ON CONFLICT (user_role_scope_grant_id) DO UPDATE
SET user_role_id = EXCLUDED.user_role_id,
    scope_type = 'SITE',
    site_id = EXCLUDED.site_id,
    site_group_id = NULL,
    grant_status = 'ACTIVE',
    grant_reason_code = EXCLUDED.grant_reason_code,
    effective_to = NULL,
    revoked_at = NULL,
    revoked_by_user_id = NULL,
    revoked_by_service_identity_id = NULL,
    revocation_reason_code = NULL,
    updated_at = now(),
    updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;

DO $$
DECLARE
    target_site constant uuid := '2d1dcdf8-f563-537c-8542-0bde7cc9da97';
    required_permissions constant text[] := ARRAY[
        'fiscal-reporting.ej.read',
        'fiscal-reporting.ej.export',
        'fiscal-reporting.x.read',
        'fiscal-reporting.x.generate',
        'fiscal-reporting.z.read',
        'fiscal-reporting.z.generate'];
BEGIN
    IF (
        WITH effective_roles AS (
            SELECT ur.user_role_id, ur.role_id
            FROM identity.users u
            JOIN identity.user_roles ur ON ur.user_id = u.user_id
            JOIN identity.roles role ON role.role_id = ur.role_id
            WHERE u.username_normalized = 'juandc01'
              AND ur.assignment_status::text = 'ACTIVE' AND ur.revoked_at IS NULL
              AND ur.effective_from <= now() AND (ur.effective_to IS NULL OR ur.effective_to > now())
              AND role.role_status::text = 'ACTIVE'
              AND role.effective_from <= now() AND (role.effective_to IS NULL OR role.effective_to > now())
        ), effective_permissions AS (
            SELECT DISTINCT permission.permission_code
            FROM effective_roles role
            JOIN identity.role_permissions binding ON binding.role_id = role.role_id
            JOIN identity.permissions permission ON permission.permission_id = binding.permission_id
            WHERE binding.binding_status::text = 'ACTIVE' AND binding.revoked_at IS NULL
              AND binding.effective_from <= now()
              AND (binding.effective_to IS NULL OR binding.effective_to > now())
              AND permission.permission_status::text = 'ACTIVE'
        )
        SELECT count(*) FROM unnest(required_permissions) required(permission_code)
        WHERE NOT EXISTS (
            SELECT 1 FROM effective_permissions effective
            WHERE effective.permission_code = required.permission_code)
    ) <> 0 THEN
        RAISE EXCEPTION 'JuanDC01 does not have the complete required fiscal-reporting permission set.';
    END IF;
    IF (
        SELECT count(*)
        FROM identity.users u
        JOIN identity.user_roles ur ON ur.user_id = u.user_id
        JOIN identity.roles role ON role.role_id = ur.role_id
        JOIN identity.user_role_scope_grants scope_grant ON scope_grant.user_role_id = ur.user_role_id
        WHERE u.username_normalized = 'juandc01'
          AND role.role_code = 'FISCAL_Z_READING_CLOSER'
          AND ur.assignment_status::text = 'ACTIVE'
          AND ur.revoked_at IS NULL
          AND scope_grant.grant_status::text = 'ACTIVE'
          AND scope_grant.revoked_at IS NULL
          AND scope_grant.scope_type::text = 'SITE'
          AND scope_grant.site_id = target_site
    ) <> 1 THEN
        RAISE EXCEPTION 'JuanDC01 must have exactly one active PITX Level 3 scope for fiscal Z close authority.';
    END IF;
    IF EXISTS (
        SELECT 1
        FROM identity.users u
        JOIN identity.user_roles ur ON ur.user_id = u.user_id
        JOIN identity.roles role ON role.role_id = ur.role_id
        JOIN identity.user_role_scope_grants scope_grant ON scope_grant.user_role_id = ur.user_role_id
        WHERE u.username_normalized = 'juandc01'
          AND role.role_code = 'FISCAL_Z_READING_CLOSER'
          AND scope_grant.grant_status::text = 'ACTIVE'
          AND (scope_grant.scope_type::text <> 'SITE' OR scope_grant.site_id <> target_site)
    ) THEN
        RAISE EXCEPTION 'JuanDC01 fiscal Z close authority must be scoped only to PITX Level 3.';
    END IF;
    IF EXISTS (
        SELECT 1
        FROM identity.roles role
        JOIN identity.role_permissions binding ON binding.role_id = role.role_id
        JOIN identity.permissions permission ON permission.permission_id = binding.permission_id
        WHERE role.role_code = 'FISCAL_Z_READING_CLOSER'
          AND binding.binding_status::text = 'ACTIVE'
          AND permission.permission_code <> 'fiscal-reporting.z.generate'
    ) THEN
        RAISE EXCEPTION 'Fiscal Z Reading Closer contains unrelated permissions.';
    END IF;
    IF EXISTS (
        SELECT 1
        FROM identity.roles role
        JOIN identity.role_permissions binding ON binding.role_id = role.role_id
        JOIN identity.permissions permission ON permission.permission_id = binding.permission_id
        WHERE role.role_code = 'SITE_OPERATOR'
          AND binding.binding_status::text = 'ACTIVE'
          AND permission.permission_code = 'fiscal-reporting.z.generate'
    ) THEN
        RAISE EXCEPTION 'Ordinary SITE_OPERATOR users must not inherit Z close authority.';
    END IF;
END $$;

COMMIT;

-- Existing human sessions retain their issued permission snapshot. JuanDC01 must
-- sign out and authenticate again after this governed assignment is applied.
