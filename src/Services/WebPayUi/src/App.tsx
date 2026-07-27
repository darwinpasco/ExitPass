import { FormEvent, KeyboardEvent, useEffect, useRef, useState } from "react";
import { QrScanner } from "./QrScanner";
import {
  ActivePaymentAttemptError,
  PayableBasisRefreshRequiredError,
  ReceiptPresentationError,
  createPaymentIntent,
  createRequestReference,
  createStatutoryDecisionIdempotencyKey,
  extractPaymentIntentContext,
  formatAmount,
  getResumeUrl,
  normalizeTicketReference,
  retrieveReceiptPresentation,
  retrievePaymentStatus,
  retrieveStatutoryDiscountDecision,
  resolveParkingSession,
  submitStatutoryDiscountDecision
} from "./webpay";
import type {
  ActivePaymentAttemptState,
  ParkingSessionResolveResponse,
  ParkingSessionSummary,
  PaymentIntentRequest,
  PaymentIntentResponse,
  PaymentMethod,
  StatutoryDiscountEntitlementType,
  WebPayReceiptPresentationResponse,
  WebPayStatutoryDiscountDecisionResponse
} from "./types";

const paymentMethods: Array<{ code: PaymentMethod; label: string; image: string; helper: string }> = [
  {
    code: "QRPH",
    label: "QRPh",
    image: "/assets/payment-methods/qrph.png",
    helper: "Pay using any QRPh-supported bank or e-wallet"
  },
  {
    code: "GCASH",
    label: "GCash",
    image: "/assets/payment-methods/gcash.png",
    helper: "Pay with GCash through PayMongo Checkout"
  },
  {
    code: "MAYA",
    label: "Maya",
    image: "/assets/payment-methods/maya.png",
    helper: "Pay with Maya through PayMongo Checkout"
  },
  {
    code: "CARD",
    label: "Card",
    image: "/assets/payment-methods/cards-visa-mastercard.png",
    helper: "Visa or Mastercard through PayMongo Checkout"
  }
];

type EntryMode = "ticket" | "plate";
type WebPayStage = "INPUT" | "SESSION_RESOLVED" | "HANDOFF_READY" | "ACTIVE_ATTEMPT" | "ERROR";
type ReturnPageMode = "success" | "cancelled";
type PaymentStatusKind = "pending" | "confirmed" | "failed" | "expired" | "cancelled";
type StatutoryDiscountFormState = {
  entitlementType: StatutoryDiscountEntitlementType;
  idDocumentType: string;
  issuingAuthority: string;
  expiryDate: string;
  maskedIdReference: string;
  requesterAttestation: boolean;
  attestationNotes: string;
};

type StatutoryDiscountUiState = {
  decision: WebPayStatutoryDiscountDecisionResponse | null;
  requestReference: string;
  idempotencyKey: string;
  correlationId: string;
  isSubmitting: boolean;
  isPolling: boolean;
  message: string;
  error: string;
};

const defaultStatutoryDiscountForm: StatutoryDiscountFormState = {
  entitlementType: "SENIOR_CITIZEN",
  idDocumentType: "",
  issuingAuthority: "",
  expiryDate: "",
  maskedIdReference: "",
  requesterAttestation: false,
  attestationNotes: ""
};

const emptyStatutoryDiscountUiState: StatutoryDiscountUiState = {
  decision: null,
  requestReference: "",
  idempotencyKey: "",
  correlationId: "",
  isSubmitting: false,
  isPolling: false,
  message: "",
  error: ""
};

