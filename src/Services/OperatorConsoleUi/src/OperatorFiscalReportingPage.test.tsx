import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { OperatorFiscalReportingPage } from "./OperatorFiscalReportingPage";
import { createOperatorFiscalReportingFixture, fiscalReportingPermissions } from "./fiscalReporting";

const allPermissions = Object.values(fiscalReportingPermissions);

describe("Operator Console fiscal reporting", () => {
  it("shows site-scoped authoritative EJ text and a backend txt URL", async () => {
    render(<OperatorFiscalReportingPage client={createOperatorFiscalReportingFixture()} siteReferences={["SITE-PITX"]} permissions={allPermissions} />);
    const invoice = await screen.findByText("SI-00000001");
    const pre = invoice.closest("article")?.querySelector("pre");
    expect(pre?.textContent).toContain("Customer Name      : Juan Dela Cruz");
    expect(pre?.textContent).toContain("VAT Exempt Sales   : PHP 0.00");
    expect(screen.getByRole("link", { name: "Download authoritative .txt" })).toHaveAttribute("href", expect.stringContaining("/v1/ops/operator-console/fiscal-reporting/sites/SITE-PITX/electronic-journal/download"));
  });

  it("generates X through the client and identifies it as non-closing", async () => {
    const client = createOperatorFiscalReportingFixture();
    client.generateX = vi.fn(async () => undefined);
    render(<OperatorFiscalReportingPage client={client} siteReferences={["SITE-PITX"]} permissions={allPermissions} />);
    await userEvent.click(screen.getByRole("tab", { name: "X Reading" }));
    expect(screen.getByText("Non-closing observation of the current reporting period.")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Generate X Reading" }));
    await waitFor(() => expect(client.generateX).toHaveBeenCalledWith("SITE-PITX"));
  });

  it("requires explicit confirmation before privileged Z generation", async () => {
    const client = createOperatorFiscalReportingFixture();
    client.generateZ = vi.fn(async () => undefined);
    render(<OperatorFiscalReportingPage client={client} siteReferences={["SITE-PITX"]} permissions={allPermissions} />);
    await userEvent.click(screen.getByRole("tab", { name: "Z Reading" }));
    await userEvent.click(screen.getByRole("button", { name: "Generate Z Reading" }));
    expect(client.generateZ).not.toHaveBeenCalled();
    expect(screen.getByRole("alertdialog", { name: "Confirm Z Reading close" })).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Confirm close and generate Z" }));
    await waitFor(() => expect(client.generateZ).toHaveBeenCalledWith("SITE-PITX"));
  });

  it("does not infer Z generation from Z read permission", async () => {
    render(<OperatorFiscalReportingPage client={createOperatorFiscalReportingFixture()} siteReferences={["SITE-PITX"]} permissions={[fiscalReportingPermissions.zRead]} />);
    await userEvent.click(screen.getByRole("tab", { name: "Z Reading" }));
    expect(await screen.findByText("Z-20260910-001")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Generate Z Reading" })).not.toBeInTheDocument();
  });
});
