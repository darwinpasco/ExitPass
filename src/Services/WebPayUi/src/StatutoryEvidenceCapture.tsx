import { useEffect, useRef, useState } from "react";
import {
  bootstrapStatutoryEvidence,
  computeSha256,
  finalizeStatutoryEvidenceUpload,
  formatBytes,
  requestStatutoryEvidenceUploadSession,
  retrieveStatutoryEvidenceStatus,
  uploadStatutoryEvidence,
  validateStatutoryEvidenceFile
} from "./statutoryEvidence";
import type { WebPayStatutoryEvidenceChannelResponse } from "./types";

type CaptureState = "loading" | "ready" | "authorizing" | "uploading" | "finalizing" | "idle";

const pollableLifecycleStates = new Set(["VALIDATION_PENDING", "SCAN_PENDING"]);
const captureLifecycleStates = new Set([
  "REQUIRED_NOT_STARTED",
  "ITEM_CREATED",
  "UPLOAD_SESSION_AVAILABLE",
  "VALIDATION_FAILED",
  "SCAN_RETRYABLE",
  "SCAN_FAILED",
  "NOT_REVIEWABLE"
]);

export function StatutoryEvidenceCapture({ statutoryDiscountDecisionCommandId }: { statutoryDiscountDecisionCommandId: string }) {
  const [channel, setChannel] = useState<WebPayStatutoryEvidenceChannelResponse | null>(null);
  const [captureState, setCaptureState] = useState<CaptureState>("loading");
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [fileError, setFileError] = useState("");
  const [operationError, setOperationError] = useState("");
  const [uploadPercent, setUploadPercent] = useState<number | null>(null);
  const [pollAttempt, setPollAttempt] = useState(0);
  const abortController = useRef<AbortController | null>(null);
  const errorRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    abortController.current?.abort();
    abortController.current = controller;
    setChannel(null);
    setSelectedFile(null);
    setCaptureState("loading");
    setFileError("");
    setOperationError("");
    setUploadPercent(null);
    setPollAttempt(0);

    void bootstrapStatutoryEvidence(statutoryDiscountDecisionCommandId, fetch, controller.signal)
      .then((response) => {
        setChannel(response);
        setCaptureState("ready");
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return;
        }
        setCaptureState("idle");
        setOperationError(error instanceof Error ? error.message : "Evidence status is temporarily unavailable.");
      });

    return () => controller.abort();
  }, [statutoryDiscountDecisionCommandId]);

  useEffect(() => {
    if (!channel || !pollableLifecycleStates.has(channel.lifecycleClassification) || pollAttempt >= 6) {
      return;
    }

    const timer = window.setTimeout(() => {
      void refreshStatus(true);
    }, 3000);
    return () => window.clearTimeout(timer);
  }, [channel, pollAttempt]);

  useEffect(() => {
    if (operationError) {
      errorRef.current?.focus();
    }
  }, [operationError]);

  async function refreshStatus(fromPoll = false) {
    if (captureState === "uploading" || captureState === "authorizing" || captureState === "finalizing") {
      return;
    }

    const controller = new AbortController();
    abortController.current = controller;
    if (!fromPoll) {
      setCaptureState("loading");
      setOperationError("");
    }

    try {
      const response = await retrieveStatutoryEvidenceStatus(
        { statutoryDiscountDecisionCommandId },
        fetch,
        controller.signal
      );
      setChannel(response);
      setCaptureState("ready");
      setPollAttempt((current) => fromPoll ? current + 1 : 0);
    } catch (error) {
      if (!controller.signal.aborted) {
        setCaptureState("idle");
        setOperationError(error instanceof Error ? error.message : "Evidence status is temporarily unavailable.");
      }
    }
  }

  function handleFileSelection(files: FileList | null) {
    if (!channel) {
      return;
    }
    if (!files || files.length !== 1) {
      setSelectedFile(null);
      setFileError("Choose one photo to upload.");
      return;
    }

    const file = files.item(0);
    const validationError = validateStatutoryEvidenceFile(file, channel);
    setSelectedFile(validationError ? null : file);
    setFileError(validationError ?? "");
    setOperationError("");
  }

  async function handleUpload() {
    if (!channel || !selectedFile || captureState === "uploading" || captureState === "authorizing" || captureState === "finalizing") {
      return;
    }

    const validationError = validateStatutoryEvidenceFile(selectedFile, channel);
    if (validationError) {
      setFileError(validationError);
      return;
    }

    const controller = new AbortController();
    abortController.current = controller;
    setOperationError("");
    setFileError("");
    setUploadPercent(0);

    try {
      setCaptureState("authorizing");
      const checksum = await computeSha256(selectedFile);
      const uploadSession = await requestStatutoryEvidenceUploadSession(channel, selectedFile, checksum);
      if (!uploadSession.opaqueUploadSessionReference) {
        throw new Error("Evidence upload authorization was incomplete. Refresh and try again.");
      }

      setCaptureState("uploading");
      await uploadStatutoryEvidence(
        uploadSession.opaqueUploadSessionReference,
        selectedFile,
        (progress) => setUploadPercent(progress.percent),
        controller.signal
      );

      setCaptureState("finalizing");
      const finalized = await finalizeStatutoryEvidenceUpload(uploadSession.opaqueUploadSessionReference);
      setChannel(finalized);
      setSelectedFile(null);
      setUploadPercent(100);
      setPollAttempt(0);
      setCaptureState("ready");
    } catch (error) {
      setCaptureState("idle");
      if (error instanceof DOMException && error.name === "AbortError") {
        setOperationError("The photo upload was cancelled. Refresh the evidence status before trying again.");
        const reconciliationController = new AbortController();
        abortController.current = reconciliationController;
        try {
          const reconciled = await retrieveStatutoryEvidenceStatus(
            { statutoryDiscountDecisionCommandId },
            fetch,
            reconciliationController.signal
          );
          setChannel(reconciled);
          setPollAttempt(0);
        } catch {
          // Keep the bounded cancellation guidance when reconciliation is unavailable.
        }
      } else {
        setOperationError(error instanceof Error ? error.message : "The photo could not be uploaded. Please try again.");
      }
    }
  }

  function cancelUpload() {
    abortController.current?.abort();
  }

  const lifecycleCopy = getLifecycleCopy(channel);
  const replacementAllowed = channel?.replacementPosture === "REPLACEMENT_ALLOWED";
  const isReplacement = Boolean(
    replacementAllowed &&
    channel &&
    channel.lifecycleClassification !== "REQUIRED_NOT_STARTED" &&
    channel.lifecycleClassification !== "ITEM_CREATED" &&
    channel.lifecycleClassification !== "UPLOAD_SESSION_AVAILABLE"
  );
  const captureAllowed = Boolean(
    channel?.evidenceRequired &&
    (captureLifecycleStates.has(channel.lifecycleClassification) || replacementAllowed)
  );
  const isBusy = captureState === "authorizing" || captureState === "uploading" || captureState === "finalizing";

  return (
    <section className="statutory-evidence" aria-labelledby="statutory-evidence-heading">
      <div className="statutory-evidence-header">
        <div>
          <p className="eyebrow">Required evidence</p>
          <h3 id="statutory-evidence-heading">Entitlement photo</h3>
        </div>
        <span className={`evidence-state is-${lifecycleCopy.tone}`}>{lifecycleCopy.label}</span>
      </div>

      {captureState === "loading" && !channel && <p role="status">Checking the evidence requirement...</p>}

      {channel && (
        <>
          <p className="statutory-copy" aria-live="polite">{lifecycleCopy.message}</p>
          {channel.evidenceRequired && (
            <dl className="evidence-rules">
              <div>
                <dt>Accepted photos</dt>
                <dd>{formatAcceptedTypes(channel.allowedContentTypes)}</dd>
              </div>
              <div>
                <dt>Maximum file size</dt>
                <dd>{formatBytes(channel.maximumContentLengthBytes)}</dd>
              </div>
              {channel.maximumImageWidth && channel.maximumImageHeight && (
                <div>
                  <dt>Maximum dimensions</dt>
                  <dd>{channel.maximumImageWidth} x {channel.maximumImageHeight} pixels</dd>
                </div>
              )}
              {channel.requiredItemRole && (
                <div>
                  <dt>Photo required</dt>
                  <dd>{displaySafeRole(channel.requiredItemRole)}</dd>
                </div>
              )}
            </dl>
          )}

          {captureAllowed && (
            <div className="evidence-capture-controls">
              {isReplacement && (
                <p className="statutory-copy">A replacement is allowed. The server will supersede the earlier photo after the new upload is verified.</p>
              )}
              <label className="field evidence-file-field">
                <span>Choose or take a clear photo</span>
                <input
                  type="file"
                  accept="image/jpeg,image/png"
                  capture="environment"
                  onChange={(event) => handleFileSelection(event.currentTarget.files)}
                  disabled={isBusy}
                />
                <small>JPEG or PNG only. The photo stays in this browser only for the active upload.</small>
              </label>
              {selectedFile && <p className="selected-file">Selected: {selectedFile.name} ({formatBytes(selectedFile.size)})</p>}
              {fileError && <div className="form-error" role="alert">{fileError}</div>}
              {isBusy && (
                <div className="upload-progress" role="status" aria-live="polite">
                  <progress value={uploadPercent ?? undefined} max={100} aria-label="Photo upload progress" />
                  <span>{getUploadStatus(captureState, uploadPercent)}</span>
                </div>
              )}
              <div className="statutory-actions">
                <button type="button" className="secondary-button" onClick={() => void handleUpload()} disabled={!selectedFile || isBusy}>
                  {isReplacement ? "Upload replacement photo" : "Upload photo"}
                </button>
                {isBusy && (
                  <button type="button" className="ghost-button" onClick={cancelUpload}>
                    Cancel upload
                  </button>
                )}
              </div>
            </div>
          )}

          {channel.evidenceRequired && !captureAllowed && channel.replacementPosture === "REPLACEMENT_NOT_ALLOWED" && (
            <p className="statutory-copy">This evidence cannot be replaced in its current review state.</p>
          )}
        </>
      )}

      {operationError && (
        <div className="form-error" role="alert" ref={errorRef} tabIndex={-1}>
          {operationError}
        </div>
      )}

      <div className="statutory-actions">
        <button type="button" className="ghost-button" onClick={() => void refreshStatus()} disabled={isBusy || captureState === "loading"}>
          {captureState === "loading" ? "Checking evidence status..." : "Refresh evidence status"}
        </button>
      </div>
      {channel?.correlationId && <p className="support-reference">Support reference: {channel.correlationId}</p>}
    </section>
  );
}

