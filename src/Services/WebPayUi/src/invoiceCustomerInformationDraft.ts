import { emptyInvoiceCustomerInformation, invoiceCustomerInformationLimits } from "./invoiceCustomerInformation";
import type { InvoiceCustomerInformation } from "./types";

const draftKeyPrefix = "exitpass.webpay.invoice-customer-information.v1:";

type SessionStorageLike = Pick<Storage, "getItem" | "setItem" | "removeItem">;

export function loadInvoiceCustomerInformationDraft(
  parkingSessionId: string,
  storage: SessionStorageLike | null = getSessionStorage()
): InvoiceCustomerInformation | null {
  if (!storage || !parkingSessionId.trim()) return null;

  try {
    const raw = storage.getItem(draftKey(parkingSessionId));
    if (!raw) return null;
    const parsed = JSON.parse(raw) as unknown;
    const draft = parseDraft(parsed);
    if (!draft) storage.removeItem(draftKey(parkingSessionId));
    return draft;
  } catch {
    return null;
  }
}

export function saveInvoiceCustomerInformationDraft(
  parkingSessionId: string,
  value: InvoiceCustomerInformation,
  storage: SessionStorageLike | null = getSessionStorage()
): boolean {
  if (!storage || !parkingSessionId.trim()) return false;

  try {
    storage.setItem(draftKey(parkingSessionId), JSON.stringify({ schemaVersion: 1, value }));
    return true;
  } catch {
    return false;
  }
}

export function clearInvoiceCustomerInformationDraft(
  parkingSessionId: string,
  storage: SessionStorageLike | null = getSessionStorage()
): void {
  if (!storage || !parkingSessionId.trim()) return;
  try {
    storage.removeItem(draftKey(parkingSessionId));
  } catch {
    // Session storage can be unavailable in restricted browser contexts.
  }
}

function draftKey(parkingSessionId: string): string {
  return `${draftKeyPrefix}${encodeURIComponent(parkingSessionId.trim())}`;
}

function parseDraft(value: unknown): InvoiceCustomerInformation | null {
  if (!value || typeof value !== "object") return null;
  const record = value as { schemaVersion?: unknown; value?: unknown };
  if (record.schemaVersion !== 1 || !record.value || typeof record.value !== "object") return null;

  const candidate = record.value as Record<string, unknown>;
  const fields = Object.keys(emptyInvoiceCustomerInformation) as Array<keyof InvoiceCustomerInformation>;
  const result = { ...emptyInvoiceCustomerInformation };
  for (const field of fields) {
    const fieldValue = candidate[field];
    if (typeof fieldValue !== "string" || fieldValue.length > invoiceCustomerInformationLimits[field]) return null;
    result[field] = fieldValue;
  }
  return result;
}

function getSessionStorage(): Storage | null {
  try {
    return typeof window === "undefined" ? null : window.sessionStorage;
  } catch {
    return null;
  }
}
