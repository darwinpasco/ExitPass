-- Forward-only governed RBAC repair for the PITX manual-acceptance identity.
-- This patch reactivates the existing narrow Z-close role/binding and assigns
-- it only to uat-operations-supervisor at PITX Level 3. It deliberately does
-- not grant Z-close authority to shared operational roles or Site Operators.
BEGIN;

CREATE OR REPLACE FUNCTION pg_temp.exitpass_pitx_uat_z_closer_uuid(input text)
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

DO $preflight$
DECLARE
    target_site constant uuid := '2d1dcdf8-f563-537c-8542-0bde7cc9da97';
BEGIN
    IF (SELECT count(*) FROM sites.sites
        WHERE site_id = target_site AND site_name = 'PITX Level 3') <> 1 THEN
        RAISE EXCEPTION 'Canonical PITX Level 3 Site identity is unavailable.';
    END IF;

    IF (SELECT count(*) FROM identity.users
        WHERE username_normalized = 'uat-operations-supervisor'
          AND user_status::text = 'ACTIVE') <> 1 THEN
        RAISE EXCEPTION 'Canonical active uat-operations-supervisor identity is unavailable or ambiguous.';
    END IF;

    IF (SELECT count(*) FROM identity.roles
        WHERE role_code = 'FISCAL_Z_READING_CLOSER') <> 1 THEN
        RAISE EXCEPTION 'Existing FISCAL_Z_READING_CLOSER role is unavailable or ambiguous.';
    END IF;

    IF (SELECT count(*) FROM identity.permissions
        WHERE permission_code = 'fiscal-reporting.z.generate'
          AND permission_status::text = 'ACTIVE') <> 1 THEN
        RAISE EXCEPTION 'Canonical active fiscal-reporting.z.generate permission is unavailable or ambiguous.';
    END IF;

    IF (SELECT count(*)
        FROM identity.role_permissions rp
        JOIN identity.roles r ON r.role_id = rp.role_id
        JOIN identity.permissions p ON p.permission_id = rp.permission_id
        WHERE r.role_code = 'FISCAL_Z_READING_CLOSER'
          AND p.permission_code = 'fiscal-reporting.z.generate') <> 1 THEN
        RAISE EXCEPTION 'Existing FISCAL_Z_READING_CLOSER Z-generate binding is unavailable or ambiguous.';
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
        WHERE u.username_normalized = 'uat-operations-supervisor'
          AND r.role_code = 'OPERATIONS_SUPERVISOR'
          AND r.role_status::text = 'ACTIVE'
          AND ur.assignment_status::text = 'ACTIVE'
          AND ur.revoked_at IS NULL
          AND g.scope_type::text = 'SITE'
          AND g.site_id = target_site
          AND g.grant_status::text = 'ACTIVE'
          AND g.revoked_at IS NULL
    ) THEN
        RAISE EXCEPTION 'uat-operations-supervisor must retain active OPERATIONS_SUPERVISOR authority scoped to PITX Level 3.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM identity.user_roles ur
        JOIN identity.users u ON u.user_id = ur.user_id
        JOIN identity.roles r ON r.role_id = ur.role_id
        WHERE u.username_normalized = 'uat-operations-supervisor'
          AND r.role_code = 'FISCAL_Z_READING_CLOSER'
          AND ur.user_role_id <> pg_temp.exitpass_pitx_uat_z_closer_uuid(
              'persistent-ist:rbac:user-role:uat-operations-supervisor:FISCAL_Z_READING_CLOSER')
    ) THEN
        RAISE EXCEPTION 'uat-operations-supervisor has a non-canonical historical FISCAL_Z_READING_CLOSER assignment.';
    END IF;
END
$preflight$;

UPDATE identity.roles role
SET role_status = 'ACTIVE',
    effective_to = NULL,
    updated_at = now(),
    updated_by_user_id = NULL,
    updated_by_service_identity_id = service.service_identity_id,
    row_version = role.row_version + 1
