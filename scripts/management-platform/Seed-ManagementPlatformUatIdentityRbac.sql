-- ExitPass v1.3 local/UAT Management Platform identity and RBAC seed.
-- Not production seed data. Do not include in baseline DDL or migrations.
-- This script is deterministic, idempotent, and local/UAT-only.
-- It does not create mutation APIs, payment state, fiscal state, gate state,
-- HikCentral state, refunds, rendered artifacts, secrets, or raw evidence.

BEGIN;
SET CONSTRAINTS ALL DEFERRED;

DO $$
BEGIN
    IF current_database() NOT IN (
        'exitpass_v12_dev',
        'centralpms_operator_uat_aligned_local',
        'centralpms_aligned_discount_payment_si_runtime_local',
        'centralpms_aligned_discount_exit_authorization_runtime_local'
    ) THEN
        RAISE EXCEPTION 'Refusing to run Management Platform UAT identity/RBAC seed against database %. Expected exitpass_v12_dev, centralpms_operator_uat_aligned_local, centralpms_aligned_discount_payment_si_runtime_local, or centralpms_aligned_discount_exit_authorization_runtime_local.', current_database();
    END IF;

    IF to_regclass('identity.users') IS NULL
       OR to_regclass('identity.roles') IS NULL
       OR to_regclass('identity.permissions') IS NULL
       OR to_regclass('identity.user_roles') IS NULL
       OR to_regclass('identity.role_permissions') IS NULL THEN
        RAISE EXCEPTION 'Required identity/RBAC tables are not present.';
    END IF;
END $$;

CREATE OR REPLACE FUNCTION pg_temp.exitpass_uat_uuid(input text)
RETURNS uuid
LANGUAGE sql
IMMUTABLE
AS $$
    SELECT (
        substr(md5(input), 1, 8) || '-' ||
        substr(md5(input), 9, 4) || '-' ||
        substr(md5(input), 13, 4) || '-' ||
        substr(md5(input), 17, 4) || '-' ||
        substr(md5(input), 21, 12)
    )::uuid
$$;

CREATE TEMP TABLE management_platform_uat_users (
    user_id uuid PRIMARY KEY,
    username varchar(128) NOT NULL,
    email varchar(256),
    display_name varchar(128) NOT NULL,
    user_type identity.user_type_enum NOT NULL,
    role_code varchar(64) NOT NULL
) ON COMMIT DROP;

INSERT INTO management_platform_uat_users (user_id, username, email, display_name, user_type, role_code) VALUES
('79000000-0000-0000-0000-000000000001', 'uat-system-rbac-admin', 'uat-system-rbac-admin@example.test', 'UAT System / RBAC Administrator', 'INTERNAL_ADMIN', 'SYSTEM_RBAC_ADMINISTRATOR'),
('79000000-0000-0000-0000-000000000002', 'uat-platform-admin', 'uat-platform-admin@example.test', 'UAT Platform Administrator', 'OPERATIONS_USER', 'PLATFORM_ADMINISTRATOR'),
('77000000-0000-0000-0000-000000000012', 'uat-operations-supervisor', 'uat-operations-supervisor@example.test', 'UAT Operations Supervisor', 'SITE_OPERATOR', 'OPERATIONS_SUPERVISOR'),
('77000000-0000-0000-0000-000000000010', 'uat-operator-support', 'uat-operator-support@example.test', 'UAT Operator / Support Staff', 'SITE_OPERATOR', 'OPERATOR_SUPPORT_STAFF'),
('79000000-0000-0000-0000-000000000005', 'uat-finance-reconciliation', 'uat-finance-reconciliation@example.test', 'UAT Finance / Reconciliation Analyst', 'FINANCE_USER', 'FINANCE_RECONCILIATION_ANALYST'),
('79000000-0000-0000-0000-000000000006', 'uat-compliance-policy-admin', 'uat-compliance-policy-admin@example.test', 'UAT Compliance / Policy Administrator', 'COMPLIANCE_USER', 'COMPLIANCE_POLICY_ADMINISTRATOR'),
('79000000-0000-0000-0000-000000000007', 'uat-executive-management', 'uat-executive-management@example.test', 'UAT Executive / Management', 'SUPPORT_USER', 'EXECUTIVE_MANAGEMENT');

CREATE TEMP TABLE management_platform_uat_roles (
    role_code varchar(64) PRIMARY KEY,
    role_name varchar(128) NOT NULL,
    role_description text NOT NULL,
    role_type identity.role_type_enum NOT NULL,
    is_privileged boolean NOT NULL,
    requires_elevated_approval boolean NOT NULL
) ON COMMIT DROP;

