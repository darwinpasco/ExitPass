import { fiscalReportingPermissions } from "./fiscalReporting";

export const routes = {
  root: "/operator-console",
  ticketLookup: "/operator-console/ticket-lookup",
  fiscalStatus: "/operator-console/fiscal-issuance-status",
  queue: "/operator-console/statutory-discounts",
  detail: "/operator-console/statutory-discounts/",
  shiftManagement: "/operator-console/shift-management",
  fiscalReporting: "/operator-console/fiscal-reporting",
  audit: "/operator-console/audit",
  fiscalStatusViewAudit: "/operator-console/audit/fiscal-status-views",
  fiscalVoidActionAudit: "/operator-console/audit/fiscal-void-actions",
  vendorAcknowledgments: "/operator-console/vendor-acknowledgments",
  vendorProjectionHealth: "/operator-console/vendor-session-projections/health",
  policyImportReview: "/operator-console/production-policy-import-review"
} as const;

export type OperatorConsoleNavigationItem = {
  route: string;
  label: string;
  requiredAnyPermissions: readonly string[];
  requiredAnyRoles?: readonly string[];
  matches?: (path: string) => boolean;
};

const statutoryWorkflowPermissions = [
  "statutory-discounts.session.lookup",
  "statutory-discounts.draft.view",
  "statutory-discounts.draft.create",
  "statutory-discounts.evidence.view",
  "statutory-discounts.evidence.capture",
  "statutory-discounts.policy.resolve",
  "statutory-discounts.review.queue.read",
  "statutory-discounts.review.detail.read",
  "statutory-discounts.decision.review",
  "statutory-discounts.decision.approve",
  "statutory-discounts.decision.reject"
] as const;

export const statutorySupervisorPermissions = [
  "statutory-discounts.review.queue.read",
  "statutory-discounts.review.detail.read",
  "statutory-discounts.decision.review"
] as const;

export const operatorConsoleNavigation: readonly OperatorConsoleNavigationItem[] = [
  {
    route: routes.ticketLookup,
    label: "Session Lookup",
    requiredAnyPermissions: ["ticket.lookup", "statutory-discounts.session.lookup"]
  },
  {
    route: routes.fiscalStatus,
    label: "Fiscal Status",
    requiredAnyPermissions: ["fiscal-issuance.status.read"],
    requiredAnyRoles: ["OPERATIONS_SUPERVISOR"]
  },
  {
    route: routes.queue,
    label: "Work Queue",
    requiredAnyPermissions: statutoryWorkflowPermissions,
    matches: (path) => path === routes.queue || path.startsWith(routes.detail)
  },
  {
    route: routes.shiftManagement,
    label: "Shift Management",
    requiredAnyPermissions: ["shift-management.view", "shift-management.manage"]
  },
  {
    route: routes.fiscalReporting,
    label: "Fiscal Reporting",
    requiredAnyPermissions: Object.values(fiscalReportingPermissions),
    requiredAnyRoles: ["OPERATIONS_SUPERVISOR"]
  },
  {
    route: routes.audit,
    label: "Audit / Reporting",
    requiredAnyPermissions: ["statutory-discounts.audit.read"]
  },
  {
    route: routes.fiscalStatusViewAudit,
    label: "Fiscal View Audit",
    requiredAnyPermissions: ["fiscal-view-audit.read"]
  },
  {
    route: routes.fiscalVoidActionAudit,
    label: "Sales Invoice Void Audit",
    requiredAnyPermissions: ["fiscal-issuance.void.audit.read"]
  },
  {
    route: routes.vendorAcknowledgments,
    label: "Vendor Acknowledgments",
    requiredAnyPermissions: ["vendor-acknowledgments.view"],
    requiredAnyRoles: ["OPERATIONS_SUPERVISOR"]
  },
  {
    route: routes.vendorProjectionHealth,
    label: "Projection Health",
    requiredAnyPermissions: [
      "projection-health.view",
      "ops.vendor-session-projection-health.view",
      "operator-console.vendor-projection-health.view"
    ],
    requiredAnyRoles: ["SYSTEM_ADMINISTRATOR"]
  },
  {
    route: routes.policyImportReview,
    label: "Policy Import Review",
    requiredAnyPermissions: [
      "operator-console.policy-import-review.submit",
      "operator-console.policy-import-review.view-own",
      "operator-console.policy-import-review.review",
      "operator-console.policy-import-review.manage",
      "operator-console.policy-import-review.approve.legal",
      "operator-console.policy-import-review.approve.ops",
      "operator-console.policy-import-review.approve.qa",
      "operator-console.policy-import-review.approve.db"
    ]
  }
];

export function resolveOperatorConsolePath(path: string): string {
  return path === "" || path === "/" || path === routes.root || path === `${routes.root}/`
    ? routes.ticketLookup
    : path;
}

export function hasAnyPermission(
  permissions: readonly string[],
  requiredAnyPermissions: readonly string[]
): boolean {
  if (requiredAnyPermissions.length === 0) return true;
  const available = new Set(permissions);
  return requiredAnyPermissions.some((permission) => available.has(permission));
}

export function visibleOperatorConsoleNavigation(
  permissions: readonly string[],
  roleCodes: readonly string[] = []
): readonly OperatorConsoleNavigationItem[] {
  return operatorConsoleNavigation.filter((item) =>
    hasAnyPermission(permissions, item.requiredAnyPermissions) && hasRequiredRole(roleCodes, item.requiredAnyRoles)
  );
}

export function canAccessOperatorConsolePath(
  path: string,
  permissions: readonly string[],
  roleCodes: readonly string[] = []
): boolean {
  const item = operatorConsoleNavigation.find((candidate) =>
    candidate.matches ? candidate.matches(path) : candidate.route === path
  );
  return item
    ? hasAnyPermission(permissions, item.requiredAnyPermissions) && hasRequiredRole(roleCodes, item.requiredAnyRoles)
    : true;
}

function hasRequiredRole(roleCodes: readonly string[], requiredAnyRoles?: readonly string[]): boolean {
  if (!requiredAnyRoles?.length) return true;
  const available = new Set(roleCodes);
  return requiredAnyRoles.some((roleCode) => available.has(roleCode));
}

export function hasStatutorySupervisorWorkspace(permissions: readonly string[]): boolean {
  return hasAnyPermission(permissions, statutorySupervisorPermissions);
}
