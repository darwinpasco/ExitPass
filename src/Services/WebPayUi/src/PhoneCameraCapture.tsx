import { useCallback, useEffect, useRef, useState } from "react";

interface PhoneCameraCaptureProps {
  value: File | null;
  disabled?: boolean;
  maximumContentLengthBytes?: number;
  maximumImageWidth?: number | null;
  maximumImageHeight?: number | null;
  onChange: (photo: File | null) => void;
}

type CameraState = "idle" | "starting" | "live" | "capturing" | "error";

const defaultMaximumContentLengthBytes = 5 * 1024 * 1024;

export function PhoneCameraCapture({
  value,
  disabled = false,
  maximumContentLengthBytes = defaultMaximumContentLengthBytes,
  maximumImageWidth = 6000,
  maximumImageHeight = 6000,
  onChange
}: PhoneCameraCaptureProps) {
  const [state, setState] = useState<CameraState>("idle");
  const [message, setMessage] = useState<string>();
  const [previewUrl, setPreviewUrl] = useState<string>();
  const videoRef = useRef<HTMLVideoElement>(null);
  const streamRef = useRef<MediaStream | null>(null);

  const stopCamera = useCallback(() => {
    streamRef.current?.getTracks().forEach((track) => track.stop());
    streamRef.current = null;
    if (videoRef.current) videoRef.current.srcObject = null;
  }, []);

  useEffect(() => {
    if (!value || typeof URL.createObjectURL !== "function") {
      setPreviewUrl(undefined);
      return;
    }

    const next = URL.createObjectURL(value);
    setPreviewUrl(next);
    return () => URL.revokeObjectURL(next);
  }, [value]);

  useEffect(() => stopCamera, [stopCamera]);

  useEffect(() => {
    if (state !== "live" || !streamRef.current || !videoRef.current) return;
    const video = videoRef.current;
    video.srcObject = streamRef.current;
    void video.play().catch(() => {
      stopCamera();
      setState("error");
      setMessage("Phone camera is unavailable. Try again.");
    });
  }, [state, stopCamera]);

  async function startCamera() {
    stopCamera();
    setMessage(undefined);
    if (!isSupportedPhone() || !navigator.mediaDevices?.getUserMedia) {
      setState("error");
      setMessage("Phone camera access is required to capture the entitlement photo.");
      return;
    }

    setState("starting");
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: false,
        video: { facingMode: { ideal: "environment" } }
      });
      const facingMode = stream.getVideoTracks()[0]?.getSettings?.().facingMode;
      if (facingMode && facingMode !== "environment") {
        stream.getTracks().forEach((track) => track.stop());
        setState("error");
        setMessage("A rear phone camera is required to capture the entitlement photo.");
        return;
      }

      streamRef.current = stream;
      setState("live");
    } catch (error) {
      stopCamera();
      setState("error");
      setMessage(cameraFailureMessage(error));
    }
  }

  function cancelCamera() {
    stopCamera();
    setState("idle");
    setMessage(undefined);
  }

  async function capturePhoto() {
    const video = videoRef.current;
    if (!video || video.videoWidth <= 0 || video.videoHeight <= 0) {
      stopCamera();
      setState("error");
      setMessage("Phone camera is unavailable. Try again.");
      return;
    }

    setState("capturing");
    const dimensions = fitCaptureDimensions(
      video.videoWidth,
      video.videoHeight,
      maximumImageWidth,
      maximumImageHeight
    );
    const canvas = document.createElement("canvas");
    canvas.width = dimensions.width;
    canvas.height = dimensions.height;
    const context = canvas.getContext("2d");
    if (!context) {
      stopCamera();
      setState("error");
      setMessage("Phone camera is unavailable. Try again.");
      return;
    }

    context.drawImage(video, 0, 0, canvas.width, canvas.height);
    const maximumBytes = maximumContentLengthBytes > 0
      ? maximumContentLengthBytes
      : defaultMaximumContentLengthBytes;
    const blob = await encodeJpegWithinLimit(canvas, maximumBytes);
    stopCamera();
    if (!blob) {
      setState("error");
      setMessage("The captured photo could not be prepared. Retake the photo.");
      return;
    }

    onChange(new File(
      [blob],
      `statutory-evidence-${Date.now()}.jpg`,
      { type: "image/jpeg", lastModified: Date.now() }
    ));
    setState("idle");
    setMessage(undefined);
  }

  function retake() {
    onChange(null);
    void startCamera();
  }

  const phoneSupported = isSupportedPhone();
  return (
    <div className="evidence-camera-field">
      <strong>Evidence photo (required)</strong>
      <small>A photo of the beneficiary's ID is required for review and approval. JPEG or PNG only.</small>
      {!value && state !== "live" && state !== "starting" && phoneSupported && (
        <button type="button" className="secondary-button" onClick={() => void startCamera()} disabled={disabled}>
          Take Photo
        </button>
      )}
      {!phoneSupported && <p className="form-error" role="alert">Use a supported phone camera to continue.</p>}
      {state === "starting" && <p role="status">Starting the phone camera...</p>}
      {(state === "live" || state === "capturing") && (
        <div className="evidence-camera" role="group" aria-label="Phone camera capture">
          <video ref={videoRef} autoPlay playsInline muted aria-label="Live rear camera preview" />
          <div className="statutory-actions">
            <button type="button" className="primary-button" onClick={() => void capturePhoto()} disabled={state === "capturing"}>
              Capture Photo
            </button>
            <button type="button" className="ghost-button" onClick={cancelCamera} disabled={state === "capturing"}>
              Cancel
            </button>
          </div>
        </div>
      )}
      {message && <p className="form-error" role="alert">{message}</p>}
      {value && (
        <div className="evidence-camera-preview" role="status">
          <strong>Photo captured</strong>
          {previewUrl && <img src={previewUrl} alt="Captured entitlement evidence preview" />}
          <button type="button" className="secondary-button" onClick={retake} disabled={disabled}>
            Retake Photo
          </button>
        </div>
      )}
    </div>
  );
}

