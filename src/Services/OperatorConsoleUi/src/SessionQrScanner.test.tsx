import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SessionQrScanner } from "./SessionQrScanner";

const scanner = vi.hoisted(() => ({
  callback: undefined as ((result: { getText(): string } | undefined, error?: Error) => void) | undefined,
  stop: vi.fn(),
  decode: vi.fn()
}));

vi.mock("@zxing/browser", () => ({
  BrowserQRCodeReader: vi.fn().mockImplementation(() => ({
    decodeFromVideoDevice: scanner.decode
  }))
}));

describe("SessionQrScanner", () => {
  beforeEach(() => {
    scanner.callback = undefined;
    scanner.stop.mockReset();
    scanner.decode.mockReset().mockImplementation(async (_device, _video, callback) => {
      scanner.callback = callback;
      return { stop: scanner.stop };
    });
    Object.defineProperty(navigator, "mediaDevices", {
      configurable: true,
      value: { getUserMedia: vi.fn() }
    });
  });

  it("decodes once and releases the camera", async () => {
    const onDecoded = vi.fn();
    const view = render(<SessionQrScanner onDecoded={onDecoded} onCancel={vi.fn()} />);
    await waitFor(() => expect(scanner.callback).toBeTypeOf("function"));

    scanner.callback?.({ getText: () => "1474119573131" });
    scanner.callback?.({ getText: () => "duplicate" });

    expect(onDecoded).toHaveBeenCalledOnce();
    expect(onDecoded).toHaveBeenCalledWith("1474119573131");
    expect(scanner.stop).toHaveBeenCalled();
    view.unmount();
    expect(scanner.stop).toHaveBeenCalled();
  });

  it("handles cancellation without changing lookup data", async () => {
    const user = userEvent.setup();
    const onCancel = vi.fn();
    render(<SessionQrScanner onDecoded={vi.fn()} onCancel={onCancel} />);

    await user.click(screen.getByRole("button", { name: "Close scanner" }));

    expect(onCancel).toHaveBeenCalledOnce();
  });

  it("reports camera permission denial safely", async () => {
    scanner.decode.mockRejectedValueOnce(new DOMException("Denied", "NotAllowedError"));
    render(<SessionQrScanner onDecoded={vi.fn()} onCancel={vi.fn()} />);

    expect(await screen.findByRole("alert")).toHaveTextContent("Camera permission was denied");
  });
});
