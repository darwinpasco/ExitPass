import { describe, expect, it, vi } from "vitest";
import {
  bootstrapStatutoryEvidence,
  finalizeStatutoryEvidenceUpload,
  requestStatutoryEvidenceUploadSession,
  retrieveStatutoryEvidenceStatus,
  uploadStatutoryEvidence,
  validateStatutoryEvidenceFile
} from "./statutoryEvidence";
import type { WebPayStatutoryEvidenceChannelResponse } from "./types";

const decisionId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
const setReference = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";
const itemReference = "cccccccc-cccc-4ccc-8ccc-cccccccccccc";

function channel(overrides: Partial<WebPayStatutoryEvidenceChannelResponse> = {}): WebPayStatutoryEvidenceChannelResponse {
  return {
    classification: "FOUND",
    retryable: false,
    correlationId: "dddddddd-dddd-4ddd-8ddd-dddddddddddd",
    evidenceRequired: true,
    evidenceSetReference: setReference,
    evidenceItemReference: itemReference,
    allowedContentTypes: ["image/jpeg", "image/png"],
    maximumContentLengthBytes: 5_000_000,
    lifecycleClassification: "REQUIRED_NOT_STARTED",
    replacementPosture: "REPLACEMENT_ALLOWED",
    readyForReview: false,
    ...overrides
  };
}

function jsonResponse(body: unknown, ok = true, status = 200): Response {
  return { ok, status, json: async () => body } as Response;
}

