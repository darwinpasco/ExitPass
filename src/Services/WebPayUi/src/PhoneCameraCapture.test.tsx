import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { PhoneCameraCapture } from "./PhoneCameraCapture";

const originalUserAgentData = Object.getOwnPropertyDescriptor(navigator, "userAgentData");
const originalMediaDevices = Object.getOwnPropertyDescriptor(navigator, "mediaDevices");
const originalGetContext = Object.getOwnPropertyDescriptor(HTMLCanvasElement.prototype, "getContext");
const originalToBlob = Object.getOwnPropertyDescriptor(HTMLCanvasElement.prototype, "toBlob");
const originalPlay = Object.getOwnPropertyDescriptor(HTMLMediaElement.prototype, "play");
const originalCreateObjectUrl = Object.getOwnPropertyDescriptor(URL, "createObjectURL");
const originalRevokeObjectUrl = Object.getOwnPropertyDescriptor(URL, "revokeObjectURL");

describe("WebPay phone-only evidence camera", () => {
  beforeEach(() => {
    Object.defineProperty(URL, "createObjectURL", { configurable: true, value: vi.fn(() => "blob:captured-evidence") });
    Object.defineProperty(URL, "revokeObjectURL", { configurable: true, value: vi.fn() });
    Object.defineProperty(HTMLMediaElement.prototype, "play", { configurable: true, value: vi.fn(async () => undefined) });
  });

  afterEach(() => {
    restore(navigator, "userAgentData", originalUserAgentData);
    restore(navigator, "mediaDevices", originalMediaDevices);
    restore(HTMLCanvasElement.prototype, "getContext", originalGetContext);
    restore(HTMLCanvasElement.prototype, "toBlob", originalToBlob);
    restore(HTMLMediaElement.prototype, "play", originalPlay);
    restore(URL, "createObjectURL", originalCreateObjectUrl);
    restore(URL, "revokeObjectURL", originalRevokeObjectUrl);
  });

  it("never renders a file or gallery picker", () => {
    setPhone(false);
    render(<PhoneCameraCapture value={null} onChange={vi.fn()} />);

    expect(screen.getByRole("alert")).toHaveTextContent("Use a supported phone camera to continue.");
    expect(screen.queryByRole("button", { name: "Take Photo" })).not.toBeInTheDocument();
    expect(document.querySelector('input[type="file"]')).not.toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/choose file|browse|select image|gallery|existing photo/i);
  });

  it("requests the rear camera, captures a bounded JPEG, previews it, and supports retake", async () => {
    setPhone(true);
    const firstTrack = track("environment");
    const secondTrack = track("environment");
    const getUserMedia = vi.fn()
      .mockResolvedValueOnce(stream(firstTrack))
      .mockResolvedValueOnce(stream(secondTrack));
    setMediaDevices(getUserMedia);
    const drawImage = mockCanvasCapture(new Blob(["camera-photo"], { type: "image/jpeg" }));

    render(<CameraHarness />);
    await userEvent.click(screen.getByRole("button", { name: "Take Photo" }));

    const video = await screen.findByLabelText("Live rear camera preview");
    Object.defineProperty(video, "videoWidth", { configurable: true, value: 4000 });
    Object.defineProperty(video, "videoHeight", { configurable: true, value: 3000 });
    expect(getUserMedia).toHaveBeenCalledWith({
      audio: false,
      video: { facingMode: { ideal: "environment" } }
    });

    await userEvent.click(screen.getByRole("button", { name: "Capture Photo" }));

    expect(await screen.findByText("Photo captured")).toBeInTheDocument();
    expect(screen.getByRole("img", { name: "Captured entitlement evidence preview" })).toHaveAttribute("src", "blob:captured-evidence");
    expect(drawImage).toHaveBeenCalledWith(video, 0, 0, 1440, 1080);
    expect(firstTrack.stop).toHaveBeenCalledOnce();
    expect(document.querySelector('input[type="file"]')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Retake Photo" }));
    expect(await screen.findByLabelText("Live rear camera preview")).toBeInTheDocument();
    expect(getUserMedia).toHaveBeenCalledTimes(2);
    expect(URL.revokeObjectURL).toHaveBeenCalledWith("blob:captured-evidence");
  });

  it("stops camera tracks on cancel and unmount", async () => {
    setPhone(true);
    const cancelTrack = track("environment");
    const unmountTrack = track("environment");
    setMediaDevices(vi.fn()
      .mockResolvedValueOnce(stream(cancelTrack))
      .mockResolvedValueOnce(stream(unmountTrack)));
    const view = render(<PhoneCameraCapture value={null} onChange={vi.fn()} />);

    await userEvent.click(screen.getByRole("button", { name: "Take Photo" }));
    await screen.findByLabelText("Live rear camera preview");
    await userEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(cancelTrack.stop).toHaveBeenCalledOnce();

    await userEvent.click(screen.getByRole("button", { name: "Take Photo" }));
    await screen.findByLabelText("Live rear camera preview");
    view.unmount();
    expect(unmountTrack.stop).toHaveBeenCalledOnce();
  });

  it("rejects a non-rear camera without exposing a file fallback", async () => {
    setPhone(true);
    const frontTrack = track("user");
    setMediaDevices(vi.fn().mockResolvedValue(stream(frontTrack)));
    render(<PhoneCameraCapture value={null} onChange={vi.fn()} />);

    await userEvent.click(screen.getByRole("button", { name: "Take Photo" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("A rear phone camera is required");
    expect(frontTrack.stop).toHaveBeenCalledOnce();
    expect(document.querySelector('input[type="file"]')).not.toBeInTheDocument();
  });

  it.each([
    ["NotAllowedError", "Camera permission is required."],
    ["NotFoundError", "A rear phone camera is required to capture the entitlement photo."],
    ["NotReadableError", "Phone camera is unavailable. Close other camera apps and try again."]
  ])("maps %s safely without a gallery fallback", async (name, message) => {
    setPhone(true);
    setMediaDevices(vi.fn().mockRejectedValue(new DOMException("private browser detail", name)));
    render(<PhoneCameraCapture value={null} onChange={vi.fn()} />);

    await userEvent.click(screen.getByRole("button", { name: "Take Photo" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(message);
    expect(screen.queryByText(/private browser detail/i)).not.toBeInTheDocument();
    expect(document.querySelector('input[type="file"]')).not.toBeInTheDocument();
  });

  it("fails safely when the captured JPEG cannot meet the configured byte limit", async () => {
    setPhone(true);
    const cameraTrack = track("environment");
    setMediaDevices(vi.fn().mockResolvedValue(stream(cameraTrack)));
    mockCanvasCapture(new Blob([new Uint8Array(101)], { type: "image/jpeg" }));
    render(<PhoneCameraCapture value={null} maximumContentLengthBytes={100} onChange={vi.fn()} />);

    await userEvent.click(screen.getByRole("button", { name: "Take Photo" }));
    const video = await screen.findByLabelText("Live rear camera preview");
    Object.defineProperty(video, "videoWidth", { configurable: true, value: 640 });
    Object.defineProperty(video, "videoHeight", { configurable: true, value: 480 });
    await userEvent.click(screen.getByRole("button", { name: "Capture Photo" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Retake the photo");
    expect(cameraTrack.stop).toHaveBeenCalledOnce();
    await waitFor(() => expect(HTMLCanvasElement.prototype.toBlob).toHaveBeenCalledTimes(4));
  });
});

function CameraHarness() {
  const [photo, setPhoto] = useState<File | null>(null);
  return (
    <PhoneCameraCapture
      value={photo}
      maximumImageWidth={1920}
      maximumImageHeight={1080}
      onChange={setPhoto}
    />
  );
}

function setPhone(mobile: boolean) {
  Object.defineProperty(navigator, "userAgentData", { configurable: true, value: { mobile } });
}

function setMediaDevices(getUserMedia: ReturnType<typeof vi.fn>) {
  Object.defineProperty(navigator, "mediaDevices", { configurable: true, value: { getUserMedia } });
}

function track(facingMode: string) {
  return {
    stop: vi.fn(),
    getSettings: vi.fn(() => ({ facingMode }))
  };
}

function stream(videoTrack: ReturnType<typeof track>) {
  return {
    getTracks: () => [videoTrack],
    getVideoTracks: () => [videoTrack]
  } as unknown as MediaStream;
}

function mockCanvasCapture(blob: Blob) {
  const drawImage = vi.fn();
  Object.defineProperty(HTMLCanvasElement.prototype, "getContext", {
    configurable: true,
    value: vi.fn(() => ({ drawImage }))
  });
  Object.defineProperty(HTMLCanvasElement.prototype, "toBlob", {
    configurable: true,
    value: vi.fn((callback: BlobCallback) => callback(blob))
  });
  return drawImage;
}

function restore(target: object, property: PropertyKey, descriptor?: PropertyDescriptor) {
  if (descriptor) Object.defineProperty(target, property, descriptor);
  else Reflect.deleteProperty(target, property);
}