export function isSupportedPhone() {
  if (typeof navigator === "undefined") return false;
  const navigatorWithClientHints = navigator as Navigator & { userAgentData?: { mobile?: boolean } };
  if (typeof navigatorWithClientHints.userAgentData?.mobile === "boolean") {
    return navigatorWithClientHints.userAgentData.mobile;
  }
  return /iPhone|iPod|Android.+Mobile|Mobile.+Android/i.test(navigator.userAgent);
}

function fitCaptureDimensions(
  sourceWidth: number,
  sourceHeight: number,
  maximumWidth?: number | null,
  maximumHeight?: number | null
) {
  const widthScale = maximumWidth && maximumWidth > 0 ? maximumWidth / sourceWidth : 1;
  const heightScale = maximumHeight && maximumHeight > 0 ? maximumHeight / sourceHeight : 1;
  const scale = Math.min(1, widthScale, heightScale);
  return {
    width: Math.max(1, Math.floor(sourceWidth * scale)),
    height: Math.max(1, Math.floor(sourceHeight * scale))
  };
}

async function encodeJpegWithinLimit(canvas: HTMLCanvasElement, maximumBytes: number) {
  for (const quality of [0.9, 0.8, 0.7, 0.6]) {
    const blob = await canvasToBlob(canvas, quality);
    if (blob && blob.size > 0 && blob.size <= maximumBytes) return blob;
  }
  return null;
}

function canvasToBlob(canvas: HTMLCanvasElement, quality: number) {
  return new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, "image/jpeg", quality));
}

function cameraFailureMessage(error: unknown) {
  const name = error instanceof DOMException ? error.name : "";
  if (name === "NotAllowedError" || name === "SecurityError") return "Camera permission is required.";
  if (name === "NotFoundError" || name === "OverconstrainedError") return "A rear phone camera is required to capture the entitlement photo.";
  if (name === "NotReadableError" || name === "AbortError") return "Phone camera is unavailable. Close other camera apps and try again.";
  return "Phone camera is unavailable.";
}
