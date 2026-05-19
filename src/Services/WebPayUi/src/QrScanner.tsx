import { useEffect, useRef, useState } from "react";
import { BrowserQRCodeReader, type IScannerControls } from "@zxing/browser";

type CameraState = "idle" | "starting" | "scanning" | "denied" | "unavailable" | "failed";

type QrScannerProps = {
  onDecoded: (value: string) => void;
};

function isPermissionDenied(error: unknown): boolean {
  if (!(error instanceof DOMException)) {
    return false;
  }

  return error.name === "NotAllowedError" || error.name === "SecurityError" || error.name === "PermissionDeniedError";
}

export function QrScanner({ onDecoded }: QrScannerProps) {
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const controlsRef = useRef<IScannerControls | null>(null);
  const [cameraState, setCameraState] = useState<CameraState>("idle");

  useEffect(() => {
    return () => {
      controlsRef.current?.stop();
    };
  }, []);

  async function startScanner() {
    if (!navigator.mediaDevices?.getUserMedia) {
      setCameraState("unavailable");
      return;
    }

    if (!videoRef.current) {
      setCameraState("failed");
      return;
    }

    setCameraState("starting");

    try {
      const reader = new BrowserQRCodeReader();
      controlsRef.current = await reader.decodeFromVideoDevice(undefined, videoRef.current, (result, error) => {
        if (result) {
          controlsRef.current?.stop();
          setCameraState("idle");
          onDecoded(result.getText());
          return;
        }

        if (error && error.name !== "NotFoundException") {
          setCameraState("failed");
        }
      });
      setCameraState("scanning");
    } catch (error) {
      setCameraState(isPermissionDenied(error) ? "denied" : "unavailable");
    }
  }

  function stopScanner() {
    controlsRef.current?.stop();
    setCameraState("idle");
  }

  const isActive = cameraState === "starting" || cameraState === "scanning";
  const hasCameraError = cameraState === "denied" || cameraState === "unavailable" || cameraState === "failed";

  return (
    <section className="scan-panel" aria-label="QR scanner">
      <div className="scan-copy">
        <img src="/assets/icons/qr-scan.svg" alt="" aria-hidden="true" />
        <div>
          <h2>Scan ticket QR</h2>
          <p>Use the camera to read the ticket QR code. The decoded value becomes the ticket reference.</p>
        </div>
      </div>

      <div className="camera-frame">
        <video ref={videoRef} className={isActive ? "camera-video is-active" : "camera-video"} muted playsInline />
        {!isActive && (
          <div className="camera-placeholder">
            <img src="/assets/illustrations/camera-permission.svg" alt="" aria-hidden="true" />
            <span>Camera starts only when you tap scan.</span>
          </div>
        )}
      </div>

      <div className="button-row">
        <button type="button" className="primary-button" onClick={startScanner} disabled={isActive}>
          <img src="/assets/icons/qr-scan.svg" alt="" aria-hidden="true" />
          {cameraState === "starting" ? "Starting..." : "Scan QR"}
        </button>
        {isActive && (
          <button type="button" className="ghost-button" onClick={stopScanner}>
            Stop
          </button>
        )}
      </div>

      {hasCameraError && (
        <div className="inline-error" role="alert">
          <img src="/assets/icons/error.svg" alt="" aria-hidden="true" />
          {cameraState === "denied" && "Camera permission was denied. Enter the ticket reference manually below."}
          {cameraState === "unavailable" && "Camera is unavailable on this device. Enter the ticket reference manually below."}
          {cameraState === "failed" && "QR scanning failed. You can try again or enter the ticket reference manually."}
        </div>
      )}
    </section>
  );
}
