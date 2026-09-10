import type { FiscalExceptionReport } from "../fiscalExceptionReporting";

export function fiscalReport(overrides: Partial<FiscalExceptionReport> = {}): FiscalExceptionReport {
  return {
    contractVersion: "management-platform-fiscal-exception-reporting:v1",
    reportId: "fiscal-exception-summary",
    requestedScope: { scopeType: "SITE", scopeReference: "71000000-0000-0000-0000-000000000101", displayName: "Development Site Alpha" },
    effectiveScope: { scopeType: "SITE", scopeReference: "71000000-0000-0000-0000-000000000101", displayName: "Development Site Alpha" },
    periodStart: "2026-08-01T00:00:00Z",
    periodEnd: "2026-08-08T00:00:00Z",
    timeBasis: "FISCAL_ISSUANCE_REFERENCE_FIRST_RECORDED_AT",
    generatedAt: "2026-08-08T00:01:00Z",
    dataAsOf: "2026-08-07T23:59:00Z",
    availability: "PARTIAL",
    freshness: "CURRENT",
    correlationId: "74000000-0000-0000-0000-000000000101",
    sourceCoverage: [{
      sourceId: "CENTRAL_PMS_FISCAL_ISSUANCE_REFERENCES",
      availability: "PARTIAL",
      dataAsOf: "2026-08-07T23:59:00Z",
      description: "Central PMS fiscal issuance coordination records.",
      limitations: ["No live POS Server read."]
    }],
    lifecycleSummaries: [
      { lifecycleState: "ISSUED", count: 7 },
      { lifecycleState: "PENDING", count: 2 },
      { lifecycleState: "FAILED", count: 1 }
    ],
    exceptionSummaries: [{
      categoryId: "SALES_INVOICE_ISSUANCE_FAILED",
      availability: "AVAILABLE",
      count: 1,
      affectedExpectedAmounts: [{ currencyCode: "PHP", amount: 125.5 }],
      definition: "Latest coordination state records an issuance failure.",
      terminal: false,
      canResolveLater: true,
      limitations: ["A later authoritative retry may resolve the failure."]
    }],
    currencySummaries: [{
      currencyCode: "PHP",
      issuanceExpectationCount: 10,
      expectedIssuanceAmount: 1250.75,
      issuedCount: 7,
      failedCount: 1
    }],
    warnings: ["This view contains partial coordination evidence."],
    limitations: ["Printing and delivery are not proven."],
    unavailableFacts: ["BIR compliance certification"],
    sourceAuthority: "CENTRAL_PMS_FISCAL_ISSUANCE_REFERENCES",
    ...overrides
  };
}
