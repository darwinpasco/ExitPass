import { useCallback, useEffect, useRef, useState, type KeyboardEvent as ReactKeyboardEvent } from "react";
import { mapApiError, type OperatorConsoleApiClient } from "./apiClient";
import type {
  LoadState,
  OperatorConsoleApiError,
  StatutoryEvidenceReview,
  StatutoryEvidenceReviewItem
} from "./types";

interface StatutoryEvidenceReviewPanelProps {
  client: OperatorConsoleApiClient;
  decisionId?: string;
  authorityContextKey: string;
  entitlementLabel: string;
}

type PreviewState =
  | { status: "closed" }
  | { status: "loading"; item: StatutoryEvidenceReviewItem }
  | { status: "decoding"; item: StatutoryEvidenceReviewItem; objectUrl: string }
  | { status: "loaded"; item: StatutoryEvidenceReviewItem; objectUrl: string }
  | { status: "error"; item: StatutoryEvidenceReviewItem; message: string; retryable: boolean };

export function StatutoryEvidenceReviewPanel({
  client,
  decisionId,
  authorityContextKey,
  entitlementLabel
}: StatutoryEvidenceReviewPanelProps) {
  const [reviewState, setReviewState] = useState<LoadState<StatutoryEvidenceReview>>({ status: "loading" });
  const [refreshToken, setRefreshToken] = useState(0);
  const [previewState, setPreviewState] = useState<PreviewState>({ status: "closed" });
  const [zoom, setZoom] = useState(1);
  const [copyMessage, setCopyMessage] = useState<string | null>(null);
  const previewAbortRef = useRef<AbortController | null>(null);
  const objectUrlRef = useRef<string | null>(null);
  const openerRef = useRef<HTMLButtonElement | null>(null);
  const dialogRef = useRef<HTMLDivElement | null>(null);
  const closeButtonRef = useRef<HTMLButtonElement | null>(null);
  const canReviewEvidence = client.canReviewStatutoryEvidence?.() ?? false;

  const clearPreview = useCallback((restoreFocus: boolean) => {
    previewAbortRef.current?.abort();
    previewAbortRef.current = null;
    if (objectUrlRef.current) {
      URL.revokeObjectURL(objectUrlRef.current);
      objectUrlRef.current = null;
    }
    setPreviewState({ status: "closed" });
    setZoom(1);
    setCopyMessage(null);
    if (restoreFocus) {
      window.requestAnimationFrame(() => openerRef.current?.focus());
    }
  }, []);

  useEffect(() => {
    clearPreview(false);
    if (!decisionId) {
      setReviewState({ status: "empty" });
      return;
    }
    if (!canReviewEvidence) {
      setReviewState({
        status: "access-denied",
        message: "Evidence preview is not available for your current access."
      });
      return;
    }

    const controller = new AbortController();
    setReviewState({ status: "loading" });
    client
      .getStatutoryEvidenceReview(decisionId, controller.signal)
      .then((review) => {
        if (!controller.signal.aborted) {
          setReviewState(review.items.length === 0 ? { status: "empty" } : { status: "loaded", data: review });
        }
      })
      .catch((error) => {
        if (controller.signal.aborted || isAbortError(error)) {
          return;
        }
        const mapped = mapEvidenceReviewError(error);
        setReviewState(
          mapped.status === "access-denied"
            ? { status: "access-denied", message: mapped.message }
            : mapped.status === "not-found"
              ? { status: "not-found" }
              : { status: "error", message: mapped.message }
        );
      });

    return () => {
      controller.abort();
      clearPreview(false);
    };
  }, [authorityContextKey, canReviewEvidence, clearPreview, client, decisionId, refreshToken]);

  useEffect(() => {
    if (previewState.status !== "closed") {
      closeButtonRef.current?.focus();
    }
  }, [previewState.status]);

  async function openPreview(item: StatutoryEvidenceReviewItem, opener: HTMLButtonElement) {
    if (!decisionId || !canReviewEvidence || !item.previewPermitted) {
      return;
    }

    clearPreview(false);
    openerRef.current = opener;
    const controller = new AbortController();
    previewAbortRef.current = controller;
    setPreviewState({ status: "loading", item });

    try {
      const preview = await client.getStatutoryEvidencePreview(decisionId, item.evidenceItemReference, controller.signal);
      if (controller.signal.aborted) {
        return;
      }
      const objectUrl = URL.createObjectURL(preview.blob);
      objectUrlRef.current = objectUrl;
      setPreviewState({ status: "decoding", item, objectUrl });
    } catch (error) {
      if (controller.signal.aborted || isAbortError(error)) {
        return;
      }
      const mapped = mapEvidencePreviewError(error);
      setPreviewState({ status: "error", item, message: mapped.message, retryable: mapped.retryable });
    }
  }

  function retryPreview() {
    if (previewState.status !== "error" || !openerRef.current) {
      return;
    }
    void openPreview(previewState.item, openerRef.current);
  }

  function handlePreviewLoad(objectUrl: string) {
    setPreviewState((current) =>
      current.status === "decoding" && current.objectUrl === objectUrl
        ? { status: "loaded", item: current.item, objectUrl }
        : current
    );
  }

  function handlePreviewDecodeError(objectUrl: string) {
    if (objectUrlRef.current !== objectUrl) {
      return;
    }
    URL.revokeObjectURL(objectUrl);
    objectUrlRef.current = null;
    setPreviewState((current) =>
      (current.status === "decoding" || current.status === "loaded") && current.objectUrl === objectUrl
        ? {
            status: "error",
            item: current.item,
            message: "Evidence preview could not be displayed. Try again.",
            retryable: true
          }
        : current
    );
  }

  function handleDialogKeyDown(event: ReactKeyboardEvent<HTMLDivElement>) {
    if (event.key === "Escape") {
      event.preventDefault();
      clearPreview(true);
      return;
    }
    if (event.key !== "Tab" || !dialogRef.current) {
      return;
    }

    const focusable = Array.from(
      dialogRef.current.querySelectorAll<HTMLElement>('button:not(:disabled), [href], [tabindex]:not([tabindex="-1"])')
    );
    if (focusable.length === 0) {
      return;
    }
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }

  return (
    <section className="panel statutoryEvidenceReviewPanel" aria-labelledby="statutory-evidence-review-title">
      <div className="panelHeader">
        <div>
          <p className="eyebrow">Review evidence</p>
          <h3 id="statutory-evidence-review-title">Secure evidence review</h3>
          <p className="panelIntro">Preview availability is determined by Central PMS at the time of each request.</p>
        </div>
        <button
          type="button"
          className="secondaryButton evidenceRefreshButton"
          onClick={() => setRefreshToken((value) => value + 1)}
          disabled={!decisionId || !canReviewEvidence || reviewState.status === "loading"}
          aria-label="Refresh secure evidence"
        >
          Refresh
        </button>
      </div>

      <p className="privacyNotice" role="note">
        Evidence is sensitive personal information. View it only for this review; do not copy, download, or share it. Access is reauthorized and audited by Central PMS, and previews are temporary.
      </p>

      {reviewState.status === "loading" && (
        <p className="notice" role="status" aria-live="polite">Loading review-safe evidence metadata.</p>
      )}
      {reviewState.status === "empty" && (
        <p className="notice">No reviewable evidence metadata is available for this request.</p>
      )}
      {reviewState.status === "not-found" && (
        <p className="notice" role="status">The evidence could not be found or is outside your authorized scope.</p>
      )}
      {reviewState.status === "access-denied" && (
        <p className="errorMessage" role="alert">{reviewState.message}</p>
      )}
      {reviewState.status === "error" && (
        <div className="evidenceLoadError" role="alert">
          <p className="errorMessage">{reviewState.message}</p>
          <button type="button" className="secondaryButton" onClick={() => setRefreshToken((value) => value + 1)}>
            Retry evidence metadata
          </button>
        </div>
      )}
      {reviewState.status === "loaded" && (
        <EvidenceReviewContent
          review={reviewState.data}
          entitlementLabel={entitlementLabel}
          canReviewEvidence={canReviewEvidence}
          onPreview={(item, opener) => void openPreview(item, opener)}
        />
      )}

      {previewState.status !== "closed" && (
        <div className="evidencePreviewBackdrop" role="presentation">
          <div
            className="evidencePreviewDialog statutoryEvidencePreviewDialog"
            role="dialog"
            aria-modal="true"
            aria-labelledby="evidence-preview-title"
            aria-describedby="evidence-preview-description"
            ref={dialogRef}
            onKeyDown={handleDialogKeyDown}
          >
            <div className="evidencePreviewHeader">
              <div>
                <p className="eyebrow">Temporary secure preview</p>
                <h3 id="evidence-preview-title">{evidenceRoleLabel(previewState.item.itemRole)}</h3>
                <p id="evidence-preview-description">{safeMediaDescription(previewState.item)}</p>
              </div>
              <button
                type="button"
                className="secondaryButton"
                ref={closeButtonRef}
                onClick={() => clearPreview(true)}
                aria-label="Close evidence preview"
              >
                Close
              </button>
            </div>

            {previewState.status === "loading" && (
              <div className="evidencePreviewState" role="status" aria-live="polite">
                Loading the current authorized preview.
              </div>
            )}
            {previewState.status === "error" && (
              <div className="evidencePreviewState" role="alert">
                <p>{previewState.message}</p>
                {previewState.retryable && (
                  <button type="button" onClick={retryPreview}>Retry preview</button>
                )}
              </div>
            )}
            {(previewState.status === "decoding" || previewState.status === "loaded") && (
              <>
                {previewState.status === "loaded" && (
                  <div className="evidencePreviewToolbar" aria-label="Evidence preview controls">
                    <button type="button" onClick={() => setZoom((value) => Math.max(0.5, value - 0.25))} aria-label="Zoom out">
                      Zoom out
                    </button>
                    <span aria-live="polite">{Math.round(zoom * 100)}%</span>
                    <button type="button" onClick={() => setZoom((value) => Math.min(3, value + 0.25))} aria-label="Zoom in">
                      Zoom in
                    </button>
                    <button type="button" className="secondaryButton" onClick={() => setZoom(1)} aria-label="Fit evidence to view">
                      Fit to view
                    </button>
                  </div>
                )}
                {previewState.status === "decoding" && (
                  <div className="evidencePreviewDecodeStatus" role="status" aria-live="polite">
                    Preparing the secure image preview.
                  </div>
                )}
                <div className="evidencePreviewViewport" aria-busy={previewState.status === "decoding"}>
                  <img
                    src={previewState.objectUrl}
                    alt={`${evidenceRoleLabel(previewState.item.itemRole)}, ${safeMediaDescription(previewState.item)}`}
                    className={previewState.status === "decoding" ? "isDecoding" : undefined}
                    style={{ transform: `scale(${zoom})` }}
                    onLoad={() => handlePreviewLoad(previewState.objectUrl)}
                    onError={() => handlePreviewDecodeError(previewState.objectUrl)}
                  />
                </div>
              </>
            )}
          </div>
        </div>
      )}
      <span className="srOnly" aria-live="polite">{copyMessage}</span>
    </section>
  );
}

