import { describe, expect, it } from "vitest";
import {
  canAccessOperatorConsolePath,
  hasStatutorySupervisorWorkspace,
  resolveOperatorConsolePath,
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
      "Session Lookup",
      "Work Queue"
    ]);
  });

  it("resolves every Operator Console root form to the canonical Session Lookup route", () => {
    expect(resolveOperatorConsolePath("")).toBe(routes.ticketLookup);
    expect(resolveOperatorConsolePath("/")).toBe(routes.ticketLookup);
    expect(resolveOperatorConsolePath(routes.root)).toBe(routes.ticketLookup);
    expect(resolveOperatorConsolePath(`${routes.root}/`)).toBe(routes.ticketLookup);
    expect(resolveOperatorConsolePath(routes.queue)).toBe(routes.queue);
    expect(visibleOperatorConsoleNavigation(siteOperatorPermissions).map((item) => item.label)).not.toContain("Overview");
  });

  it("does not expose fiscal reporting to a Site Operator even when stale fiscal permissions remain", () => {
    const staleSiteOperatorFiscalPermissions = [
      ...siteOperatorPermissions,
      "fiscal-reporting.ej.read",
      "fiscal-reporting.x.read",
      "fiscal-reporting.z.read"
    ];

    expect(visibleOperatorConsoleNavigation(staleSiteOperatorFiscalPermissions, ["SITE_OPERATOR"])
      .map((item) => item.label)).not.toContain("Fiscal Reporting");
    expect(canAccessOperatorConsolePath(
      routes.fiscalReporting,
      staleSiteOperatorFiscalPermissions,
      ["SITE_OPERATOR"]
    )).toBe(false);
  });

  it("exposes fiscal status and reporting only to an Operations Supervisor with permissions", () => {
    const permissions = [
      "fiscal-issuance.status.read",
      "fiscal-reporting.ej.read",
      "fiscal-reporting.x.read",
      "fiscal-reporting.x.generate",
      "fiscal-reporting.z.read"
    ];
    const navigation = visibleOperatorConsoleNavigation(permissions, ["OPERATIONS_SUPERVISOR"])
      .map((item) => item.label);

    expect(navigation).toContain("Fiscal Status");
    expect(navigation).toContain("Fiscal Reporting");
    expect(canAccessOperatorConsolePath(routes.fiscalReporting, permissions, ["OPERATIONS_SUPERVISOR"])).toBe(true);
  });

  it("shows Vendor Acknowledgements only to an authorized Operations Supervisor", () => {
    expect(visibleOperatorConsoleNavigation(
      ["vendor-acknowledgments.view"],
      ["OPERATIONS_SUPERVISOR"]
    ).map((item) => item.label)).toContain("Vendor Acknowledgments");
    expect(visibleOperatorConsoleNavigation(
      ["vendor-acknowledgments.view"],
      ["SITE_OPERATOR"]
    ).map((item) => item.label)).not.toContain("Vendor Acknowledgments");
  });

  it("keeps Projection Health in the technical support and administrator audience", () => {
    expect(visibleOperatorConsoleNavigation(["projection-health.view"], ["SYSTEM_ADMINISTRATOR"])
      .map((item) => item.label)).toContain("Projection Health");
    expect(visibleOperatorConsoleNavigation(
      ["projection-health.view"],
      ["OPERATIONS_SUPERVISOR"]
    ).map((item) => item.label)).not.toContain("Projection Health");
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
