import { Fragment, useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import QRCode from "qrcode";
import {
  createOperatorConsoleApiClient,
  defaultDevModeContext,
  mapApiError,
  type OperatorConsoleApiClient
} from "./apiClient";
import type { OperatorConsoleHumanSession } from "./humanAuthentication";
import { formatPhpMoney } from "./phpCurrency";
import type {
  AccessReadinessResponse,
  AuditReportItem,
  AuditReportQuery,
  AuditReportResponse,
  FiscalIssuanceStatus,
  OperatorDigitalSalesInvoice,
  FiscalVoidActionAuditReportItem,
  FiscalVoidActionAuditReportQuery,
  FiscalVoidActionAuditReportResponse,
  FiscalStatusViewAuditReportItem,
  FiscalStatusViewAuditReportQuery,
  FiscalStatusViewAuditReportResponse,
  InvoiceCustomerInformation,
  LoadState,
  ProductionPolicyImportDryRunResult,
  ProductionPolicyImportReviewDecisionAction,
  ProductionPolicyImportReviewListResult,
  ProductionPolicyImportReviewResult,
  StatutoryDiscountDraftDetail,
  StatutoryDiscountEvidenceList,
  EvidenceType,
  OperatorTicketLookupResult,
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
import { PhoneCameraCapture } from "./PhoneCameraCapture";
import { SessionQrScanner } from "./SessionQrScanner";
import { normalizeScannedTicketReference } from "./sessionLookup";
import {
  CanonicalStatutoryReviewDetailPage,
  CanonicalStatutoryReviewQueuePage,
  defaultCanonicalStatutoryReviewFilters
} from "./CanonicalStatutoryReview";
import type { CanonicalStatutoryReviewFilters } from "./types";
import { ShiftManagement } from "./ShiftManagement";
import { OperatorFiscalReportingPage } from "./OperatorFiscalReportingPage";
import {
  createOperatorFiscalReportingClient,
  createOperatorFiscalReportingFixture,
  type OperatorFiscalReportingClient
} from "./fiscalReporting";
import {
  canAccessOperatorConsolePath,
  hasStatutorySupervisorWorkspace,
  resolveOperatorConsolePath,
  routes,
  visibleOperatorConsoleNavigation
} from "./operatorConsoleRoutes";

interface AppProps {
  apiClient?: OperatorConsoleApiClient;
  initialPath?: string;
  session?: OperatorConsoleHumanSession;
  logoutPending?: boolean;
  logoutMessage?: string;
  onLogout?: () => void;
  fiscalReportingClient?: OperatorFiscalReportingClient;
}

export function App({ apiClient, initialPath, session, logoutPending = false, logoutMessage, onLogout, fiscalReportingClient }: AppProps) {
  const client = useMemo(() => apiClient ?? createOperatorConsoleApiClient(), [apiClient]);
  const fiscalClient = useMemo(() => fiscalReportingClient ?? (
    import.meta.env.DEV && new URLSearchParams(window.location.search).get("operatorFiscalReportingScenario") === "ready"
      ? createOperatorFiscalReportingFixture()
      : createOperatorFiscalReportingClient()
  ), [fiscalReportingClient]);
  const [path, setPath] = useState(() => resolveOperatorConsolePath(initialPath ?? window.location.pathname));
  const [readinessState, setReadinessState] = useState<LoadState<AccessReadinessResponse>>({ status: "idle" });
  const [statutoryReviewFilters, setStatutoryReviewFilters] = useState<CanonicalStatutoryReviewFilters>(defaultCanonicalStatutoryReviewFilters);
  const [mobileNavigationOpen, setMobileNavigationOpen] = useState(false);
  const devModeContext = useMemo(() => defaultDevModeContext(), []);
  const mountedRef = useRef(true);
  const mobileMenuButtonRef = useRef<HTMLButtonElement>(null);
  const mobileNavigationDrawerRef = useRef<HTMLElement>(null);
  const workspaceRef = useRef<HTMLElement>(null);
  const focusDestinationHeadingRef = useRef(false);
  const restoreMobileMenuFocusRef = useRef(false);

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  useEffect(() => {
    if (initialPath) {
      return;
    }

    const syncPathFromBrowser = (focusDestination: boolean) => {
      const browserPath = window.location.pathname;
      const nextPath = resolveOperatorConsolePath(browserPath);
      if (nextPath !== browserPath) {
        window.history.replaceState({}, "", `${nextPath}${window.location.search}${window.location.hash}`);
      }
      if (focusDestination) focusDestinationHeadingRef.current = true;
      setPath(nextPath);
    };

    syncPathFromBrowser(false);
    const handlePopState = () => {
      syncPathFromBrowser(true);
    };
    window.addEventListener("popstate", handlePopState);
    return () => window.removeEventListener("popstate", handlePopState);
  }, [initialPath]);

  function navigate(nextPath: string) {
    focusDestinationHeadingRef.current = true;
    setPath(nextPath);
    if (!initialPath) {
      window.history.pushState({}, "", nextPath);
    }

    if (nextPath === path) {
      focusDestinationHeading();
    }
  }

  function focusDestinationHeading() {
    window.requestAnimationFrame(() => {
      const heading = workspaceRef.current?.querySelector<HTMLElement>("h2");
      if (!heading) return;
      if (!heading.hasAttribute("tabindex")) heading.setAttribute("tabindex", "-1");
      heading.focus();
    });
  }

  function dismissMobileNavigation(restoreMenuFocus = true) {
    restoreMobileMenuFocusRef.current = restoreMenuFocus;
    setMobileNavigationOpen(false);
  }

  useEffect(() => {
    if (!focusDestinationHeadingRef.current) return;
    focusDestinationHeadingRef.current = false;
    focusDestinationHeading();
  }, [path]);

  useEffect(() => {
    if (!mobileNavigationOpen) return;

    const previousBodyOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    mobileNavigationDrawerRef.current?.querySelector<HTMLElement>("[data-drawer-initial-focus]")?.focus();

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        event.preventDefault();
        dismissMobileNavigation();
        return;
      }

      if (event.key !== "Tab") return;
      const focusable = Array.from(
        mobileNavigationDrawerRef.current?.querySelectorAll<HTMLElement>(
          'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
        ) ?? []
      );
      if (focusable.length === 0) return;

      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };

    document.addEventListener("keydown", handleKeyDown);
    return () => {
      document.body.style.overflow = previousBodyOverflow;
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, [mobileNavigationOpen]);

  useEffect(() => {
    if (mobileNavigationOpen || !restoreMobileMenuFocusRef.current) return;
    restoreMobileMenuFocusRef.current = false;
    const frame = window.requestAnimationFrame(() => mobileMenuButtonRef.current?.focus());
    return () => window.cancelAnimationFrame(frame);
  }, [mobileNavigationOpen]);

  useEffect(() => {
    if (!mobileNavigationOpen || typeof window.matchMedia !== "function") return;
    const desktopMedia = window.matchMedia("(min-width: 900px)");
    const closeAtDesktop = (event: MediaQueryListEvent | MediaQueryList) => {
      if (event.matches) dismissMobileNavigation(false);
    };
    closeAtDesktop(desktopMedia);
    desktopMedia.addEventListener("change", closeAtDesktop);
    return () => desktopMedia.removeEventListener("change", closeAtDesktop);
  }, [mobileNavigationOpen]);

  function refreshReadiness(requestedAction = "SESSION_LOOKUP") {
    setReadinessState({ status: "loading" });
    void client
      .evaluateAccessReadiness({
        requestedAction,
        clientContext: {
          uiModule: "OperatorConsoleUi",
          screenState: path
        },
        devModeContext
      })
      .then((readiness) => {
        if (mountedRef.current) setReadinessState({ status: "loaded", data: readiness });
      })
      .catch((error) => {
        if (mountedRef.current) setReadinessState({ status: "error", message: mapApiError(error).message });
      });
  }

  useEffect(() => {
    refreshReadiness("SESSION_LOOKUP");
  }, [client]);

  const draftId = path.startsWith(routes.detail) ? path.slice(routes.detail.length) : null;
  const readiness = readinessState.status === "loaded" ? readinessState.data : null;
  const readinessBlockReason = readiness && !readiness.accessAllowed ? readinessBlockedActionReason(readiness) : null;
  const permissions = session?.permissions ?? [];
  const roleCodes = session?.roleCodes ?? [];
  const navigationItems = visibleOperatorConsoleNavigation(permissions, roleCodes);
  const routeAuthorized = canAccessOperatorConsolePath(path, permissions, roleCodes);
  const supervisorStatutoryWorkspace = hasStatutorySupervisorWorkspace(permissions);

  return (
    <>
    <main
      className="appShell"
      aria-labelledby="app-title"
      aria-hidden={mobileNavigationOpen ? true : undefined}
      inert={mobileNavigationOpen ? true : undefined}
    >
      <header className="appHeader">
        <button
          ref={mobileMenuButtonRef}
          className="mobileMenuButton"
          type="button"
          aria-label="Open navigation menu"
          aria-expanded={mobileNavigationOpen}
          aria-controls="operator-console-mobile-navigation"
          onClick={() => setMobileNavigationOpen(true)}
        >
          <span aria-hidden="true">&#9776;</span>
        </button>
        <div className="appBrand">
          <p className="eyebrow">Operator Console</p>
          <h1 id="app-title">ExitPass Operator Console</h1>
        </div>
        <div className="operatorStatus" aria-label="Operator identity">
          <span>Operator</span>
          <strong>{session?.displayName ?? "Authenticated session"}</strong>
          {session?.username && <span>{session.username}</span>}
          {session && <span>{scopeSummary(session)}</span>}
          {logoutMessage && <span className="authenticationInlineError" role="alert">{logoutMessage}</span>}
          {onLogout && (
            <button type="button" className="secondaryButton" onClick={onLogout} disabled={logoutPending}>
              {logoutPending ? "Signing out" : "Sign out"}
            </button>
          )}
        </div>
      </header>

      <section className="platformShell">
        <aside className="moduleRail" aria-label="Operator Console navigation">
          <div className="panelHeader">
            <p className="eyebrow">Workspace</p>
            <h2>Navigation</h2>
          </div>

          <nav aria-label="Operator Console routes">
            {navigationItems.map((item) => {
              const selected = item.matches ? item.matches(path) : path === item.route;
              return (
                <button
                  key={item.route}
                  aria-current={selected ? "page" : undefined}
                  className={`navLink ${selected ? "navLinkActive" : ""}`}
                  type="button"
                  onClick={() => navigate(item.route)}
                >
                  {item.label}
                </button>
              );
            })}
          </nav>

          {devModeContext.usesLocalDevFallbackContext && (
            <div className="statusStack">
              <span className="statusPill warningPill">Operating context incomplete</span>
            </div>
          )}
        </aside>

        <section className="workspace" ref={workspaceRef}>
          {!routeAuthorized ? (
            <section className="stateMessage danger" role="alert">
              <h2>Function unavailable</h2>
              <p>This function is not available for your account.</p>
            </section>
          ) : draftId ? (
            supervisorStatutoryWorkspace ? (
              <CanonicalStatutoryReviewDetailPage
                client={client}
                decisionId={draftId}
                onBack={() => navigate(routes.queue)}
              />
            ) : (
              <StatutoryDiscountDetailPage
                client={client}
                draftId={draftId}
                navigate={navigate}
                readinessBlockReason={readinessBlockReason}
                currentOperatorUserId={session?.userReference ?? ""}
              />
            )
          ) : path === routes.shiftManagement ? (
            <ShiftManagement client={client} />
          ) : path === routes.ticketLookup ? (
            <SessionLookupPage client={client} />
          ) : path === routes.fiscalStatus ? (
            <FiscalIssuanceStatusPage client={client} />
          ) : path === routes.fiscalReporting ? (
            session
              ? <OperatorFiscalReportingPage client={fiscalClient} siteReferences={session.siteReferences} permissions={session.permissions} />
              : <section className="stateMessage danger" role="alert"><h2>Permission denied</h2><p>Your operator session cannot access fiscal reporting.</p></section>
          ) : path === routes.queue ? (
            supervisorStatutoryWorkspace ? (
              <CanonicalStatutoryReviewQueuePage
                client={client}
                session={session}
                filters={statutoryReviewFilters}
                onFiltersChange={setStatutoryReviewFilters}
                onOpen={(decisionId) => navigate(`${routes.detail}${decisionId}`)}
              />
            ) : (
              <StatutoryDiscountQueuePage client={client} navigate={navigate} readinessBlockReason={null} />
            )
          ) : path === routes.audit ? (
            <AuditReportPage client={client} />
          ) : path === routes.fiscalStatusViewAudit ? (
            <FiscalStatusViewAuditReportPage client={client} />
          ) : path === routes.fiscalVoidActionAudit ? (
            <FiscalVoidActionAuditReportPage client={client} />
          ) : path === routes.vendorAcknowledgments ? (
            <VendorPaymentAcknowledgmentsPage client={client} />
          ) : path === routes.vendorProjectionHealth ? (
            <VendorSessionProjectionHealthPage client={client} />
          ) : path === routes.policyImportReview ? (
            <ProductionPolicyImportReviewPage client={client} readinessBlockReason={readinessBlockReason} />
          ) : (
            <NotFoundPage navigate={navigate} />
          )}
        </section>
      </section>
    </main>
    {mobileNavigationOpen && (
      <div className="mobileNavigationLayer">
        <button
          className="mobileNavigationBackdrop"
          type="button"
          tabIndex={-1}
          aria-label="Close navigation menu"
          onClick={() => dismissMobileNavigation()}
        />
        <aside
          ref={mobileNavigationDrawerRef}
          className="mobileNavigationDrawer"
          id="operator-console-mobile-navigation"
          role="dialog"
          aria-modal="true"
          aria-labelledby="mobile-navigation-title"
        >
          <div className="mobileNavigationHeader">
            <div>
              <p className="eyebrow">Workspace</p>
              <h2 id="mobile-navigation-title">Navigation</h2>
            </div>
            <button
              className="mobileNavigationClose"
              type="button"
              data-drawer-initial-focus
              onClick={() => dismissMobileNavigation()}
            >
              Close
            </button>
          </div>

          <div className="mobileOperatorIdentity" aria-label="Mobile operator identity">
            <span>Operator</span>
            <strong>{session?.displayName ?? "Authenticated session"}</strong>
            {session?.username && <span>{session.username}</span>}
            {session && <span>{scopeSummary(session)}</span>}
          </div>

          <nav aria-label="Operator Console mobile routes">
            {navigationItems.map((item) => {
              const selected = item.matches ? item.matches(path) : path === item.route;
              return (
                <button
                  key={item.route}
                  aria-current={selected ? "page" : undefined}
                  className={`navLink ${selected ? "navLinkActive" : ""}`}
                  type="button"
                  onClick={() => {
                    setMobileNavigationOpen(false);
                    navigate(item.route);
                  }}
                >
                  {item.label}
                </button>
              );
            })}
          </nav>

          <div className="mobileNavigationFooter">
            {devModeContext.usesLocalDevFallbackContext && (
              <span className="statusPill warningPill">Operating context incomplete</span>
            )}
            {logoutMessage && <span className="authenticationInlineError" role="alert">{logoutMessage}</span>}
            {onLogout && (
              <button
                type="button"
                className="secondaryButton"
                onClick={() => {
                  setMobileNavigationOpen(false);
                  onLogout();
                }}
                disabled={logoutPending}
              >
                {logoutPending ? "Signing out" : "Sign out"}
              </button>
            )}
          </div>
        </aside>
      </div>
    )}
    </>
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
                      <td><span className={`statusPill ${statusClass(item.validationStatus)}`}>{item.validationStatus}</span></td>
                      <td>{item.latestEvidenceStatus ?? (item.evidenceRequired ? "Pending" : "Not required")}</td>
                      <td>{item.evidenceRequiredSatisfied ? "Yes" : "No"}</td>
                      <td>{item.payableBasisApplicationStatus ?? "Not applied"}</td>
                      <td>{formatPhpMoney(item.originalAmountMinorUnits, item.currencyCode)}</td>
                      <td>{formatPhpMoney(item.statutoryDiscountAmountMinorUnits, item.currencyCode)}</td>
                      <td>{formatPhpMoney(item.finalPayableAmountMinorUnits, item.currencyCode)}</td>
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

function FiscalStatusViewAuditReportPage({ client }: { client: OperatorConsoleApiClient }) {
  const [filters, setFilters] = useState<FiscalStatusViewAuditReportQuery>({ limit: 25, offset: 0 });
  const [draftFilters, setDraftFilters] = useState<FiscalStatusViewAuditReportQuery>({ limit: 25, offset: 0 });
  const [reportState, setReportState] = useState<LoadState<FiscalStatusViewAuditReportResponse>>({ status: "loading" });

  useEffect(() => {
    let active = true;
    setReportState({ status: "loading" });

    client
      .listFiscalStatusViewAuditReport(filters)
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

        const mapped = mapApiError(error);
        if (mapped.status === "access-denied") {
          setReportState({ status: "access-denied", message: mapped.message });
          return;
        }

        setReportState({ status: "error", message: mapped.message });
      });

    return () => {
      active = false;
    };
  }, [client, filters]);

  function submitFilters(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setFilters({
      ...draftFilters,
      limit: draftFilters.limit ?? 25,
      offset: 0
    });
  }

  function changePage(nextOffset: number) {
    setDraftFilters((current) => ({ ...current, offset: nextOffset }));
    setFilters((current) => ({ ...current, offset: nextOffset }));
  }

  const pageLimit = reportState.status === "loaded" ? reportState.data.limit : filters.limit ?? 25;
  const pageOffset = reportState.status === "loaded" ? reportState.data.offset : filters.offset ?? 0;
  const canGoPrevious = pageOffset > 0;
  const canGoNext = reportState.status === "loaded" && pageOffset + pageLimit < reportState.data.totalCount;

  return (
    <>
      <section className="pageTitle">
        <div>
          <p className="eyebrow">Audit / Reporting</p>
          <h2>Sales Invoice status view audit report</h2>
          <p>Read-only report of Operator Console Sales Invoice status view events.</p>
        </div>
      </section>

      <section className="panel auditGuardrail" aria-labelledby="fiscal-view-audit-guardrail-title">
        <div className="panelHeader">
          <h3 id="fiscal-view-audit-guardrail-title">Read-only boundaries</h3>
          <span className="statusPill">View logs only</span>
        </div>
        <p>View logs are observational only.</p>
        <p>View logs do not prove payment.</p>
        <p>View logs do not prove fiscal issuance.</p>
        <p>View logs do not authorize exit.</p>
        <p>View logs do not imply gate action.</p>
      </section>

      <section className="panel" aria-labelledby="fiscal-view-audit-filters-title">
        <div className="panelHeader">
          <h3 id="fiscal-view-audit-filters-title">Filters</h3>
        </div>
        <form className="auditFilterGrid" onSubmit={submitFilters}>
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
          <label>
            Result class
            <select
              value={draftFilters.resultClass ?? ""}
              onChange={(event) => setDraftFilters((current) => ({ ...current, resultClass: event.target.value || undefined }))}
            >
              <option value="">Any result</option>
              <option value="SUCCEEDED">Succeeded</option>
              <option value="DENIED">Denied</option>
              <option value="NOT_FOUND">Not found</option>
              <option value="FAILED_SAFELY">Failed safely</option>
            </select>
          </label>
          <label>
            Limit
            <select
              value={String(draftFilters.limit ?? 25)}
              onChange={(event) =>
                setDraftFilters((current) => ({ ...current, limit: Number(event.target.value), offset: 0 }))
              }
            >
              <option value="25">25</option>
              <option value="50">50</option>
              <option value="100">100</option>
              <option value="200">200</option>
            </select>
          </label>
          <button type="submit">Apply filters</button>
        </form>
      </section>

      <section className="panel" aria-labelledby="fiscal-view-audit-results-title">
        <div className="panelHeader">
          <h3 id="fiscal-view-audit-results-title">Report results</h3>
          {reportState.status === "loaded" && <span className="statusPill">{reportState.data.totalCount} rows</span>}
        </div>

        {reportState.status === "loading" && (
          <StateMessage title="Loading Sales Invoice view audit report" message="Retrieving safe Sales Invoice status view rows." />
        )}
        {reportState.status === "empty" && (
          <StateMessage title="No Sales Invoice view audit rows" message="No Sales Invoice status view audit rows matched the filters." />
        )}
        {reportState.status === "access-denied" && <StateMessage title="Access denied" message={reportState.message} />}
        {reportState.status === "error" && (
          <StateMessage title="Unable to load Sales Invoice view audit report" message={reportState.message} />
        )}
        {reportState.status === "loaded" && (
          <>
            <div className="tableScroller fiscalViewAuditTableScroller">
              <table className="fiscalViewAuditTable">
                <colgroup>
                  <col className="auditDateColumn" />
                  <col className="auditActionColumn" />
                  <col className="auditResultColumn" />
                  <col className="auditInvoiceColumn" />
                  <col className="auditOperatorColumn" />
                  <col className="auditSiteColumn" />
                  <col className="auditSiteGroupColumn" />
                  <col className="auditDetailsColumn" />
                </colgroup>
                <thead>
                  <tr>
                    <th>Viewed At</th>
                    <th>Action</th>
                    <th>Result</th>
                    <th>Sales Invoice number</th>
                    <th className="auditOperatorHeader">Operator</th>
                    <th className="auditSiteHeader">Site</th>
                    <th className="auditSiteGroupHeader">Site group</th>
                    <th>Support / Audit</th>
                  </tr>
                </thead>
                <tbody>
                  {reportState.data.items.map((item) => (
                    <FiscalStatusViewAuditReportRow item={item} key={item.actionLogEntryId} />
                  ))}
                </tbody>
              </table>
            </div>
            <div className="paginationControls" aria-label="Fiscal view audit pagination">
              <button type="button" disabled={!canGoPrevious} onClick={() => changePage(Math.max(0, pageOffset - pageLimit))}>
                Previous page
              </button>
              <span>
                Offset {pageOffset} / {reportState.data.totalCount}
              </span>
              <button type="button" disabled={!canGoNext} onClick={() => changePage(pageOffset + pageLimit)}>
                Next page
              </button>
            </div>
          </>
        )}
      </section>
    </>
  );
}

function FiscalStatusViewAuditReportRow({ item }: { item: FiscalStatusViewAuditReportItem }) {
  const [detailsOpen, setDetailsOpen] = useState(false);
  const presentation = fiscalViewAuditResultPresentation(item.resultClass);
  const detailsId = `fiscal-view-audit-details-${item.actionLogEntryId}`;
  const actionLabel = fiscalViewAuditActionLabel(item.actionCode);
  return (
    <>
      <tr className={detailsOpen ? "auditRowExpanded" : undefined}>
        <td className="auditDateCell">{formatDateTime(item.actionTimestamp)}</td>
        <td className="auditActionCell">{actionLabel}</td>
        <td className="auditResultCell">
          <span className={`statusPill ${presentation.className}`}>{presentation.label}</span>
        </td>
        <td className="auditInvoiceCell">{displayValue(item.fiscalDocumentNumber)}</td>
        <td className="auditOperatorCell">{operatorDisplayValue(item)}</td>
        <td className="auditSiteCell">{siteDisplayValue(item)}</td>
        <td className="auditSiteGroupCell">{siteGroupDisplayValue(item)}</td>
        <td className="auditDetailsCell">
          <button
            type="button"
            className="auditDetailsToggle"
            aria-expanded={detailsOpen}
            aria-controls={detailsId}
            onClick={() => setDetailsOpen((current) => !current)}
          >
            {detailsOpen ? "Hide support/audit details" : "View support/audit details"}
          </button>
        </td>
      </tr>
      {detailsOpen && (
        <tr className="auditDetailsRow">
          <td colSpan={8}>
            <section className="auditDetailsFullPanel" id={detailsId} aria-label="Support/audit details">
              <h4>Support/audit details</h4>
              <p>Read-only metadata for the selected Sales Invoice status view row.</p>
              <DescriptionList
                items={[
                  ["Action", actionLabel],
                  ["Result meaning", presentation.meaning],
                  ["Result class", item.resultClass],
                  ["Sales Invoice number", displayValue(item.fiscalDocumentNumber)],
                  ["Ticket number", displayValue(item.ticketNumber)],
                  ["Operator", operatorDisplayValue(item)],
                  ["Site", siteDisplayValue(item)],
                  ["Site group", siteGroupDisplayValue(item)],
                  ["Safe denial/error posture", displayValue(item.safeDenialOrErrorPosture)],
                  ["Source module/screen", displayValue(item.sourceModule)]
                ]}
              />
            </section>
          </td>
        </tr>
      )}
    </>
  );
}

function fiscalViewAuditActionLabel(actionCode: string) {
  switch (actionCode) {
    case "VIEW_FISCAL_ISSUANCE_STATUS":
      return "Sales Invoice status viewed";
    case "VIEW_FISCAL_STATUS_VIEW_AUDIT_REPORT":
      return "Sales Invoice view audit report viewed";
    default:
      return "Sales Invoice audit activity";
  }
}

function fiscalViewAuditResultPresentation(resultClass: string) {
  switch (resultClass) {
    case "SUCCEEDED":
      return {
        label: "Succeeded",
        className: "readiness-ready",
        meaning: "Fiscal status was viewed by an authorized user."
      };
    case "DENIED":
      return {
        label: "Denied",
        className: "blocked",
        meaning: "The view was denied or unauthorized."
      };
    case "NOT_FOUND":
      return {
        label: "Not found",
        className: "warningPill",
        meaning: "The requested fiscal issuance reference was not available through the read path."
      };
    case "FAILED_SAFELY":
      return {
        label: "Failed safely",
        className: "blocked",
        meaning: "The view did not complete and failed without exposing unsafe details."
      };
    default:
      return {
        label: resultClass,
        className: "",
        meaning: "Unrecognized result class returned by the report endpoint."
      };
  }
}

function FiscalVoidActionAuditReportPage({ client }: { client: OperatorConsoleApiClient }) {
  const [filters, setFilters] = useState<FiscalVoidActionAuditReportQuery>({ limit: 25, offset: 0 });
  const [draftFilters, setDraftFilters] = useState<FiscalVoidActionAuditReportQuery>({ limit: 25, offset: 0 });
  const [reportState, setReportState] = useState<LoadState<FiscalVoidActionAuditReportResponse>>({ status: "loading" });

  useEffect(() => {
    let active = true;
    setReportState({ status: "loading" });

    client
      .listFiscalVoidActionAuditReport(filters)
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

        const mapped = mapApiError(error);
        if (mapped.status === "access-denied") {
          setReportState({ status: "access-denied", message: mapped.message });
          return;
        }

        setReportState({ status: "error", message: mapped.message });
      });

    return () => {
      active = false;
    };
  }, [client, filters]);

  function submitFilters(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setFilters({
      ...draftFilters,
      limit: draftFilters.limit ?? 25,
      offset: 0
    });
  }

  function changePage(nextOffset: number) {
    setDraftFilters((current) => ({ ...current, offset: nextOffset }));
    setFilters((current) => ({ ...current, offset: nextOffset }));
  }

  const pageLimit = reportState.status === "loaded" ? reportState.data.limit : filters.limit ?? 25;
  const pageOffset = reportState.status === "loaded" ? reportState.data.offset : filters.offset ?? 0;
  const canGoPrevious = pageOffset > 0;
  const canGoNext = reportState.status === "loaded" && pageOffset + pageLimit < reportState.data.totalCount;

  return (
    <>
      <section className="pageTitle">
        <div>
          <p className="eyebrow">Audit / Reporting</p>
          <h2>Sales Invoice void action audit review</h2>
          <p>Read-only review of Operator Console Sales Invoice void action records.</p>
        </div>
      </section>

      <section className="panel auditGuardrail" aria-labelledby="fiscal-void-audit-guardrail-title">
        <div className="panelHeader">
          <h3 id="fiscal-void-audit-guardrail-title">Read-only boundaries</h3>
          <span className="statusPill">Audit review only</span>
        </div>
        <p>This page reviews Sales Invoice void action-log metadata only.</p>
        <p>It does not perform Sales Invoice void, refund payment, authorize exit, open gate, call HikCentral, or create replacement Sales Invoices.</p>
        <p>Raw fiscal payloads, POS Server bodies, secrets, stack traces, customer PII, and statutory evidence payloads are never displayed.</p>
      </section>

      <section className="panel" aria-labelledby="fiscal-void-audit-filters-title">
        <div className="panelHeader">
          <h3 id="fiscal-void-audit-filters-title">Filters</h3>
        </div>
        <form className="auditFilterGrid" onSubmit={submitFilters}>
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
          <label className="wideFilterField">
            Sales Invoice number
            <input
              placeholder="e.g. SI-OCVOID-0001-UAT"
              value={draftFilters.fiscalDocumentNumber ?? ""}
              onChange={(event) => setDraftFilters((current) => ({ ...current, fiscalDocumentNumber: event.target.value || undefined }))}
            />
          </label>
          <label>
            Result class
            <select
              value={draftFilters.resultClass ?? ""}
              onChange={(event) => setDraftFilters((current) => ({ ...current, resultClass: event.target.value || undefined }))}
            >
              <option value="">Any result</option>
              <option value="SUCCEEDED">Succeeded</option>
              <option value="DENIED">Denied</option>
              <option value="NOT_FOUND">Not found</option>
              <option value="CONFLICT">Conflict</option>
              <option value="REJECTED">Rejected</option>
              <option value="ALREADY_VOIDED">Already voided</option>
              <option value="FAILED_SAFELY">Failed safely</option>
            </select>
          </label>
          <label>
            Limit
            <select
              value={String(draftFilters.limit ?? 25)}
              onChange={(event) =>
                setDraftFilters((current) => ({ ...current, limit: Number(event.target.value), offset: 0 }))
              }
            >
              <option value="25">25</option>
              <option value="50">50</option>
              <option value="100">100</option>
              <option value="200">200</option>
            </select>
          </label>
          <button type="submit">Apply filters</button>
        </form>
      </section>

      <section className="panel" aria-labelledby="fiscal-void-audit-results-title">
        <div className="panelHeader">
          <h3 id="fiscal-void-audit-results-title">Report results</h3>
          {reportState.status === "loaded" && <span className="statusPill">{reportState.data.totalCount} rows</span>}
        </div>

        {reportState.status === "loading" && (
          <StateMessage title="Loading Sales Invoice void audit review" message="Retrieving safe Sales Invoice void action rows." />
        )}
        {reportState.status === "empty" && (
          <StateMessage title="No Sales Invoice void action rows" message="No Sales Invoice void action audit rows matched the filters." />
        )}
        {reportState.status === "access-denied" && <StateMessage title="Access denied" message={reportState.message} />}
        {reportState.status === "error" && (
          <StateMessage title="Unable to load Sales Invoice void audit review" message={reportState.message} />
        )}
        {reportState.status === "loaded" && (
          <>
            <div className="tableScroller fiscalVoidAuditTableScroller">
              <table className="fiscalVoidAuditTable">
                <colgroup>
                  <col className="auditDateColumn" />
                  <col className="auditResultColumn" />
                  <col className="auditInvoiceColumn" />
                  <col className="auditTicketColumn" />
                  <col className="auditPriorityOperatorColumn" />
                  <col className="auditDetailsColumn" />
                </colgroup>
                <thead>
                  <tr>
                    <th>Submitted At</th>
                    <th>Result</th>
                    <th>Sales Invoice number</th>
                    <th className="auditTicketHeader">Ticket number</th>
                    <th className="auditPriorityOperatorHeader">Operator</th>
                    <th>Support / Audit</th>
                  </tr>
                </thead>
                <tbody>
                  {reportState.data.items.map((item) => (
                    <FiscalVoidActionAuditReportRow item={item} key={item.actionLogEntryId} />
                  ))}
                </tbody>
              </table>
            </div>
            <div className="paginationControls" aria-label="Sales Invoice void action audit pagination">
              <button type="button" disabled={!canGoPrevious} onClick={() => changePage(Math.max(0, pageOffset - pageLimit))}>
                Previous page
              </button>
              <span>
                Offset {pageOffset} / {reportState.data.totalCount}
              </span>
              <button type="button" disabled={!canGoNext} onClick={() => changePage(pageOffset + pageLimit)}>
                Next page
              </button>
            </div>
          </>
        )}
      </section>
    </>
  );
}

