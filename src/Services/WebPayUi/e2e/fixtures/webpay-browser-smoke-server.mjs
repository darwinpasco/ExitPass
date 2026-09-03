import { createServer } from "node:http";
import { readFile } from "node:fs/promises";
import { extname, join, normalize } from "node:path";
import { fileURLToPath } from "node:url";

const port = Number(process.env.WEBPAY_BROWSER_SMOKE_PORT ?? 5196);
const root = normalize(join(fileURLToPath(new URL(".", import.meta.url)), "..", ".."));
const distRoot = normalize(join(root, "dist"));
const paymentAttemptIds = {
  available: "965a9700-fb9d-4f4c-be0a-5b3cbf8d6357",
  pending: "10000000-0000-4000-8000-000000000002",
  temporarilyUnavailable: "10000000-0000-4000-8000-000000000003",
  terminalFailure: "10000000-0000-4000-8000-000000000004",
  refreshPending: "10000000-0000-4000-8000-000000000005",
  expired: "10000000-0000-4000-8000-000000000006"
};

let requestLog = [];
let receiptAttempts = {};
let statutoryReadAttempts = {};
let statutoryApplyAttempts = {};
let paymentIntentAttempts = {};
let statutoryScenarioByDecisionId = {};
let ambiguousDecisionAttempts = {};
let lastTicketReferenceByParkingSessionId = {};
let parkingSessionResolveAttempts = {};
let latestStatutoryDecisionSubmissionResponse = null;
let latestStatutoryDecisionReadResponse = null;
let latestPendingLifecycleRediscoveryResponse = null;
let latestPaymentIntentRequest = null;
let latestPaymentIntentResponse = null;
let latestValidationPaymentIntentReplay = null;
let validationPaymentIntentReplayCount = 0;
let paymentIntentRequestLog = [];
let paymentIntentResponseLog = [];
let fixtureLifecycleState = null;
let providerHandoffIdentities = new Set();
let evidenceScenario = "validation-pending";
const fixtureTicketReference = "WEBPAY-EVIDENCE-G006";
const fixtureSiteGroupId = "40000000-0000-4000-8000-000000000001";
const fixtureSiteId = "50000000-0000-4000-8000-000000000001";
const fixtureVendorSystemId = "60000000-0000-4000-8000-000000000001";
let evidenceLifecycleClassification = "REQUIRED_NOT_STARTED";
let evidenceBootstrapCount = 0;
let evidenceStatusCount = 0;
let evidenceUploadSessionCount = 0;
let evidenceUploadCount = 0;
let evidenceFinalizeCount = 0;
let evidenceUploadedByteCount = 0;
let evidenceLastDeclaredContentType = null;
let evidenceLastDeclaredContentLength = null;
let evidenceActiveForCurrentDecision = false;
const statutoryDecisionCommandId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
const statutoryDecisionId = "dddddddd-dddd-4ddd-8ddd-dddddddddddd";
const statutoryRequestReference = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";
const statutoryContinuationReference = "continuation:g005:pending-review";
const statutoryContinuationUrl = `http://127.0.0.1:${port}/privilege-review/g005-pending-review`;

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
  lastTicketReferenceByParkingSessionId = {};
  parkingSessionResolveAttempts = {};
  latestStatutoryDecisionSubmissionResponse = null;
  latestStatutoryDecisionReadResponse = null;
  latestPendingLifecycleRediscoveryResponse = null;
  latestPaymentIntentRequest = null;
  latestPaymentIntentResponse = null;
  latestValidationPaymentIntentReplay = null;
  validationPaymentIntentReplayCount = 0;
  paymentIntentRequestLog = [];
  paymentIntentResponseLog = [];
  fixtureLifecycleState = null;
  providerHandoffIdentities = new Set();
  evidenceScenario = "validation-pending";
  evidenceLifecycleClassification = "REQUIRED_NOT_STARTED";
  evidenceBootstrapCount = 0;
  evidenceStatusCount = 0;
  evidenceUploadSessionCount = 0;
  evidenceUploadCount = 0;
  evidenceFinalizeCount = 0;
  evidenceUploadedByteCount = 0;
  evidenceLastDeclaredContentType = null;
  evidenceLastDeclaredContentLength = null;
  evidenceActiveForCurrentDecision = false;
}

function writeJson(response, statusCode, body) {
  const payload = JSON.stringify(body);
  response.writeHead(statusCode, {
    "Content-Type": "application/json; charset=utf-8",
    "Content-Length": Buffer.byteLength(payload)
  });
  response.end(payload);
}

function recordedResponse(statusCode, body) {
  return { statusCode, body };
}

function buildFixtureLifecycleState(source = {}) {
  return {
    statutoryDecisionId: source.statutoryDecisionId ?? statutoryDecisionId,
    statutoryDecisionCommandId:
      source.statutoryDecisionCommandId ?? source.statutoryDiscountDecisionCommandId ?? statutoryDecisionCommandId,
    requestReference: source.requestReference ?? statutoryRequestReference,
    opaqueContinuationReference: source.opaqueContinuationReference ?? statutoryContinuationReference,
    opaqueContinuationUrl: source.opaqueContinuationUrl ?? statutoryContinuationUrl
  };
}

function recordPaymentIntentEvidence(requestBody, statusCode, responseBody) {
  latestPaymentIntentRequest = requestBody;
  latestPaymentIntentResponse = recordedResponse(statusCode, responseBody);
  paymentIntentRequestLog.push(requestBody);
  paymentIntentResponseLog.push(recordedResponse(statusCode, responseBody));

  const handoffIdentity =
    responseBody?.providerSessionReference ??
    responseBody?.handoff?.handoffUrl ??
    responseBody?.handoff?.qrCodeUrl ??
    null;
  if (handoffIdentity) {
    providerHandoffIdentities.add(handoffIdentity);
  }
}

