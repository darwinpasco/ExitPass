-- ExitPass Reference Data v1.2
-- Purpose: controlled baseline data for local development and integration testing.
-- This script must be executed only after ExitPass_Full_Database_Creation_DDL_v1.2.sql.
-- This script is idempotent for the declared natural keys.
-- Do not store production secrets, private keys, webhook secrets, or legal production policy data here.

BEGIN;
-- 1. Service identities, including device identities
INSERT INTO identity.service_identities (service_identity_id, service_identity_code, service_identity_name, identity_type, identity_status, owning_service_name, credential_reference, credential_type, effective_from) VALUES('1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','seed.reference-data','ExitPass Reference Data Seeder','INTERNAL_SERVICE','ACTIVE','ExitPass.DbMigrator',NULL,NULL, now()),
('8063c159-dae6-57af-9f1f-e0a07d519fb2','central-pms','Central PMS','INTERNAL_SERVICE','ACTIVE','CentralPms','secret://exitpass/central-pms','MTLS_CERTIFICATE_REFERENCE', now()),
('d47280c6-5e0f-528d-8b22-f026e13f9cd6','payment-orchestrator','Payment Orchestrator','INTERNAL_SERVICE','ACTIVE','PaymentOrchestrator','secret://exitpass/payment-orchestrator','MTLS_CERTIFICATE_REFERENCE', now()),
('74e262ae-f55d-5292-8cd2-071c66e753e7','session-service','Session Service','INTERNAL_SERVICE','ACTIVE','SessionService','secret://exitpass/session-service','MTLS_CERTIFICATE_REFERENCE', now()),
('62bf1144-c31f-5152-bf9c-c03d9aa98dc3','vendor-pms-adapter','Vendor PMS Adapter','ADAPTER','ACTIVE','VendorPmsAdapter','secret://exitpass/vendor-pms-adapter','MTLS_CERTIFICATE_REFERENCE', now()),
('62ce8f16-4a7b-5e2e-b4a0-c3e711228597','gate-integration-service','Gate Integration Service','INTERNAL_SERVICE','ACTIVE','GateIntegrationService','secret://exitpass/gate-integration-service','MTLS_CERTIFICATE_REFERENCE', now()),
('e986ee3d-36bf-501f-96dd-04085117e2c1','coupon-service','Coupon Service','INTERNAL_SERVICE','ACTIVE','CouponService','secret://exitpass/coupon-service','MTLS_CERTIFICATE_REFERENCE', now()),
('8e1477d3-f78a-53ff-88f8-97118c44d6b2','audit-event-service','Audit and Event Service','INTERNAL_SERVICE','ACTIVE','AuditEventService','secret://exitpass/audit-event-service','MTLS_CERTIFICATE_REFERENCE', now()),
('2d881206-4526-5762-ab51-b162006e1ea7','api-gateway','API Gateway','GATEWAY','ACTIVE','ApiGateway','secret://exitpass/api-gateway','MTLS_CERTIFICATE_REFERENCE', now()),
('16d18dcd-5a93-5105-ab7a-0beada6adb03','webpay-ui','WebPay UI Client','EXTERNAL_CLIENT','ACTIVE','WebPayUi',NULL,NULL, now()),
('dff2b80b-799b-5e55-9eee-8495c571144d','mock-payment-provider','Mock Payment Provider','WEBHOOK_RECEIVER','ACTIVE','MockPaymentProvider','secret://exitpass/mock-payment-provider','KEY_VAULT_REFERENCE', now()),
('273afb7e-5ad9-57d6-bafe-3c5db5cd03c7','mock-vendor-pms','Mock Vendor PMS','ADAPTER','ACTIVE','MockVendorPms','secret://exitpass/mock-vendor-pms','API_KEY_REFERENCE', now()),
('644c34c9-a2e7-598f-a125-96d823b954bf','gate-device-mnt-exit-01','Mactan Newtown Exit Gate Device 01','DEVICE','ACTIVE','GateIntegrationService','secret://exitpass/gate-device-mnt-exit-01','MTLS_CERTIFICATE_REFERENCE', now()),
('d6b8ef5b-2690-5636-a2cb-0c71d56e45b2','gate-device-mnt-exit-02','Mactan Newtown Exit Gate Device 02','DEVICE','ACTIVE','GateIntegrationService','secret://exitpass/gate-device-mnt-exit-02','MTLS_CERTIFICATE_REFERENCE', now())
ON CONFLICT ON CONSTRAINT uq_service_identities__service_identity_code DO UPDATE SET
  service_identity_name = EXCLUDED.service_identity_name,
  identity_type = EXCLUDED.identity_type,
  identity_status = EXCLUDED.identity_status,
  owning_service_name = EXCLUDED.owning_service_name,
  credential_reference = EXCLUDED.credential_reference,
  credential_type = EXCLUDED.credential_type,
  updated_at = now();

