DO $$
DECLARE
    actual_codes text[];
    expected_codes constant text[] := ARRAY[
        'APT_CASHIER_OPERATOR',
        'COMPLIANCE_POLICY_ADMINISTRATOR',
        'EXECUTIVE_MANAGEMENT',
        'FINANCE_RECONCILIATION_ANALYST',
        'OPERATIONS_SUPERVISOR',
        'PARKING_ATTENDANT',
        'SITE_OPERATOR',
        'SYSTEM_ADMINISTRATOR'
    ];
BEGIN
    SELECT array_agg(role_code ORDER BY role_code)
    INTO actual_codes
    FROM identity.roles
    WHERE human_assignable
      AND role_status = 'ACTIVE'
      AND effective_from <= now()
      AND (effective_to IS NULL OR effective_to > now());

    IF actual_codes IS DISTINCT FROM expected_codes THEN
        RAISE EXCEPTION 'Approved active human-role catalog mismatch. Expected %, found %.',
            expected_codes, actual_codes;
    END IF;

    IF EXISTS (
        SELECT 1
        FROM identity.roles
        WHERE role_code = ANY(expected_codes)
          AND (role_provenance <> 'CANONICAL_ROLE' OR NOT human_assignable OR role_status <> 'ACTIVE')
    ) THEN
        RAISE EXCEPTION 'An approved role is not active, canonical, and human-assignable.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM identity.role_permissions rp
        JOIN identity.roles r ON r.role_id = rp.role_id
        JOIN identity.permissions p ON p.permission_id = rp.permission_id
        WHERE r.role_code = 'SYSTEM_ADMINISTRATOR'
          AND rp.binding_status = 'ACTIVE'
          AND (
              p.permission_code LIKE 'statutory-discounts.%'
              OR p.permission_code LIKE 'reconciliation.%'
              OR p.permission_code LIKE 'policy-import.%'
              OR p.permission_code LIKE 'apt.%'
              OR p.permission_code LIKE 'parking-attendant.%'
          )
    ) THEN
        RAISE EXCEPTION 'System Administrator has business-workflow authority.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM identity.role_permissions rp
        JOIN identity.roles r ON r.role_id = rp.role_id
        JOIN identity.permissions p ON p.permission_id = rp.permission_id
        WHERE r.role_code = 'OPERATIONS_SUPERVISOR'
          AND p.permission_code = 'statutory-discounts.decision.approve'
          AND rp.binding_status = 'ACTIVE'
    ) OR EXISTS (
        SELECT 1
        FROM identity.role_permissions rp
        JOIN identity.roles r ON r.role_id = rp.role_id
        JOIN identity.permissions p ON p.permission_id = rp.permission_id
        WHERE r.role_code = 'OPERATIONS_SUPERVISOR'
          AND p.permission_code = 'statutory-discounts.payable-basis.apply'
          AND rp.binding_status = 'ACTIVE'
    ) THEN
        RAISE EXCEPTION 'Operations Supervisor statutory bundle mismatch.';
    END IF;
END
$$;