function EvidenceReviewContent({
  review,
  entitlementLabel,
  canReviewEvidence,
  onPreview
}: {
  review: StatutoryEvidenceReview;
  entitlementLabel: string;
  canReviewEvidence: boolean;
  onPreview: (item: StatutoryEvidenceReviewItem, opener: HTMLButtonElement) => void;
}) {
  return (
    <>
      <dl className="evidenceReviewSummary">
        <div><dt>Entitlement</dt><dd>{entitlementLabel}</dd></div>
        <div><dt>Request channel</dt><dd>{controlledLabel(review.sourceChannel)}</dd></div>
        <div><dt>Review status</dt><dd>{controlledLabel(review.reviewStatus)}</dd></div>
        <div><dt>Evidence set</dt><dd>{controlledLabel(review.setStatus ?? (review.evidenceRecorded ? "RECORDED" : "NOT_RECORDED"))}</dd></div>
        <div><dt>Replacement</dt><dd>{controlledLabel(review.replacementPosture)}</dd></div>
        <div><dt>Hold</dt><dd>{review.holdActive ? "Active hold" : "No active hold"}</dd></div>
        <div><dt>Retention</dt><dd>{controlledLabel(review.retentionStatus ?? "UNKNOWN")}</dd></div>
        <div><dt>Deletion</dt><dd>{controlledLabel(review.deletionStatus ?? "UNKNOWN")}</dd></div>
      </dl>

      <ul className="secureEvidenceList" aria-label="Review-safe evidence items">
        {review.items.map((item, index) => {
          const denialMessage = previewDenialMessage(item.previewDenialReason);
          const previewAvailable = canReviewEvidence && item.previewPermitted;
          return (
            <li key={item.evidenceItemReference} className="secureEvidenceItem">
              <div className="secureEvidenceItemHeader">
                <div>
                  <p className="eyebrow">Evidence {index + 1}</p>
                  <h4>{evidenceRoleLabel(item.itemRole)}</h4>
                  <p>{controlledLabel(item.documentType)} / {safeMediaDescription(item)}</p>
                </div>
                <span className={`statusPill ${item.previewPermitted ? "readiness-ready" : "warningPill"}`}>
                  {item.previewPermitted ? "Eligible for preview" : "Preview unavailable"}
                </span>
              </div>

              <dl className="evidenceLifecycleGrid">
                <div><dt>Upload</dt><dd>{controlledLabel(item.uploadStatus)}</dd></div>
                <div><dt>Validation</dt><dd>{controlledLabel(item.validationStatus)}</dd></div>
                <div><dt>Security scan</dt><dd>{controlledLabel(item.scanStatus)}</dd></div>
                <div><dt>Reviewability</dt><dd>{controlledLabel(item.reviewabilityStatus)}</dd></div>
                <div><dt>Binding</dt><dd>{controlledLabel(item.bindingStatus)}</dd></div>
                <div><dt>Retention</dt><dd>{controlledLabel(item.retentionStatus)}</dd></div>
                <div><dt>Deletion</dt><dd>{controlledLabel(item.deletionStatus)}</dd></div>
                <div><dt>Hold</dt><dd>{item.holdActive ? "Active hold" : "No active hold"}</dd></div>
                <div><dt>File size</dt><dd>{formatFileSize(item.contentLength)}</dd></div>
                <div><dt>Finalized</dt><dd>{formatEvidenceDate(item.finalizedAt)}</dd></div>
              </dl>

              {!item.previewPermitted && <p id={`preview-reason-${index}`} className="notice">{denialMessage}</p>}
              {!canReviewEvidence && <p id={`preview-access-${index}`} className="notice">Your current access does not include secure evidence preview.</p>}
              <button
                type="button"
                disabled={!previewAvailable}
                aria-describedby={!canReviewEvidence ? `preview-access-${index}` : !item.previewPermitted ? `preview-reason-${index}` : undefined}
                aria-label={`Preview ${evidenceRoleLabel(item.itemRole)}`}
                onClick={(event) => onPreview(item, event.currentTarget)}
              >
                Preview
              </button>
            </li>
          );
        })}
      </ul>
    </>
  );
}