-- 2. Roles and permissions
INSERT INTO identity.roles (role_id, role_code, role_name, role_description, role_type, role_status, is_privileged, requires_elevated_approval, effective_from, created_by_service_identity_id, updated_by_service_identity_id) VALUES('1700b21d-b2b2-5a37-a513-51616a11f9a7','SYSTEM_ADMIN','System Administrator','Baseline System Administrator role for ExitPass v1.2 reference data.','SYSTEM','ACTIVE',true,true,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('fbd34936-51e8-5737-85fe-d44be36825e4','OPERATIONS_MANAGER','Operations Manager','Baseline Operations Manager role for ExitPass v1.2 reference data.','OPERATIONS','ACTIVE',true,true,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('d187851f-88fd-5974-b595-07367ad1a3b4','SITE_OPERATOR','Site Operator','Baseline Site Operator role for ExitPass v1.2 reference data.','OPERATIONS','ACTIVE',false,false,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('6454f23a-d261-5abf-9072-fd9f7f9d76f9','SUPPORT_AGENT','Support Agent','Baseline Support Agent role for ExitPass v1.2 reference data.','SUPPORT','ACTIVE',false,false,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('b2ecfdb0-2524-5b69-91d4-7f5d23d7c1e6','FINANCE_RECONCILIATION','Finance Reconciliation','Baseline Finance Reconciliation role for ExitPass v1.2 reference data.','FINANCE','ACTIVE',false,false,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('bc1205aa-5b1f-509a-bc23-00f676fb9f10','COMPLIANCE_REVIEWER','Compliance Reviewer','Baseline Compliance Reviewer role for ExitPass v1.2 reference data.','COMPLIANCE','ACTIVE',true,true,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('2a11b273-b3dc-5c46-94ce-3c303c6864a6','MERCHANT_ADMIN','Merchant Administrator','Baseline Merchant Administrator role for ExitPass v1.2 reference data.','MERCHANT','ACTIVE',false,false,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('990565d5-e7f6-525d-9e35-9a706cfb451d','SECURITY_REVIEWER','Security Reviewer','Baseline Security Reviewer role for ExitPass v1.2 reference data.','SECURITY','ACTIVE',true,true,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('cf8c3bfa-c6ff-5838-b561-ef7c99c49955','SERVICE_PRINCIPAL','Service Principal','Baseline Service Principal role for ExitPass v1.2 reference data.','SERVICE','ACTIVE',false,false,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978')
ON CONFLICT ON CONSTRAINT uq_roles__role_code DO UPDATE SET
  role_name = EXCLUDED.role_name, role_description = EXCLUDED.role_description, role_type = EXCLUDED.role_type,
  role_status = EXCLUDED.role_status, is_privileged = EXCLUDED.is_privileged,
  requires_elevated_approval = EXCLUDED.requires_elevated_approval, updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;

INSERT INTO identity.permissions (permission_id, permission_code, permission_name, permission_description, permission_domain, permission_action, permission_status, is_sensitive, requires_audit, created_by_service_identity_id, updated_by_service_identity_id) VALUES('e3eaa4ec-0176-527f-b7c4-4854731e0042','sessions.resolve','Resolve parking sessions','Baseline permission for sessions.resolve.','sessions','resolve','ACTIVE',false,true,'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('08948391-7a90-56fc-8025-5042960b6ed4','tariffs.quote','Create tariff quote','Baseline permission for tariffs.quote.','tariffs','quote','ACTIVE',false,true,'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('fedc4f77-7e66-5cf2-b204-00a9015c600e','payments.create_attempt','Create payment attempt','Baseline permission for payments.create_attempt.','payments','create_attempt','ACTIVE',false,true,'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('f69f1da9-ae0d-58f4-8a31-9f9e9c17bfc3','payments.finalize','Finalize payment attempt','Baseline permission for payments.finalize.','payments','finalize','ACTIVE',true,true,'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('e40a24bd-ab99-5028-b559-33c7573e2988','payments.provider_callback','Process provider callback','Baseline permission for payments.provider_callback.','payments','provider_callback','ACTIVE',true,true,'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('9da2d128-5061-5b2a-990e-48be0d561341','exit_authorizations.issue','Issue exit authorization','Baseline permission for exit_authorizations.issue.','exit_authorizations','issue','ACTIVE',true,true,'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('ef3fc247-edab-59f6-b66b-703896ead3ec','gate.consume_authorization','Consume exit authorization','Baseline permission for gate.consume_authorization.','gate','consume_authorization','ACTIVE',true,true,'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('36184eac-187b-5e53-8076-43d0a123d05e','gate.record_event','Record gate event','Baseline permission for gate.record_event.','gate','record_event','ACTIVE',false,true,'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('3ef898a0-7da4-521b-bb12-2497f6c23a34','discounts.validate_statutory','Validate statutory discount','Baseline permission for discounts.validate_statutory.','discounts','validate_statutory','ACTIVE',true,true,'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('aaa47ae6-acd6-55cf-87ea-44f858b9968f','coupons.manage','Manage coupons','Baseline permission for coupons.manage.','coupons','manage','ACTIVE',true,true,'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('2d5a186e-4a06-5a12-8082-a4b62513fec0','coupons.apply','Apply coupons','Baseline permission for coupons.apply.','coupons','apply','ACTIVE',false,true,'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('3a14255d-8a74-5f2b-8132-f4649972b8b8','operations.manual_gate','Record manual gate action','Baseline permission for operations.manual_gate.','operations','manual_gate','ACTIVE',true,true,'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('222ad79e-5810-50d8-a123-9fcfbea897a7','reconciliation.manage','Manage reconciliation','Baseline permission for reconciliation.manage.','reconciliation','manage','ACTIVE',true,true,'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('00221793-9fd6-579f-8ae7-dd8b7c08fc1a','audit.read','Read audit events','Baseline permission for audit.read.','audit','read','ACTIVE',true,true,'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('e98a55a7-69a8-5db7-a5dc-669188c2a77d','evidence.read_restricted','Read restricted evidence','Baseline permission for evidence.read_restricted.','evidence','read_restricted','ACTIVE',true,true,'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('5dba3ad7-927d-54ed-8f5d-10a91517b3c0','config.manage','Manage configuration','Baseline permission for config.manage.','config','manage','ACTIVE',true,true,'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('5c4c129a-bc76-5160-9582-491bb57456e1','identity.manage','Manage identity','Baseline permission for identity.manage.','identity','manage','ACTIVE',true,true,'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978')
ON CONFLICT ON CONSTRAINT uq_permissions__permission_code DO UPDATE SET
  permission_name = EXCLUDED.permission_name, permission_description = EXCLUDED.permission_description,
  permission_domain = EXCLUDED.permission_domain, permission_action = EXCLUDED.permission_action,
  permission_status = EXCLUDED.permission_status, is_sensitive = EXCLUDED.is_sensitive,
  requires_audit = EXCLUDED.requires_audit, updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;

-- Role-permission bindings. Uses NOT EXISTS because active binding uniqueness is enforced by a partial unique index.
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '84260e8e-0892-571e-92fd-9d46935f7f37', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'sessions.resolve'
WHERE r.role_code = 'SYSTEM_ADMIN'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '8e8c119d-402e-50ec-9b07-4010afd06ee4', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'tariffs.quote'
WHERE r.role_code = 'SYSTEM_ADMIN'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'f2cf31c9-7b1e-54b8-92ec-9feffb302fe1', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'payments.create_attempt'
WHERE r.role_code = 'SYSTEM_ADMIN'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '35f971f5-459c-5fd1-8f43-1ffe2b222748', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'payments.finalize'
WHERE r.role_code = 'SYSTEM_ADMIN'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '0fed1ff0-fd69-5c00-99c4-d397697f5ead', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'payments.provider_callback'
WHERE r.role_code = 'SYSTEM_ADMIN'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '081126e5-225d-5dfb-9a6e-cf7bc92fd63f', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'exit_authorizations.issue'
WHERE r.role_code = 'SYSTEM_ADMIN'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'd8912736-a85f-5d9c-93b3-deb5738d6c92', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'gate.consume_authorization'
WHERE r.role_code = 'SYSTEM_ADMIN'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '200ec6f2-150c-5a9b-80e7-d0aa3fb2009a', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'gate.record_event'
WHERE r.role_code = 'SYSTEM_ADMIN'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'a4652c4e-3251-5334-b2de-5668180cfc21', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'discounts.validate_statutory'
WHERE r.role_code = 'SYSTEM_ADMIN'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '32e427b4-355e-5d8e-b811-a046d91e7c9e', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'coupons.manage'
WHERE r.role_code = 'SYSTEM_ADMIN'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '04bb5196-09da-5126-a200-4f91de5603f0', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'coupons.apply'
WHERE r.role_code = 'SYSTEM_ADMIN'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '20389737-cb1e-58c1-a0e4-8f4312b2d744', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'operations.manual_gate'
WHERE r.role_code = 'SYSTEM_ADMIN'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'd2545ef1-d42a-5129-a202-9a483dbf59a9', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'reconciliation.manage'
WHERE r.role_code = 'SYSTEM_ADMIN'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'bb0e7337-5125-527f-bbb9-f2cde9d39b12', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'audit.read'
WHERE r.role_code = 'SYSTEM_ADMIN'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'f12882f0-c3fc-5f81-acbb-848c2146285b', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'evidence.read_restricted'
WHERE r.role_code = 'SYSTEM_ADMIN'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '79109408-9f22-5e05-b1bb-e673816de2b6', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'config.manage'
WHERE r.role_code = 'SYSTEM_ADMIN'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'd3ee60bb-7060-54b1-95c7-2615d1e30c04', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'identity.manage'
WHERE r.role_code = 'SYSTEM_ADMIN'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '4e6fbe9b-187b-59b7-8819-80d9d347700e', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'sessions.resolve'
WHERE r.role_code = 'OPERATIONS_MANAGER'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '5f5105bc-61d9-5d63-a8fd-372566a5e08f', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'tariffs.quote'
WHERE r.role_code = 'OPERATIONS_MANAGER'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '30f3c0b3-2d8b-511b-b5f9-6fbb229c1158', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'gate.consume_authorization'
WHERE r.role_code = 'OPERATIONS_MANAGER'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'e14a50e9-8c3f-54d9-8738-f2ad64c9c49e', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'gate.record_event'
WHERE r.role_code = 'OPERATIONS_MANAGER'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '6b809cd4-6ad2-5f97-abff-63a187e7a484', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'operations.manual_gate'
WHERE r.role_code = 'OPERATIONS_MANAGER'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'aa66c4f7-7bbd-5798-b8ef-ea47aac16720', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'reconciliation.manage'
WHERE r.role_code = 'OPERATIONS_MANAGER'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '93522106-b984-5a8a-a235-9c06ed36dded', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'audit.read'
WHERE r.role_code = 'OPERATIONS_MANAGER'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '4eef04bb-e4b2-5b3e-9066-f6e12f021482', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'sessions.resolve'
WHERE r.role_code = 'SITE_OPERATOR'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'd98981d7-a4ba-5ff4-8dd1-97c693386dc8', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'gate.consume_authorization'
WHERE r.role_code = 'SITE_OPERATOR'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'e78d7cf4-a760-574c-a97e-9beba03c498e', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'gate.record_event'
WHERE r.role_code = 'SITE_OPERATOR'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '241d2ed5-65e2-52e0-a407-80af019b292d', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'operations.manual_gate'
WHERE r.role_code = 'SITE_OPERATOR'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'eba5bb29-91d0-54c5-85fc-2120ed77b465', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'sessions.resolve'
WHERE r.role_code = 'SUPPORT_AGENT'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '685fa54f-edd2-5047-9538-fba2d58ce269', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'tariffs.quote'
WHERE r.role_code = 'SUPPORT_AGENT'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'b3ef46a4-f4e3-59d5-ae46-82172bf5a1ff', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'payments.create_attempt'
WHERE r.role_code = 'SUPPORT_AGENT'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '5ce9bcd7-7e80-536d-b06c-de4b73c21989', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'coupons.apply'
WHERE r.role_code = 'SUPPORT_AGENT'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'bde479ab-82ce-5140-bdfb-ad40092c4dbc', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'discounts.validate_statutory'
WHERE r.role_code = 'SUPPORT_AGENT'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'c534e194-a8ab-527c-979a-b33df52c85bb', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'audit.read'
WHERE r.role_code = 'SUPPORT_AGENT'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '53c5e093-11ff-56ea-a038-6b0e5757a7c4', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'payments.finalize'
WHERE r.role_code = 'FINANCE_RECONCILIATION'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '08c6a4fd-cb07-5cd5-a190-2e3b3b53a4ac', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'reconciliation.manage'
WHERE r.role_code = 'FINANCE_RECONCILIATION'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '50c81c45-9de8-5e02-82f8-1de6fbb462c9', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'audit.read'
WHERE r.role_code = 'FINANCE_RECONCILIATION'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'a6c6b5b4-a951-5cff-b7d8-dc5e2944c5e9', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'discounts.validate_statutory'
WHERE r.role_code = 'COMPLIANCE_REVIEWER'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'cf2039f8-02df-5fd7-912d-fee19ce95df6', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'audit.read'
WHERE r.role_code = 'COMPLIANCE_REVIEWER'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'd2739842-8a59-5e6d-8412-9ef15b566152', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'evidence.read_restricted'
WHERE r.role_code = 'COMPLIANCE_REVIEWER'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'dc2b99b8-3a61-5d63-9deb-acfc8a4bbc97', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'coupons.manage'
WHERE r.role_code = 'MERCHANT_ADMIN'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '255adaf5-154f-50f0-be29-b59d43d1adbd', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'coupons.apply'
WHERE r.role_code = 'MERCHANT_ADMIN'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'b209511d-7f45-555e-8b49-4fa67814731e', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'audit.read'
WHERE r.role_code = 'MERCHANT_ADMIN'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '57bcc55a-9629-512d-bd64-3c1090a32e9e', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'audit.read'
WHERE r.role_code = 'SECURITY_REVIEWER'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '039c9120-ac17-522b-99ff-31483d3bc0df', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'evidence.read_restricted'
WHERE r.role_code = 'SECURITY_REVIEWER'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '85c72154-ddba-5dc5-ba03-07b7b1abab2a', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'identity.manage'
WHERE r.role_code = 'SECURITY_REVIEWER'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '4f243c9c-6359-57bb-8f24-5e6ec1d62c91', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'sessions.resolve'
WHERE r.role_code = 'SERVICE_PRINCIPAL'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '4d7265e5-8dab-5ecf-813b-b6703da75f81', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'tariffs.quote'
WHERE r.role_code = 'SERVICE_PRINCIPAL'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '23ee4fa2-8efb-5aa4-bb91-622537fe5474', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'payments.create_attempt'
WHERE r.role_code = 'SERVICE_PRINCIPAL'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '7ea0bec6-a1b0-571a-bb56-cf02d6468863', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'payments.finalize'
WHERE r.role_code = 'SERVICE_PRINCIPAL'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '7b9544e9-8d60-5ae1-aeb1-7795fbfa3e5b', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'payments.provider_callback'
WHERE r.role_code = 'SERVICE_PRINCIPAL'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'd588ac83-928b-5cfc-8066-8d63f1fc9e3c', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'exit_authorizations.issue'
WHERE r.role_code = 'SERVICE_PRINCIPAL'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '2b8125fd-f646-557c-bc9f-544fe38e8223', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'gate.consume_authorization'
WHERE r.role_code = 'SERVICE_PRINCIPAL'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '0cf510dd-48d6-5fbc-8174-8c07c16cde64', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'gate.record_event'
WHERE r.role_code = 'SERVICE_PRINCIPAL'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '2141de8e-8265-5e9b-b160-3c1bd9b4de76', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'coupons.apply'
WHERE r.role_code = 'SERVICE_PRINCIPAL'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');
INSERT INTO identity.role_permissions (role_permission_id, role_id, permission_id, binding_status, binding_reason_code, assigned_by_service_identity_id, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'b147c7b2-1de6-5761-8c6f-76535bc38fa7', r.role_id, p.permission_id, 'ACTIVE', 'BASELINE_REFERENCE_DATA', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM identity.roles r
JOIN identity.permissions p ON p.permission_code = 'discounts.validate_statutory'
WHERE r.role_code = 'SERVICE_PRINCIPAL'
  AND NOT EXISTS (SELECT 1 FROM identity.role_permissions rp WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id AND rp.binding_status = 'ACTIVE');

-- 3. Controlled code sets
INSERT INTO config.controlled_code_sets (controlled_code_set_id, code_set_name, code_value, code_label, code_description, code_domain, code_status, sort_order, requires_comment, requires_approval, is_sensitive, effective_from, created_by_service_identity_id, updated_by_service_identity_id) VALUES('8c7ea04a-8425-530c-baee-edf6f2fbecf6','OVERRIDE_REASON','MANUAL_GATE_DEVICE_FAILURE','Manual gate device failure','Baseline controlled code for OVERRIDE_REASON.','operations','ACTIVE',1,true,true,false,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('49041f70-b2d2-5c23-9268-cd43ee639f18','OVERRIDE_REASON','PAYMENT_PROVIDER_CONFIRMED_SUPPORT','Payment provider confirmed by support','Baseline controlled code for OVERRIDE_REASON.','operations','ACTIVE',2,true,true,true,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('103a2dc5-1b13-5bfd-9022-6477be05e3d3','OVERRIDE_REASON','EMERGENCY_SITE_RELEASE','Emergency site release','Baseline controlled code for OVERRIDE_REASON.','operations','ACTIVE',3,true,true,true,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('f34ce58a-3563-51ae-8313-55aca03a082e','INCIDENT_CATEGORY','PAYMENT_PROVIDER_OUTAGE','Payment provider outage','Baseline controlled code for INCIDENT_CATEGORY.','operations','ACTIVE',1,false,false,false,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('136c4297-ec3e-5228-9f77-b614bad288aa','INCIDENT_CATEGORY','VENDOR_PMS_OUTAGE','Vendor PMS outage','Baseline controlled code for INCIDENT_CATEGORY.','operations','ACTIVE',2,false,false,false,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('fcb8b523-eeb1-51d2-9913-f2771cd50dd1','INCIDENT_CATEGORY','GATE_DEVICE_FAILURE','Gate device failure','Baseline controlled code for INCIDENT_CATEGORY.','operations','ACTIVE',3,false,false,false,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('25c71bc5-6602-5e36-955a-d1b342c0ec04','RECONCILIATION_EXCEPTION_REASON','AMOUNT_MISMATCH','Amount mismatch','Baseline controlled code for RECONCILIATION_EXCEPTION_REASON.','reconciliation','ACTIVE',1,true,true,false,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('f4e327b2-ccea-53c8-8b21-2360642910a2','RECONCILIATION_EXCEPTION_REASON','MISSING_PROVIDER_OUTCOME','Missing provider outcome','Baseline controlled code for RECONCILIATION_EXCEPTION_REASON.','reconciliation','ACTIVE',2,true,true,false,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('d0f0e585-dd8d-5b68-8700-ea0a721e2b82','RECONCILIATION_EXCEPTION_REASON','MANUAL_GATE_WITHOUT_PAYMENT','Manual gate without payment','Baseline controlled code for RECONCILIATION_EXCEPTION_REASON.','reconciliation','ACTIVE',3,true,true,true,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('aecd4440-44a6-5e23-a3d0-3e0e1b6a6831','AUDIT_RESULT_CODE','SUCCESS','Success','Baseline controlled code for AUDIT_RESULT_CODE.','audit','ACTIVE',1,false,false,false,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('c08ec5c4-df0b-55c3-a309-e20ba835f8a6','AUDIT_RESULT_CODE','DENIED','Denied','Baseline controlled code for AUDIT_RESULT_CODE.','audit','ACTIVE',2,true,false,false,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('e3b48dc5-0a85-507b-b653-5b5c9f7bb060','AUDIT_RESULT_CODE','FAILED','Failed','Baseline controlled code for AUDIT_RESULT_CODE.','audit','ACTIVE',3,true,false,false,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('04625f7a-3ce4-590f-af1e-7c13e5279616','INTEGRATION_ERROR_CLASSIFICATION','TIMEOUT','Timeout','Baseline controlled code for INTEGRATION_ERROR_CLASSIFICATION.','integration','ACTIVE',1,false,false,false,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('16087003-db15-5092-b2a4-e1b5664c0098','INTEGRATION_ERROR_CLASSIFICATION','AUTHENTICATION_FAILED','Authentication failed','Baseline controlled code for INTEGRATION_ERROR_CLASSIFICATION.','integration','ACTIVE',2,true,false,true,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('097f4649-4bc3-53f6-a49e-e1dc20e275b0','INTEGRATION_ERROR_CLASSIFICATION','SCHEMA_VALIDATION_FAILED','Schema validation failed','Baseline controlled code for INTEGRATION_ERROR_CLASSIFICATION.','integration','ACTIVE',3,false,false,false,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('fc28e6a5-5579-5d3d-80c1-81eff6469be0','OPERATOR_ACTION_REASON','SUPPORT_RECHECK','Support recheck','Baseline controlled code for OPERATOR_ACTION_REASON.','operations','ACTIVE',1,true,false,false,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('da373bbc-694d-547b-85fe-ea9e305f0ea6','OPERATOR_ACTION_REASON','EVIDENCE_REVIEW','Evidence review','Baseline controlled code for OPERATOR_ACTION_REASON.','operations','ACTIVE',2,true,true,true,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('d7c760d5-ba3f-5f14-a23c-5a3f1285ac2a','GATE_EVENT_REASON','AUTHORIZATION_CONSUMED','Authorization consumed','Baseline controlled code for GATE_EVENT_REASON.','gates','ACTIVE',1,false,false,false,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('cf1faedb-0599-56b8-a185-77fe56dd5afe','GATE_EVENT_REASON','BARRIER_OPEN_FAILED','Barrier open failed','Baseline controlled code for GATE_EVENT_REASON.','gates','ACTIVE',2,true,false,false,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('dceca7ec-4e75-5c44-a7be-8bc4b5d311c9','DISCOUNT_VALIDATION_REASON','SENIOR_CITIZEN_VALIDATED','Senior citizen validated','Baseline controlled code for DISCOUNT_VALIDATION_REASON.','discounts','ACTIVE',1,true,false,true,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('7c3f2695-07a7-559d-8d9f-c080dd550f0c','DISCOUNT_VALIDATION_REASON','PWD_VALIDATED','PWD validated','Baseline controlled code for DISCOUNT_VALIDATION_REASON.','discounts','ACTIVE',2,true,false,true,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('d0eda611-66d7-5a4f-8b18-8ebd68216f17','DISCOUNT_VALIDATION_REASON','EVIDENCE_INSUFFICIENT','Evidence insufficient','Baseline controlled code for DISCOUNT_VALIDATION_REASON.','discounts','ACTIVE',3,true,true,true,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('676629a4-59e2-5dc5-8a81-42290e6ac363','COUPON_APPLICATION_REASON','MERCHANT_COUPON_RESERVED','Merchant coupon reserved','Baseline controlled code for COUPON_APPLICATION_REASON.','coupons','ACTIVE',1,false,false,false,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('6f41296b-e5e7-5105-8d38-b4a0d8a5c758','COUPON_APPLICATION_REASON','INSUFFICIENT_WALLET_BALANCE','Insufficient wallet balance','Baseline controlled code for COUPON_APPLICATION_REASON.','coupons','ACTIVE',2,true,false,false,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978')
ON CONFLICT ON CONSTRAINT uq_controlled_code_sets__set_value_domain DO UPDATE SET
  code_label = EXCLUDED.code_label, code_description = EXCLUDED.code_description, code_status = EXCLUDED.code_status,
  sort_order = EXCLUDED.sort_order, requires_comment = EXCLUDED.requires_comment,
  requires_approval = EXCLUDED.requires_approval, is_sensitive = EXCLUDED.is_sensitive,
  updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;

-- 4. TTL policies
INSERT INTO config.ttl_policies (ttl_policy_id, policy_code, policy_name, policy_description, policy_domain, ttl_scope_type, ttl_seconds, grace_period_seconds, expiry_action, policy_status, effective_from, created_by_service_identity_id, updated_by_service_identity_id) VALUES('9da2035a-5bee-5872-a92d-4b4749447912','TARIFF_SNAPSHOT_DEFAULT_10M','Tariff Snapshot Default 10 Minutes','Baseline TTL policy for TARIFF_SNAPSHOT.','core','TARIFF_SNAPSHOT',600,60,'EXPIRE_RECORD','ACTIVE',now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('99204abc-8c7e-5a9c-997e-31248ce71c32','PAYMENT_ATTEMPT_DEFAULT_15M','Payment Attempt Default 15 Minutes','Baseline TTL policy for PAYMENT_ATTEMPT.','core','PAYMENT_ATTEMPT',900,60,'EXPIRE_RECORD','ACTIVE',now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('13cc9abb-c97e-57a2-ae9a-462213f66298','PROVIDER_SESSION_DEFAULT_15M','Provider Session Default 15 Minutes','Baseline TTL policy for PROVIDER_SESSION.','payments','PROVIDER_SESSION',900,60,'EXPIRE_RECORD','ACTIVE',now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('55871b55-5f25-5775-90c6-1090b41f8685','COUPON_RESERVATION_DEFAULT_10M','Coupon Reservation Default 10 Minutes','Baseline TTL policy for COUPON_RESERVATION.','coupons','COUPON_RESERVATION',600,60,'RELEASE_RESERVATION','ACTIVE',now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('26c728ca-7cca-5e41-9fca-2653d6d25b1a','STATUTORY_VALIDATION_DEFAULT_30M','Statutory Validation Default 30 Minutes','Baseline TTL policy for STATUTORY_DISCOUNT_VALIDATION.','discounts','STATUTORY_DISCOUNT_VALIDATION',1800,120,'EXPIRE_RECORD','ACTIVE',now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('5bd8067b-66e7-55ff-bf10-3ab1775a1aa3','SESSION_LOOKUP_CACHE_DEFAULT_5M','Session Lookup Cache Default 5 Minutes','Baseline TTL policy for SESSION_LOOKUP_CACHE.','sessions','SESSION_LOOKUP_CACHE',300,30,'EXPIRE_RECORD','ACTIVE',now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('f056973b-083d-5635-8ce1-5fd3c57a7298','EXIT_AUTHORIZATION_DEFAULT_15M','Exit Authorization Default 15 Minutes','Baseline TTL policy for EXIT_AUTHORIZATION.','core','EXIT_AUTHORIZATION',900,60,'INVALIDATE_RECORD','ACTIVE',now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('8dcfc0cf-5a67-597f-a285-7cd091248a54','PROVIDER_CALLBACK_REPLAY_10M','Provider Callback Replay Window 10 Minutes','Baseline TTL policy for PROVIDER_CALLBACK_REPLAY_WINDOW.','payments','PROVIDER_CALLBACK_REPLAY_WINDOW',600,0,'BLOCK_USE','ACTIVE',now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('a1ea8463-b86c-5aa1-ada3-f5b9d93aece5','EVIDENCE_RETENTION_DEV_30D','Evidence Retention Local Development 30 Days','Baseline TTL policy for EVIDENCE_RETENTION.','audit','EVIDENCE_RETENTION',2592000,0,'PURGE_OR_ARCHIVE','ACTIVE',now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('1ab4fd49-21aa-5bad-94f6-083fb31fe520','OUTBOX_RETRY_DEFAULT_1D','Outbox Retry Default 1 Day','Baseline TTL policy for OUTBOX_RETRY.','events','OUTBOX_RETRY',86400,0,'NOTIFY_ONLY','ACTIVE',now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978')
ON CONFLICT ON CONSTRAINT uq_ttl_policies__policy_code DO UPDATE SET
  policy_name = EXCLUDED.policy_name, policy_description = EXCLUDED.policy_description, policy_domain = EXCLUDED.policy_domain,
  ttl_scope_type = EXCLUDED.ttl_scope_type, ttl_seconds = EXCLUDED.ttl_seconds, grace_period_seconds = EXCLUDED.grace_period_seconds,
  expiry_action = EXCLUDED.expiry_action, policy_status = EXCLUDED.policy_status, updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;

-- 5. Rate-limit policies
INSERT INTO config.rate_limit_policies (rate_limit_policy_id, policy_code, policy_name, policy_description, policy_domain, scope_type, window_seconds, max_requests, burst_limit, penalty_seconds, policy_status, enforcement_mode, effective_from, created_by_service_identity_id, updated_by_service_identity_id) VALUES('bb22e95d-9225-502b-bf0f-fbbcebcf1ad0','PUBLIC_LOOKUP_DEFAULT','Public Lookup Default','Baseline rate-limit policy for PUBLIC_LOOKUP.','public','PUBLIC_LOOKUP',60,30,10,300,'ACTIVE','ENFORCE',now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('a10f1e44-e6a6-5d67-9ff9-06a64427a854','PAYMENT_CREATE_DEFAULT','Payment Create Default','Baseline rate-limit policy for PAYMENT_CREATE.','payments','PAYMENT_CREATE',60,20,5,300,'ACTIVE','ENFORCE',now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('8f5a3fa7-1870-5187-b5c1-80438e76f10a','PROVIDER_CALLBACK_DEFAULT','Provider Callback Default','Baseline rate-limit policy for PROVIDER_CALLBACK.','payments','PROVIDER_CALLBACK',60,300,100,60,'ACTIVE','ENFORCE',now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('5d8c2bc3-5ee4-5731-b640-36ba88d1ea90','GATE_CONSUME_DEFAULT','Gate Consume Default','Baseline rate-limit policy for GATE_CONSUME.','gates','GATE_CONSUME',60,120,30,120,'ACTIVE','ENFORCE',now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('57e1908b-e8bc-5590-9f94-7d7d5a8053d9','ADMIN_API_DEFAULT','Admin API Default','Baseline rate-limit policy for ADMIN_API.','admin','ADMIN_API',60,60,20,300,'ACTIVE','ENFORCE',now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('f6fbaab6-0b5e-5314-8bcb-d89215dfe589','SERVICE_TO_SERVICE_DEFAULT','Service-to-Service Default','Baseline rate-limit policy for SERVICE_TO_SERVICE.','internal','SERVICE_TO_SERVICE',60,600,200,60,'ACTIVE','MONITOR_ONLY',now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('fd8b5d7f-03d0-5934-860a-a4ca1f6383b5','DEVICE_DEFAULT','Device Default','Baseline rate-limit policy for DEVICE.','devices','DEVICE',60,180,60,120,'ACTIVE','ENFORCE',now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978')
ON CONFLICT ON CONSTRAINT uq_rate_limit_policies__policy_code DO UPDATE SET
  policy_name = EXCLUDED.policy_name, policy_description = EXCLUDED.policy_description, policy_domain = EXCLUDED.policy_domain,
  scope_type = EXCLUDED.scope_type, window_seconds = EXCLUDED.window_seconds, max_requests = EXCLUDED.max_requests,
  burst_limit = EXCLUDED.burst_limit, penalty_seconds = EXCLUDED.penalty_seconds, policy_status = EXCLUDED.policy_status,
  enforcement_mode = EXCLUDED.enforcement_mode, updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;

-- 6. Integration vendor systems
INSERT INTO integration.vendor_systems (vendor_system_id, vendor_code, vendor_name, vendor_system_type, vendor_system_status, environment_code, base_url_ref, api_version, owner_team, support_contact_ref, effective_from, created_by_service_identity_id, updated_by_service_identity_id) VALUES('cc0e092d-1104-58ff-8593-08a853f344e0','MOCK_VENDOR_PMS','Mock Vendor PMS','VENDOR_PMS','ACTIVE','DEV','internal://mock-vendor-pms','v1','ExitPass Engineering','dev-support',now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('b5134fc7-ada5-5ad0-a6ba-019486a51640','HIKCENTRAL_DEV','HikCentral Development Adapter','VENDOR_PMS','DRAFT','DEV','secret://integration/hikcentral/base-url/dev','v3.1','Integrations','hikvision-integration',now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('fb54413f-5cd0-5766-8935-afc882375801','PAYMONGO_DEV','PayMongo Development Provider','PAYMENT_PROVIDER','ACTIVE','DEV','secret://integration/paymongo/base-url/dev','v1','Payments','paymongo-support',now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('d7dc13cf-6236-5407-bf10-0aaa21373eb6','AUB_DEV','AUB Development Provider','PAYMENT_PROVIDER','DRAFT','DEV','secret://integration/aub/base-url/dev','v1','Payments','aub-support',now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('86f7855b-4f2f-5da9-b37e-4ee93e199b52','MOCK_GATE_CONTROLLER','Mock Gate Controller','GATE_CONTROLLER','ACTIVE','DEV','internal://mock-gate-controller','v1','Site Engineering','dev-support',now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('68969950-f361-5f70-bd91-5f59f0289071','MOCK_MOPS_PROVIDER','Mock MoPS Provider','MOPS_PROVIDER','ACTIVE','DEV','internal://mock-mops','v1','Operations','dev-support',now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978')
ON CONFLICT ON CONSTRAINT uq_vendor_systems__vendor_code_environment DO UPDATE SET
  vendor_name = EXCLUDED.vendor_name, vendor_system_type = EXCLUDED.vendor_system_type, vendor_system_status = EXCLUDED.vendor_system_status,
  base_url_ref = EXCLUDED.base_url_ref, api_version = EXCLUDED.api_version, owner_team = EXCLUDED.owner_team, support_contact_ref = EXCLUDED.support_contact_ref,
  updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;

-- 7. Vendor endpoints
INSERT INTO integration.vendor_endpoints (vendor_endpoint_id, vendor_system_id, endpoint_code, endpoint_name, endpoint_description, endpoint_type, http_method, path_template, operation_ref, timeout_policy_code, retry_policy_code, rate_limit_policy_code, endpoint_status, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '31655c74-4b39-5c68-8caf-3183e869d23e', vs.vendor_system_id, 'SESSION_LOOKUP', 'Session Lookup', 'Local development endpoint metadata for MOCK_VENDOR_PMS.SESSION_LOOKUP.', 'SESSION_LOOKUP', 'POST', '/v1/mock/sessions/resolve', 'MOCK_VENDOR_PMS.SESSION_LOOKUP', 'DEFAULT_TIMEOUT', 'DEFAULT_RETRY', 'SERVICE_TO_SERVICE_DEFAULT', 'ACTIVE', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM integration.vendor_systems vs
WHERE vs.vendor_code = 'MOCK_VENDOR_PMS' AND vs.environment_code = 'DEV'
ON CONFLICT ON CONSTRAINT uq_vendor_endpoints__vendor_endpoint_code DO UPDATE SET
  endpoint_name = EXCLUDED.endpoint_name, endpoint_description = EXCLUDED.endpoint_description, endpoint_type = EXCLUDED.endpoint_type,
  http_method = EXCLUDED.http_method, path_template = EXCLUDED.path_template, operation_ref = EXCLUDED.operation_ref,
  timeout_policy_code = EXCLUDED.timeout_policy_code, retry_policy_code = EXCLUDED.retry_policy_code, rate_limit_policy_code = EXCLUDED.rate_limit_policy_code,
  endpoint_status = EXCLUDED.endpoint_status, updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;
INSERT INTO integration.vendor_endpoints (vendor_endpoint_id, vendor_system_id, endpoint_code, endpoint_name, endpoint_description, endpoint_type, http_method, path_template, operation_ref, timeout_policy_code, retry_policy_code, rate_limit_policy_code, endpoint_status, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '4347dec0-f71e-525b-a06d-473d5992d730', vs.vendor_system_id, 'TARIFF_QUERY', 'Tariff Query', 'Local development endpoint metadata for MOCK_VENDOR_PMS.TARIFF_QUERY.', 'TARIFF_QUERY', 'POST', '/v1/mock/tariffs/calculate', 'MOCK_VENDOR_PMS.TARIFF_QUERY', 'DEFAULT_TIMEOUT', 'DEFAULT_RETRY', 'SERVICE_TO_SERVICE_DEFAULT', 'ACTIVE', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM integration.vendor_systems vs
WHERE vs.vendor_code = 'MOCK_VENDOR_PMS' AND vs.environment_code = 'DEV'
ON CONFLICT ON CONSTRAINT uq_vendor_endpoints__vendor_endpoint_code DO UPDATE SET
  endpoint_name = EXCLUDED.endpoint_name, endpoint_description = EXCLUDED.endpoint_description, endpoint_type = EXCLUDED.endpoint_type,
  http_method = EXCLUDED.http_method, path_template = EXCLUDED.path_template, operation_ref = EXCLUDED.operation_ref,
  timeout_policy_code = EXCLUDED.timeout_policy_code, retry_policy_code = EXCLUDED.retry_policy_code, rate_limit_policy_code = EXCLUDED.rate_limit_policy_code,
  endpoint_status = EXCLUDED.endpoint_status, updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;
INSERT INTO integration.vendor_endpoints (vendor_endpoint_id, vendor_system_id, endpoint_code, endpoint_name, endpoint_description, endpoint_type, http_method, path_template, operation_ref, timeout_policy_code, retry_policy_code, rate_limit_policy_code, endpoint_status, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'e61fd968-94e5-5fb4-bf5c-efe9df34e73d', vs.vendor_system_id, 'PAYMENT_CONFIRM', 'Payment Confirm', 'Local development endpoint metadata for MOCK_VENDOR_PMS.PAYMENT_CONFIRM.', 'PAYMENT_CREATE', 'POST', '/v1/mock/payments/confirm', 'MOCK_VENDOR_PMS.PAYMENT_CONFIRM', 'DEFAULT_TIMEOUT', 'DEFAULT_RETRY', 'SERVICE_TO_SERVICE_DEFAULT', 'ACTIVE', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM integration.vendor_systems vs
WHERE vs.vendor_code = 'MOCK_VENDOR_PMS' AND vs.environment_code = 'DEV'
ON CONFLICT ON CONSTRAINT uq_vendor_endpoints__vendor_endpoint_code DO UPDATE SET
  endpoint_name = EXCLUDED.endpoint_name, endpoint_description = EXCLUDED.endpoint_description, endpoint_type = EXCLUDED.endpoint_type,
  http_method = EXCLUDED.http_method, path_template = EXCLUDED.path_template, operation_ref = EXCLUDED.operation_ref,
  timeout_policy_code = EXCLUDED.timeout_policy_code, retry_policy_code = EXCLUDED.retry_policy_code, rate_limit_policy_code = EXCLUDED.rate_limit_policy_code,
  endpoint_status = EXCLUDED.endpoint_status, updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;
INSERT INTO integration.vendor_endpoints (vendor_endpoint_id, vendor_system_id, endpoint_code, endpoint_name, endpoint_description, endpoint_type, http_method, path_template, operation_ref, timeout_policy_code, retry_policy_code, rate_limit_policy_code, endpoint_status, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'b1c5fd7f-12be-5c0a-a406-bb08021e1931', vs.vendor_system_id, 'SESSION_LOOKUP', 'HikCentral Session Lookup', 'Local development endpoint metadata for HIKCENTRAL_DEV.SESSION_LOOKUP.', 'SESSION_LOOKUP', 'POST', '/artemis/api/pms/v1/sessions/search', 'HIKCENTRAL_DEV.SESSION_LOOKUP', 'DEFAULT_TIMEOUT', 'DEFAULT_RETRY', 'SERVICE_TO_SERVICE_DEFAULT', 'ACTIVE', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM integration.vendor_systems vs
WHERE vs.vendor_code = 'HIKCENTRAL_DEV' AND vs.environment_code = 'DEV'
ON CONFLICT ON CONSTRAINT uq_vendor_endpoints__vendor_endpoint_code DO UPDATE SET
  endpoint_name = EXCLUDED.endpoint_name, endpoint_description = EXCLUDED.endpoint_description, endpoint_type = EXCLUDED.endpoint_type,
  http_method = EXCLUDED.http_method, path_template = EXCLUDED.path_template, operation_ref = EXCLUDED.operation_ref,
  timeout_policy_code = EXCLUDED.timeout_policy_code, retry_policy_code = EXCLUDED.retry_policy_code, rate_limit_policy_code = EXCLUDED.rate_limit_policy_code,
  endpoint_status = EXCLUDED.endpoint_status, updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;
INSERT INTO integration.vendor_endpoints (vendor_endpoint_id, vendor_system_id, endpoint_code, endpoint_name, endpoint_description, endpoint_type, http_method, path_template, operation_ref, timeout_policy_code, retry_policy_code, rate_limit_policy_code, endpoint_status, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '1fa845e6-59a1-5bac-b66f-e398226ebf0f', vs.vendor_system_id, 'TARIFF_QUERY', 'HikCentral Tariff Query', 'Local development endpoint metadata for HIKCENTRAL_DEV.TARIFF_QUERY.', 'TARIFF_QUERY', 'POST', '/artemis/api/pms/v1/tariffs/calculate', 'HIKCENTRAL_DEV.TARIFF_QUERY', 'DEFAULT_TIMEOUT', 'DEFAULT_RETRY', 'SERVICE_TO_SERVICE_DEFAULT', 'ACTIVE', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM integration.vendor_systems vs
WHERE vs.vendor_code = 'HIKCENTRAL_DEV' AND vs.environment_code = 'DEV'
ON CONFLICT ON CONSTRAINT uq_vendor_endpoints__vendor_endpoint_code DO UPDATE SET
  endpoint_name = EXCLUDED.endpoint_name, endpoint_description = EXCLUDED.endpoint_description, endpoint_type = EXCLUDED.endpoint_type,
  http_method = EXCLUDED.http_method, path_template = EXCLUDED.path_template, operation_ref = EXCLUDED.operation_ref,
  timeout_policy_code = EXCLUDED.timeout_policy_code, retry_policy_code = EXCLUDED.retry_policy_code, rate_limit_policy_code = EXCLUDED.rate_limit_policy_code,
  endpoint_status = EXCLUDED.endpoint_status, updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;
INSERT INTO integration.vendor_endpoints (vendor_endpoint_id, vendor_system_id, endpoint_code, endpoint_name, endpoint_description, endpoint_type, http_method, path_template, operation_ref, timeout_policy_code, retry_policy_code, rate_limit_policy_code, endpoint_status, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'cfc3656e-700a-5621-b048-5e2fbfc41c15', vs.vendor_system_id, 'CHECKOUT_SESSION_CREATE', 'Create Checkout Session', 'Local development endpoint metadata for PAYMONGO_DEV.CHECKOUT_SESSION_CREATE.', 'PAYMENT_CREATE', 'POST', '/v1/checkout_sessions', 'PAYMONGO_DEV.CHECKOUT_SESSION_CREATE', 'DEFAULT_TIMEOUT', 'DEFAULT_RETRY', 'SERVICE_TO_SERVICE_DEFAULT', 'ACTIVE', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM integration.vendor_systems vs
WHERE vs.vendor_code = 'PAYMONGO_DEV' AND vs.environment_code = 'DEV'
ON CONFLICT ON CONSTRAINT uq_vendor_endpoints__vendor_endpoint_code DO UPDATE SET
  endpoint_name = EXCLUDED.endpoint_name, endpoint_description = EXCLUDED.endpoint_description, endpoint_type = EXCLUDED.endpoint_type,
  http_method = EXCLUDED.http_method, path_template = EXCLUDED.path_template, operation_ref = EXCLUDED.operation_ref,
  timeout_policy_code = EXCLUDED.timeout_policy_code, retry_policy_code = EXCLUDED.retry_policy_code, rate_limit_policy_code = EXCLUDED.rate_limit_policy_code,
  endpoint_status = EXCLUDED.endpoint_status, updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;
INSERT INTO integration.vendor_endpoints (vendor_endpoint_id, vendor_system_id, endpoint_code, endpoint_name, endpoint_description, endpoint_type, http_method, path_template, operation_ref, timeout_policy_code, retry_policy_code, rate_limit_policy_code, endpoint_status, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'bfc69097-7ca6-5ec9-a8b4-0c5e58000b06', vs.vendor_system_id, 'QRPH_CREATE', 'Create QRPh Payment', 'Local development endpoint metadata for PAYMONGO_DEV.QRPH_CREATE.', 'PAYMENT_CREATE', 'POST', '/v1/qrph', 'PAYMONGO_DEV.QRPH_CREATE', 'DEFAULT_TIMEOUT', 'DEFAULT_RETRY', 'SERVICE_TO_SERVICE_DEFAULT', 'ACTIVE', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM integration.vendor_systems vs
WHERE vs.vendor_code = 'PAYMONGO_DEV' AND vs.environment_code = 'DEV'
ON CONFLICT ON CONSTRAINT uq_vendor_endpoints__vendor_endpoint_code DO UPDATE SET
  endpoint_name = EXCLUDED.endpoint_name, endpoint_description = EXCLUDED.endpoint_description, endpoint_type = EXCLUDED.endpoint_type,
  http_method = EXCLUDED.http_method, path_template = EXCLUDED.path_template, operation_ref = EXCLUDED.operation_ref,
  timeout_policy_code = EXCLUDED.timeout_policy_code, retry_policy_code = EXCLUDED.retry_policy_code, rate_limit_policy_code = EXCLUDED.rate_limit_policy_code,
  endpoint_status = EXCLUDED.endpoint_status, updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;
INSERT INTO integration.vendor_endpoints (vendor_endpoint_id, vendor_system_id, endpoint_code, endpoint_name, endpoint_description, endpoint_type, http_method, path_template, operation_ref, timeout_policy_code, retry_policy_code, rate_limit_policy_code, endpoint_status, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '0a70df91-0015-57f1-9290-d509918f3624', vs.vendor_system_id, 'PAYMENT_STATUS', 'Payment Status', 'Local development endpoint metadata for PAYMONGO_DEV.PAYMENT_STATUS.', 'PAYMENT_STATUS', 'GET', '/v1/payments/{id}', 'PAYMONGO_DEV.PAYMENT_STATUS', 'DEFAULT_TIMEOUT', 'DEFAULT_RETRY', 'SERVICE_TO_SERVICE_DEFAULT', 'ACTIVE', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM integration.vendor_systems vs
WHERE vs.vendor_code = 'PAYMONGO_DEV' AND vs.environment_code = 'DEV'
ON CONFLICT ON CONSTRAINT uq_vendor_endpoints__vendor_endpoint_code DO UPDATE SET
  endpoint_name = EXCLUDED.endpoint_name, endpoint_description = EXCLUDED.endpoint_description, endpoint_type = EXCLUDED.endpoint_type,
  http_method = EXCLUDED.http_method, path_template = EXCLUDED.path_template, operation_ref = EXCLUDED.operation_ref,
  timeout_policy_code = EXCLUDED.timeout_policy_code, retry_policy_code = EXCLUDED.retry_policy_code, rate_limit_policy_code = EXCLUDED.rate_limit_policy_code,
  endpoint_status = EXCLUDED.endpoint_status, updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;
INSERT INTO integration.vendor_endpoints (vendor_endpoint_id, vendor_system_id, endpoint_code, endpoint_name, endpoint_description, endpoint_type, http_method, path_template, operation_ref, timeout_policy_code, retry_policy_code, rate_limit_policy_code, endpoint_status, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '0a520d31-71af-5478-87a0-5a3c66fbd521', vs.vendor_system_id, 'QRPH_CREATE', 'AUB QRPh Create', 'Local development endpoint metadata for AUB_DEV.QRPH_CREATE.', 'PAYMENT_CREATE', 'POST', '/v1/qrph/payments', 'AUB_DEV.QRPH_CREATE', 'DEFAULT_TIMEOUT', 'DEFAULT_RETRY', 'SERVICE_TO_SERVICE_DEFAULT', 'ACTIVE', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM integration.vendor_systems vs
WHERE vs.vendor_code = 'AUB_DEV' AND vs.environment_code = 'DEV'
ON CONFLICT ON CONSTRAINT uq_vendor_endpoints__vendor_endpoint_code DO UPDATE SET
  endpoint_name = EXCLUDED.endpoint_name, endpoint_description = EXCLUDED.endpoint_description, endpoint_type = EXCLUDED.endpoint_type,
  http_method = EXCLUDED.http_method, path_template = EXCLUDED.path_template, operation_ref = EXCLUDED.operation_ref,
  timeout_policy_code = EXCLUDED.timeout_policy_code, retry_policy_code = EXCLUDED.retry_policy_code, rate_limit_policy_code = EXCLUDED.rate_limit_policy_code,
  endpoint_status = EXCLUDED.endpoint_status, updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;
INSERT INTO integration.vendor_endpoints (vendor_endpoint_id, vendor_system_id, endpoint_code, endpoint_name, endpoint_description, endpoint_type, http_method, path_template, operation_ref, timeout_policy_code, retry_policy_code, rate_limit_policy_code, endpoint_status, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '5bfdcbaa-573a-5b15-9c4e-c181cb863edf', vs.vendor_system_id, 'PAYMENT_STATUS', 'AUB Payment Status', 'Local development endpoint metadata for AUB_DEV.PAYMENT_STATUS.', 'PAYMENT_STATUS', 'GET', '/v1/payments/{id}', 'AUB_DEV.PAYMENT_STATUS', 'DEFAULT_TIMEOUT', 'DEFAULT_RETRY', 'SERVICE_TO_SERVICE_DEFAULT', 'ACTIVE', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM integration.vendor_systems vs
WHERE vs.vendor_code = 'AUB_DEV' AND vs.environment_code = 'DEV'
ON CONFLICT ON CONSTRAINT uq_vendor_endpoints__vendor_endpoint_code DO UPDATE SET
  endpoint_name = EXCLUDED.endpoint_name, endpoint_description = EXCLUDED.endpoint_description, endpoint_type = EXCLUDED.endpoint_type,
  http_method = EXCLUDED.http_method, path_template = EXCLUDED.path_template, operation_ref = EXCLUDED.operation_ref,
  timeout_policy_code = EXCLUDED.timeout_policy_code, retry_policy_code = EXCLUDED.retry_policy_code, rate_limit_policy_code = EXCLUDED.rate_limit_policy_code,
  endpoint_status = EXCLUDED.endpoint_status, updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;
INSERT INTO integration.vendor_endpoints (vendor_endpoint_id, vendor_system_id, endpoint_code, endpoint_name, endpoint_description, endpoint_type, http_method, path_template, operation_ref, timeout_policy_code, retry_policy_code, rate_limit_policy_code, endpoint_status, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '7bdaa353-3c9c-5eea-ba7f-b8de0782fd3a', vs.vendor_system_id, 'GATE_OPEN', 'Mock Gate Open', 'Local development endpoint metadata for MOCK_GATE_CONTROLLER.GATE_OPEN.', 'GATE_COMMAND', 'POST', '/v1/mock/gates/open', 'MOCK_GATE_CONTROLLER.GATE_OPEN', 'DEFAULT_TIMEOUT', 'DEFAULT_RETRY', 'SERVICE_TO_SERVICE_DEFAULT', 'ACTIVE', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM integration.vendor_systems vs
WHERE vs.vendor_code = 'MOCK_GATE_CONTROLLER' AND vs.environment_code = 'DEV'
ON CONFLICT ON CONSTRAINT uq_vendor_endpoints__vendor_endpoint_code DO UPDATE SET
  endpoint_name = EXCLUDED.endpoint_name, endpoint_description = EXCLUDED.endpoint_description, endpoint_type = EXCLUDED.endpoint_type,
  http_method = EXCLUDED.http_method, path_template = EXCLUDED.path_template, operation_ref = EXCLUDED.operation_ref,
  timeout_policy_code = EXCLUDED.timeout_policy_code, retry_policy_code = EXCLUDED.retry_policy_code, rate_limit_policy_code = EXCLUDED.rate_limit_policy_code,
  endpoint_status = EXCLUDED.endpoint_status, updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;
INSERT INTO integration.vendor_endpoints (vendor_endpoint_id, vendor_system_id, endpoint_code, endpoint_name, endpoint_description, endpoint_type, http_method, path_template, operation_ref, timeout_policy_code, retry_policy_code, rate_limit_policy_code, endpoint_status, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'bbb5843e-cd12-573a-bb84-b942be0082fa', vs.vendor_system_id, 'HEALTH_CHECK', 'Mock Gate Health Check', 'Local development endpoint metadata for MOCK_GATE_CONTROLLER.HEALTH_CHECK.', 'HEALTH_CHECK', 'GET', '/health', 'MOCK_GATE_CONTROLLER.HEALTH_CHECK', 'DEFAULT_TIMEOUT', 'DEFAULT_RETRY', 'SERVICE_TO_SERVICE_DEFAULT', 'ACTIVE', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM integration.vendor_systems vs
WHERE vs.vendor_code = 'MOCK_GATE_CONTROLLER' AND vs.environment_code = 'DEV'
ON CONFLICT ON CONSTRAINT uq_vendor_endpoints__vendor_endpoint_code DO UPDATE SET
  endpoint_name = EXCLUDED.endpoint_name, endpoint_description = EXCLUDED.endpoint_description, endpoint_type = EXCLUDED.endpoint_type,
  http_method = EXCLUDED.http_method, path_template = EXCLUDED.path_template, operation_ref = EXCLUDED.operation_ref,
  timeout_policy_code = EXCLUDED.timeout_policy_code, retry_policy_code = EXCLUDED.retry_policy_code, rate_limit_policy_code = EXCLUDED.rate_limit_policy_code,
  endpoint_status = EXCLUDED.endpoint_status, updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;

-- 8. Payment rails
INSERT INTO payments.payment_rails (payment_rail_id, rail_code, rail_name, provider_code, rail_type, supported_currency_code, rail_status, is_primary, is_fallback, provider_profile_ref, configuration_ref, effective_from, created_by_service_identity_id, updated_by_service_identity_id) VALUES('c3a04815-588f-5f6f-9dfb-84e296bb2e10','PAYMONGO_QRPH_DEV','PayMongo QRPh Development','PAYMONGO','QRPH','PHP','ACTIVE',true,false,'paymongo-dev','payment-rail/paymongo/qrph/dev',now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('9111f61d-0d5c-58fe-a67a-13f181ed0164','PAYMONGO_CHECKOUT_DEV','PayMongo Hosted Checkout Development','PAYMONGO','HOSTED_CHECKOUT','PHP','ACTIVE',false,false,'paymongo-dev','payment-rail/paymongo/checkout/dev',now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('4128af61-d576-51b2-b087-c8f1cbb462ff','AUB_QRPH_DEV','AUB QRPh Development','AUB','QRPH','PHP','DRAFT',false,true,'aub-dev','payment-rail/aub/qrph/dev',now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('58f3e66a-21b9-5f97-bb59-32953a1f504b','MOCK_QRPH_DEV','Mock QRPh Development','MOCK','QRPH','PHP','ACTIVE',false,true,'mock-dev','payment-rail/mock/qrph/dev',now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978')
ON CONFLICT ON CONSTRAINT uq_payment_rails__rail_code DO UPDATE SET
  rail_name = EXCLUDED.rail_name, provider_code = EXCLUDED.provider_code, rail_type = EXCLUDED.rail_type,
  supported_currency_code = EXCLUDED.supported_currency_code, rail_status = EXCLUDED.rail_status,
  is_primary = EXCLUDED.is_primary, is_fallback = EXCLUDED.is_fallback, provider_profile_ref = EXCLUDED.provider_profile_ref,
  configuration_ref = EXCLUDED.configuration_ref, updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;

-- 9. Local-development site topology
INSERT INTO sites.site_groups (site_group_id, site_group_code, site_group_name, business_label, description, operator_entity_name, timezone_name, default_currency_code, site_group_status, public_lookup_enabled, default_payment_enabled, effective_from, created_by_service_identity_id, updated_by_service_identity_id) VALUES
('594afaf3-6f55-54be-933d-c6572f4e02ec','MNT','Mactan Newtown','Property','Local development site group for ExitPass v1.2','Pro Parking Group','Asia/Manila','PHP','ACTIVE',true,true,now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978')
ON CONFLICT ON CONSTRAINT uq_site_groups__site_group_code DO UPDATE SET
  site_group_name = EXCLUDED.site_group_name, business_label = EXCLUDED.business_label, description = EXCLUDED.description,
  operator_entity_name = EXCLUDED.operator_entity_name, timezone_name = EXCLUDED.timezone_name, default_currency_code = EXCLUDED.default_currency_code,
  site_group_status = EXCLUDED.site_group_status, public_lookup_enabled = EXCLUDED.public_lookup_enabled, default_payment_enabled = EXCLUDED.default_payment_enabled,
  updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;

INSERT INTO sites.sites (site_id, site_group_id, site_code, site_name, site_description, site_type, timezone_name, address_line1, city, province, country_code, lgu_code, site_status, public_lookup_enabled, payment_enabled, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '110a07ad-773f-5018-b18a-d4d78e2ae6dd', sg.site_group_id, 'MNT_OPEN_LOT_A', 'Mactan Newtown Open Lot A', 'Local development open lot A', 'OPEN_LOT', 'Asia/Manila', 'Mactan Newtown', 'Lapu-Lapu City', 'Cebu', 'PH', '072226', 'ACTIVE', true, true, now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM sites.site_groups sg WHERE sg.site_group_code = 'MNT'
ON CONFLICT ON CONSTRAINT uq_sites__site_group_site_code DO UPDATE SET
  site_name = EXCLUDED.site_name, site_description = EXCLUDED.site_description, site_type = EXCLUDED.site_type,
  timezone_name = EXCLUDED.timezone_name, address_line1 = EXCLUDED.address_line1, city = EXCLUDED.city, province = EXCLUDED.province,
  country_code = EXCLUDED.country_code, lgu_code = EXCLUDED.lgu_code, site_status = EXCLUDED.site_status,
  public_lookup_enabled = EXCLUDED.public_lookup_enabled, payment_enabled = EXCLUDED.payment_enabled,
  updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;
INSERT INTO sites.sites (site_id, site_group_id, site_code, site_name, site_description, site_type, timezone_name, address_line1, city, province, country_code, lgu_code, site_status, public_lookup_enabled, payment_enabled, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'db5f423b-ed17-59d4-a5be-e7440aca5b21', sg.site_group_id, 'MNT_STRUCTURED_B', 'Mactan Newtown Structured Parking B', 'Local development structured parking B', 'STRUCTURED_PARKING', 'Asia/Manila', 'Mactan Newtown', 'Lapu-Lapu City', 'Cebu', 'PH', '072226', 'ACTIVE', true, true, now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM sites.site_groups sg WHERE sg.site_group_code = 'MNT'
ON CONFLICT ON CONSTRAINT uq_sites__site_group_site_code DO UPDATE SET
  site_name = EXCLUDED.site_name, site_description = EXCLUDED.site_description, site_type = EXCLUDED.site_type,
  timezone_name = EXCLUDED.timezone_name, address_line1 = EXCLUDED.address_line1, city = EXCLUDED.city, province = EXCLUDED.province,
  country_code = EXCLUDED.country_code, lgu_code = EXCLUDED.lgu_code, site_status = EXCLUDED.site_status,
  public_lookup_enabled = EXCLUDED.public_lookup_enabled, payment_enabled = EXCLUDED.payment_enabled,
  updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;

INSERT INTO sites.lanes (lane_id, site_id, lane_code, lane_name, lane_description, lane_type, lane_direction, lane_status, display_order, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'b416bbbc-14ea-5be5-8b9b-b954b3a65112', s.site_id, 'ENTRY_01', 'Entry Lane 01', 'Local development lane for MNT_OPEN_LOT_A.', 'ENTRY', 'INBOUND', 'ACTIVE', 1, now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM sites.sites s JOIN sites.site_groups sg ON sg.site_group_id = s.site_group_id
WHERE sg.site_group_code = 'MNT' AND s.site_code = 'MNT_OPEN_LOT_A'
ON CONFLICT ON CONSTRAINT uq_lanes__site_lane_code DO UPDATE SET
  lane_name = EXCLUDED.lane_name, lane_description = EXCLUDED.lane_description, lane_type = EXCLUDED.lane_type, lane_direction = EXCLUDED.lane_direction,
  lane_status = EXCLUDED.lane_status, display_order = EXCLUDED.display_order, updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;
INSERT INTO sites.lanes (lane_id, site_id, lane_code, lane_name, lane_description, lane_type, lane_direction, lane_status, display_order, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'fdcb790e-9ca7-5dbe-9b85-437cbad33d22', s.site_id, 'EXIT_01', 'Exit Lane 01', 'Local development lane for MNT_OPEN_LOT_A.', 'EXIT', 'OUTBOUND', 'ACTIVE', 2, now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM sites.sites s JOIN sites.site_groups sg ON sg.site_group_id = s.site_group_id
WHERE sg.site_group_code = 'MNT' AND s.site_code = 'MNT_OPEN_LOT_A'
ON CONFLICT ON CONSTRAINT uq_lanes__site_lane_code DO UPDATE SET
  lane_name = EXCLUDED.lane_name, lane_description = EXCLUDED.lane_description, lane_type = EXCLUDED.lane_type, lane_direction = EXCLUDED.lane_direction,
  lane_status = EXCLUDED.lane_status, display_order = EXCLUDED.display_order, updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;
INSERT INTO sites.lanes (lane_id, site_id, lane_code, lane_name, lane_description, lane_type, lane_direction, lane_status, display_order, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '55fa1190-4dce-561e-b5c3-97d63a6439b3', s.site_id, 'EXIT_02', 'Exit Lane 02', 'Local development lane for MNT_OPEN_LOT_A.', 'EXIT', 'OUTBOUND', 'ACTIVE', 3, now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM sites.sites s JOIN sites.site_groups sg ON sg.site_group_id = s.site_group_id
WHERE sg.site_group_code = 'MNT' AND s.site_code = 'MNT_OPEN_LOT_A'
ON CONFLICT ON CONSTRAINT uq_lanes__site_lane_code DO UPDATE SET
  lane_name = EXCLUDED.lane_name, lane_description = EXCLUDED.lane_description, lane_type = EXCLUDED.lane_type, lane_direction = EXCLUDED.lane_direction,
  lane_status = EXCLUDED.lane_status, display_order = EXCLUDED.display_order, updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;
INSERT INTO sites.lanes (lane_id, site_id, lane_code, lane_name, lane_description, lane_type, lane_direction, lane_status, display_order, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'f90f5d5f-a25e-502a-9dfa-c532391d2c80', s.site_id, 'ENTRY_01', 'Entry Lane 01', 'Local development lane for MNT_STRUCTURED_B.', 'ENTRY', 'INBOUND', 'ACTIVE', 1, now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM sites.sites s JOIN sites.site_groups sg ON sg.site_group_id = s.site_group_id
WHERE sg.site_group_code = 'MNT' AND s.site_code = 'MNT_STRUCTURED_B'
ON CONFLICT ON CONSTRAINT uq_lanes__site_lane_code DO UPDATE SET
  lane_name = EXCLUDED.lane_name, lane_description = EXCLUDED.lane_description, lane_type = EXCLUDED.lane_type, lane_direction = EXCLUDED.lane_direction,
  lane_status = EXCLUDED.lane_status, display_order = EXCLUDED.display_order, updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;
INSERT INTO sites.lanes (lane_id, site_id, lane_code, lane_name, lane_description, lane_type, lane_direction, lane_status, display_order, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'c23eab9e-089f-5564-ac6c-359f8308afd4', s.site_id, 'EXIT_01', 'Exit Lane 01', 'Local development lane for MNT_STRUCTURED_B.', 'EXIT', 'OUTBOUND', 'ACTIVE', 2, now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM sites.sites s JOIN sites.site_groups sg ON sg.site_group_id = s.site_group_id
WHERE sg.site_group_code = 'MNT' AND s.site_code = 'MNT_STRUCTURED_B'
ON CONFLICT ON CONSTRAINT uq_lanes__site_lane_code DO UPDATE SET
  lane_name = EXCLUDED.lane_name, lane_description = EXCLUDED.lane_description, lane_type = EXCLUDED.lane_type, lane_direction = EXCLUDED.lane_direction,
  lane_status = EXCLUDED.lane_status, display_order = EXCLUDED.display_order, updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;

-- 10. Gate devices linked to sites.lanes and identity.service_identities
INSERT INTO gates.gate_devices (gate_device_id, site_id, lane_id, service_identity_id, device_code, device_name, device_type, vendor_device_ref, serial_number, device_status, installed_at, activated_at, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '348580f1-7770-5909-8870-71156452e262', s.site_id, l.lane_id, si.service_identity_id, 'MNT-EXIT-GATE-01', 'Mactan Newtown Exit Gate 01', 'BARRIER_CONTROLLER', 'MOCK-GATE-001', 'SN-MNT-EXIT-001', 'ACTIVE', now(), now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM sites.site_groups sg
JOIN sites.sites s ON s.site_group_id = sg.site_group_id
JOIN sites.lanes l ON l.site_id = s.site_id AND l.lane_code = 'EXIT_01'
JOIN identity.service_identities si ON si.service_identity_code = 'gate-device-mnt-exit-01'
WHERE sg.site_group_code = 'MNT' AND s.site_code = 'MNT_OPEN_LOT_A'
ON CONFLICT ON CONSTRAINT uq_gate_devices__site_device_code DO UPDATE SET
  lane_id = EXCLUDED.lane_id, service_identity_id = EXCLUDED.service_identity_id, device_name = EXCLUDED.device_name,
  device_type = EXCLUDED.device_type, vendor_device_ref = EXCLUDED.vendor_device_ref, serial_number = EXCLUDED.serial_number,
  device_status = EXCLUDED.device_status, activated_at = EXCLUDED.activated_at, updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;
INSERT INTO gates.gate_devices (gate_device_id, site_id, lane_id, service_identity_id, device_code, device_name, device_type, vendor_device_ref, serial_number, device_status, installed_at, activated_at, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '60ec952c-4f2d-598a-b9c0-8069cc9b065c', s.site_id, l.lane_id, si.service_identity_id, 'MNT-EXIT-GATE-02', 'Mactan Newtown Exit Gate 02', 'BARRIER_CONTROLLER', 'MOCK-GATE-002', 'SN-MNT-EXIT-002', 'ACTIVE', now(), now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM sites.site_groups sg
JOIN sites.sites s ON s.site_group_id = sg.site_group_id
JOIN sites.lanes l ON l.site_id = s.site_id AND l.lane_code = 'EXIT_02'
JOIN identity.service_identities si ON si.service_identity_code = 'gate-device-mnt-exit-02'
WHERE sg.site_group_code = 'MNT' AND s.site_code = 'MNT_OPEN_LOT_A'
ON CONFLICT ON CONSTRAINT uq_gate_devices__site_device_code DO UPDATE SET
  lane_id = EXCLUDED.lane_id, service_identity_id = EXCLUDED.service_identity_id, device_name = EXCLUDED.device_name,
  device_type = EXCLUDED.device_type, vendor_device_ref = EXCLUDED.vendor_device_ref, serial_number = EXCLUDED.serial_number,
  device_status = EXCLUDED.device_status, activated_at = EXCLUDED.activated_at, updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;

-- 11. Statutory discount policy references. These are local-development placeholders, not production legal advice.
INSERT INTO discounts.discount_policy_references (discount_policy_reference_id, policy_code, policy_name, policy_description, policy_type, policy_level, entitlement_type, national_law_reference, local_ordinance_reference, lgu_code, jurisdiction_name, precedence_rank, policy_version, requires_operator_validation, requires_evidence_capture, evidence_retention_policy_code, policy_status, effective_from, created_by_service_identity_id, updated_by_service_identity_id) VALUES
('e614cb4d-fbc7-54dd-80e5-9cf69bd798a4','PH_NATIONAL_SENIOR_DEV','Philippines National Senior Citizen Discount Placeholder','Local development placeholder only. Replace with approved production legal policy references before production.','LEGAL_REFERENCE','NATIONAL_LAW','SENIOR_CITIZEN','DEV_PLACEHOLDER_SENIOR_NATIONAL',NULL,NULL,'Philippines',100,'v1-dev',true,true,'EVIDENCE_RETENTION_DEV_30D','ACTIVE',now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978')
ON CONFLICT ON CONSTRAINT uq_discount_policy_references__policy_code_version DO UPDATE SET
  policy_name = EXCLUDED.policy_name, policy_description = EXCLUDED.policy_description, policy_type = EXCLUDED.policy_type, policy_level = EXCLUDED.policy_level,
  entitlement_type = EXCLUDED.entitlement_type, national_law_reference = EXCLUDED.national_law_reference, local_ordinance_reference = EXCLUDED.local_ordinance_reference,
  lgu_code = EXCLUDED.lgu_code, jurisdiction_name = EXCLUDED.jurisdiction_name, precedence_rank = EXCLUDED.precedence_rank,
  requires_operator_validation = EXCLUDED.requires_operator_validation, requires_evidence_capture = EXCLUDED.requires_evidence_capture,
  evidence_retention_policy_code = EXCLUDED.evidence_retention_policy_code, policy_status = EXCLUDED.policy_status,
  updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;
INSERT INTO discounts.discount_policy_references (discount_policy_reference_id, policy_code, policy_name, policy_description, policy_type, policy_level, entitlement_type, national_law_reference, local_ordinance_reference, lgu_code, jurisdiction_name, precedence_rank, policy_version, requires_operator_validation, requires_evidence_capture, evidence_retention_policy_code, policy_status, effective_from, created_by_service_identity_id, updated_by_service_identity_id) VALUES
('933fc631-e4c6-5351-8c70-83c585eb97dc','PH_NATIONAL_PWD_DEV','Philippines National PWD Discount Placeholder','Local development placeholder only. Replace with approved production legal policy references before production.','LEGAL_REFERENCE','NATIONAL_LAW','PWD','DEV_PLACEHOLDER_PWD_NATIONAL',NULL,NULL,'Philippines',100,'v1-dev',true,true,'EVIDENCE_RETENTION_DEV_30D','ACTIVE',now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978')
ON CONFLICT ON CONSTRAINT uq_discount_policy_references__policy_code_version DO UPDATE SET
  policy_name = EXCLUDED.policy_name, policy_description = EXCLUDED.policy_description, policy_type = EXCLUDED.policy_type, policy_level = EXCLUDED.policy_level,
  entitlement_type = EXCLUDED.entitlement_type, national_law_reference = EXCLUDED.national_law_reference, local_ordinance_reference = EXCLUDED.local_ordinance_reference,
  lgu_code = EXCLUDED.lgu_code, jurisdiction_name = EXCLUDED.jurisdiction_name, precedence_rank = EXCLUDED.precedence_rank,
  requires_operator_validation = EXCLUDED.requires_operator_validation, requires_evidence_capture = EXCLUDED.requires_evidence_capture,
  evidence_retention_policy_code = EXCLUDED.evidence_retention_policy_code, policy_status = EXCLUDED.policy_status,
  updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;
INSERT INTO discounts.discount_policy_references (discount_policy_reference_id, policy_code, policy_name, policy_description, policy_type, policy_level, entitlement_type, national_law_reference, local_ordinance_reference, lgu_code, jurisdiction_name, site_group_id, fallback_policy_reference_id, precedence_rank, policy_version, requires_operator_validation, requires_evidence_capture, evidence_retention_policy_code, policy_status, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'd43a76b6-d470-5565-a891-236f15946421', 'MNT_LOCAL_SENIOR_DEV', 'Mactan Newtown Local Senior Discount Placeholder', 'Local development placeholder only. Replace with approved production legal policy references before production.', 'LOCAL_ORDINANCE', 'LOCAL_ORDINANCE', 'SENIOR_CITIZEN', NULL, 'DEV_PLACEHOLDER_LOCAL_SENIOR', '072226', 'Lapu-Lapu City', sg.site_group_id, fp.discount_policy_reference_id, 10, 'v1-dev', true, true, 'EVIDENCE_RETENTION_DEV_30D', 'ACTIVE', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM sites.site_groups sg
JOIN discounts.discount_policy_references fp ON fp.policy_code = 'PH_NATIONAL_SENIOR_DEV' AND fp.policy_version = 'v1-dev'
WHERE sg.site_group_code = 'MNT'
ON CONFLICT ON CONSTRAINT uq_discount_policy_references__policy_code_version DO UPDATE SET
  policy_name = EXCLUDED.policy_name, policy_description = EXCLUDED.policy_description, policy_type = EXCLUDED.policy_type, policy_level = EXCLUDED.policy_level,
  entitlement_type = EXCLUDED.entitlement_type, local_ordinance_reference = EXCLUDED.local_ordinance_reference, lgu_code = EXCLUDED.lgu_code,
  jurisdiction_name = EXCLUDED.jurisdiction_name, site_group_id = EXCLUDED.site_group_id, fallback_policy_reference_id = EXCLUDED.fallback_policy_reference_id,
  precedence_rank = EXCLUDED.precedence_rank, requires_operator_validation = EXCLUDED.requires_operator_validation, requires_evidence_capture = EXCLUDED.requires_evidence_capture,
  evidence_retention_policy_code = EXCLUDED.evidence_retention_policy_code, policy_status = EXCLUDED.policy_status,
  updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;
INSERT INTO discounts.discount_policy_references (discount_policy_reference_id, policy_code, policy_name, policy_description, policy_type, policy_level, entitlement_type, national_law_reference, local_ordinance_reference, lgu_code, jurisdiction_name, site_group_id, fallback_policy_reference_id, precedence_rank, policy_version, requires_operator_validation, requires_evidence_capture, evidence_retention_policy_code, policy_status, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '1ac6a912-df91-5f20-b544-68b1b1716ea4', 'MNT_LOCAL_PWD_DEV', 'Mactan Newtown Local PWD Discount Placeholder', 'Local development placeholder only. Replace with approved production legal policy references before production.', 'LOCAL_ORDINANCE', 'LOCAL_ORDINANCE', 'PWD', NULL, 'DEV_PLACEHOLDER_LOCAL_PWD', '072226', 'Lapu-Lapu City', sg.site_group_id, fp.discount_policy_reference_id, 10, 'v1-dev', true, true, 'EVIDENCE_RETENTION_DEV_30D', 'ACTIVE', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM sites.site_groups sg
JOIN discounts.discount_policy_references fp ON fp.policy_code = 'PH_NATIONAL_PWD_DEV' AND fp.policy_version = 'v1-dev'
WHERE sg.site_group_code = 'MNT'
ON CONFLICT ON CONSTRAINT uq_discount_policy_references__policy_code_version DO UPDATE SET
  policy_name = EXCLUDED.policy_name, policy_description = EXCLUDED.policy_description, policy_type = EXCLUDED.policy_type, policy_level = EXCLUDED.policy_level,
  entitlement_type = EXCLUDED.entitlement_type, local_ordinance_reference = EXCLUDED.local_ordinance_reference, lgu_code = EXCLUDED.lgu_code,
  jurisdiction_name = EXCLUDED.jurisdiction_name, site_group_id = EXCLUDED.site_group_id, fallback_policy_reference_id = EXCLUDED.fallback_policy_reference_id,
  precedence_rank = EXCLUDED.precedence_rank, requires_operator_validation = EXCLUDED.requires_operator_validation, requires_evidence_capture = EXCLUDED.requires_evidence_capture,
  evidence_retention_policy_code = EXCLUDED.evidence_retention_policy_code, policy_status = EXCLUDED.policy_status,
  updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;

-- 12. Merchant and wallet reference data for coupon testing
INSERT INTO merchants.merchants (merchant_id, merchant_code, merchant_name, merchant_display_name, merchant_type, merchant_status, contact_email, contact_mobile_masked, default_currency_code, effective_from, created_by_service_identity_id, updated_by_service_identity_id) VALUES('e34f9bea-2504-5974-81ea-7649b28fab50','MNT_TENANT_COFFEE','Mactan Newtown Coffee Tenant','Coffee Tenant','TENANT','ACTIVE','merchant-test@example.local','+63*******0001','PHP',now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'),
('ad853f58-43aa-5776-b1b4-40bb13c18462','MNT_PROPERTY_OPERATOR','Mactan Newtown Property Operator','Property Operator','PROPERTY_OPERATOR','ACTIVE','operator@example.local','+63*******0002','PHP',now(),'1f2ffdfb-c4a9-5a00-a656-9f3a132b1978','1f2ffdfb-c4a9-5a00-a656-9f3a132b1978')
ON CONFLICT ON CONSTRAINT uq_merchants__merchant_code DO UPDATE SET
  merchant_name = EXCLUDED.merchant_name, merchant_display_name = EXCLUDED.merchant_display_name, merchant_type = EXCLUDED.merchant_type,
  merchant_status = EXCLUDED.merchant_status, contact_email = EXCLUDED.contact_email, contact_mobile_masked = EXCLUDED.contact_mobile_masked,
  default_currency_code = EXCLUDED.default_currency_code, updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;

INSERT INTO merchants.merchant_site_scopes (merchant_site_scope_id, merchant_id, site_group_id, scope_type, scope_status, scope_reason_code, allows_coupon_sponsorship, allows_full_waiver, requires_elevated_approval, effective_from, approved_at, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'f484e84c-1895-5af2-ab80-150803a01043', m.merchant_id, sg.site_group_id, 'SITE_GROUP', 'ACTIVE', 'BASELINE_REFERENCE_DATA', true, false, false, now(), now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM merchants.merchants m
JOIN sites.site_groups sg ON sg.site_group_code = 'MNT'
WHERE m.merchant_code = 'MNT_TENANT_COFFEE'
  AND NOT EXISTS (SELECT 1 FROM merchants.merchant_site_scopes ms WHERE ms.merchant_id = m.merchant_id AND ms.site_group_id = sg.site_group_id AND ms.scope_type = 'SITE_GROUP' AND ms.scope_status = 'ACTIVE');
INSERT INTO merchants.merchant_site_scopes (merchant_site_scope_id, merchant_id, site_group_id, scope_type, scope_status, scope_reason_code, allows_coupon_sponsorship, allows_full_waiver, requires_elevated_approval, effective_from, approved_at, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '58506540-94ee-5d4b-aed2-223ded21f856', m.merchant_id, sg.site_group_id, 'SITE_GROUP', 'ACTIVE', 'BASELINE_REFERENCE_DATA', true, true, true, now(), now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM merchants.merchants m
JOIN sites.site_groups sg ON sg.site_group_code = 'MNT'
WHERE m.merchant_code = 'MNT_PROPERTY_OPERATOR'
  AND NOT EXISTS (SELECT 1 FROM merchants.merchant_site_scopes ms WHERE ms.merchant_id = m.merchant_id AND ms.site_group_id = sg.site_group_id AND ms.scope_type = 'SITE_GROUP' AND ms.scope_status = 'ACTIVE');

INSERT INTO merchants.merchant_wallets (merchant_wallet_id, merchant_id, wallet_code, wallet_name, wallet_type, wallet_status, currency_code, available_balance, reserved_balance, committed_balance, allows_coupon_funding, allows_statutory_discount_funding, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '70450020-35df-57f3-9d3a-39e017277ca8', m.merchant_id, 'COUPON_WALLET', 'Coupon Wallet', 'PRE_FUNDED', 'ACTIVE', 'PHP', 10000, 0, 0, true, false, now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM merchants.merchants m WHERE m.merchant_code = 'MNT_TENANT_COFFEE'
ON CONFLICT ON CONSTRAINT uq_merchant_wallets__merchant_wallet_code DO UPDATE SET
  wallet_name = EXCLUDED.wallet_name, wallet_type = EXCLUDED.wallet_type, wallet_status = EXCLUDED.wallet_status, currency_code = EXCLUDED.currency_code,
  available_balance = EXCLUDED.available_balance, reserved_balance = EXCLUDED.reserved_balance, committed_balance = EXCLUDED.committed_balance,
  allows_coupon_funding = EXCLUDED.allows_coupon_funding, allows_statutory_discount_funding = EXCLUDED.allows_statutory_discount_funding,
  updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;
INSERT INTO merchants.merchant_wallets (merchant_wallet_id, merchant_id, wallet_code, wallet_name, wallet_type, wallet_status, currency_code, available_balance, reserved_balance, committed_balance, allows_coupon_funding, allows_statutory_discount_funding, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '50672762-de61-522b-9efd-ea8459027017', m.merchant_id, 'SERVICE_RECOVERY_WALLET', 'Service Recovery Wallet', 'PROMOTIONAL_BUDGET', 'ACTIVE', 'PHP', 50000, 0, 0, true, false, now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM merchants.merchants m WHERE m.merchant_code = 'MNT_PROPERTY_OPERATOR'
ON CONFLICT ON CONSTRAINT uq_merchant_wallets__merchant_wallet_code DO UPDATE SET
  wallet_name = EXCLUDED.wallet_name, wallet_type = EXCLUDED.wallet_type, wallet_status = EXCLUDED.wallet_status, currency_code = EXCLUDED.currency_code,
  available_balance = EXCLUDED.available_balance, reserved_balance = EXCLUDED.reserved_balance, committed_balance = EXCLUDED.committed_balance,
  allows_coupon_funding = EXCLUDED.allows_coupon_funding, allows_statutory_discount_funding = EXCLUDED.allows_statutory_discount_funding,
  updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;

-- 13. Coupon definition reference data for local coupon-flow testing
INSERT INTO coupons.coupons (coupon_id, merchant_id, coupon_code, coupon_name, coupon_description, coupon_type, denomination_type, denomination_value, currency_code, maximum_discount_amount, minimum_gross_amount, stacking_policy, allows_full_waiver, requires_elevated_approval, coupon_status, valid_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'e1592d7a-f57c-58b0-a401-bdefd78cf155', m.merchant_id, 'DEV20', 'Development PHP 20 Coupon', 'Local development coupon for ExitPass v1.2.', 'STANDARD', 'FIXED_AMOUNT', 20, 'PHP', 20, 0, 'STACK_WITH_STATUTORY_DISCOUNT', false, false, 'ACTIVE', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM merchants.merchants m WHERE m.merchant_code = 'MNT_TENANT_COFFEE'
ON CONFLICT ON CONSTRAINT uq_coupons__merchant_coupon_code DO UPDATE SET
  coupon_name = EXCLUDED.coupon_name, coupon_description = EXCLUDED.coupon_description, coupon_type = EXCLUDED.coupon_type,
  denomination_type = EXCLUDED.denomination_type, denomination_value = EXCLUDED.denomination_value, currency_code = EXCLUDED.currency_code,
  maximum_discount_amount = EXCLUDED.maximum_discount_amount, minimum_gross_amount = EXCLUDED.minimum_gross_amount, stacking_policy = EXCLUDED.stacking_policy,
  allows_full_waiver = EXCLUDED.allows_full_waiver, requires_elevated_approval = EXCLUDED.requires_elevated_approval, coupon_status = EXCLUDED.coupon_status,
  updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;
INSERT INTO coupons.coupon_rule_groups (coupon_rule_group_id, coupon_id, rule_group_code, rule_group_name, rule_group_description, evaluation_strategy, evaluation_priority, is_required, rule_group_status, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '1b3a78e3-23f2-5511-ad88-a1785fdc9cd8', c.coupon_id, 'BASELINE_SCOPE', 'Baseline Scope Rules', 'Local development baseline coupon rules.', 'ALL_RULES_MUST_PASS', 1, true, 'ACTIVE', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM coupons.coupons c
JOIN merchants.merchants m ON m.merchant_id = c.merchant_id
WHERE m.merchant_code = 'MNT_TENANT_COFFEE' AND c.coupon_code = 'DEV20'
ON CONFLICT ON CONSTRAINT uq_coupon_rule_groups__coupon_rule_group_code DO UPDATE SET
  rule_group_name = EXCLUDED.rule_group_name, rule_group_description = EXCLUDED.rule_group_description, evaluation_strategy = EXCLUDED.evaluation_strategy,
  evaluation_priority = EXCLUDED.evaluation_priority, is_required = EXCLUDED.is_required, rule_group_status = EXCLUDED.rule_group_status,
  updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;
INSERT INTO coupons.coupon_rules (coupon_rule_id, coupon_rule_group_id, rule_code, rule_name, rule_type, rule_operator, rule_value_text, site_group_id, merchant_id, rule_status, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '812a7287-bb32-573a-bf13-4b5954520e0c', crg.coupon_rule_group_id, 'SITE_GROUP_SCOPE', 'Site Group Scope', 'SITE_GROUP_SCOPE', 'EQUALS', 'MNT', sg.site_group_id, m.merchant_id, 'ACTIVE', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM coupons.coupon_rule_groups crg
JOIN coupons.coupons c ON c.coupon_id = crg.coupon_id
JOIN merchants.merchants m ON m.merchant_id = c.merchant_id
JOIN sites.site_groups sg ON sg.site_group_code = 'MNT'
WHERE m.merchant_code = 'MNT_TENANT_COFFEE' AND c.coupon_code = 'DEV20' AND crg.rule_group_code = 'BASELINE_SCOPE'
ON CONFLICT ON CONSTRAINT uq_coupon_rules__group_rule_code DO UPDATE SET
  rule_name = EXCLUDED.rule_name, rule_type = EXCLUDED.rule_type, rule_operator = EXCLUDED.rule_operator, rule_value_text = EXCLUDED.rule_value_text,
  site_group_id = EXCLUDED.site_group_id, merchant_id = EXCLUDED.merchant_id, rule_status = EXCLUDED.rule_status,
  updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;
INSERT INTO coupons.coupon_rules (coupon_rule_id, coupon_rule_group_id, rule_code, rule_name, rule_type, rule_operator, rule_value_boolean, merchant_id, rule_status, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '724264b9-d95e-57a2-8efe-65d60b0899d0', crg.coupon_rule_group_id, 'WALLET_SUFFICIENCY', 'Wallet Sufficiency', 'WALLET_SUFFICIENCY', 'EQUALS', true, m.merchant_id, 'ACTIVE', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM coupons.coupon_rule_groups crg
JOIN coupons.coupons c ON c.coupon_id = crg.coupon_id
JOIN merchants.merchants m ON m.merchant_id = c.merchant_id
WHERE m.merchant_code = 'MNT_TENANT_COFFEE' AND c.coupon_code = 'DEV20' AND crg.rule_group_code = 'BASELINE_SCOPE'
ON CONFLICT ON CONSTRAINT uq_coupon_rules__group_rule_code DO UPDATE SET
  rule_name = EXCLUDED.rule_name, rule_type = EXCLUDED.rule_type, rule_operator = EXCLUDED.rule_operator, rule_value_boolean = EXCLUDED.rule_value_boolean,
  merchant_id = EXCLUDED.merchant_id, rule_status = EXCLUDED.rule_status, updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;
INSERT INTO coupons.coupons (coupon_id, merchant_id, coupon_code, coupon_name, coupon_description, coupon_type, denomination_type, denomination_value, currency_code, maximum_discount_amount, minimum_gross_amount, stacking_policy, allows_full_waiver, requires_elevated_approval, coupon_status, valid_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '11c574e2-47f0-5bda-8b20-51d87728d15f', m.merchant_id, 'SERVICE100', 'Development Service Recovery Full Waiver', 'Local development coupon for ExitPass v1.2.', 'SERVICE_RECOVERY', 'FULL_WAIVER', 100, 'PHP', NULL, 0, 'NO_STACKING', true, true, 'DRAFT', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM merchants.merchants m WHERE m.merchant_code = 'MNT_PROPERTY_OPERATOR'
ON CONFLICT ON CONSTRAINT uq_coupons__merchant_coupon_code DO UPDATE SET
  coupon_name = EXCLUDED.coupon_name, coupon_description = EXCLUDED.coupon_description, coupon_type = EXCLUDED.coupon_type,
  denomination_type = EXCLUDED.denomination_type, denomination_value = EXCLUDED.denomination_value, currency_code = EXCLUDED.currency_code,
  maximum_discount_amount = EXCLUDED.maximum_discount_amount, minimum_gross_amount = EXCLUDED.minimum_gross_amount, stacking_policy = EXCLUDED.stacking_policy,
  allows_full_waiver = EXCLUDED.allows_full_waiver, requires_elevated_approval = EXCLUDED.requires_elevated_approval, coupon_status = EXCLUDED.coupon_status,
  updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;
INSERT INTO coupons.coupon_rule_groups (coupon_rule_group_id, coupon_id, rule_group_code, rule_group_name, rule_group_description, evaluation_strategy, evaluation_priority, is_required, rule_group_status, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'd063d659-2fa2-5126-8bc0-988424e6a7ed', c.coupon_id, 'BASELINE_SCOPE', 'Baseline Scope Rules', 'Local development baseline coupon rules.', 'ALL_RULES_MUST_PASS', 1, true, 'ACTIVE', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM coupons.coupons c
JOIN merchants.merchants m ON m.merchant_id = c.merchant_id
WHERE m.merchant_code = 'MNT_PROPERTY_OPERATOR' AND c.coupon_code = 'SERVICE100'
ON CONFLICT ON CONSTRAINT uq_coupon_rule_groups__coupon_rule_group_code DO UPDATE SET
  rule_group_name = EXCLUDED.rule_group_name, rule_group_description = EXCLUDED.rule_group_description, evaluation_strategy = EXCLUDED.evaluation_strategy,
  evaluation_priority = EXCLUDED.evaluation_priority, is_required = EXCLUDED.is_required, rule_group_status = EXCLUDED.rule_group_status,
  updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;
INSERT INTO coupons.coupon_rules (coupon_rule_id, coupon_rule_group_id, rule_code, rule_name, rule_type, rule_operator, rule_value_text, site_group_id, merchant_id, rule_status, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT '845db054-2f2c-576e-aea3-424830b493fb', crg.coupon_rule_group_id, 'SITE_GROUP_SCOPE', 'Site Group Scope', 'SITE_GROUP_SCOPE', 'EQUALS', 'MNT', sg.site_group_id, m.merchant_id, 'ACTIVE', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM coupons.coupon_rule_groups crg
JOIN coupons.coupons c ON c.coupon_id = crg.coupon_id
JOIN merchants.merchants m ON m.merchant_id = c.merchant_id
JOIN sites.site_groups sg ON sg.site_group_code = 'MNT'
WHERE m.merchant_code = 'MNT_PROPERTY_OPERATOR' AND c.coupon_code = 'SERVICE100' AND crg.rule_group_code = 'BASELINE_SCOPE'
ON CONFLICT ON CONSTRAINT uq_coupon_rules__group_rule_code DO UPDATE SET
  rule_name = EXCLUDED.rule_name, rule_type = EXCLUDED.rule_type, rule_operator = EXCLUDED.rule_operator, rule_value_text = EXCLUDED.rule_value_text,
  site_group_id = EXCLUDED.site_group_id, merchant_id = EXCLUDED.merchant_id, rule_status = EXCLUDED.rule_status,
  updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;
INSERT INTO coupons.coupon_rules (coupon_rule_id, coupon_rule_group_id, rule_code, rule_name, rule_type, rule_operator, rule_value_boolean, merchant_id, rule_status, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
SELECT 'adedcb83-0163-5778-afaa-e58d56e75aaa', crg.coupon_rule_group_id, 'WALLET_SUFFICIENCY', 'Wallet Sufficiency', 'WALLET_SUFFICIENCY', 'EQUALS', true, m.merchant_id, 'ACTIVE', now(), '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978', '1f2ffdfb-c4a9-5a00-a656-9f3a132b1978'
FROM coupons.coupon_rule_groups crg
JOIN coupons.coupons c ON c.coupon_id = crg.coupon_id
JOIN merchants.merchants m ON m.merchant_id = c.merchant_id
WHERE m.merchant_code = 'MNT_PROPERTY_OPERATOR' AND c.coupon_code = 'SERVICE100' AND crg.rule_group_code = 'BASELINE_SCOPE'
ON CONFLICT ON CONSTRAINT uq_coupon_rules__group_rule_code DO UPDATE SET
  rule_name = EXCLUDED.rule_name, rule_type = EXCLUDED.rule_type, rule_operator = EXCLUDED.rule_operator, rule_value_boolean = EXCLUDED.rule_value_boolean,
  merchant_id = EXCLUDED.merchant_id, rule_status = EXCLUDED.rule_status, updated_at = now(), updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id;

-- 14. Basic post-seed verification
DO $$
DECLARE
    missing_count integer;
BEGIN
    SELECT count(*) INTO missing_count
    FROM (
        VALUES
            ((SELECT count(*) FROM identity.service_identities WHERE service_identity_code = 'central-pms')),
            ((SELECT count(*) FROM sites.site_groups WHERE site_group_code = 'MNT')),
            ((SELECT count(*) FROM payments.payment_rails WHERE rail_code = 'PAYMONGO_QRPH_DEV')),
            ((SELECT count(*) FROM integration.vendor_systems WHERE vendor_code = 'MOCK_VENDOR_PMS' AND environment_code = 'DEV')),
            ((SELECT count(*) FROM gates.gate_devices WHERE device_code = 'MNT-EXIT-GATE-01')),
            ((SELECT count(*) FROM discounts.discount_policy_references WHERE policy_code = 'MNT_LOCAL_SENIOR_DEV')),
            ((SELECT count(*) FROM merchants.merchants WHERE merchant_code = 'MNT_TENANT_COFFEE')),
            ((SELECT count(*) FROM coupons.coupons WHERE coupon_code = 'DEV20'))
    ) AS checks(found_count)
    WHERE found_count = 0;

    IF missing_count > 0 THEN
        RAISE EXCEPTION 'ExitPass v1.2 reference-data verification failed. Missing required seeded records: %', missing_count;
    END IF;
END $$;

COMMIT;
