import { createServer } from "node:http";
import { readFile } from "node:fs/promises";
import { extname, join, normalize } from "node:path";
import { fileURLToPath } from "node:url";

const port = Number(process.env.WEBPAY_BROWSER_SMOKE_PORT ?? 5196);
const root = normalize(join(fileURLToPath(new URL(".", import.meta.url)), "..", ".."));
const distRoot = normalize(join(root, "dist"));
const paymentAttemptIds = {
  available: "10000000-0000-4000-8000-000000000001",
  pending: "10000000-0000-4000-8000-000000000002",
  temporarilyUnavailable: "10000000-0000-4000-8000-000000000003",
  terminalFailure: "10000000-0000-4000-8000-000000000004",
  refreshPending: "10000000-0000-4000-8000-000000000005"
};

let requestLog = [];
let receiptAttempts = {};
let statutoryReadAttempts = {};
let statutoryApplyAttempts = {};
let paymentIntentAttempts = {};
let statutoryScenarioByDecisionId = {};
let ambiguousDecisionAttempts = {};
const statutoryDecisionCommandId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";

const contentTypes = {
  ".css": "text/css; charset=utf-8",
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".png": "image/png",
  ".svg": "image/svg+xml"
};

function resetState() {
  requestLog = [];
  receiptAttempts = {};
  statutoryReadAttempts = {};
  statutoryApplyAttempts = {};
  paymentIntentAttempts = {};
  statutoryScenarioByDecisionId = {};
  ambiguousDecisionAttempts = {};
}

function writeJson(response, statusCode, body) {
  const payload = JSON.stringify(body);
  response.writeHead(statusCode, {
    "Content-Type": "application/json; charset=utf-8",
    "Content-Length": Buffer.byteLength(payload)
  });
  response.end(payload);
}

async function readJson(request) {
  const chunks = [];
  for await (const chunk of request) {
    chunks.push(chunk);
  }

  if (chunks.length === 0) {
    return {};
  }

  return JSON.parse(Buffer.concat(chunks).toString("utf8"));
}

function recordRequest(request, body) {
  requestLog.push({
    method: request.method,
    path: new URL(request.url, `http://${request.headers.host}`).pathname,
    headers: {
      "x-correlation-id": request.headers["x-correlation-id"],
      "idempotency-key": request.headers["idempotency-key"],
      "x-posserver-admin-key": request.headers["x-posserver-admin-key"],
      "x-posserver-admin-permission": request.headers["x-posserver-admin-permission"]
    },
    body
  });
}

function paymentStatusForTicket(ticketReference) {
  if (ticketReference.startsWith("WEBPAY-STAT-")) {
    return "Not Started";
  }

  if (ticketReference === "WEBPAY-SMOKE-FAILED") {
    return "Failed";
  }

  return "Paid";
}

function parkingStatusForTicket(ticketReference) {
  if (ticketReference.startsWith("WEBPAY-STAT-")) {
    return "PaymentRequired";
  }

  return ticketReference === "WEBPAY-SMOKE-FAILED" ? "PaymentRequired" : "Payment Completed";
}

function buildSessionResponse(ticketReference, correlationId) {
  return {
    paymentAttemptId: paymentAttemptIds.available,
    parkingSessionId: "20000000-0000-4000-8000-000000000001",
    tariffSnapshotId: "30000000-0000-4000-8000-000000000001",
    siteGroupId: "40000000-0000-4000-8000-000000000001",
    siteId: "50000000-0000-4000-8000-000000000001",
    vendorSystemId: "60000000-0000-4000-8000-000000000001",
    siteGroupName: "Browser Smoke Site Group",
    siteName: "Browser Smoke Site",
    amountMinorUnits: 12900,
    currency: "PHP",
    ticketReference,
    plateNumber: "SMK 0001",
    entryTime: "2026-07-21T08:00:00+08:00",
    currentFeeCalculationTime: "2026-07-21T10:00:00+08:00",
    durationParked: "2h 0m",
    tariffName: "Controlled Browser Smoke Tariff",
    feeValidUntil: "2026-07-21T10:15:00+08:00",
    parkingStatus: parkingStatusForTicket(ticketReference),
    paymentStatus: paymentStatusForTicket(ticketReference),
    paymentMethod: "QRPH",
    selectedProviderCode: "PAYMONGO",
    fallbackProviderCode: null,
    routingReason: "PRIMARY_PROVIDER",
    exitInstruction:
      ticketReference === "WEBPAY-ONLY-PAYMENT-MARKER"
        ? {
            status: "ISSUED",
            message: "CENTRAL PMS EXIT AUTHORIZATION MARKER",
            laneName: "Controlled Exit",
            exitBy: "2026-07-21T10:30:00+08:00"
          }
        : null,
    exitAuthorizationStatus: ticketReference === "WEBPAY-ONLY-PAYMENT-MARKER" ? "ISSUED" : null,
    handoff: null,
    correlationId
  };
}

