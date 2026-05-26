import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { App } from "./App";

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