export function App() {
  const initialTicketReference = getQueryParam("ticketReference");
  const [entryMode, setEntryMode] = useState<EntryMode>("ticket");
  const [ticketReference, setTicketReference] = useState(initialTicketReference);
  const [scannedContext, setScannedContext] = useState<Partial<PaymentIntentRequest>>({});
  const [plateNumber, setPlateNumber] = useState("");
  const [paymentMethod, setPaymentMethod] = useState<PaymentMethod>("QRPH");
  const [resolvedSession, setResolvedSession] = useState<ParkingSessionResolveResponse | null>(null);
  const [result, setResult] = useState<PaymentIntentResponse | null>(null);
  const [activePaymentAttempt, setActivePaymentAttempt] = useState<ActivePaymentAttemptState | null>(null);
  const [error, setError] = useState("");
  const [resolveError, setResolveError] = useState("");
  const [payableBasisRefreshRequired, setPayableBasisRefreshRequired] = useState(false);
  const [isResolving, setIsResolving] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [stage, setStage] = useState<WebPayStage>("INPUT");
  const [showStatutoryDiscountForm, setShowStatutoryDiscountForm] = useState(false);
  const [statutoryDiscountForm, setStatutoryDiscountForm] = useState<StatutoryDiscountFormState>(defaultStatutoryDiscountForm);
  const [statutoryDiscountState, setStatutoryDiscountState] = useState<StatutoryDiscountUiState>(emptyStatutoryDiscountUiState);
  const statutoryPollGeneration = useRef(0);

  const returnPageMode = getReturnPageMode(window.location.pathname);
  if (returnPageMode) {
    return <WebPayReturnPage mode={returnPageMode} />;
  }

  function handleQrDecoded(value: string) {
    const normalized = normalizeTicketReference(value);
    const context = extractPaymentIntentContext(value);
    setEntryMode("ticket");
    setTicketReference(normalized);
    setScannedContext(context);
    setError("");
    setResolveError("");
    setPayableBasisRefreshRequired(false);
    setResult(null);
    setResolvedSession(null);
    setActivePaymentAttempt(null);
    setShowStatutoryDiscountForm(false);
    setStatutoryDiscountForm(defaultStatutoryDiscountForm);
    setStatutoryDiscountState(emptyStatutoryDiscountUiState);
    setStage("INPUT");
  }

  function clearLookupState() {
    setError("");
    setResolveError("");
    setPayableBasisRefreshRequired(false);
    setResult(null);
    setResolvedSession(null);
    setActivePaymentAttempt(null);
    setShowStatutoryDiscountForm(false);
    setStatutoryDiscountForm(defaultStatutoryDiscountForm);
    setStatutoryDiscountState(emptyStatutoryDiscountUiState);
    setStage("INPUT");
  }

  function currentLookup() {
    const hasTicket = entryMode === "ticket" && ticketReference.trim().length > 0;
    const hasPlate = entryMode === "plate" && plateNumber.trim().length > 0;
    const lookupValue = entryMode === "ticket" ? ticketReference.trim() : plateNumber.trim();

    return { hasTicket, hasPlate, lookupValue };
  }

  async function handleResolveParkingSession() {
    const { hasTicket, hasPlate, lookupValue } = currentLookup();
    if (!hasTicket && !hasPlate) {
      setError(entryMode === "ticket" ? "Enter or scan a ticket reference." : "Enter a plate number.");
      setStage("ERROR");
      return;
    }

    const inputError = validateLookupInput(entryMode, lookupValue);
    if (inputError) {
      setError(inputError);
      setStage("ERROR");
      return;
    }

    setError("");
    setResolveError("");
    setResult(null);
    setActivePaymentAttempt(null);
    setIsResolving(true);

    try {
      const response = await resolveParkingSession({
        ticketReference: hasTicket ? lookupValue : undefined,
        plateNumber: hasPlate ? lookupValue : undefined,
        ...(hasTicket ? scannedContext : {})
      });
      setError("");
      setResult(null);
      setActivePaymentAttempt(null);
      setResolvedSession(response);
      setStage("SESSION_RESOLVED");
    } catch (apiError) {
      setResolvedSession(null);
      setResolveError(apiError instanceof Error ? apiError.message : "Parking lookup failed. Please try again.");
      setStage("ERROR");
    } finally {
      setIsResolving(false);
    }
  }

  async function handleCreatePaymentIntent() {
    if (stage !== "SESSION_RESOLVED" || !resolvedSession) {
      await handleResolveParkingSession();
      return;
    }

    const { hasTicket, hasPlate } = currentLookup();
    if (!hasTicket && !hasPlate) {
      setError(entryMode === "ticket" ? "Enter or scan a ticket reference." : "Enter a plate number.");
      setStage("ERROR");
      return;
    }

    setError("");
    setResult(null);
    setActivePaymentAttempt(null);
    setIsSubmitting(true);

    try {
      const response = await createPaymentIntent({
        ticketReference: hasTicket ? (resolvedSession.ticketReference ?? ticketReference.trim()) : undefined,
        plateNumber: hasPlate ? (resolvedSession.plateNumber ?? plateNumber.trim()) : undefined,
        paymentMethod,
        siteGroupId: resolvedSession.siteGroupId ?? scannedContext.siteGroupId,
        siteId: resolvedSession.siteId ?? scannedContext.siteId,
        vendorSystemId: resolvedSession.vendorSystemId ?? scannedContext.vendorSystemId,
        tariffSnapshotId: resolvedSession.tariffSnapshotId,
        expectedAmountMinorUnits: resolvedSession.amountMinorUnits
      }, fetch, {});
      setResult(response);
      setResolvedSession(toParkingSessionResolveResponse(response));
      setStage("HANDOFF_READY");
    } catch (apiError) {
      if (apiError instanceof ActivePaymentAttemptError) {
        setActivePaymentAttempt(apiError.activePaymentAttempt);
        setStage("ACTIVE_ATTEMPT");
      } else {
        setPayableBasisRefreshRequired(apiError instanceof PayableBasisRefreshRequiredError);
        setError(apiError instanceof Error ? apiError.message : "Payment intent creation failed. Please try again.");
        setStage("ERROR");
      }
    } finally {
      setIsSubmitting(false);
    }
  }

  function handleLookupEnter(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key !== "Enter") {
      return;
    }

    event.preventDefault();
    void handleResolveParkingSession();
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (isPaidStatus(resolvedSession?.paymentStatus)) {
      setError("");
      setStage("SESSION_RESOLVED");
      return;
    }

    if (activePaymentAttempt) {
      if (!continueActivePayment(activePaymentAttempt)) {
        setError("Payment is already in progress. Please wait a moment before checking again.");
      }
      return;
    }

    if (isResolving || isSubmitting) {
      return;
    }

    if (isStatutoryValidationPending(resolvedSession)) {
      setError("Statutory discount validation is pending review.");
      return;
    }

    if (statutoryDiscountState.decision) {
      setError(getStatutoryDiscountPaymentBlockMessage(statutoryDiscountState.decision));
      return;
    }

    if (stage !== "SESSION_RESOLVED" || !resolvedSession) {
      await handleResolveParkingSession();
      return;
    }

    await handleCreatePaymentIntent();
  }

  async function handleRecalculateFee() {
    setPayableBasisRefreshRequired(false);
    await handleResolveParkingSession();
  }

  async function handleSubmitStatutoryDiscount() {
    if (!resolvedSession || statutoryDiscountState.isSubmitting) {
      return;
    }

    const requestReference = statutoryDiscountState.requestReference || createRequestReference();
    const idempotencyKey =
      statutoryDiscountState.idempotencyKey ||
      createStatutoryDecisionIdempotencyKey(resolvedSession.parkingSessionId, statutoryDiscountForm.entitlementType);
    const correlationId = statutoryDiscountState.correlationId || createRequestReference();

    setStatutoryDiscountState((current) => ({
      ...current,
      requestReference,
      idempotencyKey,
      correlationId,
      isSubmitting: true,
      error: "",
      message: "Submitting statutory discount request..."
    }));
    setError("");

    try {
      const decision = await submitStatutoryDiscountDecision(
        {
          requestReference,
          parkingSessionId: resolvedSession.parkingSessionId,
          siteId: resolvedSession.siteId,
          siteGroupId: resolvedSession.siteGroupId,
          ticketReference: resolvedSession.ticketReference ?? ticketReference,
          plateNumber: resolvedSession.plateNumber ?? plateNumber,
          entitlementType: statutoryDiscountForm.entitlementType,
          idDocumentType: statutoryDiscountForm.idDocumentType,
          issuingAuthority: statutoryDiscountForm.issuingAuthority,
          expiryDate: statutoryDiscountForm.expiryDate || null,
          maskedIdReference: statutoryDiscountForm.maskedIdReference,
          evidenceCaptureRequested: false,
          requesterAttestation: statutoryDiscountForm.requesterAttestation,
          attestationNotes: statutoryDiscountForm.attestationNotes || null,
          originalTariffSnapshotId: resolvedSession.tariffSnapshotId
        },
        idempotencyKey,
        correlationId
      );

      setShowStatutoryDiscountForm(false);
      setStatutoryDiscountState((current) => ({
        ...current,
        decision,
        isSubmitting: false,
        isPolling: shouldPollStatutoryDecision(decision),
        message: getStatutoryDiscountStatusCopy(decision).body,
        error: ""
      }));
    } catch (apiError) {
      setStatutoryDiscountState((current) => ({
        ...current,
        isSubmitting: false,
        isPolling: false,
        error: apiError instanceof Error ? apiError.message : "Statutory discount request could not be submitted.",
        message: ""
      }));
    }
  }

  async function handleRefreshStatutoryDecision() {
    const decisionId = statutoryDiscountState.decision?.statutoryDiscountDecisionCommandId;
    if (!decisionId || statutoryDiscountState.isPolling) {
      return;
    }

    setStatutoryDiscountState((current) => ({ ...current, isPolling: true, error: "", message: "Refreshing statutory discount status..." }));
    try {
      const decision = await retrieveStatutoryDiscountDecision(decisionId, statutoryDiscountState.correlationId || undefined);
      setStatutoryDiscountState((current) => ({
        ...current,
        decision,
        isPolling: false,
        message: getStatutoryDiscountStatusCopy(decision).body,
        error: ""
      }));
    } catch (apiError) {
      setStatutoryDiscountState((current) => ({
        ...current,
        isPolling: false,
        error: apiError instanceof Error ? apiError.message : "Statutory discount status is temporarily unavailable.",
        message: ""
      }));
    }
  }

  useEffect(() => {
    const decision = statutoryDiscountState.decision;
    if (!decision || !statutoryDiscountState.isPolling || !shouldPollStatutoryDecision(decision)) {
      return;
    }

    const generation = statutoryPollGeneration.current + 1;
    statutoryPollGeneration.current = generation;
    const controller = new AbortController();
    let cancelled = false;

    async function pollDecision() {
      for (let attempt = 1; attempt <= 3; attempt += 1) {
        await new Promise((resolve) => window.setTimeout(resolve, attempt === 1 ? 0 : 800));
        if (cancelled || controller.signal.aborted || statutoryPollGeneration.current !== generation) {
          return;
        }

        try {
          const readback = await retrieveStatutoryDiscountDecision(
            decision!.statutoryDiscountDecisionCommandId,
            statutoryDiscountState.correlationId || undefined,
            fetch,
            controller.signal
          );
          if (cancelled || statutoryPollGeneration.current !== generation) {
            return;
          }

          const keepPolling = shouldPollStatutoryDecision(readback) && attempt < 3;
          setStatutoryDiscountState((current) => ({
            ...current,
            decision: readback,
            isPolling: keepPolling,
            message: getStatutoryDiscountStatusCopy(readback).body,
            error: ""
          }));

          if (!keepPolling) {
            return;
          }
        } catch (apiError) {
          if (cancelled || controller.signal.aborted || statutoryPollGeneration.current !== generation) {
            return;
          }

          setStatutoryDiscountState((current) => ({
            ...current,
            isPolling: false,
            error: apiError instanceof Error ? apiError.message : "Statutory discount status is temporarily unavailable.",
            message: ""
          }));
          return;
        }
      }
    }

    void pollDecision();

    return () => {
      cancelled = true;
      controller.abort();
    };
  }, [statutoryDiscountState.decision?.statutoryDiscountDecisionCommandId, statutoryDiscountState.isPolling, statutoryDiscountState.correlationId]);

  const handoff = result?.handoff;
  const activeResumeUrl = getResumeUrl(activePaymentAttempt?.handoff);
  const summary = resolvedSession ?? (result ? toParkingSessionResolveResponse(result) : null);
  const isPaymentComplete = isPaidStatus(summary?.paymentStatus);
  const isPayablePending = isStatutoryValidationPending(summary);
  const isActiveStatutoryWorkflow = Boolean(statutoryDiscountState.decision);
  const statutoryDiscountPaymentBlocked = isActiveStatutoryWorkflow || isPayablePending;

  return (
    <main className="app-shell">
      <header className="brand-header">
        <img className="exitpass-logo" src="/assets/logo/exitpass-logo.svg" alt="ExitPass" />
        <div className="operator-brand">
          <span>Operated with</span>
          <img src="/assets/logo/proparking-logo.png" alt="Pro Parking" />
        </div>
      </header>

      <section className="intro">
        <h1>Pay parking from your phone</h1>
        <p>Scan your ticket QR, enter a ticket reference, or use your plate number to start a secure payment handoff.</p>
      </section>

      <QrScanner onDecoded={handleQrDecoded} />

      <form className="payment-form" onSubmit={handleSubmit}>
        <div className="entry-tabs" role="tablist" aria-label="Parking lookup type">
          <button
            type="button"
            className={entryMode === "ticket" ? "entry-tab is-selected" : "entry-tab"}
            onClick={() => setEntryMode("ticket")}
          >
            <img src="/assets/icons/ticket.svg" alt="" aria-hidden="true" />
            Ticket
          </button>
          <button
            type="button"
            className={entryMode === "plate" ? "entry-tab is-selected" : "entry-tab"}
            onClick={() => setEntryMode("plate")}
          >
            <img src="/assets/icons/plate-number.svg" alt="" aria-hidden="true" />
            Plate
          </button>
        </div>

        {entryMode === "ticket" ? (
          <label className="field">
            <span>Ticket reference</span>
            <input
              name="ticketReference"
              value={ticketReference}
              onChange={(event) => {
                setTicketReference(event.target.value);
                setScannedContext({});
                clearLookupState();
              }}
              onKeyDown={handleLookupEnter}
              placeholder="Scan or enter ticket reference"
              autoComplete="off"
            />
          </label>
        ) : (
          <label className="field">
            <span>Plate number</span>
            <input
              name="plateNumber"
              value={plateNumber}
              onChange={(event) => {
                setPlateNumber(event.target.value);
                clearLookupState();
              }}
              onKeyDown={handleLookupEnter}
              placeholder="ABC 1234"
              autoCapitalize="characters"
              autoComplete="off"
            />
          </label>
        )}

        {isResolving && (
          <div className="inline-status" role="status">
            Resolving parking session...
          </div>
        )}

        {resolveError && (
          <div className="form-error" role="alert">
            <img src="/assets/icons/error.svg" alt="" aria-hidden="true" />
            <span>{resolveError}</span>
          </div>
        )}

        {summary && <ParkingSessionSummaryPanel result={summary} />}

        {summary && (
          <PayableBasisPanel
            session={summary}
          />
        )}

        {summary && stage === "SESSION_RESOLVED" && !isPaymentComplete && !isPayablePending && (
          <StatutoryDiscountRequestPanel
            session={summary}
            form={statutoryDiscountForm}
            state={statutoryDiscountState}
            showForm={showStatutoryDiscountForm}
            onShowForm={() => {
              setShowStatutoryDiscountForm(true);
              setStatutoryDiscountState(emptyStatutoryDiscountUiState);
              setError("");
            }}
            onCancel={() => {
              setShowStatutoryDiscountForm(false);
              setStatutoryDiscountForm(defaultStatutoryDiscountForm);
              setStatutoryDiscountState(emptyStatutoryDiscountUiState);
            }}
            onFormChange={setStatutoryDiscountForm}
            onSubmit={() => void handleSubmitStatutoryDiscount()}
            onRefresh={() => void handleRefreshStatutoryDecision()}
          />
        )}

        <section className="method-section" aria-labelledby="payment-method-heading">
          <h2 id="payment-method-heading">Payment method</h2>
          <div className="method-grid">
            {/* ExitPass v1.2 BRD 18.3 / SDD 10.2.4: customer-facing choices remain PayMongo-only provider routes. */}
            {paymentMethods.map((method) => (
              <label className={paymentMethod === method.code ? "method-card is-selected" : "method-card"} key={method.code}>
                <input
                  type="radio"
                  name="paymentMethod"
                  value={method.code}
                  checked={paymentMethod === method.code}
                  onChange={() => {
                    setPaymentMethod(method.code);
                    setError("");
                  }}
                />
                <img src={method.image} alt="" aria-hidden="true" />
                <span>{method.label}</span>
                <small>{method.helper}</small>
              </label>
            ))}
          </div>
        </section>

        {error && (
          <div className="form-error" role="alert">
            <img src="/assets/icons/error.svg" alt="" aria-hidden="true" />
            <span>{error}</span>
          </div>
        )}

        {payableBasisRefreshRequired && (
          <button
            type="button"
            className="ghost-button status-button"
            onClick={() => {
              void handleRecalculateFee();
            }}
            disabled={isResolving || isSubmitting}
          >
            Recalculate Fee
          </button>
        )}

        {activePaymentAttempt && (
          <section className="active-payment-panel" aria-live="polite" aria-labelledby="active-payment-heading">
            <div>
              <p className="eyebrow">Payment already started</p>
              <h2 id="active-payment-heading">Payment already started.</h2>
              <p>
                {activeResumeUrl
                  ? "You already have an active payment for this parking session."
                  : "You can check this payment status, but it cannot be resumed directly right now."}
              </p>
            </div>
            {activeResumeUrl && (
              <a
                className="primary-link"
                aria-label="Continue Existing Payment"
                href={activeResumeUrl}
              >
                Continue Existing Payment
              </a>
            )}
            <button
              type="button"
              className={activeResumeUrl ? "ghost-button status-button" : "primary-button status-button"}
              onClick={() => {
                if (!checkActivePaymentStatus(activePaymentAttempt)) {
                  setError("Payment is already in progress. Please wait a moment before checking again.");
                }
              }}
            >
                Check Status
            </button>
            <details className="support-details">
              <summary>Support details</summary>
              <dl>
                {activePaymentAttempt.correlationId && (
                  <div>
                    <dt>Correlation ID</dt>
                    <dd>{activePaymentAttempt.correlationId}</dd>
                  </div>
                )}
                {activePaymentAttempt.parkingSessionId && (
                  <div>
                    <dt>Parking session ID</dt>
                    <dd>{activePaymentAttempt.parkingSessionId}</dd>
                  </div>
                )}
                {activePaymentAttempt.paymentAttemptId && (
                  <div>
                    <dt>Payment attempt ID</dt>
                    <dd>{activePaymentAttempt.paymentAttemptId}</dd>
                  </div>
                )}
              </dl>
            </details>
          </section>
        )}

        {isPaymentComplete && (
          <section className="active-payment-panel" aria-live="polite" aria-labelledby="paid-payment-heading">
            <div>
              <p className="eyebrow">Payment completed</p>
              <h2 id="paid-payment-heading">Payment completed.</h2>
              <p>This parking session has a verified payment confirmation.</p>
            </div>
          </section>
        )}

        <button type="submit" className="submit-button" disabled={isSubmitting || isResolving || isPaymentComplete || statutoryDiscountPaymentBlocked}>
          <img src="/assets/icons/payment.svg" alt="" aria-hidden="true" />
          {isResolving
            ? "Resolving..."
            : isSubmitting
              ? "Creating payment..."
              : isActiveStatutoryWorkflow
                ? "Statutory discount pending"
              : isPayablePending
                ? "Discount review pending"
              : isPaymentComplete
                ? "Payment completed"
                : activePaymentAttempt
                ? (activeResumeUrl ? "Continue Existing Payment" : "Check Status")
                : summary
                  ? "Continue to Payment"
                  : "Continue"}
        </button>
      </form>

      {result && (
        <section className="handoff-panel" aria-live="polite">
          <img
            src={result.status.toUpperCase().includes("FAIL") ? "/assets/illustrations/payment-failed.svg" : "/assets/illustrations/payment-pending.svg"}
            alt=""
            aria-hidden="true"
          />
          <div>
            <p className="eyebrow">Payment handoff ready</p>
            <dl>
              <div>
                <dt>Payment Method</dt>
                <dd>{paymentMethods.find((method) => method.code === result.paymentMethod)?.label ?? "PayMongo Checkout"}</dd>
              </div>
              <div>
                <dt>Payment Status</dt>
                <dd>{result.status}</dd>
              </div>
            </dl>
            {handoff?.handoffUrl && (
              <a className="primary-link" href={handoff.handoffUrl}>
                Continue to Payment
              </a>
            )}
            {handoff?.qrCodeUrl && (
              <div className="qr-instructions">
                <strong>QR payment instructions</strong>
                <span>Open your preferred wallet and scan or follow the QR handoff link.</span>
                <code>{handoff.qrCodeUrl}</code>
              </div>
            )}
            <details className="support-details">
              <summary>Support details</summary>
              <dl>
                <div>
                  <dt>Correlation ID</dt>
                  <dd>{result.correlationId}</dd>
                </div>
                {result.selectedProviderCode && (
                  <div>
                    <dt>Routing provider</dt>
                    <dd>{result.selectedProviderCode}</dd>
                  </div>
                )}
                {result.fallbackProviderCode && (
                  <div>
                    <dt>Fallback provider</dt>
                    <dd>{result.fallbackProviderCode}</dd>
                  </div>
                )}
                {result.routingReason && (
                  <div>
                    <dt>Routing reason</dt>
                    <dd>{result.routingReason}</dd>
                  </div>
                )}
              </dl>
            </details>
          </div>
        </section>
      )}
    </main>
  );
}