INSERT INTO management_platform_uat_roles (role_code, role_name, role_description, role_type, is_privileged, requires_elevated_approval) VALUES
('SYSTEM_RBAC_ADMINISTRATOR', 'System / RBAC Administrator', 'UAT role bundle for identity, RBAC, role, permission, assignment, and access audit inventory/admin posture. Business workflow authority must be separately granted.', 'SYSTEM', true, true),
('PLATFORM_ADMINISTRATOR', 'Platform Administrator', 'UAT role bundle for site, site group, device, shift, POS Server/fiscal configuration, connector, platform configuration, and operational readiness administration.', 'OPERATIONS', true, true),
('OPERATIONS_SUPERVISOR', 'Operations Supervisor', 'UAT role bundle for higher-trust operational review, statutory discount approval/rejection, controlled Sales Invoice void authority, and operational audit visibility. Payable-basis application remains service-channel payment-time authority.', 'OPERATIONS', true, true),
('OPERATOR_SUPPORT_STAFF', 'Operator / Support Staff', 'UAT role bundle for site-scoped operational lookup, statutory discount draft initiation, metadata-only evidence capture, and status viewing.', 'SUPPORT', false, false),
('FINANCE_RECONCILIATION_ANALYST', 'Finance / Reconciliation Analyst', 'UAT role bundle for financial, payment, fiscal, discount, revenue, variance, and reconciliation reporting.', 'FINANCE', false, false),
('COMPLIANCE_POLICY_ADMINISTRATOR', 'Compliance / Policy Administrator', 'UAT role bundle for compliance audit review, statutory discount policy governance, evidence rules, policy import review, and compliance reporting.', 'COMPLIANCE', true, true),
('EXECUTIVE_MANAGEMENT', 'Executive / Management', 'UAT role bundle for read-only executive dashboard, management reporting, KPI, performance, fiscal summary, statutory discount summary, and exception trend visibility.', 'OTHER', false, false);

CREATE TEMP TABLE management_platform_uat_permissions (
    permission_code varchar(96) PRIMARY KEY,
    permission_name varchar(128) NOT NULL,
    permission_description text NOT NULL,
    permission_domain varchar(64) NOT NULL,
    permission_action varchar(64) NOT NULL,
    is_sensitive boolean NOT NULL,
    requires_audit boolean NOT NULL
) ON COMMIT DROP;

