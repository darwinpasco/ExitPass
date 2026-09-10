import { useEffect, useState } from "react";
import {
  fiscalReportingPermissions,
  type EjInvoice,
  type History,
  type OperatorFiscalReportingClient
} from "./fiscalReporting";

export function OperatorFiscalReportingPage({ client, siteReferences, permissions }: {
  client: OperatorFiscalReportingClient;
  siteReferences: readonly string[];
  permissions: readonly string[];
}) {
  const [siteId, setSiteId] = useState(siteReferences[0] ?? "");
  const [tab, setTab] = useState<"ej" | "x" | "z">("ej");
  const [start, setStart] = useState("2026-09-10T00:00:00Z");
  const [end, setEnd] = useState("2026-09-11T00:00:00Z");
  const [search, setSearch] = useState("");
  const [ej, setEj] = useState<EjInvoice[]>();
  const [x, setX] = useState<History>();
  const [z, setZ] = useState<History>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const [confirming, setConfirming] = useState(false);
  const has = (permission: string) => permissions.includes(permission);

  async function load() {
    if (!siteId) return;
    setBusy(true);
    setError(undefined);
    try {
      const work: Promise<void>[] = [];
      if (has(fiscalReportingPermissions.ejRead)) work.push(client.readEj(siteId, start, end, search).then(setEj));
      if (has(fiscalReportingPermissions.xRead)) work.push(client.readX(siteId).then(setX));
      if (has(fiscalReportingPermissions.zRead)) work.push(client.readZ(siteId).then(setZ));
      await Promise.all(work);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Fiscal reporting is unavailable.");
    } finally {
      setBusy(false);
    }
  }

  useEffect(() => { void load(); }, [siteId]);

  async function generate(kind: "x" | "z") {
    setBusy(true);
    setError(undefined);
    try {
      if (kind === "x") {
        await client.generateX(siteId);
        setX(await client.readX(siteId));
      } else {
        await client.generateZ(siteId);
        setZ(await client.readZ(siteId));
        setConfirming(false);
      }
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Generation failed safely.");
    } finally {
      setBusy(false);
    }
  }

  if (!siteReferences.length) return <section className="stateMessage warning"><h2>No authorized fiscal Site</h2><p>Your operator session has no Site scope.</p></section>;

  return <section className="panel fiscalReporting" aria-labelledby="operator-fiscal-reporting-title">
    <div className="pageTitle"><div><p className="eyebrow">Site POS authority</p><h2 id="operator-fiscal-reporting-title">Fiscal Reporting</h2><p>Electronic Journal, X Reading, and Z Reading are retrieved through Central PMS from the bound Site POS Server.</p></div><span className="statusPill">Site scoped</span></div>
    <label className="siteSelector">Authorized Site<select value={siteId} onChange={(event) => setSiteId(event.target.value)}>{siteReferences.map((site) => <option key={site}>{site}</option>)}</select></label>
    <div className="reportTabs" role="tablist" aria-label="Fiscal report type"><button type="button" role="tab" aria-selected={tab === "ej"} onClick={() => setTab("ej")}>Electronic Journal</button><button type="button" role="tab" aria-selected={tab === "x"} onClick={() => setTab("x")}>X Reading</button><button type="button" role="tab" aria-selected={tab === "z"} onClick={() => setTab("z")}>Z Reading</button></div>
    {busy && <p role="status">Loading authoritative POS reporting data...</p>}
    {error && <div className="stateMessage danger" role="alert"><h3>Fiscal reporting unavailable</h3><p>{error}</p></div>}
    {tab === "ej" && (has(fiscalReportingPermissions.ejRead) ? <div className="fiscalPane" role="tabpanel"><h3>Electronic Journal</h3><p>Exact visible Sales Invoice text supplied by POS authority. The browser does not create the journal artifact.</p><div className="reportFilters"><label>Period start (UTC)<input aria-label="EJ period start UTC" value={start} onChange={(e) => setStart(e.target.value)} /></label><label>Period end (UTC)<input aria-label="EJ period end UTC" value={end} onChange={(e) => setEnd(e.target.value)} /></label><label>SI / transaction reference<input aria-label="Search Electronic Journal" value={search} onChange={(e) => setSearch(e.target.value)} /></label><button type="button" onClick={load}>View journal</button>{has(fiscalReportingPermissions.ejExport) && !!ej?.length && <a className="primaryButton" href={client.ejUrl(siteId, start, end, search)}>Download authoritative .txt</a>}</div>{ej?.length === 0 && <p role="status">No Sales Invoice activity exists for this period.</p>}{ej?.map((invoice) => <article className="invoiceText" key={invoice.fiscalDocumentId}><header><strong>{invoice.fiscalDocumentNumber}</strong><span>{new Date(invoice.issuedAt).toLocaleString()}</span></header><pre>{invoice.printableText}</pre></article>)}</div> : <p className="stateMessage warning">Electronic Journal permission is required.</p>)}
    {tab === "x" && <ReadingPanel kind="X" history={x} canRead={has(fiscalReportingPermissions.xRead)} canGenerate={has(fiscalReportingPermissions.xGenerate)} onGenerate={() => generate("x")} download={(reference) => client.readingUrl(siteId, "x", reference)} />}
    {tab === "z" && <><ReadingPanel kind="Z" history={z} canRead={has(fiscalReportingPermissions.zRead)} canGenerate={false} download={(reference) => client.readingUrl(siteId, "z", reference)} />{has(fiscalReportingPermissions.zGenerate) && (!confirming ? <button className="dangerButton" type="button" onClick={() => setConfirming(true)}>Generate Z Reading</button> : <div className="zConfirmation" role="alertdialog" aria-label="Confirm Z Reading close"><h3>Close the current fiscal reporting period?</h3><p>Generating Z Reading closes and finalizes the current reporting period according to authoritative POS behavior. The browser cannot choose the period, sequence, or totals.</p><button className="dangerButton" type="button" onClick={() => generate("z")} disabled={busy}>Confirm close and generate Z</button><button type="button" onClick={() => setConfirming(false)}>Cancel</button></div>)}</>}
  </section>;
}

