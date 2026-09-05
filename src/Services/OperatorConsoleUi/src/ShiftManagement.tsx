import { useEffect, useMemo, useState } from "react";
import type { OperatorConsoleApiClient } from "./apiClient";
import { mapApiError } from "./apiClient";
import { formatPhpMoney } from "./phpCurrency";
import type { OperationalShift, ShiftAuthorizedSite } from "./types";

export function ShiftManagement({ client }: { client: OperatorConsoleApiClient }) {
  const [view, setView] = useState<"open" | "recently-closed">("open");
  const [sites, setSites] = useState<ShiftAuthorizedSite[]>([]);
  const [siteId, setSiteId] = useState("");
  const [staff, setStaff] = useState("");
  const [items, setItems] = useState<OperationalShift[]>([]);
  const [selected, setSelected] = useState<OperationalShift | null>(null);
  const [currentOwn, setCurrentOwn] = useState<OperationalShift | null>(null);
  const [state, setState] = useState<"loading" | "ready" | "error">("loading");
  const [message, setMessage] = useState("");
  const [reason, setReason] = useState("");

  const canView = client.canViewShiftManagement();
  const canManage = client.canManageShifts();
  const staffOptions = useMemo(() => {
    const values = new Map(items.map(item => [item.operatorUserId, item.displayName]));
    return [...values.entries()];
  }, [items]);

  function load() {
    if (!canView) {
      setState("ready");
      return;
    }
    setState("loading");
    Promise.all([
      client.listShiftAuthorizedSites(),
      client.getCurrentOwnShift(),
      client.listShifts(view, siteId || undefined, staff || undefined)
    ]).then(([authorizedSites, own, result]) => {
      setSites(authorizedSites);
      setCurrentOwn(own);
      setItems(result.items);
      if (selected) setSelected(result.items.find(item => item.shiftId === selected.shiftId) ?? null);
      setMessage("");
      setState("ready");
    }).catch(error => {
      setMessage(mapApiError(error).message);
      setState("error");
    });
  }

  useEffect(load, [view, siteId, staff, canView]);

  async function startShift() {
    const selectedSite = siteId || (sites.length === 1 ? sites[0].siteId : "");
    if (!selectedSite) return;
    try {
      await client.startOwnShift(selectedSite);
      load();
    } catch (error) { setMessage(mapApiError(error).message); }
  }

  async function closeOwn() {
    if (!currentOwn) return;
    try { await client.closeOwnShift(currentOwn.shiftId); load(); }
    catch (error) { setMessage(mapApiError(error).message); }
  }

  async function exceptionClose() {
    if (!selected || !reason.trim() || !window.confirm("Close this shift as a supervisor exception?")) return;
    try { await client.exceptionCloseShift(selected.shiftId, reason.trim()); setReason(""); load(); }
    catch (error) { setMessage(mapApiError(error).message); }
  }

  if (state === "loading") return <section className="pageTitle" role="status"><h2>Loading shifts</h2></section>;
  if (!canView) return <section className="pageTitle" role="status"><h2>Not authorized</h2><p>Your current role does not allow Shift Management access.</p></section>;
  if (state === "error") return <section className="pageTitle" role="alert"><h2>Unable to load Shift Management</h2><p>{message}</p><button onClick={load}>Retry</button></section>;

  return <>
    <section className="pageTitle">
      <div><p className="eyebrow">Site operations</p><h2>Shift Management</h2></div>
      <div className="shiftModeControl" role="group" aria-label="Shift view">
        <button className={view === "open" ? "navLinkActive" : ""} onClick={() => setView("open")}>Open Shifts</button>
        <button className={view === "recently-closed" ? "navLinkActive" : ""} onClick={() => setView("recently-closed")}>Recently Closed</button>
      </div>
    </section>

    {sites.length === 0 ? <section className="panel" role="status"><h3>No authorized Sites are available.</h3><p>Contact your administrator.</p></section> : <>
      <section className="shiftOwnBar" aria-label="Own shift">
        <div><span>My shift</span><strong>{currentOwn ? `${currentOwn.siteName} - ${currentOwn.status}` : "No open shift"}</strong></div>
        {!currentOwn && <button onClick={startShift} disabled={!siteId && sites.length !== 1}>Start Shift</button>}
        {currentOwn && <button onClick={closeOwn} disabled={currentOwn.cashCustodyStatus === "OPEN"}>Close Shift</button>}
        {currentOwn?.cashCustodyStatus === "OPEN" && <span>Close the cash custody session before closing this shift.</span>}
      </section>

      <section className="shiftFilters" aria-label="Shift filters">
        <label>Site<select value={siteId} onChange={event => setSiteId(event.target.value)}><option value="">All authorized Sites</option>{sites.map(site => <option key={site.siteId} value={site.siteId}>{site.siteName}</option>)}</select></label>
        <label>Staff member<select value={staff} onChange={event => setStaff(event.target.value)}><option value="">All staff</option>{staffOptions.map(([id, name]) => <option key={id} value={id}>{name}</option>)}</select></label>
      </section>
    </>}

    {message && <p className="authenticationInlineError" role="alert">{message}</p>}
    {sites.length > 0 && items.length === 0 ? <section className="panel" role="status"><h3>{view === "open" ? "No open shifts" : "No recently closed shifts"}</h3></section> : null}
    {items.length > 0 && <section className="shiftLayout">
      <div className="tableScroller"><table><thead><tr><th>Staff</th><th>Site</th><th>Shift</th><th>Started</th><th>Elapsed</th><th>Custody</th><th>Cash activity</th></tr></thead><tbody>{items.map(item => <tr key={item.shiftId} className={selected?.shiftId === item.shiftId ? "selectedRow" : ""} onClick={() => setSelected(item)}><td><strong>{item.displayName}</strong><br />{item.username}</td><td>{item.siteName}<br />{item.siteGroupName}</td><td>{item.shiftReference}<br />{item.status}</td><td>{dateTime(item.openedAt)}</td><td>{duration(item.elapsedSeconds)}</td><td>{item.cashCustodyStatus}</td><td>{item.cashTransactionCount} transactions<br />{item.cashCollectedMinorUnits == null ? "Unavailable" : formatPhpMoney(item.cashCollectedMinorUnits, "PHP")}</td></tr>)}</tbody></table></div>
      {selected && <aside className="shiftDetail" aria-label="Shift detail"><div className="panelHeader"><h3>Shift Detail</h3><button aria-label="Close detail" onClick={() => setSelected(null)}>X</button></div><dl className="detailGrid"><dt>Reference</dt><dd>{selected.shiftReference}</dd><dt>Staff</dt><dd>{selected.displayName} ({selected.username})</dd><dt>Role</dt><dd>{selected.roles.join(", ") || selected.userType}</dd><dt>Site</dt><dd>{selected.siteName}</dd><dt>Site Group</dt><dd>{selected.siteGroupName}</dd><dt>Device / terminal</dt><dd>{selected.deviceName ?? selected.terminalReference ?? "Unavailable"}</dd><dt>Opened</dt><dd>{dateTime(selected.openedAt)}</dd><dt>Closed</dt><dd>{selected.closedAt ? dateTime(selected.closedAt) : "Open"}</dd><dt>Custody</dt><dd>{selected.cashCustodyStatus}</dd><dt>Opening cash</dt><dd>{selected.openingCashMinorUnits == null ? "Unavailable" : formatPhpMoney(selected.openingCashMinorUnits, "PHP")}</dd><dt>Cash collected</dt><dd>{selected.cashCollectedMinorUnits == null ? "Unavailable" : formatPhpMoney(selected.cashCollectedMinorUnits, "PHP")}</dd><dt>Close</dt><dd>{selected.closeType ?? "Not closed"}</dd><dt>Closing actor</dt><dd>{selected.closingActorName ?? "Unavailable"}</dd><dt>Reason</dt><dd>{selected.closeReason ?? "Not applicable"}</dd></dl>{canManage && selected.status === "ACTIVE" && <div className="exceptionClose"><label>Exception close reason<textarea value={reason} onChange={event => setReason(event.target.value)} /></label><button onClick={exceptionClose} disabled={!reason.trim() || selected.cashCustodyStatus === "OPEN"}>Close Shift</button>{selected.cashCustodyStatus === "OPEN" && <p>Close the cash custody session before closing this shift.</p>}</div>}</aside>}
    </section>}
  </>;
}

function dateTime(value: string) { return new Intl.DateTimeFormat("en-PH", { dateStyle: "medium", timeStyle: "short" }).format(new Date(value)); }
function duration(seconds: number) { const hours = Math.floor(seconds / 3600); const minutes = Math.floor((seconds % 3600) / 60); return `${hours}h ${minutes}m`; }
