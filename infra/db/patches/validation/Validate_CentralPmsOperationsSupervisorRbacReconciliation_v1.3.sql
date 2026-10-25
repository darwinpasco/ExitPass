DO $validation$
DECLARE
    c_juan_user_id constant uuid := 'd94bf15c-75b8-4869-bb79-0f2ac02aa841';
    c_reference_user_id constant uuid := '77000000-0000-0000-0000-000000000012';
    c_site_id constant uuid := '2d1dcdf8-f563-537c-8542-0bde7cc9da97';
    c_expected_permissions constant text[] := ARRAY[
        'apt.access',
        'cash-custody-movements.acknowledge',
        'cash-custody-movements.correct',
        'cash-custody-movements.read',
        'fiscal-issuance.status.read',
        'fiscal-issuance.void.command',
        'fiscal-reporting.ej.export',
        'fiscal-reporting.ej.read',
        'fiscal-reporting.x.generate',
        'fiscal-reporting.x.read',
        'fiscal-reporting.z.generate',
        'fiscal-reporting.z.read',
        'operator-workflow-audit.view',
        'projection-health.view',
        'shift.manage',
        'shift.view',
        'statutory-discounts.decision.approve',
        'statutory-discounts.decision.reject',
        'statutory-discounts.decision.review',
        'statutory-discounts.draft.create',
        'statutory-discounts.draft.view',
        'statutory-discounts.evidence.capture',
        'statutory-discounts.evidence.review.view',
        'statutory-discounts.evidence.view',
        'statutory-discounts.policy.resolve',
        'statutory-discounts.review.detail.read',
        'statutory-discounts.review.queue.read',
        'statutory-discounts.session.lookup',
        'ticket.lookup',
        'vendor-acknowledgments.view'
    ];
    v_role_id uuid;