function StatutoryDiscountRequestPanel({
  session,
  form,
  state,
  showForm,
  onShowForm,
  onCancel,
  onFormChange,
  onSubmit,
  onRefresh
}: {
  session: ParkingSessionResolveResponse;
  form: StatutoryDiscountFormState;
  state: StatutoryDiscountUiState;
  showForm: boolean;
  onShowForm: () => void;
  onCancel: () => void;
  onFormChange: (form: StatutoryDiscountFormState) => void;
  onSubmit: () => void;
  onRefresh: () => void;
}) {
  const decision = state.decision;
  const copy = decision ? getStatutoryDiscountStatusCopy(decision) : null;

  return (
    <section className="statutory-discount-panel" aria-labelledby="statutory-discount-heading">
      <div className="session-summary-header">
        <div>
          <p className="eyebrow">Statutory discount</p>
          <h2 id="statutory-discount-heading">Senior Citizen or PWD request</h2>
        </div>
        {!decision && !showForm && (
          <button type="button" className="secondary-button" onClick={onShowForm}>
            Request statutory discount
          </button>
        )}
      </div>

      {!decision && !showForm && (
        <p className="statutory-copy">
          Submit safe entitlement details for review. Payment remains unavailable until review and payable-basis application
          are complete; WebPay does not approve the entitlement.
        </p>
      )}

      {showForm && (
        <div className="statutory-form">
          <fieldset disabled={state.isSubmitting}>
            <legend>Entitlement details for review</legend>

            <label className="field">
              <span>Entitlement type</span>
              <select
                value={form.entitlementType}
                onChange={(event) => onFormChange({ ...form, entitlementType: event.target.value as StatutoryDiscountEntitlementType })}
              >
                <option value="SENIOR_CITIZEN">Senior Citizen</option>
                <option value="PWD">PWD</option>
              </select>
            </label>

            <label className="field">
              <span>ID document type</span>
              <input
                name="idDocumentType"
                value={form.idDocumentType}
                onChange={(event) => onFormChange({ ...form, idDocumentType: event.target.value })}
                placeholder="OSCA, PWD ID, or equivalent"
                autoComplete="off"
              />
            </label>

            <label className="field">
              <span>Issuing authority</span>
              <input
                name="issuingAuthority"
                value={form.issuingAuthority}
                onChange={(event) => onFormChange({ ...form, issuingAuthority: event.target.value })}
                placeholder="City, municipality, or issuing office"
                autoComplete="off"
              />
            </label>

            <label className="field">
              <span>Expiry date</span>
              <input
                name="expiryDate"
                type="date"
                value={form.expiryDate}
                onChange={(event) => onFormChange({ ...form, expiryDate: event.target.value })}
              />
            </label>

            <label className="field">
              <span>Masked ID reference</span>
              <input
                name="maskedIdReference"
                value={form.maskedIdReference}
                onChange={(event) => onFormChange({ ...form, maskedIdReference: event.target.value })}
                placeholder="SC-****-1234"
                autoComplete="off"
              />
              <small>Do not enter the full ID number. Use a masked reference with asterisks.</small>
            </label>

            <label className="checkbox-field">
              <input
                name="requesterAttestation"
                type="checkbox"
                checked={form.requesterAttestation}
                onChange={(event) => onFormChange({ ...form, requesterAttestation: event.target.checked })}
              />
              <span>I confirm these entitlement details are correct and require review.</span>
            </label>

            <label className="field">
              <span>Optional note for review</span>
              <textarea
                name="attestationNotes"
                value={form.attestationNotes}
                onChange={(event) => onFormChange({ ...form, attestationNotes: event.target.value })}
                maxLength={240}
                placeholder="Short safe note only. Do not enter a full ID number."
              />
            </label>
          </fieldset>

          <p className="statutory-copy">
            Evidence upload is not available in WebPay yet. If evidence is needed, the parking site operator will review
            the request using approved tools.
          </p>

          {state.isSubmitting && <p role="status">Submitting statutory discount request...</p>}
          {state.error && <div className="form-error" role="alert">{state.error}</div>}

          <div className="statutory-actions">
            <button type="button" className="primary-button" onClick={onSubmit} disabled={state.isSubmitting}>
              Submit for review
            </button>
            <button type="button" className="ghost-button" onClick={onCancel} disabled={state.isSubmitting}>
              Cancel
            </button>
          </div>
        </div>
      )}

      {decision && copy && (
        <div className={`statutory-status is-${copy.tone}`} aria-live="polite">
          <div>
            <h3>{copy.heading}</h3>
            <p>{copy.body}</p>
          </div>
          <dl>
            <div>
              <dt>Request reference</dt>
              <dd>{decision.requestReference}</dd>
            </div>
            <div>
              <dt>Decision status</dt>
              <dd>{displayValue(decision.decisionResultStatus ?? decision.decisionCommandStatus)}</dd>
            </div>
            <div>
              <dt>Payable-basis status</dt>
              <dd>{displayValue(decision.payableBasisReadinessStatus)}</dd>
            </div>
            <div>
              <dt>Support reference</dt>
              <dd>{displayValue(decision.correlationId)}</dd>
            </div>
          </dl>

          {decision.payableBasisReady && (
            <dl className="payable-breakdown">
              <div>
                <dt>Original Amount</dt>
                <dd>{formatCurrencyAmount(decision.originalAmountMinorUnits, decision.currency)}</dd>
              </div>
              <div>
                <dt>VAT-exclusive Amount</dt>
                <dd>{formatCurrencyAmount(decision.vatExclusiveBasisAmountMinorUnits, decision.currency)}</dd>
              </div>
              <div>
                <dt>VAT Amount</dt>
                <dd>{formatCurrencyAmount(decision.vatAmountMinorUnits, decision.currency)}</dd>
              </div>
              <div>
                <dt>VAT Treatment</dt>
                <dd>{displayValue(decision.vatTreatment)}</dd>
              </div>
              <div>
                <dt>Statutory Discount</dt>
                <dd>{formatAdjustment(decision.statutoryDiscountAmountMinorUnits, decision.currency)}</dd>
              </div>
              <div>
                <dt>Final Payable Amount</dt>
                <dd>{formatCurrencyAmount(decision.finalPayableAmountMinorUnits, decision.currency)}</dd>
              </div>
            </dl>
          )}

          {state.isPolling && <p role="status">Refreshing statutory discount status...</p>}
          {state.message && <p className="statutory-copy">{state.message}</p>}
          {state.error && <div className="form-error" role="alert">{state.error}</div>}
          {!state.isPolling && !isTerminalStatutoryDecision(decision) && (
            <button type="button" className="secondary-button" onClick={onRefresh}>
              Refresh status
            </button>
          )}
          <p className="statutory-copy">
            Payment is disabled while this statutory discount workflow is active in this pending-review slice.
          </p>
        </div>
      )}
    </section>
  );
}