function buildPaymentIntentReplayEvidence() {
  const successfulResponses = paymentIntentResponseLog
    .filter((entry) => entry.statusCode >= 200 && entry.statusCode < 300 && entry.body?.paymentAttemptId)
    .map((entry) => entry.body);
  const observedPaymentAttemptIds = [...new Set(successfulResponses.map((body) => body.paymentAttemptId))];
  const observedProviderHandoffIdentities = [...providerHandoffIdentities];
  const semanticResults = successfulResponses.map((body) => ({
    paymentAttemptId: body.paymentAttemptId,
    amountMinorUnits: body.amountMinorUnits,
    currency: body.currency,
    status: body.status,
    selectedProviderCode: body.selectedProviderCode,
    handoffType: body.handoff?.type ?? null,
    handoffUrl: body.handoff?.handoffUrl ?? null,
    qrCodeUrl: body.handoff?.qrCodeUrl ?? null,
    handoffExpiresAt: body.handoff?.expiresAt ?? null
  }));
  const firstSemanticResult = semanticResults[0] ?? null;
  const semanticallyEquivalent =
    semanticResults.length > 1 &&
    semanticResults.every((result) => JSON.stringify(result) === JSON.stringify(firstSemanticResult));

  return {
    requestCount: paymentIntentRequestLog.length,
    responseCount: paymentIntentResponseLog.length,
    successfulResponseCount: successfulResponses.length,
    observedPaymentAttemptIds,
    uniquePaymentAttemptCount: observedPaymentAttemptIds.length,
    observedProviderHandoffIdentities,
    uniqueProviderHandoffCount: observedProviderHandoffIdentities.length,
    samePaymentAttemptId: successfulResponses.length > 1 && observedPaymentAttemptIds.length === 1,
    sameHandoffIdentity: successfulResponses.length > 1 && observedProviderHandoffIdentities.length === 1,
    semanticallyEquivalent
  };
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
      "x-posserver-admin-permission": request.headers["x-posserver-admin-permission"],
      "x-exitpass-service-identity-id": request.headers["x-exitpass-service-identity-id"],
      "x-exitpass-permissions": request.headers["x-exitpass-permissions"],
      authorization: request.headers.authorization
    },
    body
  });
}

function isLoopbackAddress(address) {
  return address === "127.0.0.1" || address === "::1" || address === "::ffff:127.0.0.1";
}

function paymentStatusForTicket(ticketReference) {
  if (ticketReference.startsWith("WEBPAY-STAT-") || ticketReference.startsWith("WEBPAY-EVIDENCE-")) {
    return "Not Started";
  }

  if (ticketReference === "WEBPAY-SMOKE-FAILED") {
    return "Failed";
  }

  return "Paid";
}

function parkingStatusForTicket(ticketReference) {
  if (ticketReference.startsWith("WEBPAY-STAT-") || ticketReference.startsWith("WEBPAY-EVIDENCE-")) {
    return "PaymentRequired";
  }

  return ticketReference === "WEBPAY-SMOKE-FAILED" ? "PaymentRequired" : "Payment Completed";
}

function ticketReferenceForLookup(body) {
  if (typeof body.ticketReference === "string" && body.ticketReference.length > 0) {
    return body.ticketReference;
  }

  if (typeof body.plateNumber === "string" && body.plateNumber.trim().toUpperCase() === "G005PLATE") {
    return "WEBPAY-STAT-REDISCOVER-PLATE";
  }

  return "";
}

function plateNumberForLookup(body) {
  if (typeof body.plateNumber === "string" && body.plateNumber.trim().length > 0) {
    return body.plateNumber.trim().toUpperCase();
  }

  return "SMK 0001";
}

function buildSessionResponse(ticketReference, correlationId, plateNumber = "SMK 0001") {
  const resolveAttempt = parkingSessionResolveAttempts[ticketReference] ?? 1;
  const amountChanged = ticketReference === "WEBPAY-STAT-REGULAR-AMOUNT-CHANGED" && resolveAttempt > 1;
  return {
    paymentAttemptId: paymentAttemptIds.available,
    parkingSessionId: "20000000-0000-4000-8000-000000000001",
    tariffSnapshotId: amountChanged ? "30000000-0000-4000-8000-000000000099" : "30000000-0000-4000-8000-000000000001",
    siteGroupId: fixtureSiteGroupId,
    siteId: fixtureSiteId,
    vendorSystemId: fixtureVendorSystemId,
    siteGroupName: "Browser Smoke Site Group",
    siteName: "Browser Smoke Site",
    amountMinorUnits: amountChanged ? 15900 : 12900,
    currency: "PHP",
    ticketReference,
    plateNumber,
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
      ticketReference === "R35-TICKET-PREVIEW-0002"
        ? {
            status: "ISSUED",
            message: "Proceed to exit",
            laneName: "Site A Exit",
            exitBy: "2026-09-01T14:51:52+08:00"
          }
        : null,
    exitAuthorizationStatus: ticketReference === "R35-TICKET-PREVIEW-0002" ? "ISSUED" : null,
    handoff: null,
    correlationId
  };
}

function buildStatutoryAvailabilityResponse(body, correlationId) {
  const ticketReference = lastTicketReferenceByParkingSessionId[body.parkingSessionId] ?? "";
  const evidenceRequired = ticketReference.startsWith("WEBPAY-EVIDENCE-") && evidenceScenario !== "not-required";
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
    requiredEvidenceTypes: evidenceRequired
      ? [{
          evidenceType: "STATUTORY_ID",
          requirementStatus: "REQUIRED",
          safeRequirementLabel: "Entitlement photo",
          safeRequirementNotes: "Choose a clear JPEG or PNG photo."
        }]
      : [],
    correlationId
  };
}

async function readBytes(request) {
  const chunks = [];
  for await (const chunk of request) {
    chunks.push(chunk);
  }
  return Buffer.concat(chunks);
}

