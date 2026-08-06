import type { StatutoryDiscountEntitlementType } from "./types";

export const statutoryRecoveryRecordVersion = 1;
export const statutoryRecoveryStorageKey = "exitpass:webpay:statutory-discount-recovery:v1";
export const statutoryRecoveryLifetimeMs = 6 * 60 * 60 * 1000;

export type StatutoryRecoveryStage =
  | "DECISION_SUBMITTING"
  | "DECISION_PENDING"
  | "APPLICATION_AVAILABLE"
  | "APPLICATION_SUBMITTING"
  | "APPLICATION_PROCESSING"
  | "PAYABLE_READY"
  | "PAYMENT_SUBMITTING"
  | "PAYMENT_HANDOFF"
  | "TERMINAL";

export type WebPayStatutoryRecoveryRecord = {
  schemaVersion: typeof statutoryRecoveryRecordVersion;
  parkingSessionId: string;
  entitlementType: StatutoryDiscountEntitlementType;
  statutoryDiscountDecisionCommandId?: string;
  statutoryDiscountPayableBasisApplicationCommandId?: string;
  decisionIdempotencyKey?: string;
  applicationIdempotencyKey?: string;
  paymentIntentCorrelationId?: string;
  requestReference?: string;
  correlationId?: string;
  stage: StatutoryRecoveryStage;
  paymentAttemptId?: string;
  createdAt: string;
  updatedAt: string;
  expiresAt: string;
};

export type StatutoryRecoveryLoadResult = {
  record: WebPayStatutoryRecoveryRecord | null;
  cleared: boolean;
  unavailable: boolean;
  reason?: string;
};

type BrowserStorage = Pick<Storage, "getItem" | "setItem" | "removeItem">;

const permittedStages: ReadonlySet<string> = new Set([
  "DECISION_SUBMITTING",
  "DECISION_PENDING",
  "APPLICATION_AVAILABLE",
  "APPLICATION_SUBMITTING",
  "APPLICATION_PROCESSING",
  "PAYABLE_READY",
  "PAYMENT_SUBMITTING",
  "PAYMENT_HANDOFF",
  "TERMINAL"
]);

export function createStatutoryRecoveryRecord(
  input: Pick<WebPayStatutoryRecoveryRecord, "parkingSessionId" | "entitlementType" | "stage"> &
    Partial<Omit<WebPayStatutoryRecoveryRecord, "schemaVersion" | "parkingSessionId" | "entitlementType" | "stage" | "createdAt" | "updatedAt" | "expiresAt">>,
  now: Date = new Date()
): WebPayStatutoryRecoveryRecord {
  const timestamp = now.toISOString();
  const expiresAt = new Date(now.getTime() + statutoryRecoveryLifetimeMs).toISOString();

  return sanitizeRecord({
    ...input,
    schemaVersion: statutoryRecoveryRecordVersion,
    createdAt: timestamp,
    updatedAt: timestamp,
    expiresAt
  });
}

export function loadStatutoryRecoveryRecord(
  storage: BrowserStorage | null = getBrowserStorage(),
  now: Date = new Date()
): StatutoryRecoveryLoadResult {
  if (!storage) {
    return { record: null, cleared: false, unavailable: true, reason: "STORAGE_UNAVAILABLE" };
  }

  let raw: string | null;
  try {
    raw = storage.getItem(statutoryRecoveryStorageKey);
  } catch {
    return { record: null, cleared: false, unavailable: true, reason: "STORAGE_UNAVAILABLE" };
  }

  if (!raw) {
    return { record: null, cleared: false, unavailable: false };
  }

  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    clearStatutoryRecoveryRecord(storage);
    return { record: null, cleared: true, unavailable: false, reason: "MALFORMED_RECORD" };
  }

  const record = validateRecord(parsed, now);
  if (!record) {
    clearStatutoryRecoveryRecord(storage);
    return { record: null, cleared: true, unavailable: false, reason: "INVALID_RECORD" };
  }

  return { record, cleared: false, unavailable: false };
}