function buildStatutoryAvailabilityResponse(body, correlationId) {
  return {
    requestReference: body.requestReference ?? "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
    parkingSessionId: body.parkingSessionId ?? "20000000-0000-4000-8000-000000000001",
    siteId: body.siteId ?? "50000000-0000-4000-8000-000000000001",
    siteGroupId: body.siteGroupId ?? "40000000-0000-4000-8000-000000000001",
    availabilityStatus: "AVAILABLE",
    statutoryParkingBenefitAvailable: true,
    coveredEntitlementTypes: ["SENIOR_CITIZEN", "PWD"],
    requestedEntitlementType: body.requestedEntitlementType ?? null,
    safeReasonCode: null,
    retryable: false,
    remediationAction: "CONTINUE_WITH_ORDINARY_PAYMENT",
    requiredEvidenceTypes: [],
    correlationId
  };
}

function buildPresentationResponse(paymentAttemptId, correlationId) {
  return {
    paymentAttemptId,
    paymentConfirmationId: "70000000-0000-4000-8000-000000000001",
    fiscalIssuanceReferenceId: "80000000-0000-4000-8000-000000000001",
    fiscalIssuanceState: "FISCAL_ISSUANCE_RECORDED",
    posFiscalDocumentId: "90000000-0000-4000-8000-000000000001",
    fiscalDocumentNumber: "SI-WEBPAY-BROWSER-SMOKE-0001",
    fiscalDocumentStatus: "RECORDED",
    receiptAvailabilityState: "AVAILABLE",
    presentationVersion: "digital-sales-invoice-presentation-json-v1",
    templateVersion: "digital-sales-invoice-json-v1",
    contentType: "application/json",
    authoritativePresentation: {
      presentation: {
        documentTitle: "Sales Invoice",
        sections: [
          {
            key: "summary",
            title: "Document Summary",
            rows: [
              { key: "number", label: "Sales Invoice Number", displayValue: "SI-WEBPAY-BROWSER-SMOKE-0001" },
              { key: "marker", label: "Business Marker", displayValue: "POS SERVER AUTHORITATIVE PRESENTATION" },
              { key: "tender", label: "Tender Marker", displayValue: "CASHLESS" }
            ]
          }
        ]
      }
    },
    createdAt: "2026-07-21T10:01:00+08:00",
    updatedAt: "2026-07-21T10:02:00+08:00",
    correlationId
  };
}

