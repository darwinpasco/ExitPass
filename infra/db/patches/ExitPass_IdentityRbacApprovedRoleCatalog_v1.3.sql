-- ExitPass v1.3 approved assignable role catalog.
-- Requires the I-021 identity governance columns (role_provenance,
-- human_assignable, direct_add_user_eligible) and compatibility table.
BEGIN;

CREATE TEMP TABLE approved_roles (
    role_code varchar(64) PRIMARY KEY, role_name varchar(128) NOT NULL,
    role_description text NOT NULL, role_type identity.role_type_enum NOT NULL,
    is_privileged boolean NOT NULL, elevated boolean NOT NULL,
    direct_add boolean NOT NULL, user_type identity.user_type_enum NOT NULL
) ON COMMIT DROP;

INSERT INTO approved_roles VALUES
('SYSTEM_ADMINISTRATOR','System Administrator','Administrative authority only: identity/RBAC, site, device, shift, POS Server/fiscal configuration, connector, platform configuration, and access audit.','SYSTEM',true,true,false,'INTERNAL_ADMIN'),
('OPERATIONS_SUPERVISOR','Operations Supervisor','Assigned-site operations, operational exception, and shift supervision; plus all-site statutory discount supervision on Management Platform only.','OPERATIONS',true,true,false,'OPERATIONS_USER'),
('SITE_OPERATOR','Site Operator','Assigned-site operation, support, and statutory discount processing without approval.','OPERATIONS',false,false,true,'SITE_OPERATOR'),
('PARKING_ATTENDANT','Parking Attendant','Native Parking App authority at assigned sites and devices.','OPERATIONS',false,false,true,'OPERATIONS_USER'),
('APT_CASHIER_OPERATOR','APT / Cashier Operator','APT authority at assigned sites and terminals.','OPERATIONS',false,false,true,'OPERATIONS_USER'),
('FINANCE_RECONCILIATION_ANALYST','Finance / Reconciliation Analyst','Finance and reconciliation responsibilities within assigned scope.','FINANCE',false,false,true,'FINANCE_USER'),
('COMPLIANCE_POLICY_ADMINISTRATOR','Compliance / Policy Administrator','Global-by-default compliance and policy responsibilities.','COMPLIANCE',true,true,false,'COMPLIANCE_USER'),
('EXECUTIVE_MANAGEMENT','Executive / Management','Read-only management reporting with mandatory Global scope.','OTHER',false,true,false,'OTHER');

INSERT INTO identity.roles (
    role_id, role_code, role_name, role_description, role_type, role_status,
    is_privileged, requires_elevated_approval, role_provenance,
    direct_add_user_eligible, human_assignable, effective_from,
    created_by_service_identity_id, updated_by_service_identity_id)
SELECT gen_random_uuid(), role_code, role_name, role_description, role_type, 'ACTIVE',
       is_privileged, elevated, 'CANONICAL_ROLE', direct_add, true, now(),
       '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM approved_roles
ON CONFLICT ON CONSTRAINT uq_roles__role_code DO UPDATE
SET role_name=EXCLUDED.role_name, role_description=EXCLUDED.role_description,
    role_type=EXCLUDED.role_type, role_status='ACTIVE',
    is_privileged=EXCLUDED.is_privileged,
    requires_elevated_approval=EXCLUDED.requires_elevated_approval,
    role_provenance='CANONICAL_ROLE', direct_add_user_eligible=EXCLUDED.direct_add_user_eligible,
    human_assignable=true, effective_to=NULL, updated_at=now(),
    row_version=identity.roles.row_version+1;

UPDATE identity.roles
SET human_assignable=false, direct_add_user_eligible=false,
    role_status='RETIRED', effective_to=COALESCE(effective_to, now()), updated_at=now(),
    row_version=row_version+1
WHERE human_assignable
  AND role_code NOT IN (SELECT role_code FROM approved_roles);

DELETE FROM identity.role_user_type_compatibility c
USING identity.roles r
WHERE c.role_id=r.role_id AND r.role_code IN (SELECT role_code FROM approved_roles);