INSERT INTO management_platform_uat_permissions (permission_code, permission_name, permission_description, permission_domain, permission_action, is_sensitive, requires_audit) VALUES
('management-platform.identity-rbac.inventory.read', 'Identity/RBAC inventory read', 'Read safe identity and RBAC inventory for the Management Platform.', 'management-platform', 'read', true, true),
('user.view', 'View users', 'Read safe user inventory.', 'administration', 'view', true, true),
('user.manage', 'Manage users', 'Target permission for future user administration.', 'administration', 'manage', true, true),
('rbac.view', 'View RBAC', 'Read RBAC configuration and assignment inventory.', 'administration', 'view', true, true),
('rbac.manage', 'Manage RBAC', 'Target permission for future RBAC administration.', 'administration', 'manage', true, true),
('role.view', 'View roles', 'Read role inventory.', 'administration', 'view', true, true),
('role.manage', 'Manage roles', 'Target permission for future role administration.', 'administration', 'manage', true, true),
('permission.view', 'View permissions', 'Read permission inventory.', 'administration', 'view', true, true),
('permission.manage', 'Manage permissions', 'Target permission for future permission administration.', 'administration', 'manage', true, true),
('assignment.view', 'View assignments', 'Read user, role, site, device, and shift assignments.', 'administration', 'view', true, true),
('assignment.manage', 'Manage assignments', 'Target permission for future assignment administration.', 'administration', 'manage', true, true),
('access-audit.view', 'View access audit', 'Read access and RBAC audit visibility.', 'administration', 'view', true, true),
('site.view', 'View sites', 'Read site inventory.', 'platform-config', 'view', false, true),
('site.manage', 'Manage sites', 'Target permission for future site administration.', 'platform-config', 'manage', true, true),
('site-group.view', 'View site groups', 'Read site group inventory.', 'platform-config', 'view', false, true),
('site-group.manage', 'Manage site groups', 'Target permission for future site group administration.', 'platform-config', 'manage', true, true),
('device.view', 'View devices', 'Read device inventory.', 'platform-config', 'view', false, true),
('device.manage', 'Manage devices', 'Target permission for future device administration.', 'platform-config', 'manage', true, true),
('device-binding.view', 'View device bindings', 'Read operator device binding inventory.', 'platform-config', 'view', true, true),
('device-binding.manage', 'Manage device bindings', 'Target permission for future device binding administration.', 'platform-config', 'manage', true, true),
('shift.view', 'View shifts', 'Read operator shift inventory.', 'platform-config', 'view', true, true),
('shift.manage', 'Manage shifts', 'Target permission for future shift administration.', 'platform-config', 'manage', true, true),
('pos-server-config.view', 'View POS Server configuration', 'Read POS Server/fiscal configuration inventory.', 'platform-config', 'view', true, true),
('pos-server-config.manage', 'Manage POS Server configuration', 'Target permission for future POS Server/fiscal configuration administration.', 'platform-config', 'manage', true, true),
('connector-config.view', 'View connector configuration', 'Read connector configuration inventory.', 'platform-config', 'view', true, true),
('connector-config.manage', 'Manage connector configuration', 'Target permission for future connector configuration administration.', 'platform-config', 'manage', true, true),
('platform-config.view', 'View platform configuration', 'Read platform configuration inventory.', 'platform-config', 'view', true, true),
('platform-config.manage', 'Manage platform configuration', 'Target permission for future platform configuration administration.', 'platform-config', 'manage', true, true),
('environment-config.view', 'View environment configuration', 'Read safe local/UAT environment configuration inventory.', 'platform-config', 'view', true, true),
('uat-fixture.manage', 'Manage UAT fixtures', 'Local/UAT-only permission for deterministic fixture preparation.', 'platform-config', 'manage', true, true),
('operational-monitoring.view', 'View operational monitoring', 'Read operational monitoring surfaces.', 'monitoring', 'view', false, true),
('statutory-discounts.session.lookup', 'Lookup statutory discount session', 'Lookup session context for statutory discount workflow.', 'statutory-discounts', 'lookup', false, true),
('statutory-discounts.draft.view', 'View statutory discount draft', 'View statutory discount draft/detail records.', 'statutory-discounts', 'view', false, true),
('statutory-discounts.draft.create', 'Create statutory discount draft', 'Create statutory discount review draft.', 'statutory-discounts', 'create', true, true),
('statutory-discounts.evidence.view', 'View statutory discount evidence', 'View metadata-only statutory discount evidence references.', 'statutory-discounts', 'view', true, true),
('statutory-discounts.evidence.capture', 'Capture statutory discount evidence', 'Capture metadata-only statutory discount evidence references.', 'statutory-discounts', 'capture', true, true),
('statutory-discounts.review.queue.read', 'Read statutory discount review queue', 'Read service-channel statutory discount review queue items.', 'statutory-discounts', 'read', true, true),
('statutory-discounts.review.detail.read', 'Read statutory discount review detail', 'Read service-channel statutory discount review detail records.', 'statutory-discounts', 'read', true, true),
('statutory-discounts.decision.review', 'Review statutory discount decision', 'Review statutory discount decision context.', 'statutory-discounts', 'review', true, true),
('statutory-discounts.decision.approve', 'Approve statutory discount', 'Approve statutory discount validation when prerequisites and segregation controls are satisfied.', 'statutory-discounts', 'approve', true, true),
('statutory-discounts.decision.reject', 'Reject statutory discount', 'Reject statutory discount validation when authorized.', 'statutory-discounts', 'reject', true, true),
('statutory-discounts.payable-basis.apply', 'Apply statutory discount payable basis', 'Apply approved statutory discount to payable basis at payment time through an authorized service channel.', 'statutory-discounts', 'apply', true, true),
('statutory-discounts.application.read', 'Read statutory discount application status', 'Read statutory discount payable-basis application status.', 'statutory-discounts', 'read', true, true),
('statutory-discounts.policy.resolve', 'Resolve statutory discount policy', 'Read statutory discount policy resolution context.', 'statutory-discounts', 'resolve', true, true),
('statutory-discounts.audit.read', 'Read statutory discount audit', 'Read statutory discount audit/reporting surfaces.', 'statutory-discounts', 'read', true, true),
('fiscal-issuance.status.read', 'Read Sales Invoice status', 'Read Sales Invoice/fiscal issuance status.', 'fiscal', 'read', false, true),
('fiscal-issuance.void.command', 'Command Sales Invoice void', 'Execute controlled Sales Invoice void command where allowed.', 'fiscal', 'command', true, true),
('fiscal-issuance.void.audit.read', 'Read Sales Invoice void audit', 'Read Sales Invoice void audit report.', 'fiscal', 'read', true, true),
('fiscal-view-audit.read', 'Read Sales Invoice view audit', 'Read Sales Invoice status view audit report.', 'fiscal', 'read', true, true),
('sales-invoice-report.view', 'View Sales Invoice report', 'Read Sales Invoice reporting surfaces.', 'reporting', 'view', false, true),
('fiscal-report.view', 'View fiscal report', 'Read fiscal reporting surfaces.', 'reporting', 'view', false, true),
('ticket.lookup', 'Lookup ticket', 'Lookup operator ticket/session context.', 'operator-console', 'lookup', false, true),
('projection-health.view', 'View projection health', 'Read projection health surfaces.', 'operator-console', 'view', false, true),
('ops.vendor-session-projection-health.view', 'View vendor session projection health', 'Read vendor session projection health.', 'operator-console', 'view', false, true),
('operator-console.vendor-projection-health.view', 'View Operator Console vendor projection health', 'Read Operator Console vendor projection health.', 'operator-console', 'view', false, true),
('vendor-acknowledgments.view', 'View vendor acknowledgments', 'Read vendor acknowledgment surfaces.', 'operator-console', 'view', false, true),
('operator-workflow-audit.view', 'View operator workflow audit', 'Read operator workflow audit surfaces.', 'audit', 'view', true, true),
('reconciliation.view', 'View reconciliation', 'Read reconciliation records.', 'reconciliation', 'view', false, true),
('reconciliation.manage', 'Manage reconciliation', 'Manage reconciliation workflows where UAT requires.', 'reconciliation', 'manage', true, true),
('payment-report.view', 'View payment report', 'Read payment reporting surfaces.', 'reporting', 'view', false, true),
('statutory-discount-report.view', 'View statutory discount report', 'Read statutory discount reporting surfaces.', 'reporting', 'view', false, true),
('revenue-report.view', 'View revenue report', 'Read revenue reporting surfaces.', 'reporting', 'view', false, true),
('variance-report.view', 'View variance report', 'Read variance reporting surfaces.', 'reporting', 'view', false, true),
('reports.view', 'View reports', 'Read reporting surfaces.', 'reporting', 'view', false, true),
('reports.export', 'Export reports', 'Export reports where explicitly granted.', 'reporting', 'export', true, true),
('dashboard.view', 'View dashboard', 'Read dashboard surfaces.', 'reporting', 'view', false, true),
('executive-summary.view', 'View executive summary', 'Read executive summary surfaces.', 'reporting', 'view', false, true),
('site-performance.view', 'View site performance', 'Read site performance dashboard.', 'reporting', 'view', false, true),
('site-group-performance.view', 'View site group performance', 'Read site group performance dashboard.', 'reporting', 'view', false, true),
('revenue-summary.view', 'View revenue summary', 'Read revenue summary dashboard.', 'reporting', 'view', false, true),
('payment-summary.view', 'View payment summary', 'Read payment summary dashboard.', 'reporting', 'view', false, true),
('fiscal-summary.view', 'View fiscal summary', 'Read fiscal summary dashboard.', 'reporting', 'view', false, true),
('statutory-discount-summary.view', 'View statutory discount summary', 'Read statutory discount summary dashboard.', 'reporting', 'view', false, true),
('exception-trend.view', 'View exception trends', 'Read exception trend dashboard.', 'reporting', 'view', false, true),
('audit-report.view', 'View audit report', 'Read audit reporting surfaces.', 'audit', 'view', true, true),
('compliance-report.view', 'View compliance report', 'Read compliance reporting surfaces.', 'reporting', 'view', true, true),
('policy-import.submit', 'Submit policy import', 'Submit policy import review package.', 'policy', 'submit', true, true),
('policy-import.review', 'Review policy import', 'Review policy import package.', 'policy', 'review', true, true),
('policy-import.approve', 'Approve policy import', 'Approve policy import package where allowed.', 'policy', 'approve', true, true),
('policy-import.manage', 'Manage policy import', 'Manage policy import workflow.', 'policy', 'manage', true, true),
('operator-console.policy-import-review.submit', 'Submit Operator Console policy import', 'Existing Operator Console policy import submit permission.', 'policy', 'submit', true, true),
('operator-console.policy-import-review.view-own', 'View own Operator Console policy import', 'Existing Operator Console policy import own-view permission.', 'policy', 'view', true, true),
('operator-console.policy-import-review.review', 'Review Operator Console policy import', 'Existing Operator Console policy import review permission.', 'policy', 'review', true, true),
('operator-console.policy-import-review.manage', 'Manage Operator Console policy import', 'Existing Operator Console policy import manage permission.', 'policy', 'manage', true, true),
('operator-console.policy-import-review.approve.legal', 'Approve policy import legal', 'Existing legal policy import approval permission.', 'policy', 'approve', true, true),
('operator-console.policy-import-review.approve.ops', 'Approve policy import ops', 'Existing operations policy import approval permission.', 'policy', 'approve', true, true),
('operator-console.policy-import-review.approve.qa', 'Approve policy import QA', 'Existing QA policy import approval permission.', 'policy', 'approve', true, true),
('operator-console.policy-import-review.approve.db', 'Approve policy import DB', 'Existing DB policy import approval permission.', 'policy', 'approve', true, true),
('statutory-discount-policy.view', 'View statutory discount policy', 'Read statutory discount policy inventory.', 'policy', 'view', true, true),
('statutory-discount-policy.manage', 'Manage statutory discount policy', 'Target permission for future statutory discount policy administration.', 'policy', 'manage', true, true),
('evidence-rule-policy.view', 'View evidence rule policy', 'Read evidence rule policy inventory.', 'policy', 'view', true, true),
('evidence-rule-policy.manage', 'Manage evidence rule policy', 'Target permission for future evidence rule policy administration.', 'policy', 'manage', true, true);

