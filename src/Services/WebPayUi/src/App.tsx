import { FormEvent, KeyboardEvent, RefObject, useEffect, useLayoutEffect, useRef, useState } from "react";
import type { ReactNode } from "react";
import QRCode from "qrcode";
import { QrScanner } from "./QrScanner";
import { StatutoryEvidenceCapture } from "./StatutoryEvidenceCapture";
import { AutomaticMaskedIdInput } from "./AutomaticMaskedIdInput";
import { formatCustomerSupportReference } from "./customerSafeReference";
import {
  ActivePaymentAttemptError,
  PayableBasisRefreshRequiredError,
  ReceiptPresentationError,
  applyStatutoryDiscountPayableBasis,
  createPaymentIntent,
  createRequestReference,
  createStatutoryApplicationIdempotencyKey,
  createStatutoryDecisionIdempotencyKey,
  extractPaymentIntentContext,
  formatAmount,
  getResumeUrl,
  normalizeTicketReference,
  rediscoverStatutoryDiscountPendingLifecycle,
  retrieveReceiptPresentation,
  retrievePaymentStatus,
  retrieveStatutoryDiscountAvailability,
  retrieveStatutoryDiscountDecision,
  resolveParkingSession,
  submitStatutoryDiscountDecision
} from "./webpay";
import {
  clearStatutoryRecoveryRecord,
  createStatutoryRecoveryRecord,
  hasKnownInFlightStatutoryRecoveryStage,
  loadStatutoryRecoveryRecord,
  saveStatutoryRecoveryRecord,
  subscribeStatutoryRecoveryRecord,
  updateStatutoryRecoveryRecord
} from "./statutoryRecovery";
import type {
  ActivePaymentAttemptState,
  ParkingSessionResolveResponse,
  ParkingSessionSummary,
  PaymentIntentRequest,
  PaymentIntentResponse,
  PaymentMethod,
  SalesInvoicePresentationRow,
  StatutoryDiscountEntitlementType,
  WebPayStatutoryDiscountAvailabilityResponse,
  WebPayStatutoryDiscountPendingLifecycleRediscoveryResponse,
  WebPayReceiptPresentationResponse,
  WebPayStatutoryDiscountDecisionResponse
} from "./types";
import type { StatutoryRecoveryStage, WebPayStatutoryRecoveryRecord } from "./statutoryRecovery";

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
  applicationIdempotencyKey: string;
  correlationId: string;
  isSubmitting: boolean;
  isApplying: boolean;
  isPolling: boolean;
  message: string;
  error: string;
};

type StatutoryDiscountAvailabilityUiState = {
  availability: WebPayStatutoryDiscountAvailabilityResponse | null;
  isLoading: boolean;
  error: string;
};

type RegularPaymentConfirmationState = {
  isOpen: boolean;
  amountMinorUnits: number | null;
  currency: string;
  tariffSnapshotId: string;
  isRevalidating: boolean;
  requiresRenewedConfirmation: boolean;
  message: string;
};

type AppliedStatutoryPaymentBasis = {
  tariffSnapshotId: string;
  amountMinorUnits: number;
  currency: string;
  statutoryDiscountDecisionCommandId: string;
  statutoryDiscountPayableBasisApplicationCommandId: string;
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
  applicationIdempotencyKey: "",
  correlationId: "",
  isSubmitting: false,
  isApplying: false,
  isPolling: false,
  message: "",
  error: ""
};

const emptyRegularPaymentConfirmation: RegularPaymentConfirmationState = {
  isOpen: false,
  amountMinorUnits: null,
  currency: "",
  tariffSnapshotId: "",
  isRevalidating: false,
  requiresRenewedConfirmation: false,
  message: ""
};

const emptyStatutoryDiscountAvailabilityState: StatutoryDiscountAvailabilityUiState = {
  availability: null,
  isLoading: false,
  error: ""
};

