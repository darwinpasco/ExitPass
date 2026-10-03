import { useCallback, useEffect, useRef, useState } from "react";

interface PhoneCameraCaptureProps {
  value: File | null;
  disabled?: boolean;
  onChange: (photo: File | null) => void;
}

type CameraState = "idle" | "starting" | "live" | "capturing" | "error";

export function PhoneCameraCapture({ value, disabled = false, onChange }: PhoneCameraCaptureProps) {
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
    if (!isSupportedPhone()) {
      setState("error");
      setMessage("Camera unavailable.");
      return;
    }
    if (!navigator.mediaDevices?.getUserMedia) {
      setState("error");
      setMessage("Phone camera access is required to capture the customer's ID.");
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
        setMessage("A rear phone camera is required to capture the customer's ID.");
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

  function capturePhoto() {
    const video = videoRef.current;
    if (!video || video.videoWidth <= 0 || video.videoHeight <= 0) {
      setState("error");
      setMessage("Phone camera is unavailable. Try again.");
      stopCamera();
      return;
    }

    setState("capturing");
    const canvas = document.createElement("canvas");
    canvas.width = video.videoWidth;
    canvas.height = video.videoHeight;
    const context = canvas.getContext("2d");
    if (!context) {
      stopCamera();
      setState("error");
      setMessage("Phone camera is unavailable. Try again.");
      return;
    }
    context.drawImage(video, 0, 0, canvas.width, canvas.height);
    canvas.toBlob((blob) => {
      stopCamera();
      if (!blob || blob.size <= 0 || blob.size > 5 * 1024 * 1024) {
        setState("error");
        setMessage("The captured photo could not be prepared. Retake the photo.");
        return;
      }
      onChange(new File([blob], `statutory-id-${Date.now()}.jpg`, { type: "image/jpeg", lastModified: Date.now() }));
      setState("idle");
      setMessage(undefined);
    }, "image/jpeg", 0.9);
  }

  function retake() {
    onChange(null);
    void startCamera();
  }

  const phoneSupported = isSupportedPhone();
  return (
    <div className="statutoryIdPhotoField">
      <strong>ID photo</strong>
      {!value && state !== "live" && state !== "starting" && phoneSupported && (
        <button type="button" onClick={() => void startCamera()} disabled={disabled}>Take ID Photo</button>
      )}
      {state === "starting" && <p role="status">Starting the phone camera...</p>}
      {(state === "live" || state === "capturing") && (
        <div className="statutoryCamera" role="group" aria-label="Phone camera capture">
          <video ref={videoRef} autoPlay playsInline muted aria-label="Live rear camera preview" />
          <div className="actionBar">
            <button type="button" onClick={capturePhoto} disabled={state === "capturing"}>Capture Photo</button>
            <button type="button" onClick={cancelCamera} disabled={state === "capturing"}>Cancel</button>
          </div>
        </div>
      )}
      {message && <p className="errorMessage" role="alert">{message}</p>}
      {value && <div className="statutoryIdPhotoPreview" role="status">
        <strong>ID image captured</strong>
        {previewUrl && <img src={previewUrl} alt="Captured statutory ID preview" />}
        <button type="button" onClick={retake} disabled={disabled}>Retake Photo</button>
      </div>}
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

function cameraFailureMessage(error: unknown) {
  const name = error instanceof DOMException ? error.name : "";
  if (name === "NotAllowedError" || name === "SecurityError") return "Camera permission is required.";
  if (name === "NotFoundError" || name === "OverconstrainedError") return "A rear phone camera is required to capture the customer's ID.";
  if (name === "NotReadableError" || name === "AbortError") return "Phone camera is unavailable. Close other camera apps and try again.";
  return "Phone camera is unavailable.";
}