function WebPayReturnPage({ mode }: { mode: ReturnPageMode }) {
  const [status, setStatus] = useState<"checking" | "loaded" | "error">("checking");
  const [summary, setSummary] = useState<ParkingSessionResolveResponse | null>(null);
  const [error, setError] = useState("");
  const [receiptStatus, setReceiptStatus] = useState<"idle" | "checking" | "available" | "pending" | "error">("idle");
  const [receipt, setReceipt] = useState<WebPayReceiptPresentationResponse | null>(null);
  const [receiptMessage, setReceiptMessage] = useState("");
  const [receiptCorrelationId, setReceiptCorrelationId] = useState("");
  const ticketReference = getQueryParam("ticketReference");
  const paymentAttemptId = getQueryParam("paymentAttemptId");
  const returnCorrelationId = getQueryParam("correlationId");
  const isCancelled = mode === "cancelled";
  const paymentStatusKind = classifyPaymentStatus(summary?.paymentStatus, isCancelled);
  const isPaid = paymentStatusKind === "confirmed";

  async function refreshStatus() {
    if (!ticketReference) {
      setStatus("error");
      setError("Ticket reference is missing.");
      return;
    }

    setStatus("checking");
    setError("");
    setReceipt(null);
    setReceiptStatus("idle");
    setReceiptMessage("");
    setReceiptCorrelationId("");

    try {
      const response = await retrievePaymentStatus({ ticketReference });
      setSummary(response);
      setStatus("loaded");
    } catch (apiError) {
      setSummary(null);
      setStatus("error");
      setError(apiError instanceof Error ? apiError.message : "Payment status is unavailable. Please try again.");
    }
  }

  useEffect(() => {
    void refreshStatus();
  }, [ticketReference]);

  useEffect(() => {
    if (!isPaid || !paymentAttemptId) {
      setReceipt(null);
      setReceiptStatus("idle");
      setReceiptMessage("");
      setReceiptCorrelationId("");
      return;
    }

    let isStale = false;

    async function loadReceipt() {
      setReceiptStatus("checking");
      setReceipt(null);
      setReceiptMessage("");
      setReceiptCorrelationId("");

      for (let attempt = 1; attempt <= 3; attempt += 1) {
        try {
          const response = await retrieveReceiptPresentation(paymentAttemptId!, returnCorrelationId ?? undefined);
          if (isStale) {
            return;
          }

          setReceipt(response);
          setReceiptStatus("available");
          setReceiptCorrelationId(response.correlationId);
          return;
        } catch (apiError) {
          if (isStale) {
            return;
          }

          if (apiError instanceof ReceiptPresentationError) {
            setReceiptStatus(apiError.retryable ? "pending" : "error");
            setReceiptMessage(apiError.message);
            setReceiptCorrelationId(apiError.correlationId ?? returnCorrelationId ?? "");

            if (!apiError.retryable || attempt === 3) {
              return;
            }
          } else {
            setReceiptStatus("error");
            setReceiptMessage("Sales Invoice retrieval is temporarily unavailable. Please try again shortly.");
            setReceiptCorrelationId(returnCorrelationId ?? "");
            return;
          }

          await new Promise((resolve) => window.setTimeout(resolve, 750));
        }
      }
    }

    void loadReceipt();

    return () => {
      isStale = true;
    };
  }, [isPaid, paymentAttemptId, returnCorrelationId]);

  return (
    <main className="app-shell">
      <header className="brand-header">
        <img className="exitpass-logo" src="/assets/logo/exitpass-logo.svg" alt="ExitPass" />
        <div className="operator-brand">
          <span>Operated with</span>
          <img src="/assets/logo/proparking-logo.png" alt="Pro Parking" />
        </div>
      </header>

      <section className="return-panel" aria-live="polite">
        <p className="eyebrow">{isCancelled ? "Payment cancelled" : "Payment return"}</p>
        <h1>{status === "checking" ? "Checking payment status" : "Payment status"}</h1>

        {status === "checking" && <p role="status">Checking payment status</p>}

        {status === "error" && (
          <div className="form-error" role="alert">
            <img src="/assets/icons/error.svg" alt="" aria-hidden="true" />
            <span>{error}</span>
          </div>
        )}

        {status === "loaded" && summary && (
          <>
            <PaymentStatusPanel summary={summary} statusKind={paymentStatusKind} />
            {isPaid && (
              <SalesInvoicePresentationPanel
                receipt={receipt}
                status={receiptStatus}
                message={receiptMessage}
                correlationId={receiptCorrelationId}
                onRefresh={() => {
                  if (paymentAttemptId) {
                    setReceiptStatus("checking");
                    void retrieveReceiptPresentation(paymentAttemptId, returnCorrelationId ?? undefined)
                      .then((response) => {
                        setReceipt(response);
                        setReceiptStatus("available");
                        setReceiptMessage("");
                        setReceiptCorrelationId(response.correlationId);
                      })
                      .catch((apiError) => {
                        if (apiError instanceof ReceiptPresentationError) {
                          setReceiptStatus(apiError.retryable ? "pending" : "error");
                          setReceiptMessage(apiError.message);
                          setReceiptCorrelationId(apiError.correlationId ?? returnCorrelationId ?? "");
                          return;
                        }

                        setReceiptStatus("error");
                        setReceiptMessage("Sales Invoice retrieval is temporarily unavailable. Please try again shortly.");
                        setReceiptCorrelationId(returnCorrelationId ?? "");
                      });
                  }
                }}
                canRefresh={Boolean(paymentAttemptId)}
              />
            )}
            <ParkingSessionSummaryPanel result={summary} />
          </>
        )}

        <div className="return-actions">
          <button type="button" className="primary-button status-button" onClick={() => void refreshStatus()}>
            Check Status
          </button>
          {(isCancelled || (status === "loaded" && !isPaid)) && ticketReference && (
            <a className="primary-link" href={`/?ticketReference=${encodeURIComponent(ticketReference)}`}>
              Retry Payment
            </a>
          )}
        </div>
      </section>
    </main>
  );
}

