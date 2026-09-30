import { afterEach, describe, expect, it, vi } from "vitest";
import { createHttpOperatorConsoleApiClient } from "./apiClient";

const parkingSessionId = "37e87d1a-5531-4eff-952b-7de19567d01b";

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("Operator Console Sales Invoice customer information client", () => {
  it("reads the authoritative same-session values without cache or browser-authored authority", async () => {
    const fetchMock = vi.fn(async () => customerResponse());
    vi.stubGlobal("fetch", fetchMock);

    const result = await createHttpOperatorConsoleApiClient()
      .getInvoiceCustomerInformation!(parkingSessionId);

    expect(result).toMatchObject({
      parkingSessionId,
      customerName: "Jose Rizal",
      address: "123 Calamba",
      tin: "124753284",
      businessStyle: "La Liga Inc",
      rowVersion: 1,
      fiscalSnapshotLocked: false
    });
    const [url, init] = (fetchMock.mock.calls as unknown as Array<[string, RequestInit]>)[0];
    expect(url).toBe(`/v1/ops/operator-console/sessions/${parkingSessionId}/invoice-customer-information`);
    expect(init).toMatchObject({ cache: "no-store", credentials: "same-origin" });
    expect(init.headers).not.toHaveProperty("X-Operator-User-Id");
    expect(init.headers).not.toHaveProperty("X-Site-Id");
  });

  it("retries one transient malformed public-proxy response and returns the authoritative payload", async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response("<!doctype html>", { status: 200, headers: { "Content-Type": "text/html" } }))
      .mockResolvedValueOnce(customerResponse());
    vi.stubGlobal("fetch", fetchMock);

    const result = await createHttpOperatorConsoleApiClient()
      .getInvoiceCustomerInformation!(parkingSessionId);

    expect(result.customerName).toBe("Jose Rizal");
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it("does not retry a governed authorization denial", async () => {
    const fetchMock = vi.fn(async () => jsonResponse({
      errorCode: "CENTRAL_PMS_RBAC_FORBIDDEN",
      message: "Access denied."
    }, 403));
    vi.stubGlobal("fetch", fetchMock);

    await expect(createHttpOperatorConsoleApiClient()
      .getInvoiceCustomerInformation!(parkingSessionId))
      .rejects.toMatchObject({ status: "access-denied", errorCode: "CENTRAL_PMS_RBAC_FORBIDDEN" });
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });
});

function customerResponse() {
  return jsonResponse({
    parkingSessionId,
    hasCustomerInformation: true,
    customerName: "Jose Rizal",
    address: "123 Calamba",
    tin: "124753284",
    businessStyle: "La Liga Inc",
    rowVersion: 1,
    updatedAt: "2026-09-28T09:19:04.060769Z",
    fiscalSnapshotLocked: false
  });
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" }
  });
}
