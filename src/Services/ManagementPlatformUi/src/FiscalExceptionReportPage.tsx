import { useEffect, useMemo, useRef, useState } from "react";
import type {
  FiscalExceptionReport,
  FiscalExceptionReportClient,
  FiscalExceptionReportQuery,
  FiscalReportScope
} from "./fiscalExceptionReporting";
import type { ManagementPlatformUiError } from "./types";

interface FiscalExceptionReportPageProps {
  client: FiscalExceptionReportClient;
  availableScopes: readonly FiscalReportScope[];
  now?: () => Date;
}

type ReportState = {
  loading: boolean;
  report?: FiscalExceptionReport;
  error?: ManagementPlatformUiError;
  periodError?: string;
};

/**
 * Read-only aggregate fiscal exception reporting console.
 *
 * BRD: Management Dashboard and Reporting BRD v1.0 Sections 16, 19
 * (MDR-FR-016, MDR-FR-023, MDR-FR-024, MDR-FR-042), 24, 30, and 31.
 * SDD: Management Dashboard and Reporting SDD v1.0 Sections 9-16 and 18.
 * Authority boundary: shows only Central PMS-persisted coordination evidence;
 * POS Server retains fiscal authority and no recovery or mutation is offered.
 */
export function FiscalExceptionReportPage({ client, availableScopes, now = () => new Date() }: FiscalExceptionReportPageProps) {
  const initialPeriod = useMemo(() => defaultPeriod(now()), []);
  const [selectedScopeKey, setSelectedScopeKey] = useState(() => scopeKey(availableScopes[0]));
  const [periodStart, setPeriodStart] = useState(initialPeriod.start);
  const [periodEnd, setPeriodEnd] = useState(initialPeriod.end);
  const [state, setState] = useState<ReportState>({ loading: false });
  const requestSequence = useRef(0);
  const activeController = useRef<AbortController | undefined>(undefined);

  const availableScopeKeys = availableScopes.map(scopeKey);
  const effectiveScopeKey = availableScopeKeys.includes(selectedScopeKey)
    ? selectedScopeKey
    : scopeKey(availableScopes[0]);
  const selectedScope = availableScopes.find((scope) => scopeKey(scope) === effectiveScopeKey);

  useEffect(() => {
    setSelectedScopeKey((current) => availableScopeKeys.includes(current) ? current : scopeKey(availableScopes[0]));
  }, [availableScopeKeys.join("|")]);

  useEffect(() => {
    if (!selectedScope) {
      activeController.current?.abort();
      setState({ loading: false });
      return;
    }

    loadReport(selectedScope, periodStart, periodEnd, false);
    return () => activeController.current?.abort();
  }, [client, effectiveScopeKey]);

  function loadReport(scope: FiscalReportScope, startLocal: string, endLocal: string, preserveExistingReport = true) {
    const query = buildQuery(scope, startLocal, endLocal);
    if (!query.ok) {
      setState((current) => ({ ...current, loading: false, error: undefined, periodError: query.message }));
      return;
    }

    activeController.current?.abort();
    const controller = new AbortController();
    activeController.current = controller;
    const requestId = requestSequence.current + 1;
    requestSequence.current = requestId;
    setState((current) => ({
      loading: true,
      report: preserveExistingReport ? current.report : undefined,
      error: undefined,
      periodError: undefined
    }));
    client.getSummary(query.value, controller.signal)
      .then((report) => {
        if (!controller.signal.aborted && requestSequence.current === requestId) {
          setState({ loading: false, report });
        }
      })
      .catch((error) => {
        if (!controller.signal.aborted && requestSequence.current === requestId) {
          setState((current) => ({
            loading: false,
            report: current.report,
            error: toSafeError(error)
          }));
        }
      });
  }

  if (!selectedScope) {
    return <StatePanel name="No authorized fiscal report scope" message="No Site or Site Group is available for this reporting permission." />;
  }

  const disabled = state.error?.code === "MANAGEMENT_FISCAL_EXCEPTION_REPORTING_DISABLED";

  return (
    <section className="panel fiscalReportPage" aria-labelledby="fiscal-report-title">
      <div className="pageTitle fiscalTitle">
        <div>
          <p className="eyebrow">Reports / Fiscal coordination</p>
          <h2 id="fiscal-report-title">Sales Invoice fiscal exceptions</h2>
          <p>Read-only visibility into issuance expectations and the latest outcome persisted by Central PMS.</p>
        </div>
        <span className={`statusPill ${statusTone(state.report?.availability)}`}>{state.loading ? (state.report ? "Refreshing" : "Loading") : state.report?.availability ?? "Not loaded"}</span>
      </div>

      <div className="authorityNotice" role="note">
        <strong>Reporting boundary</strong>
        <span>Central PMS coordination evidence does not prove printing, delivery, or BIR compliance. POS Server remains the fiscal authority.</span>
      </div>

      <form className="reportFilters" onSubmit={(event) => {
        event.preventDefault();
        loadReport(selectedScope, periodStart, periodEnd);
      }}>
        <label className="formField">
          <span>Report scope</span>
          <select value={effectiveScopeKey} onChange={(event) => setSelectedScopeKey(event.target.value)} disabled={state.loading}>
            {availableScopes.map((scope) => <option key={scopeKey(scope)} value={scopeKey(scope)}>{scope.displayName} ({scopeLabel(scope.scopeType)})</option>)}
          </select>
        </label>
        <label className="formField">
          <span>Period start</span>
          <input type="datetime-local" value={periodStart} onChange={(event) => setPeriodStart(event.target.value)} required />
        </label>
        <label className="formField">
          <span>Period end</span>
          <input type="datetime-local" value={periodEnd} onChange={(event) => setPeriodEnd(event.target.value)} required />
        </label>
        <button type="submit" disabled={state.loading}>{state.loading ? "Loading report" : "Refresh report"}</button>
        <p className="filterHelp">UTC query window is half-open: start included, end excluded. Maximum 31 days.</p>
      </form>

      {state.periodError && <StatePanel name="Invalid report period" message={state.periodError} tone="danger" />}
      {disabled && !state.report && <StatePanel name="Fiscal reporting unavailable" message="Fiscal exception reporting is not enabled for this environment." tone="warning" />}
      {state.error && !disabled && !state.report && <ErrorPanel error={state.error} />}
      {state.error && state.report && (
        <section className="stateMessage warning embedded" role="alert" aria-label="Previously loaded fiscal report">
          <h3>Showing previously loaded data</h3>
          <p>The refresh failed. Values below remain from the report generated {formatTimestamp(state.report.generatedAt)} and are not presented as refreshed.</p>
          <p>{state.error.message}{state.error.correlationId ? ` Support reference: ${state.error.correlationId}.` : ""}</p>
        </section>
      )}
      {state.loading && state.report && <StatePanel name="Refreshing fiscal report" message={`Values shown remain from the report generated ${formatTimestamp(state.report.generatedAt)} until the refresh completes.`} />}
      {state.loading && !state.report && <StatePanel name="Loading fiscal report" message="Loading the authorized aggregate report." />}
      {state.report?.availability === "NO_ACTIVITY" && <StatePanel name="No fiscal issuance activity" message="No active, non-superseded fiscal issuance references were first recorded in this period. This is not an exception-free or no-payment certification." />}
      {state.report && <FiscalReport report={state.report} />}
    </section>
  );
}

