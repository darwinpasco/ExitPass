\set ON_ERROR_STOP on

BEGIN TRANSACTION READ ONLY;

DO $$
DECLARE
    v_user_count integer;
    v_canonical_role_count integer;
    v_stale_role_count integer;
    v_canonical_assignment_count integer;
    v_scope_evidence_count integer;
BEGIN
    IF current_database() <> 'exitpass_ist' THEN
        RAISE EXCEPTION 'PERSISTENT_IST_DATABASE_REQUIRED: refusing database %', current_database();
    END IF;

    SELECT count(*) INTO v_user_count
    FROM identity.users
    WHERE user_id = '77000000-0000-0000-0000-000000000012'
      AND username = 'uat-operations-supervisor'
      AND username_normalized = 'uat-operations-supervisor';
    IF v_user_count <> 1 THEN
        RAISE EXCEPTION 'TARGET_IDENTITY_MISMATCH: expected the exact persistent IST UAT operations supervisor';
    END IF;

    IF EXISTS (
        SELECT 1 FROM identity.users
        WHERE user_id = '77000000-0000-0000-0000-000000000012'
          AND (user_status <> 'ACTIVE' OR user_type NOT IN ('SITE_OPERATOR', 'OPERATIONS_USER'))
    ) THEN
        RAISE EXCEPTION 'TARGET_IDENTITY_STATE_UNEXPECTED: user must be active and in the known SITE_OPERATOR-to-OPERATIONS_USER drift path';
    END IF;

    SELECT count(*) INTO v_canonical_role_count
    FROM identity.roles role
    JOIN identity.role_user_type_compatibility compatibility ON compatibility.role_id = role.role_id
    WHERE role.role_code = 'OPERATIONS_SUPERVISOR'
      AND role.role_status = 'ACTIVE'
      AND role.role_provenance = 'CANONICAL_ROLE'
      AND role.human_assignable
      AND role.direct_add_user_eligible
      AND compatibility.user_type = 'OPERATIONS_USER';
    IF v_canonical_role_count <> 1 THEN
        RAISE EXCEPTION 'CANONICAL_ROLE_UNAVAILABLE_OR_INCOMPATIBLE: expected one assignable OPERATIONS_SUPERVISOR compatible with OPERATIONS_USER';
    END IF;

    SELECT count(*) INTO v_stale_role_count
    FROM identity.roles
    WHERE role_code = 'UAT_OPERATIONS_SUPERVISOR'
      AND role_provenance = 'UAT_TEST_ROLE';
    IF v_stale_role_count <> 1 THEN
        RAISE EXCEPTION 'STALE_ROLE_STATE_UNEXPECTED: expected one historical UAT_OPERATIONS_SUPERVISOR role';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM identity.user_roles assignment
        JOIN identity.roles role ON role.role_id = assignment.role_id
        WHERE assignment.user_id = '77000000-0000-0000-0000-000000000012'
          AND assignment.assignment_status = 'ACTIVE'
          AND role.role_code NOT IN ('UAT_OPERATIONS_SUPERVISOR', 'OPERATIONS_SUPERVISOR')
    ) THEN
        RAISE EXCEPTION 'UNEXPECTED_ACTIVE_ROLE: refusing to rewrite unrelated authority';
    END IF;

    SELECT count(*) INTO v_canonical_assignment_count
    FROM identity.user_roles assignment
    JOIN identity.roles role ON role.role_id = assignment.role_id
    WHERE assignment.user_id = '77000000-0000-0000-0000-000000000012'
      AND role.role_code = 'OPERATIONS_SUPERVISOR';
    IF v_canonical_assignment_count > 1 THEN
        RAISE EXCEPTION 'AMBIGUOUS_CANONICAL_ASSIGNMENT: multiple OPERATIONS_SUPERVISOR assignments exist';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM sites.sites site
        JOIN sites.site_groups site_group ON site_group.site_group_id = site.site_group_id
        WHERE site.site_id = '2d1dcdf8-f563-537c-8542-0bde7cc9da97'
          AND site.site_code = 'PITX-LEVEL-3'
          AND site.site_status = 'ACTIVE'
          AND site.site_group_id = 'a6dbadf6-68b5-5bed-a7e0-a75faee70841'
          AND site_group.site_group_code = 'PITX'
          AND site_group.site_group_status = 'ACTIVE'
    ) THEN
        RAISE EXCEPTION 'PITX_SCOPE_REFERENCE_INVALID: the validated Site and Site Group relationship is unavailable';
    END IF;

    SELECT count(*) INTO v_scope_evidence_count
    FROM identity.user_role_scope_grants scope_grant
    JOIN identity.user_roles assignment ON assignment.user_role_id = scope_grant.user_role_id
    JOIN identity.roles role ON role.role_id = assignment.role_id
    WHERE assignment.user_id = '77000000-0000-0000-0000-000000000012'
      AND role.role_code = 'UAT_OPERATIONS_SUPERVISOR'
      AND scope_grant.scope_type = 'SITE'
      AND scope_grant.site_id = '2d1dcdf8-f563-537c-8542-0bde7cc9da97';
    IF v_scope_evidence_count <> 1 THEN
        RAISE EXCEPTION 'PITX_SITE_SCOPE_EVIDENCE_AMBIGUOUS: expected exactly one historical PITX Level 3 Site grant';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM identity.user_role_scope_grants scope_grant
        JOIN identity.user_roles assignment ON assignment.user_role_id = scope_grant.user_role_id
        JOIN identity.roles role ON role.role_id = assignment.role_id
        WHERE assignment.user_id = '77000000-0000-0000-0000-000000000012'
          AND role.role_code = 'UAT_OPERATIONS_SUPERVISOR'
          AND NOT (
              (scope_grant.scope_type = 'SITE' AND scope_grant.site_id = '2d1dcdf8-f563-537c-8542-0bde7cc9da97')
              OR (scope_grant.scope_type = 'SITE_GROUP' AND scope_grant.site_group_id = 'a6dbadf6-68b5-5bed-a7e0-a75faee70841')
          )
    ) THEN
        RAISE EXCEPTION 'UNEXPECTED_SCOPE_EVIDENCE: refusing ambiguous or broader historical scope';
    END IF;
