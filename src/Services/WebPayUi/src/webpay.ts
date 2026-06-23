import type {
  ActivePaymentAttemptState,
  ApiError,
  ParkingSessionResolveRequest,
  ParkingSessionResolveResponse,
  PaymentIntentRequest,
  PaymentIntentResponse,
  WebPayHandoff
} from "./types";

const paymentIntentPath = "/v1/webpay/payment-intents";
const parkingSessionResolvePath = "/v1/webpay/parking-session";
const activePaymentAttemptErrorCode = "ACTIVE_PAYMENT_ATTEMPT_EXISTS";
const refreshRequiredErrorCode = "PAYABLE_BASIS_REFRESH_REQUIRED";

export class ActivePaymentAttemptError extends Error {
  public readonly activePaymentAttempt: ActivePaymentAttemptState;

  public constructor(activePaymentAttempt: ActivePaymentAttemptState) {
    super(activePaymentAttempt.message);
    this.name = "ActivePaymentAttemptError";
    this.activePaymentAttempt = activePaymentAttempt;
  }
}

export class PayableBasisRefreshRequiredError extends Error {
  public readonly errorCode?: string;
  public readonly correlationId?: string;

  public constructor(errorCode: string | undefined, message: string, correlationId?: string) {
    super(message);
    this.name = "PayableBasisRefreshRequiredError";
    this.errorCode = errorCode;
    this.correlationId = correlationId;
  }
}

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

