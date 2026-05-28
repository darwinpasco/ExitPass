import { createOperatorConsoleApiClient, type OperatorConsoleApiClient } from "./apiClient";
import { FormEvent, useMemo, useState } from "react";
import type {
  OperatorConsoleModule,
  OperatorSessionSummary,
  SessionLookupStatus,
  StatutoryDiscountReview
} from "./types";

const placeholderSession: OperatorSessionSummary = {
  parkingSessionReference: "Session reference will appear here",
  vehiclePlate: "Plate will appear here",
  entryTime: "Entry time will appear here",
  currentFee: "Current fee will appear here",
  paymentStatus: "Payment status will appear here as read-only context",
  payableBasisStatus: "Backend-approved payable basis will appear here",
  siteDisplayName: "Site / site group will appear here"
};

const placeholderReview: StatutoryDiscountReview = {
  entitlementType: "Senior Citizen",
  validationStatus: "no request",
  operatorInstruction:
    "Evidence capture and backend statutory discount validation will be wired in a later slice."
};

const validationStatuses = [
  "no request",
  "pending operator review",
  "approved",
  "rejected",
  "expired"
];

const operatorConsoleModules: OperatorConsoleModule[] = [
  {
    name: "Session Lookup",
    status: "available",
    description: "Find parking sessions by ticket or plate before reviewing operator workflows."
  },
  {
    name: "Statutory Discount Validation",
    status: "first module",
    description: "Initial module for senior citizen and PWD validation review."
  },
  {
    name: "Registered Device and Shift Access",
    status: "planned",
    description: "Placeholder for device registration and shift-based operator access."
  },
  {
    name: "Supervisor Review and Overrides",
    status: "planned",
    description: "Placeholder for supervised review paths and override workflows."
  },
  {
    name: "Audit and Reporting",
    status: "planned",
    description: "Placeholder for operational audit trails and reporting views."
  }
];

interface AppProps {
  apiClient?: OperatorConsoleApiClient;
}

