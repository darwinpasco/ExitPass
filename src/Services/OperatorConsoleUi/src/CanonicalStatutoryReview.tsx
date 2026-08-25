import { useEffect, useMemo, useRef, useState, type FormEvent, type KeyboardEvent } from "react";
import type { OperatorConsoleApiClient } from "./apiClient";
import { mapApiError } from "./apiClient";
import type { OperatorConsoleHumanSession } from "./humanAuthentication";
import { formatPhpMoney } from "./phpCurrency";
import { StatutoryEvidenceReviewPanel } from "./StatutoryEvidenceReviewPanel";
import type {
  CanonicalStatutoryReviewDetail,
  CanonicalStatutoryReviewFilters,
  CanonicalStatutoryReviewQueueResult
} from "./types";

type QueueState =
  | { status: "loading" }
  | { status: "ready"; data: CanonicalStatutoryReviewQueueResult; loadedAt: string; staleMessage?: string }
  | { status: "empty" }
  | { status: "denied"; message: string }
  | { status: "unavailable"; message: string }
  | { status: "failed"; message: string };

type DetailState =
  | { status: "loading" }
  | { status: "ready"; data: CanonicalStatutoryReviewDetail }
  | { status: "not-found" }
  | { status: "denied"; message: string }
  | { status: "unavailable"; message: string }
  | { status: "failed"; message: string };

export const defaultCanonicalStatutoryReviewFilters: CanonicalStatutoryReviewFilters = {
  status: "PENDING_REVIEW",
  page: 1,
  pageSize: 25
};

