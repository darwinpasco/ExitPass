import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { StatutoryEvidenceCapture } from "./StatutoryEvidenceCapture";
import type { WebPayStatutoryEvidenceChannelResponse } from "./types";

const mocks = vi.hoisted(() => ({
  bootstrap: vi.fn(),
  status: vi.fn(),
  checksum: vi.fn(),
  session: vi.fn(),
  upload: vi.fn(),
  finalize: vi.fn()
}));

vi.mock("./statutoryEvidence", async () => {
  const actual = await vi.importActual<typeof import("./statutoryEvidence")>("./statutoryEvidence");
  return {
    ...actual,
    bootstrapStatutoryEvidence: mocks.bootstrap,
    retrieveStatutoryEvidenceStatus: mocks.status,
    computeSha256: mocks.checksum,
    requestStatutoryEvidenceUploadSession: mocks.session,
    uploadStatutoryEvidence: mocks.upload,
    finalizeStatutoryEvidenceUpload: mocks.finalize
  };
});

const decisionId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";

function evidence(overrides: Partial<WebPayStatutoryEvidenceChannelResponse> = {}): WebPayStatutoryEvidenceChannelResponse {
  return {
    classification: "FOUND",
    retryable: false,
    correlationId: "support-reference",
    evidenceRequired: true,
    evidenceSetReference: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
    evidenceItemReference: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
    allowedContentTypes: ["image/jpeg", "image/png"],
    maximumContentLengthBytes: 5_000_000,
    maximumImageWidth: 1920,
    maximumImageHeight: 1080,
    requiredItemRole: "ENTITLEMENT_ID_FRONT",
    lifecycleClassification: "REQUIRED_NOT_STARTED",
    replacementPosture: "REPLACEMENT_ALLOWED",
    readyForReview: false,
    ...overrides
  };
}