function statutoryScenarioForTicket(ticketReference, entitlementType) {
  if (ticketReference === "WEBPAY-STAT-APP-REQ") {
    return "application-required";
  }

  if (ticketReference === "WEBPAY-STAT-APPLY-PROCESSING") {
    return "apply-processing";
  }

  if (ticketReference === "WEBPAY-STAT-APPLY-READY") {
    return "apply-ready";
  }

  if (ticketReference === "WEBPAY-STAT-APPLY-RETRYABLE") {
    return "apply-retryable";
  }

  if (ticketReference === "WEBPAY-STAT-APPLY-CONFLICT") {
    return "apply-conflict";
  }

  if (ticketReference === "WEBPAY-STAT-APPLY-TERMINAL") {
    return "apply-terminal";
  }

  if (ticketReference === "WEBPAY-STAT-REJECTED") {
    return "rejected";
  }

  if (ticketReference === "WEBPAY-STAT-RETRYABLE") {
    return "retryable";
  }

  if (ticketReference === "WEBPAY-STAT-TERMINAL") {
    return "terminal";
  }

  if (ticketReference === "WEBPAY-STAT-READY") {
    return "ready";
  }

  if (ticketReference === "WEBPAY-STAT-APPLIED-PAYMENT" || ticketReference === "WEBPAY-STAT-APPLIED-DUPLICATE") {
    return "applied-payment-ready";
  }

  if (ticketReference === "WEBPAY-STAT-AMBIGUOUS-DECISION") {
    return "ambiguous-decision";
  }

  if (ticketReference === "WEBPAY-STAT-AMBIGUOUS-PAYMENT") {
    return "applied-payment-ready";
  }

  if (ticketReference === "WEBPAY-STAT-MISSING-SNAPSHOT") {
    return "missing-applied-snapshot";
  }

  if (ticketReference === "WEBPAY-STAT-MISSING-AMOUNT") {
    return "missing-final-amount";
  }

  if (ticketReference === "WEBPAY-STAT-MISSING-CURRENCY") {
    return "missing-currency";
  }

  if (entitlementType === "PWD") {
    return "pending-pwd";
  }

  return "pending";
}

