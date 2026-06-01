import { useEffect, useMemo, useState } from "react";
import { createOperatorConsoleApiClient, mapApiError, type OperatorConsoleApiClient } from "./apiClient";
import type {
  LoadState,
  PolicyContextKind,
  StatutoryDiscountDraftDetail,
  StatutoryDiscountPolicyContext,
  StatutoryDiscountQueueItem
} from "./types";

const routes = {
  home: "/operator-console",
  queue: "/operator-console/statutory-discounts",
  detail: "/operator-console/statutory-discounts/"
};

interface AppProps {
  apiClient?: OperatorConsoleApiClient;
  initialPath?: string;
}

export function App({ apiClient, initialPath }: AppProps) {
  const client = useMemo(() => apiClient ?? createOperatorConsoleApiClient(), [apiClient]);
  const [path, setPath] = useState(initialPath ?? normalizePath(window.location.pathname));

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

  const draftId = path.startsWith(routes.detail) ? path.slice(routes.detail.length) : null;

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
              className={`navLink ${path.startsWith(routes.queue) ? "navLinkActive" : ""}`}
              type="button"
              onClick={() => navigate(routes.queue)}
            >
              Statutory Discounts
            </button>
          </nav>

          <div className="statusStack">
            <span className="statusPill">Local shell</span>
            <span className="statusPill">Mock queue adapter</span>
          </div>
        </aside>

        <section className="workspace">
          {draftId ? (
            <StatutoryDiscountDetailPage client={client} draftId={draftId} navigate={navigate} />
          ) : path === routes.queue ? (
            <StatutoryDiscountQueuePage client={client} navigate={navigate} />
          ) : path === routes.home ? (
            <OperatorConsoleHome navigate={navigate} />
          ) : (
            <NotFoundPage navigate={navigate} />
          )}
        </section>
      </section>
    </main>
  );
}

function OperatorConsoleHome({ navigate }: { navigate: (path: string) => void }) {
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
      <button type="button" onClick={() => navigate(routes.queue)}>
        Open work queue
      </button>
    </section>
  );
}

function StatutoryDiscountQueuePage({
  client,
  navigate
}: {
  client: OperatorConsoleApiClient;
  navigate: (path: string) => void;
}) {
  const [queueState, setQueueState] = useState<LoadState<StatutoryDiscountQueueItem[]>>({ status: "loading" });

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
  }, [client]);

  return (
    <>
      <section className="pageTitle">
        <div>
          <p className="eyebrow">Statutory Discount Validation</p>
          <h2>Work queue</h2>
          <p>
            Review Senior Citizen and PWD statutory discount drafts with policy context before a decision workflow is
            wired.
          </p>
        </div>
      </section>

      <section className="panel" aria-labelledby="queue-title">
        <div className="panelHeader">
          <h3 id="queue-title">Validation drafts</h3>
          <span className="statusPill">Temporary mock data</span>
        </div>

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
  navigate
}: {
  client: OperatorConsoleApiClient;
  draftId: string;
  navigate: (path: string) => void;
}) {
  const [detailState, setDetailState] = useState<LoadState<StatutoryDiscountDraftDetail>>({ status: "loading" });

  useEffect(() => {
    let active = true;
    setDetailState({ status: "loading" });

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
  }, [client, draftId]);

  return (
    <>
      <button className="backButton" type="button" onClick={() => navigate(routes.queue)}>
        Back to queue
      </button>

      {detailState.status === "loading" && <StateMessage title="Loading draft" message="Retrieving draft details." />}
      {detailState.status === "not-found" && <StateMessage title="Draft not found" message="The requested draft was not found." />}
      {detailState.status === "access-denied" && <StateMessage title="Access denied" message={detailState.message} />}
      {detailState.status === "error" && <StateMessage title="Unable to load draft" message={detailState.message} />}
      {detailState.status === "loaded" && <DraftDetail detail={detailState.data} />}
    </>
  );
}

function DraftDetail({ detail }: { detail: StatutoryDiscountDraftDetail }) {
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
              ["Evidence required", detail.policyContext.evidenceRequired ? "Yes" : "No"]
            ]}
          />
        </section>
      </section>

      <PolicyContextDisplay policy={detail.policyContext} />

      <section className="panel" aria-labelledby="decision-title">
        <div className="panelHeader">
          <h3 id="decision-title">Decision actions</h3>
          <span className="statusPill">Wiring pending</span>
        </div>
        <p className="placeholderCopy">
          Approve and reject endpoints exist, but this foundation slice keeps decision controls disabled until the
          dedicated decision workflow and evidence UX slice.
        </p>
        <div className="actionBar">
          <button type="button" disabled>
            Approve
          </button>
          <button type="button" disabled>
            Reject
          </button>
        </div>
      </section>

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

function PolicyContextDisplay({ policy }: { policy: StatutoryDiscountPolicyContext }) {
  return (
    <section className={`panel policyPanel policy-${policy.kind}`} aria-labelledby="policy-context-title">
      <div className="panelHeader">
        <p className="eyebrow">Policy context</p>
        <h3 id="policy-context-title">{policy.title}</h3>
      </div>
      <p className="policySummary">{policy.operatorSummary}</p>
      <DescriptionList
        items={[
          ["Policy basis", policyBasisLabel(policy.kind)],
          ["Resolution basis", policy.policyResolutionBasis],
          ["Policy code", policy.policyCode ?? "Not available"],
          ["Policy name", policy.policyName ?? "Not available"],
          ["Legal basis", policy.legalBasisReference ?? "Not available"],
          ["National law", policy.nationalLawReference ?? "Not available"],
          ["Local ordinance", policy.ordinanceReference ?? "None"],
          ["Verification status", policy.verificationStatus ?? "Not available"],
          ["Benefit type", policy.benefitType ?? "Not available"],
          ["Evidence required", policy.evidenceRequired ? "Yes" : "No"],
          ["Operator action", policy.ineligibilityReason ?? "Review can proceed when access and evidence rules allow."]
        ]}
      />
    </section>
  );
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

function normalizePath(path: string) {
  if (path === "/" || path === "") {
    return routes.home;
  }

  return path;
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

function policyBasisLabel(kind: PolicyContextKind) {
  return {
    "national-fallback": "RA 9994 / RA 10754 national fallback",
    "verified-local": "Verified local policy",
    "blocked-unverified-local": "Blocked unverified local policy",
    "unsupported-entitlement": "Unsupported entitlement",
    "missing-site-jurisdiction": "Missing site jurisdiction"
  }[kind];
}

function statusClass(status: string) {
  return status.toLowerCase().replaceAll(" ", "-");
}
