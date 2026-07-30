import { createServer } from "node:http";
import { readFile } from "node:fs/promises";
import { extname, join, normalize } from "node:path";
import { fileURLToPath } from "node:url";

const port = Number(process.env.WEBPAY_ORDINANCE_VALIDATION_PORT ?? 5206);
const nonce = process.env.WEBPAY_ORDINANCE_VALIDATION_NONCE ?? "";
const root = normalize(join(fileURLToPath(new URL(".", import.meta.url)), "..", ".."));
const distRoot = normalize(join(root, "dist"));

if (!nonce || nonce.length < 24) {
  throw new Error("WEBPAY_ORDINANCE_VALIDATION_NONCE must be a process-scoped validation nonce.");
}

const session = {
  parkingSessionId: "21000000-0000-4000-8000-000000000001",
  staleParkingSessionId: "21000000-0000-4000-8000-000000000099",
  tariffSnapshotId: "31000000-0000-4000-8000-000000000001",
  siteGroupId: "41000000-0000-4000-8000-000000000001",
  siteId: "51000000-0000-4000-8000-000000000001",
  vendorSystemId: "61000000-0000-4000-8000-000000000001",
  ticketReference: "WEBPAY-ORD-G004-001",
  plateNumber: "ORDG004",
  amountMinorUnits: 13750,
  currency: "PHP"
};

const decisionCommandId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
const requestReference = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";

const contentTypes = {
  ".css": "text/css; charset=utf-8",
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".png": "image/png",
  ".svg": "image/svg+xml"
};

const scenarios = {
  bothCovered: { status: "AVAILABLE", covered: ["SENIOR_CITIZEN", "PWD"], available: true },
  seniorOnly: { status: "AVAILABLE", covered: ["SENIOR_CITIZEN"], available: true },
  pwdOnly: { status: "AVAILABLE", covered: ["PWD"], available: true },
  noCoverage: { status: "NO_APPLICABLE_LOCAL_ORDINANCE", covered: [], available: false, reason: "NO_APPLICABLE_LOCAL_ORDINANCE" },
  futureEffective: { status: "POLICY_NOT_YET_EFFECTIVE", covered: [], available: false, reason: "POLICY_NOT_YET_EFFECTIVE" },
  expired: { status: "POLICY_EXPIRED", covered: [], available: false, reason: "POLICY_EXPIRED" },
  inactive: { status: "POLICY_SUSPENDED", covered: [], available: false, reason: "POLICY_SUSPENDED" },
  incomplete: { status: "REQUIRED_POLICY_FACTS_INCOMPLETE", covered: [], available: false, reason: "REQUIRED_POLICY_FACTS_INCOMPLETE" },
  unavailable: { errorStatus: 503, errorCode: "WEBPAY_STATUTORY_AVAILABILITY_TEMPORARILY_UNAVAILABLE", retryable: true },
  timeout: { delayMs: 6500, errorStatus: 503, errorCode: "WEBPAY_STATUTORY_AVAILABILITY_TEMPORARILY_UNAVAILABLE", retryable: true },
  authorizationFailure: { errorStatus: 503, errorCode: "WEBPAY_STATUTORY_SERVICE_UNAVAILABLE", retryable: true },
  malformed: { errorStatus: 503, errorCode: "WEBPAY_STATUTORY_AVAILABILITY_TEMPORARILY_UNAVAILABLE", retryable: true },
  unknown: { errorStatus: 503, errorCode: "WEBPAY_STATUTORY_AVAILABILITY_TEMPORARILY_UNAVAILABLE", retryable: true },
  unsupportedVersion: { errorStatus: 503, errorCode: "WEBPAY_STATUTORY_AVAILABILITY_TEMPORARILY_UNAVAILABLE", retryable: true, unsupportedVersion: true },
  displayThenNoCoverage: { status: "AVAILABLE", covered: ["SENIOR_CITIZEN", "PWD"], available: true, submitStatus: "NO_APPLICABLE_LOCAL_ORDINANCE", submitCovered: [] },
  selectedRemovedBeforeSubmit: { status: "AVAILABLE", covered: ["SENIOR_CITIZEN", "PWD"], available: true, submitStatus: "AVAILABLE", submitCovered: ["SENIOR_CITIZEN"] },
  pendingReview: { status: "AVAILABLE", covered: ["SENIOR_CITIZEN", "PWD"], available: true, decisionScenario: "pending" },
  rejected: { status: "AVAILABLE", covered: ["SENIOR_CITIZEN", "PWD"], available: true, decisionScenario: "rejected" }
};