CREATE TEMP TABLE management_platform_uat_role_permission_map (
    role_code varchar(64) NOT NULL,
    permission_code varchar(96) NOT NULL
) ON COMMIT DROP;

INSERT INTO management_platform_uat_role_permission_map (role_code, permission_code) VALUES
('SYSTEM_RBAC_ADMINISTRATOR', 'management-platform.identity-rbac.inventory.read'),
('SYSTEM_RBAC_ADMINISTRATOR', 'user.view'),
('SYSTEM_RBAC_ADMINISTRATOR', 'user.manage'),
('SYSTEM_RBAC_ADMINISTRATOR', 'rbac.view'),
('SYSTEM_RBAC_ADMINISTRATOR', 'rbac.manage'),
('SYSTEM_RBAC_ADMINISTRATOR', 'role.view'),
('SYSTEM_RBAC_ADMINISTRATOR', 'role.manage'),
('SYSTEM_RBAC_ADMINISTRATOR', 'permission.view'),
('SYSTEM_RBAC_ADMINISTRATOR', 'permission.manage'),
('SYSTEM_RBAC_ADMINISTRATOR', 'assignment.view'),
('SYSTEM_RBAC_ADMINISTRATOR', 'assignment.manage'),
('SYSTEM_RBAC_ADMINISTRATOR', 'access-audit.view'),
('PLATFORM_ADMINISTRATOR', 'site.view'),
('PLATFORM_ADMINISTRATOR', 'site.manage'),
('PLATFORM_ADMINISTRATOR', 'site-group.view'),
('PLATFORM_ADMINISTRATOR', 'site-group.manage'),
('PLATFORM_ADMINISTRATOR', 'device.view'),
('PLATFORM_ADMINISTRATOR', 'device.manage'),
('PLATFORM_ADMINISTRATOR', 'device-binding.view'),
('PLATFORM_ADMINISTRATOR', 'device-binding.manage'),
('PLATFORM_ADMINISTRATOR', 'shift.view'),
('PLATFORM_ADMINISTRATOR', 'shift.manage'),
('PLATFORM_ADMINISTRATOR', 'pos-server-config.view'),
('PLATFORM_ADMINISTRATOR', 'pos-server-config.manage'),
('PLATFORM_ADMINISTRATOR', 'connector-config.view'),
('PLATFORM_ADMINISTRATOR', 'connector-config.manage'),
('PLATFORM_ADMINISTRATOR', 'operational-monitoring.view'),
('PLATFORM_ADMINISTRATOR', 'platform-config.view'),
('PLATFORM_ADMINISTRATOR', 'platform-config.manage'),
('PLATFORM_ADMINISTRATOR', 'environment-config.view'),
('PLATFORM_ADMINISTRATOR', 'uat-fixture.manage'),
('OPERATIONS_SUPERVISOR', 'statutory-discounts.draft.view'),
('OPERATIONS_SUPERVISOR', 'statutory-discounts.evidence.view'),
('OPERATIONS_SUPERVISOR', 'statutory-discounts.review.queue.read'),
('OPERATIONS_SUPERVISOR', 'statutory-discounts.review.detail.read'),
('OPERATIONS_SUPERVISOR', 'statutory-discounts.decision.review'),
('OPERATIONS_SUPERVISOR', 'statutory-discounts.decision.approve'),
('OPERATIONS_SUPERVISOR', 'statutory-discounts.decision.reject'),
('OPERATIONS_SUPERVISOR', 'statutory-discounts.policy.resolve'),
('OPERATIONS_SUPERVISOR', 'fiscal-issuance.status.read'),
('OPERATIONS_SUPERVISOR', 'fiscal-issuance.void.command'),
('OPERATIONS_SUPERVISOR', 'operator-workflow-audit.view'),
('OPERATIONS_SUPERVISOR', 'projection-health.view'),
('OPERATIONS_SUPERVISOR', 'ops.vendor-session-projection-health.view'),
('OPERATIONS_SUPERVISOR', 'operator-console.vendor-projection-health.view'),
('OPERATIONS_SUPERVISOR', 'vendor-acknowledgments.view'),
('OPERATOR_SUPPORT_STAFF', 'statutory-discounts.session.lookup'),
('OPERATOR_SUPPORT_STAFF', 'statutory-discounts.draft.view'),
('OPERATOR_SUPPORT_STAFF', 'statutory-discounts.draft.create'),
('OPERATOR_SUPPORT_STAFF', 'statutory-discounts.evidence.view'),
('OPERATOR_SUPPORT_STAFF', 'statutory-discounts.evidence.capture'),
('OPERATOR_SUPPORT_STAFF', 'statutory-discounts.policy.resolve'),
('OPERATOR_SUPPORT_STAFF', 'fiscal-issuance.status.read'),
('OPERATOR_SUPPORT_STAFF', 'ticket.lookup'),
('OPERATOR_SUPPORT_STAFF', 'projection-health.view'),
('OPERATOR_SUPPORT_STAFF', 'ops.vendor-session-projection-health.view'),
('OPERATOR_SUPPORT_STAFF', 'operator-console.vendor-projection-health.view'),
('OPERATOR_SUPPORT_STAFF', 'vendor-acknowledgments.view'),
('FINANCE_RECONCILIATION_ANALYST', 'reconciliation.view'),
('FINANCE_RECONCILIATION_ANALYST', 'reconciliation.manage'),
('FINANCE_RECONCILIATION_ANALYST', 'payment-report.view'),
('FINANCE_RECONCILIATION_ANALYST', 'fiscal-report.view'),
('FINANCE_RECONCILIATION_ANALYST', 'sales-invoice-report.view'),
('FINANCE_RECONCILIATION_ANALYST', 'statutory-discount-report.view'),
('FINANCE_RECONCILIATION_ANALYST', 'revenue-report.view'),
('FINANCE_RECONCILIATION_ANALYST', 'variance-report.view'),
('FINANCE_RECONCILIATION_ANALYST', 'reports.view'),
('FINANCE_RECONCILIATION_ANALYST', 'reports.export'),
('COMPLIANCE_POLICY_ADMINISTRATOR', 'statutory-discounts.audit.read'),
('COMPLIANCE_POLICY_ADMINISTRATOR', 'fiscal-issuance.void.audit.read'),
('COMPLIANCE_POLICY_ADMINISTRATOR', 'fiscal-view-audit.read'),
('COMPLIANCE_POLICY_ADMINISTRATOR', 'audit-report.view'),
('COMPLIANCE_POLICY_ADMINISTRATOR', 'policy-import.submit'),
('COMPLIANCE_POLICY_ADMINISTRATOR', 'policy-import.review'),
('COMPLIANCE_POLICY_ADMINISTRATOR', 'policy-import.approve'),
('COMPLIANCE_POLICY_ADMINISTRATOR', 'policy-import.manage'),
('COMPLIANCE_POLICY_ADMINISTRATOR', 'operator-console.policy-import-review.submit'),
('COMPLIANCE_POLICY_ADMINISTRATOR', 'operator-console.policy-import-review.view-own'),
('COMPLIANCE_POLICY_ADMINISTRATOR', 'operator-console.policy-import-review.review'),
('COMPLIANCE_POLICY_ADMINISTRATOR', 'operator-console.policy-import-review.manage'),
('COMPLIANCE_POLICY_ADMINISTRATOR', 'operator-console.policy-import-review.approve.legal'),
('COMPLIANCE_POLICY_ADMINISTRATOR', 'operator-console.policy-import-review.approve.ops'),
('COMPLIANCE_POLICY_ADMINISTRATOR', 'operator-console.policy-import-review.approve.qa'),
('COMPLIANCE_POLICY_ADMINISTRATOR', 'operator-console.policy-import-review.approve.db'),
('COMPLIANCE_POLICY_ADMINISTRATOR', 'statutory-discount-policy.view'),
('COMPLIANCE_POLICY_ADMINISTRATOR', 'statutory-discount-policy.manage'),
('COMPLIANCE_POLICY_ADMINISTRATOR', 'evidence-rule-policy.view'),
('COMPLIANCE_POLICY_ADMINISTRATOR', 'evidence-rule-policy.manage'),
('COMPLIANCE_POLICY_ADMINISTRATOR', 'compliance-report.view'),
('COMPLIANCE_POLICY_ADMINISTRATOR', 'reports.export'),
('EXECUTIVE_MANAGEMENT', 'dashboard.view'),
('EXECUTIVE_MANAGEMENT', 'reports.view'),
('EXECUTIVE_MANAGEMENT', 'executive-summary.view'),
('EXECUTIVE_MANAGEMENT', 'site-performance.view'),
('EXECUTIVE_MANAGEMENT', 'site-group-performance.view'),
('EXECUTIVE_MANAGEMENT', 'revenue-summary.view'),
('EXECUTIVE_MANAGEMENT', 'payment-summary.view'),
('EXECUTIVE_MANAGEMENT', 'fiscal-summary.view'),
('EXECUTIVE_MANAGEMENT', 'statutory-discount-summary.view'),
('EXECUTIVE_MANAGEMENT', 'exception-trend.view'),
('EXECUTIVE_MANAGEMENT', 'operational-monitoring.view');

