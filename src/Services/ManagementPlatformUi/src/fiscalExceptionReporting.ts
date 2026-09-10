import { createUiError } from "./apiClient";
import type { CentralPmsApiClient } from "./types";

export const fiscalExceptionReportingRoute = "/management-platform/reports/fiscal-exceptions";
export const fiscalExceptionReportingApiRoute = "/v1/management-platform/dashboard/fiscal-exception-summary";
export const fiscalExceptionReportPermission = "sales-invoice-report.view";
export const fiscalExceptionReportContractVersion = "management-platform-fiscal-exception-reporting:v1";

export type FiscalReportScopeType = "SITE" | "SITE_GROUP";

export interface FiscalReportScope {
  scopeType: FiscalReportScopeType;
  scopeReference: string;
  displayName: string;
}

export interface FiscalExceptionReportQuery {
  scopeType: FiscalReportScopeType;
  scopeReference: string;
  periodStart: string;
  periodEnd: string;
}

export interface FiscalExceptionReportClient {
  getSummary(query: FiscalExceptionReportQuery, signal?: AbortSignal): Promise<FiscalExceptionReport>;
}

export interface FiscalExceptionReport {
  contractVersion: string;
  reportId: string;
  requestedScope: FiscalReportScope;
  effectiveScope: FiscalReportScope;
  periodStart: string;
  periodEnd: string;
  timeBasis: string;
  generatedAt: string;
  dataAsOf: string | null;
  availability: string;
  freshness: string;
  correlationId: string;
  sourceCoverage: FiscalSourceCoverage[];
  lifecycleSummaries: FiscalLifecycleSummary[];
  exceptionSummaries: FiscalExceptionSummary[];
  currencySummaries: FiscalCurrencySummary[];
  warnings: string[];
  limitations: string[];
  unavailableFacts: string[];
  sourceAuthority: string;
}

export interface FiscalSourceCoverage {
  sourceId: string;
  availability: string;
  dataAsOf: string | null;
  description: string;
  limitations: string[];
}

export interface FiscalLifecycleSummary {
  lifecycleState: string;
  count: number;
}

export interface FiscalExceptionSummary {
  categoryId: string;
  availability: string;
  count: number;
  affectedExpectedAmounts: FiscalAmountSummary[];
  definition: string;
  terminal: boolean;
  canResolveLater: boolean;
  limitations: string[];
}

export interface FiscalCurrencySummary {
  currencyCode: string;
  issuanceExpectationCount: number;
  expectedIssuanceAmount: number;
  issuedCount: number;
  failedCount: number;
}

export interface FiscalAmountSummary {
  currencyCode: string;
  amount: number;
}

export type FiscalExceptionReportScenarioName = "activity" | "no-activity" | "disabled" | "unavailable";

export interface FiscalExceptionReportScenario {
  name: FiscalExceptionReportScenarioName;
  client: FiscalExceptionReportClient;
}

/**
 * Creates the browser-safe, read-only fiscal exception client.
 *
 * BRD: Management Dashboard and Reporting BRD v1.0 Sections 19 (MDR-FR-016,
 * MDR-FR-023, MDR-FR-024, MDR-FR-042) and 24.
 * SDD: Management Dashboard and Reporting SDD v1.0 Sections 10-14.
 * Authority boundary: Central PMS supplies persisted coordination evidence;
 * the browser never calls POS Server or performs fiscal mutation/recovery.
 */
export function createFiscalExceptionReportingClient(apiClient: CentralPmsApiClient): FiscalExceptionReportClient {
  return {
    async getSummary(query, signal) {
      assertValidQuery(query);
      const search = new URLSearchParams({
        scopeType: query.scopeType,
        scopeReference: query.scopeReference,
        periodStart: query.periodStart,
        periodEnd: query.periodEnd
      });
      const response = await apiClient.request<unknown>(`${fiscalExceptionReportingApiRoute}?${search.toString()}`, {
        method: "GET",
        signal
      });
      return parseFiscalExceptionReport(response);
    }
  };
}

