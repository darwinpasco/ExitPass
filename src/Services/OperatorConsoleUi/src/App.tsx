import { useEffect, useMemo, useState, type FormEvent } from "react";
import { createOperatorConsoleApiClient, mapApiError, type OperatorConsoleApiClient } from "./apiClient";
import type {
  LoadState,
  PolicyContextKind,
  StatutoryDiscountDraftDetail,
  StatutoryDiscountEvidenceList,
  EvidenceCaptureMethod,
  EvidenceType,
  StatutoryDiscountPayableBasisApplicationResult,
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
            <span className="statusPill">Live read model</span>
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
        <button type="button" onClick={() => setRefreshToken((value) => value + 1)}>
          Refresh
        </button>
      </section>

      <section className="panel" aria-labelledby="queue-title">
        <div className="panelHeader">
          <h3 id="queue-title">Validation drafts</h3>
          <span className="statusPill">Live read model</span>
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
        />
      )}
    </>
  );
}

function DraftDetail({
  detail,
  client,
  refreshDetail
}: {
  detail: StatutoryDiscountDraftDetail;
  client: OperatorConsoleApiClient;
  refreshDetail: () => void;
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
  const approvalDisabledReason = approvalBlockReason(detail, submittingDecision !== null);
  const rejectDisabledReason = !decisionable ? "Decision is read-only for the current validation status." : null;
  const payableBasisDisabledReason = payableBasisBlockReason(detail, submittingPayableBasis);

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

      <EvidencePanel detail={detail} client={client} refreshDetail={refreshDetail} />

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
  refreshDetail
}: {
  detail: StatutoryDiscountDraftDetail;
  client: OperatorConsoleApiClient;
  refreshDetail: () => void;
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
            <button type="submit" disabled={submitting}>
              {submitting ? "Capturing" : "Capture evidence"}
            </button>
          </form>
        </div>
      )}
    </section>
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

function statusClass(status: string) {
  return status.toLowerCase().replaceAll(" ", "-");
}