INSERT INTO identity.service_identities (
    service_identity_id,
    service_identity_code,
    service_identity_name,
    identity_type,
    identity_status,
    owning_service_name,
    credential_type,
    effective_from,
    effective_to
)
VALUES (
    '79000000-0000-0000-0000-000000000003',
    'MANAGEMENT_PLATFORM_UAT_IDENTITY_RBAC_SEED',
    'Management Platform UAT Identity/RBAC Seeder',
    'INTERNAL_SERVICE',
    'ACTIVE',
    'Central PMS Local UAT',
    'NONE',
    '2020-01-01T00:00:00Z',
    '2035-01-01T00:00:00Z'
)
ON CONFLICT (service_identity_id) DO UPDATE
SET service_identity_code = EXCLUDED.service_identity_code,
    service_identity_name = EXCLUDED.service_identity_name,
    identity_type = EXCLUDED.identity_type,
    identity_status = EXCLUDED.identity_status,
    owning_service_name = EXCLUDED.owning_service_name,
    credential_type = EXCLUDED.credential_type,
    effective_from = EXCLUDED.effective_from,
    effective_to = EXCLUDED.effective_to,
    updated_at = now();

INSERT INTO identity.users (
    user_id,
    username,
    email,
    email_normalized,
    display_name,
    user_type,
    user_status,
    effective_from,
    effective_to,
    created_by_service_identity_id,
    updated_by_service_identity_id
)
SELECT
    user_id,
    username,
    email,
    upper(email),
    display_name,
    user_type,
    'ACTIVE',
    '2020-01-01T00:00:00Z',
    '2035-01-01T00:00:00Z',
    '79000000-0000-0000-0000-000000000003',
    '79000000-0000-0000-0000-000000000003'