END $$;

SELECT user_id, username, user_type::text, user_status::text,
       credential_version, authorization_epoch, row_version
FROM identity.users
WHERE user_id = '77000000-0000-0000-0000-000000000012';

SELECT assignment.user_role_id, role.role_id, role.role_code,
       role.role_provenance::text, role.human_assignable,
       role.direct_add_user_eligible, role.role_status::text,
       assignment.assignment_status::text, assignment.assignment_reason_code,
       assignment.effective_from, assignment.effective_to,
       assignment.revoked_at, assignment.revocation_reason_code
FROM identity.user_roles assignment
JOIN identity.roles role ON role.role_id = assignment.role_id
WHERE assignment.user_id = '77000000-0000-0000-0000-000000000012'
ORDER BY assignment.created_at, assignment.user_role_id;

SELECT scope_grant.user_role_scope_grant_id, scope_grant.user_role_id,
       role.role_code, scope_grant.scope_type::text, scope_grant.site_id,
       site.site_code, scope_grant.site_group_id, site_group.site_group_code,
       scope_grant.grant_status::text, scope_grant.grant_reason_code,
       scope_grant.effective_from, scope_grant.effective_to,
       scope_grant.revoked_at, scope_grant.revocation_reason_code
FROM identity.user_role_scope_grants scope_grant
JOIN identity.user_roles assignment ON assignment.user_role_id = scope_grant.user_role_id
JOIN identity.roles role ON role.role_id = assignment.role_id
LEFT JOIN sites.sites site ON site.site_id = scope_grant.site_id
LEFT JOIN sites.site_groups site_group ON site_group.site_group_id = scope_grant.site_group_id
WHERE assignment.user_id = '77000000-0000-0000-0000-000000000012'
ORDER BY scope_grant.created_at, scope_grant.user_role_scope_grant_id;

SELECT human_session_id, session_audience::text, session_status::text,
       authenticated_at, idle_expires_at, absolute_expires_at,
       credential_version_snapshot, authorization_epoch_snapshot,
       revoked_at, revocation_reason_code
FROM identity.human_sessions
WHERE user_id = '77000000-0000-0000-0000-000000000012'
ORDER BY created_at, human_session_id;

ROLLBACK;