export function saveStatutoryRecoveryRecord(
  record: WebPayStatutoryRecoveryRecord,
  storage: BrowserStorage | null = getBrowserStorage()
): { saved: boolean; unavailable: boolean; record: WebPayStatutoryRecoveryRecord } {
  let sanitized = sanitizeRecord(record);
  if (!storage) {
    return { saved: false, unavailable: true, record: sanitized };
  }

  try {
    const current = loadStatutoryRecoveryRecord(storage, new Date(sanitized.updatedAt)).record;
    sanitized = preserveConcurrentPaymentStage(current, sanitized);
    storage.setItem(statutoryRecoveryStorageKey, JSON.stringify(sanitized));
    return { saved: true, unavailable: false, record: sanitized };
  } catch {
    return { saved: false, unavailable: true, record: sanitized };
  }
}

function preserveConcurrentPaymentStage(
  current: WebPayStatutoryRecoveryRecord | null,
  next: WebPayStatutoryRecoveryRecord
): WebPayStatutoryRecoveryRecord {
  if (!current || current.parkingSessionId !== next.parkingSessionId) {
    return next;
  }

  if (current.stage === "PAYMENT_SUBMITTING" && next.stage !== "PAYMENT_HANDOFF" && next.stage !== "TERMINAL") {
    return sanitizeRecord({
      ...next,
      stage: current.stage,
      paymentIntentCorrelationId: current.paymentIntentCorrelationId ?? next.paymentIntentCorrelationId,
      paymentAttemptId: current.paymentAttemptId ?? next.paymentAttemptId
    });
  }

  if (current.stage === "PAYMENT_HANDOFF" && next.stage !== "TERMINAL") {
    return sanitizeRecord({
      ...next,
      stage: current.stage,
      paymentIntentCorrelationId: current.paymentIntentCorrelationId ?? next.paymentIntentCorrelationId,
      paymentAttemptId: current.paymentAttemptId ?? next.paymentAttemptId
    });
  }

  return current.stage === "TERMINAL" ? current : next;
}

export function updateStatutoryRecoveryRecord(
  current: WebPayStatutoryRecoveryRecord,
  patch: Partial<Omit<WebPayStatutoryRecoveryRecord, "schemaVersion" | "createdAt" | "expiresAt">>,
  now: Date = new Date()
): WebPayStatutoryRecoveryRecord {
  return sanitizeRecord({
    ...current,
    ...patch,
    schemaVersion: statutoryRecoveryRecordVersion,
    createdAt: current.createdAt,
    updatedAt: now.toISOString(),
    expiresAt: new Date(now.getTime() + statutoryRecoveryLifetimeMs).toISOString()
  });
}

export function clearStatutoryRecoveryRecord(storage: BrowserStorage | null = getBrowserStorage()): boolean {
  if (!storage) {
    return false;
  }

  try {
    storage.removeItem(statutoryRecoveryStorageKey);
    return true;
  } catch {
    return false;
  }
}

export function subscribeStatutoryRecoveryRecord(
  listener: (record: WebPayStatutoryRecoveryRecord | null) => void,
  win: Window | undefined = typeof window === "undefined" ? undefined : window
): () => void {
  if (!win?.addEventListener) {
    return () => undefined;
  }

  const onStorage = (event: StorageEvent) => {
    if (event.key !== statutoryRecoveryStorageKey) {
      return;
    }

    const loaded = loadStatutoryRecoveryRecord(win.localStorage);
    listener(loaded.record);
  };

  win.addEventListener("storage", onStorage);
  return () => win.removeEventListener("storage", onStorage);
}

export function hasKnownInFlightStatutoryRecoveryStage(record: WebPayStatutoryRecoveryRecord | null): boolean {
  return record?.stage === "DECISION_SUBMITTING" ||
    record?.stage === "APPLICATION_SUBMITTING" ||
    record?.stage === "PAYMENT_SUBMITTING";
}