FROM management_platform_uat_users
ON CONFLICT (user_id) DO UPDATE
SET username = EXCLUDED.username,
    email = EXCLUDED.email,
    email_normalized = EXCLUDED.email_normalized,
    display_name = EXCLUDED.display_name,
    user_type = EXCLUDED.user_type,
    user_status = EXCLUDED.user_status,
    effective_from = EXCLUDED.effective_from,
    effective_to = EXCLUDED.effective_to,
    updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
    updated_at = now();

INSERT INTO identity.roles (
    role_id,
    role_code,
    role_name,
    role_description,
    role_type,
    role_status,
    is_privileged,
    requires_elevated_approval,
    effective_from,
    effective_to,
    created_by_service_identity_id,
    updated_by_service_identity_id
)
SELECT
    pg_temp.exitpass_uat_uuid('management-platform-uat-role:' || role_code),
    role_code,
    role_name,
    role_description,
    role_type,
    'ACTIVE',
    is_privileged,
    requires_elevated_approval,
    '2020-01-01T00:00:00Z',
    '2035-01-01T00:00:00Z',
    '79000000-0000-0000-0000-000000000003',
    '79000000-0000-0000-0000-000000000003'
FROM management_platform_uat_roles
ON CONFLICT ON CONSTRAINT uq_roles__role_code DO UPDATE
SET role_name = EXCLUDED.role_name,
    role_description = EXCLUDED.role_description,
    role_type = EXCLUDED.role_type,
    role_status = EXCLUDED.role_status,
    is_privileged = EXCLUDED.is_privileged,
    requires_elevated_approval = EXCLUDED.requires_elevated_approval,
    effective_from = EXCLUDED.effective_from,
    effective_to = EXCLUDED.effective_to,
    updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
    updated_at = now();

