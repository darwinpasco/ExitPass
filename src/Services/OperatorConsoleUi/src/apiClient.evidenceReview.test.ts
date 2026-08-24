import { afterEach, describe, expect, it, vi } from "vitest";
import { createHttpOperatorConsoleApiClient } from "./apiClient";

const decisionId = "31000000-0000-0000-0000-000000000001";
const itemReference = "32000000-0000-0000-0000-000000000001";

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("Operator Console I-017 API client", () => {
  it("uses the same-origin review-safe metadata GET route without browser-authored authority", async () => {
    const fetchMock = vi.fn(async () => jsonResponse(reviewResponse()));
    vi.stubGlobal("fetch", fetchMock);

    const result = await createHttpOperatorConsoleApiClient({ baseUrl: "https://internal.example.invalid" })
      .getStatutoryEvidenceReview(decisionId);

    expect(result.items[0].previewPermitted).toBe(true);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = (fetchMock.mock.calls as unknown as Array<[string, RequestInit]>)[0];
    expect(url).toBe(`https://internal.example.invalid/v1/ops/operator-console/statutory-discounts/reviews/${decisionId}/evidence`);
    expect(init).toMatchObject({ method: "GET", cache: "no-store", credentials: "same-origin" });
    const headers = init?.headers as Record<string, string>;
    expect(headers.Authorization).toBeUndefined();
    expect(headers["X-ExitPass-Service-Identity-Id"]).toBeUndefined();
    expect(headers["X-Object-Key"]).toBeUndefined();
    expect(headers["X-ExitPass-Permissions"]).toBeUndefined();
    expect(headers["X-Operator-User-Id"]).toBeUndefined();
    expect(headers["X-ExitPass-User-Id"]).toBeUndefined();
    expect(headers["X-Site-Id"]).toBeUndefined();
    expect(headers["X-Site-Group-Id"]).toBeUndefined();
  });

  it("uses a CSRF-protected preview POST body, keeps evidence identifiers out of URLs, and accepts only JPEG or PNG", async () => {
    const fetchMock = vi.fn(async () =>
      new Response(new Uint8Array([137, 80, 78, 71]), {
        status: 200,
        headers: { "Content-Type": "image/png", "Cache-Control": "no-store" }
      })
    );
    vi.stubGlobal("fetch", fetchMock);

    const preview = await createHttpOperatorConsoleApiClient({ csrfToken: () => "csrf-token" }).getStatutoryEvidencePreview(decisionId, itemReference);

    expect(preview.contentType).toBe("image/png");
    expect(preview.blob.size).toBe(4);
    const [url, init] = (fetchMock.mock.calls as unknown as Array<[string, RequestInit]>)[0];
    expect(url).toBe(
      `/v1/ops/operator-console/statutory-discounts/reviews/${decisionId}/evidence/preview`
    );
    expect(url).not.toContain(itemReference);
    expect(init).toMatchObject({ method: "POST", cache: "no-store", credentials: "same-origin" });
    expect(init.headers).toMatchObject({ "X-CSRF-Token": "csrf-token" });
    expect(JSON.parse(String(init.body))).toEqual({ evidenceItemReference: itemReference });
  });

  it("fails safely for malformed metadata and never reflects backend diagnostics", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response("{not-json", { status: 200 })));

    await expect(createHttpOperatorConsoleApiClient().getStatutoryEvidenceReview(decisionId)).rejects.toEqual(
      expect.objectContaining({
        errorCode: "OPERATOR_CONSOLE_STATUTORY_EVIDENCE_REVIEW_MALFORMED",
        message: "Evidence details are temporarily unavailable. Try again."
      })
    );
  });

  it("maps authorization, missing evidence, storage outage, and unsupported media without raw errors", async () => {
    const client = createHttpOperatorConsoleApiClient({ csrfToken: () => "csrf-token" });
    vi.stubGlobal("fetch", vi.fn(async () => jsonResponse({ errorCode: "OPERATOR_CONSOLE_STATUTORY_EVIDENCE_PREVIEW_FORBIDDEN", detail: "bucket secret" }, 403)));
    await expect(client.getStatutoryEvidencePreview(decisionId, itemReference)).rejects.toEqual(
      expect.objectContaining({ status: "access-denied", message: "You no longer have access to this evidence." })
    );

    vi.stubGlobal("fetch", vi.fn(async () => jsonResponse({ errorCode: "NOT_FOUND", detail: "object key" }, 404)));
    await expect(client.getStatutoryEvidencePreview(decisionId, itemReference)).rejects.toEqual(
      expect.objectContaining({ status: "not-found", message: "The evidence could not be found." })
    );

    vi.stubGlobal("fetch", vi.fn(async () => jsonResponse({ errorCode: "OPERATOR_CONSOLE_EVIDENCE_PREVIEW_STORAGE_UNAVAILABLE", detail: "provider endpoint" }, 503)));
    await expect(client.getStatutoryEvidencePreview(decisionId, itemReference)).rejects.toEqual(
      expect.objectContaining({ message: "Evidence preview is temporarily unavailable. Try again." })
    );

    vi.stubGlobal("fetch", vi.fn(async () => new Response("PDF", { status: 200, headers: { "Content-Type": "application/pdf" } })));
    await expect(client.getStatutoryEvidencePreview(decisionId, itemReference)).rejects.toEqual(
      expect.objectContaining({ errorCode: "STATUTORY_EVIDENCE_PREVIEW_UNSUPPORTED_MEDIA" })
    );
  });
});

function reviewResponse() {
  return {
    statutoryDiscountDecisionCommandId: decisionId,
    evidenceSetReference: "33000000-0000-0000-0000-000000000001",
    sourceChannel: "WEBPAY",
    decisionResultStatus: "PENDING_OPERATOR_REVIEW",
    reviewStatus: "PENDING",
    evidenceRequired: true,
    evidenceRecorded: true,
    setStatus: "CURRENT",
    retentionStatus: "ACTIVE",
    deletionStatus: "NONE",
    holdActive: false,
    replacementPosture: "CURRENT",
    items: [
      {
        evidenceItemReference: itemReference,
        documentType: "PWD_ID",
        itemRole: "PRIMARY_IDENTITY_DOCUMENT",
        declaredContentType: "image/png",
        authoritativeContentType: "image/png",
        contentLength: 4096,
        uploadStatus: "FINALIZED",
        validationStatus: "VALID",
        scanStatus: "CLEAN",
        reviewabilityStatus: "REVIEWABLE",
        bindingStatus: "BOUND",
        retentionStatus: "ACTIVE",
        deletionStatus: "NONE",
        holdActive: false,
        finalizedAt: "2026-08-01T08:00:00+08:00",
        previewPermitted: true
      }
    ],
    correlationId: "34000000-0000-0000-0000-000000000001"
  };
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" }
  });
}
