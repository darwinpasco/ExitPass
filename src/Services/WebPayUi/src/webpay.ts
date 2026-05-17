import type { ApiError, PaymentIntentRequest, PaymentIntentResponse } from "./types";

const paymentIntentPath = "/v1/webpay/payment-intents";

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

export function getApiBaseUrl(): string {
  return (import.meta.env.VITE_WEBPAY_API_BASE_URL ?? "").replace(/\/+$/, "");
}

export function buildPaymentIntentBody(request: PaymentIntentRequest): PaymentIntentRequest {
  const body: PaymentIntentRequest = {
    paymentMethod: request.paymentMethod
  };

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
  const response = await fetchImpl(`${getApiBaseUrl()}${paymentIntentPath}`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json"
    },
    body: JSON.stringify(buildPaymentIntentBody(request))
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
