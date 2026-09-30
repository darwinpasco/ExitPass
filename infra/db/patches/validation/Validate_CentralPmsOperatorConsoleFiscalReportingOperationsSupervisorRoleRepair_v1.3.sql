DO $validation$
DECLARE
    v_expected text[] := ARRAY[
        'fiscal-issuance.status.read',
        'fiscal-reporting.ej.read',
        'fiscal-reporting.ej.export',
        'fiscal-reporting.x.read',
        'fiscal-reporting.x.generate',
        'fiscal-reporting.z.read'
    ];
BEGIN
    IF (SELECT count(*) FROM identity.roles
        WHERE role_code = 'OPERATIONS_SUPERVISOR'
          AND role_status = 'ACTIVE') <> 1 THEN
        RAISE EXCEPTION 'OPERATIONS_SUPERVISOR must remain active.';
    END IF;

    IF EXISTS (
        SELECT expected.permission_code
        FROM unnest(v_expected) AS expected(permission_code)
        LEFT JOIN (
            SELECT p.permission_code, count(*) AS active_count
            FROM identity.role_permissions rp
            JOIN identity.roles r ON r.role_id = rp.role_id
            JOIN identity.permissions p ON p.permission_id = rp.permission_id
            WHERE r.role_code = 'OPERATIONS_SUPERVISOR'
              AND rp.binding_status = 'ACTIVE'
              AND rp.effective_from <= now()
              AND (rp.effective_to IS NULL OR rp.effective_to > now())
            GROUP BY p.permission_code
        ) actual ON actual.permission_code = expected.permission_code
        WHERE COALESCE(actual.active_count, 0) <> 1
    ) THEN
        RAISE EXCEPTION 'OPERATIONS_SUPERVISOR fiscal authority bundle is incomplete or duplicated.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM identity.role_permissions rp
        JOIN identity.roles r ON r.role_id = rp.role_id
        JOIN identity.permissions p ON p.permission_id = rp.permission_id
        WHERE r.role_code = 'SITE_OPERATOR'
          AND p.permission_code = ANY(v_expected)
          AND rp.binding_status = 'ACTIVE'
          AND rp.effective_from <= now()
          AND (rp.effective_to IS NULL OR rp.effective_to > now())
    ) THEN
        RAISE EXCEPTION 'SITE_OPERATOR still has active fiscal reporting or fiscal-status authority.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM identity.role_permissions rp
        JOIN identity.roles r ON r.role_id = rp.role_id
        JOIN identity.permissions p ON p.permission_id = rp.permission_id
        WHERE r.role_code IN ('OPERATIONS_SUPERVISOR', 'SITE_OPERATOR')
          AND p.permission_code = 'fiscal-reporting.z.generate'
          AND rp.binding_status = 'ACTIVE'
          AND rp.effective_from <= now()
          AND (rp.effective_to IS NULL OR rp.effective_to > now())
    ) THEN
        RAISE EXCEPTION 'Z generation must not be granted by this correction.';
    END IF;

    RAISE NOTICE 'CENTRAL_PMS_OPERATOR_CONSOLE_FISCAL_OPERATIONS_SUPERVISOR_ROLE_REPAIR_VALID';
END
$validation$;
