import { useEffect, useRef, useState } from "react";
import { BrowserQRCodeReader, type IScannerControls } from "@zxing/browser";

type CameraState = "starting" | "scanning" | "denied" | "unavailable" | "failed";

interface SessionQrScannerProps {
  onDecoded: (value: string) => void;
  onCancel: () => void;
}

function permissionDenied(error: unknown) {
  return error instanceof DOMException &&
    (error.name === "NotAllowedError" || error.name === "SecurityError" || error.name === "PermissionDeniedError");
}

export function SessionQrScanner({ onDecoded, onCancel }: SessionQrScannerProps) {
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const controlsRef = useRef<IScannerControls | null>(null);
  const decodedRef = useRef(false);
  const [state, setState] = useState<CameraState>("starting");

  useEffect(() => {
    let mounted = true;

    async function start() {
      if (!navigator.mediaDevices?.getUserMedia || !videoRef.current) {
        setState("unavailable");
        return;
      }

      try {
        const reader = new BrowserQRCodeReader();
        controlsRef.current = await reader.decodeFromVideoDevice(undefined, videoRef.current, (result, error) => {
          if (!mounted || decodedRef.current) return;
          if (result) {
            decodedRef.current = true;
            controlsRef.current?.stop();
            onDecoded(result.getText());
          } else if (error && error.name !== "NotFoundException") {
            setState("failed");
          }
        });
        if (mounted) setState("scanning");
      } catch (error) {
        if (mounted) setState(permissionDenied(error) ? "denied" : "unavailable");
      }
    }

    void start();
    return () => {
      mounted = false;
      controlsRef.current?.stop();
    };
  }, [onDecoded]);

  const error = state === "denied" || state === "unavailable" || state === "failed";
  return (
    <section className="sessionQrScanner" aria-label="Ticket QR scanner">
      <video ref={videoRef} muted playsInline aria-label="Ticket QR camera preview" />
      <p role="status">
        {state === "starting" && "Starting camera..."}
        {state === "scanning" && "Point the camera at the parking ticket QR code."}
      </p>
      {error && <p className="errorMessage" role="alert">
        {state === "denied" && "Camera permission was denied. Enter the ticket number manually."}
        {state === "unavailable" && "Camera is unavailable. Enter the ticket number manually."}
        {state === "failed" && "The QR code was not recognized. Retry or enter the ticket number manually."}
      </p>}
      <button type="button" className="secondaryButton" onClick={onCancel}>Close scanner</button>
    </section>
  );
}