function PayableBasisPanel({ session }: { session: ParkingSessionResolveResponse }) {
  const originalAmount = session.originalAmountMinorUnits ?? session.totalFeeMinorUnits ?? session.amountMinorUnits;
  const couponAdjustment = session.couponAdjustmentMinorUnits ?? 0;
  const statutoryAdjustment = session.statutoryAdjustmentMinorUnits ?? 0;
  const totalAdjustment = session.totalAdjustmentMinorUnits ?? couponAdjustment + statutoryAdjustment;
  const statutoryStatus = getStatutoryDiscountDisplay(session);

  return (
    <section className="payable-basis-panel" aria-labelledby="payable-basis-heading">
      <div className="session-summary-header">
        <div>
          <p className="eyebrow">Payable basis</p>
          <h2 id="payable-basis-heading">Approved payable basis</h2>
        </div>
        <div className="amount-due">
          <span>Final Amount Due</span>
          <strong>{formatAmount(session.amountMinorUnits, session.currency)}</strong>
          <small>{session.currency}</small>
        </div>
      </div>

      <dl className="payable-breakdown">
        <div>
          <dt>Original Amount</dt>
          <dd>{formatCurrencyAmount(originalAmount, session.currency)}</dd>
        </div>
        <div>
          <dt>Coupon Adjustment</dt>
          <dd>{formatAdjustment(couponAdjustment, session.currency)}</dd>
        </div>
        <div>
          <dt>Statutory Adjustment</dt>
          <dd>{formatAdjustment(statutoryAdjustment, session.currency)}</dd>
        </div>
        <div>
          <dt>Total Adjustment</dt>
          <dd>{formatAdjustment(totalAdjustment, session.currency)}</dd>
        </div>
        <div>
          <dt>Final Amount Due</dt>
          <dd>{formatCurrencyAmount(session.amountMinorUnits, session.currency)}</dd>
        </div>
      </dl>

      <div className="modifier-grid">
        <div className="modifier-card" aria-labelledby="coupon-basis-heading">
          <h3 id="coupon-basis-heading">Coupon</h3>
          <p className="modifier-message">
            {couponAdjustment > 0
              ? `Applied adjustment: ${formatAdjustment(couponAdjustment, session.currency)}`
              : "No approved coupon adjustment found."}
          </p>
        </div>

        <div className="modifier-card" aria-labelledby="statutory-basis-heading">
          <h3 id="statutory-basis-heading">Statutory discount</h3>
          <p className={statutoryStatus.isBlocking ? "modifier-message is-warning" : "modifier-message"}>
            {statutoryStatus.label}
          </p>
          {statutoryStatus.isBlocking && (
            <p className="modifier-instruction">
              Please ask the parking site operator to validate your Senior Citizen or PWD discount before payment.
            </p>
          )}
        </div>
      </div>
    </section>
  );
}