INSERT INTO identity.role_user_type_compatibility (role_id, user_type)
SELECT r.role_id, a.user_type
FROM approved_roles a JOIN identity.roles r ON r.role_code=a.role_code;

-- Dedicated app permissions are intentionally narrow; no role hierarchy is implied.
INSERT INTO identity.permissions (
    permission_id, permission_code, permission_name, permission_description,
    permission_domain, permission_action, permission_status, is_sensitive, requires_audit,
    created_by_service_identity_id, updated_by_service_identity_id)
VALUES
(gen_random_uuid(),'parking-attendant.operate','Operate Native Parking App','Perform assigned-site/device parking-attendant work.','parking-app','operate','ACTIVE',true,true,'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
(gen_random_uuid(),'apt.cashier.operate','Operate APT cashier','Perform assigned-site/terminal APT cashier work.','apt','operate','ACTIVE',true,true,'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978')
ON CONFLICT ON CONSTRAINT uq_permissions__permission_code DO UPDATE
SET permission_name=EXCLUDED.permission_name,
    permission_description=EXCLUDED.permission_description,
    permission_domain=EXCLUDED.permission_domain,
    permission_action=EXCLUDED.permission_action,
    permission_status='ACTIVE', is_sensitive=EXCLUDED.is_sensitive,
    requires_audit=EXCLUDED.requires_audit, updated_at=now(),
    row_version=identity.permissions.row_version+1;

CREATE TEMP TABLE approved_role_permissions (
    role_code varchar(64) NOT NULL, permission_code varchar(96) NOT NULL,
    PRIMARY KEY(role_code, permission_code)
) ON COMMIT DROP;

-- Statutory supervisor bindings remain part of the Operations Supervisor bundle.
-- ApprovedIdentityRoleCatalog marks these permissions as Management Platform-only;
-- Operator Console authorization denies them even when they appear in a role union.
INSERT INTO approved_role_permissions VALUES
('SYSTEM_ADMINISTRATOR','management-platform.identity-rbac.inventory.read'),
('SYSTEM_ADMINISTRATOR','user.view'),('SYSTEM_ADMINISTRATOR','user.manage'),
('SYSTEM_ADMINISTRATOR','rbac.view'),('SYSTEM_ADMINISTRATOR','rbac.manage'),
('SYSTEM_ADMINISTRATOR','role.view'),('SYSTEM_ADMINISTRATOR','role.manage'),
('SYSTEM_ADMINISTRATOR','permission.view'),('SYSTEM_ADMINISTRATOR','permission.manage'),
('SYSTEM_ADMINISTRATOR','assignment.view'),('SYSTEM_ADMINISTRATOR','assignment.manage'),
('SYSTEM_ADMINISTRATOR','access-audit.view'),
('SYSTEM_ADMINISTRATOR','identity.role-assignment.manage'),
('SYSTEM_ADMINISTRATOR','identity.scope-assignment.manage'),
('SYSTEM_ADMINISTRATOR','identity.privileged-access.decide'),
('SYSTEM_ADMINISTRATOR','identity.access-review.manage'),
('SYSTEM_ADMINISTRATOR','human-authentication.session.admin.view'),
('SYSTEM_ADMINISTRATOR','human-authentication.session.admin.revoke'),
('SYSTEM_ADMINISTRATOR','site.view'),('SYSTEM_ADMINISTRATOR','site.manage'),
('SYSTEM_ADMINISTRATOR','site-group.view'),('SYSTEM_ADMINISTRATOR','site-group.manage'),
('SYSTEM_ADMINISTRATOR','device.view'),('SYSTEM_ADMINISTRATOR','device.manage'),
('SYSTEM_ADMINISTRATOR','device-binding.view'),('SYSTEM_ADMINISTRATOR','device-binding.manage'),
('SYSTEM_ADMINISTRATOR','shift.view'),('SYSTEM_ADMINISTRATOR','shift.manage'),
('SYSTEM_ADMINISTRATOR','pos-server-config.view'),('SYSTEM_ADMINISTRATOR','pos-server-config.manage'),
('SYSTEM_ADMINISTRATOR','connector-config.view'),('SYSTEM_ADMINISTRATOR','connector-config.manage'),
('SYSTEM_ADMINISTRATOR','platform-config.view'),('SYSTEM_ADMINISTRATOR','platform-config.manage'),
('OPERATIONS_SUPERVISOR','statutory-discounts.session.lookup'),
('OPERATIONS_SUPERVISOR','statutory-discounts.draft.view'),
('OPERATIONS_SUPERVISOR','statutory-discounts.draft.create'),
('OPERATIONS_SUPERVISOR','statutory-discounts.evidence.view'),
('OPERATIONS_SUPERVISOR','statutory-discounts.evidence.capture'),
('OPERATIONS_SUPERVISOR','statutory-discounts.evidence.review.view'),
('OPERATIONS_SUPERVISOR','statutory-discounts.review.queue.read'),
('OPERATIONS_SUPERVISOR','statutory-discounts.review.detail.read'),
('OPERATIONS_SUPERVISOR','statutory-discounts.decision.review'),
('OPERATIONS_SUPERVISOR','statutory-discounts.decision.approve'),
('OPERATIONS_SUPERVISOR','statutory-discounts.decision.reject'),
('OPERATIONS_SUPERVISOR','statutory-discounts.policy.resolve'),
('OPERATIONS_SUPERVISOR','fiscal-issuance.status.read'),
('OPERATIONS_SUPERVISOR','fiscal-issuance.void.command'),
('OPERATIONS_SUPERVISOR','operator-workflow-audit.view'),
('OPERATIONS_SUPERVISOR','projection-health.view'),
('OPERATIONS_SUPERVISOR','vendor-acknowledgments.view'),
('OPERATIONS_SUPERVISOR','ticket.lookup'),
('OPERATIONS_SUPERVISOR','shift.view'),('OPERATIONS_SUPERVISOR','shift.manage'),
('SITE_OPERATOR','statutory-discounts.session.lookup'),
('SITE_OPERATOR','statutory-discounts.draft.view'),
('SITE_OPERATOR','statutory-discounts.draft.create'),
('SITE_OPERATOR','statutory-discounts.evidence.view'),
('SITE_OPERATOR','statutory-discounts.evidence.capture'),
('SITE_OPERATOR','statutory-discounts.policy.resolve'),
('SITE_OPERATOR','fiscal-issuance.status.read'),('SITE_OPERATOR','ticket.lookup'),
('PARKING_ATTENDANT','parking-attendant.operate'),
('APT_CASHIER_OPERATOR','apt.cashier.operate');

-- Replace the three redesigned bundles exactly. This is what prevents the
-- administrative role from inheriting business-workflow authority.
UPDATE identity.role_permissions rp
SET binding_status='REVOKED', effective_to=now(), revoked_at=now(),
    revocation_reason_code='V13_ROLE_MODEL_REDESIGN', updated_at=now(),
    row_version=rp.row_version+1
FROM identity.roles r, identity.permissions p
WHERE rp.role_id=r.role_id AND rp.permission_id=p.permission_id
  AND r.role_code IN ('SYSTEM_ADMINISTRATOR','OPERATIONS_SUPERVISOR','SITE_OPERATOR','PARKING_ATTENDANT','APT_CASHIER_OPERATOR')
  AND rp.binding_status='ACTIVE'
  AND NOT EXISTS (SELECT 1 FROM approved_role_permissions a
                  WHERE a.role_code=r.role_code AND a.permission_code=p.permission_code);

INSERT INTO identity.role_permissions (
    role_permission_id, role_id, permission_id, binding_status,
    binding_reason_code, assigned_by_service_identity_id, effective_from,
    created_by_service_identity_id, updated_by_service_identity_id)
SELECT gen_random_uuid(), r.role_id, p.permission_id, 'ACTIVE',
       'V13_ROLE_MODEL_REDESIGN', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(),
       '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM approved_role_permissions a
JOIN identity.roles r ON r.role_code=a.role_code
JOIN identity.permissions p ON p.permission_code=a.permission_code
WHERE NOT EXISTS (SELECT 1 FROM identity.role_permissions rp
                  WHERE rp.role_id=r.role_id AND rp.permission_id=p.permission_id
                    AND rp.binding_status='ACTIVE');

COMMIT;
