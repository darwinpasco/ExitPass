import { act, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DEFAULT_POS_VERSION_LABEL, formatManilaDateTime, ProductRuntimeIdentity } from "./ProductRuntimeIdentity";

describe("ProductRuntimeIdentity", () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllEnvs();
  });

  it("formats the workstation clock in Asia/Manila", () => {
    expect(formatManilaDateTime(new Date("2026-10-20T07:30:00Z")))
      .toBe("October 20, 2026 | 3:30:00 PM");
  });

  it("shows the stable configured version and advances without a refresh", () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-10-20T07:30:00Z"));
    vi.stubEnv("VITE_EXITPASS_POS_VERSION_LABEL", DEFAULT_POS_VERSION_LABEL);

    render(<ProductRuntimeIdentity />);
    expect(screen.getByText(DEFAULT_POS_VERSION_LABEL)).toBeInTheDocument();
    expect(screen.getByText("October 20, 2026 | 3:30:00 PM")).toBeInTheDocument();

    act(() => {
      vi.advanceTimersByTime(1_000);
    });

    expect(screen.getByText("October 20, 2026 | 3:30:01 PM")).toBeInTheDocument();
  });
});
