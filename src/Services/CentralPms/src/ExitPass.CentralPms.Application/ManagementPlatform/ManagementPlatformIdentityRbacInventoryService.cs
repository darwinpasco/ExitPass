using ExitPass.CentralPms.Application.Security;

namespace ExitPass.CentralPms.Application.ManagementPlatform;

public sealed class ManagementPlatformIdentityRbacInventoryService : IManagementPlatformIdentityRbacInventoryService
{
    private const string ImplementedStatus = "implemented";
    private const string TargetOnlyStatus = "target-only";
    private readonly IManagementPlatformIdentityRbacInventoryRepository _repository;

    public ManagementPlatformIdentityRbacInventoryService(IManagementPlatformIdentityRbacInventoryRepository repository)
    {
        _repository = repository;
    }

    public async Task<ManagementPlatformIdentityRbacInventory> GetInventoryAsync(CancellationToken cancellationToken)
    {
        var persistence = await _repository.ReadAsync(cancellationToken);
        var roleBundles = BuildRoleBundles();
        var policyMappings = BuildPolicyMappings();
        var permissions = BuildPermissions(policyMappings);
        var gaps = BuildGaps(persistence);

        return new ManagementPlatformIdentityRbacInventory(
            persistence.Users,
            roleBundles,
            permissions,
            policyMappings,
            persistence.UserRoleAssignments,
            persistence.UserSiteScopes,
            persistence.DeviceBindings,
            persistence.Shifts,
            gaps,
            DateTimeOffset.UtcNow);
    }

