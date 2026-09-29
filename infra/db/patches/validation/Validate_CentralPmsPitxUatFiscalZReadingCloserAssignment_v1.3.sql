DO $validation$
DECLARE
    target_site constant uuid := '2d1dcdf8-f563-537c-8542-0bde7cc9da97';
BEGIN
    IF (SELECT count(*) FROM identity.roles
        WHERE role_code = 'FISCAL_Z_READING_CLOSER'
          AND role_status::text = 'ACTIVE'
          AND effective_to IS NULL) <> 1 THEN
        RAISE EXCEPTION 'FISCAL_Z_READING_CLOSER must be active.';
    END IF;

    IF (SELECT count(*)
        FROM identity.role_permissions rp
        JOIN identity.roles r ON r.role_id = rp.role_id
        JOIN identity.permissions p ON p.permission_id = rp.permission_id
        WHERE r.role_code = 'FISCAL_Z_READING_CLOSER'
          AND p.permission_code = 'fiscal-reporting.z.generate'
          AND rp.binding_status::text = 'ACTIVE'
          AND rp.revoked_at IS NULL
          AND rp.effective_from <= now()
          AND (rp.effective_to IS NULL OR rp.effective_to > now())) <> 1 THEN
        RAISE EXCEPTION 'FISCAL_Z_READING_CLOSER must have exactly one active Z-generate binding.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM identity.role_permissions rp
        JOIN identity.roles r ON r.role_id = rp.role_id
        JOIN identity.permissions p ON p.permission_id = rp.permission_id
        WHERE r.role_code = 'FISCAL_Z_READING_CLOSER'
          AND p.permission_code <> 'fiscal-reporting.z.generate'
          AND rp.binding_status::text = 'ACTIVE'
          AND rp.revoked_at IS NULL
          AND rp.effective_from <= now()
          AND (rp.effective_to IS NULL OR rp.effective_to > now())
    ) THEN
        RAISE EXCEPTION 'FISCAL_Z_READING_CLOSER contains unrelated active permissions.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM identity.role_permissions rp
        JOIN identity.roles r ON r.role_id = rp.role_id
        JOIN identity.permissions p ON p.permission_id = rp.permission_id
        WHERE r.role_code IN ('OPERATIONS_SUPERVISOR', 'SITE_OPERATOR')
          AND p.permission_code = 'fiscal-reporting.z.generate'
          AND rp.binding_status::text = 'ACTIVE'
          AND rp.revoked_at IS NULL
          AND rp.effective_from <= now()
          AND (rp.effective_to IS NULL OR rp.effective_to > now())
    ) THEN
        RAISE EXCEPTION 'Shared operational roles must not grant Z-close authority.';
    END IF;

    IF (SELECT count(*)
        FROM identity.users u
        JOIN identity.user_roles ur ON ur.user_id = u.user_id
        JOIN identity.roles r ON r.role_id = ur.role_id
        WHERE u.username_normalized = 'uat-operations-supervisor'
          AND r.role_code IN ('OPERATIONS_SUPERVISOR', 'FISCAL_Z_READING_CLOSER')
          AND ur.assignment_status::text = 'ACTIVE'
          AND ur.revoked_at IS NULL
          AND ur.effective_from <= now()
          AND (ur.effective_to IS NULL OR ur.effective_to > now())) <> 2 THEN
        RAISE EXCEPTION 'uat-operations-supervisor must have active Operations Supervisor and Fiscal Z Reading Closer roles.';
    END IF;

    IF (SELECT count(*)
        FROM identity.users u
        JOIN identity.user_roles ur ON ur.user_id = u.user_id
        JOIN identity.roles r ON r.role_id = ur.role_id
        JOIN identity.user_role_scope_grants g ON g.user_role_id = ur.user_role_id
        WHERE u.username_normalized = 'uat-operations-supervisor'
          AND r.role_code = 'FISCAL_Z_READING_CLOSER'
          AND ur.assignment_status::text = 'ACTIVE'
          AND ur.revoked_at IS NULL
          AND g.scope_type::text = 'SITE'
          AND g.site_id = target_site
          AND g.grant_status::text = 'ACTIVE'
          AND g.revoked_at IS NULL
          AND g.effective_from <= now()
          AND (g.effective_to IS NULL OR g.effective_to > now())) <> 1 THEN
        RAISE EXCEPTION 'uat-operations-supervisor must have exactly one active PITX Level 3 closer scope.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM identity.users u
        JOIN identity.user_roles ur ON ur.user_id = u.user_id
        JOIN identity.roles r ON r.role_id = ur.role_id
        JOIN identity.user_role_scope_grants g ON g.user_role_id = ur.user_role_id
        WHERE u.username_normalized = 'uat-operations-supervisor'
          AND r.role_code = 'FISCAL_Z_READING_CLOSER'
          AND ur.assignment_status::text = 'ACTIVE'
          AND g.grant_status::text = 'ACTIVE'
          AND (g.scope_type::text <> 'SITE' OR g.site_id <> target_site)
    ) THEN
        RAISE EXCEPTION 'uat-operations-supervisor closer authority must be scoped only to PITX Level 3.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM identity.users u
        JOIN identity.user_roles ur ON ur.user_id = u.user_id
        JOIN identity.roles r ON r.role_id = ur.role_id
        WHERE r.role_code = 'FISCAL_Z_READING_CLOSER'
          AND u.username_normalized <> 'uat-operations-supervisor'
          AND ur.assignment_status::text = 'ACTIVE'
          AND ur.revoked_at IS NULL
          AND ur.effective_from <= now()
          AND (ur.effective_to IS NULL OR ur.effective_to > now())
    ) THEN
        RAISE EXCEPTION 'A non-target user retains active Fiscal Z Reading Closer authority.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM identity.users u
        JOIN identity.user_roles ur ON ur.user_id = u.user_id
        JOIN identity.roles r ON r.role_id = ur.role_id
        WHERE u.username_normalized = 'juandc01'
          AND r.role_code = 'FISCAL_Z_READING_CLOSER'
          AND ur.assignment_status::text = 'ACTIVE'
    ) THEN
        RAISE EXCEPTION 'JuanDC01 must not retain an active Fiscal Z Reading Closer assignment.';
    END IF;

    RAISE NOTICE 'CENTRAL_PMS_PITX_UAT_FISCAL_Z_READING_CLOSER_ASSIGNMENT_VALID';
END
$validation$;

SELECT 'PASS' AS pitx_uat_fiscal_z_reading_closer_assignment_validation;