function buildStatutoryDecisionResponse(body, correlationId, scenario) {
  const base = {
    statutoryDiscountDecisionCommandId: statutoryDecisionCommandId,
    requestReference: body.requestReference ?? "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
    statutoryDiscountPayableBasisApplicationCommandId: null,
    statutoryDiscountValidationId: null,
    parkingSessionId: body.parkingSessionId ?? "20000000-0000-4000-8000-000000000001",
    siteId: body.siteId ?? "50000000-0000-4000-8000-000000000001",
    siteGroupId: body.siteGroupId ?? "40000000-0000-4000-8000-000000000001",
    entitlementType: body.entitlementType ?? "SENIOR_CITIZEN",
    decisionCommandStatus: "AWAITING_REVIEW",
    decisionResultStatus: "NOT_DECIDED",
    applicationCommandStatus: "NOT_REQUESTED",
    applicationResultClassification: "NOT_REQUESTED",
    payableBasisReady: false,
    payableBasisReadinessStatus: "AWAITING_REVIEW",
    payableBasisReadinessAction: "POLL_READBACK",
    originalTariffSnapshotId: body.originalTariffSnapshotId ?? "30000000-0000-4000-8000-000000000001",
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
    correlationId,
    createdAt: "2026-07-27T10:00:00+08:00",
    decidedAt: null,
    appliedAt: null
  };

  if (scenario === "application-required") {
    return {
      ...base,
      decisionCommandStatus: "COMPLETED",
      decisionResultStatus: "APPROVED",
      payableBasisReadinessStatus: "DECISION_APPROVED_APPLICATION_NOT_REQUESTED",
      payableBasisReadinessAction: "SUBMIT_APPLICATION_INTENT",
      overallResultClassification: "COMPLETED",
      decidedAt: "2026-07-27T10:05:00+08:00"
    };
  }

  if (scenario === "apply-processing" || scenario === "application-processing") {
    return {
      ...base,
      statutoryDiscountPayableBasisApplicationCommandId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
      decisionCommandStatus: "COMPLETED",
      decisionResultStatus: "APPROVED",
      applicationCommandStatus: "PROCESSING",
      applicationResultClassification: "PROCESSING",
      payableBasisReadinessStatus: "APPLICATION_PROCESSING",
      payableBasisReadinessAction: "POLL_READBACK",
      overallResultClassification: "PENDING",
      decidedAt: "2026-07-27T10:05:00+08:00"
    };
  }

  if (scenario === "apply-retryable") {
    return {
      ...base,
      statutoryDiscountPayableBasisApplicationCommandId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
      decisionCommandStatus: "COMPLETED",
      decisionResultStatus: "APPROVED",
      applicationCommandStatus: "PROCESSING",
      applicationResultClassification: "PROCESSING",
      retryable: true,
      payableBasisReadinessStatus: "APPLICATION_PROCESSING",
      payableBasisReadinessAction: "WAIT_THEN_RETRY_ORIGINAL_IDEMPOTENCY_KEY",
      safeErrorCode: "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_TEMPORARILY_UNAVAILABLE",
      recoveryClassification: "TEMPORARILY_UNAVAILABLE",
      recoveryAction: "WAIT_THEN_RETRY_ORIGINAL_IDEMPOTENCY_KEY",
      decidedAt: "2026-07-27T10:05:00+08:00"
    };
  }

  if (scenario === "apply-conflict") {
    return {
      ...base,
      statutoryDiscountPayableBasisApplicationCommandId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
      decisionCommandStatus: "COMPLETED",
      decisionResultStatus: "APPROVED",
      applicationCommandStatus: "FAILED",
      applicationResultClassification: "SEMANTIC_CONFLICT",
      payableBasisReadinessStatus: "APPLICATION_SEMANTIC_CONFLICT",
      payableBasisReadinessAction: "DO_NOT_RETRY",
      safeErrorCode: "STATUTORY_DISCOUNT_APPLICATION_SEMANTIC_CONFLICT",
      recoveryClassification: "TERMINAL",
      recoveryAction: "DO_NOT_RETRY",
      overallResultClassification: "TERMINAL_FAILURE",
      decidedAt: "2026-07-27T10:05:00+08:00"
    };
  }

  if (scenario === "apply-terminal") {
    return {
      ...base,
      statutoryDiscountPayableBasisApplicationCommandId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
      decisionCommandStatus: "COMPLETED",
      decisionResultStatus: "APPROVED",
      applicationCommandStatus: "FAILED",
      applicationResultClassification: "FAILED",
      payableBasisReadinessStatus: "FAILED",
      payableBasisReadinessAction: "DO_NOT_RETRY",
      safeErrorCode: "STATUTORY_DISCOUNT_APPLICATION_FAILED",
      recoveryClassification: "TERMINAL",
      recoveryAction: "DO_NOT_RETRY",
      overallResultClassification: "TERMINAL_FAILURE",
      decidedAt: "2026-07-27T10:05:00+08:00"
    };
  }

  if (scenario === "rejected") {
    return {
      ...base,
      decisionCommandStatus: "COMPLETED",
      decisionResultStatus: "REJECTED",
      payableBasisReadinessStatus: "DECISION_REJECTED",
      payableBasisReadinessAction: "DO_NOT_RETRY",
      overallResultClassification: "TERMINAL_FAILURE",
      recoveryAction: "DO_NOT_RETRY",
      decidedAt: "2026-07-27T10:05:00+08:00"
    };
  }

  if (scenario === "retryable") {
    return {
      ...base,
      retryable: true,
      payableBasisReadinessStatus: "APPLICATION_PROCESSING",
      payableBasisReadinessAction: "WAIT_THEN_RETRY_ORIGINAL_IDEMPOTENCY_KEY",
      safeErrorCode: "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_TEMPORARILY_UNAVAILABLE",
      recoveryClassification: "TEMPORARILY_UNAVAILABLE",
      recoveryAction: "WAIT_THEN_RETRY_ORIGINAL_IDEMPOTENCY_KEY"
    };
  }

  if (scenario === "terminal") {
    return {
      ...base,
      payableBasisReadinessStatus: "FAILED",
      payableBasisReadinessAction: "DO_NOT_RETRY",
      recoveryAction: "DO_NOT_RETRY",
      overallResultClassification: "TERMINAL_FAILURE"
    };
  }

  if (scenario === "ready" || scenario === "apply-ready") {
    return {
      ...base,
      statutoryDiscountPayableBasisApplicationCommandId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
      decisionCommandStatus: "COMPLETED",
      decisionResultStatus: "APPROVED",
      applicationCommandStatus: "APPLIED",
      applicationResultClassification: "APPLIED",
      payableBasisReady: true,
      payableBasisReadinessStatus: "READY",
      payableBasisReadinessAction: null,
      appliedTariffSnapshotId: "99999999-9999-4999-8999-999999999999",
      originalAmountMinorUnits: 12900,
      vatExclusiveBasisAmountMinorUnits: 9214,
      vatAmountMinorUnits: 1106,
      vatTreatment: "VAT_EXEMPT_WITH_DISCOUNT",
      statutoryDiscountAmountMinorUnits: 2580,
      finalPayableAmountMinorUnits: 10320,
      currency: "PHP",
      overallResultClassification: "COMPLETED",
      oneShotComplete: true,
      decidedAt: "2026-07-27T10:05:00+08:00",
      appliedAt: "2026-07-27T10:06:00+08:00"
    };
  }

  if (scenario === "applied-payment-ready" || scenario === "missing-applied-snapshot" || scenario === "missing-final-amount" || scenario === "missing-currency") {
    return {
      ...base,
      statutoryDiscountPayableBasisApplicationCommandId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
      decisionCommandStatus: "COMPLETED",
      decisionResultStatus: "APPROVED",
      applicationCommandStatus: "APPLIED",
      applicationResultClassification: "APPLIED",
      payableBasisReady: true,
      payableBasisReadinessStatus: "READY",
      payableBasisReadinessAction: null,
      appliedTariffSnapshotId: scenario === "missing-applied-snapshot" ? null : "99999999-9999-4999-8999-999999999999",
      originalAmountMinorUnits: 5000,
      vatExclusiveBasisAmountMinorUnits: 3571,
      vatAmountMinorUnits: 429,
      vatTreatment: "VAT_EXEMPT_WITH_DISCOUNT",
      statutoryDiscountAmountMinorUnits: 1000,
      finalPayableAmountMinorUnits: scenario === "missing-final-amount" ? null : 4000,
      currency: scenario === "missing-currency" ? null : "PHP",
      overallResultClassification: "COMPLETED",
      oneShotComplete: true,
      decidedAt: "2026-07-27T10:05:00+08:00",
      appliedAt: "2026-07-27T10:06:00+08:00"
    };
  }

  return base;
}

