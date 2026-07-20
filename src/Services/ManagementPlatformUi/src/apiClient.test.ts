import { describe, expect, it, vi } from "vitest";
import { assertBrowserSafeHeaderName, createCentralPmsApiClient, createUiError, mapErrorResponse, toCentralPmsPath } from "./apiClient";

describe("ManagementPlatformUi Central PMS API client foundation", () => {
  it("uses relative Central PMS Management Platform routes and sends a correlation ID", async () => {
    const fetchImpl = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      expect(input).toBe("/v1/management-platform/foundation-readiness");
      expect(init?.method).toBe("GET");
      expect(new Headers(init?.headers).get("X-Correlation-Id")).toBe("corr-001");
      return jsonResponse({ status: "ready" }, 200, "corr-001");
    });

    const client = createCentralPmsApiClient({ fetchImpl });
    await expect(client.request<{ status: string }>("/v1/management-platform/foundation-readiness", { correlationId: "corr-001" })).resolves.toEqual({ status: "ready" });
    expect(fetchImpl).toHaveBeenCalledTimes(1);
  });

  it("preserves one logical correlation ID for the caller-supplied request", async () => {
    const fetchImpl = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      expect(new Headers(init?.headers).get("X-Correlation-Id")).toBe("logical-correlation");
      return jsonResponse({ ok: true }, 200, "logical-correlation");
    });

    const client = createCentralPmsApiClient({ fetchImpl });
    await client.request("/v1/management-platform/foundation-readiness", { correlationId: "logical-correlation" });
    expect(fetchImpl).toHaveBeenCalledTimes(1);
  });

  it("maps typed safe success and validation responses", async () => {
    const validation = mapErrorResponse(422, { code: "VALIDATION_FAILED", message: "Review the submitted fields." }, "corr-422", false);

    expect(validation.kind).toBe("validation");
    expect(validation.code).toBe("VALIDATION_FAILED");
    expect(validation.message).toBe("Review the submitted fields.");
  });

  it.each([
    [401, "authentication-required"],
    [403, "permission-denied"],
    [404, "not-found"],
    [409, "conflict"],
    [429, "throttled"],
    [503, "integration-unavailable"]
  ] as const)("maps HTTP %s safely", (status, expectedKind) => {
    expect(mapErrorResponse(status, { code: `HTTP_${status}` }, "corr", false).kind).toBe(expectedKind);
  });

  it("maps timeout mutation failures to uncertain-result posture without retrying", async () => {
    const fetchImpl = vi.fn(async () => jsonResponse({ code: "TIMEOUT" }, 504, "corr-timeout"));
    const client = createCentralPmsApiClient({ fetchImpl });

    await expect(client.request("/v1/management-platform/foundation-action", { method: "POST", correlationId: "corr-timeout" })).rejects.toMatchObject({
      kind: "timeout",
      mutationUncertain: true
    });
    expect(fetchImpl).toHaveBeenCalledTimes(1);
  });

  it("maps malformed responses without rendering raw body content", async () => {
    const fetchImpl = vi.fn(async () => new Response("<html>not json</html>", { status: 200, headers: { "X-Correlation-Id": "corr-malformed" } }));
    const client = createCentralPmsApiClient({ fetchImpl });

    await expect(client.request("/v1/management-platform/foundation-readiness")).rejects.toMatchObject({
      kind: "malformed-response",
      message: "The server response could not be read safely.",
      correlationId: "corr-malformed"
    });
  });

  it("handles cancellation safely", async () => {
    const fetchImpl = vi.fn(async () => {
      throw new DOMException("cancelled", "AbortError");
    });
    const client = createCentralPmsApiClient({ fetchImpl });

    await expect(client.request("/v1/management-platform/foundation-readiness", { correlationId: "corr-cancel" })).rejects.toMatchObject({
      kind: "timeout",
      code: "MANAGEMENT_PLATFORM_REQUEST_CANCELLED",
      correlationId: "corr-cancel"
    });
  });

  it("rejects absolute routes and downstream administration routes", () => {
    expect(() => toCentralPmsPath("", "https://example.test/v1/management-platform/test")).toThrow();
    expect(() => toCentralPmsPath("", "//example.test/v1/management-platform/test")).toThrow();
    expect(() => toCentralPmsPath("", "/v1/admin/fiscal-identities")).toThrow();
    expect(() => toCentralPmsPath("", "/v1/admin/sales-invoice-header-profiles")).toThrow();
    expect(() => toCentralPmsPath("", "/v1/management-platform/test")).not.toThrow();
  });

  it("rejects absolute browser API base paths", () => {
    expect(() => createCentralPmsApiClient({ basePath: "https://central-pms.example" }).request("/v1/management-platform/test")).toThrow();
  });

  it.each([
    "X-PosServer-Admin-Key",
    "X-PosServer-Admin-Permission",
    "x-PoSsErVeR-AdMiN-KeY",
    "  X-PosServer-Admin-Key  ",
    "X-PosServer-Operational-Secret",
    "Authorization",
    "Cookie",
    "Proxy-Authorization",
    "Set-Cookie"
  ])("rejects browser-provided server-only header %s", (headerName) => {
    expect(() => assertBrowserSafeHeaderName(headerName, "corr-forbidden")).toThrowError(
      expect.objectContaining({
        code: "MANAGEMENT_PLATFORM_FORBIDDEN_HEADER",
        correlationId: "corr-forbidden"
      })
    );
  });

  it("rejects forbidden request headers without exposing credential values", async () => {
    const fetchImpl = vi.fn();
    const client = createCentralPmsApiClient({ fetchImpl });

    await expect(
      client.request("/v1/management-platform/foundation-readiness", {
        correlationId: "corr-secret",
        headers: {
          "X-PosServer-Admin-Key": "secret-value-that-must-not-appear"
        }
      })
    ).rejects.toMatchObject({
      code: "MANAGEMENT_PLATFORM_FORBIDDEN_HEADER",
      correlationId: "corr-secret"
    });

    try {
      await client.request("/v1/management-platform/foundation-readiness", {
        correlationId: "corr-secret",
        headers: {
          "X-PosServer-Admin-Key": "secret-value-that-must-not-appear"
        }
      });
    } catch (error) {
      expect(JSON.stringify(error)).not.toContain("secret-value-that-must-not-appear");
    }

    expect(fetchImpl).not.toHaveBeenCalled();
  });

  it("allows safe browser headers", async () => {
    const fetchImpl = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      const headers = new Headers(init?.headers);
      expect(headers.get("Accept")).toBe("application/json");
      expect(headers.get("Content-Type")).toBe("application/json");
      expect(headers.get("X-Correlation-Id")).toBe("corr-safe-header");
      return jsonResponse({ ok: true }, 200, "corr-safe-header");
    });
    const client = createCentralPmsApiClient({ fetchImpl });

    await expect(
      client.request("/v1/management-platform/foundation-readiness", {
        method: "POST",
        correlationId: "corr-safe-header",
        headers: {
          Accept: "application/json",
          "Content-Type": "application/json",
          "X-Correlation-Id": "corr-safe-header"
        },
        body: { ok: true }
      })
    ).resolves.toEqual({ ok: true });
  });

  it("does not expose raw server bodies or secrets in mapped errors", () => {
    const error = createUiError("unknown", "SAFE", "Safe message", "corr-safe");

    expect(JSON.stringify(error)).not.toContain("stack");
    expect(JSON.stringify(error)).not.toContain("token");
    expect(JSON.stringify(error)).not.toContain("api-key");
  });
});

function jsonResponse(body: unknown, status: number, correlationId: string): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: {
      "Content-Type": "application/json",
      "X-Correlation-Id": correlationId
    }
  });
}
