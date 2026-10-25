DO $$
DECLARE
    v_count integer;
BEGIN
    SELECT count(*) INTO v_count
    FROM identity.permissions
    WHERE permission_status = 'ACTIVE'
      AND permission_code = ANY (ARRAY[
          'cash-custody-movements.initiate',
          'cash-custody-movements.acknowledge',
          'cash-custody-movements.correct',
          'cash-custody-movements.read']);
    IF v_count <> 4 THEN
        RAISE EXCEPTION 'The APT cash-custody movement permission catalogue is incomplete.';
    END IF;

    SELECT count(*) INTO v_count
    FROM identity.roles
    WHERE role_status = 'ACTIVE'
      AND human_assignable
      AND role_code = ANY (ARRAY[
          'APT_CASH_CUSTODY_MOVEMENT_OPERATOR',
          'APT_CASH_CUSTODY_SUPERVISOR',
          'APT_SITE_CUSTODIAN',
          'APT_CASH_COLLECTOR']);
    IF v_count <> 4 THEN
        RAISE EXCEPTION 'The APT movement role catalogue is incomplete.';
    END IF;

    SELECT count(*) INTO v_count
    FROM identity.role_permissions binding
    JOIN identity.roles role ON role.role_id = binding.role_id
    JOIN identity.permissions permission ON permission.permission_id = binding.permission_id
    WHERE binding.binding_status = 'ACTIVE'
      AND role.role_code = 'APT_CASHIER_OPERATOR'
      AND permission.permission_code LIKE 'cash-custody-movements.%';
    IF v_count <> 0 THEN
        RAISE EXCEPTION 'APT_CASHIER_OPERATOR must not inherit cash-custody movement authority.';
    END IF;

    SELECT count(*) INTO v_count
    FROM identity.role_permissions binding
    JOIN identity.roles role ON role.role_id = binding.role_id
    JOIN identity.permissions permission ON permission.permission_id = binding.permission_id
    WHERE binding.binding_status = 'ACTIVE'
      AND binding.binding_reason_code = 'APT_CASH_CUSTODY_MOVEMENT_AUTHORITY';
    IF v_count <> 13 THEN
        RAISE EXCEPTION 'The APT movement role-permission bindings are incomplete or duplicated.';
    END IF;

    RAISE NOTICE 'CENTRAL_PMS_APT_CASH_CUSTODY_MOVEMENT_AUTHORIZATION_VALID';
END $$;
