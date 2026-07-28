import { describe, expect, it } from "vitest";
import {
  clearStatutoryRecoveryRecord,
  createStatutoryRecoveryRecord,
  hasKnownInFlightStatutoryRecoveryStage,
  loadStatutoryRecoveryRecord,
  saveStatutoryRecoveryRecord,
  statutoryRecoveryRecordVersion,
  statutoryRecoveryStorageKey,
  updateStatutoryRecoveryRecord
} from "./statutoryRecovery";

class MemoryStorage implements Pick<Storage, "getItem" | "setItem" | "removeItem"> {
  public readonly values = new Map<string, string>();

  public getItem(key: string): string | null {
    return this.values.get(key) ?? null;
  }

  public setItem(key: string, value: string): void {
    this.values.set(key, value);
  }

  public removeItem(key: string): void {
    this.values.delete(key);
  }
}

class ThrowingStorage implements Pick<Storage, "getItem" | "setItem" | "removeItem"> {
  public getItem(): string | null {
    throw new Error("storage unavailable");
  }

  public setItem(): void {
    throw new Error("storage unavailable");
  }

  public removeItem(): void {
    throw new Error("storage unavailable");
  }
}

const now = new Date("2026-07-28T08:00:00.000Z");

function validRecord() {
  return createStatutoryRecoveryRecord({
    parkingSessionId: "11111111-1111-4111-8111-111111111111",
    entitlementType: "SENIOR_CITIZEN",
    statutoryDiscountDecisionCommandId: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
    decisionIdempotencyKey: "webpay-statutory-discount-decision:11111111-1111-4111-8111-111111111111:SENIOR_CITIZEN:key",
    applicationIdempotencyKey: "webpay-statutory-discount-application:aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
    requestReference: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
    correlationId: "dddddddd-dddd-4ddd-8ddd-dddddddddddd",
    stage: "DECISION_PENDING"
  }, now);
}

describe("statutory recovery storage", () => {
  it("round-trips a versioned privacy-minimized recovery record", () => {
    const storage = new MemoryStorage();
    const record = validRecord();

    const save = saveStatutoryRecoveryRecord(record, storage);
    const load = loadStatutoryRecoveryRecord(storage, now);

    expect(save.saved).toBe(true);
    expect(load.record).toMatchObject({
      schemaVersion: statutoryRecoveryRecordVersion,
      parkingSessionId: record.parkingSessionId,
      entitlementType: "SENIOR_CITIZEN",
      statutoryDiscountDecisionCommandId: record.statutoryDiscountDecisionCommandId,
      decisionIdempotencyKey: record.decisionIdempotencyKey,
      applicationIdempotencyKey: record.applicationIdempotencyKey,
      stage: "DECISION_PENDING"
    });
  });

  it("does not persist statutory facts, monetary values, applied snapshot authority, or reviewer data", () => {
    const storage = new MemoryStorage();
    const unsafeRecord = {
      ...validRecord(),
      maskedIdReference: "SC-****-1234",
      issuingAuthority: "Sample City",
      idDocumentType: "OSCA",
      attestationNotes: "review note",
      finalPayableAmountMinorUnits: 4000,
      vatAmountMinorUnits: 480,
      statutoryDiscountAmountMinorUnits: 1000,
      appliedTariffSnapshotId: "99999999-9999-4999-8999-999999999999",
      reviewerUserId: "reviewer",
      authorization: "Bearer token"
    };

    saveStatutoryRecoveryRecord(unsafeRecord, storage);
    const serialized = storage.getItem(statutoryRecoveryStorageKey) ?? "";

    expect(serialized).not.toContain("maskedIdReference");
    expect(serialized).not.toContain("issuingAuthority");
    expect(serialized).not.toContain("idDocumentType");
    expect(serialized).not.toContain("attestationNotes");
    expect(serialized).not.toContain("finalPayableAmountMinorUnits");
    expect(serialized).not.toContain("vatAmountMinorUnits");
    expect(serialized).not.toContain("statutoryDiscountAmountMinorUnits");
    expect(serialized).not.toContain("appliedTariffSnapshotId");
    expect(serialized).not.toContain("reviewerUserId");
    expect(serialized).not.toContain("authorization");
  });

  it("rejects malformed, unsupported-version, expired, and structurally incomplete records", () => {
    const storage = new MemoryStorage();
    storage.setItem(statutoryRecoveryStorageKey, "{bad");
    expect(loadStatutoryRecoveryRecord(storage, now)).toMatchObject({ record: null, cleared: true, reason: "MALFORMED_RECORD" });

    storage.setItem(statutoryRecoveryStorageKey, JSON.stringify({ ...validRecord(), schemaVersion: 999 }));
    expect(loadStatutoryRecoveryRecord(storage, now)).toMatchObject({ record: null, cleared: true, reason: "INVALID_RECORD" });

    storage.setItem(statutoryRecoveryStorageKey, JSON.stringify({ ...validRecord(), expiresAt: "2026-07-28T07:59:59.000Z" }));
    expect(loadStatutoryRecoveryRecord(storage, now)).toMatchObject({ record: null, cleared: true, reason: "INVALID_RECORD" });

    storage.setItem(statutoryRecoveryStorageKey, JSON.stringify({ ...validRecord(), statutoryDiscountDecisionCommandId: "", decisionIdempotencyKey: "" }));
    expect(loadStatutoryRecoveryRecord(storage, now)).toMatchObject({ record: null, cleared: true, reason: "INVALID_RECORD" });
  });

  it("updates stage and payment handoff metadata without treating persisted amounts as authority", () => {
    const record = validRecord();
    const updated = updateStatutoryRecoveryRecord(record, {
      stage: "PAYMENT_HANDOFF",
      paymentAttemptId: "eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee",
      paymentIntentCorrelationId: "ffffffff-ffff-4fff-8fff-ffffffffffff"
    }, new Date("2026-07-28T08:10:00.000Z"));

    expect(updated.stage).toBe("PAYMENT_HANDOFF");
    expect(updated.paymentAttemptId).toBe("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee");
    expect(updated.paymentIntentCorrelationId).toBe("ffffffff-ffff-4fff-8fff-ffffffffffff");
    expect(JSON.stringify(updated)).not.toContain("finalPayableAmountMinorUnits");
    expect(JSON.stringify(updated)).not.toContain("appliedTariffSnapshotId");
  });

  it("clears records and reports storage-unavailable fallback safely", () => {
    const storage = new MemoryStorage();
    saveStatutoryRecoveryRecord(validRecord(), storage);
    expect(clearStatutoryRecoveryRecord(storage)).toBe(true);
    expect(loadStatutoryRecoveryRecord(storage, now).record).toBeNull();

    expect(loadStatutoryRecoveryRecord(new ThrowingStorage(), now)).toMatchObject({ record: null, unavailable: true });
    expect(saveStatutoryRecoveryRecord(validRecord(), new ThrowingStorage())).toMatchObject({ saved: false, unavailable: true });
  });

  it("classifies mutation stages that should block another tab", () => {
    expect(hasKnownInFlightStatutoryRecoveryStage({ ...validRecord(), stage: "DECISION_SUBMITTING" })).toBe(true);
    expect(hasKnownInFlightStatutoryRecoveryStage({ ...validRecord(), stage: "APPLICATION_SUBMITTING" })).toBe(true);
    expect(hasKnownInFlightStatutoryRecoveryStage({ ...validRecord(), stage: "PAYMENT_SUBMITTING" })).toBe(true);
    expect(hasKnownInFlightStatutoryRecoveryStage({ ...validRecord(), stage: "PAYABLE_READY" })).toBe(false);
  });
});
