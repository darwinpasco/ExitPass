DO $$
BEGIN
    IF to_regclass('identity.devices') IS NULL OR
       to_regclass('identity.device_provisioning_credentials') IS NULL OR
       to_regclass('identity.device_credential_history') IS NULL OR
       to_regclass('identity.device_lifecycle_history') IS NULL THEN
        RAISE EXCEPTION 'Device Administration v1.3 schema is incomplete.';
    END IF;

    IF EXISTS (
        SELECT 1 FROM identity.devices device
        LEFT JOIN identity.service_identities service
          ON service.service_identity_id = device.service_identity_id
        WHERE service.service_identity_id IS NULL OR service.identity_type <> 'DEVICE'
    ) THEN
        RAISE EXCEPTION 'A governed device is not backed by a canonical DEVICE service identity.';
    END IF;

    IF EXISTS (
        SELECT 1 FROM identity.devices device
        WHERE device.lifecycle_status = 'ACTIVE'
          AND NOT EXISTS (
              SELECT 1 FROM sites.device_assignments assignment
              WHERE assignment.service_identity_id = device.service_identity_id
                AND assignment.assignment_type = CASE
                    WHEN device.device_type = 'ASSISTED_PAYMENT_TERMINAL'
                        THEN 'PAYMENT_DEVICE'::sites.device_assignment_type_enum
                    ELSE 'SERVICE_PRINCIPAL'::sites.device_assignment_type_enum
                END
                AND assignment.assignment_status = 'ACTIVE'
                AND assignment.unassigned_at IS NULL
          )
    ) THEN
        RAISE EXCEPTION 'An ACTIVE governed device has no ACTIVE canonical Site assignment.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM identity.roles role
        JOIN identity.role_permissions assignment ON assignment.role_id=role.role_id
        JOIN identity.permissions permission ON permission.permission_id=assignment.permission_id
        WHERE role.role_code='SYSTEM_ADMINISTRATOR'
          AND permission.permission_code IN ('device.view','device.manage')
          AND role.role_status='ACTIVE'
          AND role.effective_from<=now() AND (role.effective_to IS NULL OR role.effective_to>now())
          AND assignment.binding_status='ACTIVE' AND assignment.revoked_at IS NULL
          AND assignment.effective_from<=now() AND (assignment.effective_to IS NULL OR assignment.effective_to>now())
        GROUP BY role.role_id
        HAVING count(DISTINCT permission.permission_code)=2
    ) OR EXISTS (
        SELECT 1
        FROM identity.roles role
        JOIN identity.role_permissions assignment ON assignment.role_id=role.role_id
        JOIN identity.permissions permission ON permission.permission_id=assignment.permission_id
        WHERE role.role_code<>'SYSTEM_ADMINISTRATOR'
          AND role.role_code NOT LIKE 'UAT\_%' ESCAPE '\'
          AND permission.permission_code IN ('device.view','device.manage')
          AND role.role_status='ACTIVE'
          AND role.effective_from<=now() AND (role.effective_to IS NULL OR role.effective_to>now())
          AND assignment.binding_status='ACTIVE' AND assignment.revoked_at IS NULL
          AND assignment.effective_from<=now() AND (assignment.effective_to IS NULL OR assignment.effective_to>now())
    ) THEN
        RAISE EXCEPTION 'Device Administration permissions are not exclusive to SYSTEM_ADMINISTRATOR.';
    END IF;
END $$;