function FiscalReport({ report }: { report: FiscalExceptionReport }) {
  return (
    <div className="fiscalReportContent">
      <section className="reportMetadata" aria-label="Report scope and freshness">
        <Metadata label="Effective scope" value={`${report.effectiveScope.displayName} (${scopeLabel(report.effectiveScope.scopeType)})`} />
        <Metadata label="Period" value={`${formatTimestamp(report.periodStart)} to ${formatTimestamp(report.periodEnd)} (end excluded)`} />
        <Metadata label="Generated" value={formatTimestamp(report.generatedAt)} />
        <Metadata label="Data as of" value={report.dataAsOf ? formatTimestamp(report.dataAsOf) : "Not applicable"} />
        <Metadata label="Freshness" value={humanize(report.freshness)} />
        <Metadata label="Time basis" value={humanize(report.timeBasis)} />
      </section>

      {report.currencySummaries.length > 0 && (
        <section aria-labelledby="fiscal-currency-title">
          <SectionHeading id="fiscal-currency-title" title="Issuance expectations by currency" description="Expected amounts come from linked payment confirmations. Currencies are never combined." />
          <div className="currencyCards">
            {report.currencySummaries.map((summary) => (
              <article className="metricCard" key={summary.currencyCode}>
                <span>{summary.currencyCode} expected amount</span>
                <strong>{formatMoney(summary.currencyCode, summary.expectedIssuanceAmount)}</strong>
                <dl>
                  <div><dt>Expectations</dt><dd>{formatCount(summary.issuanceExpectationCount)}</dd></div>
                  <div><dt>Issued</dt><dd>{formatCount(summary.issuedCount)}</dd></div>
                  <div><dt>Failed</dt><dd>{formatCount(summary.failedCount)}</dd></div>
                </dl>
              </article>
            ))}
          </div>
        </section>
      )}

      {report.lifecycleSummaries.length > 0 && (
        <section aria-labelledby="fiscal-lifecycle-title">
          <SectionHeading id="fiscal-lifecycle-title" title="Issuance lifecycle" description="Latest persisted coordination state for each reference in the selected cohort." />
          <div className="tableScroller" tabIndex={0} aria-label="Fiscal lifecycle table">
            <table className="dataTable"><thead><tr><th>Lifecycle</th><th>Count</th></tr></thead><tbody>
              {report.lifecycleSummaries.map((summary) => <tr key={summary.lifecycleState}><td>{humanize(summary.lifecycleState)}</td><td>{formatCount(summary.count)}</td></tr>)}
            </tbody></table>
          </div>
        </section>
      )}

      {report.exceptionSummaries.length > 0 && (
        <section aria-labelledby="fiscal-exceptions-title">
          <SectionHeading id="fiscal-exceptions-title" title="Exception exposure" description="Supported exception categories only. Pending work is not automatically an exception." />
          <div className="tableScroller" tabIndex={0} aria-label="Fiscal exception table">
            <table className="dataTable"><thead><tr><th>Category</th><th>Count</th><th>Affected expected amount</th><th>Resolution posture</th></tr></thead><tbody>
              {report.exceptionSummaries.map((summary) => (
                <tr key={summary.categoryId}>
                  <td><strong>{exceptionLabel(summary.categoryId)}</strong><small>{summary.definition}</small></td>
                  <td>{formatCount(summary.count)}</td>
                  <td>{summary.affectedExpectedAmounts.length ? summary.affectedExpectedAmounts.map((amount) => formatMoney(amount.currencyCode, amount.amount)).join("; ") : "None recorded"}</td>
                  <td>{summary.terminal ? "Terminal" : summary.canResolveLater ? "May resolve after authoritative processing" : "Not declared terminal"}</td>
                </tr>
              ))}
            </tbody></table>
          </div>
        </section>
      )}

      <section aria-labelledby="fiscal-source-title">
        <SectionHeading id="fiscal-source-title" title="Source coverage" description="The report uses persisted Central PMS evidence and performs no live Site POS Server query." />
        <div className="sourceCards">
          {report.sourceCoverage.map((source) => (
            <article className="detailGroup" key={source.sourceId}>
              <h4>{humanize(source.sourceId)}</h4>
              <p><strong>{humanize(source.availability)}</strong> · {source.dataAsOf ? `Data as of ${formatTimestamp(source.dataAsOf)}` : "No source timestamp"}</p>
              <p>{source.description}</p>
              {source.limitations.length > 0 && <MessageList items={source.limitations} />}
            </article>
          ))}
        </div>
      </section>

      <section className="reportCaveats" aria-labelledby="fiscal-caveats-title">
        <SectionHeading id="fiscal-caveats-title" title="Warnings and limitations" description={`Source authority: ${humanize(report.sourceAuthority)}.`} />
        <CaveatGroup title="Warnings" items={report.warnings} />
        <CaveatGroup title="Limitations" items={report.limitations} />
        <CaveatGroup title="Unavailable facts" items={report.unavailableFacts} />
        <p className="supportReference">Report support reference: {report.correlationId}</p>
      </section>
    </div>
  );
}

