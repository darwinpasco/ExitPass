import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createMockOperatorConsoleApiClient } from "./apiClient";
import { StatutoryEvidenceReviewPanel, previewDenialMessage } from "./StatutoryEvidenceReviewPanel";
import type { OperatorConsoleApiError, StatutoryEvidenceReview, StatutoryEvidenceReviewItem } from "./types";

const decisionId = "41000000-0000-0000-0000-000000000001";
const itemReference = "42000000-0000-0000-0000-000000000001";
const objectUrl = "blob:http://localhost/2eecf466-ffde-4802-9d25-c3f67460777f";

beforeEach(() => {
  Object.defineProperty(URL, "createObjectURL", {
    configurable: true,
    value: vi.fn(() => objectUrl)
  });
  Object.defineProperty(URL, "revokeObjectURL", {
    configurable: true,
    value: vi.fn(() => undefined)
  });
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe("StatutoryEvidenceReviewPanel", () => {
  it("renders review-safe metadata and the JPEG/PNG eligibility posture", async () => {
    renderPanel(reviewFixture({
      items: [
        itemFixture({ authoritativeContentType: "image/jpeg", itemRole: "PRIMARY_IDENTITY_DOCUMENT" }),
        itemFixture({ evidenceItemReference: "42000000-0000-0000-0000-000000000002", authoritativeContentType: "image/png", itemRole: "SUPPORTING_DOCUMENT" })
      ]
    }));

    expect(screen.getByRole("status")).toHaveTextContent("Loading review-safe evidence metadata");
    expect(await screen.findByRole("heading", { name: "Primary identity document" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Supporting document" })).toBeInTheDocument();
    expect(screen.getByText(/JPEG image/)).toBeInTheDocument();
    expect(screen.getByText(/PNG image/)).toBeInTheDocument();
    expect(screen.getAllByText("Eligible for preview")).toHaveLength(2);
    expect(screen.getByText("Active hold")).toBeInTheDocument();
    expect(screen.getByText(/Replacement pending/i)).toBeInTheDocument();
    expect(document.body.innerHTML).not.toContain(decisionId);
    expect(document.body.innerHTML).not.toContain(itemReference);
  });

  it.each([
    ["STATUTORY_EVIDENCE_VALIDATION_PENDING", "Evidence is still being validated."],
    ["STATUTORY_EVIDENCE_VALIDATION_FAILED", "Evidence cannot be reviewed because validation failed."],
    ["STATUTORY_EVIDENCE_SCAN_PENDING", "Security scanning is still in progress."],
    ["STATUTORY_EVIDENCE_SCANNER_UNAVAILABLE", "Security scanning is temporarily unavailable."],
    ["STATUTORY_EVIDENCE_MALWARE_DETECTED", "unsafe content was detected"],
    ["STATUTORY_EVIDENCE_NOT_REVIEWABLE", "not available for review"],
    ["STATUTORY_EVIDENCE_STALE", "no longer current"],
    ["REPLACED", "has been replaced"],
    ["DELETED", "no longer available"],
    ["STATUTORY_EVIDENCE_DELETION_IN_PROGRESS", "pending deletion or unavailable"],
    ["STATUTORY_EVIDENCE_PREVIEW_UNSUPPORTED_MEDIA", "file type cannot be previewed"]
  ])("maps %s to a controlled non-previewable message", async (code, expected) => {
    renderPanel(reviewFixture({ items: [itemFixture({ previewPermitted: false, previewDenialReason: code })] }));

    expect(await screen.findByText(new RegExp(expected, "i"))).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Preview primary identity document/i })).toBeDisabled();
  });

  it("renders empty, denied, missing, and unavailable metadata states safely", async () => {
    const { rerender } = renderPanel(reviewFixture({ items: [] }));
    expect(await screen.findByText("No reviewable evidence metadata is available for this request.")).toBeInTheDocument();

    rerender(panel(createMockOperatorConsoleApiClient({ statutoryEvidenceReviewAuthorized: false })));
    expect(await screen.findByRole("alert")).toHaveTextContent("current access");

    rerender(panel(createMockOperatorConsoleApiClient({
      statutoryEvidenceReviewError: apiError("not-found", "raw database row was missing")
    })));
    expect(await screen.findByText(/could not be found or is outside your authorized scope/i)).toBeInTheDocument();
    expect(document.body).not.toHaveTextContent("raw database row");

    rerender(panel(createMockOperatorConsoleApiClient({
      statutoryEvidenceReviewError: apiError("error", "provider endpoint https://storage.invalid")
    })));
    expect(await screen.findByRole("alert")).toHaveTextContent("temporarily unavailable");
    expect(document.body).not.toHaveTextContent("storage.invalid");
    expect(screen.getByRole("button", { name: "Retry evidence metadata" })).toBeInTheDocument();
  });

  it("loads preview bytes only on explicit action and revokes the temporary object URL on close", async () => {
    const previewClient = createMockOperatorConsoleApiClient({ statutoryEvidenceReview: reviewFixture() });
    const previewSpy = vi.spyOn(previewClient, "getStatutoryEvidencePreview");
    render(panel(previewClient));

    const previewButton = await screen.findByRole("button", { name: /Preview primary identity document/i });
    expect(previewSpy).not.toHaveBeenCalled();
    await userEvent.click(previewButton);

    const dialog = await screen.findByRole("dialog", { name: "Primary identity document" });
    expect(previewSpy).toHaveBeenCalledTimes(1);
    const image = await within(dialog).findByRole("img");
    expect(image).toHaveAttribute("src", objectUrl);
    expect(URL.createObjectURL).toHaveBeenCalledWith(expect.any(Blob));
    expect(within(dialog).getByRole("status")).toHaveTextContent("Preparing the secure image preview");
    expect(within(dialog).queryByRole("button", { name: "Zoom in" })).not.toBeInTheDocument();
    fireEvent.load(image);
    expect(within(dialog).getByRole("button", { name: "Zoom in" })).toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: "Fit evidence to view" })).toBeInTheDocument();
    expect(findProhibitedGuidOccurrences(document)).toEqual([]);

    await userEvent.click(within(dialog).getByRole("button", { name: "Close evidence preview" }));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(URL.revokeObjectURL).toHaveBeenCalledWith(objectUrl);
    await waitFor(() => expect(previewButton).toHaveFocus());
  });

  it("replaces an image decode failure with a controlled retry state and revokes the failed URL", async () => {
    const client = createMockOperatorConsoleApiClient({ statutoryEvidenceReview: reviewFixture() });
    render(panel(client));

    await userEvent.click(await screen.findByRole("button", { name: /Preview primary identity document/i }));
    const dialog = await screen.findByRole("dialog");
    fireEvent.error(await within(dialog).findByRole("img"));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent("could not be displayed");
    expect(within(dialog).queryByRole("img")).not.toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: "Retry preview" })).toBeInTheDocument();
    expect(URL.revokeObjectURL).toHaveBeenCalledWith(objectUrl);
  });

  it("supports Escape, traps focus, and clears preview on decision or scope change and unmount", async () => {
    const client = createMockOperatorConsoleApiClient({ statutoryEvidenceReview: reviewFixture() });
    const { rerender, unmount } = render(panel(client));
    const previewButton = await screen.findByRole("button", { name: /Preview primary identity document/i });
    await userEvent.click(previewButton);
    const dialog = await screen.findByRole("dialog");
    const close = within(dialog).getByRole("button", { name: "Close evidence preview" });
    await waitFor(() => expect(close).toHaveFocus());
    await userEvent.keyboard("{Escape}");
    await waitFor(() => expect(previewButton).toHaveFocus());

    await userEvent.click(previewButton);
    await screen.findByRole("img");
    rerender(
      <StatutoryEvidenceReviewPanel
        client={client}
        decisionId="41000000-0000-0000-0000-000000000002"
        authorityContextKey="group-2:site-2"
        entitlementLabel="PWD"
      />
    );
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(URL.revokeObjectURL).toHaveBeenCalledWith(objectUrl);

    unmount();
    expect(URL.revokeObjectURL).toHaveBeenCalled();
  });

  it("shows a safe retry for storage outage and a non-retryable stale response", async () => {
    const storageClient = createMockOperatorConsoleApiClient({
      statutoryEvidenceReview: reviewFixture(),
      statutoryEvidencePreviewError: apiError(
        "error",
        "raw provider failed",
        "OPERATOR_CONSOLE_EVIDENCE_PREVIEW_STORAGE_UNAVAILABLE"
      )
    });
    const { rerender } = render(panel(storageClient));
    await userEvent.click(await screen.findByRole("button", { name: /Preview primary identity document/i }));
    expect(await screen.findByRole("alert")).toHaveTextContent("preview service is temporarily unavailable");
    expect(screen.getByRole("button", { name: "Retry preview" })).toBeInTheDocument();
    expect(document.body).not.toHaveTextContent("raw provider failed");

    const staleClient = createMockOperatorConsoleApiClient({
      statutoryEvidenceReview: reviewFixture(),
      statutoryEvidencePreviewError: apiError("error", "object version", "OPERATOR_CONSOLE_EVIDENCE_PREVIEW_STALE")
    });
    rerender(panel(staleClient));
    await userEvent.click(await screen.findByRole("button", { name: /Preview primary identity document/i }));
    expect(await screen.findByRole("alert")).toHaveTextContent("no longer current");
    expect(screen.queryByRole("button", { name: "Retry preview" })).not.toBeInTheDocument();
    expect(document.body).not.toHaveTextContent("object version");
  });

  it("does not expose mutations, full identifiers, storage internals, or persistent browser state", async () => {
    const storageSetItem = vi.spyOn(Storage.prototype, "setItem");
    renderPanel(reviewFixture());
    await screen.findByRole("heading", { name: "Primary identity document" });

    expect(screen.queryByRole("button", { name: /approve|reject|download|upload|delete|replace|hold/i })).not.toBeInTheDocument();
    expect(findProhibitedGuidOccurrences(document)).toEqual([]);
    expect(document.body).not.toHaveTextContent(/bucket|object key|checksum|signed url|scanner endpoint|provider credential/i);
    expect(storageSetItem).not.toHaveBeenCalled();
  });

  it("does not treat preview as approval, rejection, verification, or payable-basis authority", async () => {
    const client = createMockOperatorConsoleApiClient({ statutoryEvidenceReview: reviewFixture() });
    const decisionSpy = vi.spyOn(client, "submitStatutoryDiscountDecision");
    const verificationSpy = vi.spyOn(client, "captureStatutoryDiscountEvidence");
    render(panel(client));

    await userEvent.click(await screen.findByRole("button", { name: /Preview primary identity document/i }));
    await screen.findByRole("img");
    expect(decisionSpy).not.toHaveBeenCalled();
    expect(verificationSpy).not.toHaveBeenCalled();
  });
});

