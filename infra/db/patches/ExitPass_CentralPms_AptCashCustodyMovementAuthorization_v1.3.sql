-- ExitPass v1.3 APT cash-custody movement authorization.
-- Movement permissions remain separate from APT_CASHIER_OPERATOR.
BEGIN;

CREATE TEMP TABLE apt_movement_roles (
    role_code varchar(64) PRIMARY KEY,
    role_name varchar(128) NOT NULL,
    role_description text NOT NULL
) ON COMMIT DROP;

INSERT INTO apt_movement_roles VALUES
('APT_CASH_CUSTODY_MOVEMENT_OPERATOR', 'APT Cash Custody Movement Operator', 'Site-scoped authority to initiate and read APT cash-custody movements.'),
('APT_CASH_CUSTODY_SUPERVISOR', 'APT Cash Custody Supervisor', 'Site-scoped authority to acknowledge, review, and annotate APT cash-custody movements.'),
('APT_SITE_CUSTODIAN', 'APT Site Custodian', 'Site-scoped authority to acknowledge and review APT cash-custody handovers.'),
('APT_CASH_COLLECTOR', 'APT Cash Collector', 'Site-scoped authority to acknowledge and review APT cash-custody handovers.');

INSERT INTO identity.roles (
    role_id, role_code, role_name, role_description, role_type, role_status,
    is_privileged, requires_elevated_approval, role_provenance,
    direct_add_user_eligible, human_assignable, effective_from,
    created_by_service_identity_id, updated_by_service_identity_id)
SELECT gen_random_uuid(), role_code, role_name, role_description, 'OPERATIONS', 'ACTIVE',
       false, false, 'CANONICAL_ROLE', true, true, now(),
       '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM apt_movement_roles
ON CONFLICT ON CONSTRAINT uq_roles__role_code DO UPDATE SET
    role_name = EXCLUDED.role_name,
    role_description = EXCLUDED.role_description,
    role_type = EXCLUDED.role_type,
    role_status = 'ACTIVE',
    role_provenance = 'CANONICAL_ROLE',
    direct_add_user_eligible = true,
    human_assignable = true,
    effective_to = NULL,
    updated_at = now(),
    row_version = identity.roles.row_version + 1;

INSERT INTO identity.role_user_type_compatibility (role_id, user_type)
SELECT role.role_id, 'OPERATIONS_USER'
FROM identity.roles role
JOIN apt_movement_roles movement_role ON movement_role.role_code = role.role_code
ON CONFLICT DO NOTHING;

CREATE TEMP TABLE apt_movement_permissions (
    permission_code varchar(96) PRIMARY KEY,
    permission_name varchar(128) NOT NULL,
    permission_description text NOT NULL,
    permission_action varchar(64) NOT NULL
) ON COMMIT DROP;

INSERT INTO apt_movement_permissions VALUES
('cash-custody-movements.initiate', 'Initiate cash custody movements', 'Initiate a governed movement from an owned open APT cash-custody session.', 'initiate'),
('cash-custody-movements.acknowledge', 'Acknowledge cash custody movements', 'Acknowledge a physical cash handover as a distinct authorized APT principal.', 'acknowledge'),
('cash-custody-movements.correct', 'Annotate cash custody movements', 'Append nonfinancial correction and dispute evidence to a custody movement.', 'correct'),
('cash-custody-movements.read', 'Read cash custody movements', 'Read cash-custody movement evidence within assigned APT scope.', 'read');

INSERT INTO identity.permissions (
    permission_id, permission_code, permission_name, permission_description,
    permission_domain, permission_action, permission_status, is_sensitive, requires_audit,
    created_by_service_identity_id, updated_by_service_identity_id)
SELECT gen_random_uuid(), permission_code, permission_name, permission_description,
       'cash-custody-movements', permission_action, 'ACTIVE', true, true,
       '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM apt_movement_permissions
ON CONFLICT ON CONSTRAINT uq_permissions__permission_code DO UPDATE SET
    permission_name = EXCLUDED.permission_name,
    permission_description = EXCLUDED.permission_description,
    permission_domain = EXCLUDED.permission_domain,
    permission_action = EXCLUDED.permission_action,
    permission_status = 'ACTIVE',
    is_sensitive = true,
    requires_audit = true,
    updated_at = now(),
    row_version = identity.permissions.row_version + 1;

CREATE TEMP TABLE apt_movement_role_permissions (
    role_code varchar(64) NOT NULL,
    permission_code varchar(96) NOT NULL,
    PRIMARY KEY (role_code, permission_code)
) ON COMMIT DROP;

INSERT INTO apt_movement_role_permissions VALUES
('APT_CASH_CUSTODY_MOVEMENT_OPERATOR', 'apt.access'),
('APT_CASH_CUSTODY_MOVEMENT_OPERATOR', 'cash-custody-movements.initiate'),
('APT_CASH_CUSTODY_MOVEMENT_OPERATOR', 'cash-custody-movements.read'),
('APT_CASH_CUSTODY_SUPERVISOR', 'apt.access'),
('APT_CASH_CUSTODY_SUPERVISOR', 'cash-custody-movements.acknowledge'),
('APT_CASH_CUSTODY_SUPERVISOR', 'cash-custody-movements.correct'),
('APT_CASH_CUSTODY_SUPERVISOR', 'cash-custody-movements.read'),
('APT_SITE_CUSTODIAN', 'apt.access'),
('APT_SITE_CUSTODIAN', 'cash-custody-movements.acknowledge'),
('APT_SITE_CUSTODIAN', 'cash-custody-movements.read'),
('APT_CASH_COLLECTOR', 'apt.access'),
('APT_CASH_COLLECTOR', 'cash-custody-movements.acknowledge'),
('APT_CASH_COLLECTOR', 'cash-custody-movements.read');

INSERT INTO identity.role_permissions (
    role_permission_id, role_id, permission_id, binding_status,
    binding_reason_code, assigned_by_service_identity_id, effective_from,
    created_by_service_identity_id, updated_by_service_identity_id)
SELECT gen_random_uuid(), role.role_id, permission.permission_id, 'ACTIVE',
       'APT_CASH_CUSTODY_MOVEMENT_AUTHORITY',
       '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(),
       '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM apt_movement_role_permissions requested
JOIN identity.roles role ON role.role_code = requested.role_code
JOIN identity.permissions permission ON permission.permission_code = requested.permission_code
WHERE NOT EXISTS (
    SELECT 1 FROM identity.role_permissions existing
    WHERE existing.role_id = role.role_id
      AND existing.permission_id = permission.permission_id
      AND existing.binding_status = 'ACTIVE');

COMMIT;
