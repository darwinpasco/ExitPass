import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import {
  formatPhtInstant,
  formatPhtInputValue,
  OperatorFiscalReportingPage,
  phtInputToUtc
} from "./OperatorFiscalReportingPage";
import { createOperatorFiscalReportingFixture, fiscalReportingPermissions } from "./fiscalReporting";

const allPermissions = Object.values(fiscalReportingPermissions);

describe("Operator Console fiscal reporting", () => {
  it("converts explicit UTC instants to PHT without using the workstation timezone", () => {
    expect(formatPhtInstant("2026-09-13T23:00:00Z")).toBe("2026-09-14 07:00:00");
  });

  it("converts operator-entered PHT values to explicit UTC API instants", () => {
    expect(phtInputToUtc("2026-09-14T07:00:00")).toBe("2026-09-13T23:00:00Z");
    expect(phtInputToUtc("2026-09-14T07:00:01")).toBe("2026-09-13T23:00:01Z");
  });

  it("initializes the EJ range from the current fiscal period and calls the API with UTC instants", async () => {
    const client = createPitxFiscalReportingClient();
    render(<OperatorFiscalReportingPage client={client} siteReferences={["SITE-PITX"]} permissions={allPermissions} />);

    const start = await screen.findByLabelText("EJ period start PHT");
    const end = screen.getByLabelText("EJ period end PHT");
    expect(start).toHaveAttribute("type", "datetime-local");
    expect(start).toHaveAttribute("step", "1");
    expect(end).toHaveAttribute("type", "datetime-local");
    expect(end).toHaveAttribute("step", "1");
    expect(formatPhtInputValue("2026-09-13T23:00:00Z")).toBe("2026-09-14T07:00:00");
    expect(start).toHaveValue("2026-09-14T07:00");
    expect(end).toHaveValue("2026-09-15T07:00");
    await waitFor(() => expect(client.readEj).toHaveBeenCalledWith(
      "SITE-PITX",
      "2026-09-13T23:00:00Z",
      "2026-09-14T23:00:00Z",
      ""
    ));
    expect(screen.getByText("2026-09-14 08:15:00 PHT")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Download authoritative .txt" })).toHaveAttribute(
      "href",
      expect.stringContaining("periodStart=2026-09-13T23%3A00%3A00Z")
    );
  });

  it("converts edited PHT fields before requesting the Electronic Journal", async () => {
    const client = createPitxFiscalReportingClient();
    const user = userEvent.setup();
    render(<OperatorFiscalReportingPage client={client} siteReferences={["SITE-PITX"]} permissions={allPermissions} />);
    const start = await screen.findByLabelText("EJ period start PHT");
    const end = screen.getByLabelText("EJ period end PHT");
    await waitFor(() => expect(client.readEj).toHaveBeenCalled());
    vi.mocked(client.readEj).mockClear();

    fireEvent.change(start, { target: { value: "2026-09-14T07:00" } });
    fireEvent.change(end, { target: { value: "2026-09-15T07:00" } });
    await user.click(screen.getByRole("button", { name: "View journal" }));

    await waitFor(() => expect(client.readEj).toHaveBeenCalledWith(
      "SITE-PITX",
      "2026-09-13T23:00:00Z",
      "2026-09-14T23:00:00Z",
      ""
    ));
  });

  it.each(["X", "Z"] as const)("shows %s Reading business date, period boundaries, and generated time in PHT", async (kind) => {
    render(<OperatorFiscalReportingPage client={createPitxFiscalReportingClient()} siteReferences={["SITE-PITX"]} permissions={allPermissions} />);

    await userEvent.click(screen.getByRole("tab", { name: `${kind} Reading` }));

    expect(await screen.findByText("Fiscal Business Date")).toBeInTheDocument();
    expect(screen.getByText("2026-09-14", { selector: "dd" })).toBeInTheDocument();
    expect(screen.getByText("Period start (PHT)")).toBeInTheDocument();
    expect(screen.getByText("2026-09-14 07:00:00")).toBeInTheDocument();
    expect(screen.getByText("Period end (PHT)")).toBeInTheDocument();
    expect(screen.getByText("2026-09-15 07:00:00")).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Generated (PHT)" })).toBeInTheDocument();
    expect(screen.getByText("2026-09-14 07:30:00 PHT")).toBeInTheDocument();
  });

  it("shows the site-scoped authoritative Sales Invoice receipt without reconstructing it", async () => {
    render(<OperatorFiscalReportingPage client={createOperatorFiscalReportingFixture()} siteReferences={["SITE-PITX"]} permissions={allPermissions} />);
    const invoice = await screen.findByText("SI-00000001");
    const pre = invoice.closest("article")?.querySelector("pre");
    expect(pre?.textContent).toContain("SALES INVOICE");
    expect(pre?.textContent).toContain("ORIGINAL");
    expect(pre?.textContent).toContain("PARKING DETAILS");
    expect(pre?.textContent).toContain("ITEMS");
    expect(pre?.textContent).toContain("DISCOUNTS");
    expect(pre?.textContent).toContain("PAYMENT DETAILS");
    expect(pre?.textContent).toContain("THIS SERVES AS YOUR SALES INVOICE");
    expect(pre?.textContent).toContain("Customer Information");
    expect(pre?.textContent).toContain("POS SOFTWARE SUPPLIER / DEVELOPER");
    expect(pre?.textContent).toContain("THANK YOU FOR CHOOSING OUR SERVICE");
    expect(pre?.textContent).toContain("NOTHING FOLLOWS");
    expect(pre?.textContent).toContain("VAT Exempt Sales                        PHP 0.00");
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
    await userEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(client.generateZ).not.toHaveBeenCalled();
    expect(screen.queryByRole("alertdialog", { name: "Confirm Z Reading close" })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Generate Z Reading" }));
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

function createPitxFiscalReportingClient() {
  const client = createOperatorFiscalReportingFixture();
  const originalReadEj = client.readEj;
  const originalReadX = client.readX;
  const originalReadZ = client.readZ;
  client.readEj = vi.fn(async (siteId, start, end, search) => (await originalReadEj(siteId, start, end, search)).map((invoice) => ({
    ...invoice,
    businessDayDate: "2026-09-14",
    issuedAt: "2026-09-14T00:15:00Z"
  })));
  client.readX = async (siteId) => withPitxPeriod(await originalReadX(siteId));
  client.readZ = async (siteId) => withPitxPeriod(await originalReadZ(siteId));
  client.ejUrl = (siteId, start, end, search) => {
    const query = new URLSearchParams({ periodStart: start, periodEnd: end });
    if (search?.trim()) query.set("search", search.trim());
    return `/v1/ops/operator-console/fiscal-reporting/sites/${siteId}/electronic-journal/download?${query}`;
  };
  return client;
}

function withPitxPeriod(history: Awaited<ReturnType<ReturnType<typeof createOperatorFiscalReportingFixture>["readX"]>>) {
  const period = {
    ...history.currentPeriod!,
    businessDayDate: "2026-09-14",
    periodStartAt: "2026-09-13T23:00:00Z",
    periodEndAt: "2026-09-14T23:00:00Z"
  };
  return {
    currentPeriod: period,
    readings: history.readings.map((reading) => ({
      ...reading,
      businessDayDate: period.businessDayDate,
      periodStartAt: period.periodStartAt,
      periodEndAt: period.periodEndAt,
      generatedAt: "2026-09-13T23:30:00Z"
    }))
  };
}
