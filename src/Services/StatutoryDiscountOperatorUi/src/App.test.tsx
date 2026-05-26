import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { App } from "./App";
import type { StatutoryDiscountOperatorApiClient } from "./apiClient";

const foundSession = {
  parkingSessionReference: "STAT-FOUND-001",
  vehiclePlate: "XYZ 9876",
  entryTime: "May 26, 2026 10:45 AM",
  currentFee: "PHP 150.00",
  payableBasisStatus: "Backend-approved payable basis placeholder",
  siteDisplayName: "North Site Group / Terminal Parking"
};

describe("ExitPass Statutory Discount Operator UI", () => {
  it("StatutoryDiscountOperator_WhenRendered_ShowsOperatorScaffold", () => {
    render(<App />);

    expect(
      screen.getByRole("heading", { name: "ExitPass Statutory Discount Operator" })
    ).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: /session search/i })).toBeInTheDocument();
    expect(screen.getByLabelText(/ticket number/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/plate number/i)).toBeInTheDocument();
    expect(screen.getByDisplayValue(/site \/ site group will appear here/i)).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: /session summary/i })).toBeInTheDocument();
    expect(screen.getByText(/parking session reference/i)).toBeInTheDocument();
    expect(screen.getByText(/vehicle plate/i)).toBeInTheDocument();
    expect(screen.getAllByText(/entry time/i).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/current fee/i).length).toBeGreaterThan(0);
    expect(screen.getByText(/payable-basis status/i)).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: /review request/i })).toBeInTheDocument();
    expect(screen.getByLabelText(/senior citizen/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/^pwd$/i)).toBeInTheDocument();
    expect(screen.getByText("not searched")).toBeInTheDocument();
    expect(screen.getByText(/enter a ticket number or plate number/i)).toBeInTheDocument();
  });

  it("StatutoryDiscountOperator_IsSeparateFromWebPay", () => {
    render(<App />);

    expect(screen.getByRole("heading", { name: "ExitPass Statutory Discount Operator" })).toBeInTheDocument();
    expect(screen.queryByText(/webpay/i)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /continue to payment/i })).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/payment method/i)).not.toBeInTheDocument();
  });

  it("StatutoryDiscountOperator_DoesNotExposeCouponInputs", () => {
    render(<App />);

    expect(screen.queryByLabelText(/coupon/i)).not.toBeInTheDocument();
    expect(screen.queryByPlaceholderText(/coupon/i)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /apply coupon/i })).not.toBeInTheDocument();
  });

  it("StatutoryDiscountOperator_WhenRendered_DoesNotCallCouponPaymentOrExitApis", () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    render(<App />);

    expect(fetchMock).not.toHaveBeenCalled();
    expect(document.body.innerHTML).not.toMatch(/\/v1\/public\/coupons/i);
    expect(document.body.innerHTML).not.toMatch(/\/v1\/webpay\/payment-intents/i);
    expect(document.body.innerHTML).not.toMatch(/\/v1\/exit/i);
    expect(document.body.innerHTML).not.toMatch(/\/v1\/.*authorization/i);
  });

  it("StatutoryDiscountOperator_WhenSearchStarts_ShowsSearchingState", async () => {
    let resolveLookup: (value: Awaited<ReturnType<StatutoryDiscountOperatorApiClient["findSession"]>>) => void;
    const apiClient: StatutoryDiscountOperatorApiClient = {
      findSession: vi.fn<StatutoryDiscountOperatorApiClient["findSession"]>(
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

  it("StatutoryDiscountOperator_WhenSessionFound_RendersPlaceholderSessionSummary", async () => {
    const apiClient: StatutoryDiscountOperatorApiClient = {
      findSession: vi.fn<StatutoryDiscountOperatorApiClient["findSession"]>(
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
    expect(apiClient.findSession).toHaveBeenCalledWith({ ticketNumber: "", plateNumber: "XYZ 9876" });
  });

  it("StatutoryDiscountOperator_WhenSessionNotFound_ShowsDeterministicNotFoundState", async () => {
    const apiClient: StatutoryDiscountOperatorApiClient = {
      findSession: vi.fn<StatutoryDiscountOperatorApiClient["findSession"]>(
        async () => ({ status: "not found" })
      )
    };

    render(<App apiClient={apiClient} />);

    await userEvent.type(screen.getByLabelText(/ticket number/i), "UNKNOWN");
    await userEvent.click(screen.getByRole("button", { name: /search session/i }));

    expect(await screen.findByText("not found")).toBeInTheDocument();
    expect(screen.getByText(/no matching parking session was found/i)).toBeInTheDocument();
  });

  it("StatutoryDiscountOperator_WhenSessionAmbiguous_ShowsDisambiguationPlaceholder", async () => {
    const apiClient: StatutoryDiscountOperatorApiClient = {
      findSession: vi.fn<StatutoryDiscountOperatorApiClient["findSession"]>(
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

  it("StatutoryDiscountOperator_WhenLookupRuns_DoesNotCallPaymentCouponOrStatutoryWriteApis", async () => {
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

  it("StatutoryDiscountOperator_RendersDisabledDecisionControlsAndLaterSliceCopy", () => {
    render(<App />);

    expect(screen.getByRole("button", { name: /approve/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /^reject$/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /request more information/i })).toBeDisabled();
    expect(screen.getByText(/evidence capture and backend statutory discount validation/i)).toBeInTheDocument();
    expect(screen.getByText(/pending operator review/i)).toBeInTheDocument();
    expect(screen.getAllByText(/approved/i).length).toBeGreaterThan(0);
    expect(screen.getByText(/rejected/i)).toBeInTheDocument();
    expect(screen.getByText(/expired/i)).toBeInTheDocument();
  });
});