describe("WebPay statutory evidence API", () => {
  it("uses the same-origin bootstrap route without privileged browser headers", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(channel()));

    await bootstrapStatutoryEvidence(decisionId, fetchMock as never);

    expect(fetchMock.mock.calls[0][0]).toBe("/v1/webpay/statutory-discounts/evidence/bootstrap");
    const request = fetchMock.mock.calls[0][1] as RequestInit;
    const headers = request.headers as Record<string, string>;
    expect(request.method).toBe("POST");
    expect(headers["X-Correlation-Id"]).toBeTruthy();
    expect(headers).not.toHaveProperty("X-ExitPass-Service-Identity-Id");
    expect(headers).not.toHaveProperty("X-ExitPass-Permissions");
    expect(headers).not.toHaveProperty("Authorization");
    expect(JSON.parse(request.body as string)).toEqual({
      statutoryDiscountDecisionCommandId: decisionId,
      clientOperationKey: `webpay-evidence-bootstrap:${decisionId}`
    });
  });

  it("retrieves authoritative status using exactly one opaque-safe reference", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(channel()));

    await retrieveStatutoryEvidenceStatus({ evidenceSetReference: setReference }, fetchMock as never);

    expect(fetchMock.mock.calls[0][0]).toBe(
      `/v1/webpay/statutory-discounts/evidence/status?evidenceSetReference=${setReference}`
    );
    await expect(retrieveStatutoryEvidenceStatus({}, fetchMock as never)).rejects.toThrow(/exactly one/i);
    await expect(retrieveStatutoryEvidenceStatus({
      statutoryDiscountDecisionCommandId: decisionId,
      evidenceSetReference: setReference
    }, fetchMock as never)).rejects.toThrow(/exactly one/i);
  });

  it("requests an opaque upload session without storage-provider internals", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({
      classification: "AUTHORIZED",
      retryable: false,
      correlationId: "dddddddd-dddd-4ddd-8ddd-dddddddddddd",
      opaqueUploadSessionReference: "opaque-upload-session",
      method: "PUT",
      expiresAt: "2026-08-05T10:05:00Z",
      acceptedContentType: "image/jpeg",
      maximumContentLengthBytes: 5_000_000
    }));
    const file = new File([new Uint8Array([1, 2, 3])], "id.jpg", { type: "image/jpeg" });
    const checksum = "a".repeat(64);

    const response = await requestStatutoryEvidenceUploadSession(channel(), file, checksum, fetchMock as never);

    expect(response.opaqueUploadSessionReference).toBe("opaque-upload-session");
    const body = JSON.parse((fetchMock.mock.calls[0][1] as RequestInit).body as string);
    expect(body.declaredChecksumSha256).toBe(checksum);
    expect(body.clientOperationKey).not.toContain(checksum);
    expect(body).not.toHaveProperty("bucket");
    expect(body).not.toHaveProperty("container");
    expect(body).not.toHaveProperty("objectKey");
    expect(body).not.toHaveProperty("providerUrl");
    expect(body).not.toHaveProperty("credential");
  });

  it("streams only to the opaque same-origin route and reports progress", async () => {
    const progress = vi.fn();
    const fake = new FakeXmlHttpRequest();
    const file = new File([new Uint8Array([1, 2, 3, 4])], "id.png", { type: "image/png" });

    const upload = uploadStatutoryEvidence("opaque-session", file, progress, undefined, () => fake as never);
    fake.upload.onprogress?.({ loaded: 2, total: 4, lengthComputable: true } as ProgressEvent);
    fake.status = 204;
    fake.onload?.(new ProgressEvent("load"));
    await upload;

    expect(fake.openArgs).toEqual(["PUT", "/v1/webpay/statutory-discounts/evidence/upload-sessions/opaque-session"]);
    expect(fake.headers["Content-Type"]).toBe("image/png");
    expect(fake.headers["X-Correlation-Id"]).toBeTruthy();
    expect(fake.headers).not.toHaveProperty("Authorization");
    expect(fake.sentBody).toBe(file);
    expect(progress).toHaveBeenCalledWith({ loaded: 2, total: 4, percent: 50 });
    expect(progress).toHaveBeenLastCalledWith({ loaded: 4, total: 4, percent: 100 });
  });

  it("finalizes through the same-origin opaque route", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(channel({ lifecycleClassification: "VALIDATION_PENDING" })));

    await finalizeStatutoryEvidenceUpload("opaque-session", fetchMock as never);

    expect(fetchMock.mock.calls[0][0]).toBe(
      "/v1/webpay/statutory-discounts/evidence/upload-sessions/opaque-session/finalize"
    );
    expect((fetchMock.mock.calls[0][1] as RequestInit).method).toBe("POST");
  });

  it("fails closed when a successful response is malformed instead of treating evidence as not required", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ classification: "FOUND" }));

    await expect(bootstrapStatutoryEvidence(decisionId, fetchMock as never)).rejects.toMatchObject({
      errorCode: "WEBPAY_STATUTORY_EVIDENCE_MALFORMED_RESPONSE",
      retryable: true
    });
  });

  it("rejects malformed upload authorization without attempting a provider route", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({
      classification: "AUTHORIZED",
      retryable: false,
      correlationId: "dddddddd-dddd-4ddd-8ddd-dddddddddddd"
    }));
    const file = new File([new Uint8Array([1])], "id.jpg", { type: "image/jpeg" });

    await expect(requestStatutoryEvidenceUploadSession(channel(), file, "a".repeat(64), fetchMock as never))
      .rejects.toMatchObject({ errorCode: "WEBPAY_STATUTORY_EVIDENCE_MALFORMED_RESPONSE" });
  });

  it("does not reflect unknown backend error text to the customer", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({
      errorCode: "UNKNOWN_INTERNAL_FAILURE",
      message: "NpgsqlException at internal-host with object key secret",
      retryable: false,
      correlationId: "dddddddd-dddd-4ddd-8ddd-dddddddddddd"
    }, false, 500));

    await expect(bootstrapStatutoryEvidence(decisionId, fetchMock as never)).rejects.toMatchObject({
      message: "Evidence could not be processed safely. Please refresh and try again."
    });
  });

  it.each([
    [new File([], "empty.jpg", { type: "image/jpeg" }), /empty/i],
    [new File(["pdf"], "id.pdf", { type: "application/pdf" }), /JPEG or PNG/i],
    [new File(["gif"], "id.gif", { type: "image/gif" }), /JPEG or PNG/i],
    [new File([new Uint8Array(6_000_000)], "large.jpg", { type: "image/jpeg" }), /too large/i]
  ])("rejects invalid local file selection without treating it as authority", (file, expected) => {
    expect(validateStatutoryEvidenceFile(file, channel())).toMatch(expected);
  });

  it.each(["image/jpeg", "image/png"])("accepts a bounded %s photo", (type) => {
    const file = new File([new Uint8Array([1])], type === "image/jpeg" ? "id.jpg" : "id.png", { type });
    expect(validateStatutoryEvidenceFile(file, channel())).toBeNull();
  });
});

class FakeXmlHttpRequest {
  public status = 0;
  public responseText = "";
  public upload: { onprogress: ((event: ProgressEvent) => void) | null } = { onprogress: null };
  public onload: ((event: ProgressEvent) => void) | null = null;
  public onerror: ((event: ProgressEvent) => void) | null = null;
  public onabort: ((event: ProgressEvent) => void) | null = null;
  public headers: Record<string, string> = {};
  public openArgs: string[] = [];
  public sentBody: Document | XMLHttpRequestBodyInit | null = null;

  public open(method: string, url: string) {
    this.openArgs = [method, url];
  }

  public setRequestHeader(name: string, value: string) {
    this.headers[name] = value;
  }

  public send(body: Document | XMLHttpRequestBodyInit | null) {
    this.sentBody = body;
  }

  public abort() {
    this.onabort?.(new ProgressEvent("abort"));
  }
}