export function App() {
  const initialTicketReference = getQueryParam("ticketReference");
  const resetStatutoryRecoveryForLocalValidation = shouldResetStatutoryRecoveryForLocalValidation();
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
  const [statutoryAvailabilityState, setStatutoryAvailabilityState] =
    useState<StatutoryDiscountAvailabilityUiState>(emptyStatutoryDiscountAvailabilityState);
  const [statutoryPendingLifecycle, setStatutoryPendingLifecycle] =
    useState<WebPayStatutoryDiscountPendingLifecycleRediscoveryResponse | null>(null);
  const [regularPaymentConfirmation, setRegularPaymentConfirmation] =
    useState<RegularPaymentConfirmationState>(emptyRegularPaymentConfirmation);
  const [initialRecoveryLoad] = useState(() => {
    if (resetStatutoryRecoveryForLocalValidation) {
      clearStatutoryRecoveryRecord();
      return { record: null, cleared: false, unavailable: false, reason: "LOCAL_VALIDATION_RESET" };
    }

    return loadStatutoryRecoveryRecord();
  });
  const [statutoryRecoveryRecord, setStatutoryRecoveryRecord] = useState<WebPayStatutoryRecoveryRecord | null>(initialRecoveryLoad.record);
  const statutoryRecoveryRecordRef = useRef<WebPayStatutoryRecoveryRecord | null>(initialRecoveryLoad.record);
  const [statutoryRecoveryMessage, setStatutoryRecoveryMessage] = useState(() => getInitialRecoveryMessage(initialRecoveryLoad));
  const [isRestoringStatutoryRecovery, setIsRestoringStatutoryRecovery] = useState(false);
  const [statutoryRecoveryRefreshNonce, setStatutoryRecoveryRefreshNonce] = useState(0);
  const statutoryPollGeneration = useRef(0);
  const statutoryApplicationInFlight = useRef(false);
  const paymentIntentInFlight = useRef(false);
  const skipNextStatutoryRecoveryRestore = useRef(false);
  const regularPaymentButtonRef = useRef<HTMLButtonElement | null>(null);
  const regularPaymentConfirmButtonRef = useRef<HTMLButtonElement | null>(null);

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
    setStatutoryAvailabilityState(emptyStatutoryDiscountAvailabilityState);
    setStatutoryPendingLifecycle(null);
    setRegularPaymentConfirmation(emptyRegularPaymentConfirmation);
    clearStatutoryRecovery();
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
    setStatutoryAvailabilityState(emptyStatutoryDiscountAvailabilityState);
    setStatutoryPendingLifecycle(null);
    setRegularPaymentConfirmation(emptyRegularPaymentConfirmation);
    setStage("INPUT");
  }

  function saveStatutoryRecovery(next: WebPayStatutoryRecoveryRecord) {
    const result = saveStatutoryRecoveryRecord(next);
    skipNextStatutoryRecoveryRestore.current = true;
    statutoryRecoveryRecordRef.current = result.record;
    setStatutoryRecoveryRecord(result.record);
    if (result.unavailable) {
      setStatutoryRecoveryMessage("Durable statutory discount recovery is unavailable in this browser. This page remains safe, but refresh recovery may not work.");
    } else if (hasKnownInFlightStatutoryRecoveryStage(result.record) || result.record.stage === "PAYMENT_HANDOFF") {
      setStatutoryRecoveryMessage(getCrossTabRecoveryMessage(result.record));
    }

    return result.record;
  }

  function updateCurrentStatutoryRecovery(
    patch: Partial<Omit<WebPayStatutoryRecoveryRecord, "schemaVersion" | "createdAt" | "expiresAt">>,
    fallback?: Pick<WebPayStatutoryRecoveryRecord, "parkingSessionId" | "entitlementType" | "stage"> &
      Partial<Omit<WebPayStatutoryRecoveryRecord, "schemaVersion" | "parkingSessionId" | "entitlementType" | "stage" | "createdAt" | "updatedAt" | "expiresAt">>
  ) {
    const currentRecoveryRecord = statutoryRecoveryRecordRef.current;
    if (!currentRecoveryRecord && !fallback) {
      return null;
    }

    const next = currentRecoveryRecord
      ? updateStatutoryRecoveryRecord(currentRecoveryRecord, patch)
      : createStatutoryRecoveryRecord({
          ...fallback!,
          ...patch,
          parkingSessionId: fallback!.parkingSessionId,
          entitlementType: fallback!.entitlementType,
          stage: patch.stage ?? fallback!.stage
        });

    return saveStatutoryRecovery(next);
  }

  function updateRecoveryFromDecision(
    decision: WebPayStatutoryDiscountDecisionResponse,
    fallback?: Pick<WebPayStatutoryRecoveryRecord, "parkingSessionId" | "entitlementType" | "stage"> &
      Partial<Omit<WebPayStatutoryRecoveryRecord, "schemaVersion" | "parkingSessionId" | "entitlementType" | "stage" | "createdAt" | "updatedAt" | "expiresAt">>
  ) {
    const recoveryStage = getStatutoryRecoveryStageAfterDecisionRead(
      decision,
      statutoryRecoveryRecordRef.current
    );
    return updateCurrentStatutoryRecovery(
      {
        parkingSessionId: decision.parkingSessionId,
        entitlementType: normalizeEntitlementType(decision.entitlementType, fallback?.entitlementType),
        statutoryDiscountDecisionCommandId: decision.statutoryDiscountDecisionCommandId,
        statutoryDiscountPayableBasisApplicationCommandId: decision.statutoryDiscountPayableBasisApplicationCommandId ?? undefined,
        requestReference: decision.requestReference,
        correlationId: decision.correlationId,
        stage: recoveryStage
      },
      fallback
        ? {
            ...fallback,
            parkingSessionId: decision.parkingSessionId,
            entitlementType: normalizeEntitlementType(decision.entitlementType, fallback.entitlementType),
            stage: recoveryStage
          }
        : undefined
    );
  }

  function clearStatutoryRecovery() {
    clearStatutoryRecoveryRecord();
    statutoryRecoveryRecordRef.current = null;
    setStatutoryRecoveryRecord(null);
    setStatutoryRecoveryMessage("");
  }

  function currentLookup() {
    const hasTicket = entryMode === "ticket" && ticketReference.trim().length > 0;
    const hasPlate = entryMode === "plate" && plateNumber.trim().length > 0;
    const lookupValue = entryMode === "ticket" ? ticketReference.trim() : plateNumber.trim();

    return { hasTicket, hasPlate, lookupValue };
  }

  function buildParkingSessionResolveRequest() {
    const { hasTicket, hasPlate, lookupValue } = currentLookup();
    if (!hasTicket && !hasPlate) {
      throw new Error(entryMode === "ticket" ? "Enter or scan a ticket reference." : "Enter a plate number.");
    }

    const inputError = validateLookupInput(entryMode, lookupValue);
    if (inputError) {
      throw new Error(inputError);
    }

    return {
      ticketReference: hasTicket ? lookupValue : undefined,
      plateNumber: hasPlate ? lookupValue : undefined,
      ...(hasTicket ? scannedContext : {})
    };
  }

  async function fetchCurrentParkingSession() {
    return resolveParkingSession(buildParkingSessionResolveRequest());
  }

  async function handleResolveParkingSession() {
    setError("");
    setResolveError("");
    setResult(null);
    setActivePaymentAttempt(null);
    setIsResolving(true);

    try {
      const response = await fetchCurrentParkingSession();
      setError("");
      setResult(null);
      setActivePaymentAttempt(null);
      setResolvedSession(response);
      setStage("SESSION_RESOLVED");
      void refreshStatutoryAvailability(response);
      void rediscoverStatutoryPendingLifecycle(response);
    } catch (apiError) {
      setResolvedSession(null);
      setStatutoryAvailabilityState(emptyStatutoryDiscountAvailabilityState);
      setStatutoryPendingLifecycle(null);
      setResolveError(apiError instanceof Error ? apiError.message : "Parking lookup failed. Please try again.");
      setStage("ERROR");
    } finally {
      setIsResolving(false);
    }
  }

  async function refreshStatutoryAvailability(session?: ParkingSessionResolveResponse | null) {
    const activeSession = session ?? resolvedSession;
    if (!activeSession?.parkingSessionId) {
      return;
    }

    setStatutoryAvailabilityState((current) => ({ ...current, isLoading: true, error: "" }));
    try {
      const availability = await retrieveStatutoryDiscountAvailability(activeSession);
      setStatutoryAvailabilityState({
        availability,
        isLoading: false,
        error: ""
      });
      const coveredEntitlements = getCoveredStatutoryEntitlementTypes(availability);
      if (coveredEntitlements.length > 0 && !coveredEntitlements.includes(statutoryDiscountForm.entitlementType)) {
        setStatutoryDiscountForm((current) => ({ ...current, entitlementType: coveredEntitlements[0] }));
      }
    } catch (apiError) {
      setStatutoryAvailabilityState({
        availability: null,
        isLoading: false,
        error: apiError instanceof Error ? apiError.message : "Parking privilege availability is temporarily unavailable."
      });
    }
  }

  async function rediscoverStatutoryPendingLifecycle(session: ParkingSessionResolveResponse) {
    if (!session.parkingSessionId || !session.siteId || !session.siteGroupId) {
      return;
    }

    const correlationId = createRequestReference();
    try {
      const rediscovery = await rediscoverStatutoryDiscountPendingLifecycle(
        {
          lookupMode: "PARKING_SESSION_ID",
          parkingSessionId: session.parkingSessionId,
          siteId: session.siteId,
          siteGroupId: session.siteGroupId,
          vendorSystemId: session.vendorSystemId ?? undefined
        },
        correlationId
      );
      setStatutoryPendingLifecycle(rediscovery);

      const classification = rediscovery.classification.toUpperCase();
      if (classification === "FOUND") {
        if (!rediscovery.statutoryDecisionCommandId?.trim()) {
          setStatutoryRecoveryMessage("An existing statutory discount request was found, but its server readback reference was incomplete. Please refresh status shortly.");
          return;
        }

        const decision = await retrieveStatutoryDiscountDecision(
          rediscovery.statutoryDecisionCommandId,
          rediscovery.correlationId || correlationId
        );

        if (decision.parkingSessionId !== session.parkingSessionId) {
          setStatutoryRecoveryMessage("An existing statutory discount request did not match this parking session. It was not restored.");
          return;
        }

        setShowStatutoryDiscountForm(false);
        setStatutoryDiscountForm((current) => ({
          ...current,
          entitlementType: normalizeEntitlementType(decision.entitlementType, current.entitlementType)
        }));
        setStatutoryDiscountState((current) => ({
          ...current,
          decision,
          requestReference: current.requestReference || rediscovery.requestReference || decision.requestReference,
          idempotencyKey: current.idempotencyKey,
          applicationIdempotencyKey:
            current.applicationIdempotencyKey ||
            (decision.statutoryDiscountPayableBasisApplicationCommandId
              ? createStatutoryApplicationIdempotencyKey(decision.statutoryDiscountDecisionCommandId)
              : ""),
          correlationId: rediscovery.correlationId || decision.correlationId,
          isSubmitting: false,
          isApplying: false,
          isPolling: shouldPollStatutoryDecision(decision),
          message: getStatutoryDiscountStatusCopy(decision).body,
          error: ""
        }));
        updateRecoveryFromDecision(decision, {
          parkingSessionId: decision.parkingSessionId,
          entitlementType: normalizeEntitlementType(decision.entitlementType, "SENIOR_CITIZEN"),
          requestReference: rediscovery.requestReference ?? decision.requestReference,
          correlationId: rediscovery.correlationId || decision.correlationId,
          stage: getStatutoryRecoveryStageFromDecision(decision)
        });
        setStatutoryRecoveryMessage(
          rediscovery.opaqueContinuationUrl
            ? "Existing statutory discount request restored from Central PMS with its continuation link."
            : "Existing statutory discount request restored from Central PMS."
        );
        return;
      }

      if (classification === "NOT_FOUND" || classification === "NO_ACTIVE_LIFECYCLE") {
        return;
      }

      setStatutoryRecoveryMessage(getStatutoryPendingLifecycleRediscoveryMessage(rediscovery));
    } catch (apiError) {
      setStatutoryPendingLifecycle(null);
      setStatutoryRecoveryMessage(
        apiError instanceof Error
          ? `Existing statutory discount request could not be checked: ${apiError.message}`
          : "Existing statutory discount request could not be checked right now."
      );
    }
  }

  async function handleCreatePaymentIntent(options?: { forceRegularAmount?: boolean; sessionOverride?: ParkingSessionResolveResponse }) {
    const paymentSession = options?.sessionOverride ?? resolvedSession;
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
    paymentIntentInFlight.current = true;
    let paymentIntentCorrelationId: string | undefined;

    try {
      const appliedStatutoryBasis = options?.forceRegularAmount ? null : getAppliedStatutoryPaymentBasis(statutoryDiscountState.decision);
      paymentIntentCorrelationId =
        statutoryRecoveryRecord?.paymentIntentCorrelationId ||
        (appliedStatutoryBasis ? createRequestReference() : undefined);
      if (appliedStatutoryBasis && paymentIntentCorrelationId) {
        updateCurrentStatutoryRecovery(
          {
            paymentIntentCorrelationId,
            stage: "PAYMENT_SUBMITTING"
          },
          {
            parkingSessionId: resolvedSession.parkingSessionId,
            entitlementType: normalizeEntitlementType(statutoryDiscountState.decision?.entitlementType, "SENIOR_CITIZEN"),
            statutoryDiscountDecisionCommandId: appliedStatutoryBasis.statutoryDiscountDecisionCommandId,
            statutoryDiscountPayableBasisApplicationCommandId: appliedStatutoryBasis.statutoryDiscountPayableBasisApplicationCommandId,
            paymentIntentCorrelationId,
            requestReference: statutoryDiscountState.requestReference,
            correlationId: statutoryDiscountState.correlationId,
            stage: "PAYMENT_SUBMITTING"
          }
        );
      }

      const response = await createPaymentIntent({
        ticketReference: hasTicket ? (paymentSession?.ticketReference ?? ticketReference.trim()) : undefined,
        plateNumber: hasPlate ? (paymentSession?.plateNumber ?? plateNumber.trim()) : undefined,
        paymentMethod,
        siteGroupId: paymentSession?.siteGroupId ?? scannedContext.siteGroupId,
        siteId: paymentSession?.siteId ?? scannedContext.siteId,
        vendorSystemId: paymentSession?.vendorSystemId ?? scannedContext.vendorSystemId,
        tariffSnapshotId: appliedStatutoryBasis?.tariffSnapshotId ?? paymentSession?.tariffSnapshotId,
        expectedAmountMinorUnits: appliedStatutoryBasis?.amountMinorUnits ?? paymentSession?.amountMinorUnits,
        expectedCurrency: appliedStatutoryBasis?.currency ?? paymentSession?.currency,
        statutoryDiscountDecisionCommandId: appliedStatutoryBasis?.statutoryDiscountDecisionCommandId,
        statutoryDiscountPayableBasisApplicationCommandId: appliedStatutoryBasis?.statutoryDiscountPayableBasisApplicationCommandId,
        correlationId: paymentIntentCorrelationId
      }, fetch, {});
      setResult(response);
      setResolvedSession(toParkingSessionResolveResponse(response));
      setStage("HANDOFF_READY");
      if (appliedStatutoryBasis) {
        updateCurrentStatutoryRecovery({
          paymentAttemptId: response.paymentAttemptId,
          paymentIntentCorrelationId,
          correlationId: response.correlationId,
          stage: "PAYMENT_HANDOFF"
        });
      }
    } catch (apiError) {
      if (apiError instanceof ActivePaymentAttemptError) {
        setActivePaymentAttempt(apiError.activePaymentAttempt);
        setStage("ACTIVE_ATTEMPT");
        if (apiError.activePaymentAttempt.paymentAttemptId) {
          updateCurrentStatutoryRecovery({
            paymentAttemptId: apiError.activePaymentAttempt.paymentAttemptId,
            paymentIntentCorrelationId,
            correlationId: apiError.activePaymentAttempt.correlationId ?? statutoryRecoveryRecord?.correlationId,
            stage: "PAYMENT_HANDOFF"
          });
        }
      } else {
        setPayableBasisRefreshRequired(apiError instanceof PayableBasisRefreshRequiredError);
        setError(apiError instanceof Error ? apiError.message : "Payment intent creation failed. Please try again.");
        setStage("ERROR");
        if (statutoryDiscountState.decision && getAppliedStatutoryPaymentBasis(statutoryDiscountState.decision)) {
          updateCurrentStatutoryRecovery({ stage: "PAYABLE_READY" });
        }
      }
    } finally {
      setIsSubmitting(false);
      paymentIntentInFlight.current = false;
    }
  }

  function openRegularPaymentConfirmation() {
    if (!resolvedSession || paymentIntentInFlight.current || isSubmitting || isResolving) {
      return;
    }

    setError("");
    setRegularPaymentConfirmation({
      isOpen: true,
      amountMinorUnits: resolvedSession.amountMinorUnits,
      currency: resolvedSession.currency,
      tariffSnapshotId: resolvedSession.tariffSnapshotId,
      isRevalidating: false,
      requiresRenewedConfirmation: false,
      message: ""
    });
  }

  function closeRegularPaymentConfirmation() {
    setRegularPaymentConfirmation(emptyRegularPaymentConfirmation);
    window.setTimeout(() => regularPaymentButtonRef.current?.focus(), 0);
  }

  async function confirmRegularPayment() {
    if (!resolvedSession || regularPaymentConfirmation.isRevalidating) {
      return;
    }

    setRegularPaymentConfirmation((current) => ({ ...current, isRevalidating: true, message: "" }));

    try {
      const currentDecisionId = statutoryDiscountState.decision?.statutoryDiscountDecisionCommandId;
      if (currentDecisionId) {
        const decision = await retrieveStatutoryDiscountDecision(currentDecisionId, statutoryDiscountState.correlationId || undefined);
        setStatutoryDiscountState((current) => ({
          ...current,
          decision,
          isPolling: shouldPollStatutoryDecision(decision),
          message: getStatutoryDiscountStatusCopy(decision).body,
          error: ""
        }));
        updateRecoveryFromDecision(decision);

        if (canSubmitApplicationIntent(decision) || getAppliedStatutoryPaymentBasis(decision)) {
          setRegularPaymentConfirmation({
            ...emptyRegularPaymentConfirmation,
            isOpen: true,
            amountMinorUnits: resolvedSession.amountMinorUnits,
            currency: resolvedSession.currency,
            tariffSnapshotId: resolvedSession.tariffSnapshotId,
            message: "The statutory request status changed before payment. Review the updated status before choosing how to continue."
          });
          return;
        }

        if (!isPendingReviewStatutoryDecision(decision) && !isRejectedStatutoryDecision(decision)) {
          setRegularPaymentConfirmation(emptyRegularPaymentConfirmation);
          return;
        }
      }

      const latestSession = await fetchCurrentParkingSession();
      setResolvedSession(latestSession);
      setStage("SESSION_RESOLVED");

      const amountChanged =
        latestSession.amountMinorUnits !== regularPaymentConfirmation.amountMinorUnits ||
        latestSession.currency.toUpperCase() !== regularPaymentConfirmation.currency.toUpperCase() ||
        latestSession.tariffSnapshotId !== regularPaymentConfirmation.tariffSnapshotId;

      if (amountChanged && !regularPaymentConfirmation.requiresRenewedConfirmation) {
        setRegularPaymentConfirmation({
          isOpen: true,
          amountMinorUnits: latestSession.amountMinorUnits,
          currency: latestSession.currency,
          tariffSnapshotId: latestSession.tariffSnapshotId,
          isRevalidating: false,
          requiresRenewedConfirmation: true,
          message: "The regular parking amount changed before payment. Review the updated amount and confirm again to continue."
        });
        return;
      }

      setRegularPaymentConfirmation(emptyRegularPaymentConfirmation);
      await handleCreatePaymentIntent({ forceRegularAmount: true, sessionOverride: latestSession });
    } catch (apiError) {
      setRegularPaymentConfirmation((current) => ({
        ...current,
        isRevalidating: false,
        message: apiError instanceof Error ? apiError.message : "Regular payment could not be revalidated. Please try again."
      }));
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

    if (isResolving || isSubmitting || paymentIntentInFlight.current) {
      return;
    }

    if (isStatutoryValidationPending(resolvedSession)) {
      setError("Statutory discount validation is pending review.");
      return;
    }

    if (stage !== "SESSION_RESOLVED" || !resolvedSession) {
      await handleResolveParkingSession();
      return;
    }

    if (statutoryDiscountState.decision && !getAppliedStatutoryPaymentBasis(statutoryDiscountState.decision)) {
      setError(getStatutoryDiscountPaymentBlockMessage(statutoryDiscountState.decision));
      return;
    }

    const activeRecoveryRecord = statutoryRecoveryRecord;
    if (hasKnownInFlightStatutoryRecoveryStage(activeRecoveryRecord) && activeRecoveryRecord) {
      setError(getCrossTabRecoveryMessage(activeRecoveryRecord));
      return;
    }

    await handleCreatePaymentIntent();
  }

  async function handleRecalculateFee() {
    setPayableBasisRefreshRequired(false);
    await handleResolveParkingSession();
  }

  function buildCurrentStatutoryDiscountRequest(requestReference: string) {
    if (!resolvedSession) {
      throw new Error("Resolve your parking session before requesting a statutory discount.");
    }

    return {
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
      evidenceCaptureRequested: statutoryAvailabilityRequiresEvidence(statutoryAvailabilityState.availability),
      requesterAttestation: statutoryDiscountForm.requesterAttestation,
      attestationNotes: statutoryDiscountForm.attestationNotes || null,
      originalTariffSnapshotId: resolvedSession.tariffSnapshotId
    };
  }

  async function handleSubmitStatutoryDiscount() {
    if (!resolvedSession || statutoryDiscountState.isSubmitting) {
      return;
    }

    const coveredEntitlements = getCoveredStatutoryEntitlementTypes(statutoryAvailabilityState.availability);
    if (!coveredEntitlements.includes(statutoryDiscountForm.entitlementType)) {
      setStatutoryDiscountState((current) => ({
        ...current,
        error: "Parking privilege requests are not available for this parking session. You may continue with the regular parking amount.",
        message: ""
      }));
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
      applicationIdempotencyKey: "",
      correlationId,
      isSubmitting: true,
      error: "",
      message: "Submitting statutory discount request..."
    }));
    const submittingRecord = createStatutoryRecoveryRecord({
      parkingSessionId: resolvedSession.parkingSessionId,
      entitlementType: statutoryDiscountForm.entitlementType,
      decisionIdempotencyKey: idempotencyKey,
      requestReference,
      correlationId,
      stage: "DECISION_SUBMITTING"
    });
    saveStatutoryRecovery(submittingRecord);
    setError("");

    try {
      const decision = await submitStatutoryDiscountDecision(
        buildCurrentStatutoryDiscountRequest(requestReference),
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
      updateRecoveryFromDecision(decision, {
        ...submittingRecord,
        parkingSessionId: decision.parkingSessionId,
        entitlementType: normalizeEntitlementType(decision.entitlementType, statutoryDiscountForm.entitlementType),
        stage: getStatutoryRecoveryStageFromDecision(decision)
      });
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

  async function handleApplyStatutoryDiscount() {
    const decision = statutoryDiscountState.decision;
    if (
      !resolvedSession ||
      !decision ||
      statutoryApplicationInFlight.current ||
      statutoryDiscountState.isApplying ||
      statutoryDiscountState.isSubmitting
    ) {
      return;
    }

    if (!canSubmitApplicationIntent(decision) && !canRetryApplicationIntent(decision)) {
      return;
    }

    const requestReference = statutoryDiscountState.requestReference || decision.requestReference || createRequestReference();
    const applicationIdempotencyKey =
      statutoryDiscountState.applicationIdempotencyKey ||
      createStatutoryApplicationIdempotencyKey(decision.statutoryDiscountDecisionCommandId);
    const correlationId = statutoryDiscountState.correlationId || decision.correlationId || createRequestReference();

    setStatutoryDiscountState((current) => ({
      ...current,
      requestReference,
      applicationIdempotencyKey,
      correlationId,
      isApplying: true,
      error: "",
      message: "Applying approved statutory discount..."
    }));
    setError("");
    statutoryApplicationInFlight.current = true;
    updateCurrentStatutoryRecovery(
      {
        applicationIdempotencyKey,
        requestReference,
        correlationId,
        stage: "APPLICATION_SUBMITTING"
      },
      {
        parkingSessionId: decision.parkingSessionId,
        entitlementType: normalizeEntitlementType(decision.entitlementType, "SENIOR_CITIZEN"),
        statutoryDiscountDecisionCommandId: decision.statutoryDiscountDecisionCommandId,
        applicationIdempotencyKey,
        requestReference,
        correlationId,
        stage: "APPLICATION_SUBMITTING"
      }
    );

    try {
      const appliedDecision = await applyStatutoryDiscountPayableBasis(
        decision.statutoryDiscountDecisionCommandId,
        buildCurrentStatutoryDiscountRequest(requestReference),
        applicationIdempotencyKey,
        correlationId
      );

      setStatutoryDiscountState((current) => ({
        ...current,
        decision: appliedDecision,
        isApplying: false,
        isPolling: shouldPollStatutoryDecision(appliedDecision),
        message: getStatutoryDiscountStatusCopy(appliedDecision).body,
        error: ""
      }));
      updateRecoveryFromDecision(appliedDecision, {
        parkingSessionId: appliedDecision.parkingSessionId,
        entitlementType: normalizeEntitlementType(appliedDecision.entitlementType, normalizeEntitlementType(decision.entitlementType, "SENIOR_CITIZEN")),
        statutoryDiscountDecisionCommandId: appliedDecision.statutoryDiscountDecisionCommandId,
        applicationIdempotencyKey,
        requestReference,
        correlationId,
        stage: getStatutoryRecoveryStageFromDecision(appliedDecision)
      });
    } catch (apiError) {
      setStatutoryDiscountState((current) => ({
        ...current,
        isApplying: false,
        isPolling: false,
        error: apiError instanceof Error ? apiError.message : "Approved statutory discount could not be applied.",
        message: ""
      }));
      updateCurrentStatutoryRecovery({ stage: getStatutoryRecoveryStageFromDecision(decision), applicationIdempotencyKey });
    } finally {
      statutoryApplicationInFlight.current = false;
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
      updateRecoveryFromDecision(decision);
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
          updateRecoveryFromDecision(readback);

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

  useLayoutEffect(() => {
    return subscribeStatutoryRecoveryRecord((record) => {
      statutoryRecoveryRecordRef.current = record;
      setStatutoryRecoveryRecord(record);
      if (!record) {
        setStatutoryRecoveryMessage("Browser recovery metadata was cleared in another tab. Any Central PMS statutory request remains authoritative.");
        return;
      }

      setStatutoryRecoveryMessage(getCrossTabRecoveryMessage(record));
      if (record.statutoryDiscountDecisionCommandId) {
        setStatutoryRecoveryRefreshNonce((current) => current + 1);
      }
    });
  }, []);

  useEffect(() => {
    const record = statutoryRecoveryRecord;
    if (!record) {
      return;
    }

    if (skipNextStatutoryRecoveryRestore.current) {
      skipNextStatutoryRecoveryRestore.current = false;
      return;
    }

    if (hasKnownInFlightStatutoryRecoveryStage(record)) {
      setStatutoryRecoveryMessage(getCrossTabRecoveryMessage(record));
      setIsRestoringStatutoryRecovery(false);
      return;
    }

    if (!record.statutoryDiscountDecisionCommandId) {
      setStatutoryRecoveryMessage(
        "A previous statutory discount submission did not return a server reference. This browser preserved the original request key for the current page, but restart recovery requires a returned request reference."
      );
      return;
    }

    if (resolvedSession && resolvedSession.parkingSessionId !== record.parkingSessionId) {
      clearStatutoryRecoveryRecord();
      statutoryRecoveryRecordRef.current = null;
      setStatutoryRecoveryRecord(null);
      setStatutoryRecoveryMessage("A saved statutory discount recovery record did not match this parking session and was cleared.");
      return;
    }

    const recoveryRecord = record;
    const controller = new AbortController();
    let cancelled = false;
    setIsRestoringStatutoryRecovery(true);
    setStatutoryRecoveryMessage("Restoring an existing statutory discount request from Central PMS...");

    async function restore() {
      try {
        const decision = await retrieveStatutoryDiscountDecision(
          recoveryRecord.statutoryDiscountDecisionCommandId!,
          recoveryRecord.correlationId,
          fetch,
          controller.signal
        );

        if (cancelled) {
          return;
        }

        setStatutoryDiscountState((current) => ({
          ...current,
          decision,
          requestReference: current.requestReference || recoveryRecord.requestReference || decision.requestReference,
          idempotencyKey: current.idempotencyKey || recoveryRecord.decisionIdempotencyKey || "",
          applicationIdempotencyKey:
            current.applicationIdempotencyKey ||
            recoveryRecord.applicationIdempotencyKey ||
            (decision.statutoryDiscountPayableBasisApplicationCommandId
              ? createStatutoryApplicationIdempotencyKey(decision.statutoryDiscountDecisionCommandId)
              : ""),
          correlationId: current.correlationId || recoveryRecord.correlationId || decision.correlationId,
          isSubmitting: false,
          isApplying: false,
          isPolling: shouldPollStatutoryDecision(decision),
          message: getStatutoryDiscountStatusCopy(decision).body,
          error: ""
        }));
        updateRecoveryFromDecision(decision, {
          ...recoveryRecord,
          parkingSessionId: decision.parkingSessionId,
          entitlementType: normalizeEntitlementType(decision.entitlementType, recoveryRecord.entitlementType),
          stage: getStatutoryRecoveryStageFromDecision(decision)
        });
        setStatutoryRecoveryMessage(
          resolvedSession
            ? "Existing statutory discount request restored from authoritative Central PMS readback."
            : "Existing statutory discount request restored. Resolve the parking session again to continue payment."
        );
      } catch (apiError) {
        if (cancelled) {
          return;
        }

        setStatutoryRecoveryMessage(
          apiError instanceof Error
            ? `Saved statutory discount recovery could not be refreshed: ${apiError.message}`
            : "Saved statutory discount recovery could not be refreshed."
        );
      } finally {
        if (!cancelled) {
          setIsRestoringStatutoryRecovery(false);
        }
      }
    }

    void restore();

    return () => {
      cancelled = true;
      controller.abort();
    };
  }, [
    statutoryRecoveryRecord?.statutoryDiscountDecisionCommandId,
    statutoryRecoveryRefreshNonce,
    resolvedSession?.parkingSessionId
  ]);

  useEffect(() => {
    if (regularPaymentConfirmation.isOpen) {
      window.setTimeout(() => regularPaymentConfirmButtonRef.current?.focus(), 0);
    }
  }, [regularPaymentConfirmation.isOpen]);

  const handoff = result?.handoff;
  const activeResumeUrl = getResumeUrl(activePaymentAttempt?.handoff);
  const summary = resolvedSession ?? (result ? toParkingSessionResolveResponse(result) : null);
  const isPaymentComplete = isPaidStatus(summary?.paymentStatus);
  const isPayablePending = isStatutoryValidationPending(summary);
  const isActiveStatutoryWorkflow = Boolean(statutoryDiscountState.decision);
  const appliedStatutoryBasis = getAppliedStatutoryPaymentBasis(statutoryDiscountState.decision);
  const statutoryRecoveryMutationInFlight = hasKnownInFlightStatutoryRecoveryStage(statutoryRecoveryRecord);
  const canPayRegularWhilePending =
    stage === "SESSION_RESOLVED" &&
    Boolean(resolvedSession) &&
    !isPaymentComplete &&
    !appliedStatutoryBasis &&
    !statutoryRecoveryMutationInFlight &&
    (isPayablePending || Boolean(statutoryDiscountState.decision && isPendingReviewStatutoryDecision(statutoryDiscountState.decision)));
  const statutoryDiscountPaymentBlocked =
    stage === "SESSION_RESOLVED" &&
    (isPayablePending || (isActiveStatutoryWorkflow && !appliedStatutoryBasis));
  const statutoryRecoveryPaymentBlocked = stage === "SESSION_RESOLVED" && statutoryRecoveryMutationInFlight;
  const coveredStatutoryEntitlements = getCoveredStatutoryEntitlementTypes(statutoryAvailabilityState.availability);
  const canStartStatutoryDiscountRequest = coveredStatutoryEntitlements.length > 0 || Boolean(statutoryDiscountState.decision);

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

      {statutoryRecoveryMessage && (
        <section className="statutory-recovery-panel" aria-live="polite" aria-labelledby="statutory-recovery-heading">
          <div>
            <p className="eyebrow">Statutory discount recovery</p>
            <h2 id="statutory-recovery-heading">
              {isRestoringStatutoryRecovery ? "Restoring request" : "Saved request status"}
            </h2>
            <p>{statutoryRecoveryMessage}</p>
          </div>
          <button
            type="button"
            className="ghost-button"
            onClick={() => {
              clearStatutoryRecovery();
              setStatutoryDiscountState(emptyStatutoryDiscountUiState);
              setStatutoryRecoveryMessage("Browser recovery metadata was cleared. This does not cancel any Central PMS statutory discount request.");
            }}
          >
            Clear browser recovery
          </button>
        </section>
      )}

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
            availabilityState={statutoryAvailabilityState}
            pendingLifecycle={statutoryPendingLifecycle}
            coveredEntitlements={coveredStatutoryEntitlements}
            canStartRequest={canStartStatutoryDiscountRequest}
            showForm={showStatutoryDiscountForm}
            onShowForm={() => {
              setShowStatutoryDiscountForm(true);
              setStatutoryDiscountState(emptyStatutoryDiscountUiState);
              setStatutoryPendingLifecycle(null);
              clearStatutoryRecoveryRecord();
              statutoryRecoveryRecordRef.current = null;
              setStatutoryRecoveryRecord(null);
              setStatutoryRecoveryMessage("");
              setError("");
            }}
            onCancel={() => {
              setShowStatutoryDiscountForm(false);
              setStatutoryDiscountForm(defaultStatutoryDiscountForm);
              setStatutoryDiscountState(emptyStatutoryDiscountUiState);
              setStatutoryPendingLifecycle(null);
              clearStatutoryRecovery();
            }}
            onFormChange={setStatutoryDiscountForm}
            onSubmit={() => void handleSubmitStatutoryDiscount()}
            onApply={() => void handleApplyStatutoryDiscount()}
            onRefresh={() => void handleRefreshStatutoryDecision()}
            onRefreshAvailability={() => void refreshStatutoryAvailability(summary)}
            onPayRegular={() => openRegularPaymentConfirmation()}
            regularPaymentButtonRef={regularPaymentButtonRef}
            canPayRegularWhilePending={canPayRegularWhilePending}
            showSupportReference={!activePaymentAttempt && !result}
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
            <CustomerSupportReference value={activePaymentAttempt.correlationId} />
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

        <button type="submit" className="submit-button" disabled={isSubmitting || isResolving || isPaymentComplete || statutoryDiscountPaymentBlocked || statutoryRecoveryPaymentBlocked}>
          <img src="/assets/icons/payment.svg" alt="" aria-hidden="true" />
          {isResolving
            ? "Resolving..."
            : isSubmitting
              ? "Creating payment..."
              : stage === "SESSION_RESOLVED" && isActiveStatutoryWorkflow && !appliedStatutoryBasis
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

      {regularPaymentConfirmation.isOpen && resolvedSession && (
        <RegularPaymentConfirmationDialog
          state={regularPaymentConfirmation}
          amountLabel={displayValue(formatCurrencyAmount(regularPaymentConfirmation.amountMinorUnits, regularPaymentConfirmation.currency || resolvedSession.currency))}
          confirmButtonRef={regularPaymentConfirmButtonRef}
          onCancel={closeRegularPaymentConfirmation}
          onConfirm={() => void confirmRegularPayment()}
        />
      )}

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
            <CustomerSupportReference value={result.correlationId} />
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
  availabilityState,
  pendingLifecycle,
  coveredEntitlements,
  canStartRequest,
  showForm,
  onShowForm,
  onCancel,
  onFormChange,
  onSubmit,
  onApply,
  onRefresh,
  onRefreshAvailability,
  onPayRegular,
  regularPaymentButtonRef,
  canPayRegularWhilePending,
  showSupportReference
}: {
  session: ParkingSessionResolveResponse;
  form: StatutoryDiscountFormState;
  state: StatutoryDiscountUiState;
  availabilityState: StatutoryDiscountAvailabilityUiState;
  pendingLifecycle: WebPayStatutoryDiscountPendingLifecycleRediscoveryResponse | null;
  coveredEntitlements: StatutoryDiscountEntitlementType[];
  canStartRequest: boolean;
  showForm: boolean;
  onShowForm: () => void;
  onCancel: () => void;
  onFormChange: (form: StatutoryDiscountFormState) => void;
  onSubmit: () => void;
  onApply: () => void;
  onRefresh: () => void;
  onRefreshAvailability: () => void;
  onPayRegular: () => void;
  regularPaymentButtonRef: RefObject<HTMLButtonElement | null>;
  canPayRegularWhilePending: boolean;
  showSupportReference: boolean;
}) {
  const decision = state.decision;
  const copy = decision ? getStatutoryDiscountStatusCopy(decision) : null;
  const showApplyAction = Boolean(decision && canSubmitApplicationIntent(decision));
  const showApplicationRetryAction = Boolean(decision && canRetryApplicationIntent(decision));
  const availabilityCopy = getStatutoryAvailabilityCopy(availabilityState);

  return (
    <section className="statutory-discount-panel" aria-labelledby="statutory-discount-heading">
      <div className="session-summary-header">
        <div>
          <p className="eyebrow">Statutory discount</p>
          <h2 id="statutory-discount-heading">Senior Citizen or PWD request</h2>
        </div>
        {!decision && !showForm && canStartRequest && (
          <button type="button" className="secondary-button" onClick={onShowForm}>
            Request statutory discount
          </button>
        )}
      </div>

      {!decision && availabilityCopy && (
        <div className={`statutory-status is-${availabilityCopy.tone}`} aria-live="polite">
          <div>
            <h3>{availabilityCopy.heading}</h3>
            <p>{availabilityCopy.body}</p>
          </div>
          {showSupportReference && <CustomerSupportReference value={availabilityState.availability?.correlationId} />}
          {(availabilityState.isLoading || availabilityState.error || availabilityState.availability?.retryable) && (
            <button type="button" className="secondary-button" onClick={onRefreshAvailability} disabled={availabilityState.isLoading}>
              {availabilityState.isLoading ? "Checking availability..." : "Check availability"}
            </button>
          )}
        </div>
      )}

      {!decision && !showForm && (
        <p className="statutory-copy">
          {canStartRequest
            ? "Submit safe entitlement details for review. Payment remains unavailable until review and payable-basis application are complete; WebPay does not approve the entitlement."
            : "Regular parking payment remains available."}
        </p>
      )}

      {showForm && canStartRequest && (
        <div className="statutory-form">
          <fieldset disabled={state.isSubmitting || state.isApplying}>
            <legend>Entitlement details for review</legend>

            <label className="field">
              <span>Entitlement type</span>
              <select
                value={form.entitlementType}
                onChange={(event) => onFormChange({ ...form, entitlementType: event.target.value as StatutoryDiscountEntitlementType })}
              >
                {coveredEntitlements.includes("SENIOR_CITIZEN") && <option value="SENIOR_CITIZEN">Senior Citizen</option>}
                {coveredEntitlements.includes("PWD") && <option value="PWD">PWD</option>}
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

            <AutomaticMaskedIdInput
              value={form.maskedIdReference}
              disabled={state.isSubmitting || state.isApplying}
              onChange={(maskedIdReference) => onFormChange({ ...form, maskedIdReference })}
            />

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

          {statutoryAvailabilityRequiresEvidence(availabilityState.availability) && (
            <p className="statutory-copy">
              A photo is required after this request is created. The photo does not approve or apply the statutory privilege.
            </p>
          )}

          {state.isSubmitting && <p role="status">Submitting statutory discount request...</p>}
          {state.error && <div className="form-error" role="alert">{state.error}</div>}

          <div className="statutory-actions">
            <button type="button" className="primary-button" onClick={onSubmit} disabled={state.isSubmitting}>
              Submit for review
            </button>
            <button type="button" className="ghost-button" onClick={onCancel} disabled={state.isSubmitting || state.isApplying}>
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
              <dt>Decision status</dt>
              <dd>{displayValue(decision.decisionResultStatus ?? decision.decisionCommandStatus)}</dd>
            </div>
            <div>
              <dt>Payable-basis status</dt>
              <dd>{displayValue(decision.payableBasisReadinessStatus)}</dd>
            </div>
          </dl>
          {showSupportReference && <CustomerSupportReference value={decision.correlationId} />}

          {getAppliedStatutoryPaymentBasis(decision) && (
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
          {state.isApplying && <p role="status">Applying approved statutory discount...</p>}
          {state.message && <p className="statutory-copy">{state.message}</p>}
          {pendingLifecycle?.classification === "FOUND" && pendingLifecycle.opaqueContinuationUrl && (
            <p className="statutory-copy">
              You may close this page and return using{" "}
              <a href={pendingLifecycle.opaqueContinuationUrl}>this continuation link</a>.
            </p>
          )}
          {state.error && <div className="form-error" role="alert">{state.error}</div>}
          <StatutoryEvidenceCapture
            statutoryDiscountDecisionCommandId={decision.statutoryDiscountDecisionCommandId}
          />
          <div className="statutory-actions">
            {showApplyAction && (
              <button type="button" className="primary-button" onClick={onApply} disabled={state.isApplying || state.isPolling}>
                {state.isApplying ? "Applying approved discount..." : "Apply approved discount"}
              </button>
            )}
            {showApplicationRetryAction && (
              <button type="button" className="primary-button" onClick={onApply} disabled={state.isApplying || state.isPolling}>
                {state.isApplying ? "Retrying discount application..." : "Retry discount application"}
              </button>
            )}
            {!state.isPolling && !state.isApplying && !isTerminalStatutoryDecision(decision) && (
              <button type="button" className="secondary-button" onClick={onRefresh}>
                Refresh status
              </button>
            )}
            {canPayRegularWhilePending && isPendingReviewStatutoryDecision(decision) && (
              <button
                type="button"
                className="ghost-button"
                onClick={onPayRegular}
                ref={regularPaymentButtonRef}
                disabled={state.isPolling || state.isApplying}
              >
                Pay regular amount
              </button>
            )}
          </div>
          <p className="statutory-copy">
            {getStatutoryDiscountPaymentAvailabilityCopy(decision)}
          </p>
        </div>
      )}
    </section>
  );
}

function RegularPaymentConfirmationDialog({
  state,
  amountLabel,
  confirmButtonRef,
  onCancel,
  onConfirm
}: {
  state: RegularPaymentConfirmationState;
  amountLabel: string;
  confirmButtonRef: RefObject<HTMLButtonElement | null>;
  onCancel: () => void;
  onConfirm: () => void;
}) {
  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === "Escape" && !state.isRevalidating) {
      event.preventDefault();
      onCancel();
    }
  }

  return (
    <div className="dialog-backdrop" role="presentation" onKeyDown={handleKeyDown}>
      <section
        className="regular-payment-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="regular-payment-heading"
        aria-describedby="regular-payment-description"
      >
        <p className="eyebrow">Regular payment confirmation</p>
        <h2 id="regular-payment-heading">Proceed without the parking privilege?</h2>
        <div id="regular-payment-description" className="dialog-copy">
          <p>
            The statutory parking privilege has not been applied. If you continue now, this payment will use the current
            regular parking amount: <strong>{amountLabel}</strong>.
          </p>
          <p>Approval after payment will not automatically refund or retroactively adjust this transaction.</p>
          <p>You may keep waiting and check the review status again.</p>
        </div>
        {state.message && (
          <div className={state.requiresRenewedConfirmation ? "inline-status" : "form-error"} role={state.requiresRenewedConfirmation ? "status" : "alert"}>
            {state.message}
          </div>
        )}
        <div className="dialog-actions">
          <button
            type="button"
            className="primary-button"
            onClick={onConfirm}
            ref={confirmButtonRef}
            disabled={state.isRevalidating}
          >
            {state.isRevalidating ? "Checking regular amount..." : "Continue with regular payment"}
          </button>
          <button type="button" className="ghost-button" onClick={onCancel} disabled={state.isRevalidating}>
            Keep waiting
          </button>
        </div>
      </section>
    </div>
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
  const paymentAttemptId = getQueryParam("paymentAttemptId");
  const returnCorrelationId = getQueryParam("correlationId");
  const isCancelled = mode === "cancelled";
  const paymentStatusKind = classifyPaymentStatus(summary?.paymentStatus, isCancelled);
  const isPaid = paymentStatusKind === "confirmed";
  const retryQuery = summary?.ticketReference
    ? `ticketReference=${encodeURIComponent(summary.ticketReference)}`
    : summary?.plateNumber
      ? `plateNumber=${encodeURIComponent(summary.plateNumber)}`
      : "";

  async function refreshStatus() {
    if (!paymentAttemptId) {
      setStatus("error");
      setError("Payment reference is missing.");
      return;
    }

    setStatus("checking");
    setError("");
    setReceipt(null);
    setReceiptStatus("idle");
    setReceiptMessage("");
    setReceiptCorrelationId("");

    try {
      const response = await retrievePaymentStatus(paymentAttemptId, returnCorrelationId ?? undefined);
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
  }, [paymentAttemptId, returnCorrelationId]);

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
        {!isPaid && <p className="eyebrow">{isCancelled ? "Payment cancelled" : "Payment return"}</p>}
        {!isPaid && <h1>{status === "checking" ? "Checking payment status" : "Payment status"}</h1>}

        {status === "checking" && <p role="status">Checking payment status</p>}

        {status === "error" && (
          <div className="form-error" role="alert">
            <img src="/assets/icons/error.svg" alt="" aria-hidden="true" />
            <span>{error}</span>
          </div>
        )}

        {status === "loaded" && summary && (
          <>
            <PaymentStatusPanel
              summary={summary}
              statusKind={paymentStatusKind}
            />
            {isPaid && (
              <>
                <SalesInvoicePresentationPanel
                  receipt={receipt}
                  parkingSummary={summary}
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
                <ExitQrCodePanel ticketReference={summary.ticketReference ?? undefined} />
              </>
            )}
          </>
        )}

        {!isPaid && (
          <div className="return-actions">
            <button type="button" className="primary-button status-button" onClick={() => void refreshStatus()}>
              Check Status
            </button>
            {(isCancelled || status === "loaded") && retryQuery && (
              <a className="primary-link" href={`/?${retryQuery}`}>
                Retry Payment
              </a>
            )}
          </div>
        )}
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
  const [paymentReferenceCopyState, setPaymentReferenceCopyState] = useState<"idle" | "copied" | "failed">("idle");
  const paymentReferenceCopyResetTimer = useRef<number | null>(null);
  const paymentReference = summary.paymentReference?.trim() || "";

  useEffect(
    () => () => {
      if (paymentReferenceCopyResetTimer.current !== null) {
        window.clearTimeout(paymentReferenceCopyResetTimer.current);
      }
    },
    []
  );

  const copyPaymentReference = async () => {
    if (!paymentReference) {
      return;
    }

    try {
      if (!navigator.clipboard?.writeText) {
        throw new Error("Clipboard access is unavailable.");
      }

      await navigator.clipboard.writeText(paymentReference);
      setPaymentReferenceCopyState("copied");
    } catch {
      setPaymentReferenceCopyState("failed");
    }

    if (paymentReferenceCopyResetTimer.current !== null) {
      window.clearTimeout(paymentReferenceCopyResetTimer.current);
    }

    paymentReferenceCopyResetTimer.current = window.setTimeout(() => {
      setPaymentReferenceCopyState("idle");
      paymentReferenceCopyResetTimer.current = null;
    }, 1800);
  };

  if (statusKind === "confirmed") {
    return (
      <>
        <section className="payment-confirmation-panel" aria-labelledby="payment-confirmation-heading">
          <h2 id="payment-confirmation-heading">Payment Confirmation</h2>
          <div className="payment-reference">
            <span>Payment reference number:</span>
            <div className="payment-reference-value">
              <strong>{paymentReference || "Unavailable"}</strong>
              <button
                type="button"
                className="copy-payment-reference"
                aria-label="Copy payment reference number"
                title="Copy payment reference number"
                disabled={!paymentReference}
                onClick={() => void copyPaymentReference()}
              >
                {paymentReferenceCopyState === "copied"
                  ? "Copied"
                  : paymentReferenceCopyState === "failed"
                    ? "Copy unavailable"
                    : "Copy"}
              </button>
            </div>
          </div>
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
  parkingSummary,
  status,
  message,
  correlationId,
  onRefresh,
  canRefresh
}: {
  receipt: WebPayReceiptPresentationResponse | null;
  parkingSummary: ParkingSessionResolveResponse;
  status: "idle" | "checking" | "available" | "pending" | "error";
  message: string;
  correlationId: string;
  onRefresh: () => void;
  canRefresh: boolean;
}) {
  const presentation = receipt?.authoritativePresentation?.presentation ?? null;
  const invoice = receipt ? buildThermalSalesInvoice(receipt, parkingSummary) : null;
  const receiptElementRef = useRef<HTMLElement>(null);
  const [isDownloading, setIsDownloading] = useState(false);
  const [downloadError, setDownloadError] = useState("");

  return (
    <section className="sales-invoice-panel" aria-labelledby="sales-invoice-heading">
      <h2 id="sales-invoice-heading">{presentation?.documentTitle ?? "Sales Invoice"}</h2>

      {status === "checking" && <p role="status">Retrieving Sales Invoice</p>}

      {status === "pending" && (
        <div className="invoice-status is-pending" role="status">
          <strong>Fiscal document recorded status is pending</strong>
          <span>{message || "The Sales Invoice is still being prepared."}</span>
          <CustomerSupportReference value={correlationId} compact />
        </div>
      )}

      {status === "error" && (
        <div className="invoice-status is-error" role="alert">
          <strong>Sales Invoice unavailable</strong>
          <span>{message || "Sales Invoice retrieval is temporarily unavailable. Please try again shortly."}</span>
          <CustomerSupportReference value={correlationId} compact />
        </div>
      )}

      {status === "available" && receipt && (
        <>
          <article
            ref={receiptElementRef}
            className="thermal-receipt"
            data-paper-width="80mm"
            aria-label="Printable Sales Invoice"
          >
            <header className="thermal-receipt-header">
              <span className="thermal-rule">================================</span>
              <strong>SALES INVOICE</strong>
              <span className="thermal-rule">================================</span>
              <span>ORIGINAL</span>
              {invoice?.registeredBusinessName && <b>{invoice.registeredBusinessName}</b>}
              {invoice?.siteName && <span>{invoice.siteName}</span>}
            </header>

            {invoice && (
              <>
                <ReceiptDefinitionList
                  className="thermal-receipt-identity"
                  rows={[
                    ["VAT REG TIN", invoice.tin],
                    ["S/N", invoice.posSerialNumber],
                    ["MIN", invoice.min],
                    ["PARKING LOCATION", invoice.parkingLocation],
                    ["TERMINAL ID", invoice.terminalId]
                  ]}
                />

                <ReceiptDefinitionList
                  className="thermal-receipt-summary"
                  rows={[
                    ["SI No", receipt.fiscalDocumentNumber ?? undefined],
                    ["Issued Date", invoice.issuedDate]
                  ]}
                />

                <ReceiptSection title="PARKING DETAILS">
                  <ReceiptDefinitionList
                    rows={[
                      ["Ticket Number", invoice.ticketNumber],
                      ["Plate Number", invoice.plateNumber],
                      ["Entry Time", invoice.entryTime],
                      ["Payment", invoice.paymentTime],
                      ["Duration", invoice.duration]
                    ]}
                  />
                </ReceiptSection>

                <ReceiptSection title="ITEMS">
                  <div className="receipt-item-table" role="table" aria-label="Sales Invoice items">
                    <div className="receipt-item-row receipt-item-heading" role="row">
                      <span>#</span><span>Description</span><span>Qty</span><span>Unit</span><span>Amount</span>
                    </div>
                    {invoice.items.map((item, index) => (
                      <div className="receipt-item-row" role="row" key={`${item.description}-${index}`}>
                        <span>{index + 1}</span>
                        <span>{item.description}</span>
                        <span>{item.quantity}</span>
                        <span>{item.unitAmount ?? "-"}</span>
                        <span>{item.amount}</span>
                      </div>
                    ))}
                  </div>
                  <ReceiptDefinitionList rows={[["Subtotal", invoice.subtotal]]} emphasize />
                </ReceiptSection>

                <ReceiptSection title="DISCOUNTS">
                  <ReceiptDefinitionList
                    rows={[
                      ["Discount Reason", invoice.discountReason ?? "None"],
                      ["Discount Amount", invoice.discountAmount ?? invoice.zeroAmount]
                    ]}
                  />
                </ReceiptSection>

                <ReceiptSection title="VAT BREAKDOWN">
                  <ReceiptDefinitionList
                    rows={[
                      ["VATable Sales", invoice.vatableSales],
                      ["VAT Amount", invoice.vatAmount],
                      ["VAT Exempt", invoice.vatExempt],
                      ["Zero Rated", invoice.zeroRated]
                    ]}
                  />
                </ReceiptSection>

                <ReceiptSection title="PAYMENT DETAILS">
                  <div className="receipt-payment-table" role="table" aria-label="Payment details">
                    <div className="receipt-payment-row receipt-payment-heading" role="row">
                      <span>Type</span><span>Provider</span><span>Amount</span>
                    </div>
                    <div className="receipt-payment-row" role="row">
                      <span>{invoice.paymentMethod}</span>
                      <span>{invoice.paymentProvider}</span>
                      <span>{invoice.paymentAmount}</span>
                    </div>
                  </div>
                  {invoice.isCashChannel && (
                    <ReceiptDefinitionList
                      rows={[
                        ["Tendered Amount", invoice.tenderedAmount],
                        ["Change", invoice.changeAmount]
                      ]}
                      emphasize
                    />
                  )}
                </ReceiptSection>

                <section className="thermal-receipt-declaration">
                  <strong>{invoice.legalStatement}</strong>
                  <span>Print Date</span>
                  <b>{invoice.printDate}</b>
                </section>

                <ReceiptSection title="Customer Information" titleCase>
                  <div className="receipt-customer-fields">
                    <span>Customer Name : ____________________</span>
                    <span>ID No&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; : ____________________</span>
                    <span>Address&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; : ____________________</span>
                    <span>TIN&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; : ____________________</span>
                    <span className="customer-signature">Customer Sign : ____________________</span>
                  </div>
                </ReceiptSection>

                <footer className="thermal-receipt-footer">
                  <strong>THANK YOU FOR CHOOSING OUR SERVICE</strong>
                  {invoice.registeredBusinessName && <b>{invoice.registeredBusinessName}</b>}
                  {invoice.siteName && <span>{invoice.siteName}</span>}
                  <ReceiptDefinitionList
                    rows={[
                      ["VAT REG TIN", invoice.tin],
                      ["ACCR. NO.", invoice.accreditationNumber],
                      ["DATE ISSUED", invoice.accreditationIssuedDate],
                      ["PTU", invoice.ptuNumber],
                      ["PTU Date", invoice.ptuIssuedDate]
                    ]}
                  />
                  {invoice.qrCodeUrl && <img className="receipt-qr" src={invoice.qrCodeUrl} alt="Sales Invoice QR code" />}
                  <strong className="nothing-follows">===== NOTHING FOLLOWS =====</strong>
                </footer>
              </>
            )}
          </article>
        </>
      )}

      {status !== "checking" && (
        <div className="invoice-actions">
          {status === "available" && receipt && (
            <button
              type="button"
              className="primary-button"
              disabled={isDownloading}
              onClick={() => {
                if (!receiptElementRef.current) return;
                setIsDownloading(true);
                setDownloadError("");
                void downloadSalesInvoicePdf(receiptElementRef.current, receipt.fiscalDocumentNumber)
                  .catch(() => setDownloadError("Sales Invoice download is temporarily unavailable."))
                  .finally(() => setIsDownloading(false));
              }}
            >
              {isDownloading ? "Preparing Sales Invoice" : "Download Sales Invoice"}
            </button>
          )}
          {canRefresh && (
            <button type="button" className="secondary-button" onClick={onRefresh}>
              Retrieve Sales Invoice
            </button>
          )}
        </div>
      )}
      {downloadError && <p className="inline-error" role="alert">{downloadError}</p>}
    </section>
  );
}

function ExitQrCodePanel({ ticketReference }: { ticketReference?: string }) {
  const [qrDataUrl, setQrDataUrl] = useState("");

  useEffect(() => {
    let stale = false;
    const ticket = ticketReference?.trim();
    if (!ticket) {
      setQrDataUrl("");
      return () => { stale = true; };
    }

    void QRCode.toDataURL(ticket, { errorCorrectionLevel: "M", margin: 1, width: 320 })
      .then((dataUrl) => {
        if (!stale) setQrDataUrl(dataUrl);
      });
    return () => { stale = true; };
  }, [ticketReference]);

  const ticket = ticketReference?.trim();
  return (
    <section className="exit-qr-panel" aria-labelledby="exit-qr-heading" data-qr-source="ticket-reference">
      <h2 id="exit-qr-heading">Exit QR Code</h2>
      {qrDataUrl && <img src={qrDataUrl} alt="Exit QR Code" />}
      <p>Present this QR code to the scanner at the exit validator.</p>
      <button
        type="button"
        className="secondary-button"
        disabled={!qrDataUrl || !ticket}
        onClick={() => {
          if (qrDataUrl && ticket) downloadDataUrl(qrDataUrl, `ExitPass-Ticket-${safeFileName(ticket)}.png`);
        }}
      >
        Download Exit QR Code
      </button>
    </section>
  );
}

type ReceiptLineItem = {
  description: string;
  quantity: string;
  unitAmount?: string;
  amount: string;
};

type ThermalSalesInvoice = {
  registeredBusinessName?: string;
  siteName?: string;
  tin?: string;
  posSerialNumber?: string;
  min?: string;
  parkingLocation?: string;
  terminalId?: string;
  issuedDate: string;
  ticketNumber?: string;
  plateNumber?: string;
  entryTime?: string;
  paymentTime?: string;
  duration?: string;
  items: ReceiptLineItem[];
  subtotal: string;
  discountReason?: string;
  discountAmount?: string;
  vatableSales?: string;
  vatAmount?: string;
  vatExempt?: string;
  zeroRated?: string;
  zeroAmount: string;
  paymentMethod: string;
  paymentProvider: string;
  paymentAmount: string;
  isCashChannel: boolean;
  tenderedAmount?: string;
  changeAmount?: string;
  legalStatement: string;
  printDate: string;
  accreditationNumber?: string;
  accreditationIssuedDate?: string;
  ptuNumber?: string;
  ptuIssuedDate?: string;
  qrCodeUrl?: string;
};

function buildThermalSalesInvoice(
  receipt: WebPayReceiptPresentationResponse,
  parkingSummary: ParkingSessionResolveResponse
): ThermalSalesInvoice {
  const sections = receipt.authoritativePresentation.presentation?.sections ?? [];
  const rows = sections.flatMap((section) => section.rows ?? []);
  const value = (...keysOrLabels: string[]) => findPresentationValue(rows, keysOrLabels);
  const paymentMethod = customerPaymentMethod(
    parkingSummary.paymentMethod ?? value("appliedStatutoryFiscalFacts.sourcePaymentChannel", "Source Payment Channel", "tenders[0000].tenderTypeCodeKey", "Tender Type")
  );
  const paymentProvider = customerPaymentProvider(parkingSummary.paymentProvider);
  const isCashChannel = paymentMethod === "CASH" || /^(APT|APM|TERMINAL_CASH|CASHIER)/.test(paymentMethod);
  const paymentAmount = formatOptionalReceiptAmount(value("tenders[0000].amount", "Tender Amount"))
    ?? formatReceiptAmount(formatCurrencyAmount(parkingSummary.amountMinorUnits, parkingSummary.currency) ?? "PHP 0.00");
  const subtotal = formatReceiptAmount(value("totals.subtotal", "Subtotal", "totals[0000].amount", "Total Amount") ?? paymentAmount);
  const zeroAmount = formatReceiptAmount(`PHP 0.00`);
  const qrCodeUrl = authoritativeQrCodeUrl(receipt);

  return {
    registeredBusinessName: value("salesInvoiceHeaderSnapshot.registeredBusinessName", "Registered Business Name", "Business Name"),
    siteName: value("salesInvoiceHeaderSnapshot.branchName", "Site / Branch", "Branch Name") ?? parkingSummary.siteName ?? undefined,
    tin: value("salesInvoiceHeaderSnapshot.tin", "TIN", "VAT REG TIN"),
    posSerialNumber: value("salesInvoiceHeaderSnapshot.posSerialNumber", "POS Serial Number", "S/N"),
    min: value("salesInvoiceHeaderSnapshot.machineIdentificationNumber", "MIN"),
    parkingLocation: value("salesInvoiceHeaderSnapshot.parkingLocationDisplay", "Parking Location"),
    terminalId: value("salesInvoiceHeaderSnapshot.terminalId", "Terminal ID"),
    issuedDate: formatDateTime(value("fiscalNumbering.fiscalNumberAssignedAt", "Fiscal Number Assigned At", "Issued Date", "Transaction Date") ?? receipt.updatedAt) ?? "",
    ticketNumber: parkingSummary.ticketReference ?? undefined,
    plateNumber: parkingSummary.plateNumber ?? undefined,
    entryTime: parkingSummary.entryTime ? formatDateTime(parkingSummary.entryTime) : undefined,
    paymentTime: parkingSummary.paymentTime ? formatDateTime(parkingSummary.paymentTime) : formatDateTime(receipt.updatedAt),
    duration: parkingSummary.durationParked ?? formatDuration(parkingSummary.entryTime, parkingSummary.paymentTime ?? receipt.updatedAt),
    items: buildReceiptLineItems(sections, subtotal),
    subtotal,
    discountReason: value("appliedStatutoryFiscalFacts.entitlementType", "Discount Reason", "Entitlement Type", "Benefit Classification"),
    discountAmount: formatOptionalReceiptAmount(value("appliedStatutoryFiscalFacts.statutoryDiscountAmount", "Statutory Discount Amount", "Discount Amount")),
    vatableSales: formatOptionalReceiptAmount(value("totals.vatableSales", "VATable Sales", "taxes[0000].taxableAmount", "Taxable Amount")),
    vatAmount: formatOptionalReceiptAmount(value("appliedStatutoryFiscalFacts.vatAmount", "VAT Amount", "taxes[0000].taxAmount", "Tax Amount")),
    vatExempt: formatOptionalReceiptAmount(value("totals.vatExempt", "VAT Exempt", "VAT-Exempt Sales", "VAT Exempt Sales")),
    zeroRated: formatOptionalReceiptAmount(value("totals.zeroRated", "Zero Rated", "Zero-Rated Sales", "Zero Rated Sales")),
    zeroAmount,
    paymentMethod,
    paymentProvider,
    paymentAmount,
    isCashChannel,
    tenderedAmount: isCashChannel
      ? formatOptionalReceiptAmount(value("tenders[0000].cashReceived", "Tendered Amount", "Cash Received"))
      : undefined,
    changeAmount: isCashChannel
      ? formatOptionalReceiptAmount(value("tenders[0000].change", "Change", "Change Due"))
      : undefined,
    legalStatement: value("salesInvoiceHeaderSnapshot.salesInvoiceLegalStatement", "Sales Invoice Legal Statement") ?? "THIS SERVES AS YOUR SALES INVOICE",
    printDate: formatDateTime(new Date().toISOString()) ?? new Date().toISOString(),
    accreditationNumber: value("salesInvoiceHeaderSnapshot.birAccreditationNumber", "BIR Accreditation Number", "ACCR. NO."),
    accreditationIssuedDate: value("salesInvoiceHeaderSnapshot.birAccreditationIssuedDate", "BIR Accreditation Issued Date", "DATE ISSUED"),
    ptuNumber: value("salesInvoiceHeaderSnapshot.ptuNumber", "PTU Number", "PTU"),
    ptuIssuedDate: value("salesInvoiceHeaderSnapshot.ptuIssuedDate", "PTU Issued Date", "PTU Date"),
    qrCodeUrl
  };
}

function buildReceiptLineItems(
  sections: NonNullable<NonNullable<WebPayReceiptPresentationResponse["authoritativePresentation"]["presentation"]>["sections"]>
  , fallbackAmount: string
): ReceiptLineItem[] {
  const rows = sections.flatMap((section) => section.rows ?? []);
  const indexes = Array.from(new Set(rows.flatMap((row) => {
    const match = row.key?.match(/^lineItems\[(\d+)]\./i);
    return match ? [match[1]] : [];
  })));

  const items = indexes.flatMap((index) => {
    const value = (...suffixes: string[]) => findPresentationValue(rows, suffixes.map((suffix) => `lineItems[${index}].${suffix}`));
    const description = value("description");
    const amount = formatOptionalReceiptAmount(value("netAmount", "grossAmount"));
    if (!description || !amount) {
      return [];
    }

    return [{
      description,
      quantity: value("quantity") ?? "1",
      unitAmount: formatOptionalReceiptAmount(value("unitAmount")),
      amount
    }];
  });

  return items.length > 0 ? items : [{ description: "Parking Fee", quantity: "1", amount: fallbackAmount }];
}

function findPresentationValue(
  rows: SalesInvoicePresentationRow[],
  keysOrLabels: string[]
): string | undefined {
  for (const target of keysOrLabels) {
    const normalizedTarget = target.trim().toLowerCase();
    const row = rows.find((candidate) =>
      candidate.key?.trim().toLowerCase() === normalizedTarget ||
      candidate.label?.trim().toLowerCase() === normalizedTarget
    );
    const rawValue = row?.displayValue ?? row?.value ?? row?.rawValue;
    const resolved = rawValue === null || rawValue === undefined ? "" : String(rawValue).trim();
    if (resolved && !/^not available$/i.test(resolved) && !/^(placeholder|deferred)$/i.test(row?.posture ?? "")) {
      return resolved;
    }
  }

  return undefined;
}

function customerPaymentMethod(value?: string | null): string {
  const normalized = value?.trim().toUpperCase().replace(/[- ]/g, "_") ?? "";
  for (const method of ["QRPH", "GCASH", "MAYA", "CARD", "CASH"]) {
    if (normalized === method || normalized.endsWith(`_${method}`)) {
      return method;
    }
  }

  return normalized && normalized !== "DIGITAL_OR_CASH_TENDER" && normalized !== "CASHLESS" ? normalized : "DIGITAL";
}

function customerPaymentProvider(value?: string | null): string {
  const normalized = value?.trim();
  if (!normalized) {
    return "-";
  }

  return normalized.toUpperCase() === "PAYMONGO" ? "PayMongo" : normalized;
}

function formatOptionalReceiptAmount(value?: string): string | undefined {
  return value ? formatReceiptAmount(value) : undefined;
}

function formatReceiptAmount(value: string): string {
  return value.replace(/^PHP\s+/i, "P ");
}

function authoritativeQrCodeUrl(receipt: WebPayReceiptPresentationResponse): string | undefined {
  const source = receipt.authoritativePresentation as Record<string, unknown>;
  for (const key of ["qrCodeUrl", "digitalSalesInvoiceQrCodeUrl"]) {
    const value = source[key];
    if (typeof value === "string" && /^https:\/\//i.test(value.trim())) {
      return value.trim();
    }
  }

  return undefined;
}

async function downloadSalesInvoicePdf(receiptElement: HTMLElement, fiscalDocumentNumber?: string | null): Promise<void> {
  const [{ default: html2canvas }, { jsPDF }] = await Promise.all([
    import("html2canvas"),
    import("jspdf")
  ]);
  const canvas = await html2canvas(receiptElement, {
    backgroundColor: "#ffffff",
    logging: false,
    scale: 2,
    useCORS: true
  });
  const widthMm = 80;
  const heightMm = Math.max(1, (canvas.height * widthMm) / canvas.width);
  const pdf = new jsPDF({
    orientation: "portrait",
    unit: "mm",
    format: [widthMm, heightMm],
    compress: true
  });
  pdf.addImage(canvas.toDataURL("image/png"), "PNG", 0, 0, widthMm, heightMm, undefined, "FAST");
  pdf.save(`${safeFileName(fiscalDocumentNumber?.trim() || "Sales-Invoice")}.pdf`);
}

function downloadDataUrl(dataUrl: string, filename: string): void {
  const link = document.createElement("a");
  link.href = dataUrl;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  link.remove();
}

function safeFileName(value: string): string {
  return value.replace(/[<>:"/\\|?*\u0000-\u001f]/g, "-").replace(/\s+/g, "-");
}

function ReceiptSection({
  title,
  titleCase = false,
  children
}: {
  title: string;
  titleCase?: boolean;
  children: ReactNode;
}) {
  return (
    <section className="thermal-receipt-section">
      <h3 className={titleCase ? "receipt-title-case" : undefined}>{title}</h3>
      {children}
    </section>
  );
}

function ReceiptDefinitionList({
  rows,
  className = "",
  emphasize = false
}: {
  rows: Array<[string, string | undefined]>;
  className?: string;
  emphasize?: boolean;
}) {
  const availableRows = rows.filter((row): row is [string, string] => Boolean(row[1]?.trim()));
  if (availableRows.length === 0) {
    return null;
  }

  return (
    <dl className={`${className} ${emphasize ? "receipt-emphasis" : ""}`.trim()}>
      {availableRows.map(([label, value]) => (
        <div key={label}><dt>{label}</dt><dd>{value}</dd></div>
      ))}
    </dl>
  );
}

function ExitInstructionPanel({ summary }: { summary: ParkingSessionResolveResponse }) {
  const instruction = summary.exitInstruction;
  const authorizationStatus = instruction?.status ?? summary.exitAuthorizationStatus;
  const exitBy = instruction?.exitBy ?? summary.exitBy ?? instruction?.expiresAt ?? summary.exitAuthorizationExpiresAt;
  const hasExitAuthorization = isExitAuthorizationAvailable(authorizationStatus);

  return (
    <section className={hasExitAuthorization ? "exit-instruction-panel is-ready" : "exit-instruction-panel"} aria-labelledby="exit-instruction-heading">
      <h2 id="exit-instruction-heading">Exit Instruction</h2>
      {hasExitAuthorization ? (
        <>
          <strong>Proceed to exit</strong>
          {exitBy && <p className="exit-deadline">Exit by {formatTime(exitBy)}</p>}
          <p>Additional parking charges will apply if you do not exit by the expiry time.</p>
          <p>Do not close this page until you have exited the parking lot.</p>
        </>
      ) : (
        <>
          <strong>Exit authorization is being prepared</strong>
          <p>Please try again shortly.</p>
        </>
      )}
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
  const applicationStatus = decision.applicationCommandStatus.toUpperCase();
  return !decision.payableBasisReady &&
    (readinessAction === "POLL_READBACK" || ((readinessStatus === "APPLICATION_PROCESSING" || applicationStatus === "PROCESSING") && readinessAction !== "WAIT_THEN_RETRY_ORIGINAL_IDEMPOTENCY_KEY")) &&
    readinessStatus !== "DECISION_REJECTED" &&
    readinessStatus !== "TERMINAL_FAILURE" &&
    !readinessStatus.includes("CONFLICT");
}

function isTerminalStatutoryDecision(decision: WebPayStatutoryDiscountDecisionResponse): boolean {
  const readinessStatus = decision.payableBasisReadinessStatus.toUpperCase();
  const readinessAction = decision.payableBasisReadinessAction?.toUpperCase() ?? "";
  const decisionResult = decision.decisionResultStatus?.toUpperCase() ?? "";
  const safeErrorCode = decision.safeErrorCode?.toUpperCase() ?? "";
  return decision.payableBasisReady ||
    readinessStatus === "DECISION_REJECTED" ||
    readinessAction === "DO_NOT_RETRY" ||
    decisionResult === "REJECTED" ||
    safeErrorCode.includes("SEMANTIC_CONFLICT") ||
    readinessStatus.includes("CONFLICT") ||
    decision.overallResultClassification.toUpperCase().includes("TERMINAL");
}

function canSubmitApplicationIntent(decision: WebPayStatutoryDiscountDecisionResponse): boolean {
  const decisionCommandStatus = decision.decisionCommandStatus.toUpperCase();
  const decisionResult = decision.decisionResultStatus?.toUpperCase() ?? "";
  const readinessStatus = decision.payableBasisReadinessStatus.toUpperCase();
  const readinessAction = decision.payableBasisReadinessAction?.toUpperCase() ?? "";
  return !decision.payableBasisReady &&
    decisionCommandStatus === "COMPLETED" &&
    decisionResult === "APPROVED" &&
    readinessStatus === "DECISION_APPROVED_APPLICATION_NOT_REQUESTED" &&
    readinessAction === "SUBMIT_APPLICATION_INTENT";
}

function canRetryApplicationIntent(decision: WebPayStatutoryDiscountDecisionResponse): boolean {
  const readinessAction = decision.payableBasisReadinessAction?.toUpperCase() ?? "";
  const recoveryAction = decision.recoveryAction?.toUpperCase() ?? "";
  const safeErrorCode = decision.safeErrorCode?.toUpperCase() ?? "";
  return !decision.payableBasisReady &&
    decision.retryable &&
    !isTerminalStatutoryDecision(decision) &&
    (readinessAction === "WAIT_THEN_RETRY_ORIGINAL_IDEMPOTENCY_KEY" || recoveryAction === "WAIT_THEN_RETRY_ORIGINAL_IDEMPOTENCY_KEY") &&
    safeErrorCode === "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_TEMPORARILY_UNAVAILABLE";
}

function getAppliedStatutoryPaymentBasis(
  decision?: WebPayStatutoryDiscountDecisionResponse | null
): AppliedStatutoryPaymentBasis | null {
  if (!decision) {
    return null;
  }

  const decisionResult = decision.decisionResultStatus?.toUpperCase() ?? "";
  const applicationStatus = decision.applicationCommandStatus.toUpperCase();
  const appliedTariffSnapshotId = decision.appliedTariffSnapshotId?.trim();
  const currency = decision.currency?.trim();
  const applicationCommandId = decision.statutoryDiscountPayableBasisApplicationCommandId?.trim();

  if (
    decisionResult !== "APPROVED" ||
    applicationStatus !== "APPLIED" ||
    !decision.payableBasisReady ||
    !appliedTariffSnapshotId ||
    decision.finalPayableAmountMinorUnits === null ||
    decision.finalPayableAmountMinorUnits === undefined ||
    !Number.isFinite(decision.finalPayableAmountMinorUnits) ||
    !currency ||
    !applicationCommandId ||
    !decision.statutoryDiscountDecisionCommandId.trim()
  ) {
    return null;
  }

  return {
    tariffSnapshotId: appliedTariffSnapshotId,
    amountMinorUnits: decision.finalPayableAmountMinorUnits,
    currency: currency.toUpperCase(),
    statutoryDiscountDecisionCommandId: decision.statutoryDiscountDecisionCommandId.trim(),
    statutoryDiscountPayableBasisApplicationCommandId: applicationCommandId
  };
}

function isPendingReviewStatutoryDecision(decision: WebPayStatutoryDiscountDecisionResponse): boolean {
  const readinessStatus = decision.payableBasisReadinessStatus.toUpperCase();
  const decisionCommandStatus = decision.decisionCommandStatus.toUpperCase();
  const decisionResult = decision.decisionResultStatus?.toUpperCase() ?? "";
  const overall = decision.overallResultClassification.toUpperCase();
  const recovery = decision.recoveryClassification.toUpperCase();

  return !decision.payableBasisReady &&
    !isTerminalStatutoryDecision(decision) &&
    (readinessStatus === "AWAITING_REVIEW" ||
      decisionCommandStatus === "AWAITING_REVIEW" ||
      decisionResult === "NOT_DECIDED" ||
      overall === "PENDING_REVIEW" ||
      recovery === "PENDING_REVIEW" ||
      recovery === "PENDING");
}

function isRejectedStatutoryDecision(decision: WebPayStatutoryDiscountDecisionResponse): boolean {
  const readinessStatus = decision.payableBasisReadinessStatus.toUpperCase();
  const decisionResult = decision.decisionResultStatus?.toUpperCase() ?? "";
  return readinessStatus === "DECISION_REJECTED" || decisionResult === "REJECTED";
}

function getStatutoryDiscountPaymentAvailabilityCopy(decision: WebPayStatutoryDiscountDecisionResponse): string {
  if (getAppliedStatutoryPaymentBasis(decision)) {
    return "Payment is available using the Central PMS-approved statutory payable basis.";
  }

  if (isPendingReviewStatutoryDecision(decision)) {
    return "The parking privilege is not applied while review is pending. You may keep waiting or explicitly choose to pay the regular amount.";
  }

  return "Payment with the statutory discount remains unavailable while this statutory discount workflow is active.";
}

function getCoveredStatutoryEntitlementTypes(
  availability: WebPayStatutoryDiscountAvailabilityResponse | null
): StatutoryDiscountEntitlementType[] {
  if (!availability ||
      !availability.statutoryParkingBenefitAvailable ||
      availability.availabilityStatus.toUpperCase() !== "AVAILABLE") {
    return [];
  }

  return availability.coveredEntitlementTypes.filter(
    (entitlement): entitlement is StatutoryDiscountEntitlementType => entitlement === "SENIOR_CITIZEN" || entitlement === "PWD"
  );
}

function statutoryAvailabilityRequiresEvidence(
  availability: WebPayStatutoryDiscountAvailabilityResponse | null
): boolean {
  return Boolean(
    availability?.requiredEvidenceTypes.some(
      (requirement) => requirement.requirementStatus.trim().toUpperCase() === "REQUIRED"
    )
  );
}

function getStatutoryAvailabilityCopy(
  state: StatutoryDiscountAvailabilityUiState
): { heading: string; body: string; tone: "pending" | "success" | "warning" | "error" } | null {
  if (state.isLoading) {
    return {
      heading: "Checking parking privilege availability",
      body: "Checking whether this parking session has an active covered Senior Citizen or PWD parking privilege.",
      tone: "pending"
    };
  }

  if (state.error) {
    return {
      heading: "Parking privilege availability unavailable",
      body: state.error,
      tone: "warning"
    };
  }

  const availability = state.availability;
  if (!availability) {
    return null;
  }

  const covered = getCoveredStatutoryEntitlementTypes(availability);
  if (covered.length > 0) {
    const label = covered.length === 2
      ? "Senior Citizen and PWD"
      : covered[0] === "SENIOR_CITIZEN" ? "Senior Citizen" : "PWD";
    return {
      heading: "Parking privilege request available",
      body: `${label} parking privilege requests may be submitted for review for this parking session.`,
      tone: "success"
    };
  }

  return {
    heading: "Parking privilege request not available",
    body: "Parking privilege requests are not available for this parking session. You may continue with the regular parking amount.",
    tone: "warning"
  };
}

function getStatutoryPendingLifecycleRediscoveryMessage(
  rediscovery: WebPayStatutoryDiscountPendingLifecycleRediscoveryResponse
): string {
  switch (rediscovery.classification.toUpperCase()) {
    case "AMBIGUOUS_SESSION":
      return "A prior statutory discount request could not be safely matched to one parking session. Use the ticket reference or ask for assistance.";
    case "SOURCE_UNAVAILABLE":
    case "UNEXPECTED_FAILURE":
      return "Existing statutory discount request recovery is temporarily unavailable. You may try again shortly.";
    case "MALFORMED_AUTHORITATIVE_STATE":
      return "An existing statutory discount request could not be safely restored. Please refresh status shortly or ask for assistance.";
    case "ACCESS_DENIED":
      return "Parking-privilege request recovery is temporarily unavailable. Please try again later or ask a parking attendant for assistance.";
    default:
      return "Existing statutory discount request recovery is temporarily unavailable. Please try again shortly.";
  }
}

function getStatutoryRecoveryStageFromDecision(decision: WebPayStatutoryDiscountDecisionResponse): StatutoryRecoveryStage {
  if (getAppliedStatutoryPaymentBasis(decision)) {
    return "PAYABLE_READY";
  }

  if (isTerminalStatutoryDecision(decision)) {
    return "TERMINAL";
  }

  if (canSubmitApplicationIntent(decision)) {
    return "APPLICATION_AVAILABLE";
  }

  const readinessStatus = decision.payableBasisReadinessStatus.toUpperCase();
  const applicationStatus = decision.applicationCommandStatus.toUpperCase();
  if (readinessStatus === "APPLICATION_PROCESSING" || applicationStatus === "PROCESSING") {
    return "APPLICATION_PROCESSING";
  }

  return "DECISION_PENDING";
}

function getStatutoryRecoveryStageAfterDecisionRead(
  decision: WebPayStatutoryDiscountDecisionResponse,
  currentRecovery: WebPayStatutoryRecoveryRecord | null
): StatutoryRecoveryStage {
  if (
    currentRecovery?.parkingSessionId === decision.parkingSessionId &&
    currentRecovery.stage === "PAYMENT_SUBMITTING"
  ) {
    return "PAYMENT_SUBMITTING";
  }

  return getStatutoryRecoveryStageFromDecision(decision);
}

function normalizeEntitlementType(value: string | null | undefined, fallback: StatutoryDiscountEntitlementType = "SENIOR_CITIZEN"): StatutoryDiscountEntitlementType {
  return value === "PWD" || value === "SENIOR_CITIZEN" ? value : fallback;
}

function getInitialRecoveryMessage(load: { record: WebPayStatutoryRecoveryRecord | null; cleared: boolean; unavailable: boolean; reason?: string }): string {
  if (load.unavailable) {
    return "Durable statutory discount recovery is unavailable in this browser. This page remains safe, but refresh recovery may not work.";
  }

  if (load.cleared) {
    return "An expired or invalid statutory discount recovery record was cleared. Browser metadata is not authoritative.";
  }

  if (load.record) {
    return getCrossTabRecoveryMessage(load.record);
  }

  return "";
}

function getCrossTabRecoveryMessage(record: WebPayStatutoryRecoveryRecord): string {
  switch (record.stage) {
    case "DECISION_SUBMITTING":
      return "Another page may be submitting this statutory discount request. Wait for the server reference or refresh status before trying again.";
    case "APPLICATION_SUBMITTING":
      return "Another page may be applying the approved statutory discount. Refresh status before trying again.";
    case "PAYMENT_SUBMITTING":
      return "Another page may be starting payment for this applied statutory payable basis. Wait before trying again.";
    case "PAYMENT_HANDOFF":
      return "Payment was already started for this statutory discount workflow. Use the existing payment attempt or payment-return link.";
    case "PAYABLE_READY":
      return "A statutory discount workflow is ready for payment, but this page will refresh authoritative status before enabling payment.";
    default:
      return "A statutory discount workflow was found in browser recovery. Current status will be restored from Central PMS.";
  }
}

function getStatutoryDiscountPaymentBlockMessage(decision: WebPayStatutoryDiscountDecisionResponse): string {
  if (decision.payableBasisReady) {
    return getAppliedStatutoryPaymentBasis(decision)
      ? "Payment is available using the approved statutory payable basis."
      : "The statutory discount payable basis is missing required authoritative payment facts.";
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
      body: "Central PMS returned the approved payable basis. Continue only when you are ready to start payment.",
      tone: "success"
    };
  }

  if (decision.payableBasisReady) {
    return {
      heading: "Payment basis incomplete",
      body: "Central PMS has not returned all required payment facts for the approved statutory discount. Refresh status before payment.",
      tone: "warning"
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

  if (decision.retryable && readinessAction === "WAIT_THEN_RETRY_ORIGINAL_IDEMPOTENCY_KEY") {
    return {
      heading: "Discount application temporarily unavailable",
      body: "Central PMS could not apply the approved discount yet. Retry the same application request after a short wait.",
      tone: "warning"
    };
  }

  if (readinessStatus.includes("CONFLICT") || (decision.safeErrorCode?.toUpperCase() ?? "").includes("SEMANTIC_CONFLICT")) {
    return {
      heading: "Statutory discount conflict",
      body: "The approved discount could not be applied because the application request no longer matches the canonical decision.",
      tone: "error"
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

  if (
    readinessStatus === "AWAITING_REVIEW" ||
    decision.decisionCommandStatus.toUpperCase() === "AWAITING_REVIEW" ||
    decision.overallResultClassification.toUpperCase() === "PENDING_REVIEW" ||
    decision.recoveryClassification.toUpperCase() === "PENDING_REVIEW"
  ) {
    return {
      heading: "Awaiting review",
      body: "Your Senior Citizen or PWD parking privilege request was received and is awaiting review. Payment remains unavailable until review and payable-basis application are complete.",
      tone: "pending"
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
    body: "Your Senior Citizen or PWD parking privilege request was received and is awaiting review. Payment remains unavailable until review and payable-basis application are complete.",
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

function shouldResetStatutoryRecoveryForLocalValidation(): boolean {
  const value = getQueryParam("webpayStatutoryRecoveryReset").toLowerCase();
  if (value !== "1" && value !== "true") {
    return false;
  }

  const hostname = window.location.hostname.toLowerCase();
  return hostname === "127.0.0.1" || hostname === "localhost" || hostname === "::1";
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

function formatTime(value?: string | null): string | undefined {
  if (!value) {
    return undefined;
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return new Intl.DateTimeFormat("en-PH", {
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

function CustomerSupportReference({ value, compact = false }: { value?: string | null; compact?: boolean }) {
  const supportReference = formatCustomerSupportReference(value);
  if (!supportReference) {
    return null;
  }

  const content = <>Support reference: <strong>{supportReference}</strong></>;
  return compact
    ? <small className="support-reference">{content}</small>
    : <p className="support-reference">{content}</p>;
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
