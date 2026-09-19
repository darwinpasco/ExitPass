import { fiscalReportingPermissions } from "./fiscalReporting";

export const routes = {
  home: "/operator-console",
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
  { route: routes.home, label: "Overview", requiredAnyPermissions: [] },
  {
    route: routes.ticketLookup,
    label: "Ticket Lookup",
    requiredAnyPermissions: ["ticket.lookup", "statutory-discounts.session.lookup"]
  },
  {
    route: routes.fiscalStatus,
    label: "Fiscal Status",
    requiredAnyPermissions: ["fiscal-issuance.status.read"]
  },
  {
    route: routes.queue,
    label: "Statutory Discounts",
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
    label: "Fiscal Reporting / EJ / X / Z",
    requiredAnyPermissions: Object.values(fiscalReportingPermissions)
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
    requiredAnyPermissions: ["vendor-acknowledgments.view"]
  },
  {
    route: routes.vendorProjectionHealth,
    label: "Projection Health",
    requiredAnyPermissions: [
      "projection-health.view",
      "ops.vendor-session-projection-health.view",
      "operator-console.vendor-projection-health.view"
    ]
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

export function hasAnyPermission(
  permissions: readonly string[],
  requiredAnyPermissions: readonly string[]
): boolean {
  if (requiredAnyPermissions.length === 0) return true;
  const available = new Set(permissions);
  return requiredAnyPermissions.some((permission) => available.has(permission));
}

export function visibleOperatorConsoleNavigation(
  permissions: readonly string[]
): readonly OperatorConsoleNavigationItem[] {
  return operatorConsoleNavigation.filter((item) => hasAnyPermission(permissions, item.requiredAnyPermissions));
}

export function canAccessOperatorConsolePath(path: string, permissions: readonly string[]): boolean {
  const item = operatorConsoleNavigation.find((candidate) =>
    candidate.matches ? candidate.matches(path) : candidate.route === path
  );
  return item ? hasAnyPermission(permissions, item.requiredAnyPermissions) : true;
}

export function hasStatutorySupervisorWorkspace(permissions: readonly string[]): boolean {
  return hasAnyPermission(permissions, statutorySupervisorPermissions);
}
