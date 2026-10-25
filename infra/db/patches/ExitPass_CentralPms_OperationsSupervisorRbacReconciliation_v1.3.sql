-- ExitPass v1.3 canonical Operations Supervisor RBAC reconciliation.
-- Adds only the approved 30-permission SITE-scoped role contract.
BEGIN;

SELECT pg_advisory_xact_lock(
    hashtextextended('exitpass:v1.3:operations-supervisor-rbac-reconciliation', 0));

CREATE TEMP TABLE operations_supervisor_expected_permissions (
    permission_code varchar(96) PRIMARY KEY
) ON COMMIT DROP;

INSERT INTO operations_supervisor_expected_permissions VALUES
    ('apt.access'),
    ('cash-custody-movements.acknowledge'),
    ('cash-custody-movements.correct'),
    ('cash-custody-movements.read'),
    ('fiscal-issuance.status.read'),
    ('fiscal-issuance.void.command'),
    ('fiscal-reporting.ej.export'),
    ('fiscal-reporting.ej.read'),
    ('fiscal-reporting.x.generate'),
    ('fiscal-reporting.x.read'),
    ('fiscal-reporting.z.generate'),
    ('fiscal-reporting.z.read'),
    ('operator-workflow-audit.view'),
    ('projection-health.view'),
    ('shift.manage'),
    ('shift.view'),
    ('statutory-discounts.decision.approve'),
    ('statutory-discounts.decision.reject'),
    ('statutory-discounts.decision.review'),
    ('statutory-discounts.draft.create'),
    ('statutory-discounts.draft.view'),
    ('statutory-discounts.evidence.capture'),
    ('statutory-discounts.evidence.review.view'),
    ('statutory-discounts.evidence.view'),
    ('statutory-discounts.policy.resolve'),
    ('statutory-discounts.review.detail.read'),
    ('statutory-discounts.review.queue.read'),
    ('statutory-discounts.session.lookup'),
    ('ticket.lookup'),
    ('vendor-acknowledgments.view');

CREATE TEMP TABLE operations_supervisor_reconciliation_original_users (
    user_id uuid PRIMARY KEY,
    credential_version bigint NOT NULL,
    authorization_epoch bigint NOT NULL
) ON COMMIT DROP;

CREATE TEMP TABLE operations_supervisor_reconciliation_changed_users (
    user_id uuid PRIMARY KEY
) ON COMMIT DROP;

DO $reconciliation$
DECLARE
    c_juan_user_id constant uuid := 'd94bf15c-75b8-4869-bb79-0f2ac02aa841';
    c_reference_user_id constant uuid := '77000000-0000-0000-0000-000000000012';
    c_site_id constant uuid := '2d1dcdf8-f563-537c-8542-0bde7cc9da97';
    c_site_group_id constant uuid := 'a6dbadf6-68b5-5bed-a7e0-a75faee70841';
    c_actor_id constant uuid := '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978';
    c_reason constant text := 'OPERATIONS_SUPERVISOR_CANONICAL_PARITY';
    v_now timestamptz := clock_timestamp();
    v_role_id uuid;
    v_permission record;
    v_permission_id uuid;
    v_binding_id uuid;
    v_binding_count integer;
    v_authority_changed boolean := false;