function buildPaymentIntentResponse(body, correlationId) {
  return {
    paymentAttemptId: "10000000-0000-4000-8000-000000000010",
    parkingSessionId: "20000000-0000-4000-8000-000000000001",
    tariffSnapshotId: body.tariffSnapshotId,
    siteGroupId: body.siteGroupId ?? "40000000-0000-4000-8000-000000000001",
    siteId: body.siteId ?? "50000000-0000-4000-8000-000000000001",
    vendorSystemId: body.vendorSystemId ?? "60000000-0000-4000-8000-000000000001",
    siteGroupName: "Browser Smoke Site Group",
    siteName: "Browser Smoke Site",
    amountMinorUnits: body.expectedAmountMinorUnits,
    currency: body.expectedCurrency ?? "PHP",
    paymentMethod: body.paymentMethod ?? "QRPH",
    selectedProviderCode: "PAYMONGO",
    fallbackProviderCode: null,
    routingReason: "PRIMARY_PROVIDER",
    status: "PENDING_PROVIDER",
    handoff: {
      type: "Redirect",
      handoffUrl: "https://payments.test/handoff",
      qrCodeUrl: null,
      expiresAt: "2026-07-27T10:20:00+08:00"
    },
    correlationId,
    ticketReference: body.ticketReference,
    plateNumber: body.plateNumber,
    parkingStatus: "PaymentRequired",
    paymentStatus: "Pending Payment"
  };
}

function errorResponse(errorCode, message, retryable, correlationId) {
  return {
    errorCode,
    message,
    retryable,
    correlationId
  };
}