    private static IReadOnlyList<ManagementPlatformRoleBundle> BuildRoleBundles() =>
    [
        new(
            "system-rbac-administrator",
            "System / RBAC Administrator",
            "Owns identity and access administration, role/permission governance, assignments, and access audit visibility.",
            [
                "user.view",
                "user.manage",
                "rbac.view",
                "rbac.manage",
                "role.view",
                "role.manage",
                "permission.view",
                "permission.manage",
                "assignment.view",
                "assignment.manage",
                "access-audit.view"
            ],
            [
                "No automatic statutory discount approval.",
                "No automatic Sales Invoice void authority.",
                "Business workflow permissions must be separately granted."
            ],
            "ExitPass Management Platform -> Identity & RBAC Administration"),
        new(
            "platform-administrator",
            "Platform Administrator",
            "Manages operational platform configuration for sites, devices, POS Server assignments, connectors, readiness, and UAT setup.",
            [
                "site.view",
                "site.manage",
                "site-group.view",
                "site-group.manage",
                "device.view",
                "device.manage",
                "device-binding.view",
                "device-binding.manage",
                "shift.view",
                "shift.manage",
                "pos-server-config.view",
                "pos-server-config.manage",
                "connector-config.view",
                "connector-config.manage",
                "operational-monitoring.view"
            ],
            [
                "No user/RBAC administration unless separately granted.",
                "No statutory discount approval unless separately granted.",
                "No Sales Invoice void authority unless separately granted."
            ],
            "ExitPass Management Platform -> Central PMS Admin / Platform Configuration"),
        new(
            "operations-supervisor",
            "Operations Supervisor",
            "Supervises operational workflows and approves or rejects statutory privilege eligibility without applying payable-basis changes.",
            [
                "statutory-discounts.draft.view",
                "statutory-discounts.evidence.view",
                "statutory-discounts.evidence.review.view",
                "statutory-discounts.review.queue.read",
                "statutory-discounts.review.detail.read",
                "statutory-discounts.decision.review",
                "statutory-discounts.decision.approve",
                "statutory-discounts.decision.reject",
                "fiscal-issuance.status.read",
                "fiscal-issuance.void.command",
                "operator-workflow-audit.view"
            ],
            [
                "No user/RBAC administration.",
                "No global platform configuration administration.",
                "No payable-basis application authority.",
                "Requester cannot approve their own statutory discount."
            ],
            "Operator Console for workflow; Management Platform for supervisor reports"),
        new(
            "operator-support-staff",
            "Operator / Support Staff",
            "Performs site-scoped operational lookup and support workflows.",
            [
                "statutory-discounts.session.lookup",
                "statutory-discounts.draft.view",
                "statutory-discounts.draft.create",
                "statutory-discounts.evidence.view",
                "statutory-discounts.evidence.capture",
                "fiscal-issuance.status.read",
                "ticket.lookup"
            ],
            [
                "Cannot approve own statutory discount.",
                "No payable-basis apply unless separately granted.",
                "No Sales Invoice void unless separately granted.",
                "No admin/RBAC/configuration authority."
            ],
            "Operator Console"),
        new(
            "finance-reconciliation-analyst",
            "Finance / Reconciliation Analyst",
            "Reviews financial, payment, fiscal, discount, and reconciliation records.",
            [
                "reconciliation.view",
                "reconciliation.manage",
                "payment-report.view",
                "fiscal-report.view",
                "sales-invoice-report.view",
                "statutory-discount-report.view",
                "revenue-report.view",
                "variance-report.view",
                "reports.view",
                "reports.export"
            ],
            [
                "No operational statutory discount approval unless separately granted.",
                "No Sales Invoice void unless separately granted.",
                "No user/RBAC administration.",
                "No gate/ExitAuthorization authority."
            ],
            "ExitPass Management Platform -> Audit / Reconciliation and Management Dashboard & Reporting"),
        new(
            "compliance-policy-administrator",
            "Compliance / Policy Administrator",
            "Owns compliance oversight, policy governance, audit review, and policy lifecycle controls.",
            [
                "statutory-discounts.audit.read",
                "fiscal-issuance.void.audit.read",
                "fiscal-view-audit.read",
                "audit-report.view",
                "policy-import.submit",
                "policy-import.review",
                "policy-import.approve",
                "policy-import.manage",
                "statutory-discount-policy.view",
                "statutory-discount-policy.manage",
                "evidence-rule-policy.view",
                "evidence-rule-policy.manage"
            ],
            [
                "No automatic operator workflow mutation unless separately granted.",
                "No automatic user/RBAC administration.",
                "No payment/gate/fiscal issuance authority.",
                "No Sales Invoice void command unless separately granted."
            ],
            "ExitPass Management Platform -> Policy Administration and Audit / Compliance"),
        new(
            "executive-management",
            "Executive / Management",
            "Provides read-only management visibility for dashboards, KPIs, reports, and operational performance.",
            [
                "dashboard.view",
                "reports.view",
                "executive-summary.view",
                "site-performance.view",
                "site-group-performance.view",
                "revenue-summary.view",
                "payment-summary.view",
                "fiscal-summary.view",
                "statutory-discount-summary.view",
                "exception-trend.view",
                "operational-monitoring.view"
            ],
            [
                "Read-only by default.",
                "No operator workflow mutation.",
                "No statutory discount approval.",
                "No Sales Invoice void.",
                "No user/RBAC/platform configuration administration."
            ],
            "ExitPass Management Platform -> Management Dashboard & Reporting")
    ];

