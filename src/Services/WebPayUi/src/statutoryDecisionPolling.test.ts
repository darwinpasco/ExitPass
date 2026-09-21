import { afterEach, describe, expect, it, vi } from "vitest";
import type { WebPayStatutoryDiscountDecisionResponse } from "./types";
import {
  pollStatutoryDecision,
  isZeroPayableExitExpired,
  shouldPollStatutoryDecision,
  statutoryDecisionPollIntervalMs
} from "./statutoryDecisionPolling";

describe("statutory decision polling", () => {
  afterEach(() => vi.useRealTimers());

  it("continues beyond three reads with a three-second interval and no overlap", async () => {
    vi.useFakeTimers();
    let activeReads = 0;
    let maximumActiveReads = 0;
    const read = vi.fn(async () => {
      activeReads += 1;
      maximumActiveReads = Math.max(maximumActiveReads, activeReads);
      await Promise.resolve();
      activeReads -= 1;
      return pendingDecision();
    });
    const controller = new AbortController();
    const polling = pollStatutoryDecision({
      initialDecision: pendingDecision(),
      signal: controller.signal,
      read,
      onDecision: vi.fn(),
      onTransientError: vi.fn()
    });

    await vi.advanceTimersByTimeAsync(0);
    expect(read).toHaveBeenCalledTimes(1);
    for (let expected = 2; expected <= 4; expected += 1) {
      await vi.advanceTimersByTimeAsync(statutoryDecisionPollIntervalMs - 1);
      expect(read).toHaveBeenCalledTimes(expected - 1);
      await vi.advanceTimersByTimeAsync(1);
      expect(read).toHaveBeenCalledTimes(expected);
    }
    expect(maximumActiveReads).toBe(1);

    controller.abort();
    await polling;
  });

  it.each([
    ["ready", { payableBasisReady: true }],
    ["rejected", { decisionResultStatus: "REJECTED", payableBasisReadinessStatus: "DECISION_REJECTED" }],
    ["terminal failure", { payableBasisReadinessStatus: "FAILED", payableBasisReadinessAction: "DO_NOT_RETRY" }],
    ["semantic conflict", { payableBasisReadinessStatus: "APPLICATION_CONFLICT" }]
  ])("stops for %s", async (_name, overrides) => {
    const read = vi.fn();
    await pollStatutoryDecision({
      initialDecision: pendingDecision(overrides),
      signal: new AbortController().signal,
      read,
      onDecision: vi.fn(),
      onTransientError: vi.fn()
    });
    expect(read).not.toHaveBeenCalled();
  });

  it("cancels the scheduled read when the active component or context is disposed", async () => {
    vi.useFakeTimers();
    const controller = new AbortController();
    const read = vi.fn().mockResolvedValue(pendingDecision());
    const polling = pollStatutoryDecision({
      initialDecision: pendingDecision(),
      signal: controller.signal,
      read,
      onDecision: vi.fn(),
      onTransientError: vi.fn()
    });
    await vi.advanceTimersByTimeAsync(0);
    expect(read).toHaveBeenCalledTimes(1);
    controller.abort();
    await vi.advanceTimersByTimeAsync(statutoryDecisionPollIntervalMs * 2);
    await polling;
    expect(read).toHaveBeenCalledTimes(1);
  });

  it("does not overlap readback requests while one is still active", async () => {
    vi.useFakeTimers();
    let resolveRead!: (value: WebPayStatutoryDiscountDecisionResponse) => void;
    const read = vi.fn((signal: AbortSignal) => new Promise<WebPayStatutoryDiscountDecisionResponse>((resolve, reject) => {
      resolveRead = resolve;
      signal.addEventListener("abort", () => reject(new DOMException("Aborted", "AbortError")), { once: true });
    }));
    const controller = new AbortController();
    const polling = pollStatutoryDecision({
      initialDecision: pendingDecision(),
      signal: controller.signal,
      read,
      onDecision: vi.fn(),
      onTransientError: vi.fn()
    });

    await vi.advanceTimersByTimeAsync(statutoryDecisionPollIntervalMs * 3);
    expect(read).toHaveBeenCalledTimes(1);
    resolveRead(pendingDecision());
    await Promise.resolve();
    await vi.advanceTimersByTimeAsync(statutoryDecisionPollIntervalMs);
    expect(read).toHaveBeenCalledTimes(2);

    controller.abort();
    await polling;
  });

  it("keeps retryable application and review postures polling", () => {
    expect(shouldPollStatutoryDecision(pendingDecision())).toBe(true);
    expect(shouldPollStatutoryDecision(pendingDecision({
      decisionCommandStatus: "COMPLETED",
      decisionResultStatus: "APPROVED",
      applicationCommandStatus: "PROCESSING",
      payableBasisReadinessStatus: "APPLICATION_PROCESSING"
    }))).toBe(true);
    expect(shouldPollStatutoryDecision(pendingDecision({
      decisionCommandStatus: "COMPLETED",
      decisionResultStatus: "APPROVED",
      applicationCommandStatus: "FAILED_RETRYABLE",
      payableBasisReadinessAction: "WAIT_THEN_RETRY_ORIGINAL_IDEMPOTENCY_KEY"
    }))).toBe(true);
  });

  it("continues zero-payable readback until fiscal and exit completion", () => {
    const applied = pendingDecision({
      decisionCommandStatus: "COMPLETED",
      decisionResultStatus: "APPROVED",
      applicationCommandStatus: "APPLIED",
      payableBasisReady: true,
      payableBasisReadinessStatus: "PAYABLE_BASIS_READY",
      payableBasisReadinessAction: null,
      finalPayableAmountMinorUnits: 0,
      overallResultClassification: "DECISION_AND_APPLICATION_COMPLETED",
      recoveryClassification: "NONE"
    });
    expect(shouldPollStatutoryDecision(applied)).toBe(true);
    const finality = { ...applied, zeroPayableStatutoryFinality: { finalityState: "ZERO_PAYABLE_STATUTORY_FINALITY" } as WebPayStatutoryDiscountDecisionResponse["zeroPayableStatutoryFinality"] };
    expect(shouldPollStatutoryDecision(finality)).toBe(true);
    const fiscal = { ...finality, zeroPayableFiscalCompletion: {
      fiscalIssuanceReferenceId: "a", fiscalPrerequisiteSatisfied: false, posServerCallAttempted: true,
      fiscalIssuanceState: "FISCAL_ISSUANCE_REQUESTED", completionBasis: "ZERO_PAYABLE_STATUTORY_FINALITY"
    } };
    expect(shouldPollStatutoryDecision(fiscal)).toBe(true);
    const invoiced = { ...fiscal, zeroPayableFiscalCompletion: {
      ...fiscal.zeroPayableFiscalCompletion, fiscalPrerequisiteSatisfied: true,
      fiscalIssuanceState: "FISCAL_ISSUANCE_RECORDED", posServerFiscalDocumentId: "b", fiscalDocumentNumber: "SI-0001"
    } };
    expect(shouldPollStatutoryDecision(invoiced)).toBe(true);
    expect(shouldPollStatutoryDecision({ ...invoiced, exitAuthorization: {
      exitAuthorizationId: "c", parkingSessionId: invoiced.parkingSessionId,
      tariffSnapshotId: "d", completionBasis: "ZERO_PAYABLE_STATUTORY_FINALITY",
      authorizationStatus: "ISSUED", issuedAt: "2026-09-21T00:00:00Z", expirationTimestamp: "2030-09-21T00:15:00Z"
    } })).toBe(false);
    const expired = { ...invoiced, exitAuthorization: {
      exitAuthorizationId: "c", parkingSessionId: invoiced.parkingSessionId,
      tariffSnapshotId: "d", completionBasis: "ZERO_PAYABLE_STATUTORY_FINALITY",
      authorizationStatus: "ISSUED", issuedAt: "2020-01-01T00:00:00Z", expirationTimestamp: "2020-01-01T00:15:00Z"
    } };
    expect(isZeroPayableExitExpired(expired)).toBe(true);
    expect(shouldPollStatutoryDecision(expired)).toBe(false);
    expect(shouldPollStatutoryDecision({ ...fiscal, zeroPayableFiscalCompletion: {
      ...fiscal.zeroPayableFiscalCompletion, fiscalIssuanceState: "FISCAL_ISSUANCE_FAILED_REQUEST"
    } })).toBe(false);
  });
});

