import { cleanup, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { App } from "./App";

vi.mock("@zxing/browser", () => ({
  BrowserQRCodeReader: vi.fn().mockImplementation(() => ({
    decodeFromVideoDevice: vi.fn().mockRejectedValue(new DOMException("Denied", "NotAllowedError"))
  }))
}));

const originalFetch = globalThis.fetch;

afterEach(() => {
  cleanup();
  globalThis.fetch = originalFetch;
  window.history.pushState({}, "", "/");
  vi.restoreAllMocks();
});

beforeEach(() => {
  window.history.pushState({}, "", "/webpay/payment-return?plateNumber=R34-PLATE-A");
});

it("WebPayReturnPage_LoadsStatusByPlateNumber", async () => {
  const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
    expect(String(url)).toContain("/v1/webpay/parking-session");
    expect(init?.method).toBe("POST");

    const body = JSON.parse(String(init?.body));
    expect(body.plateNumber).toBe("R34-PLATE-A");
    expect(body).not.toHaveProperty("ticketReference");

    return {
      ok: true,
      status: 200,
      json: async () => ({
        parkingSessionId: "7456232b-8b62-4d46-b3b7-6a219b447ea5",
        tariffSnapshotId: "5a767ff2-f646-47cb-8ce0-e2d9e1df1afc",
        siteGroupId: "29b8b4f4-40dd-447b-ac06-dd52e6ad51c5",
        siteId: "93bd3cb3-e806-4c5c-ac8c-df6c4addff14",
        vendorSystemId: "45a625de-9034-4fb6-b527-0950d384e51f",
        siteName: "Site A",
        plateNumber: "R34-PLATE-A",
        amountMinorUnits: 2500,
        currency: "PHP",
        parkingStatus: "PaymentCompleted",
        paymentStatus: "CONFIRMED",
        correlationId: "77777777-7777-4777-8777-777777777777"
      })
    } as Response;
  });

  globalThis.fetch = fetchMock as unknown as typeof fetch;

  render(<App />);

  expect(await screen.findByRole("heading", { name: /payment confirmed/i })).toBeInTheDocument();
  expect(screen.getByText(/R34-PLATE-A/)).toBeInTheDocument();
  expect(screen.queryByText(/Ticket reference is missing/i)).not.toBeInTheDocument();

  await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
});