function PaymentStatusPanel({
  summary,
  statusKind
}: {
  summary: ParkingSessionResolveResponse;
  statusKind: PaymentStatusKind;
}) {
  if (statusKind === "confirmed") {
    return (
      <>
        <div className="return-state is-paid">
          <img src="/assets/illustrations/payment-success.svg" alt="" aria-hidden="true" />
          <div>
            <h2>Payment confirmed</h2>
            <p>Payment is recorded for {displayValue(summary.ticketReference ?? summary.plateNumber)}.</p>
          </div>
        </div>
        <section className="payment-confirmation-panel" aria-labelledby="payment-confirmation-heading">
          <p className="eyebrow">Payment confirmation</p>
          <h2 id="payment-confirmation-heading">Payment recorded</h2>
          <dl>
            <div>
              <dt>Amount Paid</dt>
              <dd>{formatCurrencyAmount(summary.amountMinorUnits, summary.currency)}</dd>
            </div>
            <div>
              <dt>Payment Status</dt>
              <dd>{displayValue(summary.paymentStatus)}</dd>
            </div>
            <div>
              <dt>Ticket</dt>
              <dd>{displayValue(summary.ticketReference)}</dd>
            </div>
            <div>
              <dt>Plate</dt>
              <dd>{displayValue(summary.plateNumber)}</dd>
            </div>
            <div>
              <dt>Correlation ID</dt>
              <dd>{summary.correlationId}</dd>
            </div>
          </dl>
        </section>
        <ExitInstructionPanel summary={summary} />
      </>
    );
  }

  const copy = getPaymentStatusCopy(statusKind);
  return (
    <div className={`return-state is-${statusKind}`}>
      <img src={copy.image} alt="" aria-hidden="true" />
      <div>
        <h2>{copy.heading}</h2>
        <p>{copy.body}</p>
      </div>
    </div>
  );
}

function SalesInvoicePresentationPanel({
  receipt,
  status,
  message,
  correlationId,
  onRefresh,
  canRefresh
}: {
  receipt: WebPayReceiptPresentationResponse | null;
  status: "idle" | "checking" | "available" | "pending" | "error";
  message: string;
  correlationId: string;
  onRefresh: () => void;
  canRefresh: boolean;
}) {
  const presentation = receipt?.authoritativePresentation?.presentation ?? null;
  const sections = presentation?.sections?.filter((section) => (section.rows?.length ?? 0) > 0) ?? [];

  return (
    <section className="sales-invoice-panel" aria-labelledby="sales-invoice-heading">
      <p className="eyebrow">Sales Invoice</p>
      <h2 id="sales-invoice-heading">{presentation?.documentTitle ?? "Sales Invoice"}</h2>
      <p className="invoice-source">
        This presentation is retrieved from the POS Server-owned fiscal document record through Central PMS.
      </p>

      {status === "checking" && <p role="status">Retrieving Sales Invoice</p>}

      {status === "pending" && (
        <div className="invoice-status is-pending" role="status">
          <strong>Fiscal document recorded status is pending</strong>
          <span>{message || "The Sales Invoice is still being prepared."}</span>
          {correlationId && <small>Support reference: {correlationId}</small>}
        </div>
      )}

      {status === "error" && (
        <div className="invoice-status is-error" role="alert">
          <strong>Sales Invoice unavailable</strong>
          <span>{message || "Sales Invoice retrieval is temporarily unavailable. Please try again shortly."}</span>
          {correlationId && <small>Support reference: {correlationId}</small>}
        </div>
      )}

      {status === "available" && receipt && (
        <>
          <dl className="invoice-metadata">
            <div>
              <dt>Fiscal Document Number</dt>
              <dd>{displayValue(receipt.fiscalDocumentNumber)}</dd>
            </div>
            <div>
              <dt>Fiscal Document Status</dt>
              <dd>{displayValue(receipt.fiscalDocumentStatus)}</dd>
            </div>
            <div>
              <dt>Presentation Version</dt>
              <dd>{displayValue(receipt.presentationVersion)}</dd>
            </div>
            <div>
              <dt>Recorded At</dt>
              <dd>{formatDateTime(receipt.updatedAt)}</dd>
            </div>
          </dl>

          {sections.length > 0 ? (
            <div className="invoice-sections">
              {sections.map((section, index) => (
                <section className="invoice-section" key={section.key ?? section.name ?? section.title ?? index}>
                  <h3>{section.title ?? section.name ?? `Section ${index + 1}`}</h3>
                  <dl>
                    {section.rows?.map((row, rowIndex) => (
                      <div key={row.key ?? row.label ?? rowIndex}>
                        <dt>{row.label ?? row.key ?? `Line ${rowIndex + 1}`}</dt>
                        <dd>{displayValue(row.displayValue ?? row.value)}</dd>
                      </div>
                    ))}
                  </dl>
                </section>
              ))}
            </div>
          ) : (
            <p className="invoice-status">The Sales Invoice presentation was retrieved, but no display sections were supplied.</p>
          )}
        </>
      )}

      {canRefresh && status !== "checking" && (
        <button type="button" className="secondary-button" onClick={onRefresh}>
          Retrieve Sales Invoice
        </button>
      )}
    </section>
  );
}

function ExitInstructionPanel({ summary }: { summary: ParkingSessionResolveResponse }) {
  const instruction = summary.exitInstruction;
  const authorizationStatus = instruction?.status ?? summary.exitAuthorizationStatus;
  const exitBy = instruction?.exitBy ?? summary.exitBy ?? instruction?.expiresAt ?? summary.exitAuthorizationExpiresAt;
  const hasExitAuthorization = isExitAuthorizationAvailable(authorizationStatus);

  return (
    <section className={hasExitAuthorization ? "exit-instruction-panel is-ready" : "exit-instruction-panel"} aria-labelledby="exit-instruction-heading">
      <p className="eyebrow">Exit instruction</p>
      <h2 id="exit-instruction-heading">{hasExitAuthorization ? "Proceed to exit" : "Preparing exit authorization"}</h2>
      <p>
        {hasExitAuthorization
          ? instruction?.message ?? "Proceed to the exit lane and present your parking ticket or plate for validation."
          : "Payment is confirmed. Exit authorization is still being prepared; check status again shortly."}
      </p>
      {instruction?.laneName && <p className="exit-lane">Lane: {instruction.laneName}</p>}
      {exitBy && <p className="exit-lane">Exit by {formatDateTime(exitBy)}</p>}
    </section>
  );
}

function ParkingSessionSummaryPanel({ result }: { result: ParkingSessionResolveResponse }) {
  const summary: ParkingSessionSummary = {
    ...result.sessionSummary,
    siteGroupName: result.sessionSummary?.siteGroupName ?? result.siteGroupName,
    siteName: result.sessionSummary?.siteName ?? result.siteName,
    ticketReference: result.sessionSummary?.ticketReference ?? result.ticketReference,
    plateNumber: result.sessionSummary?.plateNumber ?? result.plateNumber,
    entryTime: result.sessionSummary?.entryTime ?? result.entryTime,
    exitTime: result.sessionSummary?.exitTime ?? result.exitTime,
    currentFeeCalculationTime: result.sessionSummary?.currentFeeCalculationTime ?? result.currentFeeCalculationTime,
    durationParked: result.sessionSummary?.durationParked ?? result.durationParked,
    tariffName: result.sessionSummary?.tariffName ?? result.tariffName,
    totalFeeMinorUnits: result.sessionSummary?.totalFeeMinorUnits ?? result.totalFeeMinorUnits ?? result.amountMinorUnits,
    amountMinorUnits: result.sessionSummary?.amountMinorUnits ?? result.amountMinorUnits,
    originalAmountMinorUnits: result.sessionSummary?.originalAmountMinorUnits ?? result.originalAmountMinorUnits ?? result.totalFeeMinorUnits ?? result.amountMinorUnits,
    couponAdjustmentMinorUnits: result.sessionSummary?.couponAdjustmentMinorUnits ?? result.couponAdjustmentMinorUnits,
    statutoryAdjustmentMinorUnits: result.sessionSummary?.statutoryAdjustmentMinorUnits ?? result.statutoryAdjustmentMinorUnits,
    totalAdjustmentMinorUnits: result.sessionSummary?.totalAdjustmentMinorUnits ?? result.totalAdjustmentMinorUnits,
    couponStatus: result.sessionSummary?.couponStatus ?? result.couponStatus,
    statutoryStatus: result.sessionSummary?.statutoryStatus ?? result.statutoryStatus,
    statutoryDiscountStatus: result.sessionSummary?.statutoryDiscountStatus ?? result.statutoryDiscountStatus,
    statutoryDiscountValidationStatus: result.sessionSummary?.statutoryDiscountValidationStatus ?? result.statutoryDiscountValidationStatus,
    currency: result.sessionSummary?.currency ?? result.currency,
    sessionStatus: result.sessionSummary?.sessionStatus ?? result.parkingStatus,
    parkingStatus: result.sessionSummary?.parkingStatus ?? result.parkingStatus,
    paymentStatus: result.sessionSummary?.paymentStatus ?? result.paymentStatus,
    feeValidUntil: result.sessionSummary?.feeValidUntil ?? result.feeValidUntil ?? result.tariffExpiresAt,
    tariffExpiresAt: result.sessionSummary?.tariffExpiresAt ?? result.tariffExpiresAt
  };
  const siteGroupName = getParkerFacingSiteGroupName(summary.siteGroupName);
  const siteName = getParkerFacingSiteName(summary.siteName);

  const rows = [
    ["Site Group", siteGroupName],
    ["Site Name", siteName],
    ["Ticket", displayValue(summary.ticketReference)],
    ["Plate", displayValue(summary.plateNumber)],
    ["Entry Time", displayValue(formatDateTime(summary.entryTime))],
    ["Duration", displayValue(summary.durationParked ?? formatDuration(summary.entryTime, summary.currentFeeCalculationTime))],
    ["Total Fee", displayValue(formatCurrencyAmount(summary.totalFeeMinorUnits ?? summary.amountMinorUnits, summary.currency ?? result.currency))],
    ["Discount/Coupon Adjustment", displayValue(formatAdjustment(summary.totalAdjustmentMinorUnits ?? (summary.couponAdjustmentMinorUnits ?? 0) + (summary.statutoryAdjustmentMinorUnits ?? 0), summary.currency ?? result.currency))],
    ["Amount Due", displayValue(formatCurrencyAmount(summary.amountMinorUnits ?? result.amountMinorUnits, summary.currency ?? result.currency))],
    ["Parking Status", displayValue(getParkerFacingParkingStatus(summary))],
    ["Payment Status", displayValue(summary.paymentStatus)],
    ["Fee Valid Until", displayValue(formatDateTime(summary.feeValidUntil ?? summary.tariffExpiresAt))]
  ];

  return (
    <section className="session-summary" aria-labelledby="session-summary-heading">
      <div className="session-summary-header">
        <div>
          <p className="eyebrow">Parking Session Summary</p>
          <h2 id="session-summary-heading">{siteName}</h2>
        </div>
        <div className="amount-due">
          <span>Amount Due</span>
          <strong>{formatAmount(summary.amountMinorUnits ?? result.amountMinorUnits, summary.currency ?? result.currency)}</strong>
          <small>{summary.currency ?? result.currency}</small>
        </div>
      </div>
      <dl>
        {rows.map(([label, value]) => (
          <div key={label}>
            <dt>{label}</dt>
            <dd>{value}</dd>
          </div>
        ))}
      </dl>
    </section>
  );
}

