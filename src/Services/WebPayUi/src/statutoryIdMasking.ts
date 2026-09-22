export const statutoryIdMinimumLength = 4;

export type StatutoryIdMaskResult =
  | { ok: true; maskedValue: string }
  | { ok: false; message: string };

const allowedStatutoryIdReference = /^[A-Za-z0-9-]+$/;

export function maskStatutoryIdReference(value: string): StatutoryIdMaskResult {
  const normalized = value.trim();

  if (!normalized) {
    return { ok: false, message: "Enter the ID reference." };
  }

  if (normalized.includes("*")) {
    return {
      ok: false,
      message: "Enter the ID reference without asterisks. WebPay masks it automatically."
    };
  }

  if (!allowedStatutoryIdReference.test(normalized)) {
    return {
      ok: false,
      message: "Use letters, numbers, and hyphens only."
    };
  }

  if (normalized.length < statutoryIdMinimumLength) {
    return {
      ok: false,
      message: "Enter at least 4 characters for the ID No. / Control No."
    };
  }

  return {
    ok: true,
    maskedValue: normalized.length <= 6
      ? "*".repeat(normalized.length)
      : `${normalized.slice(0, 2)}${"*".repeat(normalized.length - 6)}${normalized.slice(-4)}`
  };
}

export function isAutomaticallyMaskedStatutoryIdReference(value: string): boolean {
  const normalized = value.trim();
  return /^\*{4,6}$/.test(normalized) || /^[A-Za-z0-9-]{2}\*+[A-Za-z0-9-]{4}$/.test(normalized);
}