function SectionHeading({ id, title, description }: { id: string; title: string; description: string }) {
  return <div className="sectionHeader"><div><h3 id={id}>{title}</h3><p>{description}</p></div></div>;
}

function Metadata({ label, value }: { label: string; value: string }) {
  return <div><span>{label}</span><strong>{value}</strong></div>;
}

function CaveatGroup({ title, items }: { title: string; items: string[] }) {
  if (!items.length) return null;
  return <div><h4>{title}</h4><MessageList items={items} /></div>;
}

function MessageList({ items }: { items: string[] }) {
  return <ul className="messageList">{items.map((item, index) => <li key={`${index}-${item}`}>{humanizeMessage(item)}</li>)}</ul>;
}

function StatePanel({ name, message, tone = "neutral" }: { name: string; message: string; tone?: "neutral" | "warning" | "danger" }) {
  return <section className={`stateMessage ${tone} embedded`} role={tone === "danger" ? "alert" : "status"} aria-label={name}><h3>{name}</h3><p>{message}</p></section>;
}

function ErrorPanel({ error }: { error: ManagementPlatformUiError }) {
  return <section className="stateMessage danger embedded" role="alert" aria-label="Fiscal report error"><h3>Fiscal report unavailable</h3><p>{error.message}{error.correlationId ? ` Support reference: ${error.correlationId}.` : ""}</p></section>;
}

