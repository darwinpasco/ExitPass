import { describe, expect, it, vi } from "vitest";
import { createOperatorFiscalReportingClient } from "./fiscalReporting";
import numericWireResponse from "./test/fixtures/statutorySalesBookNumericWireResponse.json";

describe("Operator fiscal-reporting HTTP client", () => {
  it("submits only the selected committed period and Z reference through the Site-scoped Central PMS route", async () => {
    const fetchImpl = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>(async () => new Response(JSON.stringify({ summary: summaryPayload() }), {
      status: 201,
      headers: { "Content-Type": "application/json" }
    }));
    const client = createOperatorFiscalReportingClient(fetchImpl as typeof fetch, () => "csrf-token");

    const summary = await client.generateBirSalesSummary("SITE-PITX", {
      reportReference: "Z-20260910-001",
      reportKind: "Z_READING",
      fiscalReportingPeriodId: "period-1",
      businessDayDate: "2026-09-10",
      periodStartAt: "2026-09-09T16:00:00Z",
      periodEndAt: "2026-09-10T16:00:00Z",
      generatedAt: "2026-09-10T08:15:00Z",
      transactionCount: 1,
      amounts: { grossSalesAmountMinorUnits: 5000, netSalesAmountMinorUnits: 5000, vatableSalesAmountMinorUnits: 4464, vatAmountMinorUnits: 536, vatExemptSalesAmountMinorUnits: 0, zeroRatedSalesAmountMinorUnits: 0, discountAmountMinorUnits: 0, voidAmountMinorUnits: 0, adjustmentAmountMinorUnits: 0 },
      currencyCode: "PHP",
      periodSequence: 1,
      reportStatus: "COMMITTED",
      tenders: [],
      discounts: []
    });

    expect(summary.amounts.grossSalesAmountMinorUnits).toBe(5000);
    const [url, init] = fetchImpl.mock.calls[0];
    expect(url).toBe("/v1/ops/operator-console/fiscal-reporting/sites/SITE-PITX/bir-sales-summaries");
    expect(init?.headers).toMatchObject({ "X-CSRF-Token": "csrf-token" });
    expect(JSON.parse(String(init?.body))).toMatchObject({
      fiscalReportingPeriodId: "period-1",
      governingZReadingReference: "Z-20260910-001"
    });
    expect(String(init?.body)).not.toContain("sitePosServerId");
    expect(String(init?.body)).not.toContain("fiscalIdentityId");
  });

  it("builds authoritative summary and Annex E-1 proxy download URLs", () => {
    const client = createOperatorFiscalReportingClient(vi.fn() as unknown as typeof fetch, () => "csrf-token");

    expect(client.birSalesSummaryUrl("SITE-PITX", "summary-1", "json")).toBe(
      "/v1/ops/operator-console/fiscal-reporting/sites/SITE-PITX/bir-sales-summaries/summary-1/download?format=json"
    );
    expect(client.birSalesSummaryUrl("SITE-PITX", "summary-1", "csv")).toContain("format=csv");
    expect(client.birSalesSummaryUrl("SITE-PITX", "summary-1", "pdf")).toContain("format=pdf");
    expect(client.annexE1Url("SITE-PITX", "workbook-1")).toBe(
      "/v1/ops/operator-console/fiscal-reporting/sites/SITE-PITX/annex-e/e1/workbooks/workbook-1/download?format=xlsx"
    );
    expect(client.annexE1Url("SITE-PITX", "workbook-1", "pdf")).toContain("format=pdf");
  });

  it("reads the authoritative numeric Annex E-2 representation and builds terminal-free downloads", async () => {
    const fetchImpl = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>(async () =>
      new Response(JSON.stringify(numericWireResponse.e2), {
        status: 200,
        headers: { "Content-Type": "application/json" }
      }));
    const client = createOperatorFiscalReportingClient(fetchImpl as typeof fetch);

    const report = await client.readStatutorySalesBook("SITE-PITX", "e2", 2026, 10);

    expect(report.summary.type).toBe("AnnexE2");
    expect(report.summary.qualifyingTransactionCount).toBe(0);
    expect(fetchImpl.mock.calls[0][0]).toBe(
      "/v1/ops/operator-console/fiscal-reporting/sites/SITE-PITX/annex-e/e2/statutory-sales-book?calendarYear=2026&calendarMonth=10"
    );
    expect(String(fetchImpl.mock.calls[0][0])).not.toContain("channelTerminalId");
    expect(client.statutorySalesBookUrl("SITE-PITX", "e3", 2026, 10, "xlsx")).toBe(
      "/v1/ops/operator-console/fiscal-reporting/sites/SITE-PITX/annex-e/e3/statutory-sales-book/download?calendarYear=2026&calendarMonth=10&format=xlsx"
    );
  });

  it("reads the authoritative numeric Annex E-3 representation", async () => {
    const fetchImpl = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>(async () =>
      new Response(JSON.stringify(numericWireResponse.e3), {
        status: 200,
        headers: { "Content-Type": "application/json" }
      }));
    const client = createOperatorFiscalReportingClient(fetchImpl as typeof fetch);

    const report = await client.readStatutorySalesBook("SITE-PITX", "e3", 2026, 10);

    expect(report.summary.type).toBe("AnnexE3");
    expect(report.summary.qualifyingTransactionCount).toBe(0);
  });

  it.each([
    ["e2", "AnnexE2"],
    ["e3", "AnnexE3"]
  ] as const)("retains the approved named %s compatibility representation", async (requestedType, responseType) => {
    const fetchImpl = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>(async () =>
      new Response(JSON.stringify(statutorySalesBookPayload(responseType)), {
        status: 200,
        headers: { "Content-Type": "application/json" }
      }));
    const client = createOperatorFiscalReportingClient(fetchImpl as typeof fetch);

    const report = await client.readStatutorySalesBook("SITE-PITX", requestedType, 2026, 10);

    expect(report.summary.type).toBe(responseType);
  });

  it("rejects a valid wire enum for the wrong requested Annex", async () => {
    const fetchImpl = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>(async () =>
      new Response(JSON.stringify(statutorySalesBookPayload(1)), {
        status: 200,
        headers: { "Content-Type": "application/json" }
      }));
    const client = createOperatorFiscalReportingClient(fetchImpl as typeof fetch);

    await expect(client.readStatutorySalesBook("SITE-PITX", "e2", 2026, 10)).rejects.toThrow();
  });

  it("rejects an unknown statutory sales-book enum value", async () => {
    const fetchImpl = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>(async () =>
      new Response(JSON.stringify(statutorySalesBookPayload(2)), {
        status: 200,
        headers: { "Content-Type": "application/json" }
      }));
    const client = createOperatorFiscalReportingClient(fetchImpl as typeof fetch);

    await expect(client.readStatutorySalesBook("SITE-PITX", "e2", 2026, 10)).rejects.toThrow();
  });

  it("loads a valid zero-row PRELIMINARY report from the numeric wire contract", async () => {
    const payload = statutorySalesBookPayload(0);
    payload.report.summary.qualifyingTransactionCount = 0;
    payload.report.summary.salesInclusiveVatMinorUnits = 0;
    payload.report.summary.vatAmountMinorUnits = 0;
    payload.report.summary.vatExemptSalesMinorUnits = 0;
    payload.report.summary.discount5MinorUnits = 0;
    payload.report.summary.discount20MinorUnits = 0;
    payload.report.summary.netSalesMinorUnits = 0;
    payload.report.summary.reportStatus = "PRELIMINARY";
    payload.report.summary.isEmpty = true;
    payload.report.rows = [];
    const fetchImpl = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>(async () =>
      new Response(JSON.stringify(payload), { status: 200, headers: { "Content-Type": "application/json" } }));
    const client = createOperatorFiscalReportingClient(fetchImpl as typeof fetch);

    const report = await client.readStatutorySalesBook("SITE-PITX", "e2", 2026, 10);

    expect(report.summary).toMatchObject({ type: "AnnexE2", reportStatus: "PRELIMINARY", isEmpty: true });
    expect(report.rows).toEqual([]);
  });

  it("rejects malformed statutory sales-book responses", async () => {
    const payload = statutorySalesBookPayload("AnnexE3");
    payload.report.summary.qualifyingTransactionCount = 2;
    const fetchImpl = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>(async () =>
      new Response(JSON.stringify(payload), { status: 200, headers: { "Content-Type": "application/json" } }));
    const client = createOperatorFiscalReportingClient(fetchImpl as typeof fetch);

    await expect(client.readStatutorySalesBook("SITE-PITX", "e3", 2026, 10)).rejects.toThrow();
  });

  it("rejects statutory sales-book totals that do not reconcile to report rows", async () => {
    const payload = statutorySalesBookPayload(0);
    payload.report.summary.netSalesMinorUnits += 1;
    const fetchImpl = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>(async () =>
      new Response(JSON.stringify(payload), { status: 200, headers: { "Content-Type": "application/json" } }));
    const client = createOperatorFiscalReportingClient(fetchImpl as typeof fetch);

    await expect(client.readStatutorySalesBook("SITE-PITX", "e2", 2026, 10)).rejects.toThrow();
  });

  it.each([
    ["missing fiscal identity", (payload: ReturnType<typeof statutorySalesBookPayload>) => { payload.report.summary.fiscalIdentityId = ""; }],
    ["incorrect currency", (payload: ReturnType<typeof statutorySalesBookPayload>) => { payload.report.summary.currencyCode = "USD"; }],
    ["unrecognized report status", (payload: ReturnType<typeof statutorySalesBookPayload>) => { payload.report.summary.reportStatus = "UNKNOWN"; }],
    ["inconsistent empty-report flag", (payload: ReturnType<typeof statutorySalesBookPayload>) => { payload.report.summary.isEmpty = true; }]
  ])("rejects a statutory sales book with %s", async (_case, mutate) => {
    const payload = statutorySalesBookPayload(0);
    mutate(payload);
    const fetchImpl = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>(async () =>
      new Response(JSON.stringify(payload), { status: 200, headers: { "Content-Type": "application/json" } }));
    const client = createOperatorFiscalReportingClient(fetchImpl as typeof fetch);

    await expect(client.readStatutorySalesBook("SITE-PITX", "e2", 2026, 10)).rejects.toThrow();
  });

  it("rejects a statutory sales book for a different reporting month", async () => {
    const payload = statutorySalesBookPayload("AnnexE2");
    payload.report.summary.calendarMonth = 9;
    const fetchImpl = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>(async () =>
      new Response(JSON.stringify(payload), { status: 200, headers: { "Content-Type": "application/json" } }));
    const client = createOperatorFiscalReportingClient(fetchImpl as typeof fetch);

    await expect(client.readStatutorySalesBook("SITE-PITX", "e2", 2026, 10)).rejects.toThrow();
  });

  it("builds explicit PDF and JSON X/Z Reading download URLs", () => {
    const client = createOperatorFiscalReportingClient(vi.fn() as unknown as typeof fetch);

    expect(client.readingUrl("SITE-PITX", "x", "X-20261017-001", "pdf")).toBe(
      "/v1/ops/operator-console/fiscal-reporting/sites/SITE-PITX/x-readings/X-20261017-001/download?format=pdf"
    );
    expect(client.readingUrl("SITE-PITX", "x", "X-20261017-001", "json")).toContain("format=json");
    expect(client.readingUrl("SITE-PITX", "z", "Z-20261016-001", "pdf")).toContain("format=pdf");
    expect(client.readingUrl("SITE-PITX", "z", "Z-20261016-001", "json")).toContain("format=json");
  });

  it("reads the current Annex E-1 by Site and month and treats 404 as no current workbook", async () => {
    const fetchImpl = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>(async () => new Response(JSON.stringify({
      succeeded: true,
      workbook: workbookPayload()
    }), { status: 200, headers: { "Content-Type": "application/json" } }));
    const client = createOperatorFiscalReportingClient(fetchImpl as typeof fetch, () => "csrf-token");

    const current = await client.readCurrentAnnexE1("SITE-PITX", 2026, 10);
    expect(current?.workbookId).toBe("613af721-3b65-4bc4-9ce3-d2a032c26ab3");
    expect(current?.revision).toBe(2);
    expect(fetchImpl.mock.calls[0][0]).toBe(
      "/v1/ops/operator-console/fiscal-reporting/sites/SITE-PITX/annex-e/e1/workbooks/current?calendarYear=2026&calendarMonth=10"
    );
    expect(fetchImpl.mock.calls[0][1]?.method).toBeUndefined();

    fetchImpl.mockResolvedValueOnce(new Response(JSON.stringify({
      succeeded: false,
      code: "annex_e1_not_found"
    }), { status: 404, headers: { "Content-Type": "application/json" } }));
    await expect(client.readCurrentAnnexE1("SITE-PITX", 2026, 11)).resolves.toBeUndefined();
  });

  it("reads Monthly eSales through the Site-scoped route without browser-supplied fiscal authority", async () => {
    const fetchImpl = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>(async () => new Response(JSON.stringify({
      succeeded: true,
      code: "monthly_esales_read",
      report: monthlyESalesPayload()
    }), { status: 200, headers: { "Content-Type": "application/json" } }));
    const client = createOperatorFiscalReportingClient(fetchImpl as typeof fetch, () => "csrf-token");

    const result = await client.readMonthlyESales("SITE-PITX", 2026, 9);

    expect(result.status).toBe("FINAL");
    expect(result.report?.rows[0].monthlySalesAmountMinorUnits).toBe(195000);
    const [url, init] = fetchImpl.mock.calls[0];
    expect(url).toBe("/v1/ops/operator-console/fiscal-reporting/sites/SITE-PITX/monthly-esales?calendarYear=2026&calendarMonth=9");
    expect(init?.method).toBeUndefined();
    expect(String(url)).not.toContain("sitePosServerId");
    expect(String(url)).not.toContain("fiscalIdentityId");
  });

  it("maps Monthly eSales source incompleteness to a safe non-final result", async () => {
    const fetchImpl = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>(async () => new Response(JSON.stringify({
      succeeded: false,
      code: "monthly_esales_sources_incomplete",
      message: "Monthly eSales cannot be finalized because committed BIR Sales Summaries are missing for: 2026-09-04, 2026-09-05."
    }), { status: 409, headers: { "Content-Type": "application/json" } }));
    const client = createOperatorFiscalReportingClient(fetchImpl as typeof fetch);

    await expect(client.readMonthlyESales("SITE-PITX", 2026, 9)).resolves.toEqual({
      status: "INCOMPLETE",
      code: "monthly_esales_sources_incomplete",
      message: "Monthly eSales cannot be finalized because committed BIR Sales Summaries are missing for: 2026-09-04, 2026-09-05."
    });
  });

  it("builds deterministic Monthly eSales PDF, CSV, and JSON proxy download URLs", () => {
    const client = createOperatorFiscalReportingClient(vi.fn() as unknown as typeof fetch);

    expect(client.monthlyESalesUrl("SITE-PITX", 2026, 9, "json")).toBe(
      "/v1/ops/operator-console/fiscal-reporting/sites/SITE-PITX/monthly-esales/download?calendarYear=2026&calendarMonth=9&format=json"
    );
    expect(client.monthlyESalesUrl("SITE-PITX", 2026, 9, "csv")).toContain("format=csv");
    expect(client.monthlyESalesUrl("SITE-PITX", 2026, 9, "pdf")).toContain("format=pdf");
  });
  it("maps a plain-text upstream exception response to a controlled operation error", async () => {
    const fetchImpl = vi.fn(async () => new Response(
      "ExitPass.PosServer.Runtime.InternalException: private stack trace",
      { status: 500, headers: { "Content-Type": "text/plain" } }
    ));
    const client = createOperatorFiscalReportingClient(fetchImpl as typeof fetch, () => "csrf-token");

    await expect(client.readZ("SITE-PITX")).rejects.toThrow("The fiscal reporting operation failed safely.");
    await expect(client.closeBusinessDate("SITE-PITX", {
      fiscalReportingPeriodId: "period-1",
      fiscalBusinessDate: "2026-09-20",
      periodStart: "2026-09-19T23:00:00Z",
      periodEnd: "2026-09-20T23:00:00Z",
      status: "CLOSEABLE",
      transactionCount: 1,
      expectedStateVersion: 4
    })).rejects.toThrow("The fiscal reporting operation failed safely.");
    expect(fetchImpl).toHaveBeenCalledTimes(2);
  });

  it("preserves the additive BIR completion state returned by a successful Z close", async () => {
    const fetchImpl = vi.fn(async () => new Response(JSON.stringify({
      succeeded: true,
      code: "fiscal_z_reading_closed",
      birSalesSummaryCompletion: {
        status: "PENDING",
        code: "bir_sales_summary_persistence_failed",
        retryable: true
      }
    }), { status: 201, headers: { "Content-Type": "application/json" } }));
    const client = createOperatorFiscalReportingClient(fetchImpl as typeof fetch, () => "csrf-token");

    const result = await client.closeBusinessDate("SITE-PITX", {
      fiscalReportingPeriodId: "period-1",
      fiscalBusinessDate: "2026-09-20",
      periodStart: "2026-09-19T23:00:00Z",
      periodEnd: "2026-09-20T23:00:00Z",
      status: "CLOSEABLE",
      transactionCount: 1,
      expectedStateVersion: 4
    });

    expect(result.birSalesSummaryCompletion).toEqual({
      status: "PENDING",
      summaryId: undefined,
      code: "bir_sales_summary_persistence_failed",
      retryable: true,
      message: undefined
    });
  });

  it("maps malformed JSON without leaking a JSON parser exception", async () => {
    const client = createOperatorFiscalReportingClient(
      vi.fn(async () => new Response("ExitPass.P", { status: 502, headers: { "Content-Type": "application/json" } })) as typeof fetch
    );

    await expect(client.readX("SITE-PITX")).rejects.toThrow("The fiscal reporting operation failed safely.");
    await expect(client.readX("SITE-PITX")).rejects.not.toThrow(/Unexpected token|ExitPass\.P/i);
  });

  it("uses the controlled JSON message returned by Central PMS", async () => {
    const client = createOperatorFiscalReportingClient(
      vi.fn(async () => new Response(JSON.stringify({
        code: "fiscal_reporting_upstream_operation_failed",
        message: "The authoritative fiscal reporting operation could not be completed."
      }), { status: 502, headers: { "Content-Type": "application/json" } })) as typeof fetch
    );

    await expect(client.readZ("SITE-PITX")).rejects.toThrow(
      "The authoritative fiscal reporting operation could not be completed."
    );
  });
});

