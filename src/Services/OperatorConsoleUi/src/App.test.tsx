import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { App } from "./App";
import type { OperatorConsoleApiClient } from "./apiClient";

const foundSession = {
  parkingSessionReference: "STAT-FOUND-001",
  vehiclePlate: "XYZ 9876",
  entryTime: "May 26, 2026 10:45 AM",
  currentFee: "PHP 150.00",
  paymentStatus: "Paid via WebPay - read-only",
  payableBasisStatus: "Backend-approved payable basis placeholder",
  siteDisplayName: "North Site Group / Terminal Parking"
};

describe("ExitPass Operator Console UI", () => {
  it("OperatorConsole_WhenRendered_ShowsOperatorScaffold", () => {
    render(<App />);

    expect(
      screen.getByRole("heading", { name: "ExitPass Operator Console" })
    ).toBeInTheDocument();
    expect(screen.getByText(/operator-facing console for exitpass site workflows/i)).toBeInTheDocument();
    expect(screen.getByText(/statutory discount validation is the first module/i)).toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: /operator console modules/i })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: /navigation/i })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: /session search/i })).toBeInTheDocument();
    expect(screen.getByLabelText(/ticket number/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/plate number/i)).toBeInTheDocument();
    expect(screen.getByDisplayValue(/site \/ site group will appear here/i)).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: /session summary/i })).toBeInTheDocument();
    expect(screen.getByText(/parking session reference/i)).toBeInTheDocument();
    expect(screen.getByText(/vehicle plate/i)).toBeInTheDocument();
    expect(screen.getAllByText(/entry time/i).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/current fee/i).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/payment status/i).length).toBeGreaterThan(0);
    expect(screen.getByText(/payment status will appear here as read-only context/i)).toBeInTheDocument();
    expect(screen.getByText(/payable-basis status/i)).toBeInTheDocument();
    expect(screen.getAllByRole("heading", { name: /statutory discount validation/i }).length).toBeGreaterThan(0);
    expect(screen.getByLabelText(/senior citizen/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/^pwd$/i)).toBeInTheDocument();
    expect(screen.getByText("not searched")).toBeInTheDocument();
    expect(screen.getByText(/enter a ticket number or plate number/i)).toBeInTheDocument();
  });

  it("OperatorConsole_WhenRendered_ShowsPlatformModulePlaceholders", () => {
    render(<App />);

    expect(screen.getAllByText("Session Lookup").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Statutory Discount Validation").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Registered Device and Shift Access").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Supervisor Review and Overrides").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Audit and Reporting").length).toBeGreaterThan(0);
    expect(screen.getByText(/find parking sessions by ticket or plate/i)).toBeInTheDocument();
    expect(screen.getByText(/initial module for senior citizen and pwd validation review/i)).toBeInTheDocument();
    expect(screen.getByText(/device registration and shift-based operator access/i)).toBeInTheDocument();
    expect(screen.getByText(/supervised review paths and override workflows/i)).toBeInTheDocument();
    expect(screen.getByText(/operational audit trails and reporting views/i)).toBeInTheDocument();
  });

  it("OperatorConsole_IsSeparateFromWebPay", () => {
    render(<App />);

    expect(screen.getByRole("heading", { name: "ExitPass Operator Console" })).toBeInTheDocument();
    expect(screen.queryByText(/webpay/i)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /continue to payment/i })).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/payment method/i)).not.toBeInTheDocument();
  });

  it("OperatorConsole_DoesNotExposeCouponInputs", () => {
    render(<App />);

    expect(screen.queryByLabelText(/coupon/i)).not.toBeInTheDocument();
    expect(screen.queryByPlaceholderText(/coupon/i)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /apply coupon/i })).not.toBeInTheDocument();
  });

  it("OperatorConsole_DoesNotExposePaymentExitOrGateControls", () => {
    render(<App />);

    expect(screen.queryByRole("button", { name: /pay/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /refund/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /mark paid/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /authorize exit/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /open gate/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /gate/i })).not.toBeInTheDocument();
  });

  it("OperatorConsole_WhenRendered_DoesNotCallCouponPaymentOrExitApis", () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    render(<App />);

    expect(fetchMock).not.toHaveBeenCalled();
    expect(document.body.innerHTML).not.toMatch(/\/v1\/public\/coupons/i);
    expect(document.body.innerHTML).not.toMatch(/\/v1\/webpay\/payment-intents/i);
    expect(document.body.innerHTML).not.toMatch(/\/v1\/exit/i);
    expect(document.body.innerHTML).not.toMatch(/\/v1\/.*authorization/i);
  });

  it("OperatorConsole_WhenSearchStarts_ShowsSearchingState", async () => {
    let resolveLookup: (value: Awaited<ReturnType<OperatorConsoleApiClient["findSession"]>>) => void;
    const apiClient: OperatorConsoleApiClient = {
      findSession: vi.fn<OperatorConsoleApiClient["findSession"]>(
        async () =>
          new Promise((resolve) => {
            resolveLookup = resolve;
          })
      )
    };

    render(<App apiClient={apiClient} />);

    await userEvent.type(screen.getByLabelText(/ticket number/i), "DEMO-FOUND");
    await userEvent.click(screen.getByRole("button", { name: /search session/i }));

    expect(screen.getByText("searching")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^searching$/i })).toBeDisabled();

    resolveLookup!({ status: "session found", session: foundSession });
    expect(await screen.findByText("session found")).toBeInTheDocument();
  });

  it("OperatorConsole_WhenSessionFound_RendersPlaceholderSessionSummary", async () => {
    const apiClient: OperatorConsoleApiClient = {
      findSession: vi.fn<OperatorConsoleApiClient["findSession"]>(
        async () => ({ status: "session found", session: foundSession })
      )
    };

    render(<App apiClient={apiClient} />);

    await userEvent.type(screen.getByLabelText(/plate number/i), "XYZ 9876");
    await userEvent.click(screen.getByRole("button", { name: /search session/i }));

    expect(await screen.findByText("session found")).toBeInTheDocument();
    expect(screen.getByText("STAT-FOUND-001")).toBeInTheDocument();
    expect(screen.getByText("XYZ 9876")).toBeInTheDocument();
    expect(screen.getByText("PHP 150.00")).toBeInTheDocument();
    expect(screen.getByText("Paid via WebPay - read-only")).toBeInTheDocument();
    expect(apiClient.findSession).toHaveBeenCalledWith({ ticketNumber: "", plateNumber: "XYZ 9876" });
  });

  it("OperatorConsole_WhenSessionNotFound_ShowsDeterministicNotFoundState", async () => {
    const apiClient: OperatorConsoleApiClient = {
      findSession: vi.fn<OperatorConsoleApiClient["findSession"]>(
        async () => ({ status: "not found" })
      )
    };

    render(<App apiClient={apiClient} />);

    await userEvent.type(screen.getByLabelText(/ticket number/i), "UNKNOWN");
    await userEvent.click(screen.getByRole("button", { name: /search session/i }));

    expect(await screen.findByText("not found")).toBeInTheDocument();
    expect(screen.getByText(/no matching parking session was found/i)).toBeInTheDocument();
  });

  it("OperatorConsole_WhenSessionAmbiguous_ShowsDisambiguationPlaceholder", async () => {
    const apiClient: OperatorConsoleApiClient = {
      findSession: vi.fn<OperatorConsoleApiClient["findSession"]>(
        async () => ({ status: "ambiguous session", matches: 2 })
      )
    };

    render(<App apiClient={apiClient} />);

    await userEvent.type(screen.getByLabelText(/plate number/i), "AMBIGUOUS");
    await userEvent.click(screen.getByRole("button", { name: /search session/i }));

    expect(await screen.findByText("ambiguous session")).toBeInTheDocument();
    expect(screen.getByText(/multiple matching sessions were found \(2\)/i)).toBeInTheDocument();
    expect(screen.getByText(/operator disambiguation will be wired in a later slice/i)).toBeInTheDocument();
  });

  it("OperatorConsole_WhenLookupRuns_DoesNotCallPaymentCouponOrStatutoryWriteApis", async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    render(<App />);

    await userEvent.type(screen.getByLabelText(/ticket number/i), "DEMO-FOUND");
    await userEvent.click(screen.getByRole("button", { name: /search session/i }));

    expect(await screen.findByText("session found")).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
    expect(document.body.innerHTML).not.toMatch(/\/v1\/public\/coupons/i);
    expect(document.body.innerHTML).not.toMatch(/\/v1\/webpay\/payment-intents/i);
    expect(document.body.innerHTML).not.toMatch(/\/v1\/public\/discounts\/statutory\/validate/i);
  });

  it("OperatorConsole_RendersDisabledDecisionControlsAndLaterSliceCopy", () => {
    render(<App />);

    expect(screen.getByRole("button", { name: /approve/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /^reject$/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /request more information/i })).toBeDisabled();
    expect(screen.getByText(/evidence capture and backend statutory discount validation/i)).toBeInTheDocument();
    expect(screen.getByText(/payment collection is out of scope/i)).toBeInTheDocument();
    expect(screen.getByText(/payment status is displayed read-only/i)).toBeInTheDocument();
    expect(screen.getByText(/pending operator review/i)).toBeInTheDocument();
    expect(screen.getAllByText(/approved/i).length).toBeGreaterThan(0);
    expect(screen.getByText(/rejected/i)).toBeInTheDocument();
    expect(screen.getByText(/expired/i)).toBeInTheDocument();
  });
});