export function previewDenialMessage(reason?: string) {
  const messages: Record<string, string> = {
    STATUTORY_EVIDENCE_NOT_REQUIRED: "Evidence is not required for this review.",
    STATUTORY_EVIDENCE_MISSING: "The evidence could not be found.",
    STATUTORY_EVIDENCE_STALE: "This evidence is no longer current.",
    STATUTORY_EVIDENCE_DELETION_IN_PROGRESS: "This evidence is pending deletion or unavailable.",
    STATUTORY_EVIDENCE_RETENTION_INACCESSIBLE: "This evidence is unavailable under its current retention posture.",
    STATUTORY_EVIDENCE_UPLOAD_NOT_FINALIZED: "Evidence upload is not yet complete.",
    STATUTORY_EVIDENCE_VALIDATION_PENDING: "Evidence is still being validated.",
    STATUTORY_EVIDENCE_VALIDATION_FAILED: "Evidence cannot be reviewed because validation failed.",
    STATUTORY_EVIDENCE_SCAN_PENDING: "Security scanning is still in progress.",
    STATUTORY_EVIDENCE_SCANNER_UNAVAILABLE: "Security scanning is temporarily unavailable.",
    STATUTORY_EVIDENCE_MALWARE_DETECTED: "Evidence cannot be reviewed because unsafe content was detected.",
    STATUTORY_EVIDENCE_SCAN_FAILED: "Evidence cannot be reviewed because security scanning did not complete safely.",
    STATUTORY_EVIDENCE_NOT_REVIEWABLE: "This evidence is not available for review.",
    STATUTORY_EVIDENCE_BINDING_INVALID: "This evidence is not bound to the current review.",
    STATUTORY_EVIDENCE_PREVIEW_UNSUPPORTED_MEDIA: "This file type cannot be previewed.",
    STATUTORY_EVIDENCE_PREVIEW_STALE: "This evidence is no longer current.",
    REPLACED: "This evidence has been replaced.",
    DELETED: "This evidence is no longer available."
  };
  return reason ? messages[reason] ?? "This evidence is not currently eligible for preview." : "This evidence is not currently eligible for preview.";
}