let currentScenarioName = "bothCovered";
let requestLog = [];
let decisionCreatedCount = 0;
let continuationCreatedCount = 0;
let applicationCreatedCount = 0;
let paymentIntentCount = 0;

function resetState() {
  requestLog = [];
  decisionCreatedCount = 0;
  continuationCreatedCount = 0;
  applicationCreatedCount = 0;
  paymentIntentCount = 0;
}

function isLoopback(request) {
  const remote = request.socket.remoteAddress ?? "";
  return remote === "127.0.0.1" || remote === "::1" || remote === "::ffff:127.0.0.1";
}

function requireValidationControl(request, response) {
  if (!isLoopback(request)) {
    writeJson(response, 403, { errorCode: "VALIDATION_CONTROL_LOOPBACK_REQUIRED" });
    return false;
  }

  if (request.headers["x-validation-nonce"] !== nonce) {
    writeJson(response, 403, { errorCode: "VALIDATION_CONTROL_NONCE_REQUIRED" });
    return false;
  }

  return true;
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

function safeHeaders(request) {
  return {
    "x-correlation-id": request.headers["x-correlation-id"],
    "idempotency-key": request.headers["idempotency-key"],
    "x-exitpass-service-identity-id": request.headers["x-exitpass-service-identity-id"],
    "x-exitpass-permissions": request.headers["x-exitpass-permissions"],
    authorization: request.headers.authorization
  };
}

function recordRequest(request, body, statusCode, classification = null) {
  requestLog.push({
    method: request.method,
    path: new URL(request.url, `http://${request.headers.host}`).pathname,
    headers: safeHeaders(request),
    body,
    statusCode,
    classification
  });
}

function correlationId(request) {
  const value = request.headers["x-correlation-id"];
  return typeof value === "string" && value.length > 0 ? value : "33000000-0000-4000-8000-000000000001";
}

function buildSessionResponse(body, request) {
  return {
    paymentAttemptId: null,
    parkingSessionId: session.parkingSessionId,
    tariffSnapshotId: session.tariffSnapshotId,
    siteGroupId: session.siteGroupId,
    siteId: session.siteId,
    vendorSystemId: session.vendorSystemId,
    siteGroupName: "G-004 Ordinance Validation Group",
    siteName: "G-004 Ordinance Validation Site",
    amountMinorUnits: session.amountMinorUnits,
    currency: session.currency,
    ticketReference: body.ticketReference ?? session.ticketReference,
    plateNumber: body.plateNumber ?? session.plateNumber,
    entryTime: "2026-07-30T08:00:00+08:00",
    currentFeeCalculationTime: "2026-07-30T10:00:00+08:00",
    durationParked: "2h 0m",
    tariffName: "G-004 Validation Tariff",
    feeValidUntil: "2026-07-30T10:15:00+08:00",
    parkingStatus: "PaymentRequired",
    paymentStatus: "Not Started",
    paymentMethod: "QRPH",
    selectedProviderCode: "PAYMONGO",
    fallbackProviderCode: null,
    routingReason: "PRIMARY_PROVIDER",
    handoff: null,
    correlationId: correlationId(request)
  };
}

function buildAvailability(body, request, forSubmit = false) {
  const scenario = scenarios[currentScenarioName] ?? scenarios.bothCovered;
  const availabilityStatus = forSubmit && scenario.submitStatus ? scenario.submitStatus : scenario.status;
  const covered = forSubmit && scenario.submitCovered ? scenario.submitCovered : scenario.covered;
  return {
    requestReference: body.requestReference ?? requestReference,
    parkingSessionId: body.parkingSessionId ?? session.parkingSessionId,
    siteId: body.siteId ?? session.siteId,
    siteGroupId: body.siteGroupId ?? session.siteGroupId,
    availabilityStatus,
    statutoryParkingBenefitAvailable: availabilityStatus === "AVAILABLE" && covered.length > 0,
    coveredEntitlementTypes: covered,
    requestedEntitlementType: body.requestedEntitlementType ?? null,
    safeReasonCode: availabilityStatus === "AVAILABLE" ? null : availabilityStatus,
    retryable: false,
    remediationAction: "CONTINUE_WITH_ORDINARY_PAYMENT",
    requiredEvidenceTypes: [],
    correlationId: correlationId(request),
    contractVersion: scenario.unsupportedVersion ? "statutory-availability-v999" : "statutory-availability-v1"
  };
}

function safeAvailabilityError(request, status, code, retryable) {
  return {
    errorCode: code,
    safeErrorCode: code,
    message: retryable
      ? "Parking privilege availability is temporarily unavailable. You may continue with the regular parking amount or try again shortly."
      : "Parking privilege requests are not available for this parking session. You may continue with the regular parking amount.",
    retryable,
    correlationId: correlationId(request)
  };
}

function decisionReadback(scenarioName, request) {
  const rejected = scenarioName === "rejected";
  return {
    statutoryDiscountDecisionCommandId: decisionCommandId,
    requestReference,
    parkingSessionId: session.parkingSessionId,
    sourceChannel: "WEBPAY",
    entitlementType: "SENIOR_CITIZEN",
    decisionCommandStatus: rejected ? "COMPLETED" : "AWAITING_REVIEW",
    decisionResultStatus: rejected ? "REJECTED" : "NOT_DECIDED",
    applicationCommandStatus: "NOT_REQUESTED",
    applicationResultClassification: "NOT_REQUESTED",
    payableBasisReady: false,
    payableBasisReadinessStatus: rejected ? "DECISION_REJECTED" : "AWAITING_REVIEW",
    payableBasisReadinessAction: rejected ? "DO_NOT_RETRY" : "POLL_READBACK",
    retryable: !rejected,
    recoveryClassification: rejected ? "REJECTED" : "PENDING_REVIEW",
    recoveryAction: rejected ? "DO_NOT_RETRY" : "POLL_READBACK",
    safeErrorCode: null,
    overallResultClassification: rejected ? "REJECTED" : "PENDING_REVIEW",
    correlationId: correlationId(request)
  };
}

function isCoveredForSubmit(body, request) {
  if (body.parkingSessionId !== session.parkingSessionId) {
    return false;
  }

  if (body.siteId !== session.siteId || body.siteGroupId !== session.siteGroupId) {
    return false;
  }

  const availability = buildAvailability(body, request, true);
  return availability.availabilityStatus === "AVAILABLE" &&
    availability.coveredEntitlementTypes.includes(body.entitlementType);
}

async function serveStatic(request, response) {
  const url = new URL(request.url, `http://${request.headers.host}`);
  const rawPath = url.pathname === "/" ? "/index.html" : url.pathname;
  const normalizedPath = normalize(join(distRoot, rawPath));
  if (!normalizedPath.startsWith(distRoot)) {
    response.writeHead(403);
    response.end();
    return;
  }

  try {
    const content = await readFile(normalizedPath);
    response.writeHead(200, {
      "Content-Type": contentTypes[extname(normalizedPath)] ?? "application/octet-stream",
      "Content-Length": content.length
    });
    response.end(content);
  } catch {
    response.writeHead(404);
    response.end();
  }
}

const server = createServer(async (request, response) => {
  const url = new URL(request.url, `http://${request.headers.host}`);

  if (url.pathname === "/__validation/health") {
    writeJson(response, 200, { ok: true, loopbackOnly: true });
    return;
  }

  if (url.pathname === "/__validation/reset" && request.method === "POST") {
    if (!requireValidationControl(request, response)) {
      return;
    }

    resetState();
    currentScenarioName = "bothCovered";
    writeJson(response, 200, { ok: true });
    return;
  }

  if (url.pathname === "/__validation/scenario" && request.method === "POST") {
    if (!requireValidationControl(request, response)) {
      return;
    }

    const body = await readJson(request);
    if (!Object.hasOwn(scenarios, body.name)) {
      writeJson(response, 422, { errorCode: "UNKNOWN_VALIDATION_SCENARIO" });
      return;
    }

    resetState();
    currentScenarioName = body.name;
    writeJson(response, 200, { ok: true, scenario: currentScenarioName });
    return;
  }

  if (url.pathname === "/__validation/state") {
    if (!requireValidationControl(request, response)) {
      return;
    }

    writeJson(response, 200, {
      scenario: currentScenarioName,
      requestLog,
      decisionCreatedCount,
      continuationCreatedCount,
      applicationCreatedCount,
      paymentIntentCount,
      session
    });
    return;
  }

  if (request.method === "POST" && url.pathname === "/v1/webpay/parking-session") {
    const body = await readJson(request);
    const responseBody = buildSessionResponse(body, request);
    recordRequest(request, body, 200, "PARKING_SESSION_RESOLVED");
    writeJson(response, 200, responseBody);
    return;
  }

  if (request.method === "POST" && url.pathname === "/v1/webpay/statutory-discounts/availability") {
    const body = await readJson(request);
    const scenario = scenarios[currentScenarioName] ?? scenarios.bothCovered;

    if (scenario.delayMs) {
      await new Promise((resolve) => setTimeout(resolve, scenario.delayMs));
    }

    if (scenario.malformed) {
      recordRequest(request, body, 200, "MALFORMED");
      response.writeHead(200, { "Content-Type": "application/json; charset=utf-8" });
      response.end("{");
      return;
    }

    if (scenario.errorStatus) {
      const error = safeAvailabilityError(request, scenario.errorStatus, scenario.errorCode, scenario.retryable);
      recordRequest(request, body, scenario.errorStatus, scenario.errorCode);
      writeJson(response, scenario.errorStatus, error);
      return;
    }

    const availability = buildAvailability(body, request, false);
    recordRequest(request, body, 200, availability.availabilityStatus);
    writeJson(response, 200, availability);
    return;
  }

  if (request.method === "POST" && url.pathname === "/v1/webpay/statutory-discounts/decisions") {
    const body = await readJson(request);
    if (!isCoveredForSubmit(body, request)) {
      recordRequest(request, body, 409, "WEBPAY_STATUTORY_PRIVILEGE_NOT_AVAILABLE");
      writeJson(response, 409, safeAvailabilityError(request, 409, "WEBPAY_STATUTORY_PRIVILEGE_NOT_AVAILABLE", false));
      return;
    }

    decisionCreatedCount += 1;
    continuationCreatedCount += 1;
    const readback = decisionReadback(scenarios[currentScenarioName].decisionScenario ?? "pending", request);
    recordRequest(request, body, 200, readback.overallResultClassification);
    writeJson(response, 200, readback);
    return;
  }

  const decisionMatch = url.pathname.match(/^\/v1\/webpay\/statutory-discounts\/decisions\/([^/]+)$/);
  if (request.method === "GET" && decisionMatch) {
    const readback = decisionReadback(currentScenarioName === "rejected" ? "rejected" : "pending", request);
    recordRequest(request, {}, 200, readback.overallResultClassification);
    writeJson(response, 200, readback);
    return;
  }

  if (request.method === "POST" && url.pathname === "/v1/webpay/payment-intents") {
    const body = await readJson(request);
    paymentIntentCount += 1;
    recordRequest(request, body, 200, "PAYMENT_HANDOFF_READY");
    writeJson(response, 200, {
      paymentAttemptId: "71000000-0000-4000-8000-000000000001",
      parkingSessionId: session.parkingSessionId,
      tariffSnapshotId: session.tariffSnapshotId,
      amountMinorUnits: session.amountMinorUnits,
      currency: session.currency,
      paymentMethod: body.paymentMethod ?? "QRPH",
      status: "PENDING_PROVIDER",
      selectedProviderCode: "PAYMONGO",
      fallbackProviderCode: null,
      handoff: {
        handoffType: "REDIRECT",
        handoffUrl: "https://payments.test/g004-handoff",
        qrCodeUrl: null,
        qrPayload: null,
        expiresAt: "2026-07-30T10:30:00+08:00"
      },
      correlationId: correlationId(request)
    });
    return;
  }

  await serveStatic(request, response);
});

server.listen(port, "127.0.0.1", () => {
  console.log(`WebPay G-004 ordinance validation fixture listening on http://127.0.0.1:${port}`);
});
