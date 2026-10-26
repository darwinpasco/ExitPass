import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
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
    await waitFor(() => expect(start).toHaveValue("2026-09-14T07:00"));
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

  it("clears the previous Electronic Journal result when a new search fails", async () => {
    const client = createPitxFiscalReportingClient();
    const user = userEvent.setup();
    render(<OperatorFiscalReportingPage client={client} siteReferences={["SITE-PITX"]} permissions={allPermissions} />);

    expect(await screen.findByText("SI-00000001")).toBeInTheDocument();
    vi.mocked(client.readEj).mockRejectedValueOnce(new Error("Electronic Journal lookup failed."));

    await user.type(screen.getByLabelText("Search Electronic Journal"), "SI-00000049");
    await user.click(screen.getByRole("button", { name: "View journal" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Electronic Journal lookup failed.");
    expect(screen.queryByText("SI-00000001")).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Download authoritative .txt" })).not.toBeInTheDocument();
  });

  it.each(["X", "Z"] as const)("shows %s Reading business date, period boundaries, and generated time in PHT", async (kind) => {
    render(<OperatorFiscalReportingPage client={createPitxFiscalReportingClient()} siteReferences={["SITE-PITX"]} permissions={allPermissions} />);

    await userEvent.click(await screen.findByRole("tab", { name: `${kind} Reading` }));

    expect(await screen.findByText("Fiscal Business Date", { selector: "dt" })).toBeInTheDocument();
    expect(screen.getByText("2026-09-14", { selector: "dd" })).toBeInTheDocument();
    expect(screen.getByText("Period start (PHT)")).toBeInTheDocument();
    expect(screen.getByText("2026-09-14 07:00:00")).toBeInTheDocument();
    expect(screen.getByText("Period end (PHT)")).toBeInTheDocument();
    expect(screen.getByText("2026-09-15 07:00:00")).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Generated (PHT)" })).toBeInTheDocument();
    expect(screen.getByText("2026-09-14 07:30:00 PHT")).toBeInTheDocument();
  });

  it.each(["X", "Z"] as const)("shows explicit PDF and JSON actions for %s Reading without the old ambiguous action", async (kind) => {
    render(<OperatorFiscalReportingPage client={createPitxFiscalReportingClient()} siteReferences={["SITE-PITX"]} permissions={allPermissions} />);

    await userEvent.click(await screen.findByRole("tab", { name: `${kind} Reading` }));

    expect(await screen.findByRole("link", { name: "Download PDF" })).toHaveAttribute("href", expect.stringContaining("format=pdf"));
    expect(screen.getByRole("link", { name: "Download JSON" })).toHaveAttribute("href", expect.stringContaining("format=json"));
    expect(screen.queryByRole("link", { name: "Print / download" })).not.toBeInTheDocument();
  });

  it("renders the bounded Audit Trail, exact sample column order, filters, downloads, and action legend", async () => {
    const client = createPitxFiscalReportingClient();
    client.readAuditTrail = vi.fn(client.readAuditTrail);
    render(<OperatorFiscalReportingPage client={client} siteReferences={["SITE-PITX"]} permissions={allPermissions} />);

    await userEvent.click(await screen.findByRole("tab", { name: "Audit Trail" }));
    expect(screen.getByLabelText("Audit Trail from PHT")).toHaveAttribute("type", "datetime-local");
    expect(screen.getByLabelText("Audit Trail to PHT")).toHaveAttribute("type", "datetime-local");
    await userEvent.selectOptions(screen.getByLabelText("Audit Trail result"), "ALLOWED");
    await userEvent.type(screen.getByLabelText("Audit Trail user or actor"), "System");
    await userEvent.type(screen.getByLabelText("Audit Trail SI or reference"), "SI-00000116");
    await userEvent.click(screen.getByRole("button", { name: "View Audit Trail" }));

    await waitFor(() => expect(client.readAuditTrail).toHaveBeenCalled());
    const table = await screen.findByRole("table", { name: "Audit Trail" });
    expect(within(table).getAllByRole("columnheader").map((item) => item.textContent)).toEqual([
      "Date", "Terminal", "UserID", "User Role", "Action Category", "Action Description",
      "SI No.", "Before Value", "After Value", "Reason/Remarks"
    ]);
    expect(within(table).getByText("SALES TRANSACTION")).toBeInTheDocument();
    expect(within(table).getByText("SI-00000001")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Download PDF" })).toHaveAttribute("href", expect.stringContaining("format=pdf"));
    expect(screen.getByRole("link", { name: "Download CSV" })).toHaveAttribute("href", expect.stringContaining("format=csv"));
    expect(screen.getByRole("link", { name: "Download JSON" })).toHaveAttribute("href", expect.stringContaining("format=json"));
    expect(screen.getByText("Action Code Legend")).toBeInTheDocument();
    expect(screen.getByText(/fiscal_document_committed - SALES TRANSACTION/)).toBeInTheDocument();
  });

  it("shows the site-scoped authoritative Sales Invoice receipt without reconstructing it", async () => {
    render(<OperatorFiscalReportingPage client={createOperatorFiscalReportingFixture()} siteReferences={["SITE-PITX"]} permissions={allPermissions} />);
    const invoice = await screen.findByText("SI-00000001");
    const pre = invoice.closest("article")?.querySelector("pre");
    expect(pre?.textContent).toContain("SALES INVOICE");
    expect(pre?.textContent).toContain("ORIGINAL");
    expect(pre?.textContent).toContain("BUSINESS DATE");
    expect(pre?.textContent).toContain("CASHIER");
    expect(pre?.textContent).toContain("DURATION");
    expect(pre?.textContent).toContain("PARKING FEE");
    expect(pre?.textContent).toContain("AMOUNT DUE");
    expect(pre?.textContent).toContain("TOTAL PAID");
    expect(pre?.textContent).toContain("THIS SERVES AS YOUR SALES INVOICE");
    expect(pre?.textContent).toContain("CUSTOMER INFORMATION");
    expect(pre?.textContent).toContain("POS SOFTWARE SUPPLIER / DEVELOPER");
    expect(pre?.textContent).toContain("THANK YOU FOR CHOOSING OUR SERVICE");
    expect(pre?.textContent).toContain("NOTHING FOLLOWS");
    expect(pre?.textContent).toContain("VAT EXEMPT SALES              0.00");
    expect(pre?.textContent).not.toContain("PROVIDER");
    expect(screen.getByRole("link", { name: "Download authoritative .txt" })).toHaveAttribute("href", expect.stringContaining("/v1/ops/operator-console/fiscal-reporting/sites/SITE-PITX/electronic-journal/download"));
  });

  it("generates X through the client and identifies it as non-closing", async () => {
    const client = createOperatorFiscalReportingFixture();
    client.generateX = vi.fn(async () => undefined);
    render(<OperatorFiscalReportingPage client={client} siteReferences={["SITE-PITX"]} permissions={allPermissions} />);
    await userEvent.click(await screen.findByRole("tab", { name: "X Reading" }));
    expect(screen.getByText("Non-closing observation of the current reporting period.")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Generate X Reading" }));
    await waitFor(() => expect(client.generateX).toHaveBeenCalledWith("SITE-PITX"));
  });

  it("keeps EJ, X history, Z history, and open dates available when there is no current X period", async () => {
    const client = createOperatorFiscalReportingFixture();
    const x = await client.readX("SITE-PITX");
    const z = await client.readZ("SITE-PITX");
    client.readX = vi.fn(async () => ({ currentPeriod: undefined, readings: x.readings }));
    client.readZ = vi.fn(async () => ({ currentPeriod: undefined, readings: z.readings }));
    render(<OperatorFiscalReportingPage client={client} siteReferences={["SITE-PITX"]} permissions={allPermissions} />);

    fireEvent.change(await screen.findByLabelText("EJ period start PHT"), { target: { value: "2026-09-10T00:00" } });
    fireEvent.change(screen.getByLabelText("EJ period end PHT"), { target: { value: "2026-09-11T00:00" } });
    await userEvent.click(screen.getByRole("button", { name: "View journal" }));
    expect(await screen.findByText("SI-00000001")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("tab", { name: "X Reading" }));
    expect(await screen.findByText("X-20260910-001")).toBeInTheDocument();
    expect(screen.getByText("No open fiscal reporting period is currently available for X generation.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Generate X Reading" })).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole("tab", { name: "Z Reading" }));
    expect(await screen.findByText("Z-20260910-001")).toBeInTheDocument();
    expect(screen.getByText("Open Fiscal Business Dates")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Close Business Date" })).toBeInTheDocument();
    expect(screen.queryByText("Fiscal reporting unavailable")).not.toBeInTheDocument();
  });

  it("keeps fiscal errors scoped to the failed operation", async () => {
    const client = createOperatorFiscalReportingFixture();
    client.readX = vi.fn(async () => { throw new Error("X history is temporarily unavailable."); });
    render(<OperatorFiscalReportingPage client={client} siteReferences={["SITE-PITX"]} permissions={allPermissions} />);

    await userEvent.click(await screen.findByRole("tab", { name: "X Reading" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("X history is temporarily unavailable.");

    await userEvent.click(screen.getByRole("tab", { name: "Z Reading" }));
    expect(await screen.findByText("Z-20260910-001")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Close Business Date" })).toBeInTheDocument();

    await userEvent.click(screen.getByRole("tab", { name: "Electronic Journal" }));
    expect(screen.getByRole("heading", { name: "Electronic Journal" })).toBeInTheDocument();
    expect(screen.queryByText("Fiscal reporting unavailable")).not.toBeInTheDocument();
  });

  it("requires explicit confirmation before closing one POS-returned Fiscal Business Date", async () => {
    const client = createOperatorFiscalReportingFixture();
    const initialCloseable = await client.readCloseablePeriods("SITE-PITX");
    client.readCloseablePeriods = vi.fn()
      .mockResolvedValueOnce(initialCloseable)
      .mockResolvedValue({ fiscalBusinessDates: [] });
    client.closeBusinessDate = vi.fn(async () => ({ birSalesSummaryCompletion: { status: "COMMITTED" as const, summaryId: "summary-1", code: "bir_sales_summary_committed", retryable: false } }));
    render(<OperatorFiscalReportingPage client={client} siteReferences={["SITE-PITX"]} permissions={allPermissions} />);
    await userEvent.click(await screen.findByRole("tab", { name: "Z Reading" }));
    await userEvent.click(screen.getByRole("button", { name: "Close Business Date" }));
    expect(client.closeBusinessDate).not.toHaveBeenCalled();
    expect(screen.getByRole("alertdialog", { name: "Confirm Fiscal Business Date close" })).toHaveTextContent("PITX Test Site");
    expect(screen.getByRole("alertdialog", { name: "Confirm Fiscal Business Date close" })).toHaveTextContent("2026-09-10");
    await userEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(client.closeBusinessDate).not.toHaveBeenCalled();
    expect(screen.queryByRole("alertdialog", { name: "Confirm Fiscal Business Date close" })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Close Business Date" }));
    await userEvent.click(screen.getByRole("alertdialog", { name: "Confirm Fiscal Business Date close" }).querySelector("button.dangerButton")!);
    await waitFor(() => expect(client.closeBusinessDate).toHaveBeenCalledWith("SITE-PITX", expect.objectContaining({ fiscalBusinessDate: "2026-09-10", expectedStateVersion: 14 })));
    expect(await screen.findByText("Fiscal Business Date 2026-09-10 closed for PITX Test Site.")).toBeInTheDocument();
  });

  it("reports BIR Summary pending without reporting the committed Z close as failed", async () => {
    const client = createOperatorFiscalReportingFixture();
    const initialCloseable = await client.readCloseablePeriods("SITE-PITX");
    client.readCloseablePeriods = vi.fn()
      .mockResolvedValueOnce(initialCloseable)
      .mockResolvedValue({ fiscalBusinessDates: [] });
    client.closeBusinessDate = vi.fn(async () => ({ birSalesSummaryCompletion: { status: "PENDING" as const, code: "bir_sales_summary_persistence_failed", retryable: true } }));
    render(<OperatorFiscalReportingPage client={client} siteReferences={["SITE-PITX"]} permissions={allPermissions} />);

    await userEvent.click(await screen.findByRole("tab", { name: "Z Reading" }));
    await userEvent.click(screen.getByRole("button", { name: "Close Business Date" }));
    await userEvent.click(screen.getByRole("alertdialog", { name: "Confirm Fiscal Business Date close" }).querySelector("button.dangerButton")!);

    expect(await screen.findByText("Fiscal Business Date 2026-09-10 closed for PITX Test Site.")).toBeInTheDocument();
    expect(screen.getByText("Fiscal Business Date closed and Z Reading committed. Daily BIR Sales Summary is pending and can be retried from Daily BIR Sales Summary.")).toBeInTheDocument();
    expect(screen.queryByText(/Z Reading.*failed/i)).not.toBeInTheDocument();
  });

  it("confirms and closes all POS-returned periods for only the current Site", async () => {
    const client = createOperatorFiscalReportingFixture();
    const initial = await client.readCloseablePeriods("SITE-PITX");
    const threePeriods = [
      { ...initial.fiscalBusinessDates[0], fiscalReportingPeriodId: "91000000-0000-4000-8000-000000000010", fiscalBusinessDate: "2026-09-08" },
      { ...initial.fiscalBusinessDates[0], fiscalReportingPeriodId: "91000000-0000-4000-8000-000000000011", fiscalBusinessDate: "2026-09-09" },
      { ...initial.fiscalBusinessDates[0], fiscalReportingPeriodId: "91000000-0000-4000-8000-000000000012", fiscalBusinessDate: "2026-09-10" }
    ];
    client.readCloseablePeriods = vi.fn()
      .mockResolvedValueOnce({ fiscalBusinessDates: threePeriods })
      .mockResolvedValue({ fiscalBusinessDates: [] });
    const initialHistory = await client.readZ("SITE-PITX");
    client.readZ = vi.fn(async () => ({
      currentPeriod: initialHistory.currentPeriod,
      readings: threePeriods.map((period, index) => ({
        ...initialHistory.readings[0],
        fiscalReportingPeriodId: period.fiscalReportingPeriodId,
        businessDayDate: period.fiscalBusinessDate,
        reportReference: `Z-BATCH-${index + 1}`
      }))
    }));
    client.closeAllBusinessDates = vi.fn(async () => ({ succeeded: true, closedCount: 3, birSalesSummaryPendingCount: 0 }));
    render(<OperatorFiscalReportingPage client={client} siteReferences={["SITE-PITX"]} permissions={allPermissions} />);

    await userEvent.click(await screen.findByRole("tab", { name: "Z Reading" }));
    await userEvent.click(await screen.findByRole("button", { name: "Close All Open Business Dates" }));

    const confirmation = screen.getByRole("alertdialog", { name: "Confirm Fiscal Business Date close" });
    expect(confirmation).toHaveTextContent("Close 3 open Fiscal Business Dates for PITX Test Site");
    expect(confirmation).toHaveTextContent("2026-09-08 through 2026-09-10");
    await userEvent.click(within(confirmation).getByRole("button", { name: "Close All Open Business Dates" }));
    await waitFor(() => expect(client.closeAllBusinessDates).toHaveBeenCalledWith("SITE-PITX"));
    expect(await screen.findByText("3 Fiscal Business Dates closed for PITX Test Site.")).toBeInTheDocument();
  });

  it("does not infer Z generation from Z read permission", async () => {
    render(<OperatorFiscalReportingPage client={createOperatorFiscalReportingFixture()} siteReferences={["SITE-PITX"]} permissions={[fiscalReportingPermissions.zRead]} />);
    await userEvent.click(await screen.findByRole("tab", { name: "Z Reading" }));
    expect(await screen.findByText("Z-20260910-001")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Generate Z Reading" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Close Business Date" })).not.toBeInTheDocument();
  });

  it("generates and presents the POS-authoritative Daily BIR Sales Summary without recalculation", async () => {
    const client = createOperatorFiscalReportingFixture();
    client.generateBirSalesSummary = vi.fn(client.generateBirSalesSummary);
    render(<OperatorFiscalReportingPage client={client} siteReferences={["SITE-PITX"]} permissions={allPermissions} />);

    const dailySummaryTab = await screen.findByRole("tab", { name: "Daily BIR Sales Summary" });
    expect(screen.queryByRole("tab", { name: "BIR Sales Summary" })).not.toBeInTheDocument();
    await userEvent.click(dailySummaryTab);
    expect(await screen.findByLabelText("Closed Fiscal Business Date")).toHaveValue("Z-20260910-001");
    await userEvent.click(screen.getByRole("button", { name: "Generate / View Summary" }));

    await waitFor(() => expect(client.generateBirSalesSummary).toHaveBeenCalledWith(
      "SITE-PITX",
      expect.objectContaining({ reportReference: "Z-20260910-001", reportStatus: "COMMITTED" })
    ));
    expect(screen.getByLabelText("Daily BIR Sales Summary values")).toHaveTextContent("Gross Sales₱112.00");
    expect(screen.getByLabelText("Daily BIR Sales Summary values")).toHaveTextContent("Previous Grand Total Amount₱1,000.00");
    expect(screen.getByRole("heading", { name: "Annex E-1 - BIR Sales Summary Report" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Download JSON" })).toHaveAttribute("href", expect.stringContaining("format=json"));
    expect(screen.getByRole("link", { name: "Download CSV" })).toHaveAttribute("href", expect.stringContaining("format=csv"));
    expect(screen.getAllByRole("link", { name: "Download PDF" })[0]).toHaveAttribute("href", expect.stringContaining("/bir-sales-summaries/"));
    expect(screen.getAllByRole("link", { name: "Download PDF" })[0]).toHaveAttribute("href", expect.stringContaining("format=pdf"));
  });

  it("does not permit BIR summary generation while only an open fiscal business date exists", async () => {
    const client = createOperatorFiscalReportingFixture();
    const history = await client.readZ("SITE-PITX");
    client.readZ = vi.fn(async () => ({ currentPeriod: history.currentPeriod, readings: [] }));
    client.generateBirSalesSummary = vi.fn(client.generateBirSalesSummary);
    render(<OperatorFiscalReportingPage client={client} siteReferences={["SITE-PITX"]} permissions={allPermissions} />);

    await userEvent.click(await screen.findByRole("tab", { name: "Daily BIR Sales Summary" }));

    expect(screen.getByText("The Daily BIR Sales Summary can be generated after the fiscal business date has been closed and its Z Reading has been committed.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Generate / View Summary" })).not.toBeInTheDocument();
    expect(client.generateBirSalesSummary).not.toHaveBeenCalled();
  });

  it("loads the current POS Annex E-1 without generating another revision", async () => {
    const client = createOperatorFiscalReportingFixture();
    client.readCurrentAnnexE1 = vi.fn(client.readCurrentAnnexE1);
    client.generateAnnexE1 = vi.fn(client.generateAnnexE1);
    render(<OperatorFiscalReportingPage client={client} siteReferences={["SITE-PITX"]} permissions={allPermissions} />);

    await userEvent.click(await screen.findByRole("tab", { name: "Daily BIR Sales Summary" }));
    expect(screen.queryByRole("button", { name: "Generate Annex E-1" })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Generate / View Summary" }));

    await waitFor(() => expect(client.readCurrentAnnexE1).toHaveBeenCalledWith("SITE-PITX", 2026, 9));
    expect(await screen.findByRole("link", { name: "Download XLSX" })).toHaveAttribute("href", expect.stringContaining("/annex-e/e1/workbooks/"));
    expect(screen.getByText(/Current Annex E-1:/)).toHaveTextContent("revision 1");
    expect(client.generateAnnexE1).not.toHaveBeenCalled();
  });

  it("clears the prior workbook when the month changes and treats no current workbook as a normal state", async () => {
    const client = createOperatorFiscalReportingFixture();
    const existing = await client.readCurrentAnnexE1("SITE-PITX", 2026, 9);
    client.readCurrentAnnexE1 = vi.fn(async (_site, year, month) =>
      year === 2026 && month === 9 ? existing : undefined);
    render(<OperatorFiscalReportingPage client={client} siteReferences={["SITE-PITX"]} permissions={allPermissions} />);

    await userEvent.click(await screen.findByRole("tab", { name: "Daily BIR Sales Summary" }));
    await userEvent.click(screen.getByRole("button", { name: "Generate / View Summary" }));
    expect(await screen.findByText(/Current Annex E-1:/)).toBeInTheDocument();

    const month = screen.getByLabelText("Annex reporting month");
    await userEvent.clear(month);
    await userEvent.type(month, "2026-10");

    expect(await screen.findByText("No Annex E-1 workbook has been generated for this month.")).toBeInTheDocument();
    expect(screen.queryByText(/Current Annex E-1:/)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Generate Annex E-1" })).toBeEnabled();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("places E-2 and E-3 below E-1 and loads monthly reports without a daily summary", async () => {
    const client = createOperatorFiscalReportingFixture();
    client.readStatutorySalesBook = vi.fn(client.readStatutorySalesBook);
    client.generateBirSalesSummary = vi.fn(client.generateBirSalesSummary);
    render(<OperatorFiscalReportingPage client={client} siteReferences={["SITE-PITX"]} permissions={allPermissions} />);

    await userEvent.click(await screen.findByRole("tab", { name: "Daily BIR Sales Summary" }));

    const headings = screen.getAllByRole("heading").map((heading) => heading.textContent);
    expect(headings.indexOf("Annex E-1 - BIR Sales Summary Report")).toBeLessThan(headings.indexOf("Annex E-2 - Senior Citizen Sales Book/Report"));
    expect(headings.indexOf("Annex E-2 - Senior Citizen Sales Book/Report")).toBeLessThan(headings.indexOf("Annex E-3 - Persons with Disability Sales Book/Report"));
    expect(screen.getByLabelText("Annex reporting month")).toHaveValue("2026-09");
    expect(screen.queryByRole("tab", { name: "Annex E-2" })).not.toBeInTheDocument();
    expect(screen.queryByRole("tab", { name: "Annex E-3" })).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "View Annex E-2" }));
    await waitFor(() => expect(client.readStatutorySalesBook).toHaveBeenCalledWith("SITE-PITX", "e2", 2026, 9));
    expect(await screen.findByText(/No qualifying transactions exist for this Site POS Server/)).toBeInTheDocument();
    const e2Section = screen.getByRole("heading", { name: "Annex E-2 - Senior Citizen Sales Book/Report" }).closest("section")!;
    expect(e2Section).toHaveTextContent("PRELIMINARY");
    expect(within(e2Section).getByRole("link", { name: "Download XLSX" })).toHaveAttribute("href", expect.stringContaining("/annex-e/e2/statutory-sales-book/download"));
    expect(client.generateBirSalesSummary).not.toHaveBeenCalled();
  });

  it("keeps E-2 and E-3 failures visible without replacing another Annex result", async () => {
    const client = createOperatorFiscalReportingFixture();
    client.readStatutorySalesBook = vi.fn(async (_site, type) => {
      if (type === "e3") throw new Error("The statutory sales-book response did not match the approved contract.");
      return createOperatorFiscalReportingFixture().readStatutorySalesBook("SITE-PITX", type, 2026, 9);
    });
    render(<OperatorFiscalReportingPage client={client} siteReferences={["SITE-PITX"]} permissions={allPermissions} />);

    await userEvent.click(await screen.findByRole("tab", { name: "Daily BIR Sales Summary" }));
    await userEvent.click(screen.getByRole("button", { name: "View Annex E-2" }));
    expect(await screen.findByLabelText("Annex E-2 totals")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "View Annex E-3" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("approved contract");
    expect(screen.getByLabelText("Annex E-2 totals")).toBeInTheDocument();
  });

  it("renders the eleven official columns and POS-authoritative statutory totals", async () => {
    const client = createOperatorFiscalReportingFixture();
    const base = await client.readStatutorySalesBook("SITE-PITX", "e2", 2026, 9);
    const row = {
      fiscalDocumentId: "91000000-0000-4000-8000-000000000010",
      transactionDate: "2026-09-10",
      beneficiaryFullName: "Synthetic Senior",
      statutoryIdNumber: "SC-TEST-001",
      beneficiaryTin: undefined,
      salesInvoiceNumber: "SI-00000000000117",
      salesInclusiveVatMinorUnits: 10000,
      vatAmountMinorUnits: 1071,
      vatExemptSalesMinorUnits: 0,
      discount5MinorUnits: 0,
      discount20MinorUnits: 2000,
      netSalesMinorUnits: 8000,
    };
    client.readStatutorySalesBook = vi.fn(async () => ({
      ...base,
      summary: { ...base.summary, qualifyingTransactionCount: 1, salesInclusiveVatMinorUnits: 10000, vatAmountMinorUnits: 1071, discount20MinorUnits: 2000, netSalesMinorUnits: 8000, isEmpty: false, reportStatus: "FINAL" as const },
      rows: [row],
    }));
    render(<OperatorFiscalReportingPage client={client} siteReferences={["SITE-PITX"]} permissions={allPermissions} />);

    await userEvent.click(await screen.findByRole("tab", { name: "Daily BIR Sales Summary" }));
    await userEvent.click(screen.getByRole("button", { name: "View Annex E-2" }));

    const table = await screen.findByRole("table", { name: "Annex E-2 qualifying Sales Invoices" });
    expect(within(table).getAllByRole("columnheader")).toHaveLength(11);
    expect(table).toHaveTextContent("Synthetic Senior");
    expect(screen.getByLabelText("Annex E-2 totals")).toHaveTextContent("20% Discount₱20.00");
    expect(screen.getByLabelText("Annex E-2 totals")).toHaveTextContent("Net Sales₱80.00");
  });

  it("keeps actual current-workbook service failures visible", async () => {
    const client = createOperatorFiscalReportingFixture();
    client.readCurrentAnnexE1 = vi.fn(async () => {
      throw new Error("The Site POS Server is temporarily unavailable.");
    });
    render(<OperatorFiscalReportingPage client={client} siteReferences={["SITE-PITX"]} permissions={allPermissions} />);

    await userEvent.click(await screen.findByRole("tab", { name: "Daily BIR Sales Summary" }));
    await userEvent.click(screen.getByRole("button", { name: "Generate / View Summary" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("The Site POS Server is temporarily unavailable.");
    expect(screen.queryByRole("button", { name: "Generate Annex E-1" })).not.toBeInTheDocument();
  });

  it("shows the safe POS failure when Annex E-1 accounting prerequisites are missing", async () => {
    const client = createOperatorFiscalReportingFixture();
    client.readCurrentAnnexE1 = vi.fn(async () => undefined);
    client.generateAnnexE1 = vi.fn(async () => {
      throw new Error("Annex E-1 requires complete authoritative accounting facts for the selected month.");
    });
    render(<OperatorFiscalReportingPage client={client} siteReferences={["SITE-PITX"]} permissions={allPermissions} />);

    await userEvent.click(await screen.findByRole("tab", { name: "Daily BIR Sales Summary" }));
    await userEvent.click(screen.getByRole("button", { name: "Generate / View Summary" }));
    await userEvent.click(await screen.findByRole("button", { name: "Generate Annex E-1" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Annex E-1 requires complete authoritative accounting facts for the selected month."
    );
    expect(screen.queryByRole("link", { name: "Download XLSX" })).not.toBeInTheDocument();
  });

  it("presents POS-authoritative Monthly eSales values and downloads without recalculation", async () => {
    const client = createOperatorFiscalReportingFixture();
    client.readMonthlyESales = vi.fn(client.readMonthlyESales);
    render(<OperatorFiscalReportingPage client={client} siteReferences={["SITE-PITX"]} permissions={allPermissions} />);

    await userEvent.click(await screen.findByRole("tab", { name: "Monthly eSales" }));
    expect(screen.getByLabelText("Monthly eSales calendar month")).toHaveValue("2026-09");
    await userEvent.click(screen.getByRole("button", { name: "View Monthly eSales" }));

    await waitFor(() => expect(client.readMonthlyESales).toHaveBeenCalledWith("SITE-PITX", 2026, 9));
    const values = screen.getByLabelText("Monthly eSales values");
    expect(values).toHaveTextContent("MIN-TEST-PITX-L3-001");
    expect(values).toHaveTextContent("PHP 112.00");
    expect(values).toHaveTextContent("SI-00000001");
    expect(screen.getByLabelText("Monthly eSales filing metadata")).not.toHaveTextContent("2026-10-10");
    expect(screen.queryByText("Submission Due Date")).not.toBeInTheDocument();
    expect(screen.queryByText("Submission Readiness")).not.toBeInTheDocument();
    expect(screen.queryByText("TECHNICAL UAT - NOT FOR BIR SUBMISSION")).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Download JSON" })).toHaveAttribute("href", expect.stringContaining("format=json"));
    expect(screen.getByRole("link", { name: "Download CSV" })).toHaveAttribute("href", expect.stringContaining("format=csv"));
    expect(screen.getByRole("link", { name: "Download PDF" })).toHaveAttribute("href", expect.stringContaining("format=pdf"));
  });

  it("shows incomplete Monthly eSales source dates as a neutral non-final state", async () => {
    const client = createOperatorFiscalReportingFixture();
    client.readMonthlyESales = vi.fn(async () => ({
      status: "INCOMPLETE" as const,
      code: "monthly_esales_sources_incomplete",
      message: "Monthly eSales cannot be finalized because committed BIR Sales Summaries are missing for: 2026-09-04, 2026-09-05."
    }));
    render(<OperatorFiscalReportingPage client={client} siteReferences={["SITE-PITX"]} permissions={allPermissions} />);

    await userEvent.click(await screen.findByRole("tab", { name: "Monthly eSales" }));
    await userEvent.click(screen.getByRole("button", { name: "View Monthly eSales" }));

    expect(await screen.findByRole("status")).toHaveTextContent("2026-09-04, 2026-09-05");
    expect(screen.queryByLabelText("Monthly eSales values")).not.toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Download CSV" })).not.toBeInTheDocument();
  });

  it("clears Monthly eSales results when the selected month changes", async () => {
    const client = createOperatorFiscalReportingFixture();
    render(<OperatorFiscalReportingPage client={client} siteReferences={["SITE-PITX"]} permissions={allPermissions} />);

    await userEvent.click(await screen.findByRole("tab", { name: "Monthly eSales" }));
    await userEvent.click(screen.getByRole("button", { name: "View Monthly eSales" }));
    expect(await screen.findByLabelText("Monthly eSales values")).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Monthly eSales calendar month"), { target: { value: "2026-10" } });
    expect(screen.queryByLabelText("Monthly eSales values")).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Download JSON" })).not.toBeInTheDocument();
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