FROM identity.service_identities service
WHERE role.role_code = 'FISCAL_Z_READING_CLOSER'
  AND service.service_identity_code = 'seed.reference-data'
  AND service.identity_status::text = 'ACTIVE'
  AND (role.role_status::text <> 'ACTIVE' OR role.effective_to IS NOT NULL);

UPDATE identity.role_permissions binding
SET binding_status = 'ACTIVE',
    binding_reason_code = 'PITX_UAT_FISCAL_Z_CLOSE_AUTHORITY',
    effective_to = NULL,
    revoked_at = NULL,
    revoked_by_user_id = NULL,
    revoked_by_service_identity_id = NULL,
    revocation_reason_code = NULL,
    updated_at = now(),
    updated_by_user_id = NULL,
    updated_by_service_identity_id = service.service_identity_id,
    row_version = binding.row_version + 1
FROM identity.roles role,
     identity.permissions permission,
     identity.service_identities service
WHERE binding.role_id = role.role_id
  AND binding.permission_id = permission.permission_id
  AND role.role_code = 'FISCAL_Z_READING_CLOSER'
  AND permission.permission_code = 'fiscal-reporting.z.generate'
  AND service.service_identity_code = 'seed.reference-data'
  AND service.identity_status::text = 'ACTIVE'
  AND (
      binding.binding_status::text <> 'ACTIVE'
      OR binding.binding_reason_code IS DISTINCT FROM 'PITX_UAT_FISCAL_Z_CLOSE_AUTHORITY'
      OR binding.effective_to IS NOT NULL
      OR binding.revoked_at IS NOT NULL
      OR binding.revoked_by_user_id IS NOT NULL
      OR binding.revoked_by_service_identity_id IS NOT NULL
      OR binding.revocation_reason_code IS NOT NULL
  );

-- Ensure reactivating the closer role cannot reactivate any historical user.
UPDATE identity.user_role_scope_grants scope_grant
SET grant_status = 'REVOKED',
    effective_to = COALESCE(scope_grant.effective_to, now()),
    revoked_at = COALESCE(scope_grant.revoked_at, now()),
    revoked_by_user_id = NULL,
    revoked_by_service_identity_id = service.service_identity_id,
    revocation_reason_code = 'PITX_UAT_FISCAL_Z_CLOSE_AUTHORITY_ONLY',
    updated_at = now(),
    updated_by_user_id = NULL,
    updated_by_service_identity_id = service.service_identity_id,
    row_version = scope_grant.row_version + 1
FROM identity.user_roles assignment
JOIN identity.users user_record ON user_record.user_id = assignment.user_id
JOIN identity.roles role ON role.role_id = assignment.role_id
JOIN identity.service_identities service
  ON service.service_identity_code = 'seed.reference-data'
WHERE scope_grant.user_role_id = assignment.user_role_id
  AND role.role_code = 'FISCAL_Z_READING_CLOSER'
  AND user_record.username_normalized <> 'uat-operations-supervisor'
  AND scope_grant.grant_status::text <> 'REVOKED';

UPDATE identity.user_roles assignment
SET assignment_status = 'REVOKED',
    effective_to = COALESCE(assignment.effective_to, now()),
    revoked_at = COALESCE(assignment.revoked_at, now()),
    revoked_by_user_id = NULL,
    revoked_by_service_identity_id = service.service_identity_id,
    revocation_reason_code = 'PITX_UAT_FISCAL_Z_CLOSE_AUTHORITY_ONLY',
    updated_at = now(),
    updated_by_user_id = NULL,
    updated_by_service_identity_id = service.service_identity_id,
    row_version = assignment.row_version + 1
FROM identity.users user_record,
     identity.roles role,
     identity.service_identities service
WHERE assignment.user_id = user_record.user_id
  AND assignment.role_id = role.role_id
  AND role.role_code = 'FISCAL_Z_READING_CLOSER'
  AND user_record.username_normalized <> 'uat-operations-supervisor'
  AND service.service_identity_code = 'seed.reference-data'
  AND service.identity_status::text = 'ACTIVE'
  AND assignment.assignment_status::text <> 'REVOKED';