function summaryPayload() {
  return {
    birSalesSummaryReportId: "summary-1",
    governingZReadingReference: "Z-20260910-001",
    fiscalReportingPeriodId: "period-1",
    businessDayDate: "2026-09-10",
    beginningSalesInvoiceReference: "SI-00000001",
    endingSalesInvoiceReference: "SI-00000001",
    transactionCount: 1,
    amounts: {
      grossSalesAmountMinorUnits: 5000,
      netSalesAmountMinorUnits: 5000,
      vatableSalesAmountMinorUnits: 4464,
      vatAmountMinorUnits: 536,
      vatExemptSalesAmountMinorUnits: 0,
      zeroRatedSalesAmountMinorUnits: 0,
      discountAmountMinorUnits: 0,
      seniorCitizenDiscountAmountMinorUnits: 0,
      pwdDiscountAmountMinorUnits: 0,
      otherStatutoryDiscountAmountMinorUnits: 0,
      vatExemptionAmountMinorUnits: 0,
      voidAmountMinorUnits: 0,
      refundAmountMinorUnits: 0,
      returnAmountMinorUnits: 0,
      adjustmentAmountMinorUnits: 0
    },
    counterSnapshot: {
      previousResetCounterValue: 0,
      resultingResetCounterValue: 0,
      previousZCounterValue: 1,
      resultingZCounterValue: 2,
      previousGrandTotalAmountMinorUnits: 10000,
      resultingGrandTotalAmountMinorUnits: 15000
    },
    currencyCode: "PHP",
    generatedAt: "2026-09-10T08:16:00Z",
    reportStatus: "COMMITTED",
    supportReference: "BIRSS-TEST"
  };
}

