import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { createUiError } from "./apiClient";
import { FiscalExceptionReportPage } from "./FiscalExceptionReportPage";
import { fiscalReport } from "./test/fiscalExceptionReportFixture";
import type { FiscalExceptionReportClient, FiscalReportScope } from "./fiscalExceptionReporting";

const scopes: FiscalReportScope[] = [
  { scopeType: "SITE", scopeReference: "71000000-0000-0000-0000-000000000101", displayName: "Development Site Alpha" },
  { scopeType: "SITE_GROUP", scopeReference: "71000000-0000-0000-0000-000000000201", displayName: "Development Group" }
];

describe("Management Platform fiscal exception report page", () => {
  it("renders source-labeled read-only lifecycle, exception, and per-currency aggregates", async () => {
    const client: FiscalExceptionReportClient = { getSummary: vi.fn().mockResolvedValue(fiscalReport()) };
    render(<FiscalExceptionReportPage client={client} availableScopes={scopes} now={() => new Date("2026-08-08T00:00:00Z")} />);

    expect(await screen.findByRole("heading", { name: "Sales Invoice fiscal exceptions" })).toBeInTheDocument();
    expect(await screen.findByText("PHP 1,250.75")).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: "Issued" })).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: /Sales Invoice issuance failed/i })).toBeInTheDocument();
    expect(screen.getByText(/Central PMS coordination evidence/i)).toBeInTheDocument();
    expect(screen.getByText(/does not prove printing, delivery, or BIR compliance/i)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /retry issuance|resolve exception|export/i })).not.toBeInTheDocument();
  });

  it("uses only explicit authorized scope choices and submits an explicit UTC period", async () => {
    const getSummary = vi.fn().mockResolvedValue(fiscalReport());
    render(<FiscalExceptionReportPage client={{ getSummary }} availableScopes={scopes} now={() => new Date("2026-08-08T00:00:00Z")} />);
    await screen.findByText("PHP 1,250.75");

    await userEvent.selectOptions(screen.getByLabelText("Report scope"), "SITE_GROUP:71000000-0000-0000-0000-000000000201");
    await userEvent.clear(screen.getByLabelText("Period start"));
    await userEvent.type(screen.getByLabelText("Period start"), "2026-08-02T08:00");
    await userEvent.click(screen.getByRole("button", { name: "Refresh report" }));

    await waitFor(() => expect(getSummary).toHaveBeenLastCalledWith(expect.objectContaining({
      scopeType: "SITE_GROUP",
      scopeReference: "71000000-0000-0000-0000-000000000201",
      periodStart: "2026-08-02T00:00:00.000Z",
      periodEnd: "2026-08-08T00:00:00.000Z"
    }), expect.any(AbortSignal)));
    expect(screen.queryByRole("textbox", { name: /scope reference/i })).not.toBeInTheDocument();
  });

  it("distinguishes no activity and feature-disabled states from zero or source failure", async () => {
    const noActivityClient: FiscalExceptionReportClient = {
      getSummary: vi.fn().mockResolvedValue(fiscalReport({
        availability: "NO_ACTIVITY", freshness: "NOT_APPLICABLE", dataAsOf: null,
        lifecycleSummaries: [], exceptionSummaries: [], currencySummaries: [],
        warnings: ["NO_SALES_INVOICE_ISSUANCE_ACTIVITY_IN_PERIOD"]
      }))
    };
    const { unmount } = render(<FiscalExceptionReportPage client={noActivityClient} availableScopes={scopes} now={() => new Date("2026-08-08T00:00:00Z")} />);
    expect(await screen.findByRole("status", { name: "No fiscal issuance activity" })).toBeInTheDocument();
    unmount();

    const disabledClient: FiscalExceptionReportClient = {
      getSummary: vi.fn().mockRejectedValue(createUiError("feature-disabled", "MANAGEMENT_FISCAL_EXCEPTION_REPORTING_DISABLED", "Feature disabled.", "corr-disabled", 503))
    };
    render(<FiscalExceptionReportPage client={disabledClient} availableScopes={scopes} now={() => new Date("2026-08-08T00:00:00Z")} />);
    expect(await screen.findByRole("status", { name: "Fiscal reporting unavailable" })).toHaveTextContent("not enabled");
    expect(screen.queryByText("PHP 1,250.75")).not.toBeInTheDocument();
  });

  it("retains timestamped prior data after a failed refresh without presenting it as current", async () => {
    const getSummary = vi.fn()
      .mockResolvedValueOnce(fiscalReport())
      .mockRejectedValueOnce(createUiError("integration-unavailable", "FISCAL_EXCEPTION_SOURCE_UNAVAILABLE", "Source unavailable.", "corr-refresh", 503, true));
    render(<FiscalExceptionReportPage client={{ getSummary }} availableScopes={scopes} now={() => new Date("2026-08-08T00:00:00Z")} />);
    await screen.findByText("PHP 1,250.75");

    await userEvent.click(screen.getByRole("button", { name: "Refresh report" }));

    expect(await screen.findByRole("alert", { name: "Previously loaded fiscal report" })).toHaveTextContent("2026");
    expect(screen.getByText("PHP 1,250.75")).toBeInTheDocument();
    expect(screen.getByText(/corr-refresh/i)).toBeInTheDocument();
  });

  it("does not display a previous scope's aggregates while a new scope is loading", async () => {
    let resolveGroup: ((report: ReturnType<typeof fiscalReport>) => void) | undefined;
    const getSummary = vi.fn()
      .mockResolvedValueOnce(fiscalReport())
      .mockImplementationOnce(() => new Promise((resolve) => { resolveGroup = resolve; }));
    render(<FiscalExceptionReportPage client={{ getSummary }} availableScopes={scopes} now={() => new Date("2026-08-08T00:00:00Z")} />);
    await screen.findByText("PHP 1,250.75");

    await userEvent.selectOptions(screen.getByLabelText("Report scope"), "SITE_GROUP:71000000-0000-0000-0000-000000000201");

    expect(await screen.findByRole("status", { name: "Loading fiscal report" })).toBeInTheDocument();
    expect(screen.queryByText("PHP 1,250.75")).not.toBeInTheDocument();
    await act(async () => resolveGroup?.(fiscalReport({ effectiveScope: scopes[1] })));
    const metadata = await screen.findByRole("region", { name: "Report scope and freshness" });
    expect(within(metadata).getByText("Development Group (Site Group)")).toBeInTheDocument();
  });

  it("fails locally for an invalid or over-31-day period without issuing a request", async () => {
    const getSummary = vi.fn().mockResolvedValue(fiscalReport());
    render(<FiscalExceptionReportPage client={{ getSummary }} availableScopes={scopes} now={() => new Date("2026-08-08T00:00:00Z")} />);
    await screen.findByText("PHP 1,250.75");
    getSummary.mockClear();

    await userEvent.clear(screen.getByLabelText("Period start"));
    await userEvent.type(screen.getByLabelText("Period start"), "2026-06-01T08:00");
    await userEvent.click(screen.getByRole("button", { name: "Refresh report" }));

    expect(screen.getByRole("alert", { name: "Invalid report period" })).toHaveTextContent("31 days");
    expect(getSummary).not.toHaveBeenCalled();
  });

  it("renders a safe empty-scope posture without calling the API", () => {
    const getSummary = vi.fn();
    render(<FiscalExceptionReportPage client={{ getSummary }} availableScopes={[]} now={() => new Date("2026-08-08T00:00:00Z")} />);

    expect(screen.getByRole("status", { name: "No authorized fiscal report scope" })).toBeInTheDocument();
    expect(getSummary).not.toHaveBeenCalled();
  });
});