export function CanonicalStatutoryReviewQueuePage({
  client,
  session,
  filters,
  onFiltersChange,
  onOpen
}: {
  client: OperatorConsoleApiClient;
  session?: OperatorConsoleHumanSession;
  filters: CanonicalStatutoryReviewFilters;
  onFiltersChange: (filters: CanonicalStatutoryReviewFilters) => void;
  onOpen: (decisionId: string) => void;
}) {
  const [draftFilters, setDraftFilters] = useState(filters);
  const [state, setState] = useState<QueueState>({ status: "loading" });
  const [refreshToken, setRefreshToken] = useState(0);
  const requestSequence = useRef(0);
  const headingRef = useRef<HTMLHeadingElement>(null);

  useEffect(() => headingRef.current?.focus(), []);
  useEffect(() => setDraftFilters(filters), [filters]);
  useEffect(() => {
    const sequence = ++requestSequence.current;
    const controller = new AbortController();
    const prior = state.status === "ready" ? state.data : undefined;
    if (!prior) setState({ status: "loading" });
    void client.listCanonicalStatutoryReviews(filters, controller.signal)
      .then((data) => {
        if (controller.signal.aborted || sequence !== requestSequence.current) return;
        setState(data.items.length ? { status: "ready", data, loadedAt: new Date().toISOString() } : { status: "empty" });
      })
      .catch((error) => {
        if (controller.signal.aborted || sequence !== requestSequence.current) return;
        const mapped = mapApiError(error);
        if (prior) {
          setState({
            status: "ready",
            data: prior,
            loadedAt: state.status === "ready" ? state.loadedAt : new Date().toISOString(),
            staleMessage: "Refresh failed. Showing retained results from the last successful load."
          });
        } else if (mapped.status === "access-denied") {
          setState({ status: "denied", message: mapped.message });
        } else if (isUnavailable(error, mapped.errorCode)) {
          setState({ status: "unavailable", message: "Central PMS is temporarily unavailable. Retry the queue." });
        } else {
          setState({ status: "failed", message: mapped.message });
        }
      });
    return () => controller.abort();
  }, [client, filters, refreshToken]);

  function applyFilters(event: FormEvent) {
    event.preventDefault();
    onFiltersChange({ ...draftFilters, search: draftFilters.search?.trim() || undefined, page: 1 });
  }

  const showSiteFilter = Boolean(session?.hasGlobalScope || (session?.siteReferences.length ?? 0) > 1);
  return (
    <>
      <section className="pageTitle">
        <div>
          <p className="eyebrow">Central PMS statutory review</p>
          <h2 ref={headingRef} tabIndex={-1}>Review queue</h2>
          <p>Review requests originating from WebPay or APT through the canonical Central PMS workflow.</p>
        </div>
        <button type="button" onClick={() => setRefreshToken((value) => value + 1)}>Refresh</button>
      </section>

      <section className="panel" aria-labelledby="canonical-review-filters">
        <h3 id="canonical-review-filters">Queue filters</h3>
        <form className="canonicalReviewFilters" onSubmit={applyFilters}>
          <label>Status<select value={draftFilters.status} onChange={(event) => setDraftFilters({ ...draftFilters, status: event.target.value as CanonicalStatutoryReviewFilters["status"] })}>
            <option value="PENDING_REVIEW">Pending</option><option value="APPROVED">Approved</option><option value="REJECTED">Rejected</option><option value="REVIEW_FACTS_UNAVAILABLE">Unavailable facts</option><option value="ALL">All</option>
          </select></label>
          {showSiteFilter && <label>Site<select value={draftFilters.siteId ?? ""} onChange={(event) => setDraftFilters({ ...draftFilters, siteId: event.target.value || undefined })}>
            <option value="">All authorized Sites</option>{session?.siteReferences.map((site) => <option key={site} value={site}>{siteLabel(site)}</option>)}
          </select></label>}
          <label>Benefit<select value={draftFilters.entitlementType ?? ""} onChange={(event) => setDraftFilters({ ...draftFilters, entitlementType: event.target.value || undefined })}>
            <option value="">All types</option><option value="SENIOR_CITIZEN">Senior Citizen</option><option value="PWD">PWD</option>
          </select></label>
          <label>Origin<select value={draftFilters.sourceChannel ?? ""} onChange={(event) => setDraftFilters({ ...draftFilters, sourceChannel: (event.target.value || undefined) as CanonicalStatutoryReviewFilters["sourceChannel"] })}>
            <option value="">All channels</option><option value="WEBPAY">WebPay</option><option value="ASSISTED_PAYMENT_TERMINAL">APT</option>
          </select></label>
          <label>Submitted from<input type="datetime-local" value={toLocalDateTimeInput(draftFilters.submittedFrom)} onChange={(event) => setDraftFilters({ ...draftFilters, submittedFrom: fromLocalDateTimeInput(event.target.value) })} /></label>
          <label>Submitted to<input type="datetime-local" value={toLocalDateTimeInput(draftFilters.submittedTo)} onChange={(event) => setDraftFilters({ ...draftFilters, submittedTo: fromLocalDateTimeInput(event.target.value) })} /></label>
          <label className="canonicalReviewSearch">Safe search<input aria-describedby="canonical-search-help" value={draftFilters.search ?? ""} maxLength={100} onChange={(event) => setDraftFilters({ ...draftFilters, search: event.target.value })} /></label>
          <span id="canonical-search-help" className="fieldHint">Request, parking session, ticket, or plate reference.</span>
          <button type="submit">Apply filters</button>
        </form>
      </section>

      <section className="panel" aria-labelledby="canonical-review-queue">
        <div className="panelHeader"><h3 id="canonical-review-queue">Statutory-review requests</h3>{state.status === "ready" && <span className="statusPill">{state.data.totalCount} results</span>}</div>
        {state.status === "loading" && <ReviewState title="Loading queue" message="Retrieving requests from Central PMS." />}
        {state.status === "empty" && <ReviewState title="No requests" message="No requests match the current filters." />}
        {state.status === "denied" && <ReviewState title="Access denied" message={state.message} />}
        {state.status === "unavailable" && <ReviewState title="Central PMS unavailable" message={state.message} />}
        {state.status === "failed" && <ReviewState title="Queue failed" message={state.message} />}
        {state.status === "ready" && <>
          {state.staleMessage && <p className="notice" role="status">{state.staleMessage} Loaded {formatDate(state.loadedAt)}.</p>}
          <div className="tableScroller"><table><thead><tr><th>Request</th><th>Site</th><th>Origin</th><th>Benefit</th><th>Submitted</th><th>Status</th><th>Parking / ticket</th><th>Action</th></tr></thead>
            <tbody>{state.data.items.map((item) => <tr key={item.statutoryDiscountDecisionCommandId}>
              <td><code>{shortReference(item.requestReference)}</code></td><td>{siteLabel(item.siteId)}</td><td>{originLabel(item.sourceChannel)}</td><td>{benefitLabel(item.entitlementType)}</td><td>{formatDate(item.submittedAt)}</td><td><span className="statusPill">{statusLabel(item.reviewStatus)}</span></td><td>{item.ticketReference ?? shortReference(item.parkingSessionId)}</td><td><button type="button" onClick={() => onOpen(item.statutoryDiscountDecisionCommandId)}>Review</button></td>
            </tr>)}</tbody></table></div>
          <div className="paginationBar"><button type="button" disabled={filters.page <= 1} onClick={() => onFiltersChange({ ...filters, page: filters.page - 1 })}>Previous</button><span>Page {state.data.page}</span><button type="button" disabled={!state.data.hasMore} onClick={() => onFiltersChange({ ...filters, page: filters.page + 1 })}>Next</button></div>
        </>}
      </section>
    </>
  );
}

