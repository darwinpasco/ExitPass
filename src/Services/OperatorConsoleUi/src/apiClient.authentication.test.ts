import { afterEach, describe, expect, it, vi } from "vitest";
import { createHttpOperatorConsoleApiClient, createOperatorConsoleApiClient } from "./apiClient";

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("Operator Console authenticated HTTP boundary", () => {
  it("uses session permissions only for advisory capability presentation", () => {
    const client = createHttpOperatorConsoleApiClient({
      permissions: [
        "statutory-discounts.decision.approve",
        "statutory-discounts.evidence.review.view"
      ]
    });

    expect(client.canApproveStatutoryDiscount?.()).toBe(true);
    expect(client.canRejectStatutoryDiscount?.()).toBe(false);
    expect(client.canReviewStatutoryEvidence?.()).toBe(true);
    expect(createHttpOperatorConsoleApiClient().canApproveStatutoryDiscount?.()).toBe(false);
  });

  it("sends same-origin credentials without fixture authority headers", async () => {
    const fetchMock = vi.fn(async () => jsonResponse({ items: [] }));
    vi.stubGlobal("fetch", fetchMock);

    await createHttpOperatorConsoleApiClient().listStatutoryDiscountDrafts();

    const calls = fetchMock.mock.calls as unknown as Array<[string, RequestInit]>;
    const init = calls[0][1];
    expect(init.credentials).toBe("same-origin");
    const headers = init.headers as Record<string, string>;
    for (const header of [
      "Authorization",
      "X-Operator-User-Id",
      "X-ExitPass-User-Id",
      "X-ExitPass-Permissions",
      "X-Operator-Device-Binding-Id",
      "X-Operator-Shift-Id",
      "X-Site-Id",
      "X-Site-Group-Id"
    ]) {
      expect(headers[header]).toBeUndefined();
    }
  });

  it("keeps the production client on the same-origin Operator Console boundary", async () => {
    const fetchMock = vi.fn(async () => jsonResponse({ items: [] }));
    vi.stubGlobal("fetch", fetchMock);

    await createOperatorConsoleApiClient().listStatutoryDiscountDrafts();

    const calls = fetchMock.mock.calls as unknown as Array<[string, RequestInit]>;
    expect(calls[0][0]).toMatch(/^\/v1\/ops\/operator-console\//);
    expect(calls[0][0]).not.toMatch(/^https?:\/\//);
  });

  it("sends decision facts without browser-authored reviewer or operating-context authority", async () => {
    const fetchMock = vi.fn(async () => jsonResponse({
      accessAllowed: true,
      accessDecision: "ALLOW",
      accessDenialReasons: [],
      decisionAccepted: true,
      decisionPersisted: true,
      currentValidationStatus: "APPROVED"
    }));
    vi.stubGlobal("fetch", fetchMock);

    await createHttpOperatorConsoleApiClient().submitStatutoryDiscountDecision({
      draftId: "draft-1",
      siteId: "site-target",
      siteGroupId: "site-group-target",
      decision: "APPROVE"
    });

    const calls = fetchMock.mock.calls as unknown as Array<[string, RequestInit]>;
    const body = JSON.parse(calls[0][1].body as string);
    expect(body).not.toHaveProperty("userId");
    expect(body).not.toHaveProperty("reviewerUserId");
    expect(body).not.toHaveProperty("operatorDeviceBindingId");
    expect(body).not.toHaveProperty("operatorShiftId");
    expect(body).not.toHaveProperty("siteId");
    expect(body).not.toHaveProperty("siteGroupId");
    expect(body).toEqual(expect.objectContaining({ decision: "APPROVE" }));
  });

  it("locks the client boundary on 401 but keeps the session on 403", async () => {
    const onAuthenticationRequired = vi.fn();
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse({ errorCode: "SESSION_REVOKED" }, 401))
      .mockResolvedValueOnce(jsonResponse({ errorCode: "CENTRAL_PMS_RBAC_FORBIDDEN" }, 403));
    vi.stubGlobal("fetch", fetchMock);
    const client = createHttpOperatorConsoleApiClient({ onAuthenticationRequired });

    await expect(client.listStatutoryDiscountDrafts()).rejects.toBeTruthy();
    expect(onAuthenticationRequired).toHaveBeenCalledTimes(1);
    await expect(client.listStatutoryDiscountDrafts()).rejects.toBeTruthy();
    expect(onAuthenticationRequired).toHaveBeenCalledTimes(1);
  });

  it("does not write identity, permission, or scope authority to browser storage", async () => {
    const localSet = vi.spyOn(Storage.prototype, "setItem");
    vi.stubGlobal("fetch", vi.fn(async () => jsonResponse({ items: [] })));

    await createHttpOperatorConsoleApiClient({ permissions: ["statutory-discounts.decision.approve"] })
      .listStatutoryDiscountDrafts();

    expect(localSet).not.toHaveBeenCalled();
    expect(window.localStorage).toHaveLength(0);
    expect(window.sessionStorage).toHaveLength(0);
  });
});

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" }
  });
}