function validateRecord(value: unknown, now: Date): WebPayStatutoryRecoveryRecord | null {
  if (!value || typeof value !== "object") {
    return null;
  }

  const source = value as Record<string, unknown>;
  if (source.schemaVersion !== statutoryRecoveryRecordVersion) {
    return null;
  }

  const parkingSessionId = getSafeString(source.parkingSessionId);
  const entitlementType = getEntitlementType(source.entitlementType);
  const stage = getStage(source.stage);
  const createdAt = getSafeDateString(source.createdAt);
  const updatedAt = getSafeDateString(source.updatedAt);
  const expiresAt = getSafeDateString(source.expiresAt);

  if (!parkingSessionId || !entitlementType || !stage || !createdAt || !updatedAt || !expiresAt) {
    return null;
  }

  if (new Date(expiresAt).getTime() <= now.getTime()) {
    return null;
  }

  const hasDecisionId = Boolean(getSafeString(source.statutoryDiscountDecisionCommandId));
  const hasDecisionKey = Boolean(getSafeString(source.decisionIdempotencyKey));
  if (!hasDecisionId && !hasDecisionKey) {
    return null;
  }

  return sanitizeRecord({
    schemaVersion: statutoryRecoveryRecordVersion,
    parkingSessionId,
    entitlementType,
    statutoryDiscountDecisionCommandId: getSafeString(source.statutoryDiscountDecisionCommandId),
    statutoryDiscountPayableBasisApplicationCommandId: getSafeString(source.statutoryDiscountPayableBasisApplicationCommandId),
    decisionIdempotencyKey: getSafeString(source.decisionIdempotencyKey),
    applicationIdempotencyKey: getSafeString(source.applicationIdempotencyKey),
    paymentIntentCorrelationId: getSafeString(source.paymentIntentCorrelationId),
    requestReference: getSafeString(source.requestReference),
    correlationId: getSafeString(source.correlationId),
    stage,
    paymentAttemptId: getSafeString(source.paymentAttemptId),
    createdAt,
    updatedAt,
    expiresAt
  });
}

function sanitizeRecord(record: WebPayStatutoryRecoveryRecord): WebPayStatutoryRecoveryRecord {
  const sanitized: WebPayStatutoryRecoveryRecord = {
    schemaVersion: statutoryRecoveryRecordVersion,
    parkingSessionId: record.parkingSessionId.trim(),
    entitlementType: record.entitlementType,
    stage: record.stage,
    createdAt: record.createdAt,
    updatedAt: record.updatedAt,
    expiresAt: record.expiresAt
  };

  setOptionalString(sanitized, "statutoryDiscountDecisionCommandId", record.statutoryDiscountDecisionCommandId);
  setOptionalString(sanitized, "statutoryDiscountPayableBasisApplicationCommandId", record.statutoryDiscountPayableBasisApplicationCommandId);
  setOptionalString(sanitized, "decisionIdempotencyKey", record.decisionIdempotencyKey);
  setOptionalString(sanitized, "applicationIdempotencyKey", record.applicationIdempotencyKey);
  setOptionalString(sanitized, "paymentIntentCorrelationId", record.paymentIntentCorrelationId);
  setOptionalString(sanitized, "requestReference", record.requestReference);
  setOptionalString(sanitized, "correlationId", record.correlationId);
  setOptionalString(sanitized, "paymentAttemptId", record.paymentAttemptId);

  return sanitized;
}

function setOptionalString<T extends keyof WebPayStatutoryRecoveryRecord>(
  target: WebPayStatutoryRecoveryRecord,
  key: T,
  value: WebPayStatutoryRecoveryRecord[T]
) {
  if (typeof value === "string" && value.trim()) {
    (target as Record<string, unknown>)[key] = value.trim();
  }
}

function getBrowserStorage(): BrowserStorage | null {
  try {
    return globalThis.localStorage ?? null;
  } catch {
    return null;
  }
}

function getEntitlementType(value: unknown): StatutoryDiscountEntitlementType | null {
  return value === "SENIOR_CITIZEN" || value === "PWD" ? value : null;
}

function getStage(value: unknown): StatutoryRecoveryStage | null {
  return typeof value === "string" && permittedStages.has(value) ? value as StatutoryRecoveryStage : null;
}

function getSafeString(value: unknown): string | undefined {
  if (typeof value !== "string") {
    return undefined;
  }

  const trimmed = value.trim();
  return trimmed.length > 0 && trimmed.length <= 256 && !/[\r\n]/.test(trimmed) ? trimmed : undefined;
}

function getSafeDateString(value: unknown): string | undefined {
  const safe = getSafeString(value);
  if (!safe) {
    return undefined;
  }

  const timestamp = new Date(safe).getTime();
  return Number.isFinite(timestamp) ? safe : undefined;
}
