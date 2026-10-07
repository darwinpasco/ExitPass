SELECT set_config('exitpass.bootstrap.apply', :'apply', false) AS bootstrap_apply \gset
SELECT set_config('exitpass.bootstrap.username', :'username', false) AS bootstrap_username \gset
SELECT set_config('exitpass.bootstrap.device_binding_code', :'device_binding_code', false) AS bootstrap_device \gset
SELECT set_config('exitpass.bootstrap.proof_hash', :'proof_hash', false) AS bootstrap_proof \gset

DO $bootstrap$
DECLARE
    v_apply boolean := current_setting('exitpass.bootstrap.apply')::boolean;
    v_username text := current_setting('exitpass.bootstrap.username');
    v_device_code text := current_setting('exitpass.bootstrap.device_binding_code');
    v_proof_hash text := current_setting('exitpass.bootstrap.proof_hash');
    v_user_id uuid;
    v_device_id uuid;
    v_site_id uuid;
    v_site_group_id uuid;
    v_mapping_id uuid;
    v_active_shift_count integer;
    v_correlation_id uuid := gen_random_uuid();
    v_service_identity_id uuid := '8063c159-dae6-57af-9f1f-e0a07d519fb2';
BEGIN
    SELECT u.user_id
      INTO STRICT v_user_id
      FROM identity.users u
     WHERE u.username = v_username
       AND u.user_status = 'ACTIVE'
       AND u.effective_from <= now()
       AND (u.effective_to IS NULL OR u.effective_to > now());

    SELECT d.operator_device_binding_id, d.site_id, d.site_group_id
      INTO STRICT v_device_id, v_site_id, v_site_group_id
      FROM operator_console.operator_device_bindings d
     WHERE d.device_binding_code = v_device_code
       AND d.device_status = 'ACTIVE'
       AND d.trust_level IN ('BROWSER_KEY_ONLY', 'BROWSER_KEY_AND_MTLS')
       AND d.revoked_at IS NULL
       AND d.lost_reported_at IS NULL;

    IF NOT EXISTS (
        SELECT 1
          FROM operator_console.operator_device_assignment_history a
         WHERE a.operator_device_binding_id = v_device_id
           AND a.site_id = v_site_id
           AND a.site_group_id = v_site_group_id
           AND a.assignment_status_code = 'ACTIVE'
           AND a.ended_at IS NULL
           AND a.effective_from <= now()
           AND (a.effective_to IS NULL OR a.effective_to > now())
    ) THEN
        RAISE EXCEPTION 'Controlled Operator Console device has no active canonical Site assignment.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
          FROM identity.user_roles ur
          JOIN identity.roles r ON r.role_id = ur.role_id
          JOIN identity.user_role_scope_grants g ON g.user_role_id = ur.user_role_id
         WHERE ur.user_id = v_user_id
           AND ur.assignment_status = 'ACTIVE'
           AND ur.revoked_at IS NULL
           AND ur.effective_from <= now()
           AND (ur.effective_to IS NULL OR ur.effective_to > now())
           AND r.role_status = 'ACTIVE'
           AND g.grant_status = 'ACTIVE'
           AND g.revoked_at IS NULL
           AND g.effective_from <= now()
           AND (g.effective_to IS NULL OR g.effective_to > now())
           AND (g.scope_type = 'GLOBAL'
                OR (g.scope_type = 'SITE' AND g.site_id = v_site_id)
                OR (g.scope_type = 'SITE_GROUP' AND g.site_group_id = v_site_group_id))
    ) THEN
        RAISE EXCEPTION 'Controlled Operator Console user is outside the device Site scope.';
    END IF;

    SELECT m.hr_identity_mapping_id
      INTO STRICT v_mapping_id
      FROM operator_console.hr_identity_mappings m
     WHERE m.user_id = v_user_id
       AND m.mapping_status = 'ACTIVE'
       AND m.effective_from <= now()
       AND (m.effective_to IS NULL OR m.effective_to > now())
       AND m.revoked_at IS NULL;

    SELECT count(*)::integer
      INTO v_active_shift_count
      FROM operator_console.operator_shifts s
     WHERE s.operator_user_id = v_user_id
       AND s.operational_status = 'ACTIVE'
       AND s.revoked_at IS NULL
       AND s.active_from <= now()
       AND (s.active_to IS NULL OR s.active_to > now());

    IF v_active_shift_count > 1 THEN
        RAISE EXCEPTION 'Controlled Operator Console user has conflicting active shifts.';
    END IF;
    IF v_active_shift_count = 1 AND NOT EXISTS (
        SELECT 1
          FROM operator_console.operator_shifts s
         WHERE s.operator_user_id = v_user_id
           AND s.site_id = v_site_id
           AND s.site_group_id = v_site_group_id
           AND s.operational_status = 'ACTIVE'
           AND s.revoked_at IS NULL
           AND s.active_from <= now()
           AND (s.active_to IS NULL OR s.active_to > now())
    ) THEN
        RAISE EXCEPTION 'Controlled Operator Console user has an active shift outside the device route.';
    END IF;

    IF NOT v_apply THEN
        RETURN;
    END IF;
    IF v_proof_hash !~ '^[0-9a-f]{64}$' THEN
        RAISE EXCEPTION 'Provisioned proof hash is invalid.';
    END IF;

    UPDATE operator_console.operator_device_bindings
       SET browser_key_thumbprint = v_proof_hash,
           binding_source = 'CONTROLLED_IST',
           correlation_id = v_correlation_id,
           updated_at = now(),
           updated_by_service_identity_id = v_service_identity_id,
           row_version = row_version + 1
     WHERE operator_device_binding_id = v_device_id
       AND browser_key_thumbprint IS DISTINCT FROM v_proof_hash;

    IF v_active_shift_count = 0 THEN
        INSERT INTO operator_console.operator_shifts (
            operator_shift_id, shift_reference, shift_origin, hr_provider_code,
            external_shift_id_hash, external_shift_id_masked, hr_identity_mapping_id,
            operator_user_id, site_group_id, site_id, scheduled_start_at,
            scheduled_end_at, source_imported_at, import_status_code,
            source_system_code, source_status_code, operational_status,
            active_from, active_to, operator_device_binding_id, opened_at,
            cash_custody_status, correlation_id, created_by_service_identity_id,
            updated_by_service_identity_id)
        VALUES (
            gen_random_uuid(),
            'SHIFT-' || upper(substr(replace(gen_random_uuid()::text, '-', ''), 1, 24)),
            'HR_IMPORT', 'PITX_IST',
            encode(digest(v_username || ':' || v_device_code || ':' || clock_timestamp()::text, 'sha256'), 'hex'),
            'CONTROLLED-IST-' || v_username, v_mapping_id,
            v_user_id, v_site_group_id, v_site_id, now() - interval '1 minute',
            now() + interval '12 hours', now(), 'IMPORTED',
            'CONTROLLED_IST', 'ACTIVE', 'ACTIVE', now() - interval '1 minute',
            now() + interval '12 hours', v_device_id, now(), 'NONE',
            v_correlation_id, v_service_identity_id, v_service_identity_id);
    END IF;
END
$bootstrap$;

SELECT
    u.username,
    d.device_binding_code,
    d.device_status,
    count(DISTINCT s.operator_shift_id) FILTER (
        WHERE s.operational_status = 'ACTIVE'
          AND s.revoked_at IS NULL
          AND s.active_from <= now()
          AND (s.active_to IS NULL OR s.active_to > now())) AS active_shift_count,
    bool_and(s.site_id = d.site_id AND s.site_group_id = d.site_group_id)
        FILTER (WHERE s.operational_status = 'ACTIVE') AS active_shift_matches_device_route
FROM identity.users u
JOIN operator_console.operator_device_bindings d
  ON d.device_binding_code = :'device_binding_code'
LEFT JOIN operator_console.operator_shifts s ON s.operator_user_id = u.user_id
WHERE u.username = :'username'
GROUP BY u.username, d.device_binding_code, d.device_status;
