\set ON_ERROR_STOP on

SELECT pg_advisory_xact_lock(hashtextextended('exitpass:v1.3:persistent-ist:uat-operations-supervisor-role-reconciliation', 0));

DO $$
DECLARE
    c_user_id constant uuid := '77000000-0000-0000-0000-000000000012';
    c_username constant text := 'uat-operations-supervisor';
    c_site_id constant uuid := '2d1dcdf8-f563-537c-8542-0bde7cc9da97';
    c_site_group_id constant uuid := 'a6dbadf6-68b5-5bed-a7e0-a75faee70841';
    c_actor_id constant uuid := '79000000-0000-0000-0000-000000000003';
    c_reason constant text := 'PERSISTENT_IST_CANONICAL_ROLE_RECONCILIATION';
    c_assignment_id constant uuid := md5('exitpass:v1.3:persistent-ist:uat-operations-supervisor:OPERATIONS_SUPERVISOR')::uuid;
    c_scope_grant_id constant uuid := md5('exitpass:v1.3:persistent-ist:uat-operations-supervisor:OPERATIONS_SUPERVISOR:PITX-LEVEL-3')::uuid;
    v_now timestamptz := clock_timestamp();
    v_user identity.users%ROWTYPE;
    v_canonical_role identity.roles%ROWTYPE;
    v_stale_role identity.roles%ROWTYPE;
    v_canonical_assignment_count integer;
    v_canonical_assignment_id uuid;
    v_canonical_scope_count integer;
    v_canonical_scope_id uuid;
    v_scope_evidence_count integer;
    v_authority_changed boolean := false;
    v_revoked_sessions integer := 0;