BEGIN
    IF current_database() <> 'exitpass_ist' THEN
        RAISE EXCEPTION 'PERSISTENT_IST_DATABASE_REQUIRED: refusing database %', current_database();
    END IF;

    SELECT role.role_id INTO STRICT v_role_id
    FROM identity.roles role
    WHERE role.role_code = 'OPERATIONS_SUPERVISOR'
      AND role.role_status::text = 'ACTIVE'
      AND role.role_provenance::text = 'CANONICAL_ROLE'
      AND role.role_type::text = 'OPERATIONS'
      AND role.human_assignable
      AND role.direct_add_user_eligible
      AND role.effective_from <= v_now
      AND (role.effective_to IS NULL OR role.effective_to > v_now);

    IF (SELECT count(*)
        FROM identity.role_user_type_compatibility compatibility
        WHERE compatibility.role_id = v_role_id
          AND compatibility.user_type::text = 'OPERATIONS_USER') <> 1 OR EXISTS (
        SELECT 1
        FROM identity.role_user_type_compatibility compatibility
        WHERE compatibility.role_id = v_role_id
          AND compatibility.user_type::text <> 'OPERATIONS_USER') THEN
        RAISE EXCEPTION 'OPERATIONS_SUPERVISOR_USER_TYPE_CONTRACT_INVALID';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM identity.service_identities
        WHERE service_identity_id = c_actor_id
          AND service_identity_code = 'seed.reference-data'
          AND identity_status::text = 'ACTIVE'
          AND effective_from <= v_now
          AND (effective_to IS NULL OR effective_to > v_now)
          AND revoked_at IS NULL) THEN
        RAISE EXCEPTION 'RECONCILIATION_ACTOR_UNAVAILABLE';
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

    IF NOT EXISTS (
        SELECT 1 FROM identity.users
        WHERE user_id = c_juan_user_id
          AND username = 'JuanDC05'
          AND username_normalized = 'juandc05'
          AND user_status::text = 'ACTIVE'
          AND user_type::text IN ('OTHER', 'OPERATIONS_USER')) THEN
        RAISE EXCEPTION 'JUANDC05_IDENTITY_STATE_UNEXPECTED';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM identity.users
        WHERE user_id = c_reference_user_id
          AND username = 'uat-operations-supervisor'
          AND username_normalized = 'uat-operations-supervisor'
          AND user_status::text = 'ACTIVE'
          AND user_type::text = 'OPERATIONS_USER') THEN
        RAISE EXCEPTION 'REFERENCE_IDENTITY_STATE_UNEXPECTED';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM (VALUES (c_juan_user_id), (c_reference_user_id)) target(user_id)
        WHERE (SELECT count(*)
               FROM identity.user_roles assignment
               JOIN identity.user_role_scope_grants scope_grant
                 ON scope_grant.user_role_id = assignment.user_role_id
               WHERE assignment.user_id = target.user_id
                 AND assignment.role_id = v_role_id
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
        FROM identity.user_roles assignment
        JOIN identity.users user_record ON user_record.user_id = assignment.user_id
        WHERE assignment.role_id = v_role_id
          AND assignment.assignment_status::text = 'ACTIVE'
          AND assignment.revoked_at IS NULL
          AND assignment.effective_from <= v_now
          AND (assignment.effective_to IS NULL OR assignment.effective_to > v_now)
          AND user_record.user_id <> c_juan_user_id
          AND user_record.user_type::text <> 'OPERATIONS_USER') THEN
        RAISE EXCEPTION 'INCOMPATIBLE_ACTIVE_OPERATIONS_SUPERVISOR_USER_TYPE';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM identity.user_roles assignment
        WHERE assignment.role_id = v_role_id
          AND assignment.assignment_status::text = 'ACTIVE'
          AND assignment.revoked_at IS NULL
          AND assignment.effective_from <= v_now
          AND (assignment.effective_to IS NULL OR assignment.effective_to > v_now)
          AND NOT EXISTS (
              SELECT 1
              FROM identity.user_role_scope_grants scope_grant
              WHERE scope_grant.user_role_id = assignment.user_role_id
                AND scope_grant.grant_status::text = 'ACTIVE'
                AND scope_grant.revoked_at IS NULL
                AND scope_grant.effective_from <= v_now
                AND (scope_grant.effective_to IS NULL OR scope_grant.effective_to > v_now))) THEN
        RAISE EXCEPTION 'ACTIVE_OPERATIONS_SUPERVISOR_WITHOUT_SCOPE';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM identity.user_role_scope_grants scope_grant
        JOIN identity.user_roles assignment ON assignment.user_role_id = scope_grant.user_role_id
        WHERE assignment.role_id = v_role_id
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

    IF EXISTS (
        SELECT 1
        FROM identity.role_permissions binding
        JOIN identity.permissions permission ON permission.permission_id = binding.permission_id
        WHERE binding.role_id = v_role_id
          AND binding.binding_status::text = 'ACTIVE'
          AND binding.revoked_at IS NULL
          AND NOT EXISTS (
              SELECT 1 FROM operations_supervisor_expected_permissions expected
              WHERE expected.permission_code = permission.permission_code)) THEN
        RAISE EXCEPTION 'OPERATIONS_SUPERVISOR_HAS_UNAPPROVED_PERMISSION';
    END IF;

    INSERT INTO operations_supervisor_reconciliation_original_users (
        user_id, credential_version, authorization_epoch)
    SELECT DISTINCT user_record.user_id,
           user_record.credential_version,
           user_record.authorization_epoch
    FROM identity.users user_record
    JOIN identity.user_roles assignment ON assignment.user_id = user_record.user_id
    WHERE assignment.role_id = v_role_id
      AND assignment.assignment_status::text = 'ACTIVE'
      AND assignment.revoked_at IS NULL
      AND assignment.effective_from <= v_now
      AND (assignment.effective_to IS NULL OR assignment.effective_to > v_now);

    UPDATE identity.users
    SET user_type = 'OPERATIONS_USER',
        updated_at = v_now,
        updated_by_user_id = NULL,
        updated_by_service_identity_id = c_actor_id,
        row_version = row_version + 1
    WHERE user_id = c_juan_user_id
      AND user_type::text = 'OTHER';
    IF FOUND THEN
        INSERT INTO operations_supervisor_reconciliation_changed_users VALUES (c_juan_user_id)
        ON CONFLICT DO NOTHING;
    END IF;

    FOR v_permission IN
        SELECT expected.permission_code
        FROM operations_supervisor_expected_permissions expected
        ORDER BY expected.permission_code
    LOOP
        SELECT permission.permission_id INTO STRICT v_permission_id
        FROM identity.permissions permission
        WHERE permission.permission_code = v_permission.permission_code
          AND permission.permission_status::text = 'ACTIVE';

        SELECT count(*) INTO v_binding_count
        FROM identity.role_permissions binding
        WHERE binding.role_id = v_role_id
          AND binding.permission_id = v_permission_id;
        IF v_binding_count > 1 THEN
            RAISE EXCEPTION 'AMBIGUOUS_ROLE_PERMISSION_BINDING: %', v_permission.permission_code;
        ELSIF v_binding_count = 0 THEN
            v_binding_id := md5(
                'exitpass:v1.3:operations-supervisor:permission:' ||
                v_permission.permission_code)::uuid;
            IF EXISTS (
                SELECT 1 FROM identity.role_permissions
                WHERE role_permission_id = v_binding_id) THEN
                RAISE EXCEPTION 'DETERMINISTIC_ROLE_PERMISSION_ID_COLLISION: %', v_binding_id;
            END IF;

            INSERT INTO identity.role_permissions (
                role_permission_id, role_id, permission_id, binding_status,
                binding_reason_code, assigned_by_service_identity_id,
                effective_from, created_by_service_identity_id,
                updated_by_service_identity_id)
            VALUES (
                v_binding_id, v_role_id, v_permission_id, 'ACTIVE',
                c_reason, c_actor_id, v_now, c_actor_id, c_actor_id);
            v_authority_changed := true;
        ELSE
            SELECT binding.role_permission_id INTO v_binding_id
            FROM identity.role_permissions binding
            WHERE binding.role_id = v_role_id
              AND binding.permission_id = v_permission_id
            FOR UPDATE;

            IF NOT EXISTS (
                SELECT 1 FROM identity.role_permissions binding
                WHERE binding.role_permission_id = v_binding_id
                  AND binding.binding_status::text = 'ACTIVE'
                  AND binding.revoked_at IS NULL
                  AND binding.effective_from <= v_now
                  AND (binding.effective_to IS NULL OR binding.effective_to > v_now)) THEN
                UPDATE identity.role_permissions
                SET binding_status = 'ACTIVE',
                    binding_reason_code = c_reason,
                    assigned_by_user_id = NULL,
                    assigned_by_service_identity_id = c_actor_id,
                    effective_from = v_now,
                    effective_to = NULL,
                    revoked_at = NULL,
                    revoked_by_user_id = NULL,
                    revoked_by_service_identity_id = NULL,
                    revocation_reason_code = NULL,
                    updated_at = v_now,
                    updated_by_user_id = NULL,
                    updated_by_service_identity_id = c_actor_id,
                    row_version = row_version + 1
                WHERE role_permission_id = v_binding_id;
                v_authority_changed := true;
            END IF;
        END IF;
    END LOOP;

    IF (SELECT count(*)
        FROM identity.role_permissions binding
        JOIN identity.permissions permission ON permission.permission_id = binding.permission_id
        WHERE binding.role_id = v_role_id
          AND binding.binding_status::text = 'ACTIVE'
          AND binding.revoked_at IS NULL
          AND binding.effective_from <= v_now
          AND (binding.effective_to IS NULL OR binding.effective_to > v_now)
          AND permission.permission_status::text = 'ACTIVE') <> 30 THEN
        RAISE EXCEPTION 'OPERATIONS_SUPERVISOR_PERMISSION_COUNT_NOT_30';
    END IF;

    IF v_authority_changed THEN
        INSERT INTO operations_supervisor_reconciliation_changed_users (user_id)
        SELECT DISTINCT assignment.user_id
        FROM identity.user_roles assignment
        WHERE assignment.role_id = v_role_id
          AND assignment.assignment_status::text = 'ACTIVE'
          AND assignment.revoked_at IS NULL
          AND assignment.effective_from <= v_now
          AND (assignment.effective_to IS NULL OR assignment.effective_to > v_now)
        ON CONFLICT DO NOTHING;
    END IF;

    UPDATE identity.users user_record
    SET authorization_epoch = user_record.authorization_epoch + 1,
        updated_at = v_now,
        updated_by_user_id = NULL,
        updated_by_service_identity_id = c_actor_id,
        row_version = user_record.row_version + 1
    WHERE EXISTS (
        SELECT 1 FROM operations_supervisor_reconciliation_changed_users changed
        WHERE changed.user_id = user_record.user_id);

    UPDATE identity.human_sessions session
    SET session_status = 'REVOKED',
        revoked_at = v_now,
        revoked_by_user_id = NULL,
        revoked_by_service_identity_id = c_actor_id,
        revocation_reason_code = c_reason,
        updated_at = v_now,
        updated_by_user_id = NULL,
        updated_by_service_identity_id = c_actor_id,
        row_version = session.row_version + 1
    WHERE session.session_status::text = 'ACTIVE'
      AND EXISTS (
          SELECT 1 FROM operations_supervisor_reconciliation_changed_users changed
          WHERE changed.user_id = session.user_id);

    IF EXISTS (
        SELECT 1
        FROM operations_supervisor_reconciliation_original_users original
        JOIN identity.users reconciled_user ON reconciled_user.user_id = original.user_id
        WHERE reconciled_user.credential_version <> original.credential_version) THEN
        RAISE EXCEPTION 'CREDENTIAL_VERSION_CHANGED';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM operations_supervisor_reconciliation_original_users original
        JOIN identity.users reconciled_user ON reconciled_user.user_id = original.user_id
        LEFT JOIN operations_supervisor_reconciliation_changed_users changed
          ON changed.user_id = original.user_id
        WHERE reconciled_user.authorization_epoch <>
              original.authorization_epoch + CASE WHEN changed.user_id IS NULL THEN 0 ELSE 1 END) THEN
        RAISE EXCEPTION 'AUTHORIZATION_EPOCH_INVALID';
    END IF;
END
$reconciliation$;

COMMIT;
