import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { App } from "./App";

const downloadMocks = vi.hoisted(() => ({
  html2canvas: vi.fn(),
  addImage: vi.fn(),
  save: vi.fn()
}));

vi.mock("html2canvas", () => ({ default: downloadMocks.html2canvas }));
vi.mock("jspdf", () => ({
  jsPDF: vi.fn().mockImplementation(() => ({
    addImage: downloadMocks.addImage,
    save: downloadMocks.save
  }))
}));

const canonicalText = [
  "SALES INVOICE",
  "SI NO                   SI-00000105",
  "CUSTOMER INFORMATION",
  "ADDRESS       123 Doña Juana",
  "              Townhomes, Pasig City",
  "===== NOTHING FOLLOWS ====="
].join("\r\n");

describe("public customer Digital Sales Invoice page", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    downloadMocks.html2canvas.mockResolvedValue({
      width: 800,
      height: 1600,
      toDataURL: () => "data:image/png;base64,receipt"
    });
  });

  afterEach(() => {
    cleanup();
    window.history.replaceState(null, "", "/");
  });

  it("loads canonical text anonymously from the fragment capability and downloads the same receipt", async () => {
    window.history.replaceState(null, "", "/webpay/sales-invoice?ticket=ignored#access=opaque-capability&ticket=ignored");
    const fetchMock = vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response(JSON.stringify({
      fiscalDocumentNumber: "SI-00000105",
      presentation: { documentTitle: "Sales Invoice" },
      canonicalText
    }), { status: 200, headers: { "Content-Type": "application/json" } }));

    render(<App />);

    const receipt = await screen.findByLabelText("Printable Sales Invoice");
    expect(receipt.querySelector("pre")?.textContent).toBe(canonicalText);
    expect(screen.queryByText(/sign in|log in/i)).not.toBeInTheDocument();
    expect(window.location.pathname).toBe("/webpay/sales-invoice");
    expect(window.location.search).toBe("");
    expect(window.location.hash).toBe("");
    expect(fetchMock).toHaveBeenCalledWith(
      "/v1/webpay/digital-sales-invoices/presentation",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ accessToken: "opaque-capability" })
      })
    );
    const request = fetchMock.mock.calls[0][1] as RequestInit;
    expect(request.headers).not.toHaveProperty("Authorization");

    await userEvent.click(screen.getByRole("button", { name: "Download Sales Invoice" }));
    await waitFor(() => expect(downloadMocks.save).toHaveBeenCalledWith("SI-00000105.pdf"));
    expect(downloadMocks.html2canvas).toHaveBeenCalledWith(receipt, expect.any(Object));
  });

  it.each(["expired-capability", "malformed-token", "tampered-token"])(
    "shows the same safe unavailable state for an invalid capability: %s",
    async (token) => {
      window.history.replaceState(null, "", `/webpay/sales-invoice#access=${token}`);
      vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response(null, { status: 404 }));

      render(<App />);

      expect(await screen.findByText("Sales Invoice unavailable or link expired.")).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Download Sales Invoice" })).not.toBeInTheDocument();
      expect(screen.queryByText(/sign in|log in/i)).not.toBeInTheDocument();
    }
  );

  it("fails safely without a capability and does not call the endpoint", async () => {
    window.history.replaceState(null, "", "/webpay/sales-invoice");
    const fetchMock = vi.spyOn(globalThis, "fetch");

    render(<App />);

    expect(screen.getByText("Sales Invoice unavailable or link expired.")).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