function mapEvidenceReviewError(error: unknown): OperatorConsoleApiError {
  const mapped = mapApiError(error);
  if (mapped.status === "access-denied") {
    return { ...mapped, message: "You no longer have access to this evidence." };
  }
  if (mapped.status === "not-found") {
    return { ...mapped, message: "The evidence could not be found." };
  }
  return { ...mapped, message: "Evidence details are temporarily unavailable. Try again." };
}

function mapEvidencePreviewError(error: unknown): { message: string; retryable: boolean } {
  const mapped = mapApiError(error);
  const code = mapped.errorCode ?? "";
  if (mapped.status === "access-denied") {
    return { message: "You no longer have access to this evidence.", retryable: false };
  }
  if (mapped.status === "not-found") {
    return { message: "The evidence could not be found.", retryable: false };
  }
  if (code === "OPERATOR_CONSOLE_EVIDENCE_PREVIEW_STORAGE_UNAVAILABLE") {
    return { message: "The preview service is temporarily unavailable.", retryable: true };
  }
  if (code === "OPERATOR_CONSOLE_EVIDENCE_PREVIEW_STALE" || code === "STATUTORY_EVIDENCE_PREVIEW_STALE") {
    return { message: "This evidence is no longer current.", retryable: false };
  }
  if (code === "STATUTORY_EVIDENCE_PREVIEW_UNSUPPORTED_MEDIA") {
    return { message: "This file type cannot be previewed.", retryable: false };
  }
  return { message: "Evidence preview is temporarily unavailable. Try again.", retryable: true };
}