function getLifecycleCopy(channel: WebPayStatutoryEvidenceChannelResponse | null): { label: string; message: string; tone: string } {
  if (!channel) {
    return { label: "Checking", message: "Checking the evidence requirement...", tone: "pending" };
  }
  if (!channel.evidenceRequired || channel.lifecycleClassification === "NOT_REQUIRED") {
    return { label: "Not required", message: "No evidence photo is required for this request.", tone: "neutral" };
  }

  switch (channel.lifecycleClassification) {
    case "REQUIRED_NOT_STARTED":
    case "ITEM_CREATED":
    case "UPLOAD_SESSION_AVAILABLE":
      return { label: "Photo required", message: "Choose a clear JPEG or PNG photo to continue the review request.", tone: "pending" };
    case "UPLOAD_IN_PROGRESS":
      return { label: "Upload incomplete", message: "The previous upload did not finish. Reselect the photo when the server permits another upload.", tone: "warning" };
    case "VALIDATION_PENDING":
      return { label: "Verification pending", message: "The upload completed and photo verification is pending.", tone: "pending" };
    case "VALIDATION_FAILED":
      return { label: "Photo not accepted", message: "The photo could not be verified. Choose another clear JPEG or PNG photo when replacement is allowed.", tone: "warning" };
    case "SCAN_PENDING":
      return { label: "Verification pending", message: "The photo is still being checked. Return later to check the review status.", tone: "pending" };
    case "SCAN_RETRYABLE":
      return { label: "Processing delayed", message: "Photo processing is temporarily delayed. Refresh the evidence status shortly.", tone: "warning" };
    case "SCAN_FAILED":
    case "NOT_REVIEWABLE":
      return { label: "Not ready for review", message: "The photo is not ready for review. Follow the available replacement or retry action.", tone: "warning" };
    case "MALWARE_DETECTED":
      return { label: "Unsafe file detected", message: "The selected file cannot be used. Choose another photo if replacement is allowed.", tone: "error" };
    case "REVIEWABLE":
      return { label: "Ready for review", message: "The photo is ready for review. This does not mean the statutory privilege is approved.", tone: "success" };
    case "REVIEW_PENDING":
      return { label: "Awaiting review", message: "The photo was received and the statutory privilege request is awaiting review.", tone: "pending" };
    case "APPROVED":
      return { label: "Approved", message: "The request was approved. The privilege is applied only through the governed payment-time flow.", tone: "success" };
    case "REJECTED":
      return { label: "Not approved", message: "The request was not approved. Regular parking payment remains available.", tone: "warning" };
    case "APPLIED":
      return { label: "Applied", message: "The approved privilege has been applied to the authoritative payable basis.", tone: "success" };
    default:
      return { label: "Status unavailable", message: "The evidence status could not be confirmed safely. Refresh the status or ask for assistance.", tone: "error" };
  }
}

function formatAcceptedTypes(types: string[]): string {
  const labels = types.map((value) => value === "image/jpeg" ? "JPEG" : value === "image/png" ? "PNG" : null).filter(Boolean);
  return labels.length > 0 ? labels.join(" or ") : "JPEG or PNG";
}

function displaySafeRole(value: string): string {
  return value.replaceAll("_", " ").toLowerCase().replace(/^./, (character) => character.toUpperCase());
}

function getUploadStatus(state: CaptureState, percent: number | null): string {
  if (state === "authorizing") {
    return "Preparing a protected upload...";
  }
  if (state === "finalizing") {
    return "Upload completed. Finalizing the photo...";
  }
  return percent === null ? "Uploading photo..." : `Uploading photo: ${percent}%`;
}
