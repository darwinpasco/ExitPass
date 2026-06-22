import { useEffect, useMemo, useState, type FormEvent } from "react";
import {
  createOperatorConsoleApiClient,
  defaultDevModeContext,
  getDefaultOperatorConsoleContext,
  mapApiError,
  type OperatorConsoleApiClient
} from "./apiClient";
import type {
  AccessReadinessResponse,
  AuditReportItem,
  AuditReportQuery,
  AuditReportResponse,
  LoadState,
  PolicyContextKind,
  ProductionPolicyImportDryRunResult,
  ProductionPolicyImportReviewDecisionAction,
  ProductionPolicyImportReviewListResult,
  ProductionPolicyImportReviewResult,
  StatutoryDiscountDraftDetail,
  StatutoryDiscountEvidenceList,
  EvidenceCaptureMethod,
  EvidenceType,
  OperatorTicketLookupResult,
  StatutoryDiscountPayableBasisApplicationResult,
  StatutoryDiscountPolicyContext,
  StatutoryDiscountQueueItem,
  VendorPaymentAcknowledgmentDetail,
  VendorPaymentAcknowledgmentSearchInput,
  VendorPaymentAcknowledgmentSearchResult,
  VendorPaymentAcknowledgmentStatus,
  VendorPaymentAcknowledgmentSummary,
  VendorSessionProjectionHealthConfig,
  VendorSessionProjectionHealthLatestRecord,
  VendorSessionProjectionHealthSummary,
  VendorSessionProjectionHealthTarget,
  VendorSessionProjectionHealthTargetDetail,
  VendorSessionProjectionHealthTargetsResponse
} from "./types";

const routes = {
  home: "/operator-console",
  ticketLookup: "/operator-console/ticket-lookup",
  audit: "/operator-console/audit",
  queue: "/operator-console/statutory-discounts",
  detail: "/operator-console/statutory-discounts/",
  vendorAcknowledgments: "/operator-console/vendor-acknowledgments",
  vendorProjectionHealth: "/operator-console/vendor-session-projections/health",
  policyImportReview: "/operator-console/production-policy-import-review"
};

interface AppProps {
  apiClient?: OperatorConsoleApiClient;
  initialPath?: string;
}

export function App({ apiClient, initialPath }: AppProps) {
  const client = useMemo(() => apiClient ?? createOperatorConsoleApiClient(), [apiClient]);
  const [path, setPath] = useState(initialPath ?? normalizePath(window.location.pathname));
  const [readinessState, setReadinessState] = useState<LoadState<AccessReadinessResponse>>({ status: "idle" });
  const operatorContext = useMemo(() => getDefaultOperatorConsoleContext(), []);
  const devModeContext = useMemo(() => defaultDevModeContext(), []);

  useEffect(() => {
    if (initialPath) {
      return;
    }

    const handlePopState = () => setPath(normalizePath(window.location.pathname));
    window.addEventListener("popstate", handlePopState);
    return () => window.removeEventListener("popstate", handlePopState);
  }, [initialPath]);

  function navigate(nextPath: string) {
    setPath(nextPath);
    if (!initialPath) {
      window.history.pushState({}, "", nextPath);
    }
  }

  function refreshReadiness(requestedAction = "SESSION_LOOKUP") {
    setReadinessState({ status: "loading" });
    void client
      .evaluateAccessReadiness({
        ...operatorContext,
        operatorUserId: operatorContext.userId,
        requestedAction,
        clientContext: {
          uiModule: "OperatorConsoleUi",
          screenState: path
        },
        devModeContext
      })
      .then((readiness) => setReadinessState({ status: "loaded", data: readiness }))
      .catch((error) => setReadinessState({ status: "error", message: mapApiError(error).message }));
  }

  useEffect(() => {
    refreshReadiness("SESSION_LOOKUP");
  }, [client]);

  const draftId = path.startsWith(routes.detail) ? path.slice(routes.detail.length) : null;
  const readiness = readinessState.status === "loaded" ? readinessState.data : null;
  const readinessBlockReason = readiness && !readiness.accessAllowed ? readinessBlockedActionReason(readiness) : null;

  return (
    <main className="appShell" aria-labelledby="app-title">
      <header className="appHeader">
        <div>
          <p className="eyebrow">Operator Console</p>
          <h1 id="app-title">ExitPass Operator Console</h1>
          <p className="headerCopy">
            Back-office workspace for statutory discount review, focused on operator validation and policy context.
          </p>
        </div>
        <div className="operatorStatus" aria-label="Operator identity">
          <span>Operator</span>
          <strong>shift-console-placeholder</strong>
        </div>
      </header>

      <section className="platformShell">
        <aside className="moduleRail" aria-label="Operator Console navigation">
          <div className="panelHeader">
            <p className="eyebrow">Workspace</p>
            <h2>Navigation</h2>
          </div>

          <nav aria-label="Operator Console routes">
            <button className="navLink" type="button" onClick={() => navigate(routes.home)}>
              Overview
            </button>
            <button
              className={`navLink ${path === routes.ticketLookup ? "navLinkActive" : ""}`}
              type="button"
              onClick={() => navigate(routes.ticketLookup)}
            >
              Ticket Lookup
            </button>
            <button
              className={`navLink ${path.startsWith(routes.queue) ? "navLinkActive" : ""}`}
              type="button"
              onClick={() => navigate(routes.queue)}
            >
              Statutory Discounts
            </button>
            <button
              className={`navLink ${path === routes.audit ? "navLinkActive" : ""}`}
              type="button"
              onClick={() => navigate(routes.audit)}
            >
              Audit / Reporting
            </button>
            <button
              className={`navLink ${path === routes.vendorAcknowledgments ? "navLinkActive" : ""}`}
              type="button"
              onClick={() => navigate(routes.vendorAcknowledgments)}
            >
              Vendor Acknowledgments
            </button>
            <button
              className={`navLink ${path === routes.vendorProjectionHealth ? "navLinkActive" : ""}`}
              type="button"
              onClick={() => navigate(routes.vendorProjectionHealth)}
            >
              Projection Health
            </button>
            <button
              className={`navLink ${path === routes.policyImportReview ? "navLinkActive" : ""}`}
              type="button"
              onClick={() => navigate(routes.policyImportReview)}
            >
              Policy Import Review
            </button>
          </nav>

          <div className="statusStack">
            <span className="statusPill">Local shell</span>
            <span className="statusPill">Live read model</span>
            {devModeContext.usesLocalDevFallbackContext && <span className="statusPill warningPill">Sandbox/local context</span>}
          </div>
        </aside>

        <section className="workspace">
          <AccessReadinessPanel
            state={readinessState}
            usesLocalDevFallbackContext={devModeContext.usesLocalDevFallbackContext}
            onRefresh={() => refreshReadiness("SESSION_LOOKUP")}
          />
          {draftId ? (
            <StatutoryDiscountDetailPage
              client={client}
              draftId={draftId}
              navigate={navigate}
              readinessBlockReason={readinessBlockReason}
            />
          ) : path === routes.ticketLookup ? (
            <TicketLookupPage client={client} readinessBlockReason={readinessBlockReason} />
          ) : path === routes.queue ? (
            <StatutoryDiscountQueuePage client={client} navigate={navigate} readinessBlockReason={readinessBlockReason} />
          ) : path === routes.audit ? (
            <AuditReportPage client={client} />
          ) : path === routes.vendorAcknowledgments ? (
            <VendorPaymentAcknowledgmentsPage client={client} />
          ) : path === routes.vendorProjectionHealth ? (
            <VendorSessionProjectionHealthPage client={client} />
          ) : path === routes.policyImportReview ? (
            <ProductionPolicyImportReviewPage client={client} readinessBlockReason={readinessBlockReason} />
          ) : path === routes.home ? (
            <OperatorConsoleHome navigate={navigate} readinessBlockReason={readinessBlockReason} />
          ) : (
            <NotFoundPage navigate={navigate} />
          )}
        </section>
      </section>
    </main>
  );
}

