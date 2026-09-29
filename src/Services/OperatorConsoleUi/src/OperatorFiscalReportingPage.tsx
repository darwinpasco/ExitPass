import { useEffect, useState } from "react";
import {
  fiscalReportingPermissions,
  type CloseableFiscalBusinessDate,
  type EjInvoice,
  type FiscalReportingSite,
  type History,
  type OperatorFiscalReportingClient
} from "./fiscalReporting";

export function OperatorFiscalReportingPage({ client, siteReferences, permissions }: {
  client: OperatorFiscalReportingClient;
  siteReferences: readonly string[];
  permissions: readonly string[];
}) {
  const [sites, setSites] = useState<FiscalReportingSite[]>();
  const [siteId, setSiteId] = useState("");
  const [tab, setTab] = useState<"ej" | "x" | "z">("ej");
  const [start, setStart] = useState("");
  const [end, setEnd] = useState("");
  const [search, setSearch] = useState("");
  const [ej, setEj] = useState<EjInvoice[]>();
  const [x, setX] = useState<History>();
  const [z, setZ] = useState<History>();
  const [closeablePeriods, setCloseablePeriods] = useState<CloseableFiscalBusinessDate[]>([]);
  const [busy, setBusy] = useState(false);
  const [catalogError, setCatalogError] = useState<string>();
  const [ejError, setEjError] = useState<string>();
  const [xHistoryError, setXHistoryError] = useState<string>();
  const [xGenerateError, setXGenerateError] = useState<string>();
  const [zHistoryError, setZHistoryError] = useState<string>();
  const [closeableError, setCloseableError] = useState<string>();
  const [closeError, setCloseError] = useState<string>();
  const [batchCloseError, setBatchCloseError] = useState<string>();
  const [message, setMessage] = useState<string>();
  const [closeConfirmation, setCloseConfirmation] = useState<{ kind: "single"; period: CloseableFiscalBusinessDate } | { kind: "all" }>();
  const has = (permission: string) => permissions.includes(permission);
  const siteScopeKey = siteReferences.join("|");

  async function loadEj(requireRange = true, rangeOverride?: { start: string; end: string }) {
    if (!siteId) return;
    setEjError(undefined);
    setEj(undefined);
    const range = rangeOverride ?? phtRangeToUtc(start, end);
    if (!range) {
      if (requireRange) setEjError("Enter a valid PHT period start and end. The start must be before the end.");
      return;
    }
    try {
      setEj(await client.readEj(siteId, range.start, range.end, search));
    } catch (cause) {
      setEjError(operationMessage(cause, "Electronic Journal could not be loaded."));
    }
  }

  async function loadReportingData() {
    if (!siteId) return;
    setBusy(true);
    setMessage(undefined);
    setXHistoryError(undefined);
    setZHistoryError(undefined);
    setCloseableError(undefined);
    let nextX: History | undefined;
    let nextZ: History | undefined;
    await Promise.all([
      has(fiscalReportingPermissions.xRead)
        ? client.readX(siteId).then((value) => { nextX = value; setX(value); }).catch((cause) => { setX(undefined); setXHistoryError(operationMessage(cause, "X Reading history could not be loaded.")); })
        : Promise.resolve(),
      has(fiscalReportingPermissions.zRead)
        ? client.readZ(siteId).then((value) => { nextZ = value; setZ(value); }).catch((cause) => { setZ(undefined); setZHistoryError(operationMessage(cause, "Z Reading history could not be loaded.")); })
        : Promise.resolve(),
      has(fiscalReportingPermissions.zRead)
        ? client.readCloseablePeriods(siteId).then((value) => setCloseablePeriods(value.fiscalBusinessDates)).catch((cause) => { setCloseablePeriods([]); setCloseableError(operationMessage(cause, "Open Fiscal Business Dates could not be loaded.")); })
        : Promise.resolve()
    ]);

    const currentPeriod = nextX?.currentPeriod ?? nextZ?.currentPeriod;
    if (!start && !end && currentPeriod) {
      const nextStart = formatPhtInputValue(currentPeriod.periodStartAt);
      const nextEnd = formatPhtInputValue(currentPeriod.periodEndAt);
      setStart(nextStart);
      setEnd(nextEnd);
      if (has(fiscalReportingPermissions.ejRead)) {
        await loadEj(false, { start: currentPeriod.periodStartAt, end: currentPeriod.periodEndAt });
      }
    } else if (has(fiscalReportingPermissions.ejRead)) {
      await loadEj(false);
    }
    setBusy(false);
  }

  useEffect(() => {
    let active = true;
    setCatalogError(undefined);
    if (!client.listAuthorizedSites) {
      setSites([]);
      setSiteId("");
      setCatalogError("Fiscal reporting Site catalog is unavailable.");
      return () => { active = false; };
    }

    setSites(undefined);
    setSiteId("");
    void client.listAuthorizedSites()
      .then((resolvedSites) => {
        if (!active) return;
        const sessionSites = new Set(siteReferences);
        const scopedSites = resolvedSites.filter((site) => sessionSites.has(site.siteId));
        setSites(scopedSites);
        setSiteId(scopedSites[0]?.siteId ?? "");
      })
      .catch((cause) => {
        if (!active) return;
        setSites([]);
        setCatalogError(operationMessage(cause, "Fiscal reporting Sites are unavailable."));
      });
    return () => { active = false; };
  }, [client, siteScopeKey]);

  useEffect(() => { void loadReportingData(); }, [siteId]);

  function changeSite(nextSiteId: string) {
    setStart("");
    setEnd("");
    setEj(undefined);
    setX(undefined);
    setZ(undefined);
    setCloseablePeriods([]);
    setEjError(undefined);
    setXHistoryError(undefined);
    setXGenerateError(undefined);
    setZHistoryError(undefined);
    setCloseableError(undefined);
    setCloseError(undefined);
    setBatchCloseError(undefined);
    setCloseConfirmation(undefined);
    setSiteId(nextSiteId);
  }

  async function generateX() {
    setBusy(true);
    setXGenerateError(undefined);
    try {
      await client.generateX(siteId);
      setX(await client.readX(siteId));
    } catch (cause) {
      setXGenerateError(operationMessage(cause, "X Reading generation failed safely."));
    } finally {
      setBusy(false);
    }
  }

  async function closeBusinessDates() {
    if (!closeConfirmation) return;
    setBusy(true);
    setCloseError(undefined);
    setBatchCloseError(undefined);
    setMessage(undefined);
    try {
      if (closeConfirmation.kind === "single") {
        const closingPeriod = closeConfirmation.period;
        await client.closeBusinessDate(siteId, closingPeriod);
        const [nextCloseable, nextHistory] = await Promise.all([client.readCloseablePeriods(siteId), client.readZ(siteId)]);
        if (nextCloseable.fiscalBusinessDates.some((period) => period.fiscalReportingPeriodId === closingPeriod.fiscalReportingPeriodId) ||
            nextHistory.readings.filter((reading) => reading.fiscalReportingPeriodId === closingPeriod.fiscalReportingPeriodId).length !== 1) {
          throw new Error("The authoritative fiscal close transition could not be confirmed.");
        }
        setCloseablePeriods(nextCloseable.fiscalBusinessDates);
        setZ(nextHistory);
        setMessage(`Fiscal Business Date ${closingPeriod.fiscalBusinessDate} closed for ${selectedSiteName}.`);
      } else {
        const closingPeriods = [...closeablePeriods];
        const result = await client.closeAllBusinessDates(siteId);
        const [nextCloseable, nextHistory] = await Promise.all([client.readCloseablePeriods(siteId), client.readZ(siteId)]);
        const remaining = new Set(nextCloseable.fiscalBusinessDates.map((period) => period.fiscalReportingPeriodId));
        if (closingPeriods.some((period) => remaining.has(period.fiscalReportingPeriodId)) ||
            closingPeriods.some((period) => nextHistory.readings.filter((reading) => reading.fiscalReportingPeriodId === period.fiscalReportingPeriodId).length !== 1)) {
          throw new Error("The authoritative batch fiscal close transitions could not be confirmed.");
        }
        setCloseablePeriods(nextCloseable.fiscalBusinessDates);
        setZ(nextHistory);
        setMessage(result.message ?? `${result.closedCount} Fiscal Business Dates closed for ${selectedSiteName}.`);
      }
      setCloseConfirmation(undefined);
    } catch (cause) {
      try {
        setCloseablePeriods((await client.readCloseablePeriods(siteId)).fiscalBusinessDates);
      } catch {
        // Preserve the primary close error; the next page refresh retries authoritative state.
      }
      const detail = cause instanceof Error ? cause.message : "Fiscal Business Date close failed safely.";
      if (closeConfirmation.kind === "all") setBatchCloseError(`Closing stopped for ${selectedSiteName}. ${detail}`);
      else setCloseError(detail);
      setCloseConfirmation(undefined);
    } finally {
      setBusy(false);
    }
  }

  if (!siteReferences.length) return <NoAuthorizedSite message="Your operator session has no Site scope." />;
  if (sites === undefined) return <section className="stateMessage"><h2>Loading authorized Site</h2><p>Resolving canonical Site names for fiscal reporting.</p></section>;
  if (!sites.length) {
    return catalogError
      ? <section className="stateMessage danger" role="alert"><h2>Fiscal reporting unavailable</h2><p>{catalogError}</p></section>
      : <NoAuthorizedSite message="Your operator session has no active fiscal Site scope." />;
  }

  const downloadRange = phtRangeToUtc(start, end);
  const selectedSiteName = sites.find((site) => site.siteId === siteId)?.siteName ?? "the selected Site";

  return <section className="panel fiscalReporting" aria-labelledby="operator-fiscal-reporting-title">
    <div className="pageTitle"><div><p className="eyebrow">Site POS authority</p><h2 id="operator-fiscal-reporting-title">Fiscal Reporting</h2><p>Electronic Journal, X Reading, and Z Reading are retrieved through Central PMS from the bound Site POS Server.</p></div><span className="statusPill">Site scoped</span></div>
    {sites.length === 1
      ? <div className="siteSelector" aria-label="Authorized Site"><span>Authorized Site</span><strong>{sites[0].siteName}</strong></div>
      : <label className="siteSelector">Authorized Site<select value={siteId} onChange={(event) => changeSite(event.target.value)}>{sites.map((site) => <option key={site.siteId} value={site.siteId}>{site.siteName}</option>)}</select></label>}
    <div className="reportTabs" role="tablist" aria-label="Fiscal report type"><button type="button" role="tab" aria-selected={tab === "ej"} onClick={() => setTab("ej")}>Electronic Journal</button><button type="button" role="tab" aria-selected={tab === "x"} onClick={() => setTab("x")}>X Reading</button><button type="button" role="tab" aria-selected={tab === "z"} onClick={() => setTab("z")}>Z Reading</button></div>
    {busy && <p role="status">Loading authoritative POS reporting data...</p>}
    {message && <p className="successMessage" role="status">{message}</p>}
    {tab === "ej" && (has(fiscalReportingPermissions.ejRead) ? <div className="fiscalPane" role="tabpanel"><h3>Electronic Journal</h3><p>Exact visible Sales Invoice text supplied by POS authority. The browser does not create the journal artifact.</p><div className="reportFilters"><label>Period start (PHT)<input type="datetime-local" step="1" aria-label="EJ period start PHT" value={start} onChange={(e) => setStart(e.target.value)} /></label><label>Period end (PHT)<input type="datetime-local" step="1" aria-label="EJ period end PHT" value={end} onChange={(e) => setEnd(e.target.value)} /></label><label>Ticket, Plate or SI Number<input aria-label="Search Electronic Journal" value={search} onChange={(e) => setSearch(e.target.value)} /></label><button type="button" onClick={() => void loadEj(true)}>View journal</button>{has(fiscalReportingPermissions.ejExport) && !!ej?.length && downloadRange && <a className="primaryButton" href={client.ejUrl(siteId, downloadRange.start, downloadRange.end, search)}>Download authoritative .txt</a>}</div>{ejError && <p className="errorMessage" role="alert">{ejError}</p>}{ej?.length === 0 && <p role="status">No Sales Invoice activity exists for this period.</p>}{ej?.map((invoice) => <article className="invoiceText" key={invoice.fiscalDocumentId}><header><strong>{invoice.fiscalDocumentNumber}</strong><span>{formatPhtInstant(invoice.issuedAt)} PHT</span></header><pre>{invoice.printableText}</pre></article>)}</div> : <p className="stateMessage warning">Electronic Journal permission is required.</p>)}
    {tab === "x" && <ReadingPanel kind="X" history={x} error={xHistoryError} actionError={xGenerateError} canRead={has(fiscalReportingPermissions.xRead)} canGenerate={has(fiscalReportingPermissions.xGenerate)} onGenerate={generateX} download={(reference) => client.readingUrl(siteId, "x", reference)} />}
    {tab === "z" && <>
      {has(fiscalReportingPermissions.zRead) && <section className="fiscalPane" aria-labelledby="open-fiscal-business-dates-title">
        <div className="readingTitle"><div><h3 id="open-fiscal-business-dates-title">Open Fiscal Business Dates</h3><p>Authoritative closeable periods for {selectedSiteName}. The active pre-cutoff period is excluded by POS authority.</p></div>{has(fiscalReportingPermissions.zGenerate) && closeablePeriods.length > 1 && <button className="dangerButton" type="button" onClick={() => setCloseConfirmation({ kind: "all" })}>Close All Open Business Dates</button>}</div>
        {closeableError && <p className="errorMessage" role="alert">{closeableError}</p>}
        {closeError && <p className="errorMessage" role="alert">{closeError}</p>}
        {batchCloseError && <p className="errorMessage" role="alert">{batchCloseError}</p>}
        {closeablePeriods.length === 0 ? <p role="status">No historical Fiscal Business Dates are currently closeable for this Site.</p> : <div className="tableViewport"><table><thead><tr><th>Fiscal Business Date</th><th>Period Start</th><th>Period End</th><th>Status</th><th>Transaction Count</th><th>Z Reading</th><th>Action</th></tr></thead><tbody>{closeablePeriods.map((period) => <tr key={period.fiscalReportingPeriodId}><td>{period.fiscalBusinessDate}</td><td>{formatPhtInstant(period.periodStart)} PHT</td><td>{formatPhtInstant(period.periodEnd)} PHT</td><td>{period.status}</td><td>{period.transactionCount}</td><td>Pending close</td><td>{has(fiscalReportingPermissions.zGenerate) ? <button type="button" onClick={() => setCloseConfirmation({ kind: "single", period })}>Close Business Date</button> : "Not authorized"}</td></tr>)}</tbody></table></div>}
      </section>}
      {closeConfirmation && <div className="zConfirmation" role="alertdialog" aria-label="Confirm Fiscal Business Date close">
        <h3>{closeConfirmation.kind === "single" ? `Close Fiscal Business Date ${closeConfirmation.period.fiscalBusinessDate} for ${selectedSiteName} and generate its Z Reading?` : `Close ${closeablePeriods.length} open Fiscal Business Dates for ${selectedSiteName} from ${closeablePeriods[0]?.fiscalBusinessDate} through ${closeablePeriods[closeablePeriods.length - 1]?.fiscalBusinessDate}?`}</h3>
        {closeConfirmation.kind === "all" && <p>Each business date will generate its own Z Reading and advance the Site's Z counter once. Processing stops at the first failure.</p>}
        <button className="dangerButton" type="button" onClick={() => void closeBusinessDates()} disabled={busy}>{closeConfirmation.kind === "single" ? "Close Business Date" : "Close All Open Business Dates"}</button><button type="button" onClick={() => setCloseConfirmation(undefined)} disabled={busy}>Cancel</button>
      </div>}
      <ReadingPanel kind="Z" history={z} error={zHistoryError} canRead={has(fiscalReportingPermissions.zRead)} canGenerate={false} download={(reference) => client.readingUrl(siteId, "z", reference)} />
    </>}
  </section>;
}

function NoAuthorizedSite({ message }: { message: string }) {
  return <section className="stateMessage warning"><h2>No authorized fiscal Site</h2><p><strong>NO_AUTHORIZED_SITE</strong></p><p>{message}</p></section>;
}

function ReadingPanel({ kind, history, error, actionError, canRead, canGenerate, onGenerate, download }: { kind: "X" | "Z"; history?: History; error?: string; actionError?: string; canRead: boolean; canGenerate: boolean; onGenerate?: () => void; download: (reference: string) => string }) {
  if (!canRead) return <p className="stateMessage warning">{kind} Reading permission is required.</p>;
  return <div className="fiscalPane" role="tabpanel"><div className="readingTitle"><div><h3>{kind} Reading</h3><p>{kind === "X" ? "Non-closing observation of the current reporting period." : "Closing fiscal report history."}</p></div>{canGenerate && history?.currentPeriod && <button type="button" onClick={onGenerate}>Generate {kind} Reading</button>}</div>{error && <p className="errorMessage" role="alert">{error}</p>}{actionError && <p className="errorMessage" role="alert">{actionError}</p>}{history?.currentPeriod ? <dl className="periodCard"><div><dt>Fiscal Business Date</dt><dd>{history.currentPeriod.businessDayDate}</dd></div><div><dt>Period start (PHT)</dt><dd>{formatPhtInstant(history.currentPeriod.periodStartAt)}</dd></div><div><dt>Period end (PHT)</dt><dd>{formatPhtInstant(history.currentPeriod.periodEndAt)}</dd></div><div><dt>Status</dt><dd>{history.currentPeriod.status}</dd></div></dl> : <p role="status">{kind === "X" && canGenerate ? "No open fiscal reporting period is currently available for X generation." : "No open fiscal reporting period is available."}</p>}<div className="tableViewport"><table><thead><tr><th>Reference</th><th>Generated (PHT)</th><th>Transactions</th><th>Gross sales</th><th>VATable</th><th>VAT</th><th>VAT exempt</th><th>Zero rated</th><th>Discounts</th><th>Voids</th><th>Adjustments</th><th>Net sales</th><th>Tenders</th><th>Output</th></tr></thead><tbody>{history?.readings.map((reading) => <tr key={reading.reportReference}><td>{reading.reportReference}</td><td>{formatPhtInstant(reading.generatedAt)} PHT</td><td>{reading.transactionCount}</td><td>{money(reading.amounts.grossSalesAmountMinorUnits, reading.currencyCode)}</td><td>{money(reading.amounts.vatableSalesAmountMinorUnits, reading.currencyCode)}</td><td>{money(reading.amounts.vatAmountMinorUnits, reading.currencyCode)}</td><td>{money(reading.amounts.vatExemptSalesAmountMinorUnits, reading.currencyCode)}</td><td>{money(reading.amounts.zeroRatedSalesAmountMinorUnits, reading.currencyCode)}</td><td>{money(reading.amounts.discountAmountMinorUnits, reading.currencyCode)}</td><td>{money(reading.amounts.voidAmountMinorUnits, reading.currencyCode)}</td><td>{money(reading.amounts.adjustmentAmountMinorUnits, reading.currencyCode)}</td><td>{money(reading.amounts.netSalesAmountMinorUnits, reading.currencyCode)}</td><td>{reading.tenders.length ? reading.tenders.map((tender) => `${tender.classification}: ${money(tender.amountMinorUnits, tender.currencyCode)}`).join("; ") : "None"}</td><td><a href={download(reading.reportReference)}>Print / download</a></td></tr>)}</tbody></table></div></div>;
}

function operationMessage(cause: unknown, fallback: string) {
  return cause instanceof Error && cause.message ? cause.message : fallback;
}

const PHT_OFFSET_MILLISECONDS = 8 * 60 * 60 * 1000;
const EXPLICIT_INSTANT_PATTERN = /(Z|[+-]\d{2}:\d{2})$/i;
const PHT_INPUT_PATTERN = /^(\d{4})-(\d{2})-(\d{2})[ T](\d{2}):(\d{2})(?::(\d{2}))?$/;

export function formatPhtInstant(value: string) {
  if (!EXPLICIT_INSTANT_PATTERN.test(value)) throw new Error("An explicit fiscal reporting instant is required.");
  const milliseconds = Date.parse(value);
  if (!Number.isFinite(milliseconds)) throw new Error("An explicit fiscal reporting instant is required.");
  return new Date(milliseconds + PHT_OFFSET_MILLISECONDS).toISOString().slice(0, 19).replace("T", " ");
}

export function formatPhtInputValue(value: string) {
  return formatPhtInstant(value).replace(" ", "T");
}

export function phtInputToUtc(value: string) {
  const match = PHT_INPUT_PATTERN.exec(value.trim());
  if (!match) throw new Error("A valid PHT date and time is required.");
  const [, yearText, monthText, dayText, hourText, minuteText, secondText = "0"] = match;
  const [year, month, day, hour, minute, second] = [yearText, monthText, dayText, hourText, minuteText, secondText].map(Number);
  const phtAsUtc = Date.UTC(year, month - 1, day, hour, minute, second);
  const calendarCheck = new Date(phtAsUtc);
  if (calendarCheck.getUTCFullYear() !== year || calendarCheck.getUTCMonth() !== month - 1 ||
      calendarCheck.getUTCDate() !== day || calendarCheck.getUTCHours() !== hour ||
      calendarCheck.getUTCMinutes() !== minute || calendarCheck.getUTCSeconds() !== second) {
    throw new Error("A valid PHT date and time is required.");
  }
  return new Date(phtAsUtc - PHT_OFFSET_MILLISECONDS).toISOString().replace(".000Z", "Z");
}

function phtRangeToUtc(start: string, end: string) {
  if (!start || !end) return undefined;
  try {
    const utcStart = phtInputToUtc(start);
    const utcEnd = phtInputToUtc(end);
    return Date.parse(utcStart) < Date.parse(utcEnd) ? { start: utcStart, end: utcEnd } : undefined;
  } catch {
    return undefined;
  }
}

function money(value: number, currency: string) {
  return new Intl.NumberFormat("en-PH", { style: "currency", currency }).format(value / 100);
}