BEGIN
    IF current_database() <> 'exitpass_ist' THEN
        RAISE EXCEPTION 'PERSISTENT_IST_DATABASE_REQUIRED: refusing database %', current_database();
    END IF;

    SELECT * INTO v_user
    FROM identity.users
    WHERE user_id = c_user_id
    FOR UPDATE;
    IF NOT FOUND OR v_user.username <> c_username OR v_user.username_normalized <> c_username THEN
        RAISE EXCEPTION 'TARGET_IDENTITY_MISMATCH: expected % / %', c_user_id, c_username;
    END IF;
    IF v_user.user_status <> 'ACTIVE' THEN
        RAISE EXCEPTION 'TARGET_USER_NOT_ACTIVE: status is %', v_user.user_status;
    END IF;
    IF v_user.user_type NOT IN ('SITE_OPERATOR', 'OPERATIONS_USER') THEN
        RAISE EXCEPTION 'TARGET_USER_TYPE_UNEXPECTED: refusing user type %', v_user.user_type;
    END IF;

    SELECT role.* INTO v_canonical_role
    FROM identity.roles role
    JOIN identity.role_user_type_compatibility compatibility ON compatibility.role_id = role.role_id
    WHERE role.role_code = 'OPERATIONS_SUPERVISOR'
      AND role.role_status = 'ACTIVE'
      AND role.role_provenance = 'CANONICAL_ROLE'
      AND role.human_assignable
      AND role.direct_add_user_eligible
      AND role.effective_from <= v_now
      AND (role.effective_to IS NULL OR role.effective_to > v_now)
      AND compatibility.user_type = 'OPERATIONS_USER';
    IF NOT FOUND THEN
        RAISE EXCEPTION 'CANONICAL_ROLE_UNAVAILABLE_OR_INCOMPATIBLE: OPERATIONS_SUPERVISOR / OPERATIONS_USER';
    END IF;
    IF EXISTS (
        SELECT 1
        FROM identity.roles role
        JOIN identity.role_user_type_compatibility compatibility ON compatibility.role_id = role.role_id
        WHERE role.role_code = 'OPERATIONS_SUPERVISOR'
          AND compatibility.user_type = 'OPERATIONS_USER'
          AND role.role_id <> v_canonical_role.role_id
    ) THEN
        RAISE EXCEPTION 'CANONICAL_ROLE_AMBIGUOUS: multiple compatible OPERATIONS_SUPERVISOR roles';
    END IF;

    SELECT * INTO v_stale_role
    FROM identity.roles
    WHERE role_code = 'UAT_OPERATIONS_SUPERVISOR'
      AND role_provenance = 'UAT_TEST_ROLE';
    IF NOT FOUND THEN
        RAISE EXCEPTION 'STALE_ROLE_STATE_UNEXPECTED: historical UAT_OPERATIONS_SUPERVISOR is unavailable';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM identity.service_identities
        WHERE service_identity_id = c_actor_id
          AND service_identity_code = 'MANAGEMENT_PLATFORM_UAT_IDENTITY_RBAC_SEED'
          AND identity_status = 'ACTIVE'
          AND effective_from <= v_now
          AND (effective_to IS NULL OR effective_to > v_now)
          AND revoked_at IS NULL
    ) THEN
        RAISE EXCEPTION 'RECONCILIATION_ACTOR_UNAVAILABLE: expected active UAT identity/RBAC service identity';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM identity.user_roles assignment
        JOIN identity.roles role ON role.role_id = assignment.role_id
        WHERE assignment.user_id = c_user_id
          AND assignment.assignment_status = 'ACTIVE'
          AND role.role_code NOT IN ('UAT_OPERATIONS_SUPERVISOR', 'OPERATIONS_SUPERVISOR')
    ) THEN
        RAISE EXCEPTION 'UNEXPECTED_ACTIVE_ROLE: refusing to rewrite unrelated authority';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM sites.sites site
        JOIN sites.site_groups site_group ON site_group.site_group_id = site.site_group_id
        WHERE site.site_id = c_site_id
          AND site.site_code = 'PITX-LEVEL-3'
          AND site.site_status = 'ACTIVE'
          AND site.site_group_id = c_site_group_id
          AND site_group.site_group_code = 'PITX'
          AND site_group.site_group_status = 'ACTIVE'
    ) THEN
        RAISE EXCEPTION 'PITX_SCOPE_REFERENCE_INVALID: validated Site and Site Group relationship is unavailable';
    END IF;

    SELECT count(*) INTO v_scope_evidence_count
    FROM identity.user_role_scope_grants scope_grant
    JOIN identity.user_roles assignment ON assignment.user_role_id = scope_grant.user_role_id
    WHERE assignment.user_id = c_user_id
      AND assignment.role_id = v_stale_role.role_id
      AND scope_grant.scope_type = 'SITE'
      AND scope_grant.site_id = c_site_id;
    IF v_scope_evidence_count <> 1 THEN
        RAISE EXCEPTION 'PITX_SITE_SCOPE_EVIDENCE_AMBIGUOUS: expected exactly one historical PITX Level 3 Site grant';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM identity.user_role_scope_grants scope_grant
        JOIN identity.user_roles assignment ON assignment.user_role_id = scope_grant.user_role_id
        WHERE assignment.user_id = c_user_id
          AND assignment.role_id = v_stale_role.role_id
          AND NOT (
              (scope_grant.scope_type = 'SITE' AND scope_grant.site_id = c_site_id)
              OR (scope_grant.scope_type = 'SITE_GROUP' AND scope_grant.site_group_id = c_site_group_id)
          )
    ) THEN
        RAISE EXCEPTION 'UNEXPECTED_SCOPE_EVIDENCE: refusing ambiguous or broader historical scope';
    END IF;

    SELECT count(*) INTO v_canonical_assignment_count
    FROM identity.user_roles
    WHERE user_id = c_user_id
      AND role_id = v_canonical_role.role_id;
    IF v_canonical_assignment_count > 1 THEN
        RAISE EXCEPTION 'AMBIGUOUS_CANONICAL_ASSIGNMENT: multiple canonical assignments exist';
    ELSIF v_canonical_assignment_count = 1 THEN
        SELECT user_role_id INTO v_canonical_assignment_id
        FROM identity.user_roles
        WHERE user_id = c_user_id
          AND role_id = v_canonical_role.role_id
        FOR UPDATE;
    ELSE
        IF EXISTS (SELECT 1 FROM identity.user_roles WHERE user_role_id = c_assignment_id) THEN
            RAISE EXCEPTION 'DETERMINISTIC_ASSIGNMENT_ID_COLLISION: %', c_assignment_id;
        END IF;
        v_canonical_assignment_id := c_assignment_id;
    END IF;

    IF EXISTS (
        SELECT 1
        FROM identity.user_role_scope_grants
        WHERE user_role_id = v_canonical_assignment_id
          AND NOT (scope_type = 'SITE' AND site_id = c_site_id)
    ) THEN
        RAISE EXCEPTION 'UNEXPECTED_CANONICAL_SCOPE: canonical assignment contains authority outside PITX Level 3 Site';
    END IF;

    SELECT count(*) INTO v_canonical_scope_count
    FROM identity.user_role_scope_grants
    WHERE user_role_id = v_canonical_assignment_id
      AND scope_type = 'SITE'
      AND site_id = c_site_id;
    IF v_canonical_scope_count > 1 THEN
        RAISE EXCEPTION 'AMBIGUOUS_CANONICAL_SCOPE: multiple PITX Level 3 grants exist';
    ELSIF v_canonical_scope_count = 1 THEN
        SELECT user_role_scope_grant_id INTO v_canonical_scope_id
        FROM identity.user_role_scope_grants
        WHERE user_role_id = v_canonical_assignment_id
          AND scope_type = 'SITE'
          AND site_id = c_site_id
        FOR UPDATE;
    ELSE
        IF EXISTS (SELECT 1 FROM identity.user_role_scope_grants WHERE user_role_scope_grant_id = c_scope_grant_id) THEN
            RAISE EXCEPTION 'DETERMINISTIC_SCOPE_ID_COLLISION: %', c_scope_grant_id;
        END IF;
        v_canonical_scope_id := c_scope_grant_id;
    END IF;

    v_authority_changed :=
        v_user.user_type <> 'OPERATIONS_USER'
        OR EXISTS (
            SELECT 1 FROM identity.user_roles
            WHERE user_id = c_user_id AND role_id = v_stale_role.role_id
              AND assignment_status = 'ACTIVE'
        )
        OR NOT EXISTS (
            SELECT 1 FROM identity.user_roles
            WHERE user_id = c_user_id AND role_id = v_canonical_role.role_id
              AND assignment_status = 'ACTIVE' AND revoked_at IS NULL
              AND effective_from <= v_now AND (effective_to IS NULL OR effective_to > v_now)
        )
        OR NOT EXISTS (
            SELECT 1 FROM identity.user_role_scope_grants
            WHERE user_role_id = v_canonical_assignment_id
              AND scope_type = 'SITE' AND site_id = c_site_id
              AND grant_status = 'ACTIVE' AND revoked_at IS NULL
              AND effective_from <= v_now AND (effective_to IS NULL OR effective_to > v_now)
        )
        OR EXISTS (
            SELECT 1
            FROM identity.user_role_scope_grants scope_grant
            JOIN identity.user_roles assignment ON assignment.user_role_id = scope_grant.user_role_id
            WHERE assignment.user_id = c_user_id
              AND assignment.role_id = v_stale_role.role_id
              AND scope_grant.grant_status = 'ACTIVE'
        );

    IF NOT v_authority_changed THEN
        RAISE NOTICE 'Persistent IST UAT operations supervisor is already reconciled; no mutation performed.';
        RETURN;
    END IF;

    IF v_canonical_assignment_count = 0 THEN
        INSERT INTO identity.user_roles (
            user_role_id, user_id, role_id, assignment_status,
            assignment_reason_code, assigned_by_service_identity_id,
            effective_from, effective_to, created_by_service_identity_id,
            updated_by_service_identity_id)
        VALUES (
            v_canonical_assignment_id, c_user_id, v_canonical_role.role_id, 'ACTIVE',
            c_reason, c_actor_id, v_now, NULL, c_actor_id, c_actor_id);
    ELSE
        UPDATE identity.user_roles
        SET assignment_status = 'ACTIVE', assignment_reason_code = c_reason,
            assigned_by_user_id = NULL, assigned_by_service_identity_id = c_actor_id,
            effective_from = CASE WHEN effective_from <= v_now THEN effective_from ELSE v_now END,
            effective_to = NULL, revoked_at = NULL, revoked_by_user_id = NULL,
            revoked_by_service_identity_id = NULL, revocation_reason_code = NULL,
            updated_at = v_now, updated_by_user_id = NULL,
            updated_by_service_identity_id = c_actor_id, row_version = row_version + 1
        WHERE user_role_id = v_canonical_assignment_id;
    END IF;

    IF v_canonical_scope_count = 0 THEN
        INSERT INTO identity.user_role_scope_grants (
            user_role_scope_grant_id, user_role_id, scope_type, site_id,
            grant_status, grant_reason_code, effective_from, effective_to,
            granted_by_service_identity_id, created_by_service_identity_id,
            updated_by_service_identity_id)
        VALUES (
            v_canonical_scope_id, v_canonical_assignment_id, 'SITE', c_site_id,
            'ACTIVE', c_reason, v_now, NULL, c_actor_id, c_actor_id, c_actor_id);
    ELSE
        UPDATE identity.user_role_scope_grants
        SET grant_status = 'ACTIVE', grant_reason_code = c_reason,
            effective_from = CASE WHEN effective_from <= v_now THEN effective_from ELSE v_now END,
            effective_to = NULL, granted_by_user_id = NULL,
            granted_by_service_identity_id = c_actor_id, revoked_at = NULL,
            revoked_by_user_id = NULL, revoked_by_service_identity_id = NULL,
            revocation_reason_code = NULL, updated_at = v_now,
            updated_by_user_id = NULL, updated_by_service_identity_id = c_actor_id,
            row_version = row_version + 1
        WHERE user_role_scope_grant_id = v_canonical_scope_id;
    END IF;

    UPDATE identity.user_role_scope_grants scope_grant
    SET grant_status = 'REVOKED', effective_to = LEAST(COALESCE(scope_grant.effective_to, v_now), v_now),
        revoked_at = v_now, revoked_by_user_id = NULL,
        revoked_by_service_identity_id = c_actor_id,
        revocation_reason_code = c_reason, updated_at = v_now,
        updated_by_user_id = NULL, updated_by_service_identity_id = c_actor_id,
        row_version = scope_grant.row_version + 1
    FROM identity.user_roles assignment
    WHERE scope_grant.user_role_id = assignment.user_role_id
      AND assignment.user_id = c_user_id
      AND assignment.role_id = v_stale_role.role_id
      AND scope_grant.grant_status = 'ACTIVE';

    UPDATE identity.user_roles
    SET assignment_status = 'REVOKED', effective_to = LEAST(COALESCE(effective_to, v_now), v_now),
        revoked_at = v_now, revoked_by_user_id = NULL,
        revoked_by_service_identity_id = c_actor_id,
        revocation_reason_code = c_reason, updated_at = v_now,
        updated_by_user_id = NULL, updated_by_service_identity_id = c_actor_id,
        row_version = row_version + 1
    WHERE user_id = c_user_id
      AND role_id = v_stale_role.role_id
      AND assignment_status = 'ACTIVE';

    UPDATE identity.users
    SET user_type = 'OPERATIONS_USER', authorization_epoch = authorization_epoch + 1,
        updated_at = v_now, updated_by_user_id = NULL,
        updated_by_service_identity_id = c_actor_id, row_version = row_version + 1
    WHERE user_id = c_user_id;

    UPDATE identity.human_sessions
    SET session_status = 'REVOKED', revoked_at = v_now,
        revoked_by_user_id = NULL, revoked_by_service_identity_id = c_actor_id,
        revocation_reason_code = c_reason, updated_at = v_now,
        updated_by_user_id = NULL, updated_by_service_identity_id = c_actor_id,
        row_version = row_version + 1
    WHERE user_id = c_user_id
      AND session_status = 'ACTIVE';
    GET DIAGNOSTICS v_revoked_sessions = ROW_COUNT;

    IF (SELECT credential_version FROM identity.users WHERE user_id = c_user_id) <> v_user.credential_version THEN
        RAISE EXCEPTION 'CREDENTIAL_VERSION_CHANGED: reconciliation must not change credentials';
    END IF;
    IF (SELECT authorization_epoch FROM identity.users WHERE user_id = c_user_id) <> v_user.authorization_epoch + 1 THEN
        RAISE EXCEPTION 'AUTHORIZATION_EPOCH_INVALID: expected exactly one increment';
    END IF;
    IF NOT EXISTS (
        SELECT 1
        FROM identity.user_roles assignment
        JOIN identity.roles role ON role.role_id = assignment.role_id
        JOIN identity.user_role_scope_grants scope_grant ON scope_grant.user_role_id = assignment.user_role_id
        WHERE assignment.user_id = c_user_id
          AND role.role_code = 'OPERATIONS_SUPERVISOR'
          AND assignment.assignment_status = 'ACTIVE' AND assignment.revoked_at IS NULL
          AND scope_grant.scope_type = 'SITE' AND scope_grant.site_id = c_site_id
          AND scope_grant.grant_status = 'ACTIVE' AND scope_grant.revoked_at IS NULL
    ) THEN
        RAISE EXCEPTION 'RECONCILIATION_POSTCONDITION_FAILED: canonical role and scope are not effective';
    END IF;
    IF EXISTS (
        SELECT 1
        FROM identity.user_roles assignment
        JOIN identity.roles role ON role.role_id = assignment.role_id
        WHERE assignment.user_id = c_user_id
          AND role.role_code = 'UAT_OPERATIONS_SUPERVISOR'
          AND assignment.assignment_status = 'ACTIVE'
    ) THEN
        RAISE EXCEPTION 'RECONCILIATION_POSTCONDITION_FAILED: stale role remains effective';
    END IF;

    RAISE NOTICE 'Persistent IST UAT operations supervisor reconciled; authorization epoch % -> %, sessions revoked %',
        v_user.authorization_epoch, v_user.authorization_epoch + 1, v_revoked_sessions;
