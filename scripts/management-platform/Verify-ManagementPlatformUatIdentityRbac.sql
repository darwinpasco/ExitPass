-- ExitPass v1.3 local/UAT Management Platform identity and RBAC verification.
-- Local/UAT-only read-only verification.

DO $$
BEGIN
    IF current_database() NOT IN (
        'exitpass_v12_dev',
        'centralpms_operator_uat_aligned_local',
        'centralpms_aligned_discount_payment_si_runtime_local',
        'centralpms_aligned_discount_exit_authorization_runtime_local'
    ) THEN
        RAISE EXCEPTION 'Refusing to verify Management Platform UAT identity/RBAC against database %. Expected exitpass_v12_dev, centralpms_operator_uat_aligned_local, centralpms_aligned_discount_payment_si_runtime_local, or centralpms_aligned_discount_exit_authorization_runtime_local.', current_database();
    END IF;
END $$;

SET client_min_messages TO WARNING;
DROP TABLE IF EXISTS management_platform_uat_operator_console_fixture_counts;
RESET client_min_messages;

CREATE TEMP TABLE management_platform_uat_operator_console_fixture_counts (
    requester_user_count integer NOT NULL,
    reviewer_user_count integer NOT NULL,
    site_group_count integer NOT NULL,
    site_count integer NOT NULL,
    device_binding_count integer,
    active_shift_count integer
);

INSERT INTO management_platform_uat_operator_console_fixture_counts (
    requester_user_count,
    reviewer_user_count,
    site_group_count,
    site_count,
    device_binding_count,
    active_shift_count
)
SELECT
    (SELECT COUNT(*) FROM identity.users WHERE user_id = '77000000-0000-0000-0000-000000000010' AND username = 'uat-operator-support' AND user_status = 'ACTIVE'),
    (SELECT COUNT(*) FROM identity.users WHERE user_id = '77000000-0000-0000-0000-000000000012' AND username = 'uat-operations-supervisor' AND user_status = 'ACTIVE'),
    (SELECT COUNT(*) FROM sites.site_groups WHERE site_group_id = '77000000-0000-0000-0000-000000000001' AND site_group_status = 'ACTIVE'),
    (SELECT COUNT(*) FROM sites.sites WHERE site_id = '77000000-0000-0000-0000-000000000002' AND site_status = 'ACTIVE'),
    NULL,
    NULL;

DO $$
DECLARE
    v_device_binding_count integer;
    v_active_shift_count integer;
BEGIN
    IF to_regclass('operator_console.operator_device_bindings') IS NOT NULL THEN
        EXECUTE $sql$
            SELECT COUNT(*)
            FROM operator_console.operator_device_bindings
            WHERE operator_device_binding_id = '77000000-0000-0000-0000-000000000030'
              AND device_status = 'ACTIVE'
        $sql$ INTO v_device_binding_count;

        UPDATE management_platform_uat_operator_console_fixture_counts
           SET device_binding_count = v_device_binding_count;
    END IF;

    IF to_regclass('operator_console.operator_shifts') IS NOT NULL THEN
        EXECUTE $sql$
            SELECT COUNT(*)
            FROM operator_console.operator_shifts
            WHERE operator_shift_id IN ('77000000-0000-0000-0000-000000000050', '77000000-0000-0000-0000-000000000052')
              AND operational_status = 'ACTIVE'
        $sql$ INTO v_active_shift_count;

        UPDATE management_platform_uat_operator_console_fixture_counts
           SET active_shift_count = v_active_shift_count;
    END IF;
END $$;

