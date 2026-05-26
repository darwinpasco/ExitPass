import { createStatutoryDiscountOperatorApiClient } from "./apiClient";
import type { OperatorSessionSummary, StatutoryDiscountReview } from "./types";

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

export function App() {
  createStatutoryDiscountOperatorApiClient();

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
        <form className="panel searchPanel" aria-labelledby="session-search-title">
          <div className="panelHeader">
            <p className="eyebrow">Lookup</p>
            <h2 id="session-search-title">Session search</h2>
          </div>

          <label>
            Ticket number
            <input name="ticketNumber" type="text" placeholder="Enter ticket number" />
          </label>

          <label>
            Plate number
            <input name="plateNumber" type="text" placeholder="Enter plate number" />
          </label>

          <label>
            Site / site group
            <input value={placeholderSession.siteDisplayName} readOnly />
          </label>

          <button type="button" disabled>
            Search session
          </button>
        </form>

        <section className="panel" aria-labelledby="session-summary-title">
          <div className="panelHeader">
            <p className="eyebrow">Parking session</p>
            <h2 id="session-summary-title">Session summary</h2>
          </div>

          <dl className="summaryList">
            <div>
              <dt>Parking session reference</dt>
              <dd>{placeholderSession.parkingSessionReference}</dd>
            </div>
            <div>
              <dt>Vehicle plate</dt>
              <dd>{placeholderSession.vehiclePlate}</dd>
            </div>
            <div>
              <dt>Entry time</dt>
              <dd>{placeholderSession.entryTime}</dd>
            </div>
            <div>
              <dt>Current fee</dt>
              <dd>{placeholderSession.currentFee}</dd>
            </div>
            <div>
              <dt>Payable-basis status</dt>
              <dd>{placeholderSession.payableBasisStatus}</dd>
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
