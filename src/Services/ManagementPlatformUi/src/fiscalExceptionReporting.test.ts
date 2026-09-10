import { describe, expect, it, vi } from "vitest";
import { createUiError } from "./apiClient";
import {
  createFiscalExceptionReportingClient,
  fiscalExceptionReportingApiRoute,
  parseFiscalExceptionReport,
  resolveFiscalExceptionReportScenario,
} from "./fiscalExceptionReporting";
import { fiscalReport } from "./test/fiscalExceptionReportFixture";
import type { CentralPmsApiClient } from "./types";

describe("Management Platform fiscal exception reporting client", () => {
  it("requests the read-only endpoint with explicit scope and half-open UTC period", async () => {
    const report = fiscalReport();
    const apiClient: CentralPmsApiClient = { request: vi.fn().mockResolvedValue(report) };

    const client = createFiscalExceptionReportingClient(apiClient);
    await expect(client.getSummary({
      scopeType: "SITE",
      scopeReference: "71000000-0000-0000-0000-000000000101",
      periodStart: "2026-08-01T00:00:00.000Z",
      periodEnd: "2026-08-08T00:00:00.000Z"
    })).resolves.toEqual(report);

    expect(apiClient.request).toHaveBeenCalledWith(
      `${fiscalExceptionReportingApiRoute}?scopeType=SITE&scopeReference=71000000-0000-0000-0000-000000000101&periodStart=2026-08-01T00%3A00%3A00.000Z&periodEnd=2026-08-08T00%3A00%3A00.000Z`,
      { method: "GET", signal: undefined }
    );
  });

  it("fails closed when the response contract or safe aggregates are malformed", () => {
    expect(() => parseFiscalExceptionReport({ ...fiscalReport(), contractVersion: "unexpected" })).toThrowError(
      expect.objectContaining({ code: "MANAGEMENT_FISCAL_REPORT_MALFORMED_RESPONSE" })
    );
    expect(() => parseFiscalExceptionReport({ ...fiscalReport(), currencySummaries: [{ currencyCode: "PHP", issuanceExpectationCount: -1 }] })).toThrowError(
      expect.objectContaining({ code: "MANAGEMENT_FISCAL_REPORT_MALFORMED_RESPONSE" })
    );
  });

  it("does not replace a safe API failure with response data", async () => {
    const failure = createUiError("integration-unavailable", "FISCAL_EXCEPTION_SOURCE_UNAVAILABLE", "Fiscal exception reporting is temporarily unavailable.", "corr-unavailable", 503, true);
    const apiClient: CentralPmsApiClient = { request: vi.fn().mockRejectedValue(failure) };

    await expect(createFiscalExceptionReportingClient(apiClient).getSummary({
      scopeType: "SITE_GROUP",
      scopeReference: "71000000-0000-0000-0000-000000000201",
      periodStart: "2026-08-01T00:00:00.000Z",
      periodEnd: "2026-08-08T00:00:00.000Z"
    })).rejects.toBe(failure);
  });

  it("enables controlled scenarios only in development mode", async () => {
    const development = resolveFiscalExceptionReportScenario(true, "?mpFiscalScenario=activity");
    expect(development?.name).toBe("activity");
    await expect(development?.client.getSummary({
      scopeType: "SITE",
      scopeReference: "71000000-0000-0000-0000-000000000101",
      periodStart: "2026-08-01T00:00:00.000Z",
      periodEnd: "2026-08-08T00:00:00.000Z"
    })).resolves.toMatchObject({ availability: "PARTIAL" });
    expect(resolveFiscalExceptionReportScenario(false, "?mpFiscalScenario=activity")).toBeUndefined();
    expect(resolveFiscalExceptionReportScenario(true, "?mpFiscalScenario=unknown")).toBeUndefined();
  });
});
