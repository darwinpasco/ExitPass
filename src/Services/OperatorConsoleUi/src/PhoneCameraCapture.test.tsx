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

describe("phone-only statutory ID camera", () => {
  beforeEach(() => {
    Object.defineProperty(URL, "createObjectURL", { configurable: true, value: vi.fn(() => "blob:captured-id") });
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

  it("never renders a file, gallery, upload, or desktop fallback", () => {
    setPhone(false);
    render(<PhoneCameraCapture value={null} onChange={vi.fn()} />);

    expect(screen.queryByText("Use a supported phone to capture the customer's ID.")).not.toBeInTheDocument();
    expect(screen.queryByText("A new rear-camera photo of the presented Senior Citizen or PWD ID is required.")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Take ID Photo" })).not.toBeInTheDocument();
    expect(document.querySelector('input[type="file"]')).not.toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/choose file|browse|upload image|select image|gallery|existing photo/i);
  });

  it("requests the rear phone camera, captures an in-memory still, previews it, and retakes", async () => {
    setPhone(true);
    const firstTrack = track("environment");
    const secondTrack = track("environment");
    const getUserMedia = vi.fn()
      .mockResolvedValueOnce(stream(firstTrack))
      .mockResolvedValueOnce(stream(secondTrack));
    setMediaDevices(getUserMedia);
    mockCanvasCapture();

    render(<CameraHarness />);
    await userEvent.click(screen.getByRole("button", { name: "Take ID Photo" }));

    const video = await screen.findByLabelText("Live rear camera preview");
    Object.defineProperty(video, "videoWidth", { configurable: true, value: 1280 });
    Object.defineProperty(video, "videoHeight", { configurable: true, value: 720 });
    expect(getUserMedia).toHaveBeenCalledWith({
      audio: false,
      video: { facingMode: { ideal: "environment" } }
    });
    expect(screen.getByRole("button", { name: "Capture Photo" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Cancel" })).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Capture Photo" }));
    expect(await screen.findByText("ID image captured")).toBeInTheDocument();
    expect(screen.getByRole("img", { name: "Captured statutory ID preview" })).toHaveAttribute("src", "blob:captured-id");
    expect(firstTrack.stop).toHaveBeenCalledOnce();
    expect(document.querySelector('input[type="file"]')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Retake Photo" }));
    expect(await screen.findByLabelText("Live rear camera preview")).toBeInTheDocument();
    expect(getUserMedia).toHaveBeenCalledTimes(2);
    expect(URL.revokeObjectURL).toHaveBeenCalledWith("blob:captured-id");
  });

  it("stops camera tracks on cancel and unmount", async () => {
    setPhone(true);
    const cancelTrack = track("environment");
    const unmountTrack = track("environment");
    const getUserMedia = vi.fn()
      .mockResolvedValueOnce(stream(cancelTrack))
      .mockResolvedValueOnce(stream(unmountTrack));
    setMediaDevices(getUserMedia);
    const view = render(<PhoneCameraCapture value={null} onChange={vi.fn()} />);

    await userEvent.click(screen.getByRole("button", { name: "Take ID Photo" }));
    await screen.findByLabelText("Live rear camera preview");
    await userEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(cancelTrack.stop).toHaveBeenCalledOnce();

    await userEvent.click(screen.getByRole("button", { name: "Take ID Photo" }));
    await screen.findByLabelText("Live rear camera preview");
    view.unmount();
    expect(unmountTrack.stop).toHaveBeenCalledOnce();
  });

  it.each([
    ["NotAllowedError", "Camera permission is required."],
    ["NotFoundError", "A rear phone camera is required to capture the customer's ID."],
    ["NotReadableError", "Phone camera is unavailable. Close other camera apps and try again."]
  ])("maps %s without exposing browser exceptions or an upload fallback", async (name, message) => {
    setPhone(true);
    setMediaDevices(vi.fn().mockRejectedValue(new DOMException("private browser detail", name)));
    render(<PhoneCameraCapture value={null} onChange={vi.fn()} />);

    await userEvent.click(screen.getByRole("button", { name: "Take ID Photo" }));
    expect(await screen.findByRole("alert")).toHaveTextContent(message);
    expect(screen.queryByText(/private browser detail/i)).not.toBeInTheDocument();
    expect(document.querySelector('input[type="file"]')).not.toBeInTheDocument();
  });
});

function CameraHarness() {
  const [photo, setPhoto] = useState<File | null>(null);
  return <PhoneCameraCapture value={photo} onChange={setPhoto} />;
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

function mockCanvasCapture() {
  Object.defineProperty(HTMLCanvasElement.prototype, "getContext", {
    configurable: true,
    value: vi.fn(() => ({ drawImage: vi.fn() }))
  });
  Object.defineProperty(HTMLCanvasElement.prototype, "toBlob", {
    configurable: true,
    value: vi.fn((callback: BlobCallback) => callback(new Blob(["new-camera-photo"], { type: "image/jpeg" })))
  });
}

function restore(target: object, property: PropertyKey, descriptor?: PropertyDescriptor) {
  if (descriptor) Object.defineProperty(target, property, descriptor);
  else Reflect.deleteProperty(target, property);
}