    private static IReadOnlyList<ManagementPlatformPolicyMapping> BuildPolicyMappings() =>
        CentralPmsRbacPolicyCatalog.ListPolicyMappings()
            .Select(mapping => new ManagementPlatformPolicyMapping(
                mapping.Key,
                mapping.Value,
                ResolveFeatureArea(mapping.Key),
                ImplementedStatus,
                null))
            .OrderBy(mapping => mapping.PolicyName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<ManagementPlatformPermission> BuildPermissions(
        IReadOnlyList<ManagementPlatformPolicyMapping> policyMappings)
    {
        var policiesByPermission = policyMappings
            .SelectMany(mapping => mapping.Permissions.Select(permission => new { permission, mapping.PolicyName }))
            .GroupBy(item => item.permission, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(item => item.PolicyName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var targetPermissions = TargetPermissions()
            .ToDictionary(permission => permission.PermissionKey, StringComparer.OrdinalIgnoreCase);

        var allPermissionKeys = targetPermissions.Keys
            .Union(policiesByPermission.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return allPermissionKeys
            .Select(key =>
            {
                targetPermissions.TryGetValue(key, out var target);
                policiesByPermission.TryGetValue(key, out var mappedPolicies);
                mappedPolicies ??= Array.Empty<string>();
                var hasTarget = targetPermissions.ContainsKey(key);

                return new ManagementPlatformPermission(
                    key,
                    hasTarget ? target.DisplayLabel : ToDisplayLabel(key),
                    hasTarget ? target.Category : ResolvePermissionCategory(key),
                    mappedPolicies.Count > 0
                        ? "CentralPmsRbacPolicyCatalog"
                        : "ManagementPlatformTargetRoleModel",
                    mappedPolicies,
                    mappedPolicies.Count > 0 ? ImplementedStatus : TargetOnlyStatus,
                    mappedPolicies.Count > 0
                        ? null
                        : "Target access right from the v1.3 Management Platform role model; no current Central PMS policy mapping was found.");
            })
            .ToArray();
    }

    private static IReadOnlyList<ManagementPlatformInventoryGap> BuildGaps(
        ManagementPlatformIdentityRbacPersistenceInventory persistence)
    {
        var gaps = new List<ManagementPlatformInventoryGap>(persistence.Gaps)
        {
            new(
                "management-platform-ui-missing",
                "Medium",
                "The ExitPass Management Platform Identity & RBAC Administration UI is not implemented in this slice."),
            new(
                "admin-mutation-apis-missing-by-design",
                "Low",
                "This inventory slice is read-only; user, role, permission, and assignment mutation APIs remain future work."),
            new(
                "external-iam-mapping-not-confirmed",
                "Medium",
                "No external IAM synchronization or identity-provider mapping was confirmed from current source inspection."),
            new(
                "admin-audit-events-for-rbac-mutation-not-confirmed",
                "Medium",
                "Admin audit events for future user, role, permission, and assignment mutations are not confirmed because mutation APIs are not yet implemented.")
        };

        if (persistence.UserRoleAssignments.Count == 0)
        {
            gaps.Add(new ManagementPlatformInventoryGap(
                "persisted-role-assignments-not-confirmed",
                "Medium",
                "No persisted user-role assignments were found by the read model; local/dev header permissions may still be used for UAT workflows."));
        }

        return gaps
            .GroupBy(gap => gap.GapKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(gap => gap.GapKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<(string PermissionKey, string DisplayLabel, string Category)> TargetPermissions() =>
    [
        ("statutory-discounts.session.lookup", "Statutory discount session lookup", "Statutory discount"),
        ("statutory-discounts.draft.view", "Statutory discount draft view", "Statutory discount"),
        ("statutory-discounts.draft.create", "Statutory discount draft create", "Statutory discount"),
        ("statutory-discounts.evidence.view", "Statutory discount evidence view", "Statutory discount"),
        ("statutory-discounts.evidence.review.view", "Statutory evidence review preview", "Statutory discount"),
        ("statutory-discounts.evidence.capture", "Statutory discount evidence capture", "Statutory discount"),
        ("statutory-discounts.review.queue.read", "Statutory discount review queue read", "Statutory discount"),
        ("statutory-discounts.review.detail.read", "Statutory discount review detail read", "Statutory discount"),
        ("statutory-discounts.decision.review", "Statutory discount decision review", "Statutory discount"),
        ("statutory-discounts.decision.approve", "Statutory discount decision approve", "Statutory discount"),
        ("statutory-discounts.decision.reject", "Statutory discount decision reject", "Statutory discount"),
        ("statutory-discounts.payable-basis.apply", "Statutory discount payment-time payable-basis apply", "Statutory discount"),
        ("statutory-discounts.application.read", "Statutory discount application status read", "Statutory discount"),
        ("statutory-discounts.policy.resolve", "Statutory discount policy resolve", "Statutory discount"),
        ("statutory-discounts.ordinance-availability.read.apt", "APT statutory ordinance availability read", "Statutory discount"),
        ("statutory-discounts.audit.read", "Statutory discount audit read", "Statutory discount"),
        ("fiscal-issuance.status.read", "Sales Invoice status read", "Fiscal / Sales Invoice"),
        ("fiscal-issuance.void.command", "Sales Invoice void command", "Fiscal / Sales Invoice"),
        ("fiscal-issuance.void.audit.read", "Sales Invoice void audit read", "Fiscal / Sales Invoice"),
        ("fiscal-view-audit.read", "Sales Invoice view audit read", "Fiscal / Sales Invoice"),
        ("sales-invoice-report.view", "Sales Invoice report view", "Fiscal / Sales Invoice"),
        ("policy-import.submit", "Policy import submit", "Policy"),
        ("policy-import.review", "Policy import review", "Policy"),
        ("policy-import.approve", "Policy import approve", "Policy"),
        ("policy-import.manage", "Policy import manage", "Policy"),
        ("statutory-discount-policy.view", "Statutory discount policy view", "Policy"),
        ("statutory-discount-policy.manage", "Statutory discount policy manage", "Policy"),
        ("evidence-rule-policy.view", "Evidence rule policy view", "Policy"),
        ("evidence-rule-policy.manage", "Evidence rule policy manage", "Policy"),
        ("reconciliation.view", "Reconciliation view", "Audit / reconciliation / reporting"),
        ("reconciliation.manage", "Reconciliation manage", "Audit / reconciliation / reporting"),
        ("audit-report.view", "Audit report view", "Audit / reconciliation / reporting"),
        ("reports.view", "Reports view", "Audit / reconciliation / reporting"),
        ("reports.export", "Reports export", "Audit / reconciliation / reporting"),
        ("dashboard.view", "Dashboard view", "Audit / reconciliation / reporting"),
        ("executive-summary.view", "Executive summary view", "Audit / reconciliation / reporting"),
        ("management-platform.identity-rbac.inventory.read", "Identity/RBAC inventory read", "Administration"),
        ("user.view", "User view", "Administration"),
        ("user.manage", "User manage", "Administration"),
        ("rbac.view", "RBAC view", "Administration"),
        ("rbac.manage", "RBAC manage", "Administration"),
        ("role.view", "Role view", "Administration"),
        ("role.manage", "Role manage", "Administration"),
        ("permission.view", "Permission view", "Administration"),
        ("permission.manage", "Permission manage", "Administration"),
        ("assignment.view", "Assignment view", "Administration"),
        ("assignment.manage", "Assignment manage", "Administration"),
        ("site.view", "Site view", "Administration"),
        ("site.manage", "Site manage", "Administration"),
        ("site-group.view", "Site group view", "Administration"),
        ("site-group.manage", "Site group manage", "Administration"),
        ("device.view", "Device view", "Administration"),
        ("device.manage", "Device manage", "Administration"),
        ("device-binding.view", "Device binding view", "Administration"),
        ("device-binding.manage", "Device binding manage", "Administration"),
        ("shift.view", "Shift view", "Administration"),
        ("shift.manage", "Shift manage", "Administration"),
        ("pos-server-config.view", "POS Server config view", "Administration"),
        ("pos-server-config.manage", "POS Server config manage", "Administration"),
        ("connector-config.view", "Connector config view", "Administration"),
        ("connector-config.manage", "Connector config manage", "Administration"),
        ("platform-config.view", "Platform config view", "Administration"),
        ("platform-config.manage", "Platform config manage", "Administration"),
        ("projection-health.view", "Projection health view", "Operational monitoring"),
        ("vendor-acknowledgments.view", "Vendor acknowledgments view", "Operational monitoring"),
        ("operational-monitoring.view", "Operational monitoring view", "Operational monitoring")
    ];

    private static string ResolveFeatureArea(string policyName)
    {
        if (policyName.Contains("StatutoryDiscount", StringComparison.OrdinalIgnoreCase))
        {
            return "Statutory discount";
        }

        if (policyName.Contains("Fiscal", StringComparison.OrdinalIgnoreCase))
        {
            return "Fiscal / Sales Invoice";
        }

        if (policyName.Contains("PolicyImport", StringComparison.OrdinalIgnoreCase))
        {
            return "Policy import review";
        }

        if (policyName.Contains("VendorSessionProjection", StringComparison.OrdinalIgnoreCase) ||
            policyName.Contains("VendorPaymentAcknowledgment", StringComparison.OrdinalIgnoreCase))
        {
            return "Operational monitoring";
        }

        if (policyName.Contains("ManagementPlatform", StringComparison.OrdinalIgnoreCase))
        {
            return "Management Platform administration";
        }

        if (policyName.Contains("Reconciliation", StringComparison.OrdinalIgnoreCase) ||
            policyName.Contains("Mops", StringComparison.OrdinalIgnoreCase))
        {
            return "Audit / reconciliation / reporting";
        }

        if (policyName.Contains("Event", StringComparison.OrdinalIgnoreCase))
        {
            return "Platform operations";
        }

        return "Central PMS";
    }

    private static string ResolvePermissionCategory(string permissionKey)
    {
        if (permissionKey.StartsWith("statutory-discounts.", StringComparison.OrdinalIgnoreCase))
        {
            return "Statutory discount";
        }

        if (permissionKey.StartsWith("fiscal-", StringComparison.OrdinalIgnoreCase) ||
            permissionKey.Contains("sales-invoice", StringComparison.OrdinalIgnoreCase))
        {
            return "Fiscal / Sales Invoice";
        }

        if (permissionKey.Contains("policy-import", StringComparison.OrdinalIgnoreCase) ||
            permissionKey.Contains("policy", StringComparison.OrdinalIgnoreCase))
        {
            return "Policy";
        }

        if (permissionKey.Contains("reconciliation", StringComparison.OrdinalIgnoreCase) ||
            permissionKey.Contains("report", StringComparison.OrdinalIgnoreCase) ||
            permissionKey.Contains("dashboard", StringComparison.OrdinalIgnoreCase))
        {
            return "Audit / reconciliation / reporting";
        }

        if (permissionKey.Contains("projection", StringComparison.OrdinalIgnoreCase) ||
            permissionKey.Contains("monitoring", StringComparison.OrdinalIgnoreCase) ||
            permissionKey.Contains("acknowledgment", StringComparison.OrdinalIgnoreCase))
        {
            return "Operational monitoring";
        }

        if (permissionKey.StartsWith("user.", StringComparison.OrdinalIgnoreCase) ||
            permissionKey.StartsWith("rbac.", StringComparison.OrdinalIgnoreCase) ||
            permissionKey.StartsWith("role.", StringComparison.OrdinalIgnoreCase) ||
            permissionKey.StartsWith("permission.", StringComparison.OrdinalIgnoreCase) ||
            permissionKey.StartsWith("assignment.", StringComparison.OrdinalIgnoreCase) ||
            permissionKey.StartsWith("management-platform.", StringComparison.OrdinalIgnoreCase))
        {
            return "Administration";
        }

        return "Central PMS";
    }

    private static string ToDisplayLabel(string permissionKey) =>
        permissionKey
            .Replace("-", " ", StringComparison.Ordinal)
            .Replace(".", " ", StringComparison.Ordinal);
}
