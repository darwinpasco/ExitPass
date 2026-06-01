import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { App } from "./App";
import {
  createMockOperatorConsoleApiClient,
  mapApiError,
  type OperatorConsoleApiClient
} from "./apiClient";
import type { StatutoryDiscountQueueItem } from "./types";

const firstDraftId = "47000000-0000-0000-0000-000000000008";
const verifiedLocalDraftId = "47000000-0000-0000-0000-000000000009";
const blockedLocalDraftId = "47000000-0000-0000-0000-000000000010";

describe("ExitPass Operator Console statutory discount foundation", () => {
  it("OperatorConsole_RendersShellAndRoutes", async () => {
    render(<App apiClient={createMockOperatorConsoleApiClient()} initialPath="/operator-console" />);

    expect(screen.getByRole("heading", { name: "ExitPass Operator Console" })).toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "Operator Console routes" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: /statutory discount validation foundation/i })).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /open work queue/i }));

    expect(await screen.findByRole("heading", { name: "Work queue" })).toBeInTheDocument();
    expect(await screen.findByText("STAT-OP-SESSION-0001")).toBeInTheDocument();
  });

  it("StatutoryDiscountQueue_RendersLoadingEmptyAndDataStates", async () => {
    let resolveQueue: (items: StatutoryDiscountQueueItem[]) => void = () => undefined;
    const apiClient: OperatorConsoleApiClient = {
      listStatutoryDiscountDrafts: vi.fn(
        () =>
          new Promise<StatutoryDiscountQueueItem[]>((resolve) => {
            resolveQueue = resolve;
          })
      ),
      getStatutoryDiscountDraft: vi.fn()
    };

    const { rerender } = render(
      <App apiClient={apiClient} initialPath="/operator-console/statutory-discounts" />
    );

    expect(screen.getByText("Loading queue")).toBeInTheDocument();

    resolveQueue([]);
    expect(await screen.findByText("No drafts")).toBeInTheDocument();

    rerender(
      <App
        apiClient={createMockOperatorConsoleApiClient()}
        initialPath="/operator-console/statutory-discounts"
      />
    );

    expect(await screen.findByText("STAT-OP-SESSION-0001")).toBeInTheDocument();
    expect(screen.getByText("ABC 1234")).toBeInTheDocument();
    expect(screen.getByText("RA 9994 / RA 10754 national fallback")).toBeInTheDocument();
  });

  it("StatutoryDiscountQueue_ViewActionNavigatesToDraftDetail", async () => {
    render(<App apiClient={createMockOperatorConsoleApiClient()} initialPath="/operator-console/statutory-discounts" />);

    await userEvent.click(await screen.findByRole("button", { name: /view STAT-OP-SESSION-0001/i }));

    expect(await screen.findByRole("heading", { name: "STAT-OP-SESSION-0001" })).toBeInTheDocument();
    expect(screen.getByText(firstDraftId)).toBeInTheDocument();
    expect(screen.getByText("Policy context")).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "National fallback policy" })).toBeInTheDocument();
  });

  it("StatutoryDiscountDetail_RendersNationalFallbackPolicyContext", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient()}
        initialPath={`/operator-console/statutory-discounts/${firstDraftId}`}
      />
    );

    expect(await screen.findByRole("heading", { name: "National fallback policy" })).toBeInTheDocument();
    expect(screen.getByText("Republic Act No. 9994")).toBeInTheDocument();
    expect(screen.getAllByText("RA 9994").length).toBeGreaterThan(0);
    expect(screen.getByText(/no verified local ordinance overrides it/i)).toBeInTheDocument();
  });

  it("StatutoryDiscountDetail_RendersVerifiedLocalPolicyContext", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient()}
        initialPath={`/operator-console/statutory-discounts/${verifiedLocalDraftId}`}
      />
    );

    expect(await screen.findByRole("heading", { name: "Verified local policy" })).toBeInTheDocument();
    expect(screen.getByText("QC Ordinance 2026-04")).toBeInTheDocument();
    expect(screen.getByText("RA 10754")).toBeInTheDocument();
    expect(screen.getByText(/evidence required before decision/i)).toBeInTheDocument();
  });

  it("StatutoryDiscountDetail_RendersBlockedUnverifiedLocalPolicyState", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient()}
        initialPath={`/operator-console/statutory-discounts/${blockedLocalDraftId}`}
      />
    );

    expect(await screen.findByRole("heading", { name: "Unverified local policy blocked" })).toBeInTheDocument();
    expect(screen.getByText("LOCAL_POLICY_BLOCKED")).toBeInTheDocument();
    expect(screen.getByText("Local policy is not verified for operator use.")).toBeInTheDocument();
    expect(screen.getAllByText("Blocked").length).toBeGreaterThan(0);
  });

  it("StatutoryDiscountDetail_RendersDisabledDecisionControls", async () => {
    render(
      <App
        apiClient={createMockOperatorConsoleApiClient()}
        initialPath={`/operator-console/statutory-discounts/${firstDraftId}`}
      />
    );

    expect(await screen.findByRole("heading", { name: "Decision actions" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Approve" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Reject" })).toBeDisabled();
    expect(screen.getByText(/dedicated decision workflow and evidence UX slice/i)).toBeInTheDocument();
  });

  it("OperatorConsoleApi_MapsBackendErrorsIntoUiErrors", () => {
    expect(mapApiError({ status: "access-denied", message: "Access blocked", errorCode: "ACCESS_BLOCKED" })).toEqual({
      status: "access-denied",
      message: "Access blocked",
      errorCode: "ACCESS_BLOCKED"
    });

    expect(mapApiError(new Error("network"))).toEqual({
      status: "error",
      message: "Operator Console statutory discount data could not be loaded."
    });
  });

  it("OperatorConsole_DoesNotExposeOutOfScopeControls", async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    render(<App apiClient={createMockOperatorConsoleApiClient()} initialPath="/operator-console/statutory-discounts" />);

    expect(await screen.findByText("STAT-OP-SESSION-0001")).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
    expect(screen.queryByRole("button", { name: /pay/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /apply coupon/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /open gate/i })).not.toBeInTheDocument();
    expect(document.body.innerHTML).not.toMatch(/aub/i);
    expect(document.body.innerHTML).not.toMatch(/hikcentral/i);
    expect(document.body.innerHTML).not.toMatch(/\/v1\/webpay/i);
    expect(document.body.innerHTML).not.toMatch(/\/v1\/public\/coupons/i);
  });
});