function toParkingSessionResolveResponse(result: PaymentIntentResponse): ParkingSessionResolveResponse {
  return {
    parkingSessionId: result.parkingSessionId,
    tariffSnapshotId: result.tariffSnapshotId,
    siteGroupId: result.siteGroupId,
    siteId: result.siteId,
    vendorSystemId: result.vendorSystemId,
    siteGroupName: result.siteGroupName,
    amountMinorUnits: result.amountMinorUnits,
    originalAmountMinorUnits: result.totalFeeMinorUnits ?? result.amountMinorUnits,
    couponAdjustmentMinorUnits: result.sessionSummary?.couponAdjustmentMinorUnits ?? result.couponAdjustmentMinorUnits ?? 0,
    statutoryAdjustmentMinorUnits: result.sessionSummary?.statutoryAdjustmentMinorUnits ?? result.statutoryAdjustmentMinorUnits ?? 0,
    totalAdjustmentMinorUnits: result.sessionSummary?.totalAdjustmentMinorUnits ?? result.totalAdjustmentMinorUnits ?? 0,
    couponStatus: result.sessionSummary?.couponStatus ?? result.couponStatus,
    statutoryStatus: result.sessionSummary?.statutoryStatus ?? result.statutoryStatus,
    statutoryDiscountStatus: result.sessionSummary?.statutoryDiscountStatus ?? result.statutoryDiscountStatus,
    statutoryDiscountValidationStatus: result.sessionSummary?.statutoryDiscountValidationStatus ?? result.statutoryDiscountValidationStatus,
    currency: result.currency,
    correlationId: result.correlationId,
    siteName: result.siteName,
    ticketReference: result.ticketReference,
    plateNumber: result.plateNumber,
    entryTime: result.entryTime,
    exitTime: result.exitTime,
    currentFeeCalculationTime: result.currentFeeCalculationTime,
    durationParked: result.durationParked,
    tariffName: result.tariffName,
    totalFeeMinorUnits: result.totalFeeMinorUnits,
    paymentStatus: result.paymentStatus ?? result.status,
    parkingStatus: result.parkingStatus,
    feeValidUntil: result.feeValidUntil,
    tariffExpiresAt: result.tariffExpiresAt,
    sessionSummary: result.sessionSummary
  };
}

function continueActivePayment(activePaymentAttempt: ActivePaymentAttemptState): boolean {
  const resumeUrl = getResumeUrl(activePaymentAttempt.handoff);
  if (resumeUrl) {
    window.location.assign(resumeUrl);
    return true;
  }

  return checkActivePaymentStatus(activePaymentAttempt);
}

function checkActivePaymentStatus(activePaymentAttempt: ActivePaymentAttemptState): boolean {
  const statusUrl = activePaymentAttempt.checkStatusUrl || activePaymentAttempt.statusUrl;
  if (statusUrl) {
    window.location.assign(statusUrl);
    return true;
  }

  return false;
}

function shouldPollStatutoryDecision(decision: WebPayStatutoryDiscountDecisionResponse): boolean {
  const readinessStatus = decision.payableBasisReadinessStatus.toUpperCase();
  const readinessAction = decision.payableBasisReadinessAction?.toUpperCase() ?? "";
  return !decision.payableBasisReady &&
    readinessAction === "POLL_READBACK" &&
    readinessStatus !== "DECISION_REJECTED" &&
    readinessStatus !== "TERMINAL_FAILURE";
}

function isTerminalStatutoryDecision(decision: WebPayStatutoryDiscountDecisionResponse): boolean {
  const readinessStatus = decision.payableBasisReadinessStatus.toUpperCase();
  const readinessAction = decision.payableBasisReadinessAction?.toUpperCase() ?? "";
  const decisionResult = decision.decisionResultStatus?.toUpperCase() ?? "";
  return decision.payableBasisReady ||
    readinessStatus === "DECISION_REJECTED" ||
    readinessAction === "DO_NOT_RETRY" ||
    decisionResult === "REJECTED" ||
    decision.overallResultClassification.toUpperCase().includes("TERMINAL");
}

function getStatutoryDiscountPaymentBlockMessage(decision: WebPayStatutoryDiscountDecisionResponse): string {
  if (decision.payableBasisReady) {
    return "The statutory discount payable basis is ready. Discounted payment is not available until the next WebPay integration step.";
  }

  return getStatutoryDiscountStatusCopy(decision).body;
}

function getStatutoryDiscountStatusCopy(decision: WebPayStatutoryDiscountDecisionResponse): { heading: string; body: string; tone: "pending" | "success" | "warning" | "error" } {
  const readinessStatus = decision.payableBasisReadinessStatus.toUpperCase();
  const readinessAction = decision.payableBasisReadinessAction?.toUpperCase() ?? "";
  const decisionStatus = decision.decisionResultStatus?.toUpperCase() ?? "";
  const applicationStatus = decision.applicationCommandStatus.toUpperCase();

  if (decision.payableBasisReady && decision.appliedTariffSnapshotId && decision.finalPayableAmountMinorUnits !== null && decision.finalPayableAmountMinorUnits !== undefined && decision.currency) {
    return {
      heading: "Statutory discount applied",
      body: "Central PMS returned the approved payable basis. Payment linkage will be completed in the next WebPay slice.",
      tone: "success"
    };
  }

  if (readinessStatus === "DECISION_REJECTED" || decisionStatus === "REJECTED") {
    return {
      heading: "Entitlement not approved",
      body: "The statutory discount request was not approved. No discounted payable amount is available.",
      tone: "error"
    };
  }

  if (readinessStatus === "DECISION_APPROVED_APPLICATION_NOT_REQUESTED" || readinessAction === "SUBMIT_APPLICATION_INTENT") {
    return {
      heading: "Entitlement approved",
      body: "Discount application is pending and payment is not ready yet.",
      tone: "warning"
    };
  }

  if (readinessStatus === "APPLICATION_PROCESSING" || applicationStatus === "PROCESSING") {
    return {
      heading: "Discount application processing",
      body: "The approved statutory discount is still being applied by Central PMS.",
      tone: "pending"
    };
  }

  if (readinessAction === "DO_NOT_RETRY" || decision.overallResultClassification.toUpperCase().includes("TERMINAL")) {
    return {
      heading: "Statutory discount unavailable",
      body: "Statutory discount processing could not be completed. Please ask for assistance.",
      tone: "error"
    };
  }

  if (decision.retryable || readinessAction.includes("RETRY")) {
    return {
      heading: "Status temporarily unavailable",
      body: "Statutory discount status is temporarily unavailable. Refresh status shortly.",
      tone: "warning"
    };
  }

  return {
    heading: "Awaiting review",
    body: "Your statutory discount request requires Operator Console review. Payment remains unavailable until review and payable-basis application are complete.",
    tone: "pending"
  };
}