/** Parses only the aggregate fields approved by the fiscal reporting contract. */
export function parseFiscalExceptionReport(value: unknown): FiscalExceptionReport {
  const report = object(value);
  if (string(report.contractVersion) !== fiscalExceptionReportContractVersion ||
      string(report.reportId) !== "fiscal-exception-summary" ||
      string(report.timeBasis) !== "FISCAL_ISSUANCE_REFERENCE_FIRST_RECORDED_AT") {
    malformed();
  }

  const parsed: FiscalExceptionReport = {
    contractVersion: requiredString(report.contractVersion),
    reportId: requiredString(report.reportId),
    requestedScope: parseScope(report.requestedScope),
    effectiveScope: parseScope(report.effectiveScope),
    periodStart: isoTimestamp(report.periodStart),
    periodEnd: isoTimestamp(report.periodEnd),
    timeBasis: requiredString(report.timeBasis),
    generatedAt: isoTimestamp(report.generatedAt),
    dataAsOf: nullableTimestamp(report.dataAsOf),
    availability: requiredString(report.availability),
    freshness: requiredString(report.freshness),
    correlationId: requiredString(report.correlationId),
    sourceCoverage: array(report.sourceCoverage).map(parseSourceCoverage),
    lifecycleSummaries: array(report.lifecycleSummaries).map(parseLifecycleSummary),
    exceptionSummaries: array(report.exceptionSummaries).map(parseExceptionSummary),
    currencySummaries: array(report.currencySummaries).map(parseCurrencySummary),
    warnings: stringArray(report.warnings),
    limitations: stringArray(report.limitations),
    unavailableFacts: stringArray(report.unavailableFacts),
    sourceAuthority: requiredString(report.sourceAuthority)
  };

  if (new Date(parsed.periodStart).getTime() >= new Date(parsed.periodEnd).getTime() ||
      !["PARTIAL", "NO_ACTIVITY"].includes(parsed.availability) ||
      !["CURRENT", "NOT_APPLICABLE"].includes(parsed.freshness) ||
      parsed.sourceAuthority !== "CENTRAL_PMS_FISCAL_ISSUANCE_REFERENCES") {
    malformed();
  }

  return parsed;
}

/** Resolves controlled in-memory report data only for explicit development builds. */
export function resolveFiscalExceptionReportScenario(isDevelopment: boolean, search: string): FiscalExceptionReportScenario | undefined {
  if (!isDevelopment) {
    return undefined;
  }
  const name = normalizeScenarioName(new URLSearchParams(search).get("mpFiscalScenario"));
  return name ? { name, client: developmentScenarioClient(name) } : undefined;
}

function developmentScenarioClient(name: FiscalExceptionReportScenarioName): FiscalExceptionReportClient {
  return {
    async getSummary(query, signal) {
      await developmentDelay(signal);
      if (name === "disabled") {
        throw createUiError("feature-disabled", "MANAGEMENT_FISCAL_EXCEPTION_REPORTING_DISABLED", "Fiscal exception reporting is disabled.", "dev-fiscal-disabled", 503);
      }
      if (name === "unavailable") {
        throw createUiError("integration-unavailable", "FISCAL_EXCEPTION_SOURCE_UNAVAILABLE", "Fiscal coordination evidence is temporarily unavailable.", "dev-fiscal-unavailable", 503, true);
      }
      return developmentReport(query, name === "no-activity");
    }
  };
}