function buildEvidenceChannelResponse(correlationId) {
  const evidenceRequired = evidenceActiveForCurrentDecision && evidenceScenario !== "not-required";
  const replacementAllowed = evidenceScenario !== "replacement-denied" &&
    !["REVIEW_PENDING", "APPROVED", "REJECTED", "APPLIED"].includes(evidenceLifecycleClassification);
  return {
    classification: "FOUND",
    retryable: false,
    errorCode: null,
    correlationId,
    evidenceRequired,
    evidenceSetReference: evidenceRequired ? "71000000-0000-4000-8000-000000000001" : null,
    evidenceItemReference: evidenceRequired ? "72000000-0000-4000-8000-000000000001" : null,
    allowedContentTypes: ["image/jpeg", "image/png"],
    maximumContentLengthBytes: 1048576,
    maximumImageWidth: 1920,
    maximumImageHeight: 1080,
    maximumImagePixelCount: 2073600,
    requiredDocumentType: evidenceRequired ? "STATUTORY_ID" : null,
    requiredItemRole: evidenceRequired ? "ENTITLEMENT_ID_FRONT" : null,
    lifecycleClassification: evidenceRequired ? evidenceLifecycleClassification : "NOT_REQUIRED",
    replacementPosture: replacementAllowed ? "REPLACEMENT_ALLOWED" : "REPLACEMENT_NOT_ALLOWED",
    readyForReview: evidenceLifecycleClassification === "REVIEWABLE",
    blockingReasonCode: evidenceRequired && evidenceLifecycleClassification !== "REVIEWABLE" ? "EVIDENCE_PROCESSING" : null,
    evaluatedAt: "2026-08-05T09:00:00Z"
  };
}

function buildPendingLifecycleRediscoveryResponse(body, correlationId, classification, ticketReference) {
  if (classification !== "FOUND") {
    return {
      classification,
      statutoryDecisionId: null,
      statutoryDecisionCommandId: null,
      requestReference: null,
      entitlementType: body.entitlementType ?? null,
      decisionStatus: null,
      payableBasisStatus: null,
      parkingSessionId: body.parkingSessionId ?? "20000000-0000-4000-8000-000000000001",
      siteId: body.siteId ?? "50000000-0000-4000-8000-000000000001",
      siteGroupId: body.siteGroupId ?? "40000000-0000-4000-8000-000000000001",
      opaqueContinuationReference: null,
      opaqueContinuationUrl: null,
      lifecycleState: classification,
      retryable: classification === "SOURCE_UNAVAILABLE" || classification === "UNEXPECTED_FAILURE" || classification === "ACCESS_DENIED",
      correlationId,
      createdAt: null,
      updatedAt: null,
      submittedAt: null,
      decidedAt: null,
      reviewedAt: null,
      safeMessage:
        classification === "AMBIGUOUS_SESSION"
          ? "We could not safely match one parking privilege request for this parking session. Please ask a parking attendant for assistance."
          : "Existing parking privilege status could not be restored right now. You may continue with the regular parking amount."
    };
  }

  const existingStored = Object.values(statutoryScenarioByDecisionId).find((stored) => stored?.body?.ticketReference === ticketReference);
  const storedBody = existingStored?.body ?? {
    requestReference: statutoryRequestReference,
    parkingSessionId: body.parkingSessionId ?? "20000000-0000-4000-8000-000000000001",
    siteId: body.siteId ?? "50000000-0000-4000-8000-000000000001",
    siteGroupId: body.siteGroupId ?? "40000000-0000-4000-8000-000000000001",
    ticketReference,
    plateNumber: ticketReference === "WEBPAY-STAT-REDISCOVER-PLATE" ? "G005PLATE" : "SMK 0001",
    entitlementType: body.entitlementType ?? "SENIOR_CITIZEN",
    originalTariffSnapshotId: "30000000-0000-4000-8000-000000000001"
  };
  statutoryScenarioByDecisionId[statutoryDecisionCommandId] = existingStored ?? { scenario: "pending", body: storedBody };

  return {
    classification,
    statutoryDecisionId,
    statutoryDecisionCommandId,
    requestReference: statutoryRequestReference,
    entitlementType: storedBody.entitlementType,
    decisionStatus: "AWAITING_REVIEW",
    payableBasisStatus: "AWAITING_REVIEW",
    parkingSessionId: storedBody.parkingSessionId,
    siteId: storedBody.siteId,
    siteGroupId: storedBody.siteGroupId,
    opaqueContinuationReference: statutoryContinuationReference,
    opaqueContinuationUrl: statutoryContinuationUrl,
    lifecycleState: "PENDING_REVIEW",
    retryable: true,
    correlationId,
    createdAt: "2026-07-27T10:00:00+08:00",
    updatedAt: "2026-07-27T10:01:00+08:00",
    submittedAt: "2026-07-27T10:00:00+08:00",
    decidedAt: null,
    reviewedAt: null,
    safeMessage: "Your parking privilege request was received and is awaiting review."
  };
}

function rediscoveryClassificationForTicket(ticketReference) {
  if (ticketReference === "WEBPAY-STAT-REDISCOVER-NOT-FOUND") {
    return "NOT_FOUND";
  }

  if (ticketReference === "WEBPAY-STAT-REDISCOVER-NO-ACTIVE") {
    return "NO_ACTIVE_LIFECYCLE";
  }

  if (ticketReference === "WEBPAY-STAT-REDISCOVER-AMBIGUOUS") {
    return "AMBIGUOUS_SESSION";
  }

  if (ticketReference === "WEBPAY-STAT-REDISCOVER-UNAVAILABLE") {
    return "SOURCE_UNAVAILABLE";
  }

  if (ticketReference === "WEBPAY-STAT-REDISCOVER-MALFORMED") {
    return "MALFORMED_AUTHORITATIVE_STATE";
  }

  if (ticketReference === "WEBPAY-STAT-REDISCOVER-DENIED") {
    return "ACCESS_DENIED";
  }

  if (ticketReference === "WEBPAY-STAT-REDISCOVER-UNEXPECTED") {
    return "UNEXPECTED_FAILURE";
  }

  if (
    ticketReference === "WEBPAY-STAT-REDISCOVER-PENDING" ||
    ticketReference === "WEBPAY-STAT-REDISCOVER-PLATE" ||
    Object.values(statutoryScenarioByDecisionId).some((stored) => stored?.body?.ticketReference === ticketReference)
  ) {
    return "FOUND";
  }

  return "NO_ACTIVE_LIFECYCLE";
}