function FiscalVoidActionAuditReportRow({ item }: { item: FiscalVoidActionAuditReportItem }) {
  const [detailsOpen, setDetailsOpen] = useState(false);
  const presentation = fiscalVoidAuditResultPresentation(item.resultClass);
  const detailsId = `fiscal-void-audit-details-${item.actionLogEntryId}`;
  const actionLabel = fiscalVoidAuditActionLabel(item.actionCode);
  return (
    <>
      <tr className={detailsOpen ? "auditRowExpanded" : undefined}>
        <td className="auditDateCell">{formatDateTime(item.actionTimestamp)}</td>
        <td className="auditResultCell">
          <span className={`statusPill ${presentation.className}`}>{presentation.label}</span>
        </td>
        <td className="auditInvoiceCell">{displayValue(item.fiscalDocumentNumber)}</td>
        <td className="auditTicketCell">{displayValue(item.ticketNumber)}</td>
        <td className="auditPriorityOperatorCell">{operatorDisplayValue(item)}</td>
        <td className="auditDetailsCell">
          <button
            type="button"
            className="auditDetailsToggle"
            aria-expanded={detailsOpen}
            aria-controls={detailsId}
            onClick={() => setDetailsOpen((current) => !current)}
          >
            {detailsOpen ? "Hide support/audit details" : "View support/audit details"}
          </button>
        </td>
      </tr>
      {detailsOpen && (
        <tr className="auditDetailsRow">
          <td colSpan={6}>
            <section className="auditDetailsFullPanel" id={detailsId} aria-label="Support/audit details">
              <h4>Support/audit details</h4>
              <p>Read-only metadata for the selected Sales Invoice void action row.</p>
              <DescriptionList
                items={[
                  ["Reason", displayValue(item.reasonCode)],
                  ["Side-effect posture", sideEffectPosture(item)],
                  ["Action", actionLabel],
                  ["Result meaning", presentation.meaning],
                  ["Result class", item.resultClass],
                  ["Sales Invoice number", displayValue(item.fiscalDocumentNumber)],
                  ["Ticket number", displayValue(item.ticketNumber)],
                  ["Operator", operatorDisplayValue(item)],
                  ["Site", siteDisplayValue(item)],
                  ["Site group", siteGroupDisplayValue(item)],
                  ["Reason code", displayValue(item.reasonCode)],
                  ["Reason text", displayValue(item.reasonText)],
                  ["POS Server result classification", displayValue(item.posServerResultClassification)],
                  ["Safe denial/error posture", displayValue(item.safeDenialOrErrorPosture)],
                  ["Source module/screen", displayValue(item.sourceModule)],
                  ["Payment finality changed", displayBool(item.paymentFinalityChanged)],
                  ["ExitAuthorization issued", displayBool(item.exitAuthorizationIssued)],
                  ["Gate behavior triggered", displayBool(item.gateBehaviorTriggered)],
                  ["Refund/reversal created", displayBool(item.refundOrReversalCreated)],
                  ["HikCentral called", displayBool(item.hikCentralCalled)],
                  ["Payment provider called", displayBool(item.paymentProviderCalled)],
                  ["Rendering generated", displayBool(item.renderingGenerated)],
                  ["Replacement Sales Invoice created", displayBool(item.replacementFiscalDocumentCreated)],
                  ["New fiscal number allocated", displayBool(item.newFiscalNumberAllocated)],
                  ["Fiscal sequence changed by Central PMS", displayBool(item.fiscalSequenceChangedByCentralPms)]
                ]}
              />
            </section>
          </td>
        </tr>
      )}
    </>
  );
}