INSERT INTO identity.permissions (
    permission_id,
    permission_code,
    permission_name,
    permission_description,
    permission_domain,
    permission_action,
    permission_status,
    is_sensitive,
    requires_audit,
    created_by_service_identity_id,
    updated_by_service_identity_id
)
SELECT
    pg_temp.exitpass_uat_uuid('management-platform-uat-permission:' || permission_code),
    permission_code,
    permission_name,
    permission_description,
    permission_domain,
    permission_action,
    'ACTIVE',
    is_sensitive,
    requires_audit,
    '79000000-0000-0000-0000-000000000003',
    '79000000-0000-0000-0000-000000000003'
FROM management_platform_uat_permissions
ON CONFLICT ON CONSTRAINT uq_permissions__permission_code DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    permission_description = EXCLUDED.permission_description,
    permission_domain = EXCLUDED.permission_domain,
    permission_action = EXCLUDED.permission_action,
    permission_status = EXCLUDED.permission_status,
    is_sensitive = EXCLUDED.is_sensitive,
    requires_audit = EXCLUDED.requires_audit,
    updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
    updated_at = now();

UPDATE identity.role_permissions rp
SET binding_status = 'ACTIVE',
    binding_reason_code = 'MANAGEMENT_PLATFORM_UAT_ROLE_PERMISSION_SEED',
    assigned_by_service_identity_id = '79000000-0000-0000-0000-000000000003',
    effective_from = '2020-01-01T00:00:00Z',
    effective_to = '2035-01-01T00:00:00Z',
    revoked_at = NULL,
    revoked_by_user_id = NULL,
    revoked_by_service_identity_id = NULL,
    revocation_reason_code = NULL,
    updated_by_service_identity_id = '79000000-0000-0000-0000-000000000003',
    updated_at = now()
