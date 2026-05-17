import type { ApiError, PaymentIntentRequest, PaymentIntentResponse } from "./types";

const paymentIntentPath = "/v1/webpay/payment-intents";

type WebPaySiteContext = Pick<PaymentIntentRequest, "siteGroupId" | "siteId" | "vendorSystemId">;

export function normalizeTicketReference(rawValue: string): string {
  const value = rawValue.trim();
  if (!value) {
    return "";
  }

  try {
    const parsed = JSON.parse(value) as Record<string, unknown>;
    const ticketValue = parsed.ticketReference ?? parsed.ticket_reference ?? parsed.ticket ?? parsed.ref;
    if (typeof ticketValue === "string") {
      return ticketValue.trim();
    }
  } catch {
    // Not a JSON QR payload; continue with URL and plain text handling.
  }

  try {
    const url = new URL(value);
    const ticketValue =
      url.searchParams.get("ticketReference") ??
      url.searchParams.get("ticket_reference") ??
      url.searchParams.get("ticket") ??
      url.searchParams.get("ref");
    if (ticketValue) {
      return ticketValue.trim();
    }
  } catch {
    // Not a URL QR payload; use the scanned text as the ticket reference.
  }

  return value;
}

export function extractPaymentIntentContext(rawValue: string): WebPaySiteContext {
  const value = rawValue.trim();
  if (!value) {
    return {};
  }

  try {
    const parsed = JSON.parse(value) as Record<string, unknown>;
    return {
      siteGroupId: getStringValue(parsed, "siteGroupId", "site_group_id"),
      siteId: getStringValue(parsed, "siteId", "site_id"),
      vendorSystemId: getStringValue(parsed, "vendorSystemId", "vendor_system_id", "vendor")
    };
  } catch {
    // Not a JSON QR payload; continue with URL context handling.
  }

  try {
    const url = new URL(value);
    return {
      siteGroupId: getQueryValue(url, "siteGroupId", "site_group_id"),
      siteId: getQueryValue(url, "siteId", "site_id"),
      vendorSystemId: getQueryValue(url, "vendorSystemId", "vendor_system_id", "vendor")
    };
  } catch {
    return {};
  }
}

export function getApiBaseUrl(): string {
  return (import.meta.env.VITE_WEBPAY_API_BASE_URL ?? "").replace(/\/+$/, "");
}

export function getDefaultSiteContext(): WebPaySiteContext {
  return {
    siteGroupId: (import.meta.env.VITE_WEBPAY_DEFAULT_SITE_GROUP_ID ?? "").trim() || undefined,
    siteId: (import.meta.env.VITE_WEBPAY_DEFAULT_SITE_ID ?? "").trim() || undefined,
    vendorSystemId: (import.meta.env.VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID ?? "").trim() || undefined
  };
}

export function buildPaymentIntentBody(
  request: PaymentIntentRequest,
  defaultContext: WebPaySiteContext = getDefaultSiteContext()
): PaymentIntentRequest {
  const body: PaymentIntentRequest = {
    paymentMethod: request.paymentMethod
  };

  const siteGroupId = firstNonBlank(request.siteGroupId, defaultContext.siteGroupId);
  const siteId = firstNonBlank(request.siteId, defaultContext.siteId);
  const vendorSystemId = firstNonBlank(request.vendorSystemId, defaultContext.vendorSystemId);

  if (siteGroupId) {
    body.siteGroupId = siteGroupId;
  }

  if (siteId) {
    body.siteId = siteId;
  }

  if (vendorSystemId) {
    body.vendorSystemId = vendorSystemId;
  }

  if (request.ticketReference?.trim()) {
    body.ticketReference = request.ticketReference.trim();
  }

  if (request.plateNumber?.trim()) {
    body.plateNumber = request.plateNumber.trim().toUpperCase();
  }

  return body;
}

export async function createPaymentIntent(
  request: PaymentIntentRequest,
  fetchImpl: typeof fetch = fetch
): Promise<PaymentIntentResponse> {
  const body = buildPaymentIntentBody(request);
  if (!body.vendorSystemId) {
    throw new Error("WebPay is missing vendor configuration. Set VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID for local testing.");
  }

  const response = await fetchImpl(`${getApiBaseUrl()}${paymentIntentPath}`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json"
    },
    body: JSON.stringify(body)
  });

  const payload = (await response.json().catch(() => ({}))) as PaymentIntentResponse | ApiError;
  if (!response.ok) {
    const error = payload as ApiError;
    throw new Error(toFriendlyError(error.errorCode, error.message));
  }

  return payload as PaymentIntentResponse;
}

export function toFriendlyError(errorCode?: string, message?: string): string {
  switch ((errorCode ?? "").toUpperCase()) {
    case "INVALID_TICKET":
    case "INVALID_PLATE":
    case "INVALID_TICKET_OR_PLATE":
    case "VALIDATION_FAILED":
      return "Check the ticket reference or plate number and try again.";
    case "SESSION_NOT_FOUND":
    case "PARKING_SESSION_NOT_FOUND":
      return "We could not find an active parking session for those details.";
    case "VENDOR_UNAVAILABLE":
    case "VENDOR_PARKING_RESOLUTION_FAILED":
      return "Parking lookup is temporarily unavailable. Please try again shortly.";
    case "PAYMENT_PROVIDER_ROUTE_NOT_AVAILABLE":
    case "NO_PAYMENT_ROUTE":
    case "NO_ROUTE":
      return "This payment method is not available right now. Please choose another method.";
    case "WEBPAY_PAYMENT_INTENT_FAILED":
      return "We could not start payment. Please try again.";
    default:
      return message?.trim() || "Payment intent creation failed. Please try again.";
  }
}

export function formatAmount(amountMinorUnits: number, currency: string): string {
  return new Intl.NumberFormat("en-PH", {
    style: "currency",
    currency: currency || "PHP"
  }).format((amountMinorUnits || 0) / 100);
}

function firstNonBlank(...values: Array<string | undefined>): string | undefined {
  for (const value of values) {
    const trimmed = value?.trim();
    if (trimmed) {
      return trimmed;
    }
  }

  return undefined;
}

function getStringValue(source: Record<string, unknown>, ...keys: string[]): string | undefined {
  for (const key of keys) {
    const value = source[key];
    if (typeof value === "string" && value.trim()) {
      return value.trim();
    }
  }

  return undefined;
}

function getQueryValue(url: URL, ...keys: string[]): string | undefined {
  for (const key of keys) {
    const value = url.searchParams.get(key);
    if (value?.trim()) {
      return value.trim();
    }
  }

  return undefined;
}