function fiscalVoidAuditActionLabel(actionCode: string) {
  if (actionCode === "VOID_FISCAL_DOCUMENT") {
    return "Sales Invoice void";
  }

  return displayValue(actionCode);
}

function fiscalVoidAuditResultPresentation(resultClass: string) {
  switch (resultClass) {
    case "SUCCEEDED":
      return {
        label: "Succeeded",
        className: "readiness-ready",
        meaning: "Sales Invoice void action succeeded or was accepted by the Sales Invoice void service."
      };
    case "ALREADY_VOIDED":
      return {
        label: "Already voided",
        className: "warningPill",
        meaning: "The Sales Invoice was already voided when the action was reviewed."
      };
    case "DENIED":
      return {
        label: "Denied",
        className: "blocked",
        meaning: "The operator was denied access before Sales Invoice void execution."
      };
    case "NOT_FOUND":
      return {
        label: "Not found",
        className: "warningPill",
        meaning: "The target fiscal issuance reference was not found."
      };
    case "CONFLICT":
      return {
        label: "Conflict",
        className: "blocked",
        meaning: "The Sales Invoice void action failed closed because POS Server reported a semantic conflict."
      };
    case "REJECTED":
      return {
        label: "Rejected",
        className: "blocked",
        meaning: "The Sales Invoice void action was rejected safely."
      };
    case "FAILED_SAFELY":
      return {
        label: "Failed safely",
        className: "blocked",
        meaning: "The Sales Invoice void action failed without exposing unsafe details."
      };
    default:
      return {
        label: resultClass,
        className: "",
        meaning: "Unrecognized result class returned by the report endpoint."
      };
  }
}

function sideEffectPosture(item: FiscalVoidActionAuditReportItem) {
  const flags = [
    item.paymentFinalityChanged,
    item.exitAuthorizationIssued,
    item.gateBehaviorTriggered,
    item.refundOrReversalCreated,
    item.hikCentralCalled,
    item.paymentProviderCalled,
    item.renderingGenerated,
    item.replacementFiscalDocumentCreated,
    item.newFiscalNumberAllocated,
    item.fiscalSequenceChangedByCentralPms
  ];

  if (flags.every((flag) => flag === false)) {
    return "No unsafe side effects recorded";
  }

  if (flags.some((flag) => flag === true)) {
    return "Review required";
  }

  return "Not available";
}

function displayBool(value: boolean | undefined) {
  if (value === true) {
    return "Yes";
  }

  if (value === false) {
    return "No";
  }

  return "Not available";
}

function FiscalIssuanceStatusPage({ client }: { client: OperatorConsoleApiClient }) {
  const [lookupQuery, setLookupQuery] = useState("");
  const [statusState, setStatusState] = useState<LoadState<FiscalIssuanceStatus>>({ status: "idle" });

  function loadFiscalStatus(query: string, showLoading = true) {
    if (showLoading) {
      setStatusState({ status: "loading" });
    }
    void client
      .lookupFiscalIssuanceStatus(query)
      .then((status) => setStatusState({ status: "loaded", data: status }))
      .catch((error) => {
        const mapped = mapApiError(error);
        if (mapped.status === "not-found") {
          setStatusState({ status: "not-found" });
          return;
        }

        if (mapped.status === "access-denied") {
          setStatusState({ status: "access-denied", message: mapped.message });
          return;
        }

        setStatusState({ status: "error", message: mapped.message });
      });
  }

  function submitLookup(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const trimmed = lookupQuery.trim();
    if (!trimmed) {
      setStatusState({ status: "error", message: "Ticket, Plate or SI Number is required." });
      return;
    }

    loadFiscalStatus(trimmed);
  }

  return (
    <section className="panel" aria-labelledby="fiscal-status-title">
      <div className="panelHeader">
        <div>
          <p className="eyebrow">Fiscal visibility</p>
          <h2 id="fiscal-status-title">Fiscal issuance status</h2>
          <p className="panelCopy">
            Search by an exact Ticket Number, Plate Number, or SI Number. This view is read-only.
          </p>
        </div>
        <span className="statusPill">Read only</span>
      </div>

      <form className="filterForm" onSubmit={submitLookup}>
        <label>
          Ticket, Plate or SI Number
          <input
            className="wideLookupInput"
            value={lookupQuery}
            placeholder="Ticket, plate, or SI number"
            onChange={(event) => setLookupQuery(event.target.value)}
          />
        </label>
        <button type="submit">View status</button>
      </form>
      <p className="helperText">
        Exact matches only. Results are limited to your authorized Site.
      </p>

      {statusState.status === "idle" && (
        <StateMessage title="No fiscal status selected" message="Enter a Ticket Number, Plate Number, or SI Number to view status." />
      )}
      {statusState.status === "loading" && (
        <StateMessage title="Loading fiscal status" message="Retrieving fiscal issuance status through the Operator Console facade." />
      )}
      {statusState.status === "not-found" && (
        <StateMessage
          title="Fiscal record not found"
          message="The exact identifier did not match a fiscal record in your authorized Site."
        />
      )}
      {statusState.status === "access-denied" && <StateMessage title="Access denied" message={statusState.message} />}
      {statusState.status === "error" && <StateMessage title="Unable to load fiscal status" message={statusState.message} />}
      {statusState.status === "loaded" && (
        <FiscalIssuanceStatusPanel status={statusState.data} client={client} />
      )}
    </section>
  );
}