async function handleApi(request, response, url) {
  if (request.method === "POST" && url.pathname === "/v1/webpay/parking-session") {
    const body = await readJson(request);
    recordRequest(request, body);

    const ticketReference = typeof body.ticketReference === "string" ? body.ticketReference : "";
    if (ticketReference === "WEBPAY-SMOKE-EXPIRED") {
      writeJson(response, 404, errorResponse("SESSION_NOT_FOUND", "The transaction reference is invalid or expired.", false, body.correlationId));
      return;
    }

    writeJson(response, 200, buildSessionResponse(ticketReference, body.correlationId));
    return;
  }

  if (request.method === "POST" && url.pathname === "/v1/webpay/payment-intents") {
    const body = await readJson(request);
    recordRequest(request, body);
    const ticketReference = typeof body.ticketReference === "string" ? body.ticketReference : "";

    if (ticketReference === "WEBPAY-STAT-APPLIED-PAYMENT" || ticketReference === "WEBPAY-STAT-APPLIED-DUPLICATE") {
      paymentIntentAttempts[ticketReference] = (paymentIntentAttempts[ticketReference] ?? 0) + 1;
      const isReadyStatutoryPayment =
        body.tariffSnapshotId === "99999999-9999-4999-8999-999999999999" &&
        body.expectedAmountMinorUnits === 4000 &&
        body.expectedCurrency === "PHP" &&
        body.statutoryDiscountDecisionCommandId === statutoryDecisionCommandId &&
        body.statutoryDiscountPayableBasisApplicationCommandId === "cccccccc-cccc-4ccc-8ccc-cccccccccccc";

      if (isReadyStatutoryPayment) {
        writeJson(response, 200, buildPaymentIntentResponse(body, body.correlationId));
        return;
      }

      writeJson(
        response,
        409,
        errorResponse(
          "STATUTORY_DISCOUNT_APPLIED_SNAPSHOT_MISMATCH",
          "The selected payable basis does not match Central PMS readback.",
          false,
          body.correlationId
        )
      );
      return;
    }

    if (ticketReference === "WEBPAY-STAT-AMBIGUOUS-PAYMENT") {
      paymentIntentAttempts[ticketReference] = (paymentIntentAttempts[ticketReference] ?? 0) + 1;
      writeJson(response, 409, {
        errorCode: "ACTIVE_PAYMENT_ATTEMPT_EXISTS",
        message: "A payment is already in progress. Continue with the existing payment attempt.",
        retryable: false,
        paymentAttemptId: "10000000-0000-4000-8000-000000000011",
        status: "PENDING_PROVIDER",
        handoff: {
          type: "Redirect",
          handoffUrl: "https://payments.test/existing-handoff",
          qrCodeUrl: null,
          expiresAt: "2026-07-27T10:20:00+08:00"
        },
        correlationId: body.correlationId
      });
      return;
    }

    writeJson(response, 409, errorResponse("UNEXPECTED_BROWSER_SMOKE_PAYMENT_SUBMISSION", "Browser smoke must not submit payment.", false, body.correlationId));
    return;
  }

  if (request.method === "POST" && url.pathname === "/v1/webpay/statutory-discounts/availability") {
    const body = await readJson(request);
    recordRequest(request, body);
    const correlationId = typeof request.headers["x-correlation-id"] === "string" ? request.headers["x-correlation-id"] : body.correlationId ?? "";
    writeJson(response, 200, buildStatutoryAvailabilityResponse(body, correlationId));
    return;
  }

  if (request.method === "POST" && url.pathname === "/v1/webpay/statutory-discounts/decisions") {
    const body = await readJson(request);
    recordRequest(request, body);
    const correlationId = typeof request.headers["x-correlation-id"] === "string" ? request.headers["x-correlation-id"] : body.correlationId ?? "";
    const scenario = statutoryScenarioForTicket(body.ticketReference, body.entitlementType);
    statutoryScenarioByDecisionId[statutoryDecisionCommandId] = { scenario, body };

    if (scenario === "ambiguous-decision") {
      const idempotencyKey = typeof request.headers["idempotency-key"] === "string" ? request.headers["idempotency-key"] : "missing";
      ambiguousDecisionAttempts[idempotencyKey] = (ambiguousDecisionAttempts[idempotencyKey] ?? 0) + 1;
      if (ambiguousDecisionAttempts[idempotencyKey] === 1) {
        writeJson(response, 503, errorResponse("STATUTORY_DISCOUNT_DECISION_TEMPORARILY_UNAVAILABLE", "Statutory discount decision readback is temporarily unavailable.", true, correlationId));
        return;
      }
    }

    const initialScenario = scenario.startsWith("apply-") || scenario === "application-required" ? "pending" : scenario;
    writeJson(response, 200, buildStatutoryDecisionResponse(body, correlationId, initialScenario));
    return;
  }

  const statutoryApplyMatch = url.pathname.match(/^\/v1\/webpay\/statutory-discounts\/decisions\/([^/]+)\/apply-payable-basis$/);
  if (request.method === "POST" && statutoryApplyMatch) {
    const decisionId = decodeURIComponent(statutoryApplyMatch[1]);
    const body = await readJson(request);
    const correlationId = typeof request.headers["x-correlation-id"] === "string" ? request.headers["x-correlation-id"] : body.correlationId ?? "";
    recordRequest(request, body);
    statutoryApplyAttempts[decisionId] = (statutoryApplyAttempts[decisionId] ?? 0) + 1;
    const stored = statutoryScenarioByDecisionId[decisionId] ?? { scenario: "application-required", body };
    const attempt = statutoryApplyAttempts[decisionId];

    if (stored.scenario === "apply-ready") {
      statutoryScenarioByDecisionId[decisionId] = { ...stored, scenario: "apply-ready" };
      writeJson(response, 200, buildStatutoryDecisionResponse(stored.body, correlationId, "apply-processing"));
      return;
    }

    if (stored.scenario === "apply-retryable" && attempt === 1) {
      writeJson(response, 200, buildStatutoryDecisionResponse(stored.body, correlationId, "apply-retryable"));
      return;
    }

    if (stored.scenario === "apply-retryable" && attempt > 1) {
      statutoryScenarioByDecisionId[decisionId] = { ...stored, scenario: "apply-ready" };
      writeJson(response, 200, buildStatutoryDecisionResponse(stored.body, correlationId, "apply-processing"));
      return;
    }

    writeJson(response, 200, buildStatutoryDecisionResponse(stored.body, correlationId, stored.scenario));
    return;
  }

  const statutoryMatch = url.pathname.match(/^\/v1\/webpay\/statutory-discounts\/decisions\/([^/]+)$/);
  if (request.method === "GET" && statutoryMatch) {
    const decisionId = decodeURIComponent(statutoryMatch[1]);
    const correlationId = typeof request.headers["x-correlation-id"] === "string" ? request.headers["x-correlation-id"] : "";
    recordRequest(request, null);
    statutoryReadAttempts[decisionId] = (statutoryReadAttempts[decisionId] ?? 0) + 1;
    const stored = statutoryScenarioByDecisionId[decisionId] ?? {
      scenario: "pending",
      body: {
        requestReference: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
        parkingSessionId: "20000000-0000-4000-8000-000000000001",
        entitlementType: "SENIOR_CITIZEN",
        originalTariffSnapshotId: "30000000-0000-4000-8000-000000000001"
      }
    };
    const applyAttempted = (statutoryApplyAttempts[decisionId] ?? 0) > 0;
    const scenario =
      stored.scenario === "apply-ready" && applyAttempted
        ? "ready"
        : stored.scenario.startsWith("apply-") && !applyAttempted
          ? "application-required"
          : stored.scenario;
    writeJson(response, 200, buildStatutoryDecisionResponse(stored.body, correlationId, scenario));
    return;
  }

  const receiptMatch = url.pathname.match(/^\/v1\/webpay\/payment-attempts\/([^/]+)\/receipt-presentation$/);
  if (request.method === "GET" && receiptMatch) {
    const paymentAttemptId = decodeURIComponent(receiptMatch[1]);
    const correlationId = typeof request.headers["x-correlation-id"] === "string" ? request.headers["x-correlation-id"] : "";
    recordRequest(request, null);

    receiptAttempts[paymentAttemptId] = (receiptAttempts[paymentAttemptId] ?? 0) + 1;
    const attempt = receiptAttempts[paymentAttemptId];

    if (paymentAttemptId === paymentAttemptIds.pending || paymentAttemptId === paymentAttemptIds.refreshPending) {
      writeJson(
        response,
        409,
        errorResponse(
          "WEBPAY_RECEIPT_PRESENTATION_NOT_READY",
          "Fiscal issuance is not recorded; Sales Invoice presentation is not available yet.",
          true,
          correlationId
        )
      );
      return;
    }

    if (paymentAttemptId === paymentAttemptIds.temporarilyUnavailable && attempt < 3) {
      writeJson(
        response,
        503,
        errorResponse("POS_SERVER_RECEIPT_PRESENTATION_UNAVAILABLE", "POS Server presentation readback is temporarily unavailable.", true, correlationId)
      );
      return;
    }

    if (paymentAttemptId === paymentAttemptIds.terminalFailure) {
      writeJson(
        response,
        422,
        errorResponse("WEBPAY_FISCAL_ISSUANCE_FAILED", "Sales Invoice issuance failed. Please contact support.", false, correlationId)
      );
      return;
    }

    writeJson(response, 200, buildPresentationResponse(paymentAttemptId, correlationId));
    return;
  }

  writeJson(response, 404, errorResponse("NOT_FOUND", "Not found.", false, ""));
}

