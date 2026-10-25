\set ON_ERROR_STOP on

BEGIN TRANSACTION READ ONLY;

DO $preflight$
DECLARE
    c_juan_user_id constant uuid := 'd94bf15c-75b8-4869-bb79-0f2ac02aa841';
    c_reference_user_id constant uuid := '77000000-0000-0000-0000-000000000012';
    c_site_id constant uuid := '2d1dcdf8-f563-537c-8542-0bde7cc9da97';
    c_site_group_id constant uuid := 'a6dbadf6-68b5-5bed-a7e0-a75faee70841';
    v_now timestamptz := clock_timestamp();
BEGIN
    IF current_database() <> 'exitpass_ist' THEN
        RAISE EXCEPTION 'PERSISTENT_IST_DATABASE_REQUIRED: refusing database %', current_database();
    END IF;

    IF (SELECT count(*) FROM identity.users
        WHERE (user_id = c_juan_user_id AND username = 'JuanDC05' AND username_normalized = 'juandc05')
           OR (user_id = c_reference_user_id AND username = 'uat-operations-supervisor'
               AND username_normalized = 'uat-operations-supervisor')) <> 2 THEN
        RAISE EXCEPTION 'TARGET_IDENTITY_MISMATCH: expected JuanDC05 and uat-operations-supervisor';
    END IF;

    IF EXISTS (
        SELECT 1 FROM identity.users
        WHERE user_id IN (c_juan_user_id, c_reference_user_id)
          AND user_status::text <> 'ACTIVE') THEN
        RAISE EXCEPTION 'TARGET_IDENTITY_NOT_ACTIVE';
    END IF;

    IF EXISTS (
        SELECT 1 FROM identity.users
        WHERE user_id = c_juan_user_id
          AND user_type::text NOT IN ('OTHER', 'OPERATIONS_USER')) THEN
        RAISE EXCEPTION 'JUANDC05_USER_TYPE_UNEXPECTED';
    END IF;

    IF EXISTS (
        SELECT 1 FROM identity.users
        WHERE user_id = c_reference_user_id
          AND user_type::text <> 'OPERATIONS_USER') THEN
        RAISE EXCEPTION 'REFERENCE_USER_TYPE_UNEXPECTED';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM sites.sites site
        JOIN sites.site_groups site_group ON site_group.site_group_id = site.site_group_id
        WHERE site.site_id = c_site_id
          AND site.site_code = 'PITX-LEVEL-3'
          AND site.site_status::text = 'ACTIVE'
          AND site.site_group_id = c_site_group_id
          AND site_group.site_group_code = 'PITX'
          AND site_group.site_group_status::text = 'ACTIVE') THEN
        RAISE EXCEPTION 'PITX_SCOPE_REFERENCE_INVALID';
    END IF;

    IF (SELECT count(*) FROM identity.roles
        WHERE role_code = 'OPERATIONS_SUPERVISOR'
          AND role_status::text = 'ACTIVE'
          AND role_provenance::text = 'CANONICAL_ROLE'
          AND effective_from <= now()
          AND (effective_to IS NULL OR effective_to > now())) <> 1 THEN
        RAISE EXCEPTION 'REQUIRED_CANONICAL_ROLE_UNAVAILABLE';
    END IF;

    IF (SELECT count(*) FROM identity.permissions
        WHERE permission_code IN (
            'fiscal-reporting.z.generate',
            'apt.access',
            'cash-custody-movements.acknowledge',
            'cash-custody-movements.correct',
            'cash-custody-movements.read')
          AND permission_status::text = 'ACTIVE') <> 5 THEN
        RAISE EXCEPTION 'CANONICAL_PARITY_PERMISSION_UNAVAILABLE';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM (VALUES (c_juan_user_id), (c_reference_user_id)) target(user_id)
        WHERE (SELECT count(*)
               FROM identity.user_roles assignment
               JOIN identity.roles role ON role.role_id = assignment.role_id
               JOIN identity.user_role_scope_grants scope_grant
                 ON scope_grant.user_role_id = assignment.user_role_id
               WHERE assignment.user_id = target.user_id
                 AND role.role_code = 'OPERATIONS_SUPERVISOR'
                 AND assignment.assignment_status::text = 'ACTIVE'
                 AND assignment.revoked_at IS NULL
                 AND assignment.effective_from <= v_now
                 AND (assignment.effective_to IS NULL OR assignment.effective_to > v_now)
                 AND scope_grant.scope_type::text = 'SITE'
                 AND scope_grant.site_id = c_site_id
                 AND scope_grant.site_group_id IS NULL
                 AND scope_grant.grant_status::text = 'ACTIVE'
                 AND scope_grant.revoked_at IS NULL
                 AND scope_grant.effective_from <= v_now
                 AND (scope_grant.effective_to IS NULL OR scope_grant.effective_to > v_now)) <> 1) THEN
        RAISE EXCEPTION 'TARGET_CANONICAL_OPERATIONS_SUPERVISOR_ASSIGNMENT_INVALID';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM identity.user_role_scope_grants scope_grant
        JOIN identity.user_roles assignment ON assignment.user_role_id = scope_grant.user_role_id
        WHERE assignment.user_id IN (c_juan_user_id, c_reference_user_id)
          AND assignment.assignment_status::text = 'ACTIVE'
          AND assignment.revoked_at IS NULL
          AND assignment.effective_from <= v_now
          AND (assignment.effective_to IS NULL OR assignment.effective_to > v_now)
          AND scope_grant.grant_status::text = 'ACTIVE'
          AND scope_grant.revoked_at IS NULL
          AND scope_grant.effective_from <= v_now
          AND (scope_grant.effective_to IS NULL OR scope_grant.effective_to > v_now)
          AND (scope_grant.scope_type::text <> 'SITE'
               OR scope_grant.site_id IS DISTINCT FROM c_site_id
               OR scope_grant.site_group_id IS NOT NULL)) THEN
        RAISE EXCEPTION 'UNEXPECTED_ACTIVE_SCOPE: reconciliation is PITX Level 3 Site-only';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM identity.user_roles assignment
        JOIN identity.roles role ON role.role_id = assignment.role_id
        JOIN identity.users user_record ON user_record.user_id = assignment.user_id
        WHERE role.role_code = 'OPERATIONS_SUPERVISOR'
          AND assignment.assignment_status::text = 'ACTIVE'
          AND assignment.revoked_at IS NULL
          AND assignment.effective_from <= now()
          AND (assignment.effective_to IS NULL OR assignment.effective_to > now())
          AND user_record.user_id <> c_juan_user_id
          AND user_record.user_type::text <> 'OPERATIONS_USER') THEN
        RAISE EXCEPTION 'INCOMPATIBLE_ACTIVE_OPERATIONS_SUPERVISOR_USER_TYPE';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM identity.user_role_scope_grants scope_grant
        JOIN identity.user_roles assignment ON assignment.user_role_id = scope_grant.user_role_id
        JOIN identity.roles role ON role.role_id = assignment.role_id
        WHERE role.role_code = 'OPERATIONS_SUPERVISOR'
          AND assignment.assignment_status::text = 'ACTIVE'
          AND assignment.revoked_at IS NULL
          AND assignment.effective_from <= v_now
          AND (assignment.effective_to IS NULL OR assignment.effective_to > v_now)
          AND scope_grant.grant_status::text = 'ACTIVE'
          AND scope_grant.revoked_at IS NULL
          AND scope_grant.effective_from <= v_now
          AND (scope_grant.effective_to IS NULL OR scope_grant.effective_to > v_now)
          AND (scope_grant.scope_type::text <> 'SITE'
               OR scope_grant.site_id IS NULL
               OR scope_grant.site_group_id IS NOT NULL)) THEN
        RAISE EXCEPTION 'OPERATIONS_SUPERVISOR_SCOPE_MUST_REMAIN_SITE_ONLY';
    END IF;