function controlledLabel(value: string) {
  return value
    .trim()
    .toLowerCase()
    .split("_")
    .filter(Boolean)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(" ") || "Unknown";
}

function evidenceRoleLabel(role: string) {
  const labels: Record<string, string> = {
    PRIMARY_IDENTITY_DOCUMENT: "Primary identity document",
    SUPPORTING_DOCUMENT: "Supporting document",
    RESIDENCY_DOCUMENT: "Residency document"
  };
  return labels[role] ?? controlledLabel(role);
}

function safeMediaDescription(item: StatutoryEvidenceReviewItem) {
  const type = item.authoritativeContentType ?? item.declaredContentType;
  if (type === "image/jpeg") {
    return "JPEG image";
  }
  if (type === "image/png") {
    return "PNG image";
  }
  if (type === "application/pdf") {
    return "PDF document";
  }
  return "File type unavailable";
}

function formatFileSize(value?: number) {
  if (value === undefined || value < 0 || !Number.isFinite(value)) {
    return "Not available";
  }
  if (value < 1024) {
    return `${value} bytes`;
  }
  return `${(value / 1024).toFixed(value < 10_240 ? 1 : 0)} KB`;
}

function formatEvidenceDate(value?: string) {
  if (!value) {
    return "Not available";
  }
  const date = new Date(value);
  return Number.isNaN(date.valueOf()) ? "Not available" : date.toLocaleString();
}

function isAbortError(error: unknown) {
  return error instanceof DOMException && error.name === "AbortError";
}