describe("StatutoryEvidenceCapture", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.bootstrap.mockResolvedValue(evidence());
    mocks.status.mockResolvedValue(evidence());
    mocks.checksum.mockResolvedValue("a".repeat(64));
    mocks.session.mockResolvedValue({ opaqueUploadSessionReference: "opaque-session" });
    mocks.upload.mockImplementation(async (_reference, file, onProgress) => {
      onProgress({ loaded: file.size, total: file.size, percent: 100 });
    });
    mocks.finalize.mockResolvedValue(evidence({ lifecycleClassification: "VALIDATION_PENDING", replacementPosture: "REPLACEMENT_NOT_ALLOWED" }));
    localStorage.clear();
    sessionStorage.clear();
  });

  it("bootstraps authoritative rules and exposes a mobile-capable single file input", async () => {
    render(<StatutoryEvidenceCapture statutoryDiscountDecisionCommandId={decisionId} />);

    const input = await screen.findByLabelText(/choose or take a clear photo/i);
    expect(mocks.bootstrap).toHaveBeenCalledWith(decisionId, expect.any(Function), expect.any(AbortSignal));
    expect(input).toHaveAttribute("accept", "image/jpeg,image/png");
    expect(input).toHaveAttribute("capture", "environment");
    expect(input).not.toHaveAttribute("multiple");
    expect(screen.getByText(/4.8 MB/i)).toBeInTheDocument();
    expect(screen.getByText(/Entitlement id front/i)).toBeInTheDocument();
    expect(document.body).not.toHaveTextContent(decisionId);
    expect(document.body).not.toHaveTextContent("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    expect(document.body).not.toHaveTextContent("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
  });

  it("rejects PDF before requesting an upload session", async () => {
    render(<StatutoryEvidenceCapture statutoryDiscountDecisionCommandId={decisionId} />);
    const input = await screen.findByLabelText(/choose or take a clear photo/i);

    await userEvent.upload(input, new File(["pdf"], "proof.pdf", { type: "application/pdf" }), { applyAccept: false });

    expect(screen.getByRole("alert")).toHaveTextContent(/JPEG or PNG/i);
    expect(mocks.session).not.toHaveBeenCalled();
  });

  it.each([
    ["image/jpeg", "proof.jpg"],
    ["image/png", "proof.png"]
  ])("uploads and finalizes a selected %s without persisting evidence authority", async (type, name) => {
    render(<StatutoryEvidenceCapture statutoryDiscountDecisionCommandId={decisionId} />);
    const input = await screen.findByLabelText(/choose or take a clear photo/i);
    const file = new File([new Uint8Array([1, 2, 3])], name, { type });

    await userEvent.upload(input, file);
    await userEvent.click(screen.getByRole("button", { name: /upload photo/i }));

    await waitFor(() => expect(mocks.finalize).toHaveBeenCalledWith("opaque-session"));
    expect(mocks.upload).toHaveBeenCalledWith("opaque-session", file, expect.any(Function), expect.any(AbortSignal));
    expect(screen.getByText(/verification pending/i)).toBeInTheDocument();
    expect(screen.queryByText(/^Approved$/i)).not.toBeInTheDocument();
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });

  it("keeps capture disabled when replacement is not allowed", async () => {
    mocks.bootstrap.mockResolvedValue(evidence({
      lifecycleClassification: "REVIEW_PENDING",
      replacementPosture: "REPLACEMENT_NOT_ALLOWED"
    }));

    render(<StatutoryEvidenceCapture statutoryDiscountDecisionCommandId={decisionId} />);

    expect(await screen.findByText(/cannot be replaced/i)).toBeInTheDocument();
    expect(screen.queryByLabelText(/choose or take a clear photo/i)).not.toBeInTheDocument();
  });

  it.each([
    ["REVIEWABLE", "Ready for review", /does not mean.*approved/i],
    ["APPROVED", "Approved", /applied only.*payment-time flow/i],
    ["APPLIED", "Applied", /authoritative payable basis/i],
    ["MALWARE_DETECTED", "Unsafe file detected", /cannot be used/i],
    ["UNKNOWN_FAIL_CLOSED", "Status unavailable", /could not be confirmed safely/i]
  ])("renders %s without collapsing lifecycle authority", async (lifecycleState, label, message) => {
    mocks.bootstrap.mockResolvedValue(evidence({ lifecycleClassification: lifecycleState, replacementPosture: "REPLACEMENT_NOT_ALLOWED" }));
    render(<StatutoryEvidenceCapture statutoryDiscountDecisionCommandId={decisionId} />);
    expect(await screen.findByText(label)).toBeInTheDocument();
    expect(screen.getByText(message)).toBeInTheDocument();
  });

  it("reconciles authoritative evidence state after an upload is cancelled", async () => {
    mocks.upload.mockImplementationOnce((_reference, _file, _onProgress, signal: AbortSignal) => (
      new Promise((_resolve, reject) => {
        signal.addEventListener("abort", () => reject(new DOMException("cancelled", "AbortError")), { once: true });
      })
    ));
    mocks.status.mockResolvedValueOnce(evidence({ lifecycleClassification: "UPLOAD_IN_PROGRESS" }));

    render(<StatutoryEvidenceCapture statutoryDiscountDecisionCommandId={decisionId} />);
    const input = await screen.findByLabelText(/choose or take a clear photo/i);
    await userEvent.upload(input, new File([new Uint8Array([1, 2, 3])], "proof.jpg", { type: "image/jpeg" }));
    await userEvent.click(screen.getByRole("button", { name: /upload photo/i }));
    await userEvent.click(await screen.findByRole("button", { name: /cancel upload/i }));

    await waitFor(() => expect(mocks.status).toHaveBeenCalledWith(
      { statutoryDiscountDecisionCommandId: decisionId },
      expect.any(Function),
      expect.any(AbortSignal)
    ));
    expect(await screen.findByText("Upload incomplete")).toBeInTheDocument();
    expect(screen.getByRole("alert")).toHaveTextContent(/upload was cancelled/i);
  });
});