END
$preflight$;

SELECT user_id, username, display_name, user_type::text, user_status::text,
       credential_version, authorization_epoch, effective_from, effective_to
FROM identity.users
WHERE username_normalized IN ('uat-operations-supervisor', 'juandc05')
ORDER BY username_normalized;

SELECT user_record.username, role.role_code, role.role_provenance::text,
       assignment.assignment_status::text, assignment.assignment_reason_code,
       assignment.effective_from, assignment.effective_to, assignment.revoked_at,
       scope_grant.scope_type::text, scope_grant.site_id, site.site_code,
       scope_grant.site_group_id, site_group.site_group_code,
       scope_grant.grant_status::text, scope_grant.grant_reason_code
FROM identity.users user_record
JOIN identity.user_roles assignment ON assignment.user_id = user_record.user_id
JOIN identity.roles role ON role.role_id = assignment.role_id
LEFT JOIN identity.user_role_scope_grants scope_grant ON scope_grant.user_role_id = assignment.user_role_id
LEFT JOIN sites.sites site ON site.site_id = scope_grant.site_id
LEFT JOIN sites.site_groups site_group ON site_group.site_group_id = scope_grant.site_group_id
WHERE user_record.username_normalized IN ('uat-operations-supervisor', 'juandc05')
ORDER BY user_record.username_normalized, role.role_code, scope_grant.created_at;