INSERT INTO identity.user_roles (
    user_role_id, user_id, role_id, assignment_status, assignment_reason_code,
    assigned_by_service_identity_id, effective_from,
    created_by_service_identity_id, updated_by_service_identity_id
)
SELECT pg_temp.exitpass_pitx_uat_z_closer_uuid(
           'persistent-ist:rbac:user-role:uat-operations-supervisor:FISCAL_Z_READING_CLOSER'),
       user_record.user_id,
       role.role_id,
       'ACTIVE',
       'PITX_UAT_FISCAL_Z_CLOSE_AUTHORITY',
       service.service_identity_id,
       now(),
       service.service_identity_id,
       service.service_identity_id
FROM identity.users user_record
JOIN identity.roles role ON role.role_code = 'FISCAL_Z_READING_CLOSER'
JOIN identity.service_identities service
  ON service.service_identity_code = 'seed.reference-data'
WHERE user_record.username_normalized = 'uat-operations-supervisor'
ON CONFLICT (user_role_id) DO UPDATE
SET user_id = EXCLUDED.user_id,
    role_id = EXCLUDED.role_id,
    assignment_status = 'ACTIVE',
    assignment_reason_code = 'PITX_UAT_FISCAL_Z_CLOSE_AUTHORITY',
    effective_to = NULL,
    revoked_at = NULL,
    revoked_by_user_id = NULL,
    revoked_by_service_identity_id = NULL,
    revocation_reason_code = NULL,
    updated_at = CASE
        WHEN identity.user_roles.assignment_status::text = 'ACTIVE'
         AND identity.user_roles.assignment_reason_code = 'PITX_UAT_FISCAL_Z_CLOSE_AUTHORITY'
         AND identity.user_roles.effective_to IS NULL
         AND identity.user_roles.revoked_at IS NULL
        THEN identity.user_roles.updated_at
        ELSE now()
    END,
    updated_by_user_id = NULL,
    updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
    row_version = CASE
        WHEN identity.user_roles.assignment_status::text = 'ACTIVE'
         AND identity.user_roles.assignment_reason_code = 'PITX_UAT_FISCAL_Z_CLOSE_AUTHORITY'
         AND identity.user_roles.effective_to IS NULL
         AND identity.user_roles.revoked_at IS NULL
        THEN identity.user_roles.row_version
        ELSE identity.user_roles.row_version + 1
    END
WHERE identity.user_roles.user_id IS DISTINCT FROM EXCLUDED.user_id
   OR identity.user_roles.role_id IS DISTINCT FROM EXCLUDED.role_id
   OR identity.user_roles.assignment_status::text <> 'ACTIVE'
   OR identity.user_roles.assignment_reason_code IS DISTINCT FROM 'PITX_UAT_FISCAL_Z_CLOSE_AUTHORITY'
   OR identity.user_roles.effective_to IS NOT NULL
   OR identity.user_roles.revoked_at IS NOT NULL
   OR identity.user_roles.revoked_by_user_id IS NOT NULL
   OR identity.user_roles.revoked_by_service_identity_id IS NOT NULL
   OR identity.user_roles.revocation_reason_code IS NOT NULL
   OR identity.user_roles.updated_by_user_id IS NOT NULL
   OR identity.user_roles.updated_by_service_identity_id IS DISTINCT FROM EXCLUDED.updated_by_service_identity_id;

INSERT INTO identity.user_role_scope_grants (
    user_role_scope_grant_id, user_role_id, scope_type, site_id, site_group_id,
    grant_status, grant_reason_code, effective_from,
    granted_by_service_identity_id, created_by_service_identity_id,
    updated_by_service_identity_id
)
SELECT pg_temp.exitpass_pitx_uat_z_closer_uuid(
           'persistent-ist:rbac:user-role-scope:uat-operations-supervisor:FISCAL_Z_READING_CLOSER:PITX-LEVEL-3'),
       assignment.user_role_id,
       'SITE',
       '2d1dcdf8-f563-537c-8542-0bde7cc9da97',
       NULL,
       'ACTIVE',
       'PITX_UAT_FISCAL_Z_CLOSE_AUTHORITY',
       now(),
       service.service_identity_id,
       service.service_identity_id,
       service.service_identity_id
