import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { StatutoryEvidenceCapture } from "./StatutoryEvidenceCapture";
import { StatutoryEvidenceError } from "./statutoryEvidence";
import type { WebPayStatutoryEvidenceChannelResponse } from "./types";

const mocks = vi.hoisted(() => ({
  bootstrap: vi.fn(),
  status: vi.fn(),
  checksum: vi.fn(),
  session: vi.fn(),
  upload: vi.fn(),
  finalize: vi.fn(),
  preview: vi.fn()
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
    finalizeStatutoryEvidenceUpload: mocks.finalize,
    retrieveStatutoryEvidencePreview: mocks.preview
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
    vi.resetAllMocks();
    mocks.bootstrap.mockResolvedValue(evidence());
    mocks.status.mockResolvedValue(evidence());
    mocks.checksum.mockResolvedValue("a".repeat(64));
    mocks.session.mockResolvedValue({ opaqueUploadSessionReference: "opaque-session" });
    mocks.upload.mockImplementation(async (_reference, file, onProgress) => {
      onProgress({ loaded: file.size, total: file.size, percent: 100 });
    });
    mocks.finalize.mockResolvedValue(evidence({ lifecycleClassification: "VALIDATION_PENDING", replacementPosture: "REPLACEMENT_NOT_ALLOWED" }));
    mocks.preview.mockResolvedValue(new Blob([new Uint8Array([0xff, 0xd8, 0xff, 0xd9])], { type: "image/jpeg" }));
    localStorage.clear();
    sessionStorage.clear();
    Object.defineProperty(URL, "createObjectURL", { configurable: true, value: vi.fn(() => "blob:statutory-evidence") });
    Object.defineProperty(URL, "revokeObjectURL", { configurable: true, value: vi.fn() });
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

  it.each(["REQUIRED_NOT_STARTED", "ITEM_CREATED", "UPLOAD_SESSION_AVAILABLE"])(
    "keeps photo capture available while a required request is awaiting review in %s",
    async (lifecycleClassification) => {
      mocks.bootstrap.mockResolvedValue(evidence({ lifecycleClassification }));

      render(<StatutoryEvidenceCapture statutoryDiscountDecisionCommandId={decisionId} />);

      expect(await screen.findByLabelText(/choose or take a clear photo/i)).toBeInTheDocument();
      expect(screen.getByRole("button", { name: /^upload photo$/i })).toBeInTheDocument();
    }
  );

  it("ignores bootstrap responses from an obsolete statutory decision generation", async () => {
    let resolveObsolete: ((value: WebPayStatutoryEvidenceChannelResponse) => void) | undefined;
    mocks.bootstrap
      .mockImplementationOnce(() => new Promise<WebPayStatutoryEvidenceChannelResponse>((resolve) => { resolveObsolete = resolve; }))
      .mockResolvedValueOnce(evidence({ lifecycleClassification: "ITEM_CREATED" }));
    const nextDecisionId = "dddddddd-dddd-4ddd-8ddd-dddddddddddd";
    const view = render(<StatutoryEvidenceCapture statutoryDiscountDecisionCommandId={decisionId} />);

    view.rerender(<StatutoryEvidenceCapture statutoryDiscountDecisionCommandId={nextDecisionId} />);
    expect(await screen.findByLabelText(/choose or take a clear photo/i)).toBeInTheDocument();

    await act(async () => resolveObsolete?.(evidence({
      lifecycleClassification: "UNKNOWN_FAIL_CLOSED",
      replacementPosture: "REPLACEMENT_NOT_ALLOWED"
    })));

    expect(screen.getAllByText("Photo required").length).toBeGreaterThan(0);
    expect(screen.queryByText("Status unavailable")).not.toBeInTheDocument();
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
    ["APPROVED", "Approved", /applying.*automatically/i],
    ["APPLIED", "Applied", /included in the amount due/i],
    ["MALWARE_DETECTED", "Unsafe file detected", /cannot be used/i],
    ["UNKNOWN_FAIL_CLOSED", "Status unavailable", /could not be confirmed safely/i]
  ])("renders %s without collapsing lifecycle authority", async (lifecycleState, label, message) => {
    mocks.bootstrap.mockResolvedValue(evidence({ lifecycleClassification: lifecycleState, replacementPosture: "REPLACEMENT_NOT_ALLOWED" }));
    render(<StatutoryEvidenceCapture statutoryDiscountDecisionCommandId={decisionId} />);
    expect(await screen.findByText(label)).toBeInTheDocument();
    expect(screen.getByText(message)).toBeInTheDocument();
  });

  it("rediscovers the protected submitted photo without browser-persisted image bytes", async () => {
    mocks.bootstrap.mockResolvedValue(evidence({
      lifecycleClassification: "REVIEWABLE",
      replacementPosture: "REPLACEMENT_ALLOWED"
    }));
    render(<StatutoryEvidenceCapture statutoryDiscountDecisionCommandId={decisionId} />);

    expect(await screen.findByAltText("Submitted statutory entitlement evidence")).toHaveAttribute("src", "blob:statutory-evidence");
    expect(mocks.preview).toHaveBeenCalledWith(
      decisionId,
      "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
      expect.any(Function),
      expect.any(AbortSignal)
    );
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });

  it("does not let an older status response replace a successfully finalized REVIEWABLE photo", async () => {
    let resolveStaleStatus: ((value: WebPayStatutoryEvidenceChannelResponse) => void) | undefined;
    mocks.status.mockImplementationOnce(() => new Promise<WebPayStatutoryEvidenceChannelResponse>((resolve) => { resolveStaleStatus = resolve; }));
    mocks.finalize.mockResolvedValueOnce(evidence({
      lifecycleClassification: "REVIEWABLE",
      replacementPosture: "REPLACEMENT_ALLOWED",
      readyForReview: true
    }));
    render(<StatutoryEvidenceCapture statutoryDiscountDecisionCommandId={decisionId} />);
    const input = await screen.findByLabelText(/choose or take a clear photo/i);

    await userEvent.click(screen.getByRole("button", { name: /refresh evidence status/i }));
    await userEvent.upload(input, new File([new Uint8Array([1, 2, 3])], "proof.jpg", { type: "image/jpeg" }));
    await userEvent.click(screen.getByRole("button", { name: /^upload photo$/i }));
    expect(await screen.findByText("Ready for review")).toBeInTheDocument();

    await act(async () => resolveStaleStatus?.(evidence({ lifecycleClassification: "REQUIRED_NOT_STARTED" })));

    expect(screen.getByText("Ready for review")).toBeInTheDocument();
    expect(screen.getByText(/does not mean.*approved/i)).toBeInTheDocument();
  });

  it("automatically reconciles a recoverable evidence conflict without requiring manual refresh", async () => {
    mocks.session.mockRejectedValueOnce(new StatutoryEvidenceError(
      "WEBPAY_STATUTORY_EVIDENCE_CONFLICT",
      "The evidence status changed. Refresh the status before trying again.",
      true
    ));
    mocks.status.mockResolvedValueOnce(evidence({ lifecycleClassification: "UPLOAD_SESSION_AVAILABLE" }));
    render(<StatutoryEvidenceCapture statutoryDiscountDecisionCommandId={decisionId} />);
    const input = await screen.findByLabelText(/choose or take a clear photo/i);
    await userEvent.upload(input, new File([new Uint8Array([1, 2, 3])], "proof.jpg", { type: "image/jpeg" }));

    await userEvent.click(screen.getByRole("button", { name: /^upload photo$/i }));

    await waitFor(() => expect(mocks.status).toHaveBeenCalled());
    expect(screen.getByLabelText(/choose or take a clear photo/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^upload photo$/i })).toBeInTheDocument();
    expect(screen.queryByText(/refresh the status before trying again/i)).not.toBeInTheDocument();
  });

  it("retries authoritative readback automatically when reconciliation itself briefly conflicts", async () => {
    const conflict = new StatutoryEvidenceError(
      "WEBPAY_STATUTORY_EVIDENCE_CONFLICT",
      "The evidence status changed. Refresh the status before trying again.",
      true
    );
    mocks.bootstrap.mockRejectedValueOnce(conflict);
    mocks.status
      .mockRejectedValueOnce(conflict)
      .mockResolvedValueOnce(evidence({ lifecycleClassification: "ITEM_CREATED" }));

    render(<StatutoryEvidenceCapture statutoryDiscountDecisionCommandId={decisionId} />);

    expect(await screen.findByLabelText(/choose or take a clear photo/i)).toBeInTheDocument();
    expect(mocks.status).toHaveBeenCalledTimes(2);
    expect(screen.queryByText(/refresh the status before trying again/i)).not.toBeInTheDocument();
  });

  it("automatically advances finalized evidence processing to REVIEWABLE and displays the photo", async () => {
    vi.useFakeTimers();
    try {
      mocks.status.mockResolvedValueOnce(evidence({
        lifecycleClassification: "REVIEWABLE",
        replacementPosture: "REPLACEMENT_ALLOWED",
        readyForReview: true
      }));
      render(<StatutoryEvidenceCapture statutoryDiscountDecisionCommandId={decisionId} />);
      await act(async () => undefined);
      const input = screen.getByLabelText(/choose or take a clear photo/i);
      const file = new File([new Uint8Array([1, 2, 3])], "proof.jpg", { type: "image/jpeg" });
      fireEvent.change(input, { target: { files: { 0: file, length: 1, item: () => file } } });
      await act(async () => fireEvent.click(screen.getByRole("button", { name: /^upload photo$/i })));
      expect(screen.getByText("Verification pending")).toBeInTheDocument();

      await act(async () => vi.advanceTimersByTimeAsync(3000));

      expect(screen.getByText("Ready for review")).toBeInTheDocument();
      expect(screen.getByAltText("Submitted statutory entitlement evidence")).toBeInTheDocument();
    } finally {
      vi.useRealTimers();
    }
  });

  it("restores upload controls when the component reloads before evidence is uploaded", async () => {
    const first = render(<StatutoryEvidenceCapture statutoryDiscountDecisionCommandId={decisionId} />);
    expect(await screen.findByLabelText(/choose or take a clear photo/i)).toBeInTheDocument();
    first.unmount();

    render(<StatutoryEvidenceCapture statutoryDiscountDecisionCommandId={decisionId} />);

    expect(await screen.findByLabelText(/choose or take a clear photo/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^upload photo$/i })).toBeInTheDocument();
  });

  it("restores the submitted photo when the component reloads after evidence is REVIEWABLE", async () => {
    mocks.bootstrap.mockResolvedValue(evidence({
      lifecycleClassification: "REVIEWABLE",
      replacementPosture: "REPLACEMENT_NOT_ALLOWED",
      readyForReview: true
    }));
    const first = render(<StatutoryEvidenceCapture statutoryDiscountDecisionCommandId={decisionId} />);
    expect(await screen.findByAltText("Submitted statutory entitlement evidence")).toBeInTheDocument();
    first.unmount();

    render(<StatutoryEvidenceCapture statutoryDiscountDecisionCommandId={decisionId} />);

    expect(await screen.findByAltText("Submitted statutory entitlement evidence")).toBeInTheDocument();
    expect(mocks.preview).toHaveBeenCalledTimes(2);
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
    expect(screen.getByLabelText(/choose or take a clear photo/i)).toBeInTheDocument();
    expect(screen.queryByText(/refresh the evidence status before trying again/i)).not.toBeInTheDocument();
  });
});