function developmentReport(query: FiscalExceptionReportQuery, noActivity: boolean): FiscalExceptionReport {
  const displayName = query.scopeType === "SITE_GROUP" ? "Development Site Group" : "Development Site Alpha";
  return {
    contractVersion: fiscalExceptionReportContractVersion,
    reportId: "fiscal-exception-summary",
    requestedScope: { scopeType: query.scopeType, scopeReference: query.scopeReference, displayName },
    effectiveScope: { scopeType: query.scopeType, scopeReference: query.scopeReference, displayName },
    periodStart: query.periodStart,
    periodEnd: query.periodEnd,
    timeBasis: "FISCAL_ISSUANCE_REFERENCE_FIRST_RECORDED_AT",
    generatedAt: "2026-08-23T08:15:00Z",
    dataAsOf: noActivity ? null : "2026-08-23T08:14:00Z",
    availability: noActivity ? "NO_ACTIVITY" : "PARTIAL",
    freshness: noActivity ? "NOT_APPLICABLE" : "CURRENT",
    correlationId: "74000000-0000-0000-0000-000000000401",
    sourceCoverage: noActivity ? [] : [{
      sourceId: "CENTRAL_PMS_FISCAL_ISSUANCE_REFERENCES",
      availability: "PARTIAL",
      dataAsOf: "2026-08-23T08:14:00Z",
      description: "Development-only Central PMS coordination aggregate.",
      limitations: ["No live POS Server status, printing, or delivery evidence."]
    }],
    lifecycleSummaries: noActivity ? [] : [
      { lifecycleState: "ISSUED", count: 12 },
      { lifecycleState: "PENDING", count: 3 },
      { lifecycleState: "FAILED", count: 2 },
      { lifecycleState: "OUTCOME_UNAVAILABLE", count: 1 },
      { lifecycleState: "OTHER", count: 1 }
    ],
    exceptionSummaries: noActivity ? [] : [
      {
        categoryId: "SALES_INVOICE_ISSUANCE_FAILED",
        availability: "AVAILABLE",
        count: 2,
        affectedExpectedAmounts: [{ currencyCode: "PHP", amount: 425.5 }],
        definition: "Latest coordination state records a supported issuance failure.",
        terminal: false,
        canResolveLater: true,
        limitations: ["A later authoritative retry may resolve the failure."]
      },
      {
        categoryId: "SALES_INVOICE_OUTCOME_UNAVAILABLE",
        availability: "AVAILABLE",
        count: 1,
        affectedExpectedAmounts: [{ currencyCode: "USD", amount: 12.25 }],
        definition: "Central PMS has no conclusive latest issuance outcome.",
        terminal: false,
        canResolveLater: true,
        limitations: ["Unavailable does not mean issuance failed."]
      }
    ],
    currencySummaries: noActivity ? [] : [
      { currencyCode: "PHP", issuanceExpectationCount: 16, expectedIssuanceAmount: 2425.5, issuedCount: 11, failedCount: 2 },
      { currencyCode: "USD", issuanceExpectationCount: 3, expectedIssuanceAmount: 37.25, issuedCount: 1, failedCount: 0 }
    ],
    warnings: noActivity ? ["NO_SALES_INVOICE_ISSUANCE_ACTIVITY_IN_PERIOD"] : ["Development data demonstrates partial source coverage."],
    limitations: ["Central PMS coordination evidence does not certify a BIR-authoritative fiscal report."],
    unavailableFacts: ["Sales Invoice print result", "Digital-copy availability", "BIR compliance certification"],
    sourceAuthority: "CENTRAL_PMS_FISCAL_ISSUANCE_REFERENCES"
  };
}

function normalizeScenarioName(value: string | null): FiscalExceptionReportScenarioName | undefined {
  switch (value) {
    case "activity":
    case "no-activity":
    case "disabled":
    case "unavailable":
      return value;
    default:
      return undefined;
  }
}

function developmentDelay(signal?: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    const timer = window.setTimeout(resolve, 10);
    signal?.addEventListener("abort", () => {
      window.clearTimeout(timer);
      reject(new DOMException("cancelled", "AbortError"));
    }, { once: true });
  });
}

function assertValidQuery(query: FiscalExceptionReportQuery): void {
  const start = new Date(query.periodStart);
  const end = new Date(query.periodEnd);
  if (!(["SITE", "SITE_GROUP"] as string[]).includes(query.scopeType) ||
      !query.scopeReference.trim() ||
      !isExplicitUtc(query.periodStart) ||
      !isExplicitUtc(query.periodEnd) ||
      !Number.isFinite(start.getTime()) ||
      !Number.isFinite(end.getTime()) ||
      start >= end ||
      end.getTime() - start.getTime() > 31 * 24 * 60 * 60 * 1000) {
    throw createUiError("validation", "INVALID_FISCAL_EXCEPTION_REPORT_QUERY", "Select an authorized scope and a valid UTC period of no more than 31 days.");
  }
}

