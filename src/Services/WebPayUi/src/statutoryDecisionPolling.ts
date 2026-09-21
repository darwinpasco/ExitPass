import type { WebPayStatutoryDiscountDecisionResponse } from "./types";

export const statutoryDecisionPollIntervalMs = 3_000;

export function isZeroPayableExitAuthorized(decision: WebPayStatutoryDiscountDecisionResponse): boolean {
  const authorization = decision.exitAuthorization;
  return Boolean(authorization && authorization.authorizationStatus.toUpperCase() === "ISSUED" &&
    Date.parse(authorization.expirationTimestamp) > Date.now());
}

export function isZeroPayableExitExpired(decision: WebPayStatutoryDiscountDecisionResponse): boolean {
  const authorization = decision.exitAuthorization;
  return Boolean(authorization && authorization.authorizationStatus.toUpperCase() === "ISSUED" &&
    Date.parse(authorization.expirationTimestamp) <= Date.now());
}

export function isTerminalStatutoryDecision(decision: WebPayStatutoryDiscountDecisionResponse): boolean {
  const readinessStatus = decision.payableBasisReadinessStatus.toUpperCase();
  const readinessAction = decision.payableBasisReadinessAction?.toUpperCase() ?? "";
  const decisionResult = decision.decisionResultStatus?.toUpperCase() ?? "";
  const safeErrorCode = decision.safeErrorCode?.toUpperCase() ?? "";
  if (readinessStatus === "DECISION_REJECTED" ||
    readinessStatus === "TERMINAL_FAILURE" ||
    readinessAction === "DO_NOT_RETRY" ||
    decisionResult === "REJECTED" ||
    safeErrorCode.includes("SEMANTIC_CONFLICT") ||
    readinessStatus.includes("CONFLICT") ||
    decision.overallResultClassification.toUpperCase().includes("TERMINAL")) {
    return true;
  }

  if (!decision.payableBasisReady) {
    return false;
  }

  if (decision.finalPayableAmountMinorUnits !== 0) {
    return true;
  }

  if (isZeroPayableExitExpired(decision)) {
    return true;
  }

  const fiscalState = (decision.zeroPayableFiscalCompletion?.fiscalIssuanceState ?? "").toUpperCase().replaceAll("_", "");
  if (fiscalState === "FISCALISSUANCECONFLICT" || fiscalState === "FISCALISSUANCEFAILEDREQUEST" ||
      fiscalState === "FISCALISSUANCEEXCEPTIONRELEASED") {
    return true;
  }

  const fiscal = decision.zeroPayableFiscalCompletion;
  return Boolean(decision.zeroPayableStatutoryFinality?.finalityState === "ZERO_PAYABLE_STATUTORY_FINALITY" &&
    fiscal?.fiscalPrerequisiteSatisfied && fiscal.posServerFiscalDocumentId && fiscal.fiscalDocumentNumber &&
    isZeroPayableExitAuthorized(decision));
}

export function shouldPollStatutoryDecision(decision: WebPayStatutoryDiscountDecisionResponse): boolean {
  if (isTerminalStatutoryDecision(decision)) {
    return false;
  }

  if (decision.payableBasisReady && decision.finalPayableAmountMinorUnits === 0) {
    return true;
  }

  const readinessStatus = decision.payableBasisReadinessStatus.toUpperCase();
  const readinessAction = decision.payableBasisReadinessAction?.toUpperCase() ?? "";
  const decisionStatus = decision.decisionCommandStatus.toUpperCase();
  const decisionResult = decision.decisionResultStatus?.toUpperCase() ?? "";
  const applicationStatus = decision.applicationCommandStatus.toUpperCase();
  const overall = decision.overallResultClassification.toUpperCase();
  const recovery = decision.recoveryClassification.toUpperCase();

  return readinessAction === "POLL_READBACK" ||
    readinessAction === "SUBMIT_APPLICATION_INTENT" ||
    readinessAction === "WAIT_THEN_RETRY_ORIGINAL_IDEMPOTENCY_KEY" ||
    readinessStatus === "AWAITING_REVIEW" ||
    readinessStatus === "PENDING_REVIEW" ||
    readinessStatus === "APPLICATION_PROCESSING" ||
    decisionStatus === "AWAITING_REVIEW" ||
    decisionStatus === "PENDING_REVIEW" ||
    decisionResult === "NOT_DECIDED" ||
    applicationStatus === "PROCESSING" ||
    applicationStatus === "FAILED_RETRYABLE" ||
    overall.includes("PENDING") ||
    recovery.includes("PENDING");
}

export async function pollStatutoryDecision(options: {
  initialDecision: WebPayStatutoryDiscountDecisionResponse;
  signal: AbortSignal;
  read: (signal: AbortSignal) => Promise<WebPayStatutoryDiscountDecisionResponse>;
  onDecision: (decision: WebPayStatutoryDiscountDecisionResponse) => void;
  onTransientError: (error: unknown) => void;
  intervalMs?: number;
}): Promise<void> {
  let current = options.initialDecision;
  let firstRead = true;
  const intervalMs = options.intervalMs ?? statutoryDecisionPollIntervalMs;

  while (!options.signal.aborted && shouldPollStatutoryDecision(current)) {
    if (!firstRead && !await waitForNextPoll(intervalMs, options.signal)) {
      return;
    }
    firstRead = false;

    try {
      current = await options.read(options.signal);
      if (options.signal.aborted) {
        return;
      }
      options.onDecision(current);
    } catch (error) {
      if (options.signal.aborted || isAbortError(error)) {
        return;
      }
      options.onTransientError(error);
    }
  }
}

function waitForNextPoll(intervalMs: number, signal: AbortSignal): Promise<boolean> {
  if (signal.aborted) {
    return Promise.resolve(false);
  }

  return new Promise((resolve) => {
    const timeout = window.setTimeout(() => {
      signal.removeEventListener("abort", onAbort);
      resolve(true);
    }, intervalMs);
    const onAbort = () => {
      window.clearTimeout(timeout);
      resolve(false);
    };
    signal.addEventListener("abort", onAbort, { once: true });
  });
}

function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === "AbortError";
}