function pendingDecision(overrides: Partial<WebPayStatutoryDiscountDecisionResponse> = {}): WebPayStatutoryDiscountDecisionResponse {
  return {
    statutoryDiscountDecisionCommandId: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
    requestReference: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
    statutoryDiscountPayableBasisApplicationCommandId: null,
    statutoryDiscountValidationId: null,
    parkingSessionId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
    siteId: "dddddddd-dddd-4ddd-8ddd-dddddddddddd",
    siteGroupId: "eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee",
    entitlementType: "SENIOR_CITIZEN",
    decisionCommandStatus: "AWAITING_REVIEW",
    decisionResultStatus: "NOT_DECIDED",
    applicationCommandStatus: "NOT_REQUESTED",
    applicationResultClassification: "NOT_REQUESTED",
    payableBasisReady: false,
    payableBasisReadinessStatus: "AWAITING_REVIEW",
    payableBasisReadinessAction: "POLL_READBACK",
    originalTariffSnapshotId: null,
    appliedTariffSnapshotId: null,
    originalAmountMinorUnits: null,
    vatExclusiveBasisAmountMinorUnits: null,
    vatAmountMinorUnits: null,
    vatTreatment: null,
    statutoryDiscountAmountMinorUnits: null,
    finalPayableAmountMinorUnits: null,
    currency: null,
    retryable: false,
    recoveryClassification: "PENDING",
    recoveryAction: "POLL_READBACK",
    safeErrorCode: null,
    overallResultClassification: "PENDING",
    oneShotComplete: false,
    correlationId: "ffffffff-ffff-4fff-8fff-ffffffffffff",
    createdAt: "2026-09-21T00:00:00Z",
    decidedAt: null,
    appliedAt: null,
    ...overrides
  };
}
