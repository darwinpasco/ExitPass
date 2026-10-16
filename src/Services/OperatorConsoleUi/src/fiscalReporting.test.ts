import { describe, expect, it, vi } from "vitest";
import { createOperatorFiscalReportingClient } from "./fiscalReporting";

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
    expect(client.annexE1Url("SITE-PITX", "workbook-1")).toBe(
      "/v1/ops/operator-console/fiscal-reporting/sites/SITE-PITX/annex-e/e1/workbooks/workbook-1/download"
    );
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
