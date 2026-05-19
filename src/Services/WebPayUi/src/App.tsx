import { FormEvent, KeyboardEvent, useState } from "react";
import { QrScanner } from "./QrScanner";
import {
  ActivePaymentAttemptError,
  createPaymentIntent,
  extractPaymentIntentContext,
  formatAmount,
  getResumeUrl,
  normalizeTicketReference,
  resolveParkingSession
} from "./webpay";
import type {
  ActivePaymentAttemptState,
  ParkingSessionResolveResponse,
  ParkingSessionSummary,
  PaymentIntentRequest,
  PaymentIntentResponse,
  PaymentMethod
} from "./types";

const paymentMethods: Array<{ code: PaymentMethod; label: string; image: string }> = [
  { code: "QRPH", label: "QRPh", image: "/assets/payment-methods/qrph.png" },
  { code: "CARD", label: "Card", image: "/assets/payment-methods/cards-visa-mastercard.png" },
  { code: "GCASH", label: "GCash", image: "/assets/payment-methods/gcash.png" },
  { code: "MAYA", label: "Maya", image: "/assets/payment-methods/maya.png" }
];

type EntryMode = "ticket" | "plate";
type WebPayStage = "INPUT" | "SESSION_RESOLVED" | "HANDOFF_READY" | "ACTIVE_ATTEMPT" | "ERROR";

export function App() {
  const [entryMode, setEntryMode] = useState<EntryMode>("ticket");
  const [ticketReference, setTicketReference] = useState("");
  const [scannedContext, setScannedContext] = useState<Partial<PaymentIntentRequest>>({});
  const [plateNumber, setPlateNumber] = useState("");
  const [paymentMethod, setPaymentMethod] = useState<PaymentMethod>("QRPH");
  const [resolvedSession, setResolvedSession] = useState<ParkingSessionResolveResponse | null>(null);
  const [result, setResult] = useState<PaymentIntentResponse | null>(null);
  const [activePaymentAttempt, setActivePaymentAttempt] = useState<ActivePaymentAttemptState | null>(null);
  const [error, setError] = useState("");
  const [resolveError, setResolveError] = useState("");
  const [isResolving, setIsResolving] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [stage, setStage] = useState<WebPayStage>("INPUT");

  function handleQrDecoded(value: string) {
    const normalized = normalizeTicketReference(value);
    const context = extractPaymentIntentContext(value);
    setEntryMode("ticket");
    setTicketReference(normalized);
    setScannedContext(context);
    setError("");
    setResolveError("");
    setResult(null);
    setResolvedSession(null);
    setActivePaymentAttempt(null);
    setStage("INPUT");
  }

  function clearLookupState() {
    setError("");
    setResolveError("");
    setResult(null);
    setResolvedSession(null);
    setActivePaymentAttempt(null);
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
        vendorSystemId: resolvedSession.vendorSystemId ?? scannedContext.vendorSystemId
      });
      setResult(response);
      setStage("HANDOFF_READY");
    } catch (apiError) {
      if (apiError instanceof ActivePaymentAttemptError) {
        setActivePaymentAttempt(apiError.activePaymentAttempt);
        setStage("ACTIVE_ATTEMPT");
      } else {
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
    if (activePaymentAttempt) {
      if (!continueActivePayment(activePaymentAttempt)) {
        setError("Payment is already in progress. Please wait a moment before checking again.");
      }
      return;
    }

    if (isResolving || isSubmitting) {
      return;
    }

    if (stage !== "SESSION_RESOLVED" || !resolvedSession) {
      await handleResolveParkingSession();
      return;
    }

    await handleCreatePaymentIntent();
  }

  const handoff = result?.handoff;
  const activeResumeUrl = getResumeUrl(activePaymentAttempt?.handoff);
  const summary = resolvedSession ?? (result ? toParkingSessionResolveResponse(result) : null);

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

        <section className="method-section" aria-labelledby="payment-method-heading">
          <h2 id="payment-method-heading">Payment method</h2>
          <div className="method-grid">
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

        <button type="submit" className="submit-button" disabled={isSubmitting || isResolving}>
          <img src="/assets/icons/payment.svg" alt="" aria-hidden="true" />
          {isResolving
            ? "Resolving..."
            : isSubmitting
              ? "Creating payment..."
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
                <dd>{paymentMethods.find((method) => method.code === result.paymentMethod)?.label ?? result.paymentMethod}</dd>
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
    amountMinorUnits: result.sessionSummary?.amountMinorUnits ?? result.amountMinorUnits,
    currency: result.sessionSummary?.currency ?? result.currency,
    sessionStatus: result.sessionSummary?.sessionStatus ?? result.parkingStatus,
    feeValidUntil: result.sessionSummary?.feeValidUntil ?? result.feeValidUntil ?? result.tariffExpiresAt,
    tariffExpiresAt: result.sessionSummary?.tariffExpiresAt ?? result.tariffExpiresAt
  };

  const rows = [
    ["Property / Site Group", summary.siteGroupName],
    ["Site", summary.siteName],
    ["Ticket Number", summary.ticketReference],
    ["Plate Number", summary.plateNumber],
    ["Entry Time", formatDateTime(summary.entryTime)],
    ["Fee Valid Until", formatDateTime(summary.feeValidUntil ?? summary.tariffExpiresAt)],
    ["Parking Status", summary.sessionStatus]
  ].filter((row): row is [string, string] => Boolean(row[1]));

  return (
    <section className="session-summary" aria-labelledby="session-summary-heading">
      <div className="session-summary-header">
        <div>
          <p className="eyebrow">Parking Session Summary</p>
          <h2 id="session-summary-heading">{summary.siteName || "Parking Session Summary"}</h2>
        </div>
        <div className="amount-due">
          <span>Amount Due</span>
          <strong>{formatAmount(summary.amountMinorUnits ?? result.amountMinorUnits, summary.currency ?? result.currency)}</strong>
          <small>{summary.currency ?? result.currency}</small>
        </div>
      </div>
      {rows.length > 0 && (
        <dl>
          {rows.map(([label, value]) => (
            <div key={label}>
              <dt>{label}</dt>
              <dd>{value}</dd>
            </div>
          ))}
        </dl>
      )}
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
    paymentStatus: result.status,
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