function parseScope(value: unknown): FiscalReportScope {
  const scope = object(value);
  const scopeType = requiredString(scope.scopeType);
  if (scopeType !== "SITE" && scopeType !== "SITE_GROUP") {
    malformed();
  }
  return {
    scopeType: scopeType as FiscalReportScopeType,
    scopeReference: requiredString(scope.scopeReference),
    displayName: requiredString(scope.displayName)
  };
}

function parseSourceCoverage(value: unknown): FiscalSourceCoverage {
  const source = object(value);
  return {
    sourceId: requiredString(source.sourceId),
    availability: requiredString(source.availability),
    dataAsOf: nullableTimestamp(source.dataAsOf),
    description: requiredString(source.description),
    limitations: stringArray(source.limitations)
  };
}

function parseLifecycleSummary(value: unknown): FiscalLifecycleSummary {
  const summary = object(value);
  return { lifecycleState: requiredString(summary.lifecycleState), count: nonnegativeInteger(summary.count) };
}

function parseExceptionSummary(value: unknown): FiscalExceptionSummary {
  const summary = object(value);
  return {
    categoryId: requiredString(summary.categoryId),
    availability: requiredString(summary.availability),
    count: nonnegativeInteger(summary.count),
    affectedExpectedAmounts: array(summary.affectedExpectedAmounts).map(parseAmountSummary),
    definition: requiredString(summary.definition),
    terminal: boolean(summary.terminal),
    canResolveLater: boolean(summary.canResolveLater),
    limitations: stringArray(summary.limitations)
  };
}

function parseCurrencySummary(value: unknown): FiscalCurrencySummary {
  const summary = object(value);
  return {
    currencyCode: currency(summary.currencyCode),
    issuanceExpectationCount: nonnegativeInteger(summary.issuanceExpectationCount),
    expectedIssuanceAmount: nonnegativeNumber(summary.expectedIssuanceAmount),
    issuedCount: nonnegativeInteger(summary.issuedCount),
    failedCount: nonnegativeInteger(summary.failedCount)
  };
}

function parseAmountSummary(value: unknown): FiscalAmountSummary {
  const amount = object(value);
  return { currencyCode: currency(amount.currencyCode), amount: nonnegativeNumber(amount.amount) };
}

function object(value: unknown): Record<string, unknown> {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    malformed();
  }
  return value as Record<string, unknown>;
}

function array(value: unknown): unknown[] {
  if (!Array.isArray(value)) {
    malformed();
  }
  return value;
}

function string(value: unknown): string | undefined {
  return typeof value === "string" && value.trim() ? value : undefined;
}

function requiredString(value: unknown): string {
  return string(value) ?? malformed();
}

function isoTimestamp(value: unknown): string {
  const result = requiredString(value);
  if (!Number.isFinite(new Date(result).getTime())) {
    malformed();
  }
  return result;
}

function nullableTimestamp(value: unknown): string | null {
  if (value === null) {
    return null;
  }
  return isoTimestamp(value);
}

function stringArray(value: unknown): string[] {
  return array(value).map(requiredString);
}

function nonnegativeInteger(value: unknown): number {
  if (typeof value !== "number" || !Number.isSafeInteger(value) || value < 0) {
    malformed();
  }
  return value;
}

function nonnegativeNumber(value: unknown): number {
  if (typeof value !== "number" || !Number.isFinite(value) || value < 0) {
    malformed();
  }
  return value;
}

function currency(value: unknown): string {
  const result = requiredString(value);
  if (!/^[A-Z]{3}$/.test(result)) {
    malformed();
  }
  return result;
}

function boolean(value: unknown): boolean {
  if (typeof value !== "boolean") {
    malformed();
  }
  return value;
}

function isExplicitUtc(value: string): boolean {
  return value.endsWith("Z") || value.endsWith("+00:00");
}

function malformed(): never {
  throw createUiError(
    "malformed-response",
    "MANAGEMENT_FISCAL_REPORT_MALFORMED_RESPONSE",
    "The fiscal exception report response could not be read safely."
  );
}
