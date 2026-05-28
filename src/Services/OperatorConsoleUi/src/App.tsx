import { createStatutoryDiscountOperatorApiClient, type StatutoryDiscountOperatorApiClient } from "./apiClient";
import { FormEvent, useMemo, useState } from "react";
import type {
  OperatorSessionSummary,
  SessionLookupStatus,
  StatutoryDiscountReview
} from "./types";

const placeholderSession: OperatorSessionSummary = {
  parkingSessionReference: "Session reference will appear here",
  vehiclePlate: "Plate will appear here",
  entryTime: "Entry time will appear here",
  currentFee: "Current fee will appear here",
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

interface AppProps {
  apiClient?: StatutoryDiscountOperatorApiClient;
}

export function App({ apiClient }: AppProps) {
  const client = useMemo(() => apiClient ?? createStatutoryDiscountOperatorApiClient(), [apiClient]);
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
    "session found": "Session found. Review the placeholder summary before discount validation is wired.",
    "not found": "No matching parking session was found for the entered criteria.",
    "ambiguous session": `Multiple matching sessions were found${ambiguousMatches > 0 ? ` (${ambiguousMatches})` : ""}. Operator disambiguation will be wired in a later slice.`
  }[lookupStatus];

  return (
    <main className="appShell" aria-labelledby="app-title">
      <header className="appHeader">
        <div>
          <p className="eyebrow">Operator workspace</p>
          <h1 id="app-title">ExitPass Statutory Discount Operator</h1>
        </div>
        <span className="environmentBadge">Scaffold</span>
      </header>

      <section className="layoutGrid" aria-label="Statutory discount operator scaffold">
        <form className="panel searchPanel" aria-labelledby="session-search-title" onSubmit={handleSearch}>
          <div className="panelHeader">
            <p className="eyebrow">Lookup</p>
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
            <p className="eyebrow">Parking session</p>
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
              <dt>Payable-basis status</dt>
              <dd>{session.payableBasisStatus}</dd>
            </div>
          </dl>
        </section>

        <section className="panel reviewPanel" aria-labelledby="discount-review-title">
          <div className="panelHeader">
            <p className="eyebrow">Statutory discount</p>
            <h2 id="discount-review-title">Review request</h2>
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
            This scaffold does not capture evidence, evaluate entitlement, apply coupons, initiate payment,
            or issue exit authorization.
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
    </main>
  );
}