WITH effective AS (
    SELECT user_record.username_normalized, role.role_code, permission.permission_code,
           permission.permission_description, permission.is_sensitive, scope_grant.scope_type::text,
           scope_grant.site_id, scope_grant.site_group_id
    FROM identity.users user_record
    JOIN identity.user_roles assignment ON assignment.user_id = user_record.user_id
    JOIN identity.roles role ON role.role_id = assignment.role_id
    JOIN identity.role_permissions binding ON binding.role_id = role.role_id
    JOIN identity.permissions permission ON permission.permission_id = binding.permission_id
    JOIN identity.user_role_scope_grants scope_grant ON scope_grant.user_role_id = assignment.user_role_id
    WHERE user_record.username_normalized IN ('uat-operations-supervisor', 'juandc05')
      AND user_record.user_status::text = 'ACTIVE'
      AND assignment.assignment_status::text = 'ACTIVE'
      AND assignment.revoked_at IS NULL
      AND assignment.effective_from <= now()
      AND (assignment.effective_to IS NULL OR assignment.effective_to > now())
      AND binding.binding_status::text = 'ACTIVE'
      AND binding.revoked_at IS NULL
      AND binding.effective_from <= now()
      AND (binding.effective_to IS NULL OR binding.effective_to > now())
      AND permission.permission_status::text = 'ACTIVE'
      AND scope_grant.grant_status::text = 'ACTIVE'
      AND scope_grant.revoked_at IS NULL
      AND scope_grant.effective_from <= now()
      AND (scope_grant.effective_to IS NULL OR scope_grant.effective_to > now())
)
SELECT permission_code, max(permission_description) AS description,
       bool_or(username_normalized = 'uat-operations-supervisor') AS reference_has,
       bool_or(username_normalized = 'juandc05') AS juandc05_has,
       string_agg(DISTINCT role_code, ', ' ORDER BY role_code)
           FILTER (WHERE username_normalized = 'uat-operations-supervisor') AS reference_sources,
       string_agg(DISTINCT role_code, ', ' ORDER BY role_code)
           FILTER (WHERE username_normalized = 'juandc05') AS juandc05_sources,
       bool_or(is_sensitive) AS sensitive
FROM effective
GROUP BY permission_code
ORDER BY permission_code;

SELECT count(*) AS canonical_permission_count,
       array_agg(permission.permission_code ORDER BY permission.permission_code) AS canonical_permissions
FROM identity.roles role
JOIN identity.role_permissions binding ON binding.role_id = role.role_id
JOIN identity.permissions permission ON permission.permission_id = binding.permission_id
WHERE role.role_code = 'OPERATIONS_SUPERVISOR'
  AND binding.binding_status::text = 'ACTIVE'
  AND binding.revoked_at IS NULL
  AND permission.permission_status::text = 'ACTIVE';

SELECT user_record.username, user_record.display_name, site.site_code,
       assignment.assignment_status::text, scope_grant.grant_status::text
FROM identity.roles role
JOIN identity.user_roles assignment ON assignment.role_id = role.role_id
JOIN identity.users user_record ON user_record.user_id = assignment.user_id
JOIN identity.user_role_scope_grants scope_grant ON scope_grant.user_role_id = assignment.user_role_id
LEFT JOIN sites.sites site ON site.site_id = scope_grant.site_id
WHERE role.role_code = 'OPERATIONS_SUPERVISOR'
ORDER BY user_record.username, site.site_code;

SELECT user_record.username, role.role_code, assignment.assignment_status::text,
       scope_grant.scope_type::text, site.site_code, scope_grant.grant_status::text
FROM identity.users user_record
JOIN identity.user_roles assignment ON assignment.user_id = user_record.user_id
JOIN identity.roles role ON role.role_id = assignment.role_id
LEFT JOIN identity.user_role_scope_grants scope_grant ON scope_grant.user_role_id = assignment.user_role_id
LEFT JOIN sites.sites site ON site.site_id = scope_grant.site_id
WHERE role.role_code IN (
    'APT_CASH_CUSTODY_MOVEMENT_OPERATOR',
    'APT_CASH_CUSTODY_SUPERVISOR',
    'APT_SITE_CUSTODIAN',
    'APT_CASH_COLLECTOR')
ORDER BY user_record.username, role.role_code;

SELECT user_record.username, session.session_audience::text, session.session_status::text,
       count(session.human_session_id) AS session_count,
       max(session.authenticated_at) AS latest_authentication
FROM identity.users user_record
LEFT JOIN identity.human_sessions session ON session.user_id = user_record.user_id
WHERE user_record.username_normalized IN ('uat-operations-supervisor', 'juandc05')
GROUP BY user_record.username, session.session_audience, session.session_status
ORDER BY user_record.username, session.session_audience, session.session_status;

ROLLBACK;