function FiscalIssuanceStatusPanel({ status, client }: {
  status: FiscalIssuanceStatus;
  client: OperatorConsoleApiClient;
}) {
  const presentation = fiscalStatusPresentation(status);
  const operationalItems: Array<[string, string]> = [
    ["Ticket Number", displayValue(status.ticketNumber)],
    ["Plate Number", displayValue(status.plateNumber)],
    ["SI Number", displayValue(status.fiscalDocumentNumber)],
    ["Sales Invoice status", displayStatusValue(status.posServerFiscalDocumentStatusCodeKey)],
    ["Site POS Server ref", displayValue(status.sitePosServerRef)],
    ["Fiscal sequence value", formatOptionalNumber(status.fiscalSequenceValue)],
    ["POS Server document read status", displayStatusValue(status.posServerFiscalDocumentReadStatus)],
    ["Void status", displayStatusValue(status.posServerVoidStatus)],
    ["Void reason code", status.posServerVoidReasonCode ? displayStatusValue(status.posServerVoidReasonCode) : "Not available"],
    ["Date Printed", "Not available"]
  ];

  return (
    <section aria-labelledby="fiscal-status-result-title">
      <div className="panelHeader">
        <div>
          <p className="eyebrow">Fiscal status result</p>
          <h3 id="fiscal-status-result-title">{presentation.label}</h3>
          <p className={presentation.messageClass}>{presentation.message}</p>
        </div>
        <span className={`statusPill ${presentation.className}`}>{presentation.badge}</span>
      </div>

      <DescriptionList items={operationalItems} />
      {status.posServerFiscalDocumentReadStatus === "AVAILABLE" && status.fiscalDocumentNumber && (
        <SalesInvoiceActions client={client} status={status} />
      )}
    </section>
  );
}

