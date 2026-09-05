import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import type { OperatorConsoleApiClient } from "./apiClient";
import { ShiftManagement } from "./ShiftManagement";
import type { OperationalShift, ShiftAuthorizedSite } from "./types";

const shift: OperationalShift = {
  shiftId: "61000000-0000-0000-0000-000000000001",
  shiftReference: "SHIFT-6100",
  operatorUserId: "61000000-0000-0000-0000-000000000002",
  username: "cashier",
  displayName: "Test Cashier",
  userType: "SITE_OPERATOR",
  roles: ["SITE_OPERATOR"],
  siteId: "61000000-0000-0000-0000-000000000003",
  siteGroupId: "61000000-0000-0000-0000-000000000004",
  siteCode: "PITX-L3",
  siteName: "PITX Level 3",
  siteGroupCode: "PITX",
  siteGroupName: "PITX",
  openedAt: "2026-09-05T04:00:00Z",
  elapsedSeconds: 300,
  status: "ACTIVE",
  cashCustodyStatus: "OPEN",
  openingCashMinorUnits: 150000,
  cashTransactionCount: 2,
  cashCollectedMinorUnits: 5000,
  createdAt: "2026-09-05T04:00:00Z",
  updatedAt: "2026-09-05T04:00:00Z"
};

describe("ShiftManagement", () => {
  it("shows an authenticated no-Site state without offering shift start", async () => {
    render(<ShiftManagement client={client({ sites: [], shifts: [] })} />);
    expect(await screen.findByText("No authorized Sites are available.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Start Shift" })).not.toBeInTheDocument();
  });

  it("lists Site-scoped open shifts and displays custody and authoritative cash totals", async () => {
    render(<ShiftManagement client={client({ shifts: [shift] })} />);
    await userEvent.click((await screen.findAllByText("Test Cashier")).find(element => element.tagName === "STRONG")!);
    expect(screen.getByText("Shift Detail")).toBeInTheDocument();
    expect(screen.getAllByText("OPEN").length).toBeGreaterThan(0);
    expect(screen.getByRole("cell", { name: /2 transactions/ })).toBeInTheDocument();
    expect(screen.getAllByText(/50\.00/).length).toBeGreaterThan(0);
  });

  it("blocks own and supervisor close controls while custody is open", async () => {
    render(<ShiftManagement client={client({ shifts: [shift], own: shift, manage: true })} />);
    const ownClose = await screen.findByRole("button", { name: "Close Shift" });
    expect(ownClose).toBeDisabled();
    await userEvent.click(screen.getAllByText("Test Cashier").find(element => element.tagName === "STRONG")!);
    await userEvent.type(screen.getByLabelText("Exception close reason"), "Abandoned terminal");
    const closeButtons = screen.getAllByRole("button", { name: "Close Shift" });
    expect(closeButtons.every(button => button.hasAttribute("disabled"))).toBe(true);
    expect(screen.getAllByText("Close the cash custody session before closing this shift.").length).toBeGreaterThan(0);
  });

  it("starts a shift only at the Site selected from current authorized Sites", async () => {
    const secondSite = { ...authorizedSite(), siteId: "61000000-0000-0000-0000-000000000099", siteName: "PITX Level 2" };
    const api = client({ sites: [authorizedSite(), secondSite] });
    render(<ShiftManagement client={api} />);

    await userEvent.selectOptions(await screen.findByLabelText("Site"), secondSite.siteId);
    await userEvent.click(screen.getByRole("button", { name: "Start Shift" }));
    expect(api.startOwnShift).toHaveBeenCalledWith(secondSite.siteId);
  });

  it("shows a controlled not-authorized state without loading shift data", async () => {
    const api = client({ view: false });
    render(<ShiftManagement client={api} />);

    expect(await screen.findByText("Not authorized")).toBeInTheDocument();
    expect(api.listShifts).not.toHaveBeenCalled();
  });

  it("shows own recently closed history without exposing the staff filter", async () => {
    const closed = {
      ...shift,
      status: "ENDED",
      cashCustodyStatus: "NONE",
      closedAt: "2026-09-05T05:00:00Z",
      closeType: "NORMAL",
      closingActorName: "Test Cashier"
    };
    const api = client({ shifts: [closed], allShifts: false });
    render(<ShiftManagement client={api} />);

    await userEvent.click(await screen.findByRole("button", { name: "Recently Closed" }));

    await waitFor(() => expect(api.listShifts).toHaveBeenLastCalledWith("recently-closed", undefined, undefined));
    expect(screen.queryByLabelText("Staff member")).not.toBeInTheDocument();
    await userEvent.click(screen.getByText("Test Cashier"));
    const detail = screen.getByLabelText("Shift detail");
    expect(within(detail).getByText("ENDED")).toBeInTheDocument();
    expect(within(detail).getByText("NORMAL")).toBeInTheDocument();
  });

  it("preserves the all-staff filter for supervisor viewers", async () => {
    render(<ShiftManagement client={client({ allShifts: true })} />);
    expect(await screen.findByLabelText("Staff member")).toBeInTheDocument();
  });
});

function authorizedSite(): ShiftAuthorizedSite {
  return {
    siteId: shift.siteId,
    siteGroupId: shift.siteGroupId,
    siteCode: shift.siteCode,
    siteName: shift.siteName,
    siteGroupCode: shift.siteGroupCode,
    siteGroupName: shift.siteGroupName
  };
}

function client(options: { sites?: ShiftAuthorizedSite[]; shifts?: OperationalShift[]; own?: OperationalShift | null; manage?: boolean; view?: boolean; allShifts?: boolean } = {}) {
  const sites = options.sites ?? [authorizedSite()];
  return {
    listShiftAuthorizedSites: vi.fn().mockResolvedValue(sites),
    getCurrentOwnShift: vi.fn().mockResolvedValue(options.own ?? null),
    listShifts: vi.fn().mockResolvedValue({ items: options.shifts ?? [], correlationId: crypto.randomUUID() }),
    startOwnShift: vi.fn(),
    closeOwnShift: vi.fn(),
    exceptionCloseShift: vi.fn(),
    canViewShiftManagement: () => options.view ?? true,
    canViewAllShifts: () => options.allShifts ?? true,
    canManageShifts: () => options.manage ?? false
  } as unknown as OperatorConsoleApiClient;
}