END $$;

SELECT user_id, username, user_type::text, user_status::text,
       credential_version, authorization_epoch, row_version
FROM identity.users
WHERE user_id = '77000000-0000-0000-0000-000000000012';

SELECT assignment.user_role_id, role.role_code, assignment.assignment_status::text,
       assignment.assignment_reason_code, assignment.revoked_at,
       assignment.revocation_reason_code
FROM identity.user_roles assignment
JOIN identity.roles role ON role.role_id = assignment.role_id
WHERE assignment.user_id = '77000000-0000-0000-0000-000000000012'
ORDER BY role.role_code, assignment.created_at;

SELECT scope_grant.user_role_scope_grant_id, role.role_code,
       scope_grant.scope_type::text, scope_grant.site_id,
       scope_grant.site_group_id, scope_grant.grant_status::text,
       scope_grant.grant_reason_code, scope_grant.revoked_at,
       scope_grant.revocation_reason_code
FROM identity.user_role_scope_grants scope_grant
JOIN identity.user_roles assignment ON assignment.user_role_id = scope_grant.user_role_id
JOIN identity.roles role ON role.role_id = assignment.role_id
WHERE assignment.user_id = '77000000-0000-0000-0000-000000000012'
ORDER BY role.role_code, scope_grant.created_at;