function buildPresentationResponse(paymentAttemptId, correlationId) {
  return {
    paymentAttemptId,
    paymentConfirmationId: "70000000-0000-4000-8000-000000000001",
    fiscalIssuanceReferenceId: "80000000-0000-4000-8000-000000000001",
    fiscalIssuanceState: "FISCAL_ISSUANCE_RECORDED",
    posFiscalDocumentId: "90000000-0000-4000-8000-000000000001",
    fiscalDocumentNumber: "SIA-00000002-A",
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
            name: "salesInvoiceHeaderSnapshot",
            title: "Sales Invoice Header Snapshot",
            rows: [
              { key: "salesInvoiceHeaderSnapshot.registeredBusinessName", label: "Registered Business Name", displayValue: "ExitPass Test Parking Services Inc." },
              { key: "salesInvoiceHeaderSnapshot.registeredBusinessAddress", label: "Registered Business Address", displayValue: "123 Merchant Avenue, Cebu City" },
              { key: "salesInvoiceHeaderSnapshot.tin", label: "TIN", displayValue: "123-456-789-000" },
              { key: "salesInvoiceHeaderSnapshot.posSerialNumber", label: "POS Serial Number", displayValue: "SN-IST-POS-A-0001" },
              { key: "salesInvoiceHeaderSnapshot.machineIdentificationNumber", label: "MIN", displayValue: "MIN-IST-POS-A-0001" },
              { key: "salesInvoiceHeaderSnapshot.parkingLocationDisplay", label: "Parking Location", displayValue: "Site A Parking" },
              { key: "salesInvoiceHeaderSnapshot.terminalId", label: "Terminal ID", displayValue: "SITE-A-WEBPAY-01" },
              { key: "salesInvoiceHeaderSnapshot.birAccreditationNumber", label: "BIR Accreditation Number", displayValue: "ACCR-IST-SITE-A-0001" },
              { key: "salesInvoiceHeaderSnapshot.birAccreditationIssuedDate", label: "BIR Accreditation Issued Date", displayValue: "2026-01-02" },
              { key: "salesInvoiceHeaderSnapshot.ptuNumber", label: "PTU Number", displayValue: "PTU-IST-SITE-A-0001" },
              { key: "salesInvoiceHeaderSnapshot.ptuIssuedDate", label: "PTU Issued Date", displayValue: "2026-01-03" },
              { key: "salesInvoiceHeaderSnapshot.supplierDeveloperRegisteredName", label: "Supplier / Developer Registered Name", displayValue: "ExitPass Test Software Solutions Corp." },
              { key: "salesInvoiceHeaderSnapshot.supplierDeveloperAddress", label: "Supplier / Developer Address", displayValue: "456 Software Park, Cebu City" },
              { key: "salesInvoiceHeaderSnapshot.supplierDeveloperTin", label: "Supplier / Developer TIN", displayValue: "987-654-321-000" },
              { key: "salesInvoiceHeaderSnapshot.salesInvoiceLegalStatement", label: "Sales Invoice Legal Statement", displayValue: "THIS SERVES AS YOUR SALES INVOICE" }
            ]
          },
          {
            name: "fiscalNumbering",
            title: "Fiscal Numbering",
            rows: [
              { key: "fiscalNumbering.fiscalDocumentNumber", label: "Fiscal Document Number", displayValue: "SIA-00000002-A" },
              { key: "fiscalNumbering.fiscalNumberAssignedAt", label: "Fiscal Number Assigned At", displayValue: "2026-09-01T14:36:52+08:00" }
            ]
          },
          {
            name: "lineItems",
            title: "Line Items",
            rows: [
              { key: "lineItems[0000].description", label: "Description", displayValue: "Parking Fee" },
              { key: "lineItems[0000].quantity", label: "Quantity", displayValue: "1" },
              { key: "lineItems[0000].unitAmount", label: "Unit Amount", displayValue: "PHP 25.00" },
              { key: "lineItems[0000].netAmount", label: "Net Amount", displayValue: "PHP 25.00" }
            ]
          },
          {
            name: "totals",
            title: "Totals",
            rows: [
              { key: "totals.subtotal", label: "Subtotal", displayValue: "PHP 25.00" },
              { key: "totals.vatableSales", label: "VATable Sales", displayValue: "PHP 22.32" },
              { key: "totals.vatAmount", label: "VAT Amount", displayValue: "PHP 2.68" },
              { key: "totals.vatExempt", label: "VAT Exempt", displayValue: "PHP 0.00" },
              { key: "totals.zeroRated", label: "Zero Rated", displayValue: "PHP 0.00" }
            ]
          },
          {
            name: "tenders",
            title: "Tenders",
            rows: [
              { key: "tenders[0000].tenderTypeCodeKey", label: "Tender Type", displayValue: "QRPH" },
              { key: "tenders[0000].amount", label: "Tender Amount", displayValue: "PHP 25.00" }
            ]
          }
        ]
      }
    },
    createdAt: "2026-09-01T14:36:52+08:00",
    updatedAt: "2026-09-01T14:36:52+08:00",
    correlationId
  };
}

function ticketForPaymentAttempt(paymentAttemptId) {
  if (paymentAttemptId === paymentAttemptIds.pending) return "WEBPAY-SMOKE-PENDING";
  if (paymentAttemptId === paymentAttemptIds.temporarilyUnavailable) return "WEBPAY-SMOKE-TEMPORARY-UNAVAILABLE";
  if (paymentAttemptId === paymentAttemptIds.terminalFailure) return "WEBPAY-SMOKE-TERMINAL-FAILURE";
  if (paymentAttemptId === paymentAttemptIds.refreshPending) return "WEBPAY-SMOKE-REFRESH-PENDING";
  return "R35-TICKET-PREVIEW-0002";
}