FROM management_platform_uat_role_permission_map rpm
JOIN identity.roles r ON r.role_code = rpm.role_code
JOIN identity.permissions p ON p.permission_code = rpm.permission_code
WHERE rp.role_id = r.role_id
  AND rp.permission_id = p.permission_id;

INSERT INTO identity.role_permissions (
    role_permission_id,
    role_id,
    permission_id,
    binding_status,
    binding_reason_code,
    assigned_by_service_identity_id,
    effective_from,
    effective_to,
    created_by_service_identity_id,
    updated_by_service_identity_id
)
SELECT
    pg_temp.exitpass_uat_uuid('management-platform-uat-role-permission:' || rpm.role_code || ':' || rpm.permission_code),
    r.role_id,
    p.permission_id,
    'ACTIVE',
    'MANAGEMENT_PLATFORM_UAT_ROLE_PERMISSION_SEED',
    '79000000-0000-0000-0000-000000000003',
    '2020-01-01T00:00:00Z',
    '2035-01-01T00:00:00Z',
    '79000000-0000-0000-0000-000000000003',
    '79000000-0000-0000-0000-000000000003'
FROM management_platform_uat_role_permission_map rpm
JOIN identity.roles r ON r.role_code = rpm.role_code
JOIN identity.permissions p ON p.permission_code = rpm.permission_code
WHERE NOT EXISTS (
    SELECT 1
    FROM identity.role_permissions existing
    WHERE existing.role_id = r.role_id
      AND existing.permission_id = p.permission_id
      AND existing.binding_status = 'ACTIVE'
);

UPDATE identity.user_roles ur
SET assignment_status = 'ACTIVE',
    assignment_reason_code = 'MANAGEMENT_PLATFORM_UAT_USER_ROLE_SEED',
    assigned_by_service_identity_id = '79000000-0000-0000-0000-000000000003',
    effective_from = '2020-01-01T00:00:00Z',
    effective_to = '2035-01-01T00:00:00Z',
    revoked_at = NULL,
    revoked_by_user_id = NULL,
    revoked_by_service_identity_id = NULL,
    revocation_reason_code = NULL,
    updated_by_service_identity_id = '79000000-0000-0000-0000-000000000003',
    updated_at = now()
FROM management_platform_uat_users u
JOIN identity.roles r ON r.role_code = u.role_code
WHERE ur.user_id = u.user_id
  AND ur.role_id = r.role_id;

INSERT INTO identity.user_roles (
    user_role_id,
    user_id,
    role_id,
    assignment_status,
    assignment_reason_code,
    assigned_by_service_identity_id,
    effective_from,
    effective_to,
    created_by_service_identity_id,
    updated_by_service_identity_id
)
SELECT
    pg_temp.exitpass_uat_uuid('management-platform-uat-user-role:' || u.user_id::text || ':' || u.role_code),
    u.user_id,
    r.role_id,
    'ACTIVE',
    'MANAGEMENT_PLATFORM_UAT_USER_ROLE_SEED',
    '79000000-0000-0000-0000-000000000003',
    '2020-01-01T00:00:00Z',
    '2035-01-01T00:00:00Z',
    '79000000-0000-0000-0000-000000000003',
    '79000000-0000-0000-0000-000000000003'
FROM management_platform_uat_users u
JOIN identity.roles r ON r.role_code = u.role_code
WHERE NOT EXISTS (
    SELECT 1
    FROM identity.user_roles existing
    WHERE existing.user_id = u.user_id
      AND existing.role_id = r.role_id
      AND existing.assignment_status = 'ACTIVE'
);

COMMIT;