export function App({ apiClient }: AppProps) {
  const client = useMemo(() => apiClient ?? createOperatorConsoleApiClient(), [apiClient]);
  const [ticketNumber, setTicketNumber] = useState("");
  const [plateNumber, setPlateNumber] = useState("");
  const [lookupStatus, setLookupStatus] = useState<SessionLookupStatus>("not searched");
  const [session, setSession] = useState<OperatorSessionSummary>(placeholderSession);
  const [ambiguousMatches, setAmbiguousMatches] = useState(0);

  const canSearch = ticketNumber.trim().length > 0 || plateNumber.trim().length > 0;

  async function handleSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (!canSearch || lookupStatus === "searching") {
      return;
    }

    setLookupStatus("searching");
    setAmbiguousMatches(0);

    const result = await client.findSession({ ticketNumber, plateNumber });

    setLookupStatus(result.status);
    if (result.status === "session found") {
      setSession(result.session);
      return;
    }

    if (result.status === "ambiguous session") {
      setSession(placeholderSession);
      setAmbiguousMatches(result.matches);
      return;
    }

    setSession(placeholderSession);
  }

  const statusCopy = {
    "not searched": "Enter a ticket number or plate number to look up a parking session.",
    searching: "Searching for a matching parking session.",
    "session found": "Session found. Review the read-only session context before statutory discount validation is wired.",
    "not found": "No matching parking session was found for the entered criteria.",
    "ambiguous session": `Multiple matching sessions were found${ambiguousMatches > 0 ? ` (${ambiguousMatches})` : ""}. Operator disambiguation will be wired in a later slice.`
  }[lookupStatus];

  return (
    <main className="appShell" aria-labelledby="app-title">
      <header className="appHeader">
        <div>
          <p className="eyebrow">Operator workspace</p>
          <h1 id="app-title">ExitPass Operator Console</h1>
          <p className="headerCopy">
            Operator-facing console for ExitPass site workflows. Statutory Discount Validation is the first module.
          </p>
        </div>
        <span className="environmentBadge">Scaffold</span>
      </header>

      <section className="platformShell" aria-label="ExitPass Operator Console platform shell">
        <aside className="moduleRail" aria-labelledby="module-navigation-title">
          <div className="panelHeader">
            <p className="eyebrow">Platform modules</p>
            <h2 id="module-navigation-title">Navigation</h2>
          </div>

          <nav aria-label="Operator Console modules">
            {operatorConsoleModules.map((module) => (
              <a className="moduleLink" href={`#${module.name.toLowerCase().replaceAll(" ", "-")}`} key={module.name}>
                <span>{module.name}</span>
                <span className="moduleStatus">{module.status}</span>
              </a>
            ))}
          </nav>

          <p className="moduleRailCopy">
            Payment collection stays outside the Operator Console. Payment status appears only as read-only context.
          </p>
        </aside>

        <section className="workspace" aria-label="Operator Console workspace">
          <section className="moduleOverview" aria-label="Module placeholders">
            {operatorConsoleModules.map((module) => (
              <article className="moduleCard" id={module.name.toLowerCase().replaceAll(" ", "-")} key={module.name}>
                <div>
                  <h3>{module.name}</h3>
                  <p>{module.description}</p>
                </div>
                <span className="moduleStatus">{module.status}</span>
              </article>
            ))}
          </section>

          <section className="layoutGrid" aria-label="Session Lookup and Statutory Discount Validation workspace">
            <form className="panel searchPanel" aria-labelledby="session-search-title" onSubmit={handleSearch}>
              <div className="panelHeader">
                <p className="eyebrow">Session Lookup</p>
                <h2 id="session-search-title">Session search</h2>
              </div>

              <label>
                Ticket number
                <input
                  name="ticketNumber"
                  type="text"
                  placeholder="Enter ticket number"
                  value={ticketNumber}
                  onChange={(event) => setTicketNumber(event.target.value)}
                />
              </label>

              <label>
                Plate number
                <input
                  name="plateNumber"
                  type="text"
                  placeholder="Enter plate number"
                  value={plateNumber}
                  onChange={(event) => setPlateNumber(event.target.value)}
                />
              </label>

              <label>
                Site / site group
                <input value={session.siteDisplayName} readOnly />
              </label>

              <button type="submit" disabled={!canSearch || lookupStatus === "searching"}>
                {lookupStatus === "searching" ? "Searching" : "Search session"}
              </button>

              <div className="lookupState" role="status" aria-live="polite">
                <span className="statusPill">{lookupStatus}</span>
                <p>{statusCopy}</p>
              </div>
            </form>

            <section className="panel" aria-labelledby="session-summary-title">
              <div className="panelHeader">
                <p className="eyebrow">Read-only context</p>
                <h2 id="session-summary-title">Session summary</h2>
              </div>

              <dl className="summaryList">
                <div>
                  <dt>Parking session reference</dt>
                  <dd>{session.parkingSessionReference}</dd>
                </div>
                <div>
                  <dt>Vehicle plate</dt>
                  <dd>{session.vehiclePlate}</dd>
                </div>
                <div>
                  <dt>Entry time</dt>
                  <dd>{session.entryTime}</dd>
                </div>
                <div>
                  <dt>Current fee</dt>
                  <dd>{session.currentFee}</dd>
                </div>
                <div>
                  <dt>Payment status</dt>
                  <dd>{session.paymentStatus}</dd>
                </div>
                <div>
                  <dt>Payable-basis status</dt>
                  <dd>{session.payableBasisStatus}</dd>
                </div>
              </dl>
            </section>

            <section className="panel reviewPanel" aria-labelledby="discount-review-title">
              <div className="panelHeader">
                <p className="eyebrow">Module</p>
                <h2 id="discount-review-title">Statutory Discount Validation</h2>
              </div>

              <div className="reviewControls" aria-label="Entitlement type">
                <label>
                  <input type="radio" name="entitlementType" value="Senior Citizen" defaultChecked disabled />
                  Senior Citizen
                </label>
                <label>
                  <input type="radio" name="entitlementType" value="PWD" disabled />
                  PWD
                </label>
              </div>

              <dl className="summaryList">
                <div>
                  <dt>Validation status</dt>
                  <dd>
                    <span className="statusPill">{placeholderReview.validationStatus}</span>
                  </dd>
                </div>
                <div>
                  <dt>Available statuses</dt>
                  <dd>{validationStatuses.join(", ")}</dd>
                </div>
                <div>
                  <dt>Operator instruction</dt>
                  <dd>{placeholderReview.operatorInstruction}</dd>
                </div>
              </dl>

              <p className="notice">
                Payment collection is out of scope. This console cannot accept payments, confirm payments, issue
                refunds, manually mark payments as paid, apply coupons, or issue exit authorization. Payment status is
                displayed read-only when needed for operator context.
              </p>

              <div className="actionBar" aria-label="Operator decision controls">
                <button type="button" disabled>
                  Approve
                </button>
                <button type="button" disabled>
                  Reject
                </button>
                <button type="button" disabled>
                  Request more information
                </button>
              </div>
            </section>
          </section>
        </section>
      </section>
    </main>
  );
}