function workbookPayload() {
  return {
    workbookId: "613af721-3b65-4bc4-9ce3-d2a032c26ab3",
    annexEReference: "AE1-202610-2AA202E51C8398A1-R002",
    revision: 2,
    calendarYear: 2026,
    calendarMonth: 10,
    currencyCode: "PHP",
    fileName: "annex-e1-2026-10.xlsx",
    generatedAt: "2026-10-16T08:00:00Z",
    status: "COMMITTED"
  };
}

function monthlyESalesPayload() {
  return {
    sitePosServerId: "3a138565-1b88-55f8-c83d-5380db6edccc",
    fiscalIdentityId: "ad02beb5-b8cd-4545-9ff2-5586782c686a",
    calendarYear: 2026,
    calendarMonth: 9,
    currencyCode: "PHP",
    taxpayerTin: "999-999-999-000",
    submissionDueDate: "2026-10-10",
    submissionDueDateBasis: "ODD_TIN",
    submissionReadiness: "TECHNICAL_UAT",
    submissionReadinessMessage: "UAT fiscal identifier - not for BIR submission.",
    coverageStatus: "COMPLETE_MONTH",
    coverageMessage: "Complete calendar month from 2026-09-04 through 2026-09-30.",
    firstCoveredBusinessDate: "2026-09-04",
    lastCoveredBusinessDate: "2026-09-30",
    coverageAsOf: "2026-10-01T00:00:00Z",
    rows: [{
      machineIdentificationNumber: "MIN-TEST-PITX-L3-001",
      calendarMonth: 9,
      calendarYear: 2026,
      transactionCount: 39,
      monthlySalesAmountMinorUnits: 195000,
      monthlySalesDisplay: "1950.00",
      endingSalesInvoiceReference: "SI-00000090",
      vatableSalesAmountMinorUnits: 174107,
      vatAmountMinorUnits: 20893,
      vatExemptSalesAmountMinorUnits: 0,
      zeroRatedSalesAmountMinorUnits: 0,
      sourcePeriodCount: 18,
      firstBusinessDate: "2026-09-04",
      lastBusinessDate: "2026-09-30"
    }]
  };
}

