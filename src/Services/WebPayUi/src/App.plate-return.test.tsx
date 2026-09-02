import { cleanup, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { App } from "./App";

vi.mock("@zxing/browser", () => ({
  BrowserQRCodeReader: vi.fn().mockImplementation(() => ({
    decodeFromVideoDevice: vi.fn().mockRejectedValue(new DOMException("Denied", "NotAllowedError"))
  }))
}));

const originalFetch = globalThis.fetch;
const paymentAttemptId = "965a9700-fb9d-4f4c-be0a-5b3cbf8d6357";

afterEach(() => {
  cleanup();
  globalThis.fetch = originalFetch;
  window.history.pushState({}, "", "/");
  vi.restoreAllMocks();
});

beforeEach(() => {
  window.history.pushState(
    {},
    "",
    `/webpay/payment-return?paymentAttemptId=${paymentAttemptId}&correlationId=77777777-7777-4777-8777-777777777777`
  );
});

it("WebPayReturnPage_LoadsPlateOriginatedStatusByPaymentAttemptOnly", async () => {
  const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
    expect(String(url)).toContain(`/v1/webpay/payment-attempts/${paymentAttemptId}/status`);
    expect(String(url)).not.toContain("plateNumber");
    expect(String(url)).not.toContain("ticketReference");
    expect(init?.method).toBe("GET");

    return {
      ok: true,
      status: 200,
      json: async () => ({
        paymentAttemptId,
        parkingSessionId: "7456232b-8b62-4d46-b3b7-6a219b447ea5",
        tariffSnapshotId: "5a767ff2-f646-47cb-8ce0-e2d9e1df1afc",
        siteGroupId: "29b8b4f4-40dd-447b-ac06-dd52e6ad51c5",
        siteId: "93bd3cb3-e806-4c5c-ac8c-df6c4addff14",
        vendorSystemId: "45a625de-9034-4fb6-b527-0950d384e51f",
        siteName: "Site A",
        plateNumber: "R35-PLATE-C",
        amountMinorUnits: 2500,
        currency: "PHP",
        parkingStatus: "ACTIVE",
        paymentStatus: "PENDING_PROVIDER",
        correlationId: "77777777-7777-4777-8777-777777777777"
      })
    } as Response;
  });

  globalThis.fetch = fetchMock as unknown as typeof fetch;

  render(<App />);

  expect(await screen.findByRole("heading", { name: /payment is still being verified/i })).toBeInTheDocument();
  expect(screen.getByRole("link", { name: /retry payment/i })).toHaveAttribute("href", "/?plateNumber=R35-PLATE-C");
  expect(screen.queryByText("Parking Session Summary")).not.toBeInTheDocument();
  expect(screen.queryByText(/Payment reference is missing/i)).not.toBeInTheDocument();

  await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
});

it("WebPayReturnPage_UsesServerReturnedTicketForRetryWithoutProviderTicketQuery", async () => {
  const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
    expect(String(url)).toContain(`/v1/webpay/payment-attempts/${paymentAttemptId}/status`);
    expect(String(url)).not.toContain("ticketReference");
    expect(init?.method).toBe("GET");

    return {
      ok: true,
      status: 200,
      json: async () => ({
        paymentAttemptId,
        parkingSessionId: "7456232b-8b62-4d46-b3b7-6a219b447ea5",
        tariffSnapshotId: "5a767ff2-f646-47cb-8ce0-e2d9e1df1afc",
        siteGroupId: "29b8b4f4-40dd-447b-ac06-dd52e6ad51c5",
        siteId: "93bd3cb3-e806-4c5c-ac8c-df6c4addff14",
        siteName: "Site A",
        ticketReference: "R35-TICKET-RETURN",
        amountMinorUnits: 2500,
        currency: "PHP",
        parkingStatus: "ACTIVE",
        paymentStatus: "PENDING_PROVIDER",
        correlationId: "77777777-7777-4777-8777-777777777777"
      })
    } as Response;
  });

  globalThis.fetch = fetchMock as unknown as typeof fetch;
  render(<App />);

  expect(await screen.findByRole("link", { name: /retry payment/i })).toHaveAttribute(
    "href",
    "/?ticketReference=R35-TICKET-RETURN"
  );
  expect(screen.queryByText("Parking Session Summary")).not.toBeInTheDocument();
  expect(window.location.search).not.toContain("ticketReference");
  await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
});

it("WebPayReturnPage_WithoutPaymentAttemptId_FailsClosed", async () => {
  window.history.pushState({}, "", "/webpay/payment-return?plateNumber=R35-PLATE-C");
  const fetchMock = vi.fn();
  globalThis.fetch = fetchMock as unknown as typeof fetch;

  render(<App />);

  expect(await screen.findByText("Payment reference is missing.")).toBeInTheDocument();
  expect(fetchMock).not.toHaveBeenCalled();
});