function isStatutoryValidationPending(session?: ParkingSessionResolveResponse | null): boolean {
  return getNormalizedStatutoryStatus(session) === "PENDING";
}

function getStatutoryDiscountDisplay(session: ParkingSessionResolveResponse): { label: string; isBlocking: boolean } {
  switch (getNormalizedStatutoryStatus(session)) {
    case "PENDING":
      return { label: "Pending operator validation.", isBlocking: true };
    case "APPROVED":
      return { label: "Approved.", isBlocking: false };
    case "REJECTED":
      return { label: "Rejected.", isBlocking: false };
    case "EXPIRED":
      return { label: "Expired.", isBlocking: false };
    default:
      return { label: "No approved statutory discount found.", isBlocking: false };
  }
}

function firstNonBlank(...values: Array<string | null | undefined>): string | undefined {
  return values.find((value) => typeof value === "string" && value.trim().length > 0)?.trim();
}

function getNormalizedStatutoryStatus(session?: ParkingSessionResolveResponse | null): "NONE" | "PENDING" | "APPROVED" | "REJECTED" | "EXPIRED" {
  const status = firstNonBlank(
    session?.statutoryDiscountStatus,
    session?.statutoryDiscountValidationStatus,
    session?.statutoryStatus
  )?.toUpperCase();

  switch (status) {
    case "APPROVED":
    case "APPROVED_OPERATOR_VALIDATION":
      return "APPROVED";
    case "PENDING":
    case "PENDING_REVIEW":
    case "PENDING_OPERATOR_REVIEW":
    case "PENDING_OPERATOR_VALIDATION":
    case "REQUESTED":
      return "PENDING";
    case "REJECTED":
    case "DECLINED":
      return "REJECTED";
    case "EXPIRED":
      return "EXPIRED";
    default:
      return "NONE";
  }
}

function isPaidStatus(status?: string | null): boolean {
  if (!status) {
    return false;
  }

  const normalized = status.trim().toUpperCase();
  return normalized === "PAID" || normalized === "COMPLETED" || normalized === "CONFIRMED";
}

function classifyPaymentStatus(status?: string | null, checkoutWasCancelled = false): PaymentStatusKind {
  if (isPaidStatus(status)) {
    return "confirmed";
  }

  const normalized = status?.trim().toUpperCase() ?? "";
  if (normalized === "FAILED" || normalized === "DECLINED") {
    return "failed";
  }

  if (normalized === "EXPIRED") {
    return "expired";
  }

  if (checkoutWasCancelled || normalized === "CANCELLED" || normalized === "CANCELED") {
    return "cancelled";
  }

  return "pending";
}

function getPaymentStatusCopy(statusKind: PaymentStatusKind): { heading: string; body: string; image: string } {
  switch (statusKind) {
    case "failed":
      return {
        heading: "Payment failed",
        body: "We could not confirm this payment. You can retry payment or check status again.",
        image: "/assets/illustrations/payment-failed.svg"
      };
    case "expired":
      return {
        heading: "Payment expired",
        body: "This checkout session expired before payment was confirmed. Please retry payment.",
        image: "/assets/illustrations/payment-failed.svg"
      };
    case "cancelled":
      return {
        heading: "Payment was cancelled",
        body: "You can retry payment or check status again.",
        image: "/assets/illustrations/payment-failed.svg"
      };
    case "confirmed":
      return {
        heading: "Payment confirmed",
        body: "Payment has been confirmed by ExitPass.",
        image: "/assets/illustrations/payment-success.svg"
      };
    case "pending":
    default:
      return {
        heading: "Payment is still being verified",
        body: "Payment is pending server-side confirmation. Check status again in a moment.",
        image: "/assets/illustrations/payment-pending.svg"
      };
  }
}

function isExitAuthorizationAvailable(status?: string | null): boolean {
  if (!status) {
    return false;
  }

  const normalized = status.trim().toUpperCase();
  return normalized === "ISSUED" || normalized === "ACTIVE" || normalized === "AUTHORIZED";
}

function getReturnPageMode(pathname: string): ReturnPageMode | null {
  const normalized = pathname.replace(/\/+$/, "").toLowerCase();
  if (normalized === "/webpay/payment-return") {
    return "success";
  }

  if (normalized === "/webpay/payment-cancelled") {
    return "cancelled";
  }

  return null;
}

function getQueryParam(name: string): string {
  return new URLSearchParams(window.location.search).get(name)?.trim() ?? "";
}

function validateLookupInput(entryMode: EntryMode, lookupValue: string): string {
  const normalized = lookupValue.trim();
  const allowedLookupPattern = /^[A-Za-z0-9][A-Za-z0-9 -]{2,63}$/;
  if (!allowedLookupPattern.test(normalized)) {
    return entryMode === "ticket"
      ? "Enter a valid ticket reference."
      : "Enter a valid plate number.";
  }

  return "";
}

function getParkerFacingParkingStatus(summary: ParkingSessionSummary): string | undefined {
  if (isPaidStatus(summary.paymentStatus)) {
    return "Payment Completed";
  }

  return summary.parkingStatus ?? summary.sessionStatus ?? undefined;
}

function formatDateTime(value?: string | null): string | undefined {
  if (!value) {
    return undefined;
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return new Intl.DateTimeFormat("en-PH", {
    year: "numeric",
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit"
  }).format(date);
}

function formatDuration(entryTime?: string | null, calculationTime?: string | null): string | undefined {
  if (!entryTime || !calculationTime) {
    return undefined;
  }

  const entry = new Date(entryTime);
  const calculation = new Date(calculationTime);
  if (Number.isNaN(entry.getTime()) || Number.isNaN(calculation.getTime())) {
    return undefined;
  }

  const totalMinutes = Math.max(0, Math.floor((calculation.getTime() - entry.getTime()) / 60000));
  const hours = Math.floor(totalMinutes / 60);
  const minutes = totalMinutes % 60;

  if (hours > 0 && minutes > 0) {
    return `${hours}h ${minutes}m`;
  }

  if (hours > 0) {
    return `${hours}h`;
  }

  return `${minutes}m`;
}

function formatCurrencyAmount(amountMinorUnits?: number | null, currency?: string | null): string | undefined {
  if (amountMinorUnits === null || amountMinorUnits === undefined) {
    return undefined;
  }

  const normalizedCurrency = currency?.trim() || "PHP";
  return `${normalizedCurrency.toUpperCase()} ${formatAmount(amountMinorUnits, normalizedCurrency)}`;
}

function formatAdjustment(amountMinorUnits?: number | null, currency?: string | null): string | undefined {
  const amount = amountMinorUnits ?? 0;
  if (amount <= 0) {
    return formatCurrencyAmount(0, currency);
  }

  return `-${formatCurrencyAmount(amount, currency)}`;
}

function displayValue(value?: string | number | null): string {
  if (typeof value === "number") {
    return Number.isFinite(value) ? value.toString() : "Not available";
  }

  return value?.trim() || "Not available";
}

function getParkerFacingSiteName(value?: string | null): string {
  return getParkerFacingDisplayName(value, "Parking Site");
}

function getParkerFacingSiteGroupName(value?: string | null): string {
  return getParkerFacingDisplayName(value, "Parking Group");
}

function getParkerFacingDisplayName(value: string | null | undefined, fallback: string): string {
  const normalized = value?.trim();
  if (!normalized || isFallbackLookingDisplayName(normalized)) {
    return fallback;
  }

  return normalized;
}

function isFallbackLookingDisplayName(value: string): boolean {
  const normalized = value.trim();
  const uuidPattern = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;
  const uuidWithoutDashesPattern = /^[0-9a-fA-F]{32}$/;

  if (uuidPattern.test(normalized) || uuidWithoutDashesPattern.test(normalized)) {
    return true;
  }

  const lowered = normalized.toLowerCase();
  for (const prefix of ["site ", "site group "]) {
    if (lowered.startsWith(prefix) && isFallbackLookingDisplayName(normalized.slice(prefix.length))) {
      return true;
    }
  }

  return false;
}
