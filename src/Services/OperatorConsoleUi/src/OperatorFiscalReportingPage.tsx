import { useEffect, useState } from "react";
import {
  fiscalReportingPermissions,
  type AuditTrailFilters,
  type AuditTrailReport,
  type AnnexE1Workbook,
  type BirSalesSummary,
  type CloseableFiscalBusinessDate,
  type EjInvoice,
  type FiscalReportingSite,
  type History,
  type MonthlyESalesReadResult,
  type OperatorFiscalReportingClient,
  type StatutorySalesBookReport,
  type StatutorySalesBookType
} from "./fiscalReporting";

export function OperatorFiscalReportingPage({ client, siteReferences, permissions }: {
  client: OperatorFiscalReportingClient;
  siteReferences: readonly string[];
  permissions: readonly string[];
}) {
  const [sites, setSites] = useState<FiscalReportingSite[]>();
  const [siteId, setSiteId] = useState("");
  const [tab, setTab] = useState<"ej" | "audit" | "x" | "z" | "bir" | "esales">("ej");
  const [start, setStart] = useState("");
  const [end, setEnd] = useState("");
  const [search, setSearch] = useState("");
  const [ej, setEj] = useState<EjInvoice[]>();
  const [auditTrail, setAuditTrail] = useState<AuditTrailReport>();
  const [auditAction, setAuditAction] = useState("");
  const [auditActor, setAuditActor] = useState("");
  const [auditResult, setAuditResult] = useState("");
  const [auditReference, setAuditReference] = useState("");
  const [x, setX] = useState<History>();
  const [z, setZ] = useState<History>();
  const [closeablePeriods, setCloseablePeriods] = useState<CloseableFiscalBusinessDate[]>([]);
  const [selectedZReference, setSelectedZReference] = useState("");
  const [summaryId, setSummaryId] = useState("");
  const [summary, setSummary] = useState<BirSalesSummary>();
  const [annexMonth, setAnnexMonth] = useState("");
  const [annexWorkbook, setAnnexWorkbook] = useState<AnnexE1Workbook>();
  const [statutoryBooks, setStatutoryBooks] = useState<Partial<Record<StatutorySalesBookType, StatutorySalesBookReport>>>({});
  const [statutoryBookErrors, setStatutoryBookErrors] = useState<Partial<Record<StatutorySalesBookType, string>>>({});
  const [statutoryBookLoading, setStatutoryBookLoading] = useState<StatutorySalesBookType>();
  const [annexLookupComplete, setAnnexLookupComplete] = useState(false);
  const [annexLoading, setAnnexLoading] = useState(false);
  const [monthlyESalesMonth, setMonthlyESalesMonth] = useState("");
  const [monthlyESalesResult, setMonthlyESalesResult] = useState<MonthlyESalesReadResult>();
  const [monthlyESalesError, setMonthlyESalesError] = useState<string>();
  const [birError, setBirError] = useState<string>();
  const [annexError, setAnnexError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [catalogError, setCatalogError] = useState<string>();
  const [ejError, setEjError] = useState<string>();
  const [auditError, setAuditError] = useState<string>();
  const [xHistoryError, setXHistoryError] = useState<string>();
  const [xGenerateError, setXGenerateError] = useState<string>();
  const [zHistoryError, setZHistoryError] = useState<string>();
  const [closeableError, setCloseableError] = useState<string>();
  const [closeError, setCloseError] = useState<string>();
  const [batchCloseError, setBatchCloseError] = useState<string>();
  const [message, setMessage] = useState<string>();
  const [birCompletionWarning, setBirCompletionWarning] = useState<string>();
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

  async function loadAuditTrail() {
    if (!siteId) return;
    const range = phtRangeToUtc(start, end);
    setAuditError(undefined);
    setAuditTrail(undefined);
    if (!range) {
      setAuditError("Enter a valid PHT period start and end. The start must be before the end.");
      return;
    }
    setBusy(true);
    try {
      setAuditTrail(await client.readAuditTrail(siteId, range.start, range.end, auditFilters()));
    } catch (cause) {
      setAuditError(operationMessage(cause, "Audit Trail could not be loaded."));
    } finally {
      setBusy(false);
    }
  }

  function auditFilters(): AuditTrailFilters {
    return { actionCode: auditAction, actor: auditActor, result: auditResult, reference: auditReference };
  }

  async function loadReportingData() {
    if (!siteId) return;
    setBusy(true);
    setMessage(undefined);
    setBirCompletionWarning(undefined);
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
  useEffect(() => {
    const committed = z?.readings.find((reading) => reading.reportStatus === "COMMITTED");
    setSelectedZReference(committed?.reportReference ?? "");
    setAnnexMonth(committed?.businessDayDate.slice(0, 7) ?? "");
    setMonthlyESalesMonth(committed?.businessDayDate.slice(0, 7) ?? "");
  }, [z]);
  useEffect(() => {
    const match = /^(\d{4})-(\d{2})$/.exec(annexMonth);
    setAnnexWorkbook(undefined);
    setStatutoryBooks({});
    setStatutoryBookErrors({});
    setStatutoryBookLoading(undefined);
    setAnnexError(undefined);
    setAnnexLookupComplete(false);
    if (!siteId || !match) {
      setAnnexLoading(false);
      return;
    }
    let active = true;
    setAnnexLoading(true);
    void client.readCurrentAnnexE1(siteId, Number(match[1]), Number(match[2]))
      .then((workbook) => {
        if (!active) return;
        setAnnexWorkbook(workbook);
        setAnnexLookupComplete(true);
      })
      .catch((cause) => {
        if (!active) return;
        setAnnexError(operationMessage(cause, "Annex E-1 current workbook could not be loaded."));
        setAnnexLookupComplete(true);
      })
      .finally(() => {
        if (active) setAnnexLoading(false);
      });
    return () => { active = false; };
  }, [client, siteId, annexMonth]);

  function changeSite(nextSiteId: string) {
    setStart("");
    setEnd("");
    setEj(undefined);
    setAuditTrail(undefined);
    setAuditError(undefined);
    setX(undefined);
    setZ(undefined);
    setCloseablePeriods([]);
    setSelectedZReference("");
    setSummaryId("");
    setSummary(undefined);
    setAnnexMonth("");
    setAnnexWorkbook(undefined);
    setAnnexLookupComplete(false);
    setAnnexLoading(false);
    setMonthlyESalesMonth("");
    setMonthlyESalesResult(undefined);
    setMonthlyESalesError(undefined);
    setBirError(undefined);
    setAnnexError(undefined);
    setEjError(undefined);
    setXHistoryError(undefined);
    setXGenerateError(undefined);
    setZHistoryError(undefined);
    setCloseableError(undefined);
    setCloseError(undefined);
    setBatchCloseError(undefined);
    setBirCompletionWarning(undefined);
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
    setBirCompletionWarning(undefined);
    try {
      if (closeConfirmation.kind === "single") {
        const closingPeriod = closeConfirmation.period;
        const closeResult = await client.closeBusinessDate(siteId, closingPeriod);
        const [nextCloseable, nextHistory] = await Promise.all([client.readCloseablePeriods(siteId), client.readZ(siteId)]);
        if (nextCloseable.fiscalBusinessDates.some((period) => period.fiscalReportingPeriodId === closingPeriod.fiscalReportingPeriodId) ||
            nextHistory.readings.filter((reading) => reading.fiscalReportingPeriodId === closingPeriod.fiscalReportingPeriodId).length !== 1) {
          throw new Error("The authoritative fiscal close transition could not be confirmed.");
        }
        setCloseablePeriods(nextCloseable.fiscalBusinessDates);
        setZ(nextHistory);
        setMessage(`Fiscal Business Date ${closingPeriod.fiscalBusinessDate} closed for ${selectedSiteName}.`);
        if (closeResult.birSalesSummaryCompletion?.status === "PENDING") {
          setBirCompletionWarning("Fiscal Business Date closed and Z Reading committed. Daily BIR Sales Summary is pending and can be retried from Daily BIR Sales Summary.");
        }
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
        if (result.birSalesSummaryPendingCount > 0) {
          setBirCompletionWarning(`${result.birSalesSummaryPendingCount} Daily BIR Sales Summary operation(s) are pending and can be retried from Daily BIR Sales Summary.`);
        }
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

  async function generateBirSalesSummary() {
    const reading = z?.readings.find((item) => item.reportReference === selectedZReference && item.reportStatus === "COMMITTED");
    if (!reading) {
      setBirError("The Daily BIR Sales Summary is unavailable until the governing Z Reading is committed.");
      return;
    }
    setBusy(true);
    setBirError(undefined);
    try {
      const value = await client.generateBirSalesSummary(siteId, reading);
      setSummary(value);
      setSummaryId(value.birSalesSummaryReportId);
    } catch (cause) {
      setBirError(operationMessage(cause, "Daily BIR Sales Summary generation failed safely."));
    } finally {
      setBusy(false);
    }
  }

  async function readBirSalesSummary() {
    if (!summaryId.trim()) return;
    setBusy(true);
    setBirError(undefined);
    try {
      setSummary(await client.readBirSalesSummary(siteId, summaryId.trim()));
    } catch (cause) {
      setSummary(undefined);
      setBirError(operationMessage(cause, "No Daily BIR Sales Summary has been generated for this fiscal business date."));
    } finally {
      setBusy(false);
    }
  }

  async function generateAnnexE1() {
    const match = /^(\d{4})-(\d{2})$/.exec(annexMonth);
    if (!match) return;
    setBusy(true);
    setAnnexError(undefined);
    try {
      setAnnexWorkbook(await client.generateAnnexE1(siteId, Number(match[1]), Number(match[2])));
      setAnnexLookupComplete(true);
    } catch (cause) {
      setAnnexWorkbook(undefined);
      setAnnexError(operationMessage(cause, "Annex E-1 is unavailable because its governed accounting prerequisites are incomplete."));
    } finally {
      setBusy(false);
    }
  }

  function changeAnnexMonth(value: string) {
    setAnnexWorkbook(undefined);
    setAnnexError(undefined);
    setAnnexLookupComplete(false);
    setStatutoryBooks({});
    setStatutoryBookErrors({});
    setStatutoryBookLoading(undefined);
    setAnnexMonth(value);
  }

  async function readStatutorySalesBook(type: StatutorySalesBookType) {
    const match = /^(\d{4})-(\d{2})$/.exec(annexMonth);
    if (!match) {
      setStatutoryBookErrors((current) => ({ ...current, [type]: "Select a valid reporting month." }));
      return;
    }
    setStatutoryBookLoading(type);
    setStatutoryBookErrors((current) => ({ ...current, [type]: undefined }));
    try {
      const report = await client.readStatutorySalesBook(siteId, type, Number(match[1]), Number(match[2]));
      setStatutoryBooks((current) => ({ ...current, [type]: report }));
    } catch (cause) {
      setStatutoryBooks((current) => ({ ...current, [type]: undefined }));
      setStatutoryBookErrors((current) => ({
        ...current,
        [type]: operationMessage(cause, `Annex ${type.toUpperCase()} could not be loaded.`),
      }));
    } finally {
      setStatutoryBookLoading(undefined);
    }
  }

  async function readMonthlyESales() {
    const match = /^(\d{4})-(\d{2})$/.exec(monthlyESalesMonth);
    if (!match) return;
    setBusy(true);
    setMonthlyESalesResult(undefined);
    setMonthlyESalesError(undefined);
    try {
      setMonthlyESalesResult(await client.readMonthlyESales(siteId, Number(match[1]), Number(match[2])));
    } catch (cause) {
      setMonthlyESalesError(operationMessage(cause, "Monthly eSales could not be loaded."));
    } finally {
      setBusy(false);
    }
  }

  function changeMonthlyESalesMonth(value: string) {
    setMonthlyESalesResult(undefined);
    setMonthlyESalesError(undefined);
    setMonthlyESalesMonth(value);
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
    <div className="pageTitle"><div><p className="eyebrow">Site POS authority</p><h2 id="operator-fiscal-reporting-title">Fiscal Reporting</h2><p>Electronic Journal, Audit Trail, X Reading, Z Reading, Daily BIR Sales Summary, and Monthly eSales are retrieved through Central PMS from the bound Site POS Server.</p></div><span className="statusPill">Site scoped</span></div>
    {sites.length === 1
      ? <div className="siteSelector" aria-label="Authorized Site"><span>Authorized Site</span><strong>{sites[0].siteName}</strong></div>
      : <label className="siteSelector">Authorized Site<select value={siteId} onChange={(event) => changeSite(event.target.value)}>{sites.map((site) => <option key={site.siteId} value={site.siteId}>{site.siteName}</option>)}</select></label>}
    <div className="reportTabs" role="tablist" aria-label="Fiscal report type"><button type="button" role="tab" aria-selected={tab === "ej"} onClick={() => setTab("ej")}>Electronic Journal</button><button type="button" role="tab" aria-selected={tab === "audit"} onClick={() => setTab("audit")}>Audit Trail</button><button type="button" role="tab" aria-selected={tab === "x"} onClick={() => setTab("x")}>X Reading</button><button type="button" role="tab" aria-selected={tab === "z"} onClick={() => setTab("z")}>Z Reading</button><button type="button" role="tab" aria-selected={tab === "bir"} onClick={() => setTab("bir")}>Daily BIR Sales Summary</button><button type="button" role="tab" aria-selected={tab === "esales"} onClick={() => setTab("esales")}>Monthly eSales</button></div>
    {busy && <p role="status">Loading authoritative POS reporting data...</p>}
    {message && <p className="successMessage" role="status">{message}</p>}
    {birCompletionWarning && <p className="stateMessage warning" role="status">{birCompletionWarning}</p>}
    {tab === "ej" && (has(fiscalReportingPermissions.ejRead) ? <div className="fiscalPane" role="tabpanel"><h3>Electronic Journal</h3><p>Exact visible Sales Invoice text supplied by POS authority. The browser does not create the journal artifact.</p><div className="reportFilters"><label>Period start (PHT)<input type="datetime-local" step="1" aria-label="EJ period start PHT" value={start} onChange={(e) => setStart(e.target.value)} /></label><label>Period end (PHT)<input type="datetime-local" step="1" aria-label="EJ period end PHT" value={end} onChange={(e) => setEnd(e.target.value)} /></label><label>Ticket, Plate or SI Number<input aria-label="Search Electronic Journal" value={search} onChange={(e) => setSearch(e.target.value)} /></label><button type="button" onClick={() => void loadEj(true)}>View journal</button>{has(fiscalReportingPermissions.ejExport) && !!ej?.length && downloadRange && <a className="primaryButton" href={client.ejUrl(siteId, downloadRange.start, downloadRange.end, search)}>Download authoritative .txt</a>}</div>{ejError && <p className="errorMessage" role="alert">{ejError}</p>}{ej?.length === 0 && <p role="status">No Sales Invoice activity exists for this period.</p>}{ej?.map((invoice) => <article className="invoiceText" key={invoice.fiscalDocumentId}><header><strong>{invoice.fiscalDocumentNumber}</strong><span>{formatPhtInstant(invoice.issuedAt)} PHT</span></header><pre>{invoice.printableText}</pre></article>)}</div> : <p className="stateMessage warning">Electronic Journal permission is required.</p>)}
    {tab === "audit" && (has(fiscalReportingPermissions.zRead) ? <AuditTrailPanel
      start={start} end={end} onStart={setStart} onEnd={setEnd}
      action={auditAction} onAction={setAuditAction} actor={auditActor} onActor={setAuditActor}
      result={auditResult} onResult={setAuditResult} reference={auditReference} onReference={setAuditReference}
      report={auditTrail} error={auditError} busy={busy} onRead={() => void loadAuditTrail()}
      downloadUrl={(format) => downloadRange ? client.auditTrailUrl(siteId, downloadRange.start, downloadRange.end, format, auditFilters()) : "#"}
    /> : <p className="stateMessage warning">Fiscal reporting read permission is required to access Audit Trail.</p>)}
    {tab === "x" && <ReadingPanel kind="X" history={x} error={xHistoryError} actionError={xGenerateError} canRead={has(fiscalReportingPermissions.xRead)} canGenerate={has(fiscalReportingPermissions.xGenerate)} onGenerate={generateX} download={(reference, format) => client.readingUrl(siteId, "x", reference, format)} />}
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
      <ReadingPanel kind="Z" history={z} error={zHistoryError} canRead={has(fiscalReportingPermissions.zRead)} canGenerate={false} download={(reference, format) => client.readingUrl(siteId, "z", reference, format)} />
    </>}
    {tab === "bir" && (has(fiscalReportingPermissions.zRead)
      ? <BirSalesSummaryPanel
          zHistory={z}
          selectedZReference={selectedZReference}
          onSelectZ={setSelectedZReference}
          summaryId={summaryId}
          onSummaryIdChange={setSummaryId}
          summary={summary}
          error={birError}
          canGenerate={has(fiscalReportingPermissions.zGenerate)}
          busy={busy}
          onGenerate={() => void generateBirSalesSummary()}
          onRead={() => void readBirSalesSummary()}
          summaryUrl={(format) => summary ? client.birSalesSummaryUrl(siteId, summary.birSalesSummaryReportId, format) : "#"}
          annexMonth={annexMonth}
          onAnnexMonthChange={changeAnnexMonth}
          annexWorkbook={annexWorkbook}
          annexLookupComplete={annexLookupComplete}
          annexLoading={annexLoading}
          annexError={annexError}
          onGenerateAnnex={() => void generateAnnexE1()}
          annexXlsxUrl={annexWorkbook ? client.annexE1Url(siteId, annexWorkbook.workbookId, "xlsx") : "#"}
          annexPdfUrl={annexWorkbook ? client.annexE1Url(siteId, annexWorkbook.workbookId, "pdf") : "#"}
          statutoryBooks={statutoryBooks}
          statutoryBookErrors={statutoryBookErrors}
          statutoryBookLoading={statutoryBookLoading}
          onReadStatutoryBook={(type) => void readStatutorySalesBook(type)}
          statutoryBookUrl={(type, format) => {
            const match = /^(\d{4})-(\d{2})$/.exec(annexMonth);
            return match ? client.statutorySalesBookUrl(siteId, type, Number(match[1]), Number(match[2]), format) : "#";
          }}
        />
      : <p className="stateMessage warning">Z Reading permission is required to access Daily BIR Sales Summary.</p>)}
    {tab === "esales" && (has(fiscalReportingPermissions.zRead)
      ? <MonthlyESalesPanel
          month={monthlyESalesMonth}
          onMonthChange={changeMonthlyESalesMonth}
          result={monthlyESalesResult}
          error={monthlyESalesError}
          busy={busy}
          onRead={() => void readMonthlyESales()}
          downloadUrl={(format) => {
            const match = /^(\d{4})-(\d{2})$/.exec(monthlyESalesMonth);
            return match ? client.monthlyESalesUrl(siteId, Number(match[1]), Number(match[2]), format) : "#";
          }}
        />
      : <p className="stateMessage warning">Z Reading permission is required to access Monthly eSales.</p>)}
  </section>;
}

function AuditTrailPanel({ start, end, onStart, onEnd, action, onAction, actor, onActor, result, onResult,
  reference, onReference, report, error, busy, onRead, downloadUrl }: {
  start: string;
  end: string;
  onStart: (value: string) => void;
  onEnd: (value: string) => void;
  action: string;
  onAction: (value: string) => void;
  actor: string;
  onActor: (value: string) => void;
  result: string;
  onResult: (value: string) => void;
  reference: string;
  onReference: (value: string) => void;
  report?: AuditTrailReport;
  error?: string;
  busy: boolean;
  onRead: () => void;
  downloadUrl: (format: "pdf" | "csv" | "json") => string;
}) {
  return <div className="fiscalPane" role="tabpanel">
    <div className="readingTitle"><div><h3>Audit Trail</h3><p>POS-owned actor, action, access, and governed change evidence for a bounded Site interval.</p></div></div>
    <div className="reportFilters">
      <label>From (PHT)<input type="datetime-local" step="1" aria-label="Audit Trail from PHT" value={start} onChange={(event) => onStart(event.target.value)} /></label>
      <label>To (PHT)<input type="datetime-local" step="1" aria-label="Audit Trail to PHT" value={end} onChange={(event) => onEnd(event.target.value)} /></label>
      <label>Action Category<select aria-label="Audit Trail action category" value={action} onChange={(event) => onAction(event.target.value)}><option value="">All actions</option>{report?.actionCodes.map((item) => <option key={item.code} value={item.code}>{item.displayName}</option>)}</select></label>
      <label>User / Actor<input aria-label="Audit Trail user or actor" value={actor} onChange={(event) => onActor(event.target.value)} /></label>
      <label>Result<select aria-label="Audit Trail result" value={result} onChange={(event) => onResult(event.target.value)}><option value="">All results</option><option value="SUCCESS">Success</option><option value="ALLOWED">Allowed</option><option value="DENIED">Denied</option><option value="FAILED">Failed</option></select></label>
      <label>SI / Reference<input aria-label="Audit Trail SI or reference" value={reference} onChange={(event) => onReference(event.target.value)} /></label>
      <button type="button" onClick={onRead} disabled={busy}>View Audit Trail</button>
      {report && <><a className="primaryButton" href={downloadUrl("pdf")}>Download PDF</a><a className="primaryButton" href={downloadUrl("csv")}>Download CSV</a><a className="primaryButton" href={downloadUrl("json")}>Download JSON</a></>}
    </div>
    {error && <p className="errorMessage" role="alert">{error}</p>}
    {report?.rows.length === 0 && <p role="status">No durable audit activity exists for this bounded interval.</p>}
    {report && report.rows.length > 0 && <div className="tableViewport"><table aria-label="Audit Trail"><thead><tr><th>Date</th><th>Terminal</th><th>UserID</th><th>User Role</th><th>Action Category</th><th>Action Description</th><th>SI No.</th><th>Before Value</th><th>After Value</th><th>Reason/Remarks</th></tr></thead><tbody>{report.rows.map((row) => <tr key={row.auditReference}><td>{formatPhtInstant(row.occurredAt)} PHT</td><td>{row.terminal ?? ""}</td><td>{row.userId ?? ""}</td><td>{row.userRole ?? ""}</td><td>{row.actionCategory}</td><td><pre className="auditDescription">{row.actionDescription}</pre></td><td>{row.salesInvoiceNumber ?? ""}</td><td>{row.beforeValue ?? ""}</td><td>{row.afterValue ?? ""}</td><td>{row.reasonRemarks ?? "-"}</td></tr>)}</tbody></table></div>}
    {report && <details className="auditLegend"><summary>Action Code Legend</summary><dl>{report.actionCodes.map((item) => <div key={item.code}><dt>{item.code} - {item.displayName}</dt><dd>{item.definition}</dd></div>)}</dl></details>}
  </div>;
}

function MonthlyESalesPanel({ month, onMonthChange, result, error, busy, onRead, downloadUrl }: {
  month: string;
  onMonthChange: (value: string) => void;
  result?: MonthlyESalesReadResult;
  error?: string;
  busy: boolean;
  onRead: () => void;
  downloadUrl: (format: "json" | "csv" | "pdf") => string;
}) {
  const report = result?.report;
  return <div className="fiscalPane" role="tabpanel">
    <div className="readingTitle"><div><h3>Monthly eSales</h3><p>POS-authoritative monthly sales by governed Machine Identification Number. This report prepares an export and does not submit to BIR.</p></div></div>
    <div className="reportFilters">
      <label>Calendar month<input type="month" aria-label="Monthly eSales calendar month" value={month} onChange={(event) => onMonthChange(event.target.value)} /></label>
      <button type="button" onClick={onRead} disabled={busy || !month}>View Monthly eSales</button>
      {report && <><a className="primaryButton" href={downloadUrl("pdf")}>Download PDF</a><a className="primaryButton" href={downloadUrl("csv")}>Download CSV</a><a className="primaryButton" href={downloadUrl("json")}>Download JSON</a></>}
    </div>
    {error && <p className="errorMessage" role="alert">{error}</p>}
    {result?.status === "INCOMPLETE" && <p className="stateMessage warning" role="status">{result.message}</p>}
    {report && <>
      <dl className="periodCard" aria-label="Monthly eSales filing metadata">
        <SummaryValue label="Reporting Month" value={`${String(report.calendarMonth).padStart(2, "0")}/${report.calendarYear}`} />
        <SummaryValue label="Taxpayer TIN" value={report.taxpayerTin || "Unavailable"} />
        <SummaryValue label="Currency" value={report.currencyCode} />
        <SummaryValue label="Coverage" value={report.coverageStatus === "MONTH_TO_DATE" ? "Month to date" : "Complete month"} />
        <SummaryValue label="Covered Business Dates" value={`${report.firstCoveredBusinessDate} to ${report.lastCoveredBusinessDate}`} />
        <SummaryValue label="As Of" value={formatPhtInstant(report.coverageAsOf) + " PHT"} />
      </dl>
      <p className="stateMessage" role="status">{report.coverageMessage}</p>
      <div className="tableViewport"><table aria-label="Monthly eSales values"><thead><tr><th>MIN</th><th>Transactions</th><th>Gross Monthly Sales</th><th>End Range</th><th>VATable Sales</th><th>VAT</th><th>VAT Exempt Sales</th><th>Zero-Rated Sales</th><th>Source Periods</th><th>Business Dates</th></tr></thead><tbody>{report.rows.map((row) => <tr key={row.machineIdentificationNumber}><td>{row.machineIdentificationNumber}</td><td>{row.transactionCount}</td><td>{report.currencyCode} {row.monthlySalesDisplay}</td><td>{row.endingSalesInvoiceReference ?? ""}</td><td>{money(row.vatableSalesAmountMinorUnits, report.currencyCode)}</td><td>{money(row.vatAmountMinorUnits, report.currencyCode)}</td><td>{money(row.vatExemptSalesAmountMinorUnits, report.currencyCode)}</td><td>{money(row.zeroRatedSalesAmountMinorUnits, report.currencyCode)}</td><td>{row.sourcePeriodCount}</td><td>{row.firstBusinessDate} to {row.lastBusinessDate}</td></tr>)}</tbody></table></div>
    </>}
  </div>;
}

function BirSalesSummaryPanel({ zHistory, selectedZReference, onSelectZ, summaryId, onSummaryIdChange, summary, error,
  canGenerate, busy, onGenerate, onRead, summaryUrl, annexMonth, onAnnexMonthChange, annexWorkbook, annexLookupComplete, annexLoading, annexError,
  onGenerateAnnex, annexXlsxUrl, annexPdfUrl, statutoryBooks, statutoryBookErrors, statutoryBookLoading,
  onReadStatutoryBook, statutoryBookUrl }: {
  zHistory?: History;
  selectedZReference: string;
  onSelectZ: (value: string) => void;
  summaryId: string;
  onSummaryIdChange: (value: string) => void;
  summary?: BirSalesSummary;
  error?: string;
  canGenerate: boolean;
  busy: boolean;
  onGenerate: () => void;
  onRead: () => void;
  summaryUrl: (format: "json" | "csv" | "pdf") => string;
  annexMonth: string;
  onAnnexMonthChange: (value: string) => void;
  annexWorkbook?: AnnexE1Workbook;
  annexLookupComplete: boolean;
  annexLoading: boolean;
  annexError?: string;
  onGenerateAnnex: () => void;
  annexXlsxUrl: string;
  annexPdfUrl: string;
  statutoryBooks: Partial<Record<StatutorySalesBookType, StatutorySalesBookReport>>;
  statutoryBookErrors: Partial<Record<StatutorySalesBookType, string>>;
  statutoryBookLoading?: StatutorySalesBookType;
  onReadStatutoryBook: (type: StatutorySalesBookType) => void;
  statutoryBookUrl: (type: StatutorySalesBookType, format: "xlsx" | "pdf") => string;
}) {
  const committedReadings = zHistory?.readings.filter((reading) => reading.reportStatus === "COMMITTED") ?? [];
  return <div className="fiscalPane" role="tabpanel">
    <div className="readingTitle"><div><h3>Daily BIR Sales Summary</h3><p>Authoritative daily sales summary governed by a closed Fiscal Business Date and its committed Z Reading.</p></div></div>
    {committedReadings.length === 0
      ? <p role="status">{zHistory?.currentPeriod
          ? "The Daily BIR Sales Summary can be generated after the fiscal business date has been closed and its Z Reading has been committed."
          : "No closed fiscal business date is available."}</p>
      : <div className="reportFilters">
          <label>Closed Fiscal Business Date<select aria-label="Closed Fiscal Business Date" value={selectedZReference} onChange={(event) => onSelectZ(event.target.value)}>{committedReadings.map((reading) => <option key={reading.reportReference} value={reading.reportReference}>{reading.businessDayDate} - {reading.reportReference}</option>)}</select></label>
          <button type="button" onClick={onGenerate} disabled={!canGenerate || busy || !selectedZReference}>Generate / View Summary</button>
        </div>}
    {!canGenerate && <p className="stateMessage warning">Z Reading generation authority is required to generate the Daily BIR Sales Summary.</p>}
    <div className="reportFilters">
      <label>Existing Daily Summary ID<input aria-label="Existing Daily BIR Sales Summary ID" value={summaryId} onChange={(event) => onSummaryIdChange(event.target.value)} /></label>
      <button type="button" onClick={onRead} disabled={busy || !summaryId.trim()}>View generated summary</button>
    </div>
    {error && <p className="errorMessage" role="alert">{error}</p>}
    {summary && <>
      <dl className="periodCard" aria-label="Daily BIR Sales Summary values">
        <SummaryValue label="Business Date" value={summary.businessDayDate} />
        <SummaryValue label="Summary ID" value={summary.birSalesSummaryReportId} />
        <SummaryValue label="Support Reference" value={summary.supportReference} />
        <SummaryValue label="Governing Z Reading" value={`${summary.governingZReadingReference} (${summary.reportStatus})`} />
        <SummaryValue label="Beginning SI Number" value={summary.beginningSalesInvoiceReference ?? ""} />
        <SummaryValue label="Ending SI Number" value={summary.endingSalesInvoiceReference ?? ""} />
        <SummaryValue label="Transaction Count" value={String(summary.transactionCount)} />
        <SummaryValue label="Previous Grand Total Amount" value={money(summary.counterSnapshot.previousGrandTotalAmountMinorUnits, summary.currencyCode)} />
        <SummaryValue label="Present Grand Total Amount" value={money(summary.counterSnapshot.resultingGrandTotalAmountMinorUnits, summary.currencyCode)} />
        <SummaryValue label="Gross Sales" value={money(summary.amounts.grossSalesAmountMinorUnits, summary.currencyCode)} />
        <SummaryValue label="VATable Sales" value={money(summary.amounts.vatableSalesAmountMinorUnits, summary.currencyCode)} />
        <SummaryValue label="VAT Amount" value={money(summary.amounts.vatAmountMinorUnits, summary.currencyCode)} />
        <SummaryValue label="VAT-Exempt Sales" value={money(summary.amounts.vatExemptSalesAmountMinorUnits, summary.currencyCode)} />
        <SummaryValue label="Zero-Rated Sales" value={money(summary.amounts.zeroRatedSalesAmountMinorUnits, summary.currencyCode)} />
        <SummaryValue label="Total Discounts" value={money(summary.amounts.discountAmountMinorUnits, summary.currencyCode)} />
        <SummaryValue label="Senior Citizen Discount" value={money(summary.amounts.seniorCitizenDiscountAmountMinorUnits, summary.currencyCode)} />
        <SummaryValue label="PWD Discount" value={money(summary.amounts.pwdDiscountAmountMinorUnits, summary.currencyCode)} />
        <SummaryValue label="Other Statutory Discount" value={money(summary.amounts.otherStatutoryDiscountAmountMinorUnits, summary.currencyCode)} />
        <SummaryValue label="VAT Exemption Adjustment" value={money(summary.amounts.vatExemptionAmountMinorUnits, summary.currencyCode)} />
        <SummaryValue label="Void Amount" value={money(summary.amounts.voidAmountMinorUnits, summary.currencyCode)} />
        <SummaryValue label="Refund Amount" value={money(summary.amounts.refundAmountMinorUnits, summary.currencyCode)} />
        <SummaryValue label="Return Amount" value={money(summary.amounts.returnAmountMinorUnits, summary.currencyCode)} />
        <SummaryValue label="Other Adjustment Amount" value={money(summary.amounts.adjustmentAmountMinorUnits, summary.currencyCode)} />
        <SummaryValue label="Net Sales" value={money(summary.amounts.netSalesAmountMinorUnits, summary.currencyCode)} />
        <SummaryValue label="Reset Counter" value={String(summary.counterSnapshot.resultingResetCounterValue)} />
        <SummaryValue label="Z-Counter" value={String(summary.counterSnapshot.resultingZCounterValue)} />
        <SummaryValue label="Currency" value={summary.currencyCode} />
        <SummaryValue label="Generated (PHT)" value={formatPhtInstant(summary.generatedAt)} />
      </dl>
      <div className="reportFilters"><a className="primaryButton" href={summaryUrl("pdf")}>Download PDF</a><a className="primaryButton" href={summaryUrl("csv")}>Download CSV</a><a className="primaryButton" href={summaryUrl("json")}>Download JSON</a></div>
    </>}
    <div className="reportFilters"><label>Annex reporting month<input type="month" aria-label="Annex reporting month" value={annexMonth} onChange={(event) => onAnnexMonthChange(event.target.value)} /></label></div>
    <section className="annexReportSection" aria-labelledby="annex-e1-title"><h4 id="annex-e1-title">Annex E-1 - BIR Sales Summary Report</h4><div className="reportFilters">{annexLookupComplete && !annexWorkbook && !annexError && <button type="button" onClick={onGenerateAnnex} disabled={!canGenerate || busy || annexLoading || !annexMonth}>Generate Annex E-1</button>}{annexWorkbook && <><a className="primaryButton" href={annexPdfUrl}>Download PDF</a><a className="primaryButton" href={annexXlsxUrl}>Download XLSX</a></>}</div>{annexLoading && <p role="status">Loading current Annex E-1 workbook...</p>}{annexError && <p className="errorMessage" role="alert">{annexError}</p>}{annexLookupComplete && !annexWorkbook && !annexError && <p role="status">No Annex E-1 workbook has been generated for this month.</p>}{annexWorkbook && <p role="status">Current Annex E-1: {annexWorkbook.annexEReference}, revision {annexWorkbook.revision}, generated {formatPhtInstant(annexWorkbook.generatedAt)} PHT ({annexWorkbook.fileName}).</p>}</section>
    <StatutorySalesBookPanel type="e2" report={statutoryBooks.e2} error={statutoryBookErrors.e2} loading={statutoryBookLoading === "e2"} month={annexMonth} busy={busy} onRead={() => onReadStatutoryBook("e2")} downloadUrl={(format) => statutoryBookUrl("e2", format)} />
    <StatutorySalesBookPanel type="e3" report={statutoryBooks.e3} error={statutoryBookErrors.e3} loading={statutoryBookLoading === "e3"} month={annexMonth} busy={busy} onRead={() => onReadStatutoryBook("e3")} downloadUrl={(format) => statutoryBookUrl("e3", format)} />
  </div>;
}

function StatutorySalesBookPanel({ type, report, error, loading, month, busy, onRead, downloadUrl }: {
  type: StatutorySalesBookType;
  report?: StatutorySalesBookReport;
  error?: string;
  loading: boolean;
  month: string;
  busy: boolean;
  onRead: () => void;
  downloadUrl: (format: "xlsx" | "pdf") => string;
}) {
  const annex = type === "e2" ? "E-2" : "E-3";
  const title = type === "e2" ? "Senior Citizen Sales Book/Report" : "Persons with Disability Sales Book/Report";
  return <section className="annexReportSection" aria-labelledby={`annex-${type}-title`}>
    <div className="readingTitle"><div><h4 id={`annex-${type}-title`}>Annex {annex} - {title}</h4></div>{report && <span className="statusPill">{report.summary.reportStatus}</span>}</div>
    <div className="reportFilters"><button type="button" onClick={onRead} disabled={busy || loading || !month}>{loading ? "Loading report" : `View Annex ${annex}`}</button>{report && <><a className="primaryButton" href={downloadUrl("xlsx")}>Download XLSX</a><a className="primaryButton" href={downloadUrl("pdf")}>Download PDF</a></>}</div>
    {error && <p className="errorMessage" role="alert">{error}</p>}
    {report?.summary.warnings.map((warning) => <p className="stateMessage warning" role="status" key={warning}>{warning}</p>)}
    {report?.summary.isEmpty && <p role="status">No qualifying transactions exist for this Site POS Server and reporting month. All totals are {money(0, report.summary.currencyCode)}.</p>}
    {report && !report.summary.isEmpty && <StatutorySalesBookTable report={report} />}
    {report && <dl className="periodCard" aria-label={`Annex ${annex} totals`}>
      <SummaryValue label="POS Server" value={report.summary.posServerCode} />
      <SummaryValue label="POS Serial Number" value={report.header.serialNumber} />
      <SummaryValue label="Machine Identification Number" value={report.header.machineIdentificationNumber} />
      <SummaryValue label="Transactions" value={String(report.summary.qualifyingTransactionCount)} />
      <SummaryValue label="Fiscal Periods" value={`${report.summary.committedZPeriodCount}/${report.summary.scopedFiscalPeriodCount} finalized`} />
      <SummaryValue label="Sales Including VAT" value={money(report.summary.salesInclusiveVatMinorUnits, report.summary.currencyCode)} />
      <SummaryValue label="VAT Amount" value={money(report.summary.vatAmountMinorUnits, report.summary.currencyCode)} />
      <SummaryValue label="VAT Exempt Sales" value={money(report.summary.vatExemptSalesMinorUnits, report.summary.currencyCode)} />
      <SummaryValue label="5% Discount" value={money(report.summary.discount5MinorUnits, report.summary.currencyCode)} />
      <SummaryValue label="20% Discount" value={money(report.summary.discount20MinorUnits, report.summary.currencyCode)} />
      <SummaryValue label="Net Sales" value={money(report.summary.netSalesMinorUnits, report.summary.currencyCode)} />
    </dl>}
  </section>;
}

function StatutorySalesBookTable({ report }: { report: StatutorySalesBookReport }) {
  const annex = report.summary.type === "AnnexE2" ? "E-2" : "E-3";
  return <div className="tableViewport"><table aria-label={`Annex ${annex} qualifying Sales Invoices`}><thead><tr>{["Date", "Beneficiary Name", "ID Number", "TIN", "SI/OR", "Sales Including VAT", "VAT Amount", "VAT Exempt Sales", "5% Discount", "20% Discount", "Net Sales"].map((heading) => <th key={heading}>{heading}</th>)}</tr></thead><tbody>{report.rows.map((row) => <tr key={row.fiscalDocumentId}><td>{row.transactionDate}</td><td>{row.beneficiaryFullName}</td><td>{row.statutoryIdNumber}</td><td>{row.beneficiaryTin ?? ""}</td><td>{row.salesInvoiceNumber}</td><td>{money(row.salesInclusiveVatMinorUnits, report.summary.currencyCode)}</td><td>{money(row.vatAmountMinorUnits, report.summary.currencyCode)}</td><td>{money(row.vatExemptSalesMinorUnits, report.summary.currencyCode)}</td><td>{money(row.discount5MinorUnits, report.summary.currencyCode)}</td><td>{money(row.discount20MinorUnits, report.summary.currencyCode)}</td><td>{money(row.netSalesMinorUnits, report.summary.currencyCode)}</td></tr>)}</tbody></table></div>;
}

function SummaryValue({ label, value }: { label: string; value: string }) {
  return <div><dt>{label}</dt><dd>{value}</dd></div>;
}

function NoAuthorizedSite({ message }: { message: string }) {
  return <section className="stateMessage warning"><h2>No authorized fiscal Site</h2><p><strong>NO_AUTHORIZED_SITE</strong></p><p>{message}</p></section>;
}

function ReadingPanel({ kind, history, error, actionError, canRead, canGenerate, onGenerate, download }: { kind: "X" | "Z"; history?: History; error?: string; actionError?: string; canRead: boolean; canGenerate: boolean; onGenerate?: () => void; download: (reference: string, format: "pdf" | "json") => string }) {
  if (!canRead) return <p className="stateMessage warning">{kind} Reading permission is required.</p>;
  return <div className="fiscalPane" role="tabpanel"><div className="readingTitle"><div><h3>{kind} Reading</h3><p>{kind === "X" ? "Non-closing observation of the current reporting period." : "Closing fiscal report history."}</p></div>{canGenerate && history?.currentPeriod && <button type="button" onClick={onGenerate}>Generate {kind} Reading</button>}</div>{error && <p className="errorMessage" role="alert">{error}</p>}{actionError && <p className="errorMessage" role="alert">{actionError}</p>}{history?.currentPeriod ? <dl className="periodCard"><div><dt>Fiscal Business Date</dt><dd>{history.currentPeriod.businessDayDate}</dd></div><div><dt>Period start (PHT)</dt><dd>{formatPhtInstant(history.currentPeriod.periodStartAt)}</dd></div><div><dt>Period end (PHT)</dt><dd>{formatPhtInstant(history.currentPeriod.periodEndAt)}</dd></div><div><dt>Status</dt><dd>{history.currentPeriod.status}</dd></div></dl> : <p role="status">{kind === "X" && canGenerate ? "No open fiscal reporting period is currently available for X generation." : "No open fiscal reporting period is available."}</p>}<div className="tableViewport"><table><thead><tr><th>Reference</th><th>Generated (PHT)</th><th>Transactions</th><th>Gross sales</th><th>VATable</th><th>VAT</th><th>VAT exempt</th><th>Zero rated</th><th>Discounts</th><th>Voids</th><th>Adjustments</th><th>Net sales</th><th>Tenders</th><th>Output</th></tr></thead><tbody>{history?.readings.map((reading) => <tr key={reading.reportReference}><td>{reading.reportReference}</td><td>{formatPhtInstant(reading.generatedAt)} PHT</td><td>{reading.transactionCount}</td><td>{money(reading.amounts.grossSalesAmountMinorUnits, reading.currencyCode)}</td><td>{money(reading.amounts.vatableSalesAmountMinorUnits, reading.currencyCode)}</td><td>{money(reading.amounts.vatAmountMinorUnits, reading.currencyCode)}</td><td>{money(reading.amounts.vatExemptSalesAmountMinorUnits, reading.currencyCode)}</td><td>{money(reading.amounts.zeroRatedSalesAmountMinorUnits, reading.currencyCode)}</td><td>{money(reading.amounts.discountAmountMinorUnits, reading.currencyCode)}</td><td>{money(reading.amounts.voidAmountMinorUnits, reading.currencyCode)}</td><td>{money(reading.amounts.adjustmentAmountMinorUnits, reading.currencyCode)}</td><td>{money(reading.amounts.netSalesAmountMinorUnits, reading.currencyCode)}</td><td>{reading.tenders.length ? reading.tenders.map((tender) => `${tender.classification}: ${money(tender.amountMinorUnits, tender.currencyCode)}`).join("; ") : "None"}</td><td><div className="reportFilters"><a href={download(reading.reportReference, "pdf")}>Download PDF</a><a href={download(reading.reportReference, "json")}>Download JSON</a></div></td></tr>)}</tbody></table></div></div>;
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