describe("previewDenialMessage", () => {
  it("fails safely for unknown controlled codes", () => {
    expect(previewDenialMessage("FUTURE_BACKEND_STATE")).toBe("This evidence is not currently eligible for preview.");
  });
});

function renderPanel(review: StatutoryEvidenceReview) {
  return render(panel(createMockOperatorConsoleApiClient({ statutoryEvidenceReview: review })));
}

function panel(client: ReturnType<typeof createMockOperatorConsoleApiClient>) {
  return (
    <StatutoryEvidenceReviewPanel
      client={client}
      decisionId={decisionId}
      authorityContextKey="group-1:site-1"
      entitlementLabel="PWD"
    />
  );
}

function reviewFixture(overrides: Partial<StatutoryEvidenceReview> = {}): StatutoryEvidenceReview {
  return {
    statutoryDiscountDecisionCommandId: decisionId,
    sourceChannel: "WEBPAY",
    decisionResultStatus: "PENDING_OPERATOR_REVIEW",
    reviewStatus: "PENDING",
    evidenceRequired: true,
    evidenceRecorded: true,
    setStatus: "CURRENT",
    retentionStatus: "ACTIVE",
    deletionStatus: "NONE",
    holdActive: true,
    replacementPosture: "REPLACEMENT_PENDING",
    items: [itemFixture()],
    ...overrides
  };
}