function fiscalStatusPresentation(status: FiscalIssuanceStatus) {
  const state = normalizeStatus(status.fiscalIssuanceState);
  const documentStatus = normalizeStatus(status.posServerFiscalDocumentStatusCodeKey);
  const voidStatus = normalizeStatus(status.posServerVoidStatus);

  if (documentStatus === "VOIDED" || voidStatus === "RECORDED") {
    return {
      label: "Sales Invoice voided",
      badge: "Voided",
      message: "Sales Invoice is voided in POS Server. This view is observational only and does not authorize payment, exit, gate, refund, or replacement action.",
      className: "pending-review",
      messageClass: "notice"
    };
  }

  if (state === "FISCAL_ISSUANCE_RECORDED" && status.fiscalDocumentNumber) {
    return {
      label: "Issued",
      badge: "Issued",
      message: "Fiscal issuance was recorded and a Sales Invoice number is assigned.",
      className: "readiness-ready",
      messageClass: "successMessage"
    };
  }

  if (state === "FISCAL_ISSUANCE_RECORDED") {
    return {
      label: "Recorded - number not available",
      badge: "Recorded",
      message: "Fiscal issuance was recorded, but no Sales Invoice number is available in the status response.",
      className: "pending-review",
      messageClass: "notice"
    };
  }

  if (state === "FISCAL_ISSUANCE_REPLAYED") {
    return {
      label: "Existing issuance reused",
      badge: "Replay",
      message: "Existing fiscal issuance reused. No duplicate issuance was created.",
      className: "readiness-ready",
      messageClass: "notice"
    };
  }

  if (state === "FISCAL_ISSUANCE_CONFLICT") {
    return {
      label: "Fiscal issuance conflict",
      badge: "Conflict",
      message: "Fiscal issuance conflict. Escalate for review; do not retry without corrected request details.",
      className: "blocked",
      messageClass: "errorMessage"
    };
  }

  if (state === "FISCAL_ISSUANCE_FAILED_SERVICE") {
    return {
      label: "Fiscal service failed",
      badge: "Failed service",
      message: "Fiscal service failed. Support review is required before any retry or closure action.",
      className: "blocked",
      messageClass: "errorMessage"
    };
  }

  return {
    label: "Fiscal status requires review",
    badge: "Review",
    message: "Fiscal status requires support/audit review.",
    className: "pending-review",
    messageClass: "notice"
  };
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
              ["Request fee", formatPhpMoney(detail.requestFeeMinorUnits, detail.requestCurrencyCode)],
              ["Confirmed fee", formatPhpMoney(detail.confirmedFeeMinorUnits, detail.requestCurrencyCode)],
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

function SessionLookupPage({
  client
}: {
  client: OperatorConsoleApiClient;
}) {
  const [lookupMode, setLookupMode] = useState<"TICKET_REFERENCE" | "PLATE_LICENSE">("TICKET_REFERENCE");
  const [ticketReference, setTicketReference] = useState("");
  const [plateNumber, setPlateNumber] = useState("");
  const [scannerOpen, setScannerOpen] = useState(false);
  const [lookupState, setLookupState] = useState<LoadState<OperatorTicketLookupResult>>({ status: "idle" });
  const [draftState, setDraftState] = useState<LoadState<string>>({ status: "idle" });
  const [currentDraftState, setCurrentDraftState] = useState<LoadState<StatutoryDiscountDraftDetail>>({ status: "idle" });
  const [customerInformationState, setCustomerInformationState] = useState<LoadState<InvoiceCustomerInformation>>({ status: "idle" });
  const [customerInformationSaveState, setCustomerInformationSaveState] = useState<LoadState<string>>({ status: "idle" });
  const [customerName, setCustomerName] = useState("");
  const [customerAddress, setCustomerAddress] = useState("");
  const [customerTin, setCustomerTin] = useState("");
  const [customerBusinessStyle, setCustomerBusinessStyle] = useState("");
  const [entitlementType, setEntitlementType] = useState<"SENIOR_CITIZEN" | "PWD">("SENIOR_CITIZEN");
  const [issuingAuthority, setIssuingAuthority] = useState("");
  const [idReference, setIdReference] = useState("");
  const [idReferenceEditing, setIdReferenceEditing] = useState(false);
  const [birthDate, setBirthDate] = useState("");
  const [operatorAttestation, setOperatorAttestation] = useState(false);
  const [idPhoto, setIdPhoto] = useState<File | null>(null);
  function replaceIdPhoto(file: File | null) {
    if (!file) {
      setIdPhoto(null);
      return;
    }

    if (!(["image/jpeg", "image/png"] as const).includes(file.type as "image/jpeg" | "image/png") || file.size <= 0 || file.size > 5 * 1024 * 1024) {
      setDraftState({ status: "error", message: "Use a JPEG or PNG ID photo smaller than 5 MB." });
      setIdPhoto(null);
      return;
    }

    setDraftState({ status: "idle" });
    setIdPhoto(file);
  }

  const handleQrDecoded = useCallback((rawValue: string) => {
    const ticket = normalizeScannedTicketReference(rawValue);
    if (ticket) {
      setTicketReference(ticket);
      setLookupState({ status: "idle" });
    } else {
      setLookupState({ status: "error", message: "The QR code does not contain a ticket number." });
    }
    setScannerOpen(false);
  }, []);

  function changeLookupMode(nextMode: "TICKET_REFERENCE" | "PLATE_LICENSE") {
    if (nextMode === lookupMode) return;
    setLookupMode(nextMode);
    setScannerOpen(false);
    setLookupState({ status: "idle" });
    setDraftState({ status: "idle" });
    setCurrentDraftState({ status: "idle" });
    setCustomerInformationState({ status: "idle" });
    setCustomerInformationSaveState({ status: "idle" });
  }

  async function submitLookup(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const identifier = lookupMode === "TICKET_REFERENCE"
      ? ticketReference.trim()
      : plateNumber.trim().toUpperCase();
    if (!identifier) {
      setLookupState({
        status: "error",
        message: lookupMode === "TICKET_REFERENCE" ? "Scan or enter a ticket number." : "Enter a plate number."
      });
      return;
    }

    setLookupState({ status: "loading" });
    setDraftState({ status: "idle" });
    setCurrentDraftState({ status: "idle" });
    setCustomerInformationState({ status: "idle" });
    setCustomerInformationSaveState({ status: "idle" });
    setCustomerName("");
    setCustomerAddress("");
    setCustomerTin("");
    setCustomerBusinessStyle("");
    setIssuingAuthority("");
    setIdReference("");
    setIdReferenceEditing(false);
    setBirthDate("");
    replaceIdPhoto(null);
    setOperatorAttestation(false);
    try {
      const result = await client.lookupSession({ lookupMode, identifier });
      setLookupState(result.sessionFound ? { status: "loaded", data: result } : { status: "not-found" });
      if (result.sessionFound && result.parkingSessionId) {
        setCurrentDraftState({ status: "loading" });
        try {
          const currentDraft = await client.getCurrentStatutoryDiscountDraft(result.parkingSessionId);
          setCurrentDraftState(currentDraft ? { status: "loaded", data: currentDraft } : { status: "empty" });
        } catch (error) {
          setCurrentDraftState({ status: "error", message: mapApiError(error).message });
        }
      } else if (result.sessionFound) {
        setCurrentDraftState({ status: "empty" });
      }
      if (result.sessionFound && result.parkingSessionId && client.getInvoiceCustomerInformation) {
        setCustomerInformationState({ status: "loading" });
        try {
          const customerInformation = await client.getInvoiceCustomerInformation(result.parkingSessionId);
          applyCustomerInformation(customerInformation);
          setCustomerInformationState({ status: "loaded", data: customerInformation });
        } catch {
          setCustomerInformationState({
            status: "error",
            message: "Unable to load customer information for this session."
          });
        }
      } else if (result.sessionFound) {
        setCustomerInformationState({
          status: "error",
          message: "Unable to load customer information for this session."
        });
      }
    } catch (error) {
      const mapped = mapApiError(error);
      setLookupState(
        mapped.status === "access-denied"
          ? { status: "access-denied", message: mapped.message }
          : { status: "error", message: mapped.message }
      );
    }
  }

  async function createDraft(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (lookupState.status !== "loaded") {
      return;
    }

    const result = lookupState.data;
    if (!result.parkingSessionId) {
      setDraftState({ status: "error", message: "Unable to start the statutory discount request for this session." });
      return;
    }

    const normalizedIdReference = idReference.trim();
    const maskedIdReference = maskStatutoryIdReference(normalizedIdReference);
    setIdReferenceEditing(false);
    if (normalizedIdReference.length < 4 || !maskedIdReference) {
      setDraftState({
        status: "error",
        message: "Enter an ID reference using 4 to 64 letters, numbers, or hyphens."
      });
      return;
    }

    if (!operatorAttestation) {
      setDraftState({ status: "error", message: "Operator attestation is required before creating the draft." });
      return;
    }

    if (!idPhoto) {
      setDraftState({ status: "error", message: "Take an ID photo before submitting the request." });
      return;
    }

    setDraftState({ status: "loading" });
    try {
      const draft = await client.createStatutoryDiscountDraft({
        parkingSessionId: result.parkingSessionId,
        ticketReference: result.ticketNumber ?? result.cardNum,
        plateNumber: result.plateLicense,
        siteId: result.siteId,
        siteGroupId: result.siteGroupId,
        entitlementType,
        idDocumentType: idDocumentTypeForEntitlement(entitlementType),
        issuingAuthority,
        idControlReference: normalizedIdReference,
        maskedIdReference,
        birthDate,
        idPhoto,
        evidenceCaptureRequested: true,
        operatorAttestation,
        attestationNotes: "Operator confirmed the statutory ID photo and entitlement details.",
        reasonCode: "OPERATOR_ASSISTED_STATUTORY_DISCOUNT_REQUEST"
      });

      if (!draft.accepted || !draft.draftId) {
        setDraftState({ status: "error", message: statutoryDraftErrorMessage(draft.message) });
        return;
      }

      setDraftState({ status: "loaded", data: draft.message });
      const currentDraft = await client.getCurrentStatutoryDiscountDraft(result.parkingSessionId);
      setCurrentDraftState(currentDraft ? { status: "loaded", data: currentDraft } : { status: "error", message: "The submitted statutory request could not be reloaded." });
      setIdReference("");
      setOperatorAttestation(false);
      replaceIdPhoto(null);
    } catch (error) {
      const mapped = mapApiError(error);
      setDraftState({
        status: "error",
        message: statutoryDraftErrorMessage(mapped.message)
      });
    }
  }

  function applyCustomerInformation(value: InvoiceCustomerInformation) {
    setCustomerName(value.customerName ?? "");
    setCustomerAddress(value.address ?? "");
    setCustomerTin(value.tin ?? "");
    setCustomerBusinessStyle(value.businessStyle ?? "");
  }

  async function saveCustomerInformation(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (lookupState.status !== "loaded" || !lookupState.data.parkingSessionId || !client.saveInvoiceCustomerInformation) return;
    const current = customerInformationState.status === "loaded" ? customerInformationState.data : undefined;
    setCustomerInformationSaveState({ status: "loading" });
    try {
      const saved = await client.saveInvoiceCustomerInformation(lookupState.data.parkingSessionId, {
        customerName: customerName.trim() || undefined,
        address: customerAddress.trim() || undefined,
        tin: customerTin.trim() || undefined,
        businessStyle: customerBusinessStyle.trim() || undefined,
        expectedVersion: current?.rowVersion
      });
      applyCustomerInformation(saved);
      setCustomerInformationState({ status: "loaded", data: saved });
      if (client.getInvoiceCustomerInformation) {
        const verified = await client.getInvoiceCustomerInformation(lookupState.data.parkingSessionId);
        applyCustomerInformation(verified);
        setCustomerInformationState({ status: "loaded", data: verified });
      }
      setCustomerInformationSaveState({ status: "loaded", data: "Customer information saved." });
    } catch (error) {
      const mapped = mapApiError(error);
      setCustomerInformationSaveState({
        status: "error",
        message: mapped.errorCode === "CUSTOMER_INFORMATION_VERSION_CONFLICT"
          ? "Customer information changed after it was loaded. Reload the authoritative values before saving again."
          : mapped.errorCode === "CUSTOMER_INFORMATION_FISCAL_SNAPSHOT_LOCKED"
            ? "Customer information can no longer be changed because the Sales Invoice information has already been finalized."
            : mapped.message
      });
    }
  }

  async function reloadCustomerInformation() {
    if (lookupState.status !== "loaded" || !lookupState.data.parkingSessionId || !client.getInvoiceCustomerInformation) return;
    const previous = customerInformationState.status === "loaded" ? customerInformationState.data : undefined;
    setCustomerInformationState({ status: "loading" });
    setCustomerInformationSaveState({ status: "idle" });
    try {
      const current = await client.getInvoiceCustomerInformation(lookupState.data.parkingSessionId);
      applyCustomerInformation(current);
      setCustomerInformationState({ status: "loaded", data: current });
    } catch (error) {
      if (previous) {
        applyCustomerInformation(previous);
        setCustomerInformationState({ status: "loaded", data: previous });
        setCustomerInformationSaveState({ status: "error", message: mapApiError(error).message });
      } else {
        setCustomerInformationState({ status: "error", message: mapApiError(error).message });
      }
    }
  }

  return (
    <>
      <section className="pageTitle">
        <div>
          <h2>Session Lookup</h2>
        </div>
      </section>

      <section className="panel" aria-labelledby="session-lookup-title">
        <div className="panelHeader">
          <h3 id="session-lookup-title">Find parking session</h3>
          <span className="statusPill">Read-only lookup</span>
        </div>

        <div className="sessionLookupModes" role="group" aria-label="Session lookup method">
          <button type="button" aria-pressed={lookupMode === "TICKET_REFERENCE"} onClick={() => changeLookupMode("TICKET_REFERENCE")}>Ticket</button>
          <button type="button" aria-pressed={lookupMode === "PLATE_LICENSE"} onClick={() => changeLookupMode("PLATE_LICENSE")}>Plate</button>
        </div>

        <form className="ticketLookupForm" onSubmit={submitLookup}>
          {lookupMode === "TICKET_REFERENCE" ? <div className="sessionLookupIdentifier">
            <label>
              Ticket number
              <input
                autoComplete="off"
                autoFocus
                inputMode="text"
                name="ticketReference"
                placeholder="Scan or enter HikCentral ticket number"
                value={ticketReference}
                onChange={(event) => setTicketReference(event.target.value)}
              />
            </label>
            <button type="button" className="secondaryButton" onClick={() => setScannerOpen(true)}>Scan QR</button>
          </div> : <label>
            Plate number
            <input
              autoComplete="off"
              autoFocus
              inputMode="text"
              name="plateNumber"
              placeholder="Enter plate number"
              value={plateNumber}
              onChange={(event) => setPlateNumber(event.target.value)}
            />
          </label>}
          <button type="submit" disabled={lookupState.status === "loading"}>
            {lookupState.status === "loading" ? "Looking up" : "Lookup"}
          </button>
        </form>

        {lookupMode === "TICKET_REFERENCE" && scannerOpen && (
          <SessionQrScanner onDecoded={handleQrDecoded} onCancel={() => setScannerOpen(false)} />
        )}

      </section>

      {lookupState.status === "loading" && <StateMessage title="Looking up session" message="Retrieving Operator Console session status." />}
      {lookupState.status === "not-found" && <StateMessage title="Session not found" message="No active session was found for this identifier." />}
      {lookupState.status === "access-denied" && <StateMessage title="Access denied" message={lookupState.message} />}
      {lookupState.status === "error" && <StateMessage title="Unable to look up session" message={lookupState.message} />}
      {lookupState.status === "loaded" && (
        <>
          <TicketLookupSummary result={lookupState.data} />
          {currentDraftState.status === "loaded" ? (
            <SubmittedStatutoryRequestSummary detail={currentDraftState.data} client={client} />
          ) : <section className="panel" aria-labelledby="statutory-discount-start-title">
            <div className="panelHeader">
              <h3 id="statutory-discount-start-title">Request Statutory Discount</h3>
              <span className="statusPill">{isCompletedTransaction(lookupState.data) ? "Request unavailable" : "ID photo required"}</span>
            </div>
            {draftState.status === "error" && <p className="errorMessage">{draftState.message}</p>}
            {draftState.status === "loaded" && <p className="successMessage">{draftState.data}</p>}
            {currentDraftState.status === "loading" && <p role="status">Checking for an existing statutory request...</p>}
            {currentDraftState.status === "error" && <p className="errorMessage">Unable to load the existing statutory request for this session.</p>}
            {currentDraftState.status === "empty" && (isCompletedTransaction(lookupState.data) ? (
              <p className="notice">Statutory discount request is no longer available because payment has been completed and exit authorization has been issued.</p>
            ) : !lookupState.data.parkingSessionId ? (
              <p className="errorMessage">Unable to start the statutory discount request for this session.</p>
            ) : <form className="draftStartForm" onSubmit={createDraft}>
              <label>
                Entitlement type
                <select
                  value={entitlementType}
                  onChange={(event) => {
                    const next = event.target.value as "SENIOR_CITIZEN" | "PWD";
                    setEntitlementType(next);
                    setIssuingAuthority("");
                    setIdReference("");
                    setIdReferenceEditing(false);
                    replaceIdPhoto(null);
                  }}
                >
                  <option value="SENIOR_CITIZEN">Senior Citizen</option>
                  <option value="PWD">PWD</option>
                </select>
              </label>
              <label>
                ID document type
                <input value={idDocumentTypeForEntitlement(entitlementType)} readOnly />
              </label>
              <label>
                Issuing authority
                <input value={issuingAuthority} onChange={(event) => setIssuingAuthority(event.target.value)} />
              </label>
              <label>
                ID reference
                <input
                  value={idReferenceEditing
                    ? idReference
                    : maskStatutoryIdReference(idReference) ?? ""}
                  onChange={(event) => setIdReference(event.target.value)}
                  onFocus={() => setIdReferenceEditing(true)}
                  onBlur={() => setIdReferenceEditing(false)}
                  autoComplete="off"
                />
              </label>
              <label>
                Birth date
                <input type="date" value={birthDate} onChange={(event) => setBirthDate(event.target.value)} />
              </label>
              {maskStatutoryIdReference(idReference) && (
                <p className="fieldHint" role="status">ID reference will be masked when displayed.</p>
              )}
              <PhoneCameraCapture value={idPhoto} onChange={replaceIdPhoto} disabled={draftState.status === "loading"} />
              <label className="checkboxField">
                <input
                  type="checkbox"
                  checked={operatorAttestation}
                  onChange={(event) => setOperatorAttestation(event.target.checked)}
                />
                Operator confirms the entitlement information and captured ID photo match the presented document.
              </label>
              <div className="actionBar">
                <button
                  type="submit"
                  disabled={
                    draftState.status === "loading" ||
                    !issuingAuthority.trim() ||
                    !idReference.trim() ||
                    !birthDate ||
                    !operatorAttestation ||
                    !idPhoto
                  }
                >
                  {draftState.status === "loading" ? "Submitting Request" : "Submit Request"}
                </button>
              </div>
            </form>)}
          </section>}
          <section className="panel" aria-labelledby="invoice-customer-information-title">
            <div className="panelHeader">
              <h3 id="invoice-customer-information-title">Customer Information for Sales Invoice</h3>
              <span className="statusPill">Optional</span>
            </div>
            {customerInformationState.status === "loading" && <p role="status">Loading authoritative customer information...</p>}
            {customerInformationState.status === "error" && <div className="errorMessage" role="alert"><p>{customerInformationState.message}</p><button type="button" onClick={() => void reloadCustomerInformation()}>Retry customer information</button></div>}
            {(() => {
              const locked = customerInformationState.status === "loaded" && customerInformationState.data.fiscalSnapshotLocked;
              const authoritativeReady = customerInformationState.status === "loaded";
              return <>
              {locked && <p className="notice">Customer information can no longer be changed because the Sales Invoice information has already been finalized.</p>}
              {customerInformationSaveState.status === "loaded" && <p className="successMessage" role="status">{customerInformationSaveState.data}</p>}
              {customerInformationSaveState.status === "error" && <div className="errorMessage" role="alert"><p>{customerInformationSaveState.message}</p><button type="button" onClick={() => void reloadCustomerInformation()}>Reload authoritative values</button></div>}
              <form className="draftStartForm" onSubmit={saveCustomerInformation}>
                <label>Name<input value={customerName} onChange={(event) => setCustomerName(event.target.value)} maxLength={160} readOnly={locked} disabled={!authoritativeReady} /></label>
                <label>Address<textarea value={customerAddress} onChange={(event) => setCustomerAddress(event.target.value)} maxLength={300} readOnly={locked} disabled={!authoritativeReady} /></label>
                <label>TIN<input value={customerTin} onChange={(event) => setCustomerTin(event.target.value)} maxLength={40} readOnly={locked} disabled={!authoritativeReady} /></label>
                <label>Business Style / Business Name<input value={customerBusinessStyle} onChange={(event) => setCustomerBusinessStyle(event.target.value)} maxLength={160} readOnly={locked} disabled={!authoritativeReady} /></label>
                {!locked && <button type="submit" disabled={!authoritativeReady || customerInformationSaveState.status === "loading" || !(customerName.trim() || customerAddress.trim() || customerTin.trim() || customerBusinessStyle.trim())}>{customerInformationSaveState.status === "loading" ? "Saving" : "Save Customer Information"}</button>}
              </form>
            </>;
            })()}
          </section>
        </>
      )}
    </>
  );
}

type SubmittedEvidencePreviewState =
  | { status: "loading" }
  | { status: "loaded"; objectUrl: string }
  | { status: "unavailable"; message: string };

function SubmittedStatutoryRequestSummary({
  detail,
  client
}: {
  detail: StatutoryDiscountDraftDetail;
  client: OperatorConsoleApiClient;
}) {
  const [refreshToken, setRefreshToken] = useState(0);
  const [previewState, setPreviewState] = useState<SubmittedEvidencePreviewState>(
    detail.latestEvidenceId ? { status: "loading" } : { status: "unavailable", message: "No submitted ID image is linked to this request." }
  );

  useEffect(() => {
    if (!detail.latestEvidenceId) {
      setPreviewState({ status: "unavailable", message: "No submitted ID image is linked to this request." });
      return;
    }

    const controller = new AbortController();
    let objectUrl: string | null = null;
    setPreviewState({ status: "loading" });
    client.getSubmittedStatutoryEvidencePreview(detail.draftId, detail.latestEvidenceId, controller.signal)
      .then((preview) => {
        if (controller.signal.aborted) return;
        objectUrl = URL.createObjectURL(preview.blob);
        setPreviewState({ status: "loaded", objectUrl });
      })
      .catch((error) => {
        if (controller.signal.aborted) return;
        setPreviewState({ status: "unavailable", message: mapApiError(error).message });
      });

    return () => {
      controller.abort();
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [client, detail.draftId, detail.latestEvidenceId, refreshToken]);

  const reviewStatus = statutoryDocumentReviewStatus(detail);
  return (
    <section className="panel submittedStatutoryRequest" aria-labelledby={`submitted-statutory-${detail.draftId}`}>
      <div className="panelHeader">
        <div>
          <p className="eyebrow">Statutory Discount Request</p>
          <h3 id={`submitted-statutory-${detail.draftId}`}>{plainEntitlementLabel(detail.entitlementType)}</h3>
        </div>
        <span className={`statusPill ${statusClassForOperationalState(detail.status)}`}>{detail.status}</span>
      </div>
      <dl className="detailGrid submittedRequestDetails">
        <dt>Entitlement</dt><dd>{plainEntitlementLabel(detail.entitlementType)}</dd>
        <dt>ID document type</dt><dd>{statutoryDocumentTypeLabel(detail.idDocumentType, detail.entitlementType)}</dd>
        <dt>Issuing authority</dt><dd>{displayValue(detail.issuingAuthority)}</dd>
        <dt>ID reference</dt><dd>{displayValue(detail.maskedIdReference)}</dd>
        <dt>Submitted by</dt><dd>{displayValue(detail.requestedBy)}</dd>
        <dt>Submitted at</dt><dd>{formatDateTime(detail.requestedAt)}</dd>
        <dt>Document review status</dt><dd>{reviewStatus}</dd>
      </dl>
      <div className="submittedEvidencePreview">
        <div className="panelHeader">
          <h4>Submitted ID image</h4>
          {detail.latestEvidenceId && (
            <button type="button" className="secondaryButton" onClick={() => setRefreshToken((value) => value + 1)}>
              Refresh image
            </button>
          )}
        </div>
        {previewState.status === "loading" && <p role="status">Loading submitted ID image...</p>}
        {previewState.status === "loaded" && (
          <img src={previewState.objectUrl} alt="Submitted statutory ID evidence" className="submittedEvidenceImage" />
        )}
        {previewState.status === "unavailable" && <p className="notice">{previewState.message}</p>}
      </div>
      {reviewStatus === "Pending review" && <p className="notice">The submitted ID image is awaiting review by an authorized Statutory Discount Processor.</p>}
    </section>
  );
}

function TicketLookupSummary({ result }: { result: OperatorTicketLookupResult }) {
  return (
    <section className="panel ticketSummaryPanel" aria-labelledby="ticket-summary-title">
      <div className="panelHeader">
        <h3 id="ticket-summary-title">Session Summary</h3>
      </div>

      <DescriptionList
        items={[
          ["Ticket number", displayValue(result.ticketNumber)],
          ["Sales Invoice number", displayValue(result.salesInvoiceNumber)],
          ["Plate license", displayPlateLicense(result.plateLicense)],
          ["Site", displayValue(result.siteName)],
          ["Parking in time", result.parkingInTime ? formatDateTime(result.parkingInTime) : "Not available"],
          ["Parking duration", formatParkingDuration(result.parkingDurationSeconds)],
          ["Tariff source", result.tariffSource === "EXITPASS_CONTINUITY" ? "ExitPass Continuity" : displayValue(result.tariffSource)],
          ["Exit Authorization", displayValue(result.exitAuthorizationStatus)],
          ["Exit handling", displayValue(result.exitHandlingStatus)],
          ["Current payable amount", formatTicketLookupMoney(result.feeMinorUnits, result.currencyCode)],
          ["Payment attempt status", displayValue(result.paymentAttemptStatus)],
          ["Payment Status", displayValue(result.paymentStatus)],
          ["Amount Paid", formatTicketLookupMoney(result.amountPaidMinorUnits, result.currencyCode)],
          ["Payment method", displayValue(result.paymentMethod)]
        ]}
      />
      {result.exitHandlingStatus === "MANUAL_EXIT_REQUIRED" && (
        <p className="notice">Verify payment and allow manual exit. When HikCentral service is restored, tag the vehicle as EXITED in HikCentral.</p>
      )}
    </section>
  );
}

function SalesInvoiceActions({ status, client }: {
  status: FiscalIssuanceStatus;
  client: OperatorConsoleApiClient;
}) {
  const [invoice, setInvoice] = useState<OperatorDigitalSalesInvoice>();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [qrDataUrl, setQrDataUrl] = useState("");
  const customerUrl = invoice ? customerDigitalSalesInvoiceUrl(invoice.customerDigitalSalesInvoicePath) : "";

  useEffect(() => {
    let active = true;
    setQrDataUrl("");
    if (!customerUrl) return () => { active = false; };
    void QRCode.toDataURL(customerUrl, { errorCorrectionLevel: "M", margin: 1, width: 280 })
      .then((value) => { if (active) setQrDataUrl(value); })
      .catch(() => { if (active) setError("The customer Sales Invoice QR code could not be generated."); });
    return () => { active = false; };
  }, [customerUrl]);

  async function viewInvoice() {
    setLoading(true);
    setError("");
    try {
      setInvoice(await client.getDigitalSalesInvoice(status.fiscalIssuanceReferenceId));
    } catch (cause) {
      const mapped = mapApiError(cause);
      setError(mapped.message || "Digital Sales Invoice is unavailable.");
    } finally {
      setLoading(false);
    }
  }

  return <section className="digitalSalesInvoice" aria-labelledby="digital-sales-invoice-title">
    <div className="salesInvoiceActions">
      <button type="button" onClick={viewInvoice} disabled={loading}>
        {loading ? "Retrieving Sales Invoice..." : "View Sales Invoice"}
      </button>
      {invoice && <button type="button" onClick={() => window.print()}>Print Sales Invoice</button>}
    </div>
    {error && <p className="errorMessage" role="alert">{error}</p>}
    {invoice && <article className="canonicalSalesInvoice" aria-label="Canonical Digital Sales Invoice">
      <h3 id="digital-sales-invoice-title">{invoice.presentation.documentTitle ?? "Sales Invoice"}</h3>
      <pre className="canonicalSalesInvoiceText">{invoice.canonicalText}</pre>
      {qrDataUrl && <figure className="customerInvoiceQr">
        <img src={qrDataUrl} alt="QR code for the customer Digital Sales Invoice URL" />
        <figcaption>Scan to view or download this Sales Invoice. Access expires in 24 hours.</figcaption>
      </figure>}
    </article>}
  </section>;
}

function customerDigitalSalesInvoiceUrl(path: string) {
  const configuredBase = import.meta.env.VITE_WEBPAY_PUBLIC_BASE_URL?.trim();
  return new URL(path, configuredBase || window.location.origin).toString();
}

function isCompletedTransaction(result: OperatorTicketLookupResult) {
  return normalizeStatus(result.paymentConfirmationStatus) === "RECORDED" &&
    normalizeStatus(result.exitAuthorizationStatus) === "ISSUED";
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
  const [expandedDraftIds, setExpandedDraftIds] = useState<Set<string>>(() => new Set());

  function toggleQueueDetails(draftId: string) {
    setExpandedDraftIds((current) => {
      const next = new Set(current);
      if (next.has(draftId)) next.delete(draftId);
      else next.add(draftId);
      return next;
    });
  }

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
          <p>Review Senior Citizen and PWD statutory discount requests.</p>
        </div>
        <button type="button" disabled={readinessBlockReason !== null} onClick={() => setRefreshToken((value) => value + 1)}>
          Refresh
        </button>
      </section>

      <section className="panel" aria-labelledby="queue-title">
        <div className="panelHeader">
          <h3 id="queue-title">Statutory discount requests</h3>
        </div>

        {readinessBlockReason && <p className="notice">{readinessBlockReason}</p>}
        {queueState.status === "loading" && <StateMessage title="Loading queue" message="Retrieving drafts." />}
        {queueState.status === "empty" && <StateMessage title="No drafts" message="No statutory discount drafts are waiting for review." />}
        {queueState.status === "access-denied" && <StateMessage title="Access denied" message={queueState.message} />}
        {queueState.status === "error" && <StateMessage title="Unable to load queue" message={queueState.message} />}
        {queueState.status === "loaded" && (
          <div className="tableScroller workQueueScroller">
            <table className="workQueueTable">
              <thead>
                <tr>
                  <th className="workQueueTicketColumn">Ticket / Plate</th>
                  <th className="workQueueSiteColumn">Site</th>
                  <th className="workQueueEntitlementColumn">Entitlement</th>
                  <th className="workQueueStatusColumn">Status</th>
                  <th className="workQueueRequestedByColumn">Requested By</th>
                  <th className="workQueueRequestedAtColumn">Requested At</th>
                  <th className="workQueueActionColumn">Action</th>
                </tr>
              </thead>
              <tbody>
                {queueState.data.map((item) => {
                  const detailsId = `work-queue-details-${item.draftId}`;
                  const expanded = expandedDraftIds.has(item.draftId);
                  return (
                    <Fragment key={item.draftId}>
                      <tr className="workQueuePrimaryRow">
                        <td className="workQueueTicketColumn" data-label="Ticket / Plate">
                          <strong>{item.ticketReference}</strong>
                          <span>{item.plateNumber}</span>
                        </td>
                        <td className="workQueueSiteColumn" data-label="Site">{item.siteName}</td>
                        <td className="workQueueEntitlementColumn" data-label="Entitlement">{item.entitlementType}</td>
                        <td className="workQueueStatusColumn" data-label="Status">
                          <span className={`statusPill ${statusClass(item.status)}`}>{item.status}</span>
                        </td>
                        <td className="workQueueRequestedByColumn" data-label="Requested By">{item.requestedBy}</td>
                        <td className="workQueueRequestedAtColumn" data-label="Requested At">{formatDateTime(item.requestedAt)}</td>
                        <td className="workQueueActionColumn" data-label="Action">
                          <div className="workQueueActions">
                            <button
                              className="workQueueExpander"
                              type="button"
                              aria-label={`${expanded ? "Hide details" : "Details"} for ${item.ticketReference}`}
                              aria-expanded={expanded}
                              aria-controls={detailsId}
                              onClick={() => toggleQueueDetails(item.draftId)}
                            >
                              {expanded ? "Hide details" : "Details"}
                            </button>
                            <button
                              type="button"
                              aria-label={`Review ${item.ticketReference}`}
                              disabled={readinessBlockReason !== null}
                              onClick={() => navigate(`${routes.detail}${item.draftId}`)}
                            >
                              Review
                            </button>
                          </div>
                        </td>
                      </tr>
                      <tr className={`workQueueDetailRow ${expanded ? "workQueueDetailRowExpanded" : ""}`}>
                        <td colSpan={7}>
                          <dl id={detailsId} className="workQueueDetails">
                            <div className="workQueueDetailSite"><dt>Site</dt><dd>{item.siteName}</dd></div>
                            <div className="workQueueDetailEntitlement"><dt>Entitlement</dt><dd>{item.entitlementType}</dd></div>
                            <div><dt>Requested By</dt><dd>{item.requestedBy}</dd></div>
                            <div><dt>Requested At</dt><dd>{formatDateTime(item.requestedAt)}</dd></div>
                            <div><dt>Source</dt><dd>Operator Console</dd></div>
                          </dl>
                        </td>
                      </tr>
                    </Fragment>
                  );
                })}
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
  readinessBlockReason,
  currentOperatorUserId
}: {
  client: OperatorConsoleApiClient;
  draftId: string;
  navigate: (path: string) => void;
  readinessBlockReason: string | null;
  currentOperatorUserId: string;
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
          currentOperatorUserId={currentOperatorUserId}
        />
      )}
    </>
  );
}

function DraftDetail({
  detail,
  client,
  refreshDetail,
  readinessBlockReason,
  currentOperatorUserId
}: {
  detail: StatutoryDiscountDraftDetail;
  client: OperatorConsoleApiClient;
  refreshDetail: () => void;
  readinessBlockReason: string | null;
  currentOperatorUserId: string;
}) {
  const [rejectReason, setRejectReason] = useState("");
  const [reviewerPolicyAttestation, setReviewerPolicyAttestation] = useState(false);
  const [decisionMessage, setDecisionMessage] = useState<string | null>(null);
  const [decisionError, setDecisionError] = useState<string | null>(null);
  const [submittingDecision, setSubmittingDecision] = useState<"APPROVE" | "REJECT" | null>(null);
  const decisionable = detail.status === "Requested" || detail.status === "Pending Review";
  const canApproveDecision = client.canApproveStatutoryDiscount?.() ?? true;
  const canRejectDecision = client.canRejectStatutoryDiscount?.() ?? true;
  const decisionReadOnlyReason = statutoryDiscountDecisionReadOnlyReason(
    detail,
    currentOperatorUserId,
    canApproveDecision,
    canRejectDecision,
    decisionable
  );
  const showDecisionControls = decisionReadOnlyReason === null;
  const approvalDisabledReason =
    readinessBlockReason ?? approvalBlockReason(detail, submittingDecision !== null, reviewerPolicyAttestation);
  const rejectDisabledReason =
    readinessBlockReason ?? (!decisionable ? "Decision is read-only for the current validation status." : null);
  const decisionPanelStatus = !showDecisionControls ? "Read-only" : approvalDisabledReason ? "Blocked" : "Ready";
  const pageStatusLabel = statutoryReviewPageStatus(detail, showDecisionControls, approvalDisabledReason);

  async function submitDecision(decision: "APPROVE" | "REJECT") {
    setDecisionMessage(null);
    setDecisionError(null);

    if (decision === "REJECT" && rejectReason.trim().length === 0) {
      setDecisionError("Select a rejection reason.");
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

  return (
    <>
      <section className="pageTitle">
        <div>
          <p className="eyebrow">Statutory review</p>
          <h2>{plainEntitlementLabel(detail.entitlementType)} Parking Privilege</h2>
          <p>{detail.ticketReference} / {detail.plateNumber}</p>
        </div>
        <span className={`statusPill ${statusClassForOperationalState(pageStatusLabel)}`}>{pageStatusLabel}</span>
      </section>

      <StatutoryReviewEligibilityPanel detail={detail} />

      <SubmittedStatutoryRequestSummary detail={detail} client={client} />

      <section className="panel" aria-labelledby="decision-title">
        <div className="panelHeader">
          <h3 id="decision-title">Decision</h3>
          <span className={`statusPill ${decisionPanelStatus === "Blocked" ? "blocked" : ""}`}>{decisionPanelStatus}</span>
        </div>
        {decisionReadOnlyReason && <StatutoryDecisionReadOnlySummary detail={detail} fallbackReason={decisionReadOnlyReason} />}
        {showDecisionControls && approvalDisabledReason && <p className="notice">{approvalDisabledReason}</p>}
        {showDecisionControls && rejectDisabledReason && <p className="notice">{rejectDisabledReason}</p>}
        {decisionMessage && <p className="successMessage">{decisionMessage}</p>}
        {decisionError && <p className="errorMessage">{decisionError}</p>}
        {showDecisionControls && (
          <>
            <label className="attestationField">
              <input
                type="checkbox"
                checked={reviewerPolicyAttestation}
                onChange={(event) => setReviewerPolicyAttestation(event.target.checked)}
              />
              I verified the required beneficiary documents and confirmed that the request meets the applicable parking-privilege requirements.
            </label>
            {canRejectDecision && (
              <label className="reasonField">
                Reason for rejection
                <select
                  value={rejectReason}
                  onChange={(event) => setRejectReason(event.target.value)}
                >
                  <option value="">Select a reason</option>
                  {statutoryDiscountRejectionReasons.map((reason) => (
                    <option key={reason.code} value={reason.code}>
                      {reason.label}
                    </option>
                  ))}
                </select>
              </label>
            )}
            <div className="actionBar">
              {canApproveDecision && (
                <button
                  type="button"
                  disabled={approvalDisabledReason !== null}
                  onClick={() => void submitDecision("APPROVE")}
                >
                  {submittingDecision === "APPROVE" ? "Approving" : "Approve"}
                </button>
              )}
              {canRejectDecision && (
                <button
                  type="button"
                  disabled={rejectDisabledReason !== null || submittingDecision !== null}
                  onClick={() => void submitDecision("REJECT")}
                >
                  {submittingDecision === "REJECT" ? "Rejecting" : "Reject"}
                </button>
              )}
            </div>
          </>
        )}
      </section>
    </>
  );
}

function CompactEvidencePanel({
  detail,
  client,
  refreshDetail,
  readinessBlockReason,
  readOnly
}: {
  detail: StatutoryDiscountDraftDetail;
  client: OperatorConsoleApiClient;
  refreshDetail: () => void;
  readinessBlockReason: string | null;
  readOnly: boolean;
}) {
  const [evidenceState, setEvidenceState] = useState<LoadState<StatutoryDiscountEvidenceList>>({ status: "loading" });
  const evidenceOptions = operationalEvidenceOptions(detail);
  const [evidenceType, setEvidenceType] = useState<EvidenceType>(evidenceOptions[0]?.value ?? "SENIOR_CITIZEN_ID");
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

    if (readOnly) {
      setFormError("Document review is read-only for this request.");
      return;
    }

    if (!operatorConfirmation) {
      setFormError("Confirm the submitted document details are accurate.");
      return;
    }

    if (readinessBlockReason) {
      setFormError(readinessBlockReason);
      return;
    }

    setSubmitting(true);
    try {
      await client.captureStatutoryDiscountEvidence({
        draftId: detail.draftId,
        siteId: detail.siteId,
        siteGroupId: detail.siteGroupId,
        evidenceType,
        captureMethod: "OPERATOR_CONFIRMED",
        notes: notes.trim() || undefined,
        operatorConfirmation
      });

      setNotes("");
      setOperatorConfirmation(false);
      setFormMessage("Document evidence recorded for processor review.");
      setRefreshToken((value) => value + 1);
      refreshDetail();
    } catch (error) {
      setFormError(mapApiError(error).message);
    } finally {
      setSubmitting(false);
    }
  }

  const evidenceLoaded = evidenceState.status === "loaded" ? evidenceState.data : null;
  const evidenceSatisfied = evidenceLoaded?.evidenceRequiredSatisfied ?? detail.evidenceRequiredSatisfied;
  const evidenceStatus = plainEvidenceStatus(detail, evidenceSatisfied);

  return (
    <section className="panel compactEvidencePanel" aria-labelledby="compact-evidence-title">
      <div className="panelHeader">
        <div>
          <p className="eyebrow">Evidence</p>
          <h3 id="compact-evidence-title">Document review</h3>
        </div>
        <span className={`statusPill ${evidenceSatisfied ? "readiness-ready" : "warningPill"}`}>{evidenceStatus}</span>
      </div>

      <div className="compactEvidenceGrid">
        <div>
          <h4>Documents</h4>
          <ul className="documentStatusList">
            {evidenceOptions.map((option) => (
              <li key={option.label}>
                <span>{option.label}</span>
                <strong>{evidenceStatus}</strong>
              </li>
            ))}
          </ul>
        </div>

        {!readOnly ? (
          <form className="compactEvidenceForm" onSubmit={(event) => void submitEvidence(event)}>
            <label>
              Document to verify
              <select value={evidenceType} onChange={(event) => setEvidenceType(event.target.value as EvidenceType)}>
                {evidenceOptions.map((option) => (
                  <option key={option.label} value={option.value}>{option.label}</option>
                ))}
              </select>
            </label>
            <label>
              Reviewer note
              <textarea
                value={notes}
                placeholder="Optional short note"
                onChange={(event) => setNotes(event.target.value)}
              />
            </label>
            <label className="checkboxField">
              <input
                type="checkbox"
                checked={operatorConfirmation}
                onChange={(event) => setOperatorConfirmation(event.target.checked)}
              />
              Submitted document details are accurate
            </label>
            {formMessage && <p className="successMessage">{formMessage}</p>}
            {formError && <p className="errorMessage">{formError}</p>}
            {evidenceState.status === "error" && <p className="errorMessage">{evidenceState.message}</p>}
            <button type="submit" disabled={submitting || !operatorConfirmation || readinessBlockReason !== null}>
              {submitting ? "Saving" : "Record evidence"}
            </button>
          </form>
        ) : (
          <div className="compactEvidenceForm readOnlyEvidenceSummary">
            <p className="notice">Document review is read-only for this request.</p>
            {evidenceState.status === "error" && <p className="errorMessage">{evidenceState.message}</p>}
          </div>
        )}
      </div>
    </section>
  );
}

function StatutoryReviewEligibilityPanel({ detail }: { detail: StatutoryDiscountDraftDetail }) {
  const policy = detail.governingPolicy;
  const blockReason = governingPolicyBlockReason(detail);
  const checks = operationalRequiredChecks(detail);
  const benefitLabel = benefitDisplayLabel(policy, detail.policyContext);
  const evidenceStatus = plainEvidenceStatus(detail, detail.evidenceRequiredSatisfied);
  const requestCardStatus = privilegeRequestCardStatus(detail, blockReason);

  return (
    <section className="panel reviewerChecklistPanel" aria-labelledby="reviewer-checklist-title">
      <div className="panelHeader">
        <div>
          <p className="eyebrow">Privilege request</p>
          <h3 id="reviewer-checklist-title">{plainEntitlementLabel(detail.entitlementType)} Parking Privilege</h3>
        </div>
        <span className={`statusPill ${statusClassForOperationalState(requestCardStatus)}`}>{requestCardStatus}</span>
      </div>

      <div className="reviewerSummaryGrid">
        <div className="reviewerSummaryItem">
          <span>Ticket</span>
          <strong>{detail.ticketReference}</strong>
        </div>
        <div className="reviewerSummaryItem">
          <span>Plate</span>
          <strong>{detail.plateNumber}</strong>
        </div>
        <div className="reviewerSummaryItem">
          <span>Parking amount</span>
          <strong>{formatPhpMoney(detail.originalAmountMinorUnits ?? detail.payableAmountMinorUnits, detail.currencyCode)}</strong>
        </div>
        <div className="reviewerSummaryItem">
          <span>Benefit</span>
          <strong>{benefitLabel}</strong>
        </div>
        {policy?.beneficiaryResidencyScope === "RESIDENT_ONLY" && (
          <div className="reviewerSummaryItem">
            <span>Residency requirement</span>
            <strong>{policy.jurisdictionDisplayName} resident</strong>
          </div>
        )}
      </div>

      {blockReason && <p className="notice">This request is not currently available for decision.</p>}

      <div className="reviewerChecklistGrid">
        <section aria-labelledby="required-checks-title">
          <h4 id="required-checks-title">Required checks</h4>
          {checks.length === 0 ? (
            <p className="placeholderCopy">No additional document checks were returned for this request.</p>
          ) : (
            <ul className="operationalCheckList">
              {checks.map((check) => (
                <li key={`${check.kind}-${check.label}`}>
                  <strong>{check.label}</strong>
                  <span>{check.description}</span>
                </li>
              ))}
            </ul>
          )}
        </section>

        <section aria-labelledby="evidence-status-title">
          <h4 id="evidence-status-title">Evidence status</h4>
          <DescriptionList
            items={[
              ["Submitted", detail.evidenceCaptured ? "Yes" : "No"],
              ["Required documents", detail.evidenceRequiredSatisfied ? "Complete" : "Missing or needs review"],
              ["Review status", evidenceStatus]
            ]}
          />
        </section>
      </div>

      <div className="policyGuardrail" role="note">
        <p>Review the required beneficiary documents and applicable conditions.</p>
      </div>
    </section>
  );
}

function approvalBlockReason(detail: StatutoryDiscountDraftDetail, submitting: boolean, reviewerPolicyAttestation: boolean) {
  if (submitting) {
    return "Decision submission is in progress.";
  }

  if (!detail.draftId) {
    return "Resolve a session before starting statutory discount validation.";
  }

  if (detail.policyContext.evidenceRequired && !detail.evidenceRequiredSatisfied) {
    return requiredEvidenceBlockMessage(detail);
  }

  const policyBlockReason = governingPolicyBlockReason(detail);
  if (policyBlockReason) {
    return policyBlockReason;
  }

  if (!reviewerPolicyAttestation) {
    return "Approval requires reviewer attestation that the required documents and parking-privilege conditions were checked.";
  }

  if (["Approved", "Rejected", "Cancelled", "Expired", "Blocked"].includes(detail.status)) {
    return "Decision is read-only for the current validation status.";
  }

  return null;
}

function governingPolicyBlockReason(detail: StatutoryDiscountDraftDetail) {
  const policy = detail.governingPolicy;
  if (!policy) {
    return "This request is not currently available for decision.";
  }

  if (
    !policy.statutoryDiscountPolicyVersionId ||
    !policy.jurisdictionId ||
    !policy.jurisdictionCode ||
    !policy.jurisdictionDisplayName ||
    !policy.policyCode ||
    !policy.policyVersion
  ) {
    return "This request is not currently available for decision.";
  }

  if (policy.transactionPublicationStatus !== "ACTIVE_FOR_TRANSACTION_USE") {
    return "This request is not currently available for decision.";
  }

  if (
    policy.sourceVerificationStatus !== "VERIFIED_OFFICIAL" &&
    policy.sourceVerificationStatus !== "VERIFIED_ACTIVE_OPERATIONAL" &&
    policy.sourceVerificationStatus !== "ACTIVE_APPROVED"
  ) {
    return "This request is not currently available for decision.";
  }

  if (policy.parkingServiceApplicability !== "COVERED") {
    return "This request is not currently available for decision.";
  }

  if (!supportedBenefitEffect(policy.benefitType)) {
    return "This request is not currently available for decision.";
  }

  return null;
}

type OperationalCheck = {
  kind: string;
  label: string;
  description: string;
};

function operationalRequiredChecks(detail: StatutoryDiscountDraftDetail): OperationalCheck[] {
  const policy = detail.governingPolicy;
  if (!policy) {
    return [];
  }

  const requirements = policy.requiredEvidenceTypes.filter((requirement) => isRequiredRequirement(requirement.requirementStatus));
  const checks = requirements.map((requirement) => operationalCheckForRequirement(requirement.evidenceType, requirement.safeRequirementLabel));

  if (policy.beneficiaryResidencyScope === "RESIDENT_ONLY" && !checks.some((check) => check.kind === "residency")) {
    checks.push({
      kind: "residency",
      label: "Proof of residency",
      description: "Confirm residency evidence required by the resolved parking privilege."
    });
  }

  return checks;
}

function operationalEvidenceOptions(detail: StatutoryDiscountDraftDetail): Array<{ label: string; value: EvidenceType }> {
  const checks = operationalRequiredChecks(detail);
  const options = checks.map((check) => ({
    label: check.label,
    value: evidenceTypeForOperationalCheck(check)
  }));

  if (options.length > 0) {
    return uniqueEvidenceOptions(options);
  }

  return [
    {
      label: plainEntitlementLabel(detail.entitlementType) === "PWD" ? "Valid PWD ID" : "Valid Senior Citizen ID",
      value: plainEntitlementLabel(detail.entitlementType) === "PWD" ? "PWD_ID" : "SENIOR_CITIZEN_ID"
    }
  ];
}

function evidenceTypeForOperationalCheck(check: OperationalCheck): EvidenceType {
  if (check.kind === "senior-id") {
    return "SENIOR_CITIZEN_ID";
  }

  if (check.kind === "pwd-id") {
    return "PWD_ID";
  }

  return "OTHER_SUPPORTING_DOCUMENT";
}

function uniqueEvidenceOptions(options: Array<{ label: string; value: EvidenceType }>) {
  const seen = new Set<string>();
  return options.filter((option) => {
    const key = `${option.label}:${option.value}`;
    if (seen.has(key)) {
      return false;
    }

    seen.add(key);
    return true;
  });
}

function operationalCheckForRequirement(evidenceType: string, safeRequirementLabel?: string): OperationalCheck {
  const normalized = evidenceType.toUpperCase();
  if (normalized === "SENIOR_CITIZEN_ID") {
    return {
      kind: "senior-id",
      label: "Valid Senior Citizen ID",
      description: safeRequirementLabel ?? "Confirm the beneficiary document is present and readable."
    };
  }

  if (normalized === "PWD_ID") {
    return {
      kind: "pwd-id",
      label: "Valid PWD ID",
      description: safeRequirementLabel ?? "Confirm the beneficiary document is present and readable."
    };
  }

  if (normalized === "RESIDENCY_EVIDENCE") {
    return {
      kind: "residency",
      label: "Proof of residency",
      description: safeRequirementLabel ?? "Confirm residency evidence required by the resolved parking privilege."
    };
  }

  if (normalized === "BENEFICIARY_PRESENCE") {
    return {
      kind: "beneficiary-presence",
      label: "Beneficiary must be present",
      description: safeRequirementLabel ?? "Confirm the beneficiary is physically present only because the policy requires it."
    };
  }

  if (normalized === "BENEFICIARY_DRIVER" || normalized === "DRIVER_REQUIREMENT" || normalized === "DRIVER_STATUS") {
    return {
      kind: "driver",
      label: "Beneficiary must be the driver",
      description: safeRequirementLabel ?? "Confirm the beneficiary is the driver only because the policy requires it."
    };
  }

  if (normalized === "BENEFICIARY_PASSENGER" || normalized === "PASSENGER_REQUIREMENT" || normalized === "PASSENGER_STATUS") {
    return {
      kind: "passenger",
      label: "Beneficiary must be a passenger",
      description: safeRequirementLabel ?? "Confirm the beneficiary is a passenger only because the policy requires it."
    };
  }

  return {
    kind: normalized.toLowerCase(),
    label: displayStatusValue(evidenceType),
    description: safeRequirementLabel ?? "Confirm this policy-required document or condition."
  };
}

function isRequiredRequirement(requirementStatus: string) {
  return requirementStatus.toUpperCase() === "REQUIRED";
}

function requiredEvidenceBlockMessage(detail: StatutoryDiscountDraftDetail) {
  const requiredChecks = operationalRequiredChecks(detail);
  if (requiredChecks.some((check) => check.kind === "residency")) {
    return "This request cannot be approved because proof of residency is missing.";
  }

  if (requiredChecks.some((check) => check.kind === "driver")) {
    return "This request cannot be approved until the required driver condition is verified.";
  }

  if (requiredChecks.some((check) => check.kind === "passenger")) {
    return "This request cannot be approved until the required passenger condition is verified.";
  }

  if (requiredChecks.some((check) => check.kind === "beneficiary-presence")) {
    return "This request cannot be approved until required beneficiary presence is verified.";
  }

  return "This request cannot be approved because required documents are missing or need review.";
}

function supportedBenefitEffect(benefitType: string) {
  return benefitType === "STATUTORY_DISCOUNT_VAT_EXEMPT";
}

function benefitDisplayLabel(
  policy: StatutoryDiscountDraftDetail["governingPolicy"],
  policyContext: StatutoryDiscountPolicyContext
) {
  const benefitType = policy?.benefitType ?? policyContext.benefitType;
  const policyName = policyContext.policyName?.toLowerCase() ?? "";
  if (policyName.includes("free parking")) {
    return "Free parking";
  }

  switch (benefitType) {
    case "FULL_FEE_EXEMPTION":
      return "Free parking";
    case "PERCENTAGE_DISCOUNT":
    case "STATUTORY_DISCOUNT_VAT_EXEMPT":
      return "Parking discount";
    case "FREE_DURATION":
      return "Free parking period";
    case "INITIAL_RATE_EXEMPTION":
      return "Initial parking fee waived";
    case "CAPPED_BENEFIT":
      return "Capped parking benefit";
    default:
      return benefitType ? displayStatusValue(benefitType) : "Parking privilege";
  }
}

function plainEntitlementLabel(entitlementType: string) {
  return entitlementType === "PWD" ? "PWD" : "Senior Citizen";
}

function plainReviewStatus(status: string) {
  const normalized = status.toUpperCase().replaceAll(" ", "_");
  if (normalized === "REQUESTED" || normalized === "PENDING_REVIEW" || normalized === "PENDING_OPERATOR_REVIEW") {
    return "Ready for review";
  }

  if (normalized === "APPROVED") {
    return "Approved";
  }

  if (normalized === "REJECTED") {
    return "Rejected";
  }

  if (normalized === "BLOCKED") {
    return "Blocked";
  }

  return displayStatusValue(status);
}

function privilegeRequestCardStatus(detail: StatutoryDiscountDraftDetail, blockReason: string | null) {
  const normalized = detail.status.toUpperCase().replaceAll(" ", "_");
  if (normalized === "APPROVED") {
    return "Approved";
  }

  if (normalized === "REJECTED") {
    return "Rejected";
  }

  if (blockReason) {
    return "Blocked";
  }

  if (detail.policyContext.evidenceRequired && !detail.evidenceRequiredSatisfied) {
    return "Blocked";
  }

  return "Ready for review";
}

function statutoryReviewPageStatus(
  detail: StatutoryDiscountDraftDetail,
  showDecisionControls: boolean,
  approvalDisabledReason: string | null
) {
  const normalized = detail.status.toUpperCase().replaceAll(" ", "_");
  if (normalized === "APPROVED" || normalized === "REJECTED" || normalized === "BLOCKED") {
    return plainReviewStatus(detail.status);
  }

  if (showDecisionControls && approvalDisabledReason && !isAttestationApprovalBlock(approvalDisabledReason)) {
    return "Blocked";
  }

  return plainReviewStatus(detail.status);
}

function isAttestationApprovalBlock(reason: string) {
  return reason.toLowerCase().includes("attestation");
}

function statusClassForOperationalState(status: string) {
  if (status === "Blocked") {
    return "blocked";
  }

  if (status === "Approved") {
    return "approved";
  }

  if (status === "Rejected") {
    return "rejected";
  }

  return "readiness-ready";
}

const statutoryDiscountRejectionReasons = [
  { code: "ID_NOT_VALID", label: "Document is invalid" },
  { code: "ENTITLEMENT_MISMATCH", label: "Document does not match the requested privilege" },
  { code: "REQUIRED_DOCUMENT_MISSING", label: "Required document is missing" },
  { code: "RESIDENCY_NOT_VERIFIED", label: "Residency could not be verified" },
  { code: "DRIVER_CONDITION_NOT_MET", label: "Required driver condition was not met" },
  { code: "PASSENGER_CONDITION_NOT_MET", label: "Required passenger condition was not met" },
  { code: "INFORMATION_INCONSISTENT", label: "Information is inconsistent" },
  { code: "OTHER_REVIEW_ISSUE", label: "Other review issue" }
];

function rejectionReasonLabel(reasonCode?: string | null) {
  const normalized = (reasonCode ?? "").trim().toUpperCase();
  if (!normalized) {
    return "Review issue recorded";
  }

  return statutoryDiscountRejectionReasons.find((reason) => reason.code === normalized)?.label ?? "Review issue recorded";
}

function plainEvidenceStatus(detail: StatutoryDiscountDraftDetail, evidenceSatisfied: boolean) {
  if (detail.status === "Approved" && Boolean(detail.validatedAt || detail.validatedByUserId)) {
    return "Verified";
  }

  const normalized = (detail.latestEvidenceStatus ?? "").trim().toUpperCase();
  if (normalized.includes("INVALID") || normalized.includes("REJECTED")) {
    return "Invalid";
  }

  if (normalized.includes("MISSING") || !detail.evidenceCaptured) {
    return "Missing";
  }

  if (evidenceSatisfied || normalized.includes("SUBMITTED") || normalized.includes("CAPTURED")) {
    return "Pending review";
  }

  return "Needs review";
}

function StatutoryDecisionReadOnlySummary({
  detail,
  fallbackReason
}: {
  detail: StatutoryDiscountDraftDetail;
  fallbackReason: string;
}) {
  if (detail.status === "Approved") {
    return (
      <div className="readOnlyDecisionSummary">
        <p className="successMessage">Parking privilege approved</p>
        <p>Central PMS will apply the approved privilege when the customer proceeds with payment through WebPay or the Cashier-Assisted Terminal.</p>
      </div>
    );
  }

  if (detail.status === "Rejected") {
    return (
      <div className="readOnlyDecisionSummary">
        <p className="errorMessage">Parking privilege rejected</p>
        <p>{rejectionReasonLabel(detail.decisionReasonCode)}</p>
      </div>
    );
  }

  return <p className="notice">{fallbackReason}</p>;
}

function statutoryDiscountDecisionReadOnlyReason(
  detail: StatutoryDiscountDraftDetail,
  currentOperatorUserId: string,
  canApproveDecision: boolean,
  canRejectDecision: boolean,
  decisionable: boolean
) {
  if (!decisionable || ["Approved", "Rejected", "Cancelled", "Expired", "Blocked"].includes(detail.status)) {
    return "Decision is read-only for the current validation status.";
  }

  if (sameIdentity(detail.requestedBy, currentOperatorUserId)) {
    return "You cannot approve or reject your own statutory discount request.";
  }

  if (!canApproveDecision && !canRejectDecision) {
    return "Decision requires an authorized reviewer.";
  }

  return null;
}

function readinessBlockedActionReason(readiness: AccessReadinessResponse) {
  const reasonCodes = readiness.denialReasons.map((reason) => reason.code).join(", ");
  return `Readiness check is blocking controlled Operator Console actions.${reasonCodes ? ` Reasons: ${reasonCodes}.` : ""}`;
}

function scopeSummary(session: OperatorConsoleHumanSession) {
  if (session.hasGlobalScope) {
    return "Global operating scope";
  }

  const siteCount = session.siteReferences.length;
  const siteGroupCount = session.siteGroupReferences.length;
  if (siteCount === 0 && siteGroupCount === 0) {
    return "No operating scope";
  }

  return `${siteCount} Site${siteCount === 1 ? "" : "s"}, ${siteGroupCount} Site Group${siteGroupCount === 1 ? "" : "s"}`;
}

function sameIdentity(left?: string | null, right?: string | null) {
  return (left ?? "").trim().toLowerCase() === (right ?? "").trim().toLowerCase();
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

function normalizeStatus(status?: string | null) {
  return status?.trim().toUpperCase().replaceAll(" ", "_") ?? "";
}

function newUiRequestId() {
  return globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

function displayValue(value?: string) {
  return value && value.trim().length > 0 ? value : "Not available";
}

function formatTicketLookupMoney(minorUnits?: number, currencyCode?: string) {
  if (minorUnits === undefined || currencyCode !== "PHP") {
    return "Not available";
  }

  return formatPhpMoney(minorUnits, currencyCode);
}

export function maskStatutoryIdReference(value: string) {
  const normalized = value.trim();
  if (!normalized || normalized.length > 64 || !/^[\p{L}\p{N}-]+$/u.test(normalized)) {
    return null;
  }

  return normalized.length <= 4
    ? normalized
    : `${"*".repeat(normalized.length - 4)}${normalized.slice(-4)}`;
}

function idDocumentTypeForEntitlement(entitlementType: "SENIOR_CITIZEN" | "PWD") {
  return entitlementType === "PWD" ? "PWD" : "Senior Citizen";
}

function statutoryDocumentTypeLabel(value: string | undefined, entitlementType: string) {
  const normalized = (value ?? "").trim().toUpperCase().replaceAll("_", " ");
  if (normalized.includes("PWD")) return "PWD";
  if (normalized.includes("SENIOR")) return "Senior Citizen";
  return plainEntitlementLabel(entitlementType);
}

function statutoryDocumentReviewStatus(detail: StatutoryDiscountDraftDetail) {
  if (detail.status === "Approved" && Boolean(detail.validatedAt || detail.validatedByUserId)) return "Verified";
  if (detail.status === "Rejected") return "Rejected";
  return "Pending review";
}

export function statutoryDraftErrorMessage(message: string) {
  if (message.includes("MaskedIdReference")) {
    return "Enter a valid ID reference.";
  }

  if (/policy|ordinance|verification unknown|source unavailable/i.test(message)) {
    return "This statutory entitlement is not available for this Site.";
  }

  return message;
}

function operatorDisplayValue(item: { operatorDisplayName?: string; operatorUsername?: string }) {
  return displayValue(item.operatorDisplayName ?? item.operatorUsername ?? "Unknown user");
}

function siteDisplayValue(item: { siteName?: string }) {
  return displayValue(item.siteName);
}

function siteGroupDisplayValue(item: { siteGroupName?: string }) {
  return displayValue(item.siteGroupName);
}

function displayStatusValue(value?: string) {
  if (!value || value.trim().length === 0) {
    return "Not available";
  }

  return value
    .trim()
    .split(/[_\s-]+/)
    .filter(Boolean)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1).toLowerCase())
    .join(" ");
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

export function formatParkingDuration(value?: number) {
  if (value === undefined || value < 0) return "Not available";
  const totalMinutes = Math.floor(value / 60);
  const hours = Math.floor(totalMinutes / 60);
  const minutes = totalMinutes % 60;
  if (hours === 0) return `${minutes} ${minutes === 1 ? "minute" : "minutes"}`;
  const hourText = `${hours} ${hours === 1 ? "hour" : "hours"}`;
  return minutes === 0 ? hourText : `${hourText} ${String(minutes).padStart(2, "0")} minutes`;
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