function ReadingPanel({ kind, history, canRead, canGenerate, onGenerate, download }: { kind: "X" | "Z"; history?: History; canRead: boolean; canGenerate: boolean; onGenerate?: () => void; download: (reference: string) => string }) {
  if (!canRead) return <p className="stateMessage warning">{kind} Reading permission is required.</p>;
  return <div className="fiscalPane" role="tabpanel"><div className="readingTitle"><div><h3>{kind} Reading</h3><p>{kind === "X" ? "Non-closing observation of the current reporting period." : "Closing fiscal report history."}</p></div>{canGenerate && <button type="button" onClick={onGenerate}>Generate {kind} Reading</button>}</div>{history?.currentPeriod ? <dl className="periodCard"><div><dt>Current business date</dt><dd>{history.currentPeriod.businessDayDate}</dd></div><div><dt>Reporting period</dt><dd>{history.currentPeriod.periodStartAt} - {history.currentPeriod.periodEndAt}</dd></div><div><dt>Status</dt><dd>{history.currentPeriod.status}</dd></div></dl> : <p role="status">No open fiscal reporting period is available.</p>}<div className="tableViewport"><table><thead><tr><th>Reference</th><th>Generated</th><th>Transactions</th><th>Gross sales</th><th>VATable</th><th>VAT</th><th>VAT exempt</th><th>Zero rated</th><th>Discounts</th><th>Voids</th><th>Adjustments</th><th>Net sales</th><th>Tenders</th><th>Output</th></tr></thead><tbody>{history?.readings.map((reading) => <tr key={reading.reportReference}><td>{reading.reportReference}</td><td>{new Date(reading.generatedAt).toLocaleString()}</td><td>{reading.transactionCount}</td><td>{money(reading.amounts.grossSalesAmountMinorUnits, reading.currencyCode)}</td><td>{money(reading.amounts.vatableSalesAmountMinorUnits, reading.currencyCode)}</td><td>{money(reading.amounts.vatAmountMinorUnits, reading.currencyCode)}</td><td>{money(reading.amounts.vatExemptSalesAmountMinorUnits, reading.currencyCode)}</td><td>{money(reading.amounts.zeroRatedSalesAmountMinorUnits, reading.currencyCode)}</td><td>{money(reading.amounts.discountAmountMinorUnits, reading.currencyCode)}</td><td>{money(reading.amounts.voidAmountMinorUnits, reading.currencyCode)}</td><td>{money(reading.amounts.adjustmentAmountMinorUnits, reading.currencyCode)}</td><td>{money(reading.amounts.netSalesAmountMinorUnits, reading.currencyCode)}</td><td>{reading.tenders.length ? reading.tenders.map((tender) => `${tender.classification}: ${money(tender.amountMinorUnits, tender.currencyCode)}`).join("; ") : "None"}</td><td><a href={download(reading.reportReference)}>Print / download</a></td></tr>)}</tbody></table></div></div>;
}

function money(value: number, currency: string) {
  return new Intl.NumberFormat("en-PH", { style: "currency", currency }).format(value / 100);
}