function buildQuery(scope: FiscalReportScope, periodStart: string, periodEnd: string): { ok: true; value: FiscalExceptionReportQuery } | { ok: false; message: string } {
  const start = new Date(periodStart);
  const end = new Date(periodEnd);
  if (!periodStart || !periodEnd || !Number.isFinite(start.getTime()) || !Number.isFinite(end.getTime()) || start >= end) {
    return { ok: false, message: "Period end must be later than period start." };
  }
  if (end.getTime() - start.getTime() > 31 * 24 * 60 * 60 * 1000) {
    return { ok: false, message: "The reporting period cannot exceed 31 days." };
  }
  return { ok: true, value: { scopeType: scope.scopeType, scopeReference: scope.scopeReference, periodStart: start.toISOString(), periodEnd: end.toISOString() } };
}

function defaultPeriod(now: Date): { start: string; end: string } {
  const end = new Date(now);
  end.setSeconds(0, 0);
  const start = new Date(end.getTime() - 7 * 24 * 60 * 60 * 1000);
  return { start: toDateTimeLocal(start), end: toDateTimeLocal(end) };
}

function toDateTimeLocal(value: Date): string {
  const offset = value.getTimezoneOffset() * 60_000;
  return new Date(value.getTime() - offset).toISOString().slice(0, 16);
}

function toSafeError(error: unknown): ManagementPlatformUiError {
  if (typeof error === "object" && error !== null && "kind" in error && "message" in error) {
    return error as ManagementPlatformUiError;
  }
  return { kind: "unknown", code: "MANAGEMENT_FISCAL_REPORT_FAILED", message: "The fiscal exception report could not be loaded safely.", retryable: false, mutationUncertain: false };
}

function scopeKey(scope?: FiscalReportScope): string {
  return scope ? `${scope.scopeType}:${scope.scopeReference}` : "";
}

function scopeLabel(scopeType: string): string {
  return scopeType === "SITE_GROUP" ? "Site Group" : "Site";
}

function exceptionLabel(categoryId: string): string {
  const labels: Record<string, string> = {
    SALES_INVOICE_ISSUANCE_FAILED: "Sales Invoice issuance failed",
    SALES_INVOICE_REFERENCE_CONFLICT: "Sales Invoice reference conflict",
    SALES_INVOICE_OUTCOME_UNAVAILABLE: "Sales Invoice outcome unavailable"
  };
  return labels[categoryId] ?? humanize(categoryId);
}

function humanize(value: string): string {
  return value.toLowerCase().replaceAll("_", " ").replace(/^./, (character) => character.toUpperCase());
}

function humanizeMessage(value: string): string {
  return /^[A-Z0-9_]+$/.test(value) ? humanize(value) : value;
}

function formatCount(value: number): string {
  return value.toLocaleString("en-US");
}

function formatMoney(currencyCode: string, value: number): string {
  return `${currencyCode} ${value.toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

function formatTimestamp(value: string): string {
  return new Intl.DateTimeFormat("en-PH", {
    year: "numeric",
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
    timeZoneName: "short"
  }).format(new Date(value));
}

function statusTone(availability?: string): string {
  return availability === "PARTIAL" ? "statusPartial" : availability === "NO_ACTIVITY" ? "statusNeutral" : "";
}