function itemFixture(overrides: Partial<StatutoryEvidenceReviewItem> = {}): StatutoryEvidenceReviewItem {
  return {
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
    previewPermitted: true,
    ...overrides
  };
}

function apiError(status: OperatorConsoleApiError["status"], message: string, errorCode?: string): OperatorConsoleApiError {
  return { status, message, errorCode };
}

function findProhibitedGuidOccurrences(documentRoot: Document) {
  const guidPattern = /\b[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\b/gi;
  const findings: Array<{ type: string; tag: string; attribute?: string; value: string }> = [];

  for (const element of documentRoot.querySelectorAll("*")) {
    for (const attribute of element.attributes) {
      guidPattern.lastIndex = 0;
      if (
        guidPattern.test(attribute.value) &&
        !(element instanceof HTMLImageElement && attribute.name === "src" && attribute.value.startsWith("blob:"))
      ) {
        findings.push({ type: "attribute", tag: element.tagName, attribute: attribute.name, value: attribute.value });
      }
    }

    for (const node of element.childNodes) {
      guidPattern.lastIndex = 0;
      if (node.nodeType === Node.TEXT_NODE && node.textContent && guidPattern.test(node.textContent)) {
        findings.push({ type: "text", tag: element.tagName, value: node.textContent });
      }
    }

    if (element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement || element instanceof HTMLSelectElement) {
      guidPattern.lastIndex = 0;
      if (guidPattern.test(element.value)) {
        findings.push({ type: "form-value", tag: element.tagName, value: element.value });
      }
    }
  }

  return findings;
}