async function serveStatic(response, url) {
  const requestedPath = url.pathname === "/" || url.pathname.startsWith("/webpay/") ? "/index.html" : url.pathname;
  const filePath = normalize(join(distRoot, requestedPath.replace(/^\/+/, "")));

  if (!filePath.startsWith(distRoot)) {
    response.writeHead(403);
    response.end();
    return;
  }

  try {
    const data = await readFile(filePath);
    const contentType = contentTypes[extname(filePath)] ?? "application/octet-stream";
    response.writeHead(200, { "Content-Type": contentType });
    response.end(data);
  } catch {
    response.writeHead(404, { "Content-Type": "text/plain; charset=utf-8" });
    response.end("Not found");
  }
}

const server = createServer(async (request, response) => {
  const url = new URL(request.url ?? "/", `http://${request.headers.host}`);

  try {
    if (url.pathname === "/__fixture/health") {
      writeJson(response, 200, { ok: true });
      return;
    }

    if (request.method === "POST" && url.pathname === "/__fixture/reset") {
      await readJson(request).catch(() => ({}));
      resetState();
      writeJson(response, 200, { ok: true });
      return;
    }

    if (request.method === "POST" && url.pathname === "/__fixture/statutory-scenario") {
      const body = await readJson(request).catch(() => ({}));
      const decisionId = typeof body.decisionId === "string" ? body.decisionId : statutoryDecisionCommandId;
      const scenario = typeof body.scenario === "string" ? body.scenario : "pending";
      const ticketReference = typeof body.ticketReference === "string" ? body.ticketReference : "WEBPAY-STAT-RECOVERY";
      statutoryScenarioByDecisionId[decisionId] = {
        scenario,
        body: {
          requestReference: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
          parkingSessionId: "20000000-0000-4000-8000-000000000001",
          siteId: "50000000-0000-4000-8000-000000000001",
          siteGroupId: "40000000-0000-4000-8000-000000000001",
          entitlementType: "SENIOR_CITIZEN",
          originalTariffSnapshotId: "30000000-0000-4000-8000-000000000001",
          ticketReference
        }
      };
      writeJson(response, 200, { ok: true, decisionId, scenario });
      return;
    }

    if (request.method === "GET" && url.pathname === "/__fixture/state") {
      writeJson(response, 200, {
        requestLog,
        receiptAttempts,
        paymentAttemptIds,
        statutoryReadAttempts,
        statutoryApplyAttempts,
        paymentIntentAttempts,
        ambiguousDecisionAttempts
      });
      return;
    }

    if (url.pathname.startsWith("/v1/")) {
      await handleApi(request, response, url);
      return;
    }

    await serveStatic(response, url);
  } catch {
    writeJson(response, 500, errorResponse("FIXTURE_ERROR", "Browser smoke fixture failed.", false, ""));
  }
});

server.listen(port, "127.0.0.1");

process.on("SIGTERM", () => {
  server.close(() => process.exit(0));
});

process.on("SIGINT", () => {
  server.close(() => process.exit(0));
});