function buildPaymentAttemptStatusResponse(paymentAttemptId, correlationId) {
  const ticketReference = ticketForPaymentAttempt(paymentAttemptId);
  return {
    ...buildSessionResponse(ticketReference, correlationId),
    paymentAttemptId,
    siteGroupName: "Test Site Group A",
    siteName: "Site A Parking",
    plateNumber: "R35-PLATE-B",
    amountMinorUnits: 2500,
    entryTime: "2026-09-01T14:25:00+08:00",
    currentFeeCalculationTime: "2026-09-01T14:36:52+08:00",
    durationParked: "12m",
    paymentMethod: "QRPH",
    paymentProvider: "PAYMONGO",
    paymentReference: "pay_eTN1CLQY5o9Dbv41Gj9vDAMs",
    paymentTime: "2026-09-01T14:36:52+08:00",
    paymentStatus: "CONFIRMED",
    parkingStatus: "Payment Completed"
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

    const ticketReference = ticketReferenceForLookup(body);
    if (ticketReference === fixtureTicketReference && typeof body.vendorSystemId !== "string") {
      writeJson(response, 400, errorResponse(
        "WEBPAY_FIXTURE_VENDOR_CONFIGURATION_MISSING",
        "The local WebPay fixture vendor context is unavailable.",
        false,
        body.correlationId
      ));
      return;
    }
    if (ticketReference === fixtureTicketReference && body.vendorSystemId !== fixtureVendorSystemId) {
      writeJson(response, 409, errorResponse(
        "WEBPAY_FIXTURE_VENDOR_CONFIGURATION_MISMATCH",
        "The local WebPay fixture vendor context does not match the synthetic parking session.",
        false,
        body.correlationId
      ));
      return;
    }
    if (ticketReference === "WEBPAY-SMOKE-EXPIRED") {
      writeJson(response, 404, errorResponse("SESSION_NOT_FOUND", "The transaction reference is invalid or expired.", false, body.correlationId));
      return;
    }

    parkingSessionResolveAttempts[ticketReference] = (parkingSessionResolveAttempts[ticketReference] ?? 0) + 1;
    lastTicketReferenceByParkingSessionId["20000000-0000-4000-8000-000000000001"] = ticketReference;
    writeJson(response, 200, buildSessionResponse(ticketReference, body.correlationId, plateNumberForLookup(body)));
    return;
  }

  if (request.method === "POST" && url.pathname === "/v1/webpay/payment-intents") {
    const body = await readJson(request);
    recordRequest(request, body);
    latestPaymentIntentRequest = body;
    const ticketReference = typeof body.ticketReference === "string" ? body.ticketReference : "";

    if (
      ticketReference === "WEBPAY-STAT-PENDING-REGULAR" ||
      ticketReference === "WEBPAY-STAT-REGULAR-AMOUNT-CHANGED" ||
      ticketReference === "WEBPAY-STAT-REDISCOVER-PENDING" ||
      ticketReference === "WEBPAY-STAT-REDISCOVER-PLATE"
    ) {
      paymentIntentAttempts[ticketReference] = (paymentIntentAttempts[ticketReference] ?? 0) + 1;
      const amountChanged = ticketReference === "WEBPAY-STAT-REGULAR-AMOUNT-CHANGED";
      const isOrdinaryPayment =
        !body.statutoryDiscountDecisionCommandId &&
        !body.statutoryDiscountPayableBasisApplicationCommandId &&
        body.tariffSnapshotId === (amountChanged ? "30000000-0000-4000-8000-000000000099" : "30000000-0000-4000-8000-000000000001") &&
        body.expectedAmountMinorUnits === (amountChanged ? 15900 : 12900) &&
        body.expectedCurrency === "PHP";

      if (isOrdinaryPayment) {
        const responseBody = buildPaymentIntentResponse(body, body.correlationId);
        recordPaymentIntentEvidence(body, 200, responseBody);
        writeJson(response, 200, responseBody);
        return;
      }

      const responseBody = errorResponse(
        "ORDINARY_PAYABLE_BASIS_MISMATCH",
        "The regular parking amount changed. Please review the latest amount.",
        false,
        body.correlationId
      );
      recordPaymentIntentEvidence(body, 409, responseBody);
      writeJson(response, 409, responseBody);
      return;
    }

    if (ticketReference === "WEBPAY-STAT-APPLIED-PAYMENT" || ticketReference === "WEBPAY-STAT-APPLIED-DUPLICATE") {
      paymentIntentAttempts[ticketReference] = (paymentIntentAttempts[ticketReference] ?? 0) + 1;
      const isReadyStatutoryPayment =
        body.tariffSnapshotId === "99999999-9999-4999-8999-999999999999" &&
        body.expectedAmountMinorUnits === 4000 &&
        body.expectedCurrency === "PHP" &&
        body.statutoryDiscountDecisionCommandId === statutoryDecisionCommandId &&
        body.statutoryDiscountPayableBasisApplicationCommandId === "cccccccc-cccc-4ccc-8ccc-cccccccccccc";

      if (isReadyStatutoryPayment) {
        const responseBody = buildPaymentIntentResponse(body, body.correlationId);
        recordPaymentIntentEvidence(body, 200, responseBody);
        writeJson(response, 200, responseBody);
        return;
      }

      const responseBody = errorResponse(
        "STATUTORY_DISCOUNT_APPLIED_SNAPSHOT_MISMATCH",
        "The selected payable basis does not match Central PMS readback.",
        false,
        body.correlationId
      );
      recordPaymentIntentEvidence(body, 409, responseBody);
      writeJson(response, 409, responseBody);
      return;
    }

    if (ticketReference === "WEBPAY-STAT-AMBIGUOUS-PAYMENT") {
      paymentIntentAttempts[ticketReference] = (paymentIntentAttempts[ticketReference] ?? 0) + 1;
      const responseBody = {
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
      };
      recordPaymentIntentEvidence(body, 409, responseBody);
      writeJson(response, 409, responseBody);
      return;
    }

    const responseBody = errorResponse(
      "UNEXPECTED_BROWSER_SMOKE_PAYMENT_SUBMISSION",
      "Browser smoke must not submit payment.",
      false,
      body.correlationId
    );
    recordPaymentIntentEvidence(body, 409, responseBody);
    writeJson(response, 409, responseBody);
    return;
  }

  if (request.method === "POST" && url.pathname === "/v1/webpay/statutory-discounts/availability") {
    const body = await readJson(request);
    recordRequest(request, body);
    const correlationId = typeof request.headers["x-correlation-id"] === "string" ? request.headers["x-correlation-id"] : body.correlationId ?? "";
    writeJson(response, 200, buildStatutoryAvailabilityResponse(body, correlationId));
    return;
  }

  if (request.method === "POST" && url.pathname === "/v1/webpay/statutory-discounts/evidence/bootstrap") {
    const body = await readJson(request);
    recordRequest(request, body);
    evidenceBootstrapCount += 1;
    const storedTicketReference = statutoryScenarioByDecisionId[body.statutoryDiscountDecisionCommandId]?.body?.ticketReference ?? "";
    evidenceActiveForCurrentDecision = storedTicketReference.startsWith("WEBPAY-EVIDENCE-");
    const correlationId = request.headers["x-correlation-id"] ?? "";
    if (evidenceScenario === "service-unavailable") {
      writeJson(response, 503, errorResponse(
        "WEBPAY_STATUTORY_EVIDENCE_SERVICE_UNAVAILABLE",
        "Evidence upload is temporarily unavailable. Please try again later or ask a parking attendant for assistance.",
        true,
        correlationId));
      return;
    }
    if (evidenceScenario === "access-denied") {
      writeJson(response, 503, errorResponse(
        "WEBPAY_STATUTORY_EVIDENCE_SERVICE_UNAVAILABLE",
        "Evidence upload is temporarily unavailable. Please try again later or ask a parking attendant for assistance.",
        true,
        correlationId));
      return;
    }
    if (evidenceScenario === "malformed-response") {
      writeJson(response, 200, { classification: "FOUND" });
      return;
    }
    writeJson(response, 200, buildEvidenceChannelResponse(correlationId));
    return;
  }

  if (request.method === "GET" && url.pathname === "/v1/webpay/statutory-discounts/evidence/status") {
    recordRequest(request, null);
    evidenceStatusCount += 1;
    writeJson(response, 200, buildEvidenceChannelResponse(request.headers["x-correlation-id"] ?? ""));
    return;
  }

  if (request.method === "POST" && url.pathname === "/v1/webpay/statutory-discounts/evidence/upload-sessions") {
    const body = await readJson(request);
    recordRequest(request, { ...body, declaredChecksumSha256: "[redacted]" });
    evidenceUploadSessionCount += 1;
    evidenceLastDeclaredContentType = body.declaredContentType ?? null;
    evidenceLastDeclaredContentLength = body.declaredContentLength ?? null;
    const correlationId = request.headers["x-correlation-id"] ?? "";
    if (evidenceScenario === "replacement-denied") {
      writeJson(response, 409, errorResponse(
        "WEBPAY_STATUTORY_EVIDENCE_CONFLICT",
        "This evidence is already under review and cannot be replaced.",
        false,
        correlationId));
      return;
    }
    writeJson(response, 200, {
      classification: "UPLOAD_AUTHORIZED",
      retryable: false,
      errorCode: null,
      correlationId,
      opaqueUploadSessionReference: "73000000-0000-4000-8000-000000000001",
      method: "PUT",
      expiresAt: "2026-08-05T09:05:00Z",
      acceptedContentType: body.declaredContentType,
      maximumContentLengthBytes: 1048576
    });
    evidenceLifecycleClassification = "UPLOAD_SESSION_AVAILABLE";
    return;
  }

  if (request.method === "PUT" && url.pathname === "/v1/webpay/statutory-discounts/evidence/upload-sessions/73000000-0000-4000-8000-000000000001") {
    const bytes = await readBytes(request);
    recordRequest(request, { evidenceBytes: "[not retained]", contentLength: bytes.length });
    evidenceUploadCount += 1;
    evidenceUploadedByteCount += bytes.length;
    const correlationId = request.headers["x-correlation-id"] ?? "";
    if (evidenceScenario === "provider-unavailable") {
      writeJson(response, 503, errorResponse(
        "WEBPAY_STATUTORY_EVIDENCE_TEMPORARILY_UNAVAILABLE",
        "We could not process the photo right now. Please try again.",
        true,
        correlationId));
      return;
    }
    if (evidenceScenario === "expired-session") {
      writeJson(response, 409, errorResponse(
        "WEBPAY_STATUTORY_EVIDENCE_UPLOAD_EXPIRED",
        "The photo upload expired. Request a new upload and try again.",
        true,
        correlationId));
      return;
    }
    evidenceLifecycleClassification = "UPLOAD_IN_PROGRESS";
    if (evidenceScenario === "upload-delayed") {
      await new Promise((resolve) => setTimeout(resolve, 10_000));
    }
    writeJson(response, 200, {
      classification: "UPLOADED",
      retryable: false,
      errorCode: null,
      correlationId,
      opaqueUploadSessionReference: "73000000-0000-4000-8000-000000000001",
      method: "PUT",
      expiresAt: "2026-08-05T09:05:00Z",
      acceptedContentType: request.headers["content-type"] ?? "image/jpeg",
      maximumContentLengthBytes: 1048576
    });
    return;
  }

  if (request.method === "POST" && url.pathname === "/v1/webpay/statutory-discounts/evidence/upload-sessions/73000000-0000-4000-8000-000000000001/finalize") {
    const body = await readJson(request);
    recordRequest(request, body);
    evidenceFinalizeCount += 1;
    evidenceLifecycleClassification = evidenceScenario === "reviewable"
      ? "REVIEWABLE"
      : evidenceScenario === "malware"
        ? "MALWARE_DETECTED"
        : evidenceScenario === "review-pending"
          ? "REVIEW_PENDING"
          : evidenceScenario === "approved"
            ? "APPROVED"
            : evidenceScenario === "rejected"
              ? "REJECTED"
              : evidenceScenario === "applied"
                ? "APPLIED"
                : evidenceScenario === "validation-failed"
                  ? "VALIDATION_FAILED"
                  : evidenceScenario === "scan-pending"
                    ? "SCAN_PENDING"
                    : evidenceScenario === "scan-retryable"
                      ? "SCAN_RETRYABLE"
                      : "VALIDATION_PENDING";
    writeJson(response, 200, buildEvidenceChannelResponse(request.headers["x-correlation-id"] ?? ""));
    return;
  }

  if (request.method === "POST" && url.pathname === "/v1/webpay/statutory-discounts/pending-lifecycle/rediscover") {
    const body = await readJson(request);
    recordRequest(request, body);
    const correlationId = typeof request.headers["x-correlation-id"] === "string" ? request.headers["x-correlation-id"] : body.correlationId ?? "";
    const lookupTicket =
      typeof body.ticketReference === "string" && body.ticketReference.length > 0
        ? body.ticketReference
        : typeof body.plateNumber === "string" && body.plateNumber.trim().toUpperCase() === "G005PLATE"
          ? "WEBPAY-STAT-REDISCOVER-PLATE"
          : lastTicketReferenceByParkingSessionId[body.parkingSessionId] ?? "";
    const classification = rediscoveryClassificationForTicket(lookupTicket);
    const responseBody = buildPendingLifecycleRediscoveryResponse(body, correlationId, classification, lookupTicket);
    latestPendingLifecycleRediscoveryResponse = recordedResponse(200, responseBody);
    if (classification === "FOUND") {
      fixtureLifecycleState = buildFixtureLifecycleState(responseBody);
    }
    writeJson(response, 200, responseBody);
    return;
  }

  if (request.method === "POST" && url.pathname === "/v1/webpay/statutory-discounts/decisions") {
    const body = await readJson(request);
    recordRequest(request, body);
    const correlationId = typeof request.headers["x-correlation-id"] === "string" ? request.headers["x-correlation-id"] : body.correlationId ?? "";
    const scenario = statutoryScenarioForTicket(body.ticketReference, body.entitlementType);
    const canonicalBody = { ...body, requestReference: statutoryRequestReference };
    statutoryScenarioByDecisionId[statutoryDecisionCommandId] = { scenario, body: canonicalBody };

    if (scenario === "ambiguous-decision") {
      const idempotencyKey = typeof request.headers["idempotency-key"] === "string" ? request.headers["idempotency-key"] : "missing";
      ambiguousDecisionAttempts[idempotencyKey] = (ambiguousDecisionAttempts[idempotencyKey] ?? 0) + 1;
      if (ambiguousDecisionAttempts[idempotencyKey] === 1) {
        const responseBody = errorResponse(
          "STATUTORY_DISCOUNT_DECISION_TEMPORARILY_UNAVAILABLE",
          "Statutory discount decision readback is temporarily unavailable.",
          true,
          correlationId
        );
        latestStatutoryDecisionSubmissionResponse = recordedResponse(503, responseBody);
        writeJson(response, 503, responseBody);
        return;
      }
    }

    const initialScenario = scenario.startsWith("apply-") || scenario === "application-required" ? "pending" : scenario;
    const responseBody = buildStatutoryDecisionResponse(canonicalBody, correlationId, initialScenario);
    latestStatutoryDecisionSubmissionResponse = recordedResponse(200, responseBody);
    fixtureLifecycleState = buildFixtureLifecycleState(responseBody);
    writeJson(response, 200, responseBody);
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
    const responseBody = buildStatutoryDecisionResponse(stored.body, correlationId, scenario);
    latestStatutoryDecisionReadResponse = recordedResponse(200, responseBody);
    fixtureLifecycleState = buildFixtureLifecycleState({ ...fixtureLifecycleState, ...responseBody });
    writeJson(response, 200, responseBody);
    return;
  }

  const statusMatch = url.pathname.match(/^\/v1\/webpay\/payment-attempts\/([^/]+)\/status$/);
  if (request.method === "GET" && statusMatch) {
    const paymentAttemptId = decodeURIComponent(statusMatch[1]);
    const correlationId = typeof request.headers["x-correlation-id"] === "string" ? request.headers["x-correlation-id"] : "";
    recordRequest(request, null);

    if (paymentAttemptId === paymentAttemptIds.expired) {
      writeJson(response, 404, errorResponse("WEBPAY_PAYMENT_ATTEMPT_NOT_FOUND", "Payment reference was not found.", false, correlationId));
      return;
    }

    writeJson(response, 200, buildPaymentAttemptStatusResponse(paymentAttemptId, correlationId));
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
      const fixtureBaseUrl = `http://127.0.0.1:${server.address().port}`;
      writeJson(response, 200, {
        ok: true,
        contract: {
          webPayBaseUrl: fixtureBaseUrl,
          paymentOrchestratorBaseUrl: fixtureBaseUrl,
          ticketReference: fixtureTicketReference,
          siteGroupId: fixtureSiteGroupId,
          siteId: fixtureSiteId,
          vendorSystemId: fixtureVendorSystemId
        }
      });
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
      fixtureLifecycleState = buildFixtureLifecycleState({
        statutoryDecisionCommandId: decisionId,
        requestReference: statutoryRequestReference
      });
      writeJson(response, 200, { ok: true, decisionId, scenario });
      return;
    }

    if (request.method === "POST" && url.pathname === "/__fixture/evidence-scenario") {
      const body = await readJson(request).catch(() => ({}));
      if (!isLoopbackAddress(request.socket.remoteAddress)) {
        writeJson(response, 403, { ok: false, errorCode: "FIXTURE_LOOPBACK_REQUIRED" });
        return;
      }
      const allowedScenarios = new Set([
        "validation-pending", "reviewable", "malware", "review-pending", "approved", "rejected", "applied",
        "validation-failed", "scan-pending", "scan-retryable", "not-required", "replacement-denied",
        "provider-unavailable", "expired-session", "upload-delayed", "service-unavailable", "access-denied",
        "malformed-response"
      ]);
      if (!allowedScenarios.has(body.scenario)) {
        writeJson(response, 400, { ok: false, errorCode: "FIXTURE_EVIDENCE_SCENARIO_INVALID" });
        return;
      }
      evidenceScenario = body.scenario;
      evidenceLifecycleClassification = body.scenario === "not-required"
        ? "NOT_REQUIRED"
        : body.scenario === "replacement-denied"
          ? "REVIEW_PENDING"
          : "REQUIRED_NOT_STARTED";
      writeJson(response, 200, { ok: true, scenario: evidenceScenario });
      return;
    }

    if (request.method === "POST" && url.pathname === "/__fixture/replay-latest-payment-intent") {
      await readJson(request).catch(() => ({}));
      if (!isLoopbackAddress(request.socket.remoteAddress)) {
        writeJson(response, 403, { ok: false, errorCode: "FIXTURE_LOOPBACK_REQUIRED" });
        return;
      }

      const recordedPaymentRequests = requestLog.filter(
        (entry) => entry.method === "POST" && entry.path === "/v1/webpay/payment-intents"
      );
      if (recordedPaymentRequests.length !== 1) {
        writeJson(response, 409, {
          ok: false,
          errorCode: "FIXTURE_REPLAY_REQUIRES_ONE_PAYMENT_INTENT",
          observedRequestCount: recordedPaymentRequests.length
        });
        return;
      }

      const recordedRequest = recordedPaymentRequests[0];
      const idempotencyKey = recordedRequest.headers["idempotency-key"];
      const correlationId = recordedRequest.headers["x-correlation-id"];
      const bodyCorrelationId = recordedRequest.body?.correlationId;
      if (
        typeof correlationId !== "string" ||
        correlationId.length === 0 ||
        typeof bodyCorrelationId !== "string" ||
        bodyCorrelationId !== correlationId
      ) {
        writeJson(response, 409, { ok: false, errorCode: "FIXTURE_REPLAY_CANONICAL_IDENTITY_MISSING" });
        return;
      }

      const replayHeaders = {
        "Content-Type": "application/json",
        "X-Correlation-Id": correlationId
      };
      if (typeof idempotencyKey === "string" && idempotencyKey.length > 0) {
        replayHeaders["Idempotency-Key"] = idempotencyKey;
      }
      const originalIdempotencyKeyPresent = typeof idempotencyKey === "string" && idempotencyKey.length > 0;
      const replayIdempotencyKey = replayHeaders["Idempotency-Key"];
      const replayIdempotencyKeyPresent = typeof replayIdempotencyKey === "string" && replayIdempotencyKey.length > 0;
      const idempotencyKeyValuesMatch =
        (!originalIdempotencyKeyPresent && !replayIdempotencyKeyPresent) ||
        (originalIdempotencyKeyPresent && replayIdempotencyKeyPresent && replayIdempotencyKey === idempotencyKey);
      const idempotencyKeyDisposition = originalIdempotencyKeyPresent
        ? "REUSED_RECORDED_HEADER"
        : "ABSENT_IN_RECORDED_BROWSER_REQUEST";
      const idempotencyKeyPreservationPassed =
        idempotencyKeyValuesMatch &&
        ((originalIdempotencyKeyPresent && replayIdempotencyKeyPresent) ||
          (!originalIdempotencyKeyPresent &&
            !replayIdempotencyKeyPresent &&
            idempotencyKeyDisposition === "ABSENT_IN_RECORDED_BROWSER_REQUEST"));

      const replayResponse = await fetch(`http://127.0.0.1:${port}/v1/webpay/payment-intents`, {
        method: "POST",
        headers: replayHeaders,
        body: JSON.stringify(recordedRequest.body)
      });
      validationPaymentIntentReplayCount += 1;
      const replayBody = await replayResponse.json();
      latestValidationPaymentIntentReplay = {
        route: "/v1/webpay/payment-intents",
        reusedRecordedBody: true,
        reusedIdempotencyKey: originalIdempotencyKeyPresent && replayIdempotencyKeyPresent && idempotencyKeyValuesMatch,
        originalIdempotencyKeyPresent,
        replayIdempotencyKeyPresent,
        idempotencyKeyValuesMatch,
        idempotencyKeyDisposition,
        idempotencyKeyPreservationPassed,
        reusedCorrelationId: true,
        reusedCanonicalRequestIdentity: true,
        statusCode: replayResponse.status,
        body: replayBody
      };
      writeJson(response, replayResponse.status, {
        ok: replayResponse.ok,
        ...latestValidationPaymentIntentReplay
      });
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
        ambiguousDecisionAttempts,
        providerHandoffCount: providerHandoffIdentities.size,
        observedPaymentAttemptIds: buildPaymentIntentReplayEvidence().observedPaymentAttemptIds,
        observedProviderHandoffIdentities: buildPaymentIntentReplayEvidence().observedProviderHandoffIdentities,
        paymentIntentRequestLog,
        paymentIntentResponseLog,
        paymentIntentReplayResult: buildPaymentIntentReplayEvidence(),
        latestStatutoryDecisionSubmissionResponse,
        latestStatutoryDecisionReadResponse,
        latestPendingLifecycleRediscoveryResponse,
        latestPaymentIntentRequest,
        latestPaymentIntentResponse,
        latestValidationPaymentIntentReplay,
        validationPaymentIntentReplayCount,
        fixtureLifecycleState,
        evidence: {
          scenario: evidenceScenario,
          lifecycleClassification: evidenceLifecycleClassification,
          bootstrapCount: evidenceBootstrapCount,
          statusCount: evidenceStatusCount,
          uploadSessionCount: evidenceUploadSessionCount,
          uploadCount: evidenceUploadCount,
          finalizeCount: evidenceFinalizeCount,
          uploadedByteCount: evidenceUploadedByteCount,
          lastDeclaredContentType: evidenceLastDeclaredContentType,
          lastDeclaredContentLength: evidenceLastDeclaredContentLength,
          activeForCurrentDecision: evidenceActiveForCurrentDecision
        }
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