function statutorySalesBookPayload(type: unknown) {
  return {
    succeeded: true,
    code: "statutory_sales_book_read",
    report: {
      header: {
        serialNumber: "EP-PITX-L3-POS-0001",
        machineIdentificationNumber: "MIN-PITX-L3-01",
        posServerCode: "PITX-L3-POS-01"
      },
      summary: {
        type,
        sitePosServerId: "3a138565-1b88-55f8-c83d-5380db6edccc",
        fiscalIdentityId: "ad02beb5-b8cd-4545-9ff2-5586782c686a",
        currencyCode: "PHP",
        calendarYear: 2026,
        calendarMonth: 10,
        posServerCode: "PITX-L3-POS-01",
        qualifyingTransactionCount: 1,
        salesInclusiveVatMinorUnits: 10000,
        vatAmountMinorUnits: 1071,
        vatExemptSalesMinorUnits: 0,
        discount5MinorUnits: 0,
        discount20MinorUnits: 2000,
        netSalesMinorUnits: 8000,
        reportAsOf: "2026-10-17T08:00:00Z",
        warnings: [],
        reportStatus: "FINAL",
        isEmpty: false,
        scopedFiscalPeriodCount: 31,
        closedFiscalPeriodCount: 31,
        committedZPeriodCount: 31
      },
      rows: [{
        fiscalDocumentId: "91000000-0000-4000-8000-000000000010",
        transactionDate: "2026-10-17",
        beneficiaryFullName: "Synthetic Beneficiary",
        statutoryIdNumber: "TEST-001",
        beneficiaryTin: null,
        salesInvoiceNumber: "SI-00000000000117",
        salesInclusiveVatMinorUnits: 10000,
        vatAmountMinorUnits: 1071,
        vatExemptSalesMinorUnits: 0,
        discount5MinorUnits: 0,
        discount20MinorUnits: 2000,
        netSalesMinorUnits: 8000
      }]
    }
  };
}
