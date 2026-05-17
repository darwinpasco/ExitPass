import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { App } from "./App";

vi.mock("@zxing/browser", () => ({
  BrowserQRCodeReader: vi.fn().mockImplementation(() => ({
    decodeFromVideoDevice: vi.fn().mockRejectedValue(new DOMException("Denied", "NotAllowedError"))
  }))
}));

const successResponse = {
  paymentAttemptId: "44444444-4444-4444-4444-444444444444",
  parkingSessionId: "55555555-5555-5555-5555-555555555555",
  tariffSnapshotId: "66666666-6666-6666-6666-666666666666",
  amountMinorUnits: 12500,
  currency: "PHP",
  paymentMethod: "QRPH",
  selectedProviderCode: "AUB",
  fallbackProviderCode: "PAYMONGO",
  routingReason: "PRIMARY_PROVIDER",
  status: "PENDING_PROVIDER",
  handoff: {
    type: "Redirect",
    handoffUrl: "https://payments.test/handoff",
    qrCodeUrl: "https://payments.test/qr.png"
  },
  correlationId: "77777777-7777-7777-7777-777777777777"
};

afterEach(() => {
  vi.restoreAllMocks();
});

describe("ExitPass WebPay UI", () => {
  it("WebPay_WhenApiReturnsHandoff_DisplaysContinueToPayment", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        ok: true,
        json: async () => successResponse
      })
    );

    render(<App />);

    await userEvent.type(screen.getByLabelText(/ticket reference/i), "TICKET-001");
    await userEvent.click(screen.getByRole("button", { name: /continue/i }));

    expect(await screen.findByRole("link", { name: /continue to payment/i })).toHaveAttribute(
      "href",
      "https://payments.test/handoff"
    );
    expect(screen.getByText("PHP")).toBeInTheDocument();
    expect(screen.getByText("PENDING_PROVIDER")).toBeInTheDocument();
    expect(screen.getByText("₱125.00")).toBeInTheDocument();
  });

  it("WebPay_WhenApiReturnsQrCodeUrl_DisplaysQrPaymentInstructions", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        ok: true,
        json: async () => successResponse
      })
    );

    render(<App />);

    await userEvent.type(screen.getByLabelText(/ticket reference/i), "TICKET-001");
    await userEvent.click(screen.getByRole("button", { name: /continue/i }));

    expect(await screen.findByText(/QR payment instructions/i)).toBeInTheDocument();
    expect(screen.getByText("https://payments.test/qr.png")).toBeInTheDocument();
  });

  it("WebPay_WhenApiReturnsError_DisplaysFriendlyError", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        ok: false,
        json: async () => ({ errorCode: "SESSION_NOT_FOUND", message: "Vendor parking session was not found." })
      })
    );

    render(<App />);

    await userEvent.type(screen.getByLabelText(/ticket reference/i), "UNKNOWN");
    await userEvent.click(screen.getByRole("button", { name: /continue/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent("could not find an active parking session");
  });

  it("WebPay_WhenCameraUnavailable_ShowsManualFallback", async () => {
    vi.stubGlobal("navigator", { mediaDevices: undefined });

    render(<App />);

    await userEvent.click(screen.getByRole("button", { name: /scan qr/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Camera is unavailable");
    expect(screen.getByLabelText(/ticket reference/i)).toBeInTheDocument();
  });

  it("WebPay_WhenCameraDenied_ShowsManualFallback", async () => {
    vi.stubGlobal("navigator", {
      mediaDevices: {
        getUserMedia: vi.fn()
      }
    });

    render(<App />);

    await userEvent.click(screen.getByRole("button", { name: /scan qr/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/Camera permission was denied|Camera is unavailable/);
    expect(screen.getByLabelText(/ticket reference/i)).toBeInTheDocument();
  });

  it("WebPay_UsesExistingAssetPaths", () => {
    render(<App />);

    expect(screen.getByAltText("ExitPass")).toHaveAttribute("src", "/assets/logo/exitpass-logo.svg");
    expect(screen.getByAltText("Pro Parking")).toHaveAttribute("src", "/assets/logo/proparking-logo.png");
    expect(document.body.innerHTML).toContain("/assets/payment-methods/qrph.png");
    expect(document.body.innerHTML).toContain("/assets/payment-methods/cards-visa-mastercard.png");
    expect(document.body.innerHTML).toContain("/assets/payment-methods/gcash.png");
    expect(document.body.innerHTML).toContain("/assets/payment-methods/maya.png");
  });

  it("WebPay_WhenPlateEntered_SubmitsPlateNumber", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ ...successResponse, paymentMethod: "GCASH" })
    });
    vi.stubGlobal("fetch", fetchMock);

    render(<App />);

    await userEvent.click(screen.getByRole("button", { name: /plate/i }));
    await userEvent.type(screen.getByLabelText(/plate number/i), "abc 1234");
    await userEvent.click(screen.getByLabelText(/GCash/i));
    await userEvent.click(screen.getByRole("button", { name: /continue/i }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalled());
    const body = JSON.parse(fetchMock.mock.calls[0][1].body as string);
    expect(body).toEqual({ plateNumber: "ABC 1234", paymentMethod: "GCASH" });
  });
});
