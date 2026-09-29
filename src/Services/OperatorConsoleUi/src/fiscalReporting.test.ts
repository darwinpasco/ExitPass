import { describe, expect, it, vi } from "vitest";
import { createOperatorFiscalReportingClient } from "./fiscalReporting";

describe("Operator fiscal-reporting HTTP client", () => {
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