BEGIN
    IF current_database() <> 'exitpass_ist' THEN
        RAISE EXCEPTION 'PERSISTENT_IST_DATABASE_REQUIRED: refusing database %', current_database();
    END IF;

    SELECT role.role_id INTO STRICT v_role_id
    FROM identity.roles role
    WHERE role.role_code = 'OPERATIONS_SUPERVISOR'
      AND role.role_status::text = 'ACTIVE'
      AND role.role_provenance::text = 'CANONICAL_ROLE'
      AND role.human_assignable
      AND role.direct_add_user_eligible;

    IF cardinality(c_expected_permissions) <> 30 THEN
        RAISE EXCEPTION 'EXPECTED_PERMISSION_CONTRACT_NOT_30';
    END IF;

    IF (SELECT count(*)
        FROM identity.role_permissions binding
        JOIN identity.permissions permission ON permission.permission_id = binding.permission_id
        WHERE binding.role_id = v_role_id
          AND binding.binding_status::text = 'ACTIVE'
          AND binding.revoked_at IS NULL
          AND binding.effective_from <= now()
          AND (binding.effective_to IS NULL OR binding.effective_to > now())
          AND permission.permission_status::text = 'ACTIVE') <> 30 THEN
        RAISE EXCEPTION 'OPERATIONS_SUPERVISOR_PERMISSION_COUNT_NOT_30';
    END IF;

    IF EXISTS (
        SELECT unnest(c_expected_permissions) AS permission_code
        EXCEPT
        SELECT permission.permission_code
        FROM identity.role_permissions binding
        JOIN identity.permissions permission ON permission.permission_id = binding.permission_id
        WHERE binding.role_id = v_role_id
          AND binding.binding_status::text = 'ACTIVE'
          AND binding.revoked_at IS NULL
          AND permission.permission_status::text = 'ACTIVE') OR EXISTS (
        SELECT permission.permission_code
        FROM identity.role_permissions binding
        JOIN identity.permissions permission ON permission.permission_id = binding.permission_id
        WHERE binding.role_id = v_role_id
          AND binding.binding_status::text = 'ACTIVE'
          AND binding.revoked_at IS NULL
          AND permission.permission_status::text = 'ACTIVE'
        EXCEPT
        SELECT unnest(c_expected_permissions) AS permission_code) THEN
        RAISE EXCEPTION 'OPERATIONS_SUPERVISOR_PERMISSION_SET_INVALID';
    END IF;

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

    IF (SELECT count(*) FROM identity.users
        WHERE user_id IN (c_juan_user_id, c_reference_user_id)
          AND user_type::text = 'OPERATIONS_USER'
          AND user_status::text = 'ACTIVE') <> 2 THEN
        RAISE EXCEPTION 'TARGET_IDENTITIES_NOT_RECONCILED';
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
                 AND assignment.effective_from <= now()
                 AND (assignment.effective_to IS NULL OR assignment.effective_to > now())
                 AND scope_grant.scope_type::text = 'SITE'
                 AND scope_grant.site_id = c_site_id
                 AND scope_grant.site_group_id IS NULL
                 AND scope_grant.grant_status::text = 'ACTIVE'
                 AND scope_grant.revoked_at IS NULL
                 AND scope_grant.effective_from <= now()
                 AND (scope_grant.effective_to IS NULL OR scope_grant.effective_to > now())) <> 1) THEN
        RAISE EXCEPTION 'TARGET_CANONICAL_ROLE_OR_PITX_SCOPE_MISSING';
    END IF;

    IF EXISTS (
        WITH effective AS (
            SELECT user_record.user_id, permission.permission_code
            FROM identity.users user_record
            JOIN identity.user_roles assignment ON assignment.user_id = user_record.user_id
            JOIN identity.role_permissions binding ON binding.role_id = assignment.role_id
            JOIN identity.permissions permission ON permission.permission_id = binding.permission_id
            JOIN identity.user_role_scope_grants scope_grant
              ON scope_grant.user_role_id = assignment.user_role_id
            WHERE user_record.user_id IN (c_juan_user_id, c_reference_user_id)
              AND assignment.assignment_status::text = 'ACTIVE'
              AND assignment.revoked_at IS NULL
              AND assignment.effective_from <= now()
              AND (assignment.effective_to IS NULL OR assignment.effective_to > now())
              AND binding.binding_status::text = 'ACTIVE'
              AND binding.revoked_at IS NULL
              AND binding.effective_from <= now()
              AND (binding.effective_to IS NULL OR binding.effective_to > now())
              AND permission.permission_status::text = 'ACTIVE'
              AND scope_grant.scope_type::text = 'SITE'
              AND scope_grant.site_id = c_site_id
              AND scope_grant.site_group_id IS NULL
              AND scope_grant.grant_status::text = 'ACTIVE'
              AND scope_grant.revoked_at IS NULL
              AND scope_grant.effective_from <= now()
              AND (scope_grant.effective_to IS NULL OR scope_grant.effective_to > now())
        ), difference AS (
            (SELECT permission_code FROM effective WHERE user_id = c_reference_user_id
             EXCEPT
             SELECT permission_code FROM effective WHERE user_id = c_juan_user_id)
            UNION ALL
            (SELECT permission_code FROM effective WHERE user_id = c_juan_user_id
             EXCEPT
             SELECT permission_code FROM effective WHERE user_id = c_reference_user_id)
            UNION ALL
            (SELECT permission_code FROM effective WHERE user_id = c_juan_user_id
             EXCEPT
             SELECT unnest(c_expected_permissions))
            UNION ALL
            (SELECT unnest(c_expected_permissions)
             EXCEPT
             SELECT permission_code FROM effective WHERE user_id = c_juan_user_id)
        )
        SELECT 1 FROM difference) THEN
        RAISE EXCEPTION 'EFFECTIVE_PERMISSION_PARITY_FAILED';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM identity.role_permissions binding
        JOIN identity.permissions permission ON permission.permission_id = binding.permission_id
        WHERE binding.role_id = v_role_id
          AND binding.binding_status::text = 'ACTIVE'
          AND binding.revoked_at IS NULL
          AND permission.permission_code IN (
              'apt.cashier.operate',
              'cashier-shifts.operate',
              'cash-custody.operate',
              'terminal-cash.receive',
              'terminal-cash.payable-basis.read',
              'cash-custody-movements.initiate')) THEN
        RAISE EXCEPTION 'OPERATIONS_SUPERVISOR_HAS_CASHIER_OR_INITIATOR_AUTHORITY';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM identity.role_permissions binding
        JOIN identity.permissions permission ON permission.permission_id = binding.permission_id
        WHERE binding.role_id = v_role_id
          AND binding.binding_status::text = 'ACTIVE'
          AND binding.revoked_at IS NULL
          AND (permission.permission_code LIKE 'human-authentication.%'
               OR permission.permission_code LIKE 'identity.%'
               OR permission.permission_code IN (
                   'rbac.manage', 'role.manage', 'permission.manage', 'assignment.manage',
                   'user.manage', 'site.manage', 'site-group.manage', 'platform-config.manage'))) THEN
        RAISE EXCEPTION 'OPERATIONS_SUPERVISOR_HAS_ADMINISTRATIVE_PRIVILEGE';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM identity.user_roles assignment
        JOIN identity.users user_record ON user_record.user_id = assignment.user_id
        WHERE assignment.role_id = v_role_id
          AND assignment.assignment_status::text = 'ACTIVE'
          AND assignment.revoked_at IS NULL
          AND assignment.effective_from <= now()
          AND (assignment.effective_to IS NULL OR assignment.effective_to > now())
          AND user_record.user_type::text <> 'OPERATIONS_USER') THEN
        RAISE EXCEPTION 'INCOMPATIBLE_ACTIVE_OPERATIONS_SUPERVISOR_USER_TYPE';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM identity.user_role_scope_grants scope_grant
        JOIN identity.user_roles assignment ON assignment.user_role_id = scope_grant.user_role_id
        WHERE assignment.role_id = v_role_id
          AND assignment.assignment_status::text = 'ACTIVE'
          AND assignment.revoked_at IS NULL
          AND assignment.effective_from <= now()
          AND (assignment.effective_to IS NULL OR assignment.effective_to > now())
          AND scope_grant.grant_status::text = 'ACTIVE'
          AND scope_grant.revoked_at IS NULL
          AND scope_grant.effective_from <= now()
          AND (scope_grant.effective_to IS NULL OR scope_grant.effective_to > now())
          AND (scope_grant.scope_type::text <> 'SITE'
               OR scope_grant.site_id IS NULL
               OR scope_grant.site_group_id IS NOT NULL)) THEN
        RAISE EXCEPTION 'OPERATIONS_SUPERVISOR_SCOPE_NOT_SITE_ONLY';
    END IF;
END
$validation$;

SELECT user_record.username,
       user_record.user_type::text,
       user_record.credential_version,
       user_record.authorization_epoch,
       site.site_code,
       assignment.assignment_status::text,
       scope_grant.grant_status::text
FROM identity.users user_record
JOIN identity.user_roles assignment ON assignment.user_id = user_record.user_id
JOIN identity.roles role ON role.role_id = assignment.role_id
JOIN identity.user_role_scope_grants scope_grant
  ON scope_grant.user_role_id = assignment.user_role_id
LEFT JOIN sites.sites site ON site.site_id = scope_grant.site_id
WHERE role.role_code = 'OPERATIONS_SUPERVISOR'
  AND assignment.assignment_status::text = 'ACTIVE'
  AND scope_grant.grant_status::text = 'ACTIVE'
ORDER BY user_record.username_normalized, site.site_code;

SELECT permission.permission_code
FROM identity.roles role
JOIN identity.role_permissions binding ON binding.role_id = role.role_id
JOIN identity.permissions permission ON permission.permission_id = binding.permission_id
WHERE role.role_code = 'OPERATIONS_SUPERVISOR'
  AND binding.binding_status::text = 'ACTIVE'
  AND binding.revoked_at IS NULL
ORDER BY permission.permission_code;