export function CanonicalStatutoryReviewDetailPage({ client, decisionId, onBack }: { client: OperatorConsoleApiClient; decisionId: string; onBack: () => void }) {
  const [state, setState] = useState<DetailState>({ status: "loading" });
  const [refreshToken, setRefreshToken] = useState(0);
  const [reason, setReason] = useState("");
  const [attested, setAttested] = useState(false);
  const [confirming, setConfirming] = useState<"APPROVE" | "REJECT" | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [message, setMessage] = useState<string>();
  const [error, setError] = useState<string>();
  const headingRef = useRef<HTMLHeadingElement>(null);
  const requestSequence = useRef(0);
  const approveButtonRef = useRef<HTMLButtonElement>(null);
  const rejectButtonRef = useRef<HTMLButtonElement>(null);
  const confirmButtonRef = useRef<HTMLButtonElement>(null);
  const cancelButtonRef = useRef<HTMLButtonElement>(null);

  useEffect(() => headingRef.current?.focus(), []);
  useEffect(() => {
    const sequence = ++requestSequence.current;
    const controller = new AbortController();
    setState({ status: "loading" });
    void client.getCanonicalStatutoryReview(decisionId, controller.signal).then((data) => {
      if (!controller.signal.aborted && sequence === requestSequence.current) setState({ status: "ready", data });
    }).catch((raw) => {
      if (controller.signal.aborted || sequence !== requestSequence.current) return;
      const mapped = mapApiError(raw);
      if (mapped.status === "not-found") setState({ status: "not-found" });
      else if (mapped.status === "access-denied") setState({ status: "denied", message: mapped.message });
      else if (isUnavailable(raw, mapped.errorCode)) setState({ status: "unavailable", message: "Central PMS is temporarily unavailable." });
      else setState({ status: "failed", message: mapped.message });
    });
    return () => controller.abort();
  }, [client, decisionId, refreshToken]);

  const detail = state.status === "ready" ? state.data : undefined;
  const terminal = detail ? detail.reviewStatus !== "PENDING_REVIEW" : true;
  const canApprove = client.canApproveStatutoryDiscount?.() ?? false;
  const canReject = client.canRejectStatutoryDiscount?.() ?? false;

  useEffect(() => {
    if (confirming) (attested ? confirmButtonRef.current : cancelButtonRef.current)?.focus();
  }, [confirming, attested]);

  function closeConfirmation() {
    const action = confirming;
    setConfirming(null);
    queueMicrotask(() => (action === "APPROVE" ? approveButtonRef.current : rejectButtonRef.current)?.focus());
  }

  function containConfirmationFocus(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === "Escape" && !submitting) {
      event.preventDefault();
      closeConfirmation();
      return;
    }
    if (event.key !== "Tab") return;
    const first = confirmButtonRef.current;
    const last = cancelButtonRef.current;
    if (!first || !last) return;
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }

  async function decide() {
    if (!detail || !confirming || submitting) return;
    if (!attested) { setError("Confirm that you reviewed the required evidence and request facts."); return; }
    if (confirming === "REJECT" && !reason) { setError("A rejection reason is required."); return; }
    setSubmitting(true); setError(undefined); setMessage(undefined);
    try {
      const result = await client.submitCanonicalStatutoryReviewDecision({
        statutoryDiscountDecisionCommandId: detail.statutoryDiscountDecisionCommandId,
        decision: confirming,
        reasonCode: confirming === "REJECT" ? reason : undefined,
        reviewerAttestation: attested,
        idempotencyKey: `operator-console-review-${detail.statutoryDiscountDecisionCommandId}-${confirming.toLowerCase()}`
      });
      if (!result.decisionAccepted) setError(result.errorCode ?? "Central PMS did not accept the decision.");
      else setMessage(result.alreadyDecided ? "This request was already decided. The authoritative state has been refreshed." : `Decision recorded: ${result.currentValidationStatus}.`);
      setConfirming(null);
      setRefreshToken((value) => value + 1);
    } catch (raw) {
      const mapped = mapApiError(raw);
      if (mapped.errorCode?.includes("ALREADY_COMPLETED") || mapped.errorCode?.includes("NOT_AWAITING_REVIEW")) {
        setError("Another reviewer already decided this request. The authoritative state has been refreshed.");
        setRefreshToken((value) => value + 1);
      } else setError(mapped.message);
    } finally { setSubmitting(false); }
  }

  return <>
    <button className="backButton" type="button" onClick={onBack}>Back to filtered queue</button>
    {state.status === "loading" && <ReviewState title="Loading request" message="Retrieving authoritative detail from Central PMS." />}
    {state.status === "not-found" && <ReviewState title="Request unavailable" message="The request was not found or is outside your authorized scope." />}
    {state.status === "denied" && <ReviewState title="Access denied" message={state.message} />}
    {state.status === "unavailable" && <ReviewState title="Central PMS unavailable" message={state.message} />}
    {state.status === "failed" && <ReviewState title="Detail failed" message={state.message} />}
    {detail && <>
      <section className="pageTitle"><div><p className="eyebrow">Central PMS statutory review</p><h2 ref={headingRef} tabIndex={-1}>{benefitLabel(detail.entitlementType)}</h2><p>Request {shortReference(detail.requestReference)}</p></div><span className="statusPill">{statusLabel(detail.reviewStatus)}</span></section>
      <section className="panel canonicalReviewSummary" aria-labelledby="review-request-facts"><h3 id="review-request-facts">Request facts</h3><dl>
        <dt>Request reference</dt><dd>{detail.requestReference}</dd><dt>Site</dt><dd>{siteLabel(detail.siteId)}</dd><dt>Originating channel</dt><dd>{originLabel(detail.sourceChannel)}</dd><dt>Parking session / ticket</dt><dd>{detail.ticketReference ?? detail.parkingSessionId}</dd><dt>Benefit</dt><dd>{benefitLabel(detail.entitlementType)}</dd><dt>Submitted</dt><dd>{formatDate(detail.submittedAt)}</dd><dt>Workflow status</dt><dd>{statusLabel(detail.reviewStatus)}</dd>
        {detail.reviewedAt && <><dt>Decided</dt><dd>{formatDate(detail.reviewedAt)}</dd></>}{detail.reviewerUserId && <><dt>Reviewer</dt><dd>{detail.reviewerUserId}</dd></>}{terminal && <><dt>Decision</dt><dd>{statusLabel(detail.reviewStatus)}</dd></>}{detail.reviewerReasonCode && <><dt>Reason</dt><dd>{detail.reviewerReasonCode}</dd></>}
        <dt>Session eligibility</dt><dd>{statusLabel(detail.sessionEligibilityStatus)}</dd>
        <dt>Payable basis</dt><dd>{detail.payableBasisStatus === "NOT_YET_CREATED" ? "Not yet created" : statusLabel(detail.payableBasisStatus)}</dd>
        <dt>Discount application</dt><dd>{detail.payableBasisApplicationStatus ? statusLabel(detail.payableBasisApplicationStatus) : detail.sessionEligibilityStatus === "ELIGIBLE" ? "Pending payable-basis creation" : "Not applicable"}</dd>
      </dl></section>
      {detail.sessionEligibilityStatus === "ELIGIBLE" && detail.payableBasisStatus === "NOT_YET_CREATED" && <section className="panel" aria-labelledby="deferred-benefit"><h3 id="deferred-benefit">Approved eligibility</h3><p>The parking session is eligible for the approved statutory benefit. Central PMS will calculate and apply the benefit when the payable basis is created.</p></section>}
      {detail.payableBasisStatus === "CREATED" && (detail.originalAmountMinorUnits !== undefined || detail.finalPayableAmountMinorUnits !== undefined) && <section className="panel" aria-labelledby="review-amounts"><h3 id="review-amounts">Payable-basis amounts (read-only)</h3><p>Original: {formatPhpMoney(detail.originalAmountMinorUnits, detail.currency)}</p>{detail.statutoryDiscountAmountMinorUnits !== undefined && <p>Statutory benefit: {formatPhpMoney(detail.statutoryDiscountAmountMinorUnits, detail.currency)}</p>}<p>Final payable: {formatPhpMoney(detail.finalPayableAmountMinorUnits, detail.currency)}</p><p>Central PMS calculated these amounts when it created the payable basis.</p></section>}
      <StatutoryEvidenceReviewPanel client={client} decisionId={detail.statutoryDiscountDecisionCommandId} authorityContextKey={`${detail.siteGroupId ?? ""}:${detail.siteId ?? ""}`} entitlementLabel={benefitLabel(detail.entitlementType)} />
      <section className="panel" aria-labelledby="canonical-decision"><div className="panelHeader"><h3 id="canonical-decision">Decision</h3><span className="statusPill">{terminal ? "Final" : "Pending"}</span></div>
        {message && <p className="successMessage" role="status">{message}</p>}{error && <p className="errorMessage" role="alert">{error}</p>}
        {terminal ? <p>The Central PMS decision is final and cannot be changed in Operator Console.</p> : <>
          <label className="attestationField"><input type="checkbox" aria-describedby="canonical-attestation-help" checked={attested} onChange={(event) => setAttested(event.target.checked)} />I reviewed the required evidence and authoritative request facts.</label>
          <p id="canonical-attestation-help" className="fieldHint">Required before Central PMS can record a decision.</p>
          {canReject && <label className="reasonField">Rejection reason<select aria-describedby="canonical-rejection-help" value={reason} onChange={(event) => setReason(event.target.value)}><option value="">Select a reason</option><option value="EVIDENCE_INCOMPLETE">Evidence incomplete</option><option value="EVIDENCE_INVALID">Evidence invalid</option><option value="ENTITLEMENT_NOT_ESTABLISHED">Entitlement not established</option><option value="REQUEST_FACTS_MISMATCH">Request facts mismatch</option></select></label>}
          {canReject && <p id="canonical-rejection-help" className="fieldHint">Required for rejection.</p>}
          {!confirming ? <div className="actionBar">{canApprove && <button ref={approveButtonRef} type="button" disabled={submitting} onClick={() => setConfirming("APPROVE")}>Approve</button>}{canReject && <button ref={rejectButtonRef} type="button" disabled={submitting || !reason} onClick={() => setConfirming("REJECT")}>Reject</button>}</div> : <div className="decisionConfirmation" role="alertdialog" aria-modal="true" aria-labelledby="decision-confirm-title" aria-describedby="decision-confirm-description" onKeyDown={containConfirmationFocus}><h4 id="decision-confirm-title">Confirm {confirming === "APPROVE" ? "approval" : "rejection"}</h4><p id="decision-confirm-description">Central PMS will record this as an immutable final decision.</p><div className="actionBar"><button ref={confirmButtonRef} type="button" disabled={submitting || !attested} onClick={() => void decide()}>{submitting ? "Submitting" : "Confirm decision"}</button><button ref={cancelButtonRef} type="button" className="secondaryButton" disabled={submitting} onClick={closeConfirmation}>Cancel</button></div></div>}
        </>}
      </section>
    </>}
  </>;
}

