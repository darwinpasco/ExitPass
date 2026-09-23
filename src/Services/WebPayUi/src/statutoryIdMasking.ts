export const statutoryIdMinimumLength = 4;
export const statutoryIdMaximumLength = 64;

export type StatutoryIdMaskResult =
  | { ok: true; normalizedValue: string; maskedValue: string }
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

  if (normalized.length > statutoryIdMaximumLength) {
    return {
      ok: false,
      message: "Enter no more than 64 characters for the ID No. / Control No."
    };
  }

  return {
    ok: true,
    normalizedValue: normalized,
    maskedValue: normalized.length <= 4
      ? normalized
      : `${"*".repeat(normalized.length - 4)}${normalized.slice(-4)}`
  };
}

export function isAutomaticallyMaskedStatutoryIdReference(value: string): boolean {
  const normalized = value.trim();
  return /^\*+[A-Za-z0-9-]{4}$/.test(normalized);
}