WITH expected_users(user_id, username, role_code) AS (
    VALUES
    ('79000000-0000-0000-0000-000000000001'::uuid, 'uat-system-rbac-admin', 'SYSTEM_RBAC_ADMINISTRATOR'),
    ('79000000-0000-0000-0000-000000000002'::uuid, 'uat-platform-admin', 'PLATFORM_ADMINISTRATOR'),
    ('77000000-0000-0000-0000-000000000012'::uuid, 'uat-operations-supervisor', 'OPERATIONS_SUPERVISOR'),
    ('77000000-0000-0000-0000-000000000010'::uuid, 'uat-operator-support', 'OPERATOR_SUPPORT_STAFF'),
    ('79000000-0000-0000-0000-000000000005'::uuid, 'uat-finance-reconciliation', 'FINANCE_RECONCILIATION_ANALYST'),
    ('79000000-0000-0000-0000-000000000006'::uuid, 'uat-compliance-policy-admin', 'COMPLIANCE_POLICY_ADMINISTRATOR'),
    ('79000000-0000-0000-0000-000000000007'::uuid, 'uat-executive-management', 'EXECUTIVE_MANAGEMENT')
),
expected_roles(role_code) AS (
    VALUES
    ('SYSTEM_RBAC_ADMINISTRATOR'),
    ('PLATFORM_ADMINISTRATOR'),
    ('OPERATIONS_SUPERVISOR'),
    ('OPERATOR_SUPPORT_STAFF'),
    ('FINANCE_RECONCILIATION_ANALYST'),
    ('COMPLIANCE_POLICY_ADMINISTRATOR'),
    ('EXECUTIVE_MANAGEMENT')
),
expected_role_permissions(role_code, permission_code) AS (
    VALUES
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
    ('OPERATIONS_SUPERVISOR', 'statutory-discounts.decision.review'),
    ('OPERATIONS_SUPERVISOR', 'statutory-discounts.decision.approve'),
    ('OPERATIONS_SUPERVISOR', 'statutory-discounts.decision.reject'),
    ('OPERATIONS_SUPERVISOR', 'statutory-discounts.payable-basis.apply'),
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
    ('EXECUTIVE_MANAGEMENT', 'operational-monitoring.view')
),
role_permission_state AS (
    SELECT
        erp.role_code,
        erp.permission_code,
        CASE WHEN rp.role_permission_id IS NULL THEN 1 ELSE 0 END AS missing_binding
    FROM expected_role_permissions erp
    LEFT JOIN identity.roles r
        ON r.role_code = erp.role_code
       AND r.role_status = 'ACTIVE'
    LEFT JOIN identity.permissions p
        ON p.permission_code = erp.permission_code
       AND p.permission_status = 'ACTIVE'
    LEFT JOIN identity.role_permissions rp
        ON rp.role_id = r.role_id
       AND rp.permission_id = p.permission_id
       AND rp.binding_status = 'ACTIVE'
),
user_role_state AS (
    SELECT
        eu.user_id,
        eu.username,
        eu.role_code,
        CASE WHEN ur.user_role_id IS NULL THEN 1 ELSE 0 END AS missing_assignment
    FROM expected_users eu
    LEFT JOIN identity.users u
        ON u.user_id = eu.user_id
       AND u.username = eu.username
       AND u.user_status = 'ACTIVE'
    LEFT JOIN identity.roles r
        ON r.role_code = eu.role_code
       AND r.role_status = 'ACTIVE'
    LEFT JOIN identity.user_roles ur
        ON ur.user_id = u.user_id
       AND ur.role_id = r.role_id
       AND ur.assignment_status = 'ACTIVE'
),
active_role_permissions AS (
    SELECT
        r.role_code,
        p.permission_code
    FROM identity.role_permissions rp
    JOIN identity.roles r ON r.role_id = rp.role_id
    JOIN identity.permissions p ON p.permission_id = rp.permission_id
    WHERE rp.binding_status = 'ACTIVE'
      AND r.role_code IN (SELECT role_code FROM expected_roles)
),
operator_console_fixture AS (
    SELECT *
    FROM management_platform_uat_operator_console_fixture_counts
)
SELECT
    current_database() AS database_name,
    (SELECT COUNT(*) FROM expected_users) AS expected_uat_user_count,
    (SELECT COUNT(*) FROM identity.users u JOIN expected_users eu ON eu.user_id = u.user_id WHERE u.username = eu.username AND u.user_status = 'ACTIVE') AS active_uat_user_count,
    (SELECT COUNT(*) FROM expected_roles) AS expected_role_bundle_count,
    (SELECT COUNT(*) FROM identity.roles r JOIN expected_roles er ON er.role_code = r.role_code WHERE r.role_status = 'ACTIVE') AS active_role_bundle_count,
    (SELECT COUNT(DISTINCT permission_code) FROM expected_role_permissions) AS expected_permission_count,
    (SELECT COUNT(*) FROM identity.permissions p WHERE p.permission_code IN (SELECT DISTINCT permission_code FROM expected_role_permissions) AND p.permission_status = 'ACTIVE') AS active_permission_count,
    (SELECT COALESCE(SUM(missing_binding), 0) FROM role_permission_state) AS missing_role_permission_count,
    (SELECT COALESCE(SUM(missing_assignment), 0) FROM user_role_state) AS missing_user_role_assignment_count,
    NOT EXISTS (
        SELECT 1 FROM active_role_permissions
        WHERE role_code = 'SYSTEM_RBAC_ADMINISTRATOR'
          AND permission_code IN ('statutory-discounts.decision.approve', 'statutory-discounts.payable-basis.apply', 'fiscal-issuance.void.command', 'reconciliation.manage')
    ) AS system_rbac_admin_lacks_business_mutation,
    NOT EXISTS (
        SELECT 1 FROM active_role_permissions
        WHERE role_code = 'EXECUTIVE_MANAGEMENT'
          AND (
              permission_code LIKE '%.manage'
              OR permission_code LIKE '%.command'
              OR permission_code LIKE '%.apply'
              OR permission_code LIKE '%.approve'
              OR permission_code LIKE '%.reject'
              OR permission_code IN ('statutory-discounts.draft.create', 'statutory-discounts.evidence.capture', 'fiscal-issuance.void.command', 'reports.export')
          )
    ) AS executive_management_is_read_only,
    EXISTS (SELECT 1 FROM active_role_permissions WHERE role_code = 'OPERATOR_SUPPORT_STAFF' AND permission_code = 'statutory-discounts.session.lookup')
      AND EXISTS (SELECT 1 FROM active_role_permissions WHERE role_code = 'OPERATOR_SUPPORT_STAFF' AND permission_code = 'statutory-discounts.draft.create')
      AND EXISTS (SELECT 1 FROM active_role_permissions WHERE role_code = 'OPERATOR_SUPPORT_STAFF' AND permission_code = 'statutory-discounts.evidence.capture')
      AND NOT EXISTS (SELECT 1 FROM active_role_permissions WHERE role_code = 'OPERATOR_SUPPORT_STAFF' AND permission_code IN ('statutory-discounts.decision.approve', 'statutory-discounts.decision.reject', 'statutory-discounts.payable-basis.apply')) AS operator_support_requester_only,
    EXISTS (SELECT 1 FROM active_role_permissions WHERE role_code = 'OPERATIONS_SUPERVISOR' AND permission_code = 'statutory-discounts.decision.approve')
      AND EXISTS (SELECT 1 FROM active_role_permissions WHERE role_code = 'OPERATIONS_SUPERVISOR' AND permission_code = 'statutory-discounts.payable-basis.apply')
      AND NOT EXISTS (SELECT 1 FROM active_role_permissions WHERE role_code = 'OPERATIONS_SUPERVISOR' AND permission_code IN ('user.manage', 'rbac.manage')) AS operations_supervisor_approves_without_rbac_admin,
    EXISTS (SELECT 1 FROM active_role_permissions WHERE role_code = 'COMPLIANCE_POLICY_ADMINISTRATOR' AND permission_code = 'policy-import.approve')
      AND EXISTS (SELECT 1 FROM active_role_permissions WHERE role_code = 'COMPLIANCE_POLICY_ADMINISTRATOR' AND permission_code = 'statutory-discounts.audit.read')
      AND NOT EXISTS (SELECT 1 FROM active_role_permissions WHERE role_code = 'COMPLIANCE_POLICY_ADMINISTRATOR' AND permission_code IN ('statutory-discounts.decision.approve', 'statutory-discounts.payable-basis.apply', 'fiscal-issuance.void.command')) AS compliance_policy_boundary_preserved,
    EXISTS (SELECT 1 FROM active_role_permissions WHERE role_code = 'FINANCE_RECONCILIATION_ANALYST' AND permission_code = 'reconciliation.view')
      AND EXISTS (SELECT 1 FROM active_role_permissions WHERE role_code = 'FINANCE_RECONCILIATION_ANALYST' AND permission_code = 'reports.view')
      AND NOT EXISTS (SELECT 1 FROM active_role_permissions WHERE role_code = 'FINANCE_RECONCILIATION_ANALYST' AND permission_code IN ('statutory-discounts.decision.approve', 'fiscal-issuance.void.command', 'user.manage')) AS finance_reconciliation_boundary_preserved,
    NOT EXISTS (
        SELECT 1 FROM active_role_permissions
        WHERE role_code = 'OPERATIONS_SUPERVISOR'
          AND permission_code LIKE 'policy-import.%'
    )
      AND NOT EXISTS (
        SELECT 1 FROM active_role_permissions
        WHERE role_code = 'COMPLIANCE_POLICY_ADMINISTRATOR'
          AND permission_code IN ('statutory-discounts.decision.approve', 'statutory-discounts.payable-basis.apply')
    ) AS policy_import_runtime_boundary_preserved,
    (SELECT requester_user_count FROM operator_console_fixture) AS requester_user_count,
    (SELECT reviewer_user_count FROM operator_console_fixture) AS reviewer_user_count,
    (SELECT site_group_count FROM operator_console_fixture) AS uat_site_group_count,
    (SELECT site_count FROM operator_console_fixture) AS uat_site_count,
    (SELECT device_binding_count FROM operator_console_fixture) AS uat_device_binding_count,
    (SELECT active_shift_count FROM operator_console_fixture) AS uat_active_shift_count;