export function createCorrelationId(): string {
  return globalThis.crypto?.randomUUID?.() ?? `webpay-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

function withCorrelationId<TRequest extends object>(request: TRequest): TRequest & { correlationId: string } {
  const existing = (request as { correlationId?: unknown }).correlationId;
  const correlationId = typeof existing === "string" && existing.trim() ? existing.trim() : createCorrelationId();

  return {
    ...request,
    correlationId
  };
}

function jsonHeaders(correlationId: string): HeadersInit {
  return {
    "Content-Type": "application/json",
    "X-Correlation-Id": correlationId
  };
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
  /*
   * ExitPass v1.2 BRD 18.3 Payment Initiation.
   * ExitPass v1.2 SDD 10.2.4 Initiate Payment Attempt.
   * Invariant: WebPay customer-facing methods may initiate only through the PayMongo-backed server route.
   */
  if (!["QRPH", "GCASH", "MAYA", "CARD"].includes(request.paymentMethod)) {
    throw new Error("Only QRPh, GCash, Maya, and Card payment through PayMongo Checkout are available right now.");
  }

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

  if (request.tariffSnapshotId?.trim()) {
    body.tariffSnapshotId = request.tariffSnapshotId.trim();
  }

  if (Number.isFinite(request.expectedAmountMinorUnits)) {
    body.expectedAmountMinorUnits = request.expectedAmountMinorUnits;
  }

  if (request.correlationId?.trim()) {
    body.correlationId = request.correlationId.trim();
  }

  return body;
}

export function buildParkingSessionResolveBody(
  request: ParkingSessionResolveRequest,
  defaultContext: WebPaySiteContext = getDefaultSiteContext()
): ParkingSessionResolveRequest {
  const body = buildPaymentIntentBody(
    {
      ...request,
      paymentMethod: "QRPH"
    },
    defaultContext
  );

  const { paymentMethod: _, ...resolveBody } = body;
  return resolveBody;
}

export async function resolveParkingSession(
  request: ParkingSessionResolveRequest,
  fetchImpl: typeof fetch = fetch
): Promise<ParkingSessionResolveResponse> {
  const body = buildParkingSessionResolveBody(request);
  if (!body.vendorSystemId) {
    throw new Error("WebPay is missing vendor configuration. Set VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID for local testing.");
  }

  const correlatedBody = withCorrelationId(body);

  const response = await fetchImpl(`${getApiBaseUrl()}${parkingSessionResolvePath}`, {
    method: "POST",
    headers: jsonHeaders(correlatedBody.correlationId),
    body: JSON.stringify(correlatedBody)
  });

  const payload = (await response.json().catch(() => ({}))) as ParkingSessionResolveResponse | ApiError;
  if (!response.ok) {
    const error = payload as ApiError;
    throw new Error(toFriendlyError(error.errorCode, error.message));
  }

  return payload as ParkingSessionResolveResponse;
}

export async function retrievePaymentStatus(
  request: ParkingSessionResolveRequest,
  fetchImpl: typeof fetch = fetch
): Promise<ParkingSessionResolveResponse> {
  /*
   * ExitPass v1.2 BRD 18.3 Payment Initiation.
   * ExitPass v1.2 SDD 10.2.4 Initiate Payment Attempt.
   * Invariant: WebPay status display is read-only and must derive payment state from server-side
   * Central PMS status, never from checkout redirect state.
   */
  return resolveParkingSession(request, fetchImpl);
}

export async function createPaymentIntent(
  request: PaymentIntentRequest,
  fetchImpl: typeof fetch = fetch,
  defaultContext?: WebPaySiteContext
): Promise<PaymentIntentResponse> {
  const body = buildPaymentIntentBody(request, defaultContext);
  if (!body.vendorSystemId) {
    throw new Error("WebPay is missing vendor configuration. Set VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID for local testing.");
  }

  const correlatedBody = withCorrelationId(body);

  const response = await fetchImpl(`${getApiBaseUrl()}${paymentIntentPath}`, {
    method: "POST",
    headers: jsonHeaders(correlatedBody.correlationId),
    body: JSON.stringify(correlatedBody)
  });

  const payload = (await response.json().catch(() => ({}))) as PaymentIntentResponse | ApiError;
  if (!response.ok) {
    const error = payload as ApiError;
    if (isActivePaymentAttemptConflict(response.status, error)) {
      throw new ActivePaymentAttemptError({
        kind: "active-payment-attempt",
        message:
          error.message?.trim() ||
          "You already have an active payment for this parking session. Continue your existing payment or check its status.",
        correlationId: error.correlationId,
        parkingSessionId: error.parkingSessionId,
        paymentAttemptId: error.paymentAttemptId,
        status: error.status,
        handoff: normalizeHandoff(error),
        statusUrl: error.statusUrl,
        checkStatusUrl: error.checkStatusUrl,
        paymentMethod: error.paymentMethod,
        amountMinorUnits: error.amountMinorUnits,
        currency: error.currency,
        siteName: error.siteName,
        ticketReference: error.ticketReference,
        plateNumber: error.plateNumber
      });
    }

    if (isPayableBasisRefreshRequired(error)) {
      throw new PayableBasisRefreshRequiredError(
        error.errorCode,
        toFriendlyError(error.errorCode, error.message),
        error.correlationId);
    }

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
    case "PAYMENT_ATTEMPT_NOT_FOUND":
    case "UNKNOWN_PAYMENT_ATTEMPT":
      return "We could not find an active parking session for those details.";
    case "VENDOR_UNAVAILABLE":
    case "VENDOR_PARKING_RESOLUTION_FAILED":
      return "Parking lookup is temporarily unavailable. Please try again shortly.";
    case "PAYMENT_PROVIDER_ROUTE_NOT_AVAILABLE":
    case "NO_PAYMENT_ROUTE":
    case "NO_ROUTE":
    case "UNSUPPORTED_PAYMENT_METHOD":
      return "This payment method is not available right now. Please choose another method.";
    case "AMBIGUOUS_MATCH":
      return "Multiple matching parking sessions were found. Please use the ticket reference instead.";
    case "TARIFF_CALCULATION_FAILED":
    case "TARIFF_SNAPSHOT_NOT_FOUND":
    case "TARIFF_SNAPSHOT_INVALID":
      return "We could not calculate the parking fee. Please try again shortly.";
    case "PAYABLE_BASIS_REFRESH_REQUIRED":
      return "Your parking fee quote has expired. Please recalculate the fee to continue.";
    case "WEBPAY_PAYMENT_INTENT_FAILED":
    case "PAYMENT_PROVIDER_CONFIGURATION_ERROR":
      return "We could not start payment. Please try again.";
    case "STATUTORY_DISCOUNT_PENDING_REVIEW":
    case "PENDING_REVIEW":
      return "Statutory discount validation is pending review.";
    case "STATUTORY_DISCOUNT_REJECTED":
    case "REJECTED":
      return "Statutory discount validation was rejected.";
    case "STATUTORY_DISCOUNT_EXPIRED":
      return "Statutory discount validation has expired.";
    case "PAYABLE_BASIS_LOCKED":
      return "The payable amount changed or payment has already started. Please restart from lookup.";
    case "PAYMENT_ALREADY_INITIATED":
      return "Payment has already been initiated for this payable amount.";
    default:
      return message?.trim() || "Payment intent creation failed. Please try again.";
  }
}

function isPayableBasisRefreshRequired(error: ApiError): boolean {
  return (error.errorCode ?? "").toUpperCase() === refreshRequiredErrorCode;
}

export function formatAmount(amountMinorUnits: number, currency: string): string {
  const normalizedCurrency = currency || "PHP";
  if (normalizedCurrency.toUpperCase() === "PHP") {
    return new Intl.NumberFormat("en-PH", {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2
    }).format((amountMinorUnits || 0) / 100);
  }

  return new Intl.NumberFormat("en-PH", {
    style: "currency",
    currency: normalizedCurrency
  }).format((amountMinorUnits || 0) / 100);
}

export function isActivePaymentAttemptConflict(statusCode: number, error: ApiError): boolean {
  return statusCode === 409 && (error.errorCode ?? "").toUpperCase() === activePaymentAttemptErrorCode;
}

export function getResumeUrl(handoff?: WebPayHandoff | null): string | undefined {
  return firstNonBlank(handoff?.resumePaymentUrl ?? undefined, handoff?.handoffUrl ?? undefined, handoff?.checkoutUrl ?? undefined);
}

function normalizeHandoff(error: ApiError): WebPayHandoff | null {
  const handoff = error.handoff ?? {};
  const resumePaymentUrl = firstNonBlank(error.resumePaymentUrl ?? undefined, handoff.resumePaymentUrl ?? undefined);
  const handoffUrl = firstNonBlank(error.handoffUrl ?? undefined, handoff.handoffUrl ?? undefined);
  const checkoutUrl = firstNonBlank(error.checkoutUrl ?? undefined, handoff.checkoutUrl ?? undefined);

  if (!resumePaymentUrl && !handoffUrl && !checkoutUrl && !handoff.qrCodeUrl && !handoff.expiresAt && !handoff.type) {
    return null;
  }

  return {
    ...handoff,
    resumePaymentUrl,
    handoffUrl,
    checkoutUrl
  };
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
