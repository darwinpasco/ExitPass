import { useEffect, useRef, useState } from "react";
import {
  bootstrapStatutoryEvidence,
  computeSha256,
  finalizeStatutoryEvidenceUpload,
  formatBytes,
  requestStatutoryEvidenceUploadSession,
  retrieveStatutoryEvidencePreview,
  retrieveStatutoryEvidenceStatus,
  StatutoryEvidenceError,
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
const previewLifecycleStates = new Set(["REVIEWABLE", "REVIEW_PENDING", "APPROVED", "APPLIED"]);

export function StatutoryEvidenceCapture({ statutoryDiscountDecisionCommandId }: { statutoryDiscountDecisionCommandId: string }) {
  const [channel, setChannel] = useState<WebPayStatutoryEvidenceChannelResponse | null>(null);
  const [captureState, setCaptureState] = useState<CaptureState>("loading");
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [fileError, setFileError] = useState("");
  const [operationError, setOperationError] = useState("");
  const [uploadPercent, setUploadPercent] = useState<number | null>(null);
  const [pollAttempt, setPollAttempt] = useState(0);
  const [previewUrl, setPreviewUrl] = useState("");
  const [previewError, setPreviewError] = useState("");
  const statusAbortController = useRef<AbortController | null>(null);
  const uploadAbortController = useRef<AbortController | null>(null);
  const requestGeneration = useRef(0);
  const evidenceReadSequence = useRef(0);
  const committedEvidenceSequence = useRef(0);
  const captureStateRef = useRef<CaptureState>("loading");
  const errorRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    const generation = requestGeneration.current + 1;
    requestGeneration.current = generation;
    statusAbortController.current?.abort();
    uploadAbortController.current?.abort();
    committedEvidenceSequence.current = 0;
    setChannel(null);
    setSelectedFile(null);
    transitionCaptureState("loading");
    setFileError("");
    setOperationError("");
    setUploadPercent(null);
    setPollAttempt(0);
    setPreviewUrl("");
    setPreviewError("");

    void bootstrapEvidence(generation);

    return () => {
      statusAbortController.current?.abort();
      uploadAbortController.current?.abort();
    };
  }, [statutoryDiscountDecisionCommandId]);

  useEffect(() => {
    if (!channel?.evidenceItemReference || !previewLifecycleStates.has(channel.lifecycleClassification)) {
      setPreviewUrl("");
      setPreviewError("");
      return;
    }

    const controller = new AbortController();
    let objectUrl = "";
    setPreviewError("");
    void retrieveStatutoryEvidencePreview(
      statutoryDiscountDecisionCommandId,
      channel.evidenceItemReference,
      fetch,
      controller.signal
    ).then((blob) => {
      if (!controller.signal.aborted) {
        objectUrl = URL.createObjectURL(blob);
        setPreviewUrl(objectUrl);
      }
    }).catch((error: unknown) => {
      if (!controller.signal.aborted) {
        setPreviewError(error instanceof Error ? error.message : "The submitted photo could not be displayed safely.");
      }
    });

    return () => {
      controller.abort();
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [channel?.evidenceItemReference, channel?.lifecycleClassification, statutoryDiscountDecisionCommandId]);

  useEffect(() => {
    if (!channel || !pollableLifecycleStates.has(channel.lifecycleClassification)) {
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
    if (isCaptureBusy(captureStateRef.current)) {
      return;
    }

    if (!fromPoll) {
      transitionCaptureState("loading");
      setOperationError("");
    }

    await reconcileEvidenceStatus(requestGeneration.current, fromPoll);
  }

  async function bootstrapEvidence(generation: number) {
    const sequence = evidenceReadSequence.current + 1;
    evidenceReadSequence.current = sequence;
    const controller = replaceStatusController();

    try {
      const response = await bootstrapStatutoryEvidence(statutoryDiscountDecisionCommandId, fetch, controller.signal);
      commitEvidenceChannel(response, generation, sequence);
    } catch (error: unknown) {
      if (controller.signal.aborted || generation !== requestGeneration.current) {
        return;
      }
      if (isRecoverableEvidenceConflict(error)) {
        await reconcileEvidenceStatus(generation, false);
        return;
      }
      transitionCaptureState("idle");
      setOperationError(error instanceof Error ? error.message : "Evidence status is temporarily unavailable.");
    }
  }

  async function reconcileEvidenceStatus(generation: number, fromPoll: boolean, conflictRetry = 0) {
    const sequence = evidenceReadSequence.current + 1;
    evidenceReadSequence.current = sequence;
    const controller = replaceStatusController();

    try {
      const response = await retrieveStatutoryEvidenceStatus(
        { statutoryDiscountDecisionCommandId },
        fetch,
        controller.signal
      );
      if (commitEvidenceChannel(response, generation, sequence)) {
        setPollAttempt((current) => fromPoll ? current + 1 : 0);
        setOperationError("");
      }
    } catch (error: unknown) {
      if (controller.signal.aborted || generation !== requestGeneration.current) {
        return;
      }
      if (isRecoverableEvidenceConflict(error) && conflictRetry < 2) {
        setOperationError("Evidence status is being reconciled automatically. Please wait a moment.");
        await waitForAutomaticReadback(500);
        if (generation === requestGeneration.current) {
          await reconcileEvidenceStatus(generation, fromPoll, conflictRetry + 1);
        }
        return;
      }
      transitionCaptureState("idle");
      setOperationError(
        isRecoverableEvidenceConflict(error)
          ? "Evidence status is being reconciled automatically. Please wait a moment."
          : error instanceof Error ? error.message : "Evidence status is temporarily unavailable."
      );
    }
  }

  function replaceStatusController() {
    statusAbortController.current?.abort();
    const controller = new AbortController();
    statusAbortController.current = controller;
    return controller;
  }

  function commitEvidenceChannel(
    response: WebPayStatutoryEvidenceChannelResponse,
    generation: number,
    sequence: number
  ) {
    if (generation !== requestGeneration.current || sequence < committedEvidenceSequence.current) {
      return false;
    }

    committedEvidenceSequence.current = sequence;
    setChannel(response);
    transitionCaptureState("ready");
    return true;
  }

  function transitionCaptureState(next: CaptureState) {
    captureStateRef.current = next;
    setCaptureState(next);
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
    if (!channel || !selectedFile || isCaptureBusy(captureStateRef.current)) {
      return;
    }

    const validationError = validateStatutoryEvidenceFile(selectedFile, channel);
    if (validationError) {
      setFileError(validationError);
      return;
    }

    const generation = requestGeneration.current;
    const operationSequence = evidenceReadSequence.current + 1;
    evidenceReadSequence.current = operationSequence;
    statusAbortController.current?.abort();
    const controller = new AbortController();
    uploadAbortController.current = controller;
    setOperationError("");
    setFileError("");
    setUploadPercent(0);

    try {
      transitionCaptureState("authorizing");
      const checksum = await computeSha256(selectedFile);
      if (generation !== requestGeneration.current) return;
      const uploadSession = await requestStatutoryEvidenceUploadSession(channel, selectedFile, checksum);
      if (generation !== requestGeneration.current) return;
      if (!uploadSession.opaqueUploadSessionReference) {
        throw new Error("Evidence upload authorization was incomplete. Refresh and try again.");
      }

      transitionCaptureState("uploading");
      await uploadStatutoryEvidence(
        uploadSession.opaqueUploadSessionReference,
        selectedFile,
        (progress) => setUploadPercent(progress.percent),
        controller.signal
      );
      if (generation !== requestGeneration.current) return;

      transitionCaptureState("finalizing");
      const finalized = await finalizeStatutoryEvidenceUpload(uploadSession.opaqueUploadSessionReference);
      if (!commitEvidenceChannel(finalized, generation, operationSequence)) return;
      setSelectedFile(null);
      setUploadPercent(100);
      setPollAttempt(0);
    } catch (error: unknown) {
      if (generation !== requestGeneration.current) return;
      transitionCaptureState("idle");
      if (error instanceof DOMException && error.name === "AbortError") {
        setOperationError("The photo upload was cancelled. Current evidence status is being restored automatically.");
        await reconcileEvidenceStatus(generation, false);
      } else if (isRecoverableEvidenceConflict(error)) {
        setOperationError("Evidence status changed while the photo was being prepared. Current status is being restored automatically.");
        await reconcileEvidenceStatus(generation, false);
      } else {
        setOperationError(error instanceof Error ? error.message : "The photo could not be uploaded. Please try again.");
      }
    }
  }

  function cancelUpload() {
    uploadAbortController.current?.abort();
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
          {(previewUrl || previewError) && (
            <div className="evidence-preview" aria-label="Submitted evidence">
              <h4>Submitted evidence</h4>
              {previewUrl && <img src={previewUrl} alt="Submitted statutory entitlement evidence" />}
              {previewError && <p className="form-error" role="alert">{previewError}</p>}
            </div>
          )}
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
    </section>
  );
}

function isCaptureBusy(state: CaptureState) {
  return state === "authorizing" || state === "uploading" || state === "finalizing";
}

function isRecoverableEvidenceConflict(error: unknown) {
  return error instanceof StatutoryEvidenceError &&
    error.errorCode?.toUpperCase() === "WEBPAY_STATUTORY_EVIDENCE_CONFLICT";
}

function waitForAutomaticReadback(delayMilliseconds: number) {
  return new Promise<void>((resolve) => window.setTimeout(resolve, delayMilliseconds));
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
      return { label: "Approved", message: "The request was approved. Central PMS is applying the governed parking privilege automatically.", tone: "success" };
    case "REJECTED":
      return { label: "Not approved", message: "The request was not approved. Regular parking payment remains available.", tone: "warning" };
    case "APPLIED":
      return { label: "Applied", message: "The approved privilege is included in the amount due.", tone: "success" };
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
