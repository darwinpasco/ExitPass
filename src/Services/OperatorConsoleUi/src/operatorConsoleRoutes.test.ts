import { describe, expect, it } from "vitest";
import {
  canAccessOperatorConsolePath,
  hasStatutorySupervisorWorkspace,
  routes,
  visibleOperatorConsoleNavigation
} from "./operatorConsoleRoutes";

const siteOperatorPermissions = [
  "ticket.lookup",
  "fiscal-issuance.status.read",
  "statutory-discounts.session.lookup",
  "statutory-discounts.draft.view",
  "statutory-discounts.draft.create",
  "statutory-discounts.evidence.view",
  "statutory-discounts.evidence.capture",
  "statutory-discounts.policy.resolve",
  "cashier-shifts.operate"
];

describe("Operator Console permission-driven routes", () => {
  it("shows exactly the approved Site Operator navigation surface", () => {
    expect(visibleOperatorConsoleNavigation(siteOperatorPermissions).map((item) => item.label)).toEqual([
      "Overview",
      "Ticket Lookup",
      "Fiscal Status",
      "Statutory Discounts"
    ]);
  });

  it("denies protected direct routes that are absent from the permission set", () => {
    expect(canAccessOperatorConsolePath(routes.audit, siteOperatorPermissions)).toBe(false);
    expect(canAccessOperatorConsolePath(routes.fiscalReporting, siteOperatorPermissions)).toBe(false);
    expect(canAccessOperatorConsolePath(routes.shiftManagement, siteOperatorPermissions)).toBe(false);
  });

  it("selects the supervisor workspace only from supervisory review permissions", () => {
    expect(hasStatutorySupervisorWorkspace(siteOperatorPermissions)).toBe(false);
    expect(hasStatutorySupervisorWorkspace([
      ...siteOperatorPermissions,
      "statutory-discounts.review.queue.read"
    ])).toBe(true);
  });
});
