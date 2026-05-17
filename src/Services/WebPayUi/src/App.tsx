import { FormEvent, useState } from "react";
import { QrScanner } from "./QrScanner";
import { createPaymentIntent, extractPaymentIntentContext, formatAmount, normalizeTicketReference } from "./webpay";
import type { PaymentIntentRequest, PaymentIntentResponse, PaymentMethod } from "./types";

const paymentMethods: Array<{ code: PaymentMethod; label: string; image: string }> = [
  { code: "QRPH", label: "QRPh", image: "/assets/payment-methods/qrph.png" },
  { code: "CARD", label: "Card", image: "/assets/payment-methods/cards-visa-mastercard.png" },
  { code: "GCASH", label: "GCash", image: "/assets/payment-methods/gcash.png" },
  { code: "MAYA", label: "Maya", image: "/assets/payment-methods/maya.png" }
];

type EntryMode = "ticket" | "plate";

export function App() {
  const [entryMode, setEntryMode] = useState<EntryMode>("ticket");
  const [ticketReference, setTicketReference] = useState("");
  const [scannedContext, setScannedContext] = useState<Partial<PaymentIntentRequest>>({});
  const [plateNumber, setPlateNumber] = useState("");
  const [paymentMethod, setPaymentMethod] = useState<PaymentMethod>("QRPH");
  const [result, setResult] = useState<PaymentIntentResponse | null>(null);
  const [error, setError] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);

  function handleQrDecoded(value: string) {
    const normalized = normalizeTicketReference(value);
    const context = extractPaymentIntentContext(value);
    setEntryMode("ticket");
    setTicketReference(normalized);
    setScannedContext(context);
    setError("");
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError("");
    setResult(null);

    const hasTicket = entryMode === "ticket" && ticketReference.trim().length > 0;
    const hasPlate = entryMode === "plate" && plateNumber.trim().length > 0;
    if (!hasTicket && !hasPlate) {
      setError(entryMode === "ticket" ? "Enter or scan a ticket reference." : "Enter a plate number.");
      return;
    }

    setIsSubmitting(true);
    try {
      const response = await createPaymentIntent({
        ticketReference: hasTicket ? ticketReference : undefined,
        plateNumber: hasPlate ? plateNumber : undefined,
        paymentMethod,
        ...(hasTicket ? scannedContext : {})
      });
      setResult(response);
    } catch (apiError) {
      setError(apiError instanceof Error ? apiError.message : "Payment intent creation failed. Please try again.");
    } finally {
      setIsSubmitting(false);
    }
  }

  const handoff = result?.handoff;

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
              }}
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
              onChange={(event) => setPlateNumber(event.target.value)}
              placeholder="ABC 1234"
              autoCapitalize="characters"
              autoComplete="off"
            />
          </label>
        )}

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
                  onChange={() => setPaymentMethod(method.code)}
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

        <button type="submit" className="submit-button" disabled={isSubmitting}>
          <img src="/assets/icons/payment.svg" alt="" aria-hidden="true" />
          {isSubmitting ? "Creating payment..." : "Continue"}
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
            <h2>{formatAmount(result.amountMinorUnits, result.currency)}</h2>
            <dl>
              <div>
                <dt>Method</dt>
                <dd>{paymentMethods.find((method) => method.code === result.paymentMethod)?.label ?? result.paymentMethod}</dd>
              </div>
              <div>
                <dt>Status</dt>
                <dd>{result.status}</dd>
              </div>
              <div>
                <dt>Currency</dt>
                <dd>{result.currency}</dd>
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