function AuditReportPage({ client }: { client: OperatorConsoleApiClient }) {
  const [filters, setFilters] = useState<AuditReportQuery>({ limit: 25, offset: 0 });
  const [draftFilters, setDraftFilters] = useState<AuditReportQuery>({ limit: 25, offset: 0 });
  const [reportState, setReportState] = useState<LoadState<AuditReportResponse>>({ status: "loading" });

  useEffect(() => {
    let active = true;
    setReportState({ status: "loading" });

    client
      .listAuditReport(filters)
      .then((report) => {
        if (!active) {
          return;
        }

        setReportState(report.items.length === 0 ? { status: "empty" } : { status: "loaded", data: report });
      })
      .catch((error) => {
        if (!active) {
          return;
        }

        setReportState({ status: "error", message: mapApiError(error).message });
      });

    return () => {
      active = false;
    };
  }, [client, filters]);

  function submitFilters(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setFilters({
      ...draftFilters,
      limit: 25,
      offset: 0
    });
  }

  return (
    <>
      <section className="pageTitle">
        <div>
          <p className="eyebrow">Audit / Reporting</p>
          <h2>Statutory discount audit report</h2>
          <p>
            Read-only audit/reporting view for statutory discount validation and access readiness review.
          </p>
        </div>
      </section>

      <section className="panel auditGuardrail" aria-labelledby="audit-guardrail-title">
        <div className="panelHeader">
          <h3 id="audit-guardrail-title">Read-only boundaries</h3>
          <span className="statusPill">Safe summary</span>
        </div>
        <p>Read-only audit/reporting view.</p>
        <p>No payment, gate, coupon, reconciliation, or evidence-file action is performed here.</p>
        <p>Raw ID numbers and raw evidence files are not displayed.</p>
      </section>

      <section className="panel" aria-labelledby="audit-filters-title">
        <div className="panelHeader">
          <h3 id="audit-filters-title">Filters</h3>
        </div>
        <form className="auditFilterGrid" onSubmit={submitFilters}>
          <label>
            Status
            <select
              value={draftFilters.validationStatus ?? ""}
              onChange={(event) => setDraftFilters((current) => ({ ...current, validationStatus: event.target.value || undefined }))}
            >
              <option value="">Any status</option>
              <option value="REQUESTED">Requested</option>
              <option value="APPROVED">Approved</option>
              <option value="REJECTED">Rejected</option>
              <option value="BLOCKED">Blocked</option>
              <option value="EXPIRED">Expired</option>
              <option value="CANCELLED">Cancelled</option>
            </select>
          </label>
          <label>
            Site ID
            <input
              value={draftFilters.siteId ?? ""}
              onChange={(event) => setDraftFilters((current) => ({ ...current, siteId: event.target.value || undefined }))}
            />
          </label>
          <label>
            Parking session ID
            <input
              value={draftFilters.parkingSessionId ?? ""}
              onChange={(event) => setDraftFilters((current) => ({ ...current, parkingSessionId: event.target.value || undefined }))}
            />
          </label>
          <label>
            Date from
            <input
              type="datetime-local"
              value={draftFilters.from ?? ""}
              onChange={(event) => setDraftFilters((current) => ({ ...current, from: event.target.value || undefined }))}
            />
          </label>
          <label>
            Date to
            <input
              type="datetime-local"
              value={draftFilters.to ?? ""}
              onChange={(event) => setDraftFilters((current) => ({ ...current, to: event.target.value || undefined }))}
            />
          </label>
          <button type="submit">Apply filters</button>
        </form>
      </section>

      <section className="panel" aria-labelledby="audit-results-title">
        <div className="panelHeader">
          <h3 id="audit-results-title">Report results</h3>
          {reportState.status === "loaded" && <span className="statusPill">{reportState.data.totalCount} rows</span>}
        </div>

        {reportState.status === "loading" && <StateMessage title="Loading audit report" message="Retrieving safe reporting rows." />}
        {reportState.status === "empty" && <StateMessage title="No report rows" message="No statutory discount audit rows matched the filters." />}
        {reportState.status === "error" && <StateMessage title="Unable to load audit report" message={reportState.message} />}
        {reportState.status === "loaded" && (
          <>
            <p className="placeholderCopy">Correlation ID: {reportState.data.correlationId}</p>
            <div className="tableScroller">
              <table>
                <thead>
                  <tr>
                    <th>Ticket Reference</th>
                    <th>Session ID</th>
                    <th>Entitlement</th>
                    <th>Policy Readiness</th>
                    <th>Validation Status</th>
                    <th>Evidence Status</th>
                    <th>Evidence Satisfied</th>
                    <th>Payable Basis Status</th>
                    <th>Original Amount</th>
                    <th>Discount</th>
                    <th>Final Payable</th>
                    <th>Currency</th>
                    <th>Requested At</th>
                    <th>Validated At</th>
                    <th>Correlation ID</th>
                    <th>Access Summary</th>
                  </tr>
                </thead>
                <tbody>
                  {reportState.data.items.map((item) => (
                    <tr key={item.statutoryDiscountValidationId}>
                      <td>{item.ticketReference ?? "Not available"}</td>
                      <td><code>{shortId(item.parkingSessionId)}</code></td>
                      <td>{item.entitlementType}</td>
                      <td>
                        <AuditPolicyReadinessSummary item={item} />
                      </td>
                      <td><span className={`statusPill ${statusClass(item.validationStatus)}`}>{item.validationStatus}</span></td>
                      <td>{item.latestEvidenceStatus ?? (item.evidenceRequired ? "Pending" : "Not required")}</td>
                      <td>{item.evidenceRequiredSatisfied ? "Yes" : "No"}</td>
                      <td>{item.payableBasisApplicationStatus ?? "Not applied"}</td>
                      <td>{formatMoney(item.originalAmountMinorUnits, item.currencyCode)}</td>
                      <td>{formatMoney(item.statutoryDiscountAmountMinorUnits, item.currencyCode)}</td>
                      <td>{formatMoney(item.finalPayableAmountMinorUnits, item.currencyCode)}</td>
                      <td>{item.currencyCode ?? "Not available"}</td>
                      <td>{formatDateTime(item.requestedAt)}</td>
                      <td>{item.validatedAt ? formatDateTime(item.validatedAt) : "Not validated"}</td>
                      <td>{item.correlationId ?? "Not available"}</td>
                      <td>{item.accessEvaluationSummary ?? "Not available"}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}
      </section>
    </>
  );
}

const vendorAcknowledgmentStatuses: VendorPaymentAcknowledgmentStatus[] = [
  "PENDING",
  "RETRY_PENDING",
  "FAILED",
  "CONFIRMED",
  "SKIPPED_DISABLED",
  "CANCELLED"
];

function VendorPaymentAcknowledgmentsPage({ client }: { client: OperatorConsoleApiClient }) {
  const [filters, setFilters] = useState<VendorPaymentAcknowledgmentSearchInput>({
    pageIndex: 0,
    pageSize: 25,
    nextRetryDueOnly: false
  });
  const [draftFilters, setDraftFilters] = useState<VendorPaymentAcknowledgmentSearchInput>({
    pageIndex: 0,
    pageSize: 25,
    nextRetryDueOnly: false
  });
  const [refreshToken, setRefreshToken] = useState(0);
  const [searchState, setSearchState] = useState<LoadState<VendorPaymentAcknowledgmentSearchResult>>({ status: "loading" });
  const [selectedAcknowledgmentId, setSelectedAcknowledgmentId] = useState<string | null>(null);
  const [detailState, setDetailState] = useState<LoadState<VendorPaymentAcknowledgmentDetail>>({ status: "idle" });

  useEffect(() => {
    let active = true;
    setSearchState({ status: "loading" });

    client
      .searchVendorPaymentAcknowledgments(filters)
      .then((result) => {
        if (!active) {
          return;
        }

        setSearchState(result.items.length === 0 ? { status: "empty" } : { status: "loaded", data: result });
      })
      .catch((error) => {
        if (!active) {
          return;
        }

        const mapped = mapApiError(error);
        setSearchState(
          mapped.status === "access-denied"
            ? { status: "access-denied", message: mapped.message }
            : { status: "error", message: mapped.message }
        );
      });

    return () => {
      active = false;
    };
  }, [client, filters, refreshToken]);

  useEffect(() => {
    if (!selectedAcknowledgmentId) {
      setDetailState({ status: "idle" });
      return;
    }

    let active = true;
    setDetailState({ status: "loading" });
    client
      .getVendorPaymentAcknowledgment(selectedAcknowledgmentId)
      .then((detail) => {
        if (active) {
          setDetailState({ status: "loaded", data: detail });
        }
      })
      .catch((error) => {
        if (!active) {
          return;
        }

        const mapped = mapApiError(error);
        setDetailState(
          mapped.status === "not-found"
            ? { status: "not-found" }
            : mapped.status === "access-denied"
              ? { status: "access-denied", message: mapped.message }
              : { status: "error", message: mapped.message }
        );
      });

    return () => {
      active = false;
    };
  }, [client, selectedAcknowledgmentId]);

  function submitFilters(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setFilters({
      acknowledgmentStatus: draftFilters.acknowledgmentStatus || undefined,
      vendorSystemCode: draftFilters.vendorSystemCode || undefined,
      ticketNumber: draftFilters.ticketNumber || undefined,
      cardNum: draftFilters.cardNum || undefined,
      nextRetryDueOnly: draftFilters.nextRetryDueOnly ?? false,
      pageIndex: draftFilters.pageIndex ?? 0,
      pageSize: draftFilters.pageSize ?? 25
    });
  }

  function updateDraftFilter<K extends keyof VendorPaymentAcknowledgmentSearchInput>(
    key: K,
    value: VendorPaymentAcknowledgmentSearchInput[K]
  ) {
    setDraftFilters((current) => ({ ...current, [key]: value }));
  }

  return (
    <>
      <section className="pageTitle">
        <div>
          <p className="eyebrow">Vendor PMS Monitoring</p>
          <h2>Vendor payment acknowledgments</h2>
          <p>
            Read-only monitoring for Vendor PMS paid-state acknowledgments after ExitPass payment finality.
          </p>
        </div>
        <button type="button" onClick={() => setRefreshToken((current) => current + 1)}>
          Refresh
        </button>
      </section>

      <section className="panel auditGuardrail" aria-labelledby="vendor-ack-boundaries-title">
        <div className="panelHeader">
          <h3 id="vendor-ack-boundaries-title">Read-only boundaries</h3>
          <span className="statusPill">Ops monitoring</span>
        </div>
        <p>Vendor PMS acknowledgment is not ExitPass payment finality.</p>
        <p>No retry, confirm, cancel, payment, vendor adapter, or gate action is available here.</p>
        <p>Secret-bearing payloads, signatures, and auth headers are not displayed.</p>
      </section>

      <section className="panel" aria-labelledby="vendor-ack-filters-title">
        <div className="panelHeader">
          <h3 id="vendor-ack-filters-title">Filters</h3>
        </div>
        <form className="auditFilterGrid" onSubmit={submitFilters}>
          <label>
            Status
            <select
              value={draftFilters.acknowledgmentStatus ?? ""}
              onChange={(event) =>
                updateDraftFilter("acknowledgmentStatus", event.target.value as VendorPaymentAcknowledgmentStatus | "")
              }
            >
              <option value="">Any status</option>
              {vendorAcknowledgmentStatuses.map((status) => (
                <option key={status} value={status}>
                  {status}
                </option>
              ))}
            </select>
          </label>
          <label>
            Vendor system code
            <input
              value={draftFilters.vendorSystemCode ?? ""}
              onChange={(event) => updateDraftFilter("vendorSystemCode", event.target.value || undefined)}
            />
          </label>
          <label>
            Ticket number
            <input
              value={draftFilters.ticketNumber ?? ""}
              onChange={(event) => updateDraftFilter("ticketNumber", event.target.value || undefined)}
            />
          </label>
          <label>
            Card number
            <input
              value={draftFilters.cardNum ?? ""}
              onChange={(event) => updateDraftFilter("cardNum", event.target.value || undefined)}
            />
          </label>
          <label>
            Page index
            <input
              min="0"
              type="number"
              value={draftFilters.pageIndex ?? 0}
              onChange={(event) => updateDraftFilter("pageIndex", Math.max(0, Number(event.target.value) || 0))}
            />
          </label>
          <label>
            Page size
            <select
              value={draftFilters.pageSize ?? 25}
              onChange={(event) => updateDraftFilter("pageSize", Number(event.target.value))}
            >
              <option value={10}>10</option>
              <option value={25}>25</option>
              <option value={50}>50</option>
              <option value={100}>100</option>
            </select>
          </label>
          <label className="checkboxField">
            <input
              checked={draftFilters.nextRetryDueOnly ?? false}
              type="checkbox"
              onChange={(event) => updateDraftFilter("nextRetryDueOnly", event.target.checked)}
            />
            Next retry due only
          </label>
          <button type="submit">Apply filters</button>
        </form>
      </section>

      <section className="panel" aria-labelledby="vendor-ack-results-title">
        <div className="panelHeader">
          <h3 id="vendor-ack-results-title">Acknowledgments</h3>
          {searchState.status === "loaded" && (
            <span className="statusPill">
              Page {searchState.data.pageIndex} / {searchState.data.items.length} rows
            </span>
          )}
        </div>

        {searchState.status === "loading" && (
          <StateMessage title="Loading vendor acknowledgments" message="Retrieving read-only acknowledgment rows." />
        )}
        {searchState.status === "empty" && (
          <StateMessage title="No acknowledgments" message="No vendor payment acknowledgments matched the filters." />
        )}
        {searchState.status === "access-denied" && <StateMessage title="Access denied" message={searchState.message} />}
        {searchState.status === "error" && <StateMessage title="Unable to load acknowledgments" message={searchState.message} />}
        {searchState.status === "loaded" && (
          <>
            <VendorAcknowledgmentStatusBuckets result={searchState.data} />
            <div className="tableScroller">
              <table>
                <thead>
                  <tr>
                    <th>Status</th>
                    <th>Vendor</th>
                    <th>Ticket/Card</th>
                    <th>Payment Attempt ID</th>
                    <th>Payment Confirmation ID</th>
                    <th>Vendor Code</th>
                    <th>Vendor Message</th>
                    <th>Attempt Count</th>
                    <th>Last Attempted At</th>
                    <th>Next Retry At</th>
                    <th>Vendor Confirmed At</th>
                    <th>Correlation ID</th>
                    <th>Details</th>
                  </tr>
                </thead>
                <tbody>
                  {searchState.data.items.map((item) => (
                    <VendorPaymentAcknowledgmentRow
                      key={item.vendorPaymentAcknowledgmentId}
                      item={item}
                      selected={selectedAcknowledgmentId === item.vendorPaymentAcknowledgmentId}
                      onSelect={() => setSelectedAcknowledgmentId(item.vendorPaymentAcknowledgmentId)}
                    />
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}
      </section>

      <VendorPaymentAcknowledgmentDetailPanel state={detailState} />
    </>
  );
}

function VendorAcknowledgmentStatusBuckets({ result }: { result: VendorPaymentAcknowledgmentSearchResult }) {
  const buckets = result.statusBuckets;
  return (
    <div className="statusStack" aria-label="Vendor acknowledgment status buckets">
      <span className="statusPill pending-review">PENDING {buckets.pending}</span>
      <span className="statusPill pending-review">RETRY_PENDING {buckets.retryPending}</span>
      <span className="statusPill blocked">FAILED {buckets.failed}</span>
      <span className="statusPill readiness-ready">CONFIRMED {buckets.confirmed}</span>
      <span className="statusPill">SKIPPED_DISABLED {buckets.skippedDisabled}</span>
      <span className="statusPill">CANCELLED {buckets.cancelled}</span>
      {result.hasMore && <span className="statusPill warningPill">More pages available</span>}
    </div>
  );
}

function VendorPaymentAcknowledgmentRow({
  item,
  selected,
  onSelect
}: {
  item: VendorPaymentAcknowledgmentSummary;
  selected: boolean;
  onSelect: () => void;
}) {
  return (
    <tr>
      <td>
        <span className={`statusPill ${vendorAcknowledgmentStatusClass(item.acknowledgmentStatus)}`}>
          {item.acknowledgmentStatus}
        </span>
      </td>
      <td>{displayValue(item.vendorSystemCode)}</td>
      <td>
        <strong>{displayValue(item.ticketNumber)}</strong>
        <span>{displayValue(item.cardNum)}</span>
      </td>
      <td><code>{shortId(item.paymentAttemptId)}</code></td>
      <td><code>{shortId(item.paymentConfirmationId)}</code></td>
      <td>{displayValue(item.vendorCode)}</td>
      <td>{displayValue(item.vendorMessage)}</td>
      <td>{item.attemptCount}</td>
      <td>{formatOptionalDateTime(item.lastAttemptedAt)}</td>
      <td>{formatOptionalDateTime(item.nextRetryAt)}</td>
      <td>{formatOptionalDateTime(item.vendorConfirmedAt)}</td>
      <td>{displayValue(item.correlationId)}</td>
      <td>
        <button type="button" onClick={onSelect}>
          {selected ? "Selected" : "View details"}
        </button>
      </td>
    </tr>
  );
}

function VendorPaymentAcknowledgmentDetailPanel({ state }: { state: LoadState<VendorPaymentAcknowledgmentDetail> }) {
  if (state.status === "idle") {
    return (
      <section className="panel">
        <StateMessage title="No acknowledgment selected" message="Select a row to read durable vendor acknowledgment detail." />
      </section>
    );
  }

  if (state.status === "loading") {
    return <StateMessage title="Loading acknowledgment detail" message="Retrieving safe detail fields." />;
  }

  if (state.status === "not-found") {
    return <StateMessage title="Acknowledgment not found" message="The selected vendor acknowledgment was not found." />;
  }

  if (state.status === "access-denied") {
    return <StateMessage title="Access denied" message={state.message} />;
  }

  if (state.status === "error") {
    return <StateMessage title="Unable to load acknowledgment detail" message={state.message} />;
  }

  if (state.status === "empty") {
    return <StateMessage title="No detail available" message="No vendor acknowledgment detail was returned." />;
  }

  if (state.status !== "loaded") {
    return null;
  }

  const detail = state.data;
  return (
    <section className="panel" aria-labelledby="vendor-ack-detail-title">
      <div className="panelHeader">
        <div>
          <p className="eyebrow">Acknowledgment detail</p>
          <h3 id="vendor-ack-detail-title">{detail.vendorPaymentAcknowledgmentId}</h3>
        </div>
        <span className={`statusPill ${vendorAcknowledgmentStatusClass(detail.acknowledgmentStatus)}`}>
          {detail.acknowledgmentStatus}
        </span>
      </div>

      <div className="detailGrid">
        <section aria-labelledby="vendor-ack-identity-heading">
          <h4 id="vendor-ack-identity-heading">Identity</h4>
          <DescriptionList
            items={[
              ["Vendor payment acknowledgment ID", detail.vendorPaymentAcknowledgmentId],
              ["Payment attempt ID", detail.paymentAttemptId],
              ["Payment confirmation ID", detail.paymentConfirmationId],
              ["Parking session ID", displayValue(detail.parkingSessionId)],
              ["Vendor system code", displayValue(detail.vendorSystemCode)],
              ["Vendor session ref", displayValue(detail.vendorSessionRef)]
            ]}
          />
        </section>

        <section aria-labelledby="vendor-ack-ticket-heading">
          <h4 id="vendor-ack-ticket-heading">Ticket</h4>
          <DescriptionList
            items={[
              ["Ticket number", displayValue(detail.ticketNumber)],
              ["Card number", displayValue(detail.cardNum)],
              ["Acknowledgment status", displayValue(detail.acknowledgmentStatus)],
              ["Vendor code", displayValue(detail.vendorCode)],
              ["Vendor message", displayValue(detail.vendorMessage)],
              ["Correlation ID", displayValue(detail.correlationId)]
            ]}
          />
        </section>

        <section aria-labelledby="vendor-ack-fees-heading">
          <h4 id="vendor-ack-fees-heading">Fees and attempts</h4>
          <DescriptionList
            items={[
              ["Request fee minor units", formatOptionalNumber(detail.requestFeeMinorUnits)],
              ["Request currency code", displayValue(detail.requestCurrencyCode)],
              ["Confirmed fee minor units", formatOptionalNumber(detail.confirmedFeeMinorUnits)],
              ["Vendor confirmed at", formatOptionalDateTime(detail.vendorConfirmedAt)],
              ["Attempt count", String(detail.attemptCount)],
              ["Last attempted at", formatOptionalDateTime(detail.lastAttemptedAt)],
              ["Next retry at", formatOptionalDateTime(detail.nextRetryAt)],
              ["Created at", formatDateTime(detail.createdAt)],
              ["Updated at", formatDateTime(detail.updatedAt)]
            ]}
          />
        </section>
      </div>

      <details className="diagnosticsPanel">
        <summary>Derived diagnostics</summary>
        {detail.diagnostics.length === 0 ? (
          <p className="placeholderCopy">No safe diagnostics were returned.</p>
        ) : (
          <ul className="activityList">
            {detail.diagnostics.map((diagnostic) => (
              <li key={`${diagnostic.code}-${diagnostic.message}`}>
                <strong>{diagnostic.code}</strong>
                <span>{diagnostic.message}</span>
                <span>{diagnostic.source}</span>
                <span>Retryable: {diagnostic.retryable ? "Yes" : "No"}</span>
                <span>Correlation ID: {displayValue(diagnostic.correlationId)}</span>
              </li>
            ))}
          </ul>
        )}
      </details>
    </section>
  );
}

function VendorSessionProjectionHealthPage({ client }: { client: OperatorConsoleApiClient }) {
  const [refreshToken, setRefreshToken] = useState(0);
  const [summaryState, setSummaryState] = useState<LoadState<VendorSessionProjectionHealthSummary>>({ status: "loading" });
  const [targetsState, setTargetsState] = useState<LoadState<VendorSessionProjectionHealthTargetsResponse>>({ status: "loading" });
  const [selectedTargetId, setSelectedTargetId] = useState<string | null>(null);
  const [detailState, setDetailState] = useState<LoadState<VendorSessionProjectionHealthTargetDetail>>({ status: "idle" });

  useEffect(() => {
    let active = true;
    setSummaryState({ status: "loading" });
    setTargetsState({ status: "loading" });

    client
      .getVendorSessionProjectionHealthSummary()
      .then((summary) => {
        if (active) {
          setSummaryState({ status: "loaded", data: summary });
        }
      })
      .catch((error) => {
        if (active) {
          setSummaryState({ status: "error", message: mapApiError(error).message });
        }
      });

    client
      .listVendorSessionProjectionHealthTargets()
      .then((result) => {
        if (active) {
          setTargetsState(result.targets.length === 0 ? { status: "empty" } : { status: "loaded", data: result });
        }
      })
      .catch((error) => {
        if (active) {
          const mapped = mapApiError(error);
          setTargetsState(
            mapped.status === "access-denied"
              ? { status: "access-denied", message: mapped.message }
              : { status: "error", message: mapped.message }
          );
        }
      });

    return () => {
      active = false;
    };
  }, [client, refreshToken]);

  useEffect(() => {
    if (!selectedTargetId) {
      setDetailState({ status: "idle" });
      return;
    }

    let active = true;
    setDetailState({ status: "loading" });
    client
      .getVendorSessionProjectionHealthTarget(selectedTargetId)
      .then((detail) => {
        if (active) {
          setDetailState({ status: "loaded", data: detail });
        }
      })
      .catch((error) => {
        if (!active) {
          return;
        }

        const mapped = mapApiError(error);
        setDetailState(
          mapped.status === "not-found"
            ? { status: "not-found" }
            : mapped.status === "access-denied"
              ? { status: "access-denied", message: mapped.message }
              : { status: "error", message: mapped.message }
        );
      });

    return () => {
      active = false;
    };
  }, [client, selectedTargetId]);

  const summary = summaryState.status === "loaded" ? summaryState.data : null;
  const targets = targetsState.status === "loaded" ? targetsState.data.targets : [];
  const hasStaleOrFailingTarget = targets.some((target) => target.isStale || target.healthStatus.toUpperCase() === "FAILING");
  const config = summary?.config ?? (targetsState.status === "loaded" ? targetsState.data.config : null);

  return (
    <>
      <section className="pageTitle">
        <div>
          <p className="eyebrow">Vendor PMS Monitoring</p>
          <h2>HikCentral Projection Health</h2>
          <p>Read-only continuity snapshot visibility for HikCentral vendor session projections.</p>
        </div>
        <button type="button" onClick={() => setRefreshToken((current) => current + 1)}>
          Refresh
        </button>
      </section>

      <section className="panel auditGuardrail" aria-labelledby="projection-health-boundaries-title">
        <div className="panelHeader">
          <h3 id="projection-health-boundaries-title">Read-only boundaries</h3>
          <span className="statusPill">Non-authoritative projection</span>
        </div>
        <p>Projection data is continuity visibility only.</p>
        <p>Vendor PMS remains parking-session and tariff authority. ExitPass remains payment authority.</p>
        <p>No sync trigger, enable, disable, fallback toggle, payment, tariff, paid-state, or exit action is available here.</p>
        <p>This page uses read-only projection-health RBAC. Operator action readiness does not grant payment, tariff, sync, or exit controls.</p>
        <p>Raw HikCentral payloads, credentials, signatures, and database passwords are not displayed.</p>
      </section>

      {config?.degradedResolveFallbackEnabled && (
        <p className="notice" role="alert">
          Degraded resolve fallback is currently enabled. Confirm this is approved and freshness-bound before relying on
          continuity visibility.
        </p>
      )}
      {hasStaleOrFailingTarget && (
        <p className="notice" role="alert">
          One or more projection targets are stale or failing. Escalate if the state is unexpected for this environment.
        </p>
      )}

      <VendorSessionProjectionSummaryPanel state={summaryState} />
      <VendorSessionProjectionTargetsPanel
        state={targetsState}
        selectedTargetId={selectedTargetId}
        onSelect={setSelectedTargetId}
      />
      <VendorSessionProjectionDetailPanel state={detailState} config={config} />
    </>
  );
}

function VendorSessionProjectionSummaryPanel({ state }: { state: LoadState<VendorSessionProjectionHealthSummary> }) {
  if (state.status === "loading") {
    return <StateMessage title="Loading projection health summary" message="Retrieving read-only projection health totals." />;
  }

  if (state.status === "error") {
    return <StateMessage title="Unable to load projection health summary" message={state.message} />;
  }

  if (state.status !== "loaded") {
    return null;
  }

  const summary = state.data;
  return (
    <section className="panel" aria-labelledby="projection-summary-title">
      <div className="panelHeader">
        <h3 id="projection-summary-title">Projection summary</h3>
        <span className={`statusPill ${summary.staleTargets > 0 || summary.failingTargets > 0 ? "warningPill" : "readiness-ready"}`}>
          {summary.staleTargets > 0 || summary.failingTargets > 0 ? "Review required" : "Fresh"}
        </span>
      </div>
      <div className="projectionMetricGrid">
        <ProjectionMetric label="Total targets" value={summary.totalTargets} />
        <ProjectionMetric label="Enabled" value={summary.enabledTargets} />
        <ProjectionMetric label="Disabled" value={summary.disabledTargets} />
        <ProjectionMetric label="Healthy" value={summary.healthyTargets} />
        <ProjectionMetric label="Degraded" value={summary.degradedTargets} />
        <ProjectionMetric label="Failing" value={summary.failingTargets} emphasis={summary.failingTargets > 0 ? "blocked" : undefined} />
        <ProjectionMetric label="Stale" value={summary.staleTargets} emphasis={summary.staleTargets > 0 ? "warningPill" : undefined} />
        <ProjectionMetric label="Active projections" value={summary.totalActiveProjections} />
        <ProjectionMetric label="Exited projections" value={summary.totalExitedProjections} />
      </div>
      <DescriptionList
        items={[
          ["Latest successful sync", formatOptionalDateTime(summary.latestSuccessfulProjectionSyncAt ?? undefined)],
          ["Scheduler enabled", summary.config.schedulerEnabled ? "Yes" : "No"],
          ["Degraded fallback enabled", summary.config.degradedResolveFallbackEnabled ? "Yes" : "No"],
          ["Max projection age minutes", String(summary.config.maxProjectionAgeMinutes)],
          ["Max parallel site jobs", String(summary.config.maxParallelSiteJobs)],
          ["Scheduler scan interval seconds", String(summary.config.schedulerScanIntervalSeconds)]
        ]}
      />
    </section>
  );
}

function ProjectionMetric({ label, value, emphasis }: { label: string; value: number; emphasis?: string }) {
  return (
    <div className="projectionMetric">
      <span>{label}</span>
      <strong className={emphasis}>{value}</strong>
    </div>
  );
}

function VendorSessionProjectionTargetsPanel({
  state,
  selectedTargetId,
  onSelect
}: {
  state: LoadState<VendorSessionProjectionHealthTargetsResponse>;
  selectedTargetId: string | null;
  onSelect: (projectionSyncTargetId: string) => void;
}) {
  return (
    <section className="panel" aria-labelledby="projection-targets-title">
      <div className="panelHeader">
        <h3 id="projection-targets-title">Projection targets</h3>
        {state.status === "loaded" && <span className="statusPill">{state.data.targets.length} targets</span>}
      </div>

      {state.status === "loading" && <StateMessage title="Loading projection targets" message="Retrieving sync target health." />}
      {state.status === "empty" && <StateMessage title="No projection targets" message="No vendor session projection targets are configured." />}
      {state.status === "access-denied" && <StateMessage title="Access denied" message={state.message} />}
      {state.status === "error" && <StateMessage title="Unable to load projection targets" message={state.message} />}
      {state.status === "loaded" && (
        <div className="tableScroller">
          <table>
            <thead>
              <tr>
                <th>Parking lot</th>
                <th>Site</th>
                <th>Enabled</th>
                <th>Health</th>
                <th>Freshness</th>
                <th>Last success</th>
                <th>Last failure</th>
                <th>Failures</th>
                <th>Last error</th>
                <th>Projection counts</th>
                <th>Details</th>
              </tr>
            </thead>
            <tbody>
              {state.data.targets.map((target) => (
                <VendorSessionProjectionTargetRow
                  key={target.projectionSyncTargetId}
                  target={target}
                  selected={selectedTargetId === target.projectionSyncTargetId}
                  onSelect={() => onSelect(target.projectionSyncTargetId)}
                />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}

function VendorSessionProjectionTargetRow({
  target,
  selected,
  onSelect
}: {
  target: VendorSessionProjectionHealthTarget;
  selected: boolean;
  onSelect: () => void;
}) {
  return (
    <tr>
      <td>
        <strong>{displayValue(target.parkingLotName ?? undefined)}</strong>
        <span>Index {target.parkingLotIndexCode}</span>
      </td>
      <td>
        <code>{shortId(target.siteId)}</code>
        <span>Group {shortId(target.siteGroupId)}</span>
      </td>
      <td>
        <span className={`statusPill ${target.enabledFlag ? "readiness-ready" : ""}`}>
          {target.enabledFlag ? "Enabled" : "Disabled"}
        </span>
      </td>
      <td>
        <span className={`statusPill ${projectionHealthStatusClass(target.healthStatus)}`}>
          {target.healthStatus}
        </span>
      </td>
      <td>
        <span className={`statusPill ${target.isStale ? "warningPill" : "readiness-ready"}`}>
          {target.isStale ? "Stale" : "Fresh"}
        </span>
        <span>{formatFreshnessAge(target.freshnessAgeSeconds)}</span>
      </td>
      <td>{formatOptionalDateTime(target.lastSuccessAt ?? undefined)}</td>
      <td>{formatOptionalDateTime(target.lastFailureAt ?? undefined)}</td>
      <td>{target.failureCount}</td>
      <td>
        <strong>{displayValue(target.lastErrorCode ?? undefined)}</strong>
        <span>{displayValue(target.lastErrorMessage ?? undefined)}</span>
      </td>
      <td>
        <span>Active {target.activeProjectionCount}</span>
        <span>Exited {target.exitedProjectionCount}</span>
        <span>Cards {target.cardNumProjectionCount}</span>
        <span>Plates {target.plateLicenseProjectionCount}</span>
      </td>
      <td>
        <button type="button" onClick={onSelect}>
          {selected ? "Selected" : "View details"}
        </button>
      </td>
    </tr>
  );
}

function VendorSessionProjectionDetailPanel({
  state,
  config
}: {
  state: LoadState<VendorSessionProjectionHealthTargetDetail>;
  config: VendorSessionProjectionHealthConfig | null;
}) {
  if (state.status === "idle") {
    return (
      <section className="panel">
        <StateMessage title="No projection target selected" message="Select a target to read safe projection detail." />
      </section>
    );
  }

  if (state.status === "loading") {
    return <StateMessage title="Loading projection target detail" message="Retrieving latest safe projection rows." />;
  }

  if (state.status === "not-found") {
    return <StateMessage title="Projection target not found" message="The selected projection target was not found." />;
  }

  if (state.status === "access-denied") {
    return <StateMessage title="Access denied" message={state.message} />;
  }

  if (state.status === "error") {
    return <StateMessage title="Unable to load projection target detail" message={state.message} />;
  }

  if (state.status !== "loaded") {
    return null;
  }

  const detail = state.data;
  const target = detail.target;
  const effectiveConfig = config ?? detail.config;
  return (
    <section className="panel" aria-labelledby="projection-detail-title">
      <div className="panelHeader">
        <div>
          <p className="eyebrow">Projection target detail</p>
          <h3 id="projection-detail-title">{displayValue(target.parkingLotName ?? undefined)}</h3>
        </div>
        <span className={`statusPill ${projectionHealthStatusClass(target.healthStatus)}`}>{target.healthStatus}</span>
      </div>

      <div className="detailGrid">
        <section aria-labelledby="projection-target-metadata-heading">
          <h4 id="projection-target-metadata-heading">Target metadata</h4>
          <DescriptionList
            items={[
              ["Projection sync target ID", target.projectionSyncTargetId],
              ["Site ID", target.siteId],
              ["Site group ID", target.siteGroupId],
              ["Vendor system ID", target.vendorSystemId],
              ["Parking lot index code", target.parkingLotIndexCode],
              ["Parking lot name", displayValue(target.parkingLotName ?? undefined)]
            ]}
          />
        </section>
        <section aria-labelledby="projection-target-health-heading">
          <h4 id="projection-target-health-heading">Health and freshness</h4>
          <DescriptionList
            items={[
              ["Enabled", target.enabledFlag ? "Yes" : "No"],
              ["Health status", target.healthStatus],
              ["Stale", target.isStale ? "Yes" : "No"],
              ["Freshness age", formatFreshnessAge(target.freshnessAgeSeconds)],
              ["Latest projection refreshed at", formatOptionalDateTime(target.latestProjectionLastRefreshedAt ?? undefined)],
              ["Last success", formatOptionalDateTime(target.lastSuccessAt ?? undefined)],
              ["Last failure", formatOptionalDateTime(target.lastFailureAt ?? undefined)],
              ["Failure count", String(target.failureCount)]
            ]}
          />
        </section>
        <section aria-labelledby="projection-config-heading">
          <h4 id="projection-config-heading">Safe config visibility</h4>
          <DescriptionList
            items={[
              ["Scheduler enabled", effectiveConfig.schedulerEnabled ? "Yes" : "No"],
              ["Degraded fallback enabled", effectiveConfig.degradedResolveFallbackEnabled ? "Yes" : "No"],
              ["Max projection age minutes", String(effectiveConfig.maxProjectionAgeMinutes)],
              ["Max parallel site jobs", String(effectiveConfig.maxParallelSiteJobs)],
              ["Scheduler scan interval seconds", String(effectiveConfig.schedulerScanIntervalSeconds)]
            ]}
          />
        </section>
      </div>

      <section aria-labelledby="projection-latest-records-title">
        <div className="panelHeader">
          <h4 id="projection-latest-records-title">Latest projected records</h4>
          <span className="statusPill">Limited safe fields</span>
        </div>
        {detail.latestProjectedRecords.length === 0 ? (
          <p className="placeholderCopy">No latest projection rows were returned for this target.</p>
        ) : (
          <div className="tableScroller">
            <table>
              <thead>
                <tr>
                  <th>Status</th>
                  <th>Card/Plate</th>
                  <th>Vendor record</th>
                  <th>Enter/Exit</th>
                  <th>Last refreshed</th>
                  <th>Correlation</th>
                </tr>
              </thead>
              <tbody>
                {detail.latestProjectedRecords.map((record) => (
                  <VendorSessionProjectionLatestRecordRow key={record.vendorSessionProjectionId} record={record} />
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </section>
  );
}

function VendorSessionProjectionLatestRecordRow({ record }: { record: VendorSessionProjectionHealthLatestRecord }) {
  return (
    <tr>
      <td>
        <span className={`statusPill ${projectionStatusClass(record.projectionStatus)}`}>
          {record.projectionStatus}
        </span>
      </td>
      <td>
        <strong>{displayValue(record.cardNum ?? undefined)}</strong>
        <span>{displayPlateLicense(record.plateLicense ?? undefined)}</span>
      </td>
      <td>
        <code>{shortId(record.vendorSessionProjectionId)}</code>
        <span>{displayValue(record.vendorRecordGuid ?? undefined)}</span>
      </td>
      <td>
        <span>Enter {formatOptionalDateTime(record.enterTime ?? undefined)}</span>
        <span>Exit {formatOptionalDateTime(record.exitTime ?? undefined)}</span>
      </td>
      <td>{formatDateTime(record.lastRefreshedAt)}</td>
      <td>{displayValue(record.correlationId ?? undefined)}</td>
    </tr>
  );
}

function OperatorConsoleHome({
  navigate,
  readinessBlockReason
}: {
  navigate: (path: string) => void;
  readinessBlockReason: string | null;
}) {
  return (
    <section className="panel homePanel" aria-labelledby="home-title">
      <div className="panelHeader">
        <p className="eyebrow">Review workspace</p>
        <h2 id="home-title">Statutory discount validation foundation</h2>
      </div>
      <p>
        The first Operator Console module provides a work queue, draft details, and readable policy context for Senior
        Citizen and PWD statutory discount validation.
      </p>
      {readinessBlockReason && <p className="notice">{readinessBlockReason}</p>}
      <button type="button" disabled={readinessBlockReason !== null} onClick={() => navigate(routes.queue)}>
        Open work queue
      </button>
      <button type="button" disabled={readinessBlockReason !== null} onClick={() => navigate(routes.ticketLookup)}>
        Open ticket lookup
      </button>
    </section>
  );
}

function TicketLookupPage({
  client,
  readinessBlockReason
}: {
  client: OperatorConsoleApiClient;
  readinessBlockReason: string | null;
}) {
  const [ticketReference, setTicketReference] = useState("");
  const [lookupState, setLookupState] = useState<LoadState<OperatorTicketLookupResult>>({ status: "idle" });

  async function submitLookup(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const normalizedTicket = ticketReference.trim();
    if (!normalizedTicket) {
      setLookupState({ status: "error", message: "Scan or enter a ticket number." });
      return;
    }

    setLookupState({ status: "loading" });
    try {
      const result = await client.lookupSessionByTicket({ ticketNumber: normalizedTicket });
      setLookupState(result.sessionFound ? { status: "loaded", data: result } : { status: "not-found" });
    } catch (error) {
      const mapped = mapApiError(error);
      setLookupState(
        mapped.status === "access-denied"
          ? { status: "access-denied", message: mapped.message }
          : { status: "error", message: mapped.message }
      );
    }
  }

  return (
    <>
      <section className="pageTitle">
        <div>
          <p className="eyebrow">Ticket Lookup</p>
          <h2>Ticket exit readiness</h2>
          <p>Scan or enter a ticket number to review session, payment, and vendor confirmation state.</p>
        </div>
      </section>

      <section className="panel" aria-labelledby="ticket-lookup-title">
        <div className="panelHeader">
          <h3 id="ticket-lookup-title">Ticket number</h3>
          <span className="statusPill">Read-only lookup</span>
        </div>

        {readinessBlockReason && <p className="notice">{readinessBlockReason}</p>}
        <form className="ticketLookupForm" onSubmit={submitLookup}>
          <label>
            Ticket number
            <input
              autoComplete="off"
              autoFocus
              inputMode="text"
              name="ticketReference"
              placeholder="Scan or enter ticket number"
              value={ticketReference}
              onChange={(event) => setTicketReference(event.target.value)}
            />
          </label>
          <button type="submit" disabled={readinessBlockReason !== null || lookupState.status === "loading"}>
            {lookupState.status === "loading" ? "Looking up" : "Lookup"}
          </button>
        </form>

        <div className="lookupGuardrail" role="note">
          <p>No manual mark-as-paid.</p>
          <p>No payment collection.</p>
          <p>No direct gate open.</p>
        </div>
      </section>

      {lookupState.status === "loading" && <StateMessage title="Looking up ticket" message="Retrieving Operator Console session status." />}
      {lookupState.status === "not-found" && <StateMessage title="Ticket not found" message="No active session was found for this ticket." />}
      {lookupState.status === "access-denied" && <StateMessage title="Access denied" message={lookupState.message} />}
      {lookupState.status === "error" && <StateMessage title="Unable to look up ticket" message={lookupState.message} />}
      {lookupState.status === "loaded" && <TicketLookupSummary result={lookupState.data} />}
    </>
  );
}

function TicketLookupSummary({ result }: { result: OperatorTicketLookupResult }) {
  const guidance = ticketLookupGuidance(result);

  return (
    <section className="panel ticketSummaryPanel" aria-labelledby="ticket-summary-title">
      <div className="panelHeader">
        <div>
          <p className="eyebrow">Session summary</p>
          <h3 id="ticket-summary-title">{displayValue(result.ticketNumber ?? result.cardNum)}</h3>
        </div>
        <span className={`statusPill ${guidance.className}`}>{guidance.label}</span>
      </div>

      <div className={guidance.messageClass}>
        {guidance.messages.map((message) => (
          <p key={message}>{message}</p>
        ))}
      </div>

      <div className="detailGrid">
        <section aria-labelledby="ticket-session-heading">
          <h4 id="ticket-session-heading">Session Information</h4>
          <DescriptionList
            items={[
              ["Ticket number", displayValue(result.ticketNumber)],
              ["Card number", displayValue(result.cardNum)],
              ["Plate license", displayPlateLicense(result.plateLicense)],
              ["Parking in time", result.parkingInTime ? formatDateTime(result.parkingInTime) : "Not available"],
              ["Parking duration seconds", formatSeconds(result.parkingDurationSeconds)]
            ]}
          />
        </section>

        <section aria-labelledby="ticket-tariff-heading">
          <h4 id="ticket-tariff-heading">Tariff Information</h4>
          <DescriptionList
            items={[
              ["Fee minor units", formatMinorUnits(result.feeMinorUnits)],
              ["Currency code", displayValue(result.currencyCode)],
              ["Fee rule type", displayValue(result.feeRuleType)],
              ["Fee rule index code", displayValue(result.feeRuleIndexCode)],
              ["Fee rule name", displayValue(result.feeRuleName)]
            ]}
          />
        </section>

        <section aria-labelledby="ticket-payment-heading">
          <h4 id="ticket-payment-heading">Payment Information</h4>
          <DescriptionList
            items={[
              ["Payment attempt status", displayValue(result.paymentAttemptStatus)],
              ["Payment status", displayValue(result.paymentStatus)],
              ["Payment confirmation status", displayValue(result.paymentConfirmationStatus)]
            ]}
          />
        </section>

        <section aria-labelledby="ticket-vendor-heading">
          <h4 id="ticket-vendor-heading">Vendor Information</h4>
          <DescriptionList
            items={[
              ["Vendor system code", displayValue(result.vendorSystemCode)],
              ["Vendor confirmation code", displayValue(result.vendorConfirmationCode)],
              ["Vendor confirmation status", result.vendorConfirmationStatus ?? "Vendor confirmation unavailable"],
              [
                "Vendor confirmation timestamp",
                result.vendorConfirmationTimestamp ? formatDateTime(result.vendorConfirmationTimestamp) : "Not available"
              ],
              ["Vendor message", displayValue(result.vendorMessage)]
            ]}
          />
        </section>
      </div>

      <details className="diagnosticsPanel">
        <summary>Diagnostics</summary>
        <DescriptionList
          items={[
            ["Vendor system code", displayValue(result.vendorSystemCode)],
            ["Vendor confirmation code", displayValue(result.vendorConfirmationCode)],
            ["Vendor message", displayValue(result.vendorMessage)],
            ["Correlation ID", displayValue(result.correlationId)]
          ]}
        />
      </details>
    </section>
  );
}

function ProductionPolicyImportReviewPage({
  client,
  readinessBlockReason
}: {
  client: OperatorConsoleApiClient;
  readinessBlockReason: string | null;
}) {
  const [csvContent, setCsvContent] = useState(productionPolicyImportSampleCsv());
  const [fileName, setFileName] = useState("production-policy-candidate.csv");
  const [dryRunResult, setDryRunResult] = useState<ProductionPolicyImportDryRunResult | null>(null);
  const [reviewResult, setReviewResult] = useState<ProductionPolicyImportReviewResult | null>(null);
  const [reviewQueueState, setReviewQueueState] = useState<LoadState<ProductionPolicyImportReviewListResult>>({ status: "loading" });
  const [selectedReviewId, setSelectedReviewId] = useState<string | null>(null);
  const [reviewerRole, setReviewerRole] = useState<"LEGAL" | "OPS" | "QA" | "DB">("LEGAL");
  const [decisionReason, setDecisionReason] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState<"dry-run" | "submit-review" | ProductionPolicyImportReviewDecisionAction | null>(null);

  function loadReviewQueue(selectFirst = false) {
    setReviewQueueState({ status: "loading" });
    return client
      .listProductionPolicyImportReviews({ limit: 50, offset: 0 })
      .then((result) => {
        setReviewQueueState(result.items.length === 0 ? { status: "empty" } : { status: "loaded", data: result });
        if (selectFirst && !selectedReviewId && result.items[0]) {
          setSelectedReviewId(result.items[0].submission.reviewId);
        }
        return result;
      })
      .catch((caught) => {
        const mapped = mapApiError(caught);
        setReviewQueueState(
          mapped.status === "access-denied"
            ? { status: "access-denied", message: mapped.message }
            : { status: "error", message: mapped.message }
        );
        return null;
      });
  }

  useEffect(() => {
    let active = true;
    setReviewQueueState({ status: "loading" });
    client
      .listProductionPolicyImportReviews({ limit: 50, offset: 0 })
      .then((result) => {
        if (!active) {
          return;
        }

        setReviewQueueState(result.items.length === 0 ? { status: "empty" } : { status: "loaded", data: result });
        if (result.items[0]) {
          setSelectedReviewId((current) => current ?? result.items[0].submission.reviewId);
        }
      })
      .catch((caught) => {
        if (active) {
          const mapped = mapApiError(caught);
          setReviewQueueState(
            mapped.status === "access-denied"
              ? { status: "access-denied", message: mapped.message }
              : { status: "error", message: mapped.message }
          );
        }
      });

    return () => {
      active = false;
    };
  }, [client]);

  useEffect(() => {
    if (!selectedReviewId) {
      return;
    }

    let active = true;
    client
      .getProductionPolicyImportReview(selectedReviewId)
      .then((result) => {
        if (active) {
          setReviewResult(result);
        }
      })
      .catch((caught) => {
        if (active) {
          const mapped = mapApiError(caught);
          setReviewResult(null);
          setError(mapped.status === "access-denied" ? `Access denied: ${mapped.message}` : mapped.message);
        }
      });

    return () => {
      active = false;
    };
  }, [client, selectedReviewId]);

  async function runDryRun(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setMessage(null);
    setError(null);
    setReviewResult(null);
    setSelectedReviewId(null);

    if (!csvContent.trim()) {
      setError("CSV content is required.");
      return;
    }

    setSubmitting("dry-run");
    try {
      const result = await client.dryRunProductionPolicyImport({
        csvContent,
        fileName: fileName.trim() || undefined
      });
      setDryRunResult(result);
      setMessage(result.message);
    } catch (caught) {
      setError(mapApiError(caught).message);
    } finally {
      setSubmitting(null);
    }
  }

  async function submitForReview() {
    setMessage(null);
    setError(null);

    if (!dryRunResult) {
      setError("Run dry-run validation before submitting for review.");
      return;
    }

    setSubmitting("submit-review");
    try {
      const result = await client.submitProductionPolicyImportReview({
        dryRunResult,
        fileName: fileName.trim() || undefined
      });
      setReviewResult(result);
      setSelectedReviewId(result.submission.reviewId);
      setMessage(result.message);
      void loadReviewQueue(false);
    } catch (caught) {
      setError(mapApiError(caught).message);
    } finally {
      setSubmitting(null);
    }
  }

  async function decide(action: "APPROVE" | "REJECT" | "REQUEST_CHANGES" | "ESCALATE") {
    setMessage(null);
    setError(null);

    if (!canRecordReviewDecision) {
      setError("Access denied: the current operator is not authorized to record production policy import review decisions.");
      return;
    }

    if (!reviewResult) {
      setError("Submit the dry-run result for review before recording a decision.");
      return;
    }

    if (action !== "APPROVE" && decisionReason.trim().length === 0) {
      setError(`${action} requires a reason.`);
      return;
    }

    const mappedAction: ProductionPolicyImportReviewDecisionAction =
      action === "APPROVE" ? `APPROVE_${reviewerRole}` : action;

    setSubmitting(mappedAction);
    try {
      const result = await client.decideProductionPolicyImportReview({
        reviewId: reviewResult.submission.reviewId,
        action: mappedAction,
        reason: action === "APPROVE" ? decisionReason.trim() || "Approved for DB repo alignment." : decisionReason.trim()
      });
      const refreshed = await client.getProductionPolicyImportReview(result.submission.reviewId);
      setReviewResult(refreshed);
      setMessage(result.message);
      void loadReviewQueue(false);
    } catch (caught) {
      setError(mapApiError(caught).message);
    } finally {
      setSubmitting(null);
    }
  }

  const canRecordReviewDecision = client.canDecideProductionPolicyImportReview?.() ?? true;
  const decisionDisabled = readinessBlockReason !== null || reviewResult === null || submitting !== null || !canRecordReviewDecision;

  return (
    <>
      <section className="pageTitle">
        <div>
          <p className="eyebrow">Production Policy Import Review</p>
          <h2>DB-backed review queue</h2>
          <p>Dry-run candidate policies, then submit the dry-run result for review queue persistence.</p>
        </div>
        <span className="statusPill warningPill">No import execution</span>
      </section>

      <section className="panel auditGuardrail" aria-labelledby="policy-import-boundary-title">
        <div className="panelHeader">
          <h3 id="policy-import-boundary-title">Review-only boundary</h3>
          <span className="statusPill">Activation blocked</span>
        </div>
        <p>This screen does not execute production import.</p>
        <p>This screen does not activate production policies.</p>
        <p>Approval means DB repo alignment only.</p>
        <p>Final approved state is APPROVED_FOR_DB_REPO_ALIGNMENT, not production active.</p>
        {readinessBlockReason && <p className="notice">{readinessBlockReason}</p>}
      </section>

      <section className="panel" aria-labelledby="review-queue-title">
        <div className="panelHeader">
          <h3 id="review-queue-title">Review queue</h3>
          {reviewQueueState.status === "loaded" && <span className="statusPill">{reviewQueueState.data.totalCount} persisted</span>}
        </div>
        <p className="placeholderCopy">Persisted review queue records reload from the backend; approval remains DB repo alignment only.</p>
        <div className="actionBar">
          <button type="button" onClick={() => void loadReviewQueue(false)} disabled={submitting !== null}>
            Refresh reviews
          </button>
        </div>

        {reviewQueueState.status === "loading" && <StateMessage title="Loading review queue" message="Retrieving persisted review submissions." />}
        {reviewQueueState.status === "empty" && <StateMessage title="No persisted reviews" message="No production policy import reviews are in the queue." />}
        {reviewQueueState.status === "access-denied" && <StateMessage title="Access denied" message={reviewQueueState.message} />}
        {reviewQueueState.status === "error" && <StateMessage title="Unable to load review queue" message={reviewQueueState.message} />}
        {reviewQueueState.status === "loaded" && (
          <div className="tableScroller policyImportRows">
            <table>
              <thead>
                <tr>
                  <th>Review</th>
                  <th>Status</th>
                  <th>Dry-run summary</th>
                  <th>Safety state</th>
                  <th>Updated</th>
                  <th>Action</th>
                </tr>
              </thead>
              <tbody>
                {reviewQueueState.data.items.map((item) => (
                  <tr key={item.submission.reviewId}>
                    <td>
                      <code>{shortId(item.submission.reviewId)}</code>
                      <span>{item.submission.fileName ?? "No file name"}</span>
                    </td>
                    <td><span className={`statusPill ${statusClass(item.submission.status)}`}>{item.submission.status}</span></td>
                    <td>
                      <strong>{item.submission.dryRunSummary.totalRows} rows</strong>
                      <span>{item.submission.dryRunSummary.failCount} fail / {item.submission.dryRunSummary.importableCount} importable</span>
                    </td>
                    <td>
                      <span>imported={String(item.imported)}</span>
                      <span>productionPolicyActivationBlocked={String(item.productionPolicyActivationBlocked)}</span>
                      <span>DB repo alignment only</span>
                    </td>
                    <td>{formatDateTime(item.submission.updatedAt)}</td>
                    <td>
                      <button type="button" onClick={() => setSelectedReviewId(item.submission.reviewId)}>
                        Inspect review
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="panel" aria-labelledby="dry-run-title">
        <div className="panelHeader">
          <h3 id="dry-run-title">Dry-run validation</h3>
          <span className="statusPill">imported=false</span>
        </div>
        <form className="policyImportForm" onSubmit={(event) => void runDryRun(event)}>
          <label>
            File name
            <input value={fileName} onChange={(event) => setFileName(event.target.value)} />
          </label>
          <label>
            Candidate CSV
            <textarea value={csvContent} onChange={(event) => setCsvContent(event.target.value)} />
          </label>
          <button type="submit" disabled={readinessBlockReason !== null || submitting !== null}>
            {submitting === "dry-run" ? "Running dry-run" : "Run dry-run"}
          </button>
        </form>
        {message && <p className="successMessage">{message}</p>}
        {error && <p className="errorMessage">{error}</p>}
      </section>

      {dryRunResult && (
        <section className="panel" aria-labelledby="dry-run-result-title">
          <div className="panelHeader">
            <h3 id="dry-run-result-title">Dry-run result</h3>
            <span className="statusPill">{dryRunResult.dryRunOnly ? "Dry-run only" : "Unexpected state"}</span>
          </div>
          <DescriptionList
            items={[
              ["Imported", String(dryRunResult.imported)],
              ["Imported row count", String(dryRunResult.importedRowCount)],
              ["Dry-run only", String(dryRunResult.dryRunOnly)],
              ["Total rows", String(dryRunResult.summary.totalRows)],
              ["Importable rows", String(dryRunResult.summary.importableCount)],
              ["Fail count", String(dryRunResult.summary.failCount)],
              ["Correlation ID", dryRunResult.correlationId]
            ]}
          />
          <div className="actionBar">
            <button
              type="button"
              disabled={readinessBlockReason !== null || submitting !== null}
              onClick={() => void submitForReview()}
            >
              {submitting === "submit-review" ? "Submitting for review" : "Submit for review"}
            </button>
          </div>
          {dryRunResult.rows.length > 0 && (
            <div className="tableScroller policyImportRows">
              <table>
                <thead>
                  <tr>
                    <th>Row</th>
                    <th>Policy</th>
                    <th>Entitlement</th>
                    <th>Decision</th>
                    <th>Findings</th>
                  </tr>
                </thead>
                <tbody>
                  {dryRunResult.rows.map((row) => (
                    <tr key={`${row.rowNumber}-${row.policyCode ?? "policy"}`}>
                      <td>{row.rowNumber}</td>
                      <td>{row.policyCode ?? "Not available"}</td>
                      <td>{row.entitlementType ?? "Not available"}</td>
                      <td>{row.decision}</td>
                      <td>{row.findings.map((finding) => finding.message).join("; ") || "None"}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      )}

      {reviewResult && (
        <section className="panel" aria-labelledby="review-result-title">
          <div className="panelHeader">
            <h3 id="review-result-title">Persisted review</h3>
            <span className={`statusPill ${statusClass(reviewResult.submission.status)}`}>{reviewResult.submission.status}</span>
          </div>
          <DescriptionList
            items={[
              ["Review ID", reviewResult.submission.reviewId],
              ["Status", reviewResult.submission.status],
              ["Imported", String(reviewResult.imported)],
              ["Production policy activation blocked", String(reviewResult.productionPolicyActivationBlocked)],
              ["Approval meaning", "DB repo alignment only"],
              ["Final approved state", "APPROVED_FOR_DB_REPO_ALIGNMENT"],
              ["Created at", formatDateTime(reviewResult.submission.createdAt)],
              ["Updated at", formatDateTime(reviewResult.submission.updatedAt)],
              ["Dry-run total rows", String(reviewResult.submission.dryRunSummary.totalRows)],
              ["Dry-run fail count", String(reviewResult.submission.dryRunSummary.failCount)],
              ["Dry-run importable rows", String(reviewResult.submission.dryRunSummary.importableCount)],
              ["History count", String(reviewResult.submission.history.length)],
              ["Decision count", String(reviewResult.submission.reviewerDecisions.length)]
            ]}
          />

          {canRecordReviewDecision ? (
            <div className="policyReviewDecisionControls" aria-label="Production policy review decision controls">
              <label>
                Reviewer role for approve
                <select value={reviewerRole} onChange={(event) => setReviewerRole(event.target.value as typeof reviewerRole)}>
                  <option value="LEGAL">Legal</option>
                  <option value="OPS">Ops</option>
                  <option value="QA">QA</option>
                  <option value="DB">DB</option>
                </select>
              </label>
              <label>
                Decision reason
                <input
                  value={decisionReason}
                  placeholder="Reviewed for DB repo alignment"
                  onChange={(event) => setDecisionReason(event.target.value)}
                />
              </label>
              <div className="actionBar">
                <button type="button" disabled={decisionDisabled} onClick={() => void decide("APPROVE")}>
                  Approve
                </button>
                <button type="button" disabled={decisionDisabled} onClick={() => void decide("REJECT")}>
                  Reject
                </button>
                <button type="button" disabled={decisionDisabled} onClick={() => void decide("REQUEST_CHANGES")}>
                  Request changes
                </button>
                <button type="button" disabled={decisionDisabled} onClick={() => void decide("ESCALATE")}>
                  Escalate
                </button>
              </div>
            </div>
          ) : (
            <p className="notice">
              Access denied: the current operator can view this review but is not authorized to record reviewer decisions.
            </p>
          )}

          {reviewResult.findings.length > 0 && (
            <ul className="activityList" aria-label="Review findings">
              {reviewResult.findings.map((finding) => (
                <li key={`${finding.severity}-${finding.message}`}>
                  {finding.severity}: {finding.message}
                </li>
              ))}
            </ul>
          )}
          <div className="reviewDetailGrid">
            <section aria-labelledby="review-decisions-title">
              <h4 id="review-decisions-title">Reviewer decisions</h4>
              {reviewResult.submission.reviewerDecisions.length === 0 ? (
                <p className="placeholderCopy">No reviewer decisions recorded.</p>
              ) : (
                <ul className="activityList">
                  {reviewResult.submission.reviewerDecisions.map((decision) => (
                    <li key={`${decision.reviewerRole}-${decision.decidedAt}`}>
                      {decision.reviewerRole}: {decision.action} by {shortId(decision.reviewerOperatorId)} at {formatDateTime(decision.decidedAt)}
                    </li>
                  ))}
                </ul>
              )}
            </section>
            <section aria-labelledby="review-history-title">
              <h4 id="review-history-title">Decision history</h4>
              <ul className="activityList">
                {reviewResult.submission.history.map((entry) => (
                  <li key={`${entry.action}-${entry.occurredAt}`}>
                    {entry.action} - {entry.status} - {formatDateTime(entry.occurredAt)}
                  </li>
                ))}
              </ul>
            </section>
          </div>
        </section>
      )}
    </>
  );
}

function AccessReadinessPanel({
  state,
  usesLocalDevFallbackContext,
  onRefresh
}: {
  state: LoadState<AccessReadinessResponse>;
  usesLocalDevFallbackContext: boolean;
  onRefresh: () => void;
}) {
  const readiness = state.status === "loaded" ? state.data : null;
  const blocked = readiness?.accessAllowed === false;
  const fallbackDenied = readiness?.denialReasons.some((reason) => reason.code === "LOCAL_DEV_CONTEXT_NOT_ALLOWED_IN_PRODUCTION");

  return (
    <section
      className={`panel readinessPanel ${blocked ? "readinessBlocked" : readiness ? "readinessReady" : "readinessPending"}`}
      aria-labelledby="access-readiness-title"
    >
      <div className="panelHeader">
        <div>
          <p className="eyebrow">Access readiness</p>
          <h3 id="access-readiness-title">Operator readiness state</h3>
        </div>
        <button type="button" onClick={onRefresh}>
          {state.status === "loading" ? "Checking readiness" : "Refresh readiness"}
        </button>
      </div>

      {usesLocalDevFallbackContext && (
        <p className="sandboxIndicator">
          Sandbox/local validation context is active. This is not production trust.
        </p>
      )}

      {state.status === "idle" && <StateMessage title="Readiness not checked" message="Check readiness before controlled actions." />}
      {state.status === "loading" && <StateMessage title="Checking readiness" message="Evaluating operator, device, shift, site, and workflow state." />}
      {state.status === "error" && (
        <StateMessage
          title="Unable to check readiness"
          message={`${state.message} Read-only monitoring pages may still load when their RBAC checks allow access.`}
        />
      )}

      {readiness && (
        <>
          <DescriptionList
            items={[
              ["Overall readiness", readiness.readinessStatus],
              ["Access decision", readiness.accessDecision],
              ["Requested action", readiness.requestedAction],
              ["Operator readiness", readinessLabel(readiness.operatorReadiness.ready, readiness.operatorReadiness.status)],
              ["Device readiness", readinessLabel(readiness.deviceReadiness.ready, readiness.deviceReadiness.status)],
              ["Shift readiness", readinessLabel(readiness.shiftReadiness.ready, readiness.shiftReadiness.status)],
              ["Site readiness", readinessLabel(readiness.siteReadiness.ready, readiness.siteReadiness.status)],
              ["Workflow readiness", readinessLabel(readiness.workflowReadiness.ready, readiness.workflowReadiness.status)],
              ["Audit persisted", readiness.auditPersisted ? "Yes" : "No"],
              ["Correlation ID", readiness.correlationId],
              ["Evaluated at", formatDateTime(readiness.evaluatedAt)]
            ]}
          />

          <div className="readinessDimensionGrid" aria-label="Readiness dimensions">
            {readiness.readinessDimensions.map((dimension) => (
              <div
                className={`readinessDimension ${dimension.denialReasonCodes.length > 0 ? "dimensionBlocked" : "dimensionReady"}`}
                key={dimension.dimension}
              >
                <span>{dimension.required ? "Required" : "Optional"}</span>
                <strong>{dimension.dimension}</strong>
                <span>{dimension.status}</span>
                {dimension.denialReasonCodes.length > 0 && <code>{dimension.denialReasonCodes.join(", ")}</code>}
              </div>
            ))}
          </div>

          {blocked && (
            <div className="readinessDenial" role="alert">
              <p>This device, shift, or site is not ready for controlled Operator Console actions.</p>
              <p>Read-only monitoring pages may still load when their RBAC checks allow access.</p>
              <p>Contact a supervisor or support and provide the correlation ID.</p>
              {fallbackDenied && <p>Local/dev fallback context is not accepted as production trust.</p>}
              {readiness.nextOperatorAction && <p>Next action: {readiness.nextOperatorAction}</p>}
              <p>Retryable: {readiness.retryable ? "Yes" : "No"}</p>
              <ul className="denialReasonList">
                {readiness.denialReasons.map((reason) => (
                  <li key={reason.code}>
                    <code>{reason.code}</code>
                    <span>{reason.severity} / {reason.uxMessageCategory}</span>
                  </li>
                ))}
              </ul>
            </div>
          )}
        </>
      )}
    </section>
  );
}

function StatutoryDiscountQueuePage({
  client,
  navigate,
  readinessBlockReason
}: {
  client: OperatorConsoleApiClient;
  navigate: (path: string) => void;
  readinessBlockReason: string | null;
}) {
  const [queueState, setQueueState] = useState<LoadState<StatutoryDiscountQueueItem[]>>({ status: "loading" });
  const [refreshToken, setRefreshToken] = useState(0);

  useEffect(() => {
    let active = true;
    setQueueState({ status: "loading" });

    client
      .listStatutoryDiscountDrafts()
      .then((drafts) => {
        if (!active) {
          return;
        }

        setQueueState(drafts.length === 0 ? { status: "empty" } : { status: "loaded", data: drafts });
      })
      .catch((error) => {
        if (!active) {
          return;
        }

        const mapped = mapApiError(error);
        setQueueState(
          mapped.status === "access-denied"
            ? { status: "access-denied", message: mapped.message }
            : { status: "error", message: mapped.message }
        );
      });

    return () => {
      active = false;
    };
  }, [client, refreshToken]);

  return (
    <>
      <section className="pageTitle">
        <div>
          <p className="eyebrow">Statutory Discount Validation</p>
          <h2>Work queue</h2>
          <p>
            Review Senior Citizen and PWD statutory discount drafts with stored policy context and decision state.
          </p>
        </div>
        <button type="button" disabled={readinessBlockReason !== null} onClick={() => setRefreshToken((value) => value + 1)}>
          Refresh
        </button>
      </section>

      <section className="panel" aria-labelledby="queue-title">
        <div className="panelHeader">
          <h3 id="queue-title">Validation drafts</h3>
          <span className="statusPill">Live read model</span>
        </div>

        {readinessBlockReason && <p className="notice">{readinessBlockReason}</p>}
        {queueState.status === "loading" && <StateMessage title="Loading queue" message="Retrieving drafts." />}
        {queueState.status === "empty" && <StateMessage title="No drafts" message="No statutory discount drafts are waiting for review." />}
        {queueState.status === "access-denied" && <StateMessage title="Access denied" message={queueState.message} />}
        {queueState.status === "error" && <StateMessage title="Unable to load queue" message={queueState.message} />}
        {queueState.status === "loaded" && (
          <div className="tableScroller">
            <table>
              <thead>
                <tr>
                  <th>Draft</th>
                  <th>Ticket / Plate</th>
                  <th>Site</th>
                  <th>Entitlement</th>
                  <th>Policy basis</th>
                  <th>Policy readiness</th>
                  <th>Evidence</th>
                  <th>Status</th>
                  <th>Requested</th>
                  <th>Action</th>
                </tr>
              </thead>
              <tbody>
                {queueState.data.map((item) => (
                  <tr key={item.draftId}>
                    <td>
                      <code>{shortId(item.draftId)}</code>
                    </td>
                    <td>
                      <strong>{item.ticketReference}</strong>
                      <span>{item.plateNumber}</span>
                    </td>
                    <td>{item.siteName}</td>
                    <td>{item.entitlementType}</td>
                    <td>{policyBasisLabel(item.policyContext.kind)}</td>
                    <td>
                      <PolicyReadinessSummary policy={item.policyContext} compact />
                    </td>
                    <td>
                      <span>{item.policyContext.evidenceRequired ? "Required" : "Not required"}</span>
                      <span>{item.evidenceRequiredSatisfied ? "Satisfied" : `${item.evidenceCount} captured`}</span>
                    </td>
                    <td>
                      <span className={`statusPill ${statusClass(item.status)}`}>{item.status}</span>
                    </td>
                    <td>
                      <span>{formatDateTime(item.requestedAt)}</span>
                      <span>{item.requestedBy}</span>
                    </td>
                    <td>
                      <button
                        type="button"
                        aria-label={`View ${item.ticketReference}`}
                        disabled={readinessBlockReason !== null}
                        onClick={() => navigate(`${routes.detail}${item.draftId}`)}
                      >
                        View
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </>
  );
}

function StatutoryDiscountDetailPage({
  client,
  draftId,
  navigate,
  readinessBlockReason
}: {
  client: OperatorConsoleApiClient;
  draftId: string;
  navigate: (path: string) => void;
  readinessBlockReason: string | null;
}) {
  const [detailState, setDetailState] = useState<LoadState<StatutoryDiscountDraftDetail>>({ status: "loading" });
  const [refreshToken, setRefreshToken] = useState(0);

  useEffect(() => {
    let active = true;
    setDetailState((current) => (current.status === "loaded" ? current : { status: "loading" }));

    client
      .getStatutoryDiscountDraft(draftId)
      .then((detail) => {
        if (active) {
          setDetailState({ status: "loaded", data: detail });
        }
      })
      .catch((error) => {
        if (!active) {
          return;
        }

        const mapped = mapApiError(error);
        setDetailState(
          mapped.status === "not-found"
            ? { status: "not-found" }
            : mapped.status === "access-denied"
              ? { status: "access-denied", message: mapped.message }
              : { status: "error", message: mapped.message }
        );
      });

    return () => {
      active = false;
    };
  }, [client, draftId, refreshToken]);

  return (
    <>
      <button className="backButton" type="button" onClick={() => navigate(routes.queue)}>
        Back to queue
      </button>

      {detailState.status === "loading" && <StateMessage title="Loading draft" message="Retrieving draft details." />}
      {detailState.status === "not-found" && <StateMessage title="Draft not found" message="The requested draft was not found." />}
      {detailState.status === "access-denied" && <StateMessage title="Access denied" message={detailState.message} />}
      {detailState.status === "error" && <StateMessage title="Unable to load draft" message={detailState.message} />}
      {detailState.status === "loaded" && (
        <DraftDetail
          detail={detailState.data}
          client={client}
          refreshDetail={() => setRefreshToken((value) => value + 1)}
          readinessBlockReason={readinessBlockReason}
        />
      )}
    </>
  );
}

function DraftDetail({
  detail,
  client,
  refreshDetail,
  readinessBlockReason
}: {
  detail: StatutoryDiscountDraftDetail;
  client: OperatorConsoleApiClient;
  refreshDetail: () => void;
  readinessBlockReason: string | null;
}) {
  const [rejectReason, setRejectReason] = useState("");
  const [decisionMessage, setDecisionMessage] = useState<string | null>(null);
  const [decisionError, setDecisionError] = useState<string | null>(null);
  const [submittingDecision, setSubmittingDecision] = useState<"APPROVE" | "REJECT" | null>(null);
  const [payableBasisMessage, setPayableBasisMessage] = useState<string | null>(null);
  const [payableBasisError, setPayableBasisError] = useState<string | null>(null);
  const [submittingPayableBasis, setSubmittingPayableBasis] = useState(false);
  const [latestPayableBasisResult, setLatestPayableBasisResult] =
    useState<StatutoryDiscountPayableBasisApplicationResult | null>(null);
  const decisionable = detail.status === "Requested" || detail.status === "Pending Review";
  const approvalDisabledReason = readinessBlockReason ?? approvalBlockReason(detail, submittingDecision !== null);
  const rejectDisabledReason =
    readinessBlockReason ?? (!decisionable ? "Decision is read-only for the current validation status." : null);
  const payableBasisDisabledReason = readinessBlockReason ?? payableBasisBlockReason(detail, submittingPayableBasis);

  async function submitDecision(decision: "APPROVE" | "REJECT") {
    setDecisionMessage(null);
    setDecisionError(null);

    if (decision === "REJECT" && rejectReason.trim().length === 0) {
      setDecisionError("Reject requires a reason code.");
      return;
    }

    setSubmittingDecision(decision);
    try {
      const result = await client.submitStatutoryDiscountDecision({
        draftId: detail.draftId,
        siteId: detail.siteId,
        siteGroupId: detail.siteGroupId,
        decision,
        reasonCode: decision === "REJECT" ? rejectReason : undefined,
        notes: decision === "REJECT" ? "Rejected from Operator Console UI." : "Approved from Operator Console UI."
      });

      if (!result.accepted) {
        setDecisionError(result.message);
        return;
      }

      setDecisionMessage(result.message);
      refreshDetail();
    } catch (error) {
      setDecisionError(mapApiError(error).message);
    } finally {
      setSubmittingDecision(null);
    }
  }

  async function applyPayableBasis() {
    setPayableBasisMessage(null);
    setPayableBasisError(null);

    if (payableBasisDisabledReason) {
      setPayableBasisError(payableBasisDisabledReason);
      return;
    }

    setSubmittingPayableBasis(true);
    try {
      const result = await client.applyStatutoryDiscountPayableBasis({
        draftId: detail.draftId,
        siteId: detail.siteId,
        siteGroupId: detail.siteGroupId,
        originalTariffSnapshotId: detail.originalTariffSnapshotId
      });

      if (!result.accepted) {
        setPayableBasisError(result.message);
        return;
      }

      setLatestPayableBasisResult(result);
      setPayableBasisMessage(result.message);
      refreshDetail();
    } catch (error) {
      setPayableBasisError(mapApiError(error).message);
    } finally {
      setSubmittingPayableBasis(false);
    }
  }

  return (
    <>
      <section className="pageTitle">
        <div>
          <p className="eyebrow">Draft detail</p>
          <h2>{detail.ticketReference}</h2>
          <p>
            {detail.entitlementType} validation for plate {detail.plateNumber} at {detail.siteName}.
          </p>
        </div>
        <span className={`statusPill ${statusClass(detail.status)}`}>{detail.status}</span>
      </section>

      <section className="detailGrid">
        <section className="panel" aria-labelledby="draft-summary-title">
          <div className="panelHeader">
            <h3 id="draft-summary-title">Draft summary</h3>
          </div>
          <DescriptionList
            items={[
              ["Draft ID", detail.draftId],
              ["Parking session", detail.parkingSessionId],
              ["Ticket reference", detail.ticketReference],
              ["Plate number", detail.plateNumber],
              ["Current status", detail.status],
              ["Requested by", detail.requestedBy],
              ["Requested at", formatDateTime(detail.requestedAt)]
            ]}
          />
        </section>

        <section className="panel" aria-labelledby="session-context-title">
          <div className="panelHeader">
            <h3 id="session-context-title">Parking session context</h3>
          </div>
          <DescriptionList
            items={[
              ["Site", detail.siteName],
              ["Lane", detail.laneName],
              ["Started", formatDateTime(detail.parkingStartedAt)],
              ["Payment status", detail.currentPaymentStatus],
              ["Original tariff amount", detail.originalTariffAmount],
              ["Payable-basis preview", detail.payableBasisPreview]
            ]}
          />
        </section>

        <section className="panel" aria-labelledby="entitlement-title">
          <div className="panelHeader">
            <h3 id="entitlement-title">Entitlement context</h3>
          </div>
          <DescriptionList
            items={[
              ["Entitlement type", detail.entitlementType],
              ["Masked ID reference", detail.maskedIdReference],
              ["Issuing authority", detail.issuingAuthority],
              ["Evidence required", detail.policyContext.evidenceRequired ? "Yes" : "No"],
              ["Evidence captured", detail.evidenceCaptured ? "Yes" : "No"],
              ["Evidence satisfied", detail.evidenceRequiredSatisfied ? "Yes" : "No"],
              ["Evidence count", String(detail.evidenceCount)],
              ["Latest evidence status", detail.latestEvidenceStatus ?? "None"]
            ]}
          />
        </section>
      </section>

      <WorkflowStatePanel detail={detail} payableBasisResult={latestPayableBasisResult} />

      <PolicyContextDisplay policy={detail.policyContext} />

      <EvidencePanel
        detail={detail}
        client={client}
        refreshDetail={refreshDetail}
        readinessBlockReason={readinessBlockReason}
      />

      <section className="panel" aria-labelledby="decision-title">
        <div className="panelHeader">
          <h3 id="decision-title">Decision actions</h3>
          <span className="statusPill">{decisionable ? "Ready" : "Read-only status"}</span>
        </div>
        {approvalDisabledReason && <p className="notice">{approvalDisabledReason}</p>}
        {rejectDisabledReason && <p className="notice">{rejectDisabledReason}</p>}
        {decisionMessage && <p className="successMessage">{decisionMessage}</p>}
        {decisionError && <p className="errorMessage">{decisionError}</p>}
        <label className="reasonField">
          Reject reason code
          <input
            type="text"
            value={rejectReason}
            placeholder="ID_NOT_VALID"
            onChange={(event) => setRejectReason(event.target.value)}
          />
        </label>
        <div className="actionBar">
          <button
            type="button"
            disabled={approvalDisabledReason !== null}
            onClick={() => void submitDecision("APPROVE")}
          >
            {submittingDecision === "APPROVE" ? "Approving" : "Approve"}
          </button>
          <button
            type="button"
            disabled={rejectDisabledReason !== null || submittingDecision !== null}
            onClick={() => void submitDecision("REJECT")}
          >
            {submittingDecision === "REJECT" ? "Rejecting" : "Reject"}
          </button>
        </div>
      </section>

      <section className="panel" aria-labelledby="payable-basis-title">
        <div className="panelHeader">
          <h3 id="payable-basis-title">Apply payable basis</h3>
          <span className="statusPill">{isPayableBasisApplied(detail) ? "Applied" : "Awaiting approval"}</span>
        </div>
        <DescriptionList
          items={[
            ["Original tariff snapshot", detail.originalTariffSnapshotId ?? "Not available"],
            ["Application status", detail.payableBasisApplicationStatus ?? "Not applied"],
            ["Application ID", detail.payableBasisApplicationId ?? latestPayableBasisResult?.payableBasisApplicationId ?? "Not available"]
          ]}
        />
        {payableBasisDisabledReason && <p className="notice">{payableBasisDisabledReason}</p>}
        {payableBasisMessage && <p className="successMessage">{payableBasisMessage}</p>}
        {payableBasisError && <p className="errorMessage">{payableBasisError}</p>}
        <div className="actionBar">
          <button
            type="button"
            disabled={payableBasisDisabledReason !== null}
            onClick={() => void applyPayableBasis()}
          >
            {submittingPayableBasis ? "Applying payable basis" : "Apply payable basis"}
          </button>
        </div>
      </section>

      <FinalVerificationPanel detail={detail} payableBasisResult={latestPayableBasisResult} />

      <section className="panel" aria-labelledby="activity-title">
        <div className="panelHeader">
          <h3 id="activity-title">Audit activity placeholder</h3>
        </div>
        <ul className="activityList">
          {detail.auditActivity.map((item) => (
            <li key={item}>{item}</li>
          ))}
        </ul>
      </section>
    </>
  );
}

function WorkflowStatePanel({
  detail,
  payableBasisResult
}: {
  detail: StatutoryDiscountDraftDetail;
  payableBasisResult: StatutoryDiscountPayableBasisApplicationResult | null;
}) {
  const applied = isPayableBasisApplied(detail) || payableBasisResult?.applicationStatus === "APPLIED";
  const finalAmount =
    payableBasisResult?.finalPayableAmountMinorUnits ?? detail.finalPayableAmountMinorUnits ?? detail.payableAmountMinorUnits;

  return (
    <section className="panel" aria-labelledby="workflow-state-title">
      <div className="panelHeader">
        <h3 id="workflow-state-title">Workflow state</h3>
        <span className="statusPill">Validated sequence</span>
      </div>
      <div className="workflowGrid">
        <WorkflowStateItem label="Session resolved" complete={Boolean(detail.parkingSessionId)} />
        <WorkflowStateItem label="Policy resolved" complete={Boolean(detail.policyContext.policyCode)} />
        <WorkflowStateItem label="Draft created" complete={Boolean(detail.draftId)} />
        <WorkflowStateItem
          label="Evidence required"
          complete={detail.policyContext.evidenceRequired}
          inactiveLabel={detail.policyContext.evidenceRequired ? undefined : "Not required"}
        />
        <WorkflowStateItem label="Evidence satisfied" complete={detail.evidenceRequiredSatisfied} />
        <WorkflowStateItem label={`Validation ${detail.status.toLowerCase()}`} complete={detail.status === "Approved"} />
        <WorkflowStateItem label="Payable basis applied" complete={applied} />
        <WorkflowStateItem
          label={`Final payable ${formatMoney(finalAmount, detail.currencyCode)}`}
          complete={applied && finalAmount !== undefined}
        />
      </div>
    </section>
  );
}

function WorkflowStateItem({
  label,
  complete,
  inactiveLabel
}: {
  label: string;
  complete: boolean;
  inactiveLabel?: string;
}) {
  return (
    <div className={`workflowStep ${complete ? "workflowComplete" : "workflowPending"}`}>
      <span>{complete ? "Complete" : inactiveLabel ?? "Pending"}</span>
      <strong>{label}</strong>
    </div>
  );
}

function EvidencePanel({
  detail,
  client,
  refreshDetail,
  readinessBlockReason
}: {
  detail: StatutoryDiscountDraftDetail;
  client: OperatorConsoleApiClient;
  refreshDetail: () => void;
  readinessBlockReason: string | null;
}) {
  const [evidenceState, setEvidenceState] = useState<LoadState<StatutoryDiscountEvidenceList>>({ status: "loading" });
  const [evidenceType, setEvidenceType] = useState<EvidenceType>(
    (detail.requiredEvidenceTypes[0] as EvidenceType | undefined) ?? "SENIOR_CITIZEN_ID"
  );
  const [captureMethod, setCaptureMethod] = useState<EvidenceCaptureMethod>("OPERATOR_CONFIRMED");
  const [fileName, setFileName] = useState("");
  const [contentType, setContentType] = useState("image/jpeg");
  const [sizeBytes, setSizeBytes] = useState("");
  const [referenceNumber, setReferenceNumber] = useState("");
  const [notes, setNotes] = useState("");
  const [operatorConfirmation, setOperatorConfirmation] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [formMessage, setFormMessage] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [refreshToken, setRefreshToken] = useState(0);

  useEffect(() => {
    let active = true;
    setEvidenceState((current) => (current.status === "loaded" ? current : { status: "loading" }));

    client
      .listStatutoryDiscountEvidence(detail.draftId)
      .then((evidence) => {
        if (active) {
          setEvidenceState({ status: "loaded", data: evidence });
        }
      })
      .catch((error) => {
        if (active) {
          setEvidenceState({ status: "error", message: mapApiError(error).message });
        }
      });

    return () => {
      active = false;
    };
  }, [client, detail.draftId, refreshToken]);

  async function submitEvidence(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setFormError(null);
    setFormMessage(null);

    if (!operatorConfirmation) {
      setFormError("Operator confirmation is required.");
      return;
    }

    if (readinessBlockReason) {
      setFormError(readinessBlockReason);
      return;
    }

    if (captureMethod === "UPLOAD" && (!fileName.trim() || !contentType.trim() || Number(sizeBytes) <= 0)) {
      setFormError("Upload metadata requires file name, content type, and size.");
      return;
    }

    if (captureMethod === "MANUAL_REFERENCE" && !referenceNumber.trim()) {
      setFormError("Manual reference capture requires a reference value.");
      return;
    }

    setSubmitting(true);
    try {
      const result = await client.captureStatutoryDiscountEvidence({
        draftId: detail.draftId,
        siteId: detail.siteId,
        siteGroupId: detail.siteGroupId,
        evidenceType,
        captureMethod,
        fileName: captureMethod === "UPLOAD" ? fileName : undefined,
        contentType: captureMethod === "UPLOAD" ? contentType : undefined,
        sizeBytes: captureMethod === "UPLOAD" ? Number(sizeBytes) : undefined,
        referenceNumber: captureMethod === "MANUAL_REFERENCE" ? referenceNumber : undefined,
        notes: notes.trim() || undefined,
        operatorConfirmation
      });

      setReferenceNumber("");
      setNotes("");
      setOperatorConfirmation(false);
      setFormMessage(result.message);
      setRefreshToken((value) => value + 1);
      refreshDetail();
    } catch (error) {
      setFormError(mapApiError(error).message);
    } finally {
      setSubmitting(false);
    }
  }

  const evidenceLoaded = evidenceState.status === "loaded" ? evidenceState.data : null;
  const requiredSatisfied = evidenceLoaded?.evidenceRequiredSatisfied ?? detail.evidenceRequiredSatisfied;

  return (
    <section className="panel" aria-labelledby="evidence-title">
      <div className="panelHeader">
        <h3 id="evidence-title">Evidence</h3>
        <span className="statusPill">{detail.policyContext.evidenceRequired ? "Required" : "Not required"}</span>
      </div>

      <p className="notice">Metadata-only evidence capture. Do not upload or enter raw ID numbers.</p>
      {detail.policyContext.evidenceRequired && !requiredSatisfied && (
        <p className="notice">Approval is blocked until required evidence is captured.</p>
      )}
      {readinessBlockReason && <p className="notice">{readinessBlockReason}</p>}
      {requiredSatisfied && <p className="successMessage">Required evidence is captured.</p>}

      {evidenceState.status === "loading" && <StateMessage title="Loading evidence" message="Retrieving evidence metadata." />}
      {evidenceState.status === "error" && <StateMessage title="Unable to load evidence" message={evidenceState.message} />}
      {evidenceLoaded && (
        <div className="evidenceLayout">
          <div>
            <DescriptionList
              items={[
                ["Evidence satisfied", evidenceLoaded.evidenceRequiredSatisfied ? "Yes" : "No"],
                ["Required types", evidenceLoaded.requiredEvidenceTypes.join(", ") || "None"],
                ["Evidence count", String(evidenceLoaded.evidenceCount)],
                ["Latest status", evidenceLoaded.latestEvidenceStatus ?? "None"],
                ["Metadata-only", "Yes"],
                ["Raw ID or evidence bytes", "Do not enter or upload"]
              ]}
            />

            {evidenceLoaded.items.length === 0 ? (
              <p className="placeholderCopy">No evidence metadata has been captured for this draft.</p>
            ) : (
              <ul className="evidenceList">
                {evidenceLoaded.items.map((item) => (
                  <li key={item.evidenceId}>
                    <strong>{item.evidenceType}</strong>
                    <span>{item.captureMethod} / {item.verificationStatus}</span>
                    <span>Storage reference: {item.storageReference ?? "metadata-only"}</span>
                    <span>{formatDateTime(item.capturedAt)}</span>
                    <span>{item.capturedByUserId ?? "Unknown operator"}</span>
                  </li>
                ))}
              </ul>
            )}
          </div>

          <form className="evidenceForm" onSubmit={(event) => void submitEvidence(event)}>
            <label>
              Evidence type
              <select value={evidenceType} onChange={(event) => setEvidenceType(event.target.value as EvidenceType)}>
                <option value="SENIOR_CITIZEN_ID">Senior Citizen ID</option>
                <option value="PWD_ID">PWD ID</option>
                <option value="OTHER_SUPPORTING_DOCUMENT">Other supporting document</option>
              </select>
            </label>

            <label>
              Capture method
              <select value={captureMethod} onChange={(event) => setCaptureMethod(event.target.value as EvidenceCaptureMethod)}>
                <option value="OPERATOR_CONFIRMED">Operator confirmed</option>
                <option value="MANUAL_REFERENCE">Masked manual reference</option>
                <option value="UPLOAD">File metadata only</option>
              </select>
            </label>

            {captureMethod === "UPLOAD" && (
              <>
                <label>
                  File name
                  <input value={fileName} onChange={(event) => setFileName(event.target.value)} />
                </label>
                <label>
                  Content type
                  <input value={contentType} onChange={(event) => setContentType(event.target.value)} />
                </label>
                <label>
                  Size bytes
                  <input inputMode="numeric" value={sizeBytes} onChange={(event) => setSizeBytes(event.target.value)} />
                </label>
              </>
            )}

            {captureMethod === "MANUAL_REFERENCE" && (
              <label>
                Masked ID reference / last 4 only
                <input
                  value={referenceNumber}
                  placeholder="****1234"
                  onChange={(event) => setReferenceNumber(event.target.value)}
                />
                <span className="fieldHelp">Do not enter the full ID number.</span>
              </label>
            )}

            <label>
              Notes
              <textarea value={notes} onChange={(event) => setNotes(event.target.value)} />
            </label>

            <label className="checkboxField">
              <input
                type="checkbox"
                checked={operatorConfirmation}
                onChange={(event) => setOperatorConfirmation(event.target.checked)}
              />
              Operator confirms evidence was reviewed
            </label>

            {formMessage && <p className="successMessage">{formMessage}</p>}
            {formError && <p className="errorMessage">{formError}</p>}
            <button type="submit" disabled={submitting || readinessBlockReason !== null}>
              {submitting ? "Capturing" : "Capture evidence"}
            </button>
          </form>
        </div>
      )}
    </section>
  );
}

function PolicyContextDisplay({ policy }: { policy: StatutoryDiscountPolicyContext }) {
  const readiness = policy.policyReadinessClassification ?? "NOT_READY";
  const readinessClassName = policyReadinessClass(readiness);
  return (
    <section
      className={`panel policyPanel policy-${policy.kind} ${readinessClassName}`}
      aria-labelledby="policy-context-title"
    >
      <div className="panelHeader">
        <div>
          <p className="eyebrow">Policy context</p>
          <h3 id="policy-context-title">{policy.title}</h3>
        </div>
        <span className={`statusPill ${readinessClassName}`}>{readinessLabelForPolicy(readiness)}</span>
      </div>
      <p className="policySummary">{policy.operatorSummary}</p>
      <div className="policyGuardrail" role="note">
        <p>Policy readiness is not the same as payment approval.</p>
        <p>Sandbox/test policies are not production-ready.</p>
        <p>Manual review is required before automatic production application.</p>
        <p>No raw evidence or ID numbers are displayed here.</p>
      </div>
      {!policy.productionAutoApplicationEligible && (
        <p className="notice">Automatic production application is not allowed for this policy state.</p>
      )}
      {policy.requiresManualReview && <p className="notice">Manual review required before production use.</p>}
      <DescriptionList
        items={[
          ["Policy source", policySourceLabel(policy.registrySource)],
          ["Policy basis", policyBasisLabel(policy.kind)],
          ["Resolution basis", policy.policyResolutionBasis],
          ["Policy code", policy.policyCode ?? "Not available"],
          ["Policy name", policy.policyName ?? "Not available"],
          ["Readiness classification", readiness],
          ["Manual review required", policy.requiresManualReview ? "Yes" : "No"],
          ["Production auto-application eligible", policy.productionAutoApplicationEligible ? "Yes" : "No"],
          ["Readiness reason", policy.policyReadinessReason ?? "Not available"],
          ["Operator message", policy.operatorMessage ?? "Not available"],
          ["Legal basis", policy.legalBasisReference ?? "Not available"],
          ["National law", policy.nationalLawReference ?? "Not available"],
          ["Local ordinance", policy.ordinanceReference ?? "None"],
          ["Verification status", policy.verificationStatus ?? "Not available"],
          ["Benefit type", policy.benefitType ?? "Not available"],
          ["Discount base scope", policy.discountBaseScope ?? "Not available"],
          ["Evidence required", policy.evidenceRequired ? "Yes" : "No"],
          ["Required evidence type", policy.requiredEvidenceType ?? "Not available"],
          ["Effective from", policy.effectiveFrom ? formatDateTime(policy.effectiveFrom) : "Not available"],
          ["Effective to", policy.effectiveTo ? formatDateTime(policy.effectiveTo) : "Open-ended or not available"],
          ["Operator action", policy.ineligibilityReason ?? "Review can proceed when access and evidence rules allow."]
        ]}
      />
    </section>
  );
}

function PolicyReadinessSummary({
  policy,
  compact = false
}: {
  policy: StatutoryDiscountPolicyContext;
  compact?: boolean;
}) {
  const readiness = policy.policyReadinessClassification ?? "NOT_READY";
  return (
    <div className={`policyReadinessSummary ${compact ? "policyReadinessCompact" : ""}`}>
      <span className={`statusPill ${policyReadinessClass(readiness)}`}>{readinessLabelForPolicy(readiness)}</span>
      <strong>{policy.policyCode ?? "Policy missing"}</strong>
      <span>{readiness}</span>
      <span>{policySourceLabel(policy.registrySource)}</span>
      <span>{policy.verificationStatus ?? "Verification unknown"}</span>
      <span>{policy.requiresManualReview ? "Manual review required" : "No manual review flag"}</span>
    </div>
  );
}

function AuditPolicyReadinessSummary({ item }: { item: AuditReportItem }) {
  const readiness = item.policyReadinessClassification ?? "NOT_READY";
  return (
    <div className="policyReadinessSummary policyReadinessCompact">
      <span className={`statusPill ${policyReadinessClass(readiness)}`}>{readinessLabelForPolicy(readiness)}</span>
      <strong>{item.policyCode ?? "Policy missing"}</strong>
      <span>{readiness}</span>
      <span>{policySourceLabel(item.registrySource)}</span>
      <span>{item.verificationStatus ?? "Verification unknown"}</span>
      <span>{item.requiresManualReview ? "Manual review required" : "No manual review flag"}</span>
    </div>
  );
}

function FinalVerificationPanel({
  detail,
  payableBasisResult
}: {
  detail: StatutoryDiscountDraftDetail;
  payableBasisResult: StatutoryDiscountPayableBasisApplicationResult | null;
}) {
  const applied = isPayableBasisApplied(detail) || payableBasisResult?.applicationStatus === "APPLIED";
  const currency = payableBasisResult?.currencyCode ?? detail.currencyCode;
  const originalAmount = payableBasisResult?.grossAmountMinorUnits ?? detail.originalAmountMinorUnits;
  const statutoryDiscountAmount =
    payableBasisResult?.statutoryDiscountAmountMinorUnits ?? detail.statutoryDiscountAmountMinorUnits;
  const finalPayableAmount =
    payableBasisResult?.finalPayableAmountMinorUnits ?? detail.finalPayableAmountMinorUnits ?? detail.payableAmountMinorUnits;
  const appliedTariffSnapshotId = payableBasisResult?.appliedTariffSnapshotId ?? detail.appliedTariffSnapshotId;

  return (
    <section className="panel" aria-labelledby="final-verification-title">
      <div className="panelHeader">
        <h3 id="final-verification-title">Final verification</h3>
        <span className="statusPill">{applied ? "Approved + applied" : "Pending apply"}</span>
      </div>
      <DescriptionList
        items={[
          ["Validation status", detail.status],
          ["Payable basis status", detail.payableBasisApplicationStatus ?? payableBasisResult?.applicationStatus ?? "Not applied"],
          ["Original amount", formatMoney(originalAmount, currency)],
          ["VAT amount", formatMoney(payableBasisResult?.vatAmountMinorUnits ?? detail.vatAmountMinorUnits, currency)],
          [
            "VAT-exclusive amount",
            formatMoney(payableBasisResult?.vatExclusiveAmountMinorUnits ?? detail.vatExclusiveAmountMinorUnits, currency)
          ],
          ["Statutory discount amount", formatMoney(statutoryDiscountAmount, currency)],
          ["Final payable amount", formatMoney(finalPayableAmount, currency)],
          ["Currency", currency ?? "Not available"],
          ["Applied tariff snapshot ID", appliedTariffSnapshotId ?? "Not available"]
        ]}
      />
      {applied ? (
        <p className="successMessage">
          This did not create payment, exit authorization, coupon, or gate records.
        </p>
      ) : (
        <p className="notice">Final payable verification appears after approval and payable-basis application.</p>
      )}
    </section>
  );
}

function approvalBlockReason(detail: StatutoryDiscountDraftDetail, submitting: boolean) {
  if (submitting) {
    return "Decision submission is in progress.";
  }

  if (!detail.draftId) {
    return "Resolve a session before starting statutory discount validation.";
  }

  if (detail.policyContext.evidenceRequired && !detail.evidenceRequiredSatisfied) {
    return "Approval is blocked until required evidence is captured.";
  }

  if (["Approved", "Rejected", "Cancelled", "Expired", "Blocked"].includes(detail.status)) {
    return "Decision is read-only for the current validation status.";
  }

  return null;
}

function payableBasisBlockReason(detail: StatutoryDiscountDraftDetail, submitting: boolean) {
  if (submitting) {
    return "Payable basis application is in progress.";
  }

  if (!detail.draftId) {
    return "Resolve a session before starting statutory discount validation.";
  }

  if (detail.status !== "Approved") {
    return "Payable basis can be applied only after approval.";
  }

  if (isPayableBasisApplied(detail)) {
    return "Payable basis has already been applied.";
  }

  if (!detail.originalTariffSnapshotId) {
    return "Original tariff snapshot is required before applying payable basis.";
  }

  return null;
}

function readinessBlockedActionReason(readiness: AccessReadinessResponse) {
  const reasonCodes = readiness.denialReasons.map((reason) => reason.code).join(", ");
  return `Readiness check is blocking controlled Operator Console actions.${reasonCodes ? ` Reasons: ${reasonCodes}.` : ""}`;
}

function readinessLabel(ready: boolean, status: string) {
  return `${ready ? "Ready" : "Not ready"} (${status})`;
}

function isPayableBasisApplied(detail: StatutoryDiscountDraftDetail) {
  return detail.payableBasisApplicationStatus?.toUpperCase() === "APPLIED";
}

function DescriptionList({ items }: { items: Array<[string, string]> }) {
  return (
    <dl className="summaryList">
      {items.map(([label, value]) => (
        <div key={label}>
          <dt>{label}</dt>
          <dd>{value}</dd>
        </div>
      ))}
    </dl>
  );
}

function StateMessage({ title, message }: { title: string; message: string }) {
  return (
    <div className="stateMessage" role="status">
      <h3>{title}</h3>
      <p>{message}</p>
    </div>
  );
}

function NotFoundPage({ navigate }: { navigate: (path: string) => void }) {
  return (
    <section className="panel">
      <StateMessage title="Route not found" message="The requested Operator Console route does not exist." />
      <button type="button" onClick={() => navigate(routes.queue)}>
        Open statutory discount queue
      </button>
    </section>
  );
}

function ticketLookupGuidance(result: OperatorTicketLookupResult) {
  const vendorStatus = normalizeStatus(result.vendorConfirmationStatus);

  if (!vendorStatus) {
    return {
      label: "Vendor unavailable",
      messages: ["Vendor confirmation unavailable"],
      messageClass: "notice",
      className: "pending-review"
    };
  }

  if (vendorStatus === "PENDING") {
    return {
      label: "Vendor pending",
      messages: ["Payment confirmed in ExitPass. Vendor confirmation pending."],
      messageClass: "notice",
      className: "pending-review"
    };
  }

  if (vendorStatus === "CONFIRMED") {
    return {
      label: "Vendor confirmed",
      messages: ["Vendor confirmation complete.", "Proceed to ticket exit validator."],
      messageClass: "successMessage",
      className: "readiness-ready"
    };
  }

  if (vendorStatus === "FAILED") {
    return {
      label: "Vendor failed",
      messages: ["Vendor confirmation failed.", "Escalate to supervisor."],
      messageClass: "errorMessage",
      className: "blocked"
    };
  }

  return {
    label: "Status review",
    messages: ["Vendor confirmation unavailable"],
    messageClass: "notice",
    className: "pending-review"
  };
}

function normalizeStatus(status?: string | null) {
  return status?.trim().toUpperCase().replaceAll(" ", "_") ?? "";
}

function displayValue(value?: string) {
  return value && value.trim().length > 0 ? value : "Not available";
}

function formatOptionalDateTime(value?: string) {
  return value ? formatDateTime(value) : "Not available";
}

function formatOptionalNumber(value?: number) {
  return value === undefined ? "Not available" : String(value);
}

function displayPlateLicense(value?: string) {
  if (!value || value.trim().length === 0 || value.trim().toUpperCase() === "UNKNOWN") {
    return "Unknown";
  }

  return value;
}

function formatSeconds(value?: number) {
  return value === undefined ? "Not available" : String(value);
}

function formatFreshnessAge(value?: number | null) {
  if (value === undefined || value === null) {
    return "Not available";
  }

  if (value < 60) {
    return `${Math.round(value)} seconds`;
  }

  const minutes = value / 60;
  if (minutes < 60) {
    return `${Math.round(minutes)} minutes`;
  }

  const hours = minutes / 60;
  if (hours < 48) {
    return `${Math.round(hours)} hours`;
  }

  return `${Math.round(hours / 24)} days`;
}

function formatMinorUnits(value?: number) {
  return value === undefined ? "Not available" : String(value);
}

function normalizePath(path: string) {
  if (path === "/" || path === "") {
    return routes.home;
  }

  return path;
}

function productionPolicyImportSampleCsv() {
  return [
    [
      "policy_code",
      "policy_name",
      "entitlement_type",
      "lgu_code",
      "jurisdiction_name",
      "site_group_code",
      "site_code",
      "policy_level",
      "policy_type",
      "policy_resolution_basis",
      "benefit_type",
      "discount_base_scope",
      "free_duration_minutes",
      "initial_rate_exempt",
      "full_fee_exempt",
      "overnight_excluded",
      "valet_excluded",
      "standalone_parking_excluded",
      "driver_or_passenger_required",
      "beneficiary_residency_scope",
      "requires_evidence",
      "required_evidence_type",
      "requires_operator_validation",
      "legal_basis_reference",
      "ordinance_reference",
      "national_law_reference",
      "source_reference",
      "verification_status",
      "effective_from",
      "effective_to",
      "reviewed_by",
      "reviewed_at",
      "approved_by",
      "approved_at",
      "notes",
      "review_status",
      "review_owner",
      "legal_review_decision",
      "product_review_decision",
      "ops_review_decision",
      "engineering_review_decision",
      "qa_review_decision",
      "approval_notes"
    ].join(","),
    [
      "PH_VALID_SC_IMPORT_001",
      "Controlled Senior Citizen Candidate",
      "SENIOR_CITIZEN",
      "QAX",
      "Controlled Review City",
      "CONTROLLED_GROUP",
      "CONTROLLED_SITE",
      "LOCAL_ORDINANCE",
      "LOCAL_ORDINANCE",
      "LOCAL_ORDINANCE_APPLIED",
      "STATUTORY_DISCOUNT_VAT_EXEMPT",
      "VAT_EXCLUSIVE",
      "",
      "false",
      "false",
      "true",
      "true",
      "false",
      "true",
      "RESIDENT_ONLY",
      "true",
      "SENIOR_CITIZEN_ID",
      "true",
      "CONTROLLED LEGAL REFERENCE",
      "ORD-2099-001",
      "",
      "CONTROLLED SOURCE REFERENCE",
      "ACTIVE_APPROVED",
      "2099-01-01",
      "",
      "reviewer",
      "2099-01-02T00:00:00Z",
      "approver",
      "2099-01-03T00:00:00Z",
      "Controlled review note",
      "APPROVE_FOR_IMPORT",
      "review-owner",
      "APPROVE",
      "APPROVE",
      "APPROVE",
      "APPROVE",
      "APPROVE",
      "Controlled approval note"
    ].join(",")
  ].join("\n");
}

function shortId(id: string) {
  return id.slice(0, 8);
}

function formatDateTime(value: string) {
  return new Intl.DateTimeFormat("en-PH", {
    dateStyle: "medium",
    timeStyle: "short"
  }).format(new Date(value));
}

function formatMoney(minorUnits?: number, currencyCode?: string) {
  if (minorUnits === undefined) {
    return "Not available";
  }

  return `${currencyCode ?? "PHP"} ${(minorUnits / 100).toFixed(2)}`;
}

function policyBasisLabel(kind: PolicyContextKind) {
  return {
    "national-fallback": "RA 9994 / RA 10754 national fallback",
    "verified-local": "Verified local policy",
    "blocked-unverified-local": "Blocked unverified local policy",
    "unsupported-entitlement": "Unsupported entitlement",
    "missing-site-jurisdiction": "Missing site jurisdiction"
  }[kind];
}

function policySourceLabel(source?: string) {
  return {
    DEDICATED_REGISTRY: "Dedicated registry",
    COMPATIBILITY_POLICY_REFERENCES: "Compatibility policy references"
  }[source ?? ""] ?? "Policy source not confirmed";
}

function readinessLabelForPolicy(classification: string) {
  return {
    READY_VERIFIED: "Production-ready",
    READY_WITH_MANUAL_REVIEW: "Manual review",
    CONFIGURED_BUT_UNVERIFIED: "Unverified",
    LEAD_UNVERIFIED: "Unverified",
    PROPOSED_ONLY: "Proposed only",
    SANDBOX_ONLY: "Sandbox/test",
    MISSING_REQUIRED_POLICY: "Policy missing",
    NOT_READY: "Blocked",
    EXPIRED_OR_INACTIVE: "Inactive"
  }[classification] ?? classification;
}

function policyReadinessClass(classification: string) {
  if (classification === "READY_VERIFIED") {
    return "readiness-ready";
  }

  if (classification === "READY_WITH_MANUAL_REVIEW") {
    return "readiness-manual";
  }

  if (
    classification === "CONFIGURED_BUT_UNVERIFIED" ||
    classification === "LEAD_UNVERIFIED" ||
    classification === "PROPOSED_ONLY" ||
    classification === "SANDBOX_ONLY"
  ) {
    return "readiness-warning";
  }

  return "readiness-blocked";
}

function statusClass(status: string) {
  return status.toLowerCase().replaceAll(" ", "-");
}

function vendorAcknowledgmentStatusClass(status: string) {
  const normalized = status.toUpperCase();
  if (normalized === "CONFIRMED") {
    return "readiness-ready";
  }

  if (normalized === "FAILED") {
    return "blocked";
  }

  if (normalized === "PENDING" || normalized === "RETRY_PENDING") {
    return "pending-review";
  }

  if (normalized === "SKIPPED_DISABLED") {
    return "warningPill";
  }

  return statusClass(status);
}

function projectionHealthStatusClass(status: string) {
  const normalized = status.toUpperCase();
  if (normalized === "HEALTHY") {
    return "readiness-ready";
  }

  if (normalized === "DEGRADED") {
    return "warningPill";
  }

  if (normalized === "FAILING") {
    return "blocked";
  }

  if (normalized === "DISABLED") {
    return "";
  }

  return "pending-review";
}

function projectionStatusClass(status: string) {
  const normalized = status.toUpperCase();
  if (normalized === "ACTIVE") {
    return "readiness-ready";
  }

  if (normalized === "EXITED") {
    return "";
  }

  if (normalized === "STALE") {
    return "warningPill";
  }

  if (normalized === "INVALIDATED") {
    return "blocked";
  }

  return "pending-review";
}