FROM identity.user_roles assignment
JOIN identity.users user_record ON user_record.user_id = assignment.user_id
JOIN identity.roles role ON role.role_id = assignment.role_id
JOIN identity.service_identities service
  ON service.service_identity_code = 'seed.reference-data'
WHERE user_record.username_normalized = 'uat-operations-supervisor'
  AND role.role_code = 'FISCAL_Z_READING_CLOSER'
  AND assignment.assignment_status::text = 'ACTIVE'
ON CONFLICT (user_role_scope_grant_id) DO UPDATE
SET user_role_id = EXCLUDED.user_role_id,
    scope_type = 'SITE',
    site_id = EXCLUDED.site_id,
    site_group_id = NULL,
    grant_status = 'ACTIVE',
    grant_reason_code = 'PITX_UAT_FISCAL_Z_CLOSE_AUTHORITY',
    effective_to = NULL,
    revoked_at = NULL,
    revoked_by_user_id = NULL,
    revoked_by_service_identity_id = NULL,
    revocation_reason_code = NULL,
    updated_at = CASE
        WHEN identity.user_role_scope_grants.grant_status::text = 'ACTIVE'
         AND identity.user_role_scope_grants.grant_reason_code = 'PITX_UAT_FISCAL_Z_CLOSE_AUTHORITY'
         AND identity.user_role_scope_grants.scope_type::text = 'SITE'
         AND identity.user_role_scope_grants.site_id = EXCLUDED.site_id
         AND identity.user_role_scope_grants.site_group_id IS NULL
         AND identity.user_role_scope_grants.effective_to IS NULL
         AND identity.user_role_scope_grants.revoked_at IS NULL
        THEN identity.user_role_scope_grants.updated_at
        ELSE now()
    END,
    updated_by_user_id = NULL,
    updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
    row_version = CASE
        WHEN identity.user_role_scope_grants.grant_status::text = 'ACTIVE'
         AND identity.user_role_scope_grants.grant_reason_code = 'PITX_UAT_FISCAL_Z_CLOSE_AUTHORITY'
         AND identity.user_role_scope_grants.scope_type::text = 'SITE'
         AND identity.user_role_scope_grants.site_id = EXCLUDED.site_id
         AND identity.user_role_scope_grants.site_group_id IS NULL
         AND identity.user_role_scope_grants.effective_to IS NULL
         AND identity.user_role_scope_grants.revoked_at IS NULL
        THEN identity.user_role_scope_grants.row_version
        ELSE identity.user_role_scope_grants.row_version + 1
    END
WHERE identity.user_role_scope_grants.user_role_id IS DISTINCT FROM EXCLUDED.user_role_id
   OR identity.user_role_scope_grants.scope_type::text <> 'SITE'
   OR identity.user_role_scope_grants.site_id IS DISTINCT FROM EXCLUDED.site_id
   OR identity.user_role_scope_grants.site_group_id IS NOT NULL
   OR identity.user_role_scope_grants.grant_status::text <> 'ACTIVE'
   OR identity.user_role_scope_grants.grant_reason_code IS DISTINCT FROM 'PITX_UAT_FISCAL_Z_CLOSE_AUTHORITY'
   OR identity.user_role_scope_grants.effective_to IS NOT NULL
   OR identity.user_role_scope_grants.revoked_at IS NOT NULL
   OR identity.user_role_scope_grants.revoked_by_user_id IS NOT NULL
   OR identity.user_role_scope_grants.revoked_by_service_identity_id IS NOT NULL
   OR identity.user_role_scope_grants.revocation_reason_code IS NOT NULL
   OR identity.user_role_scope_grants.updated_by_user_id IS NOT NULL
   OR identity.user_role_scope_grants.updated_by_service_identity_id IS DISTINCT FROM EXCLUDED.updated_by_service_identity_id;

COMMIT;