function ReviewState({ title, message }: { title: string; message: string }) { return <div className="stateMessage" role="status"><h3>{title}</h3><p>{message}</p></div>; }
function isUnavailable(error: unknown, code?: string) { return error instanceof TypeError || Boolean(code?.includes("UNAVAILABLE")); }
function shortReference(value: string) { return value.length > 12 ? `${value.slice(0, 8)}…` : value; }
function siteLabel(value?: string) { return value ? `Site ${shortReference(value)}` : "—"; }
function formatDate(value: string) { const date = new Date(value); return Number.isNaN(date.getTime()) ? value : new Intl.DateTimeFormat("en-PH", { dateStyle: "medium", timeStyle: "short", timeZone: "Asia/Manila" }).format(date); }
function toLocalDateTimeInput(value?: string) { if (!value) return ""; const date = new Date(value); if (Number.isNaN(date.getTime())) return ""; const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000); return local.toISOString().slice(0, 16); }
function fromLocalDateTimeInput(value: string) { if (!value) return undefined; const date = new Date(value); return Number.isNaN(date.getTime()) ? undefined : date.toISOString(); }
function originLabel(value: string) { return value === "ASSISTED_PAYMENT_TERMINAL" ? "APT" : value === "WEBPAY" ? "WebPay" : value; }
function benefitLabel(value: string) { return value === "SENIOR_CITIZEN" ? "Senior Citizen" : value === "PWD" ? "Person with Disability" : value; }
function statusLabel(value: string) { return value.replaceAll("_", " ").toLowerCase().replace(/\b\w/g, (letter) => letter.toUpperCase()); }
