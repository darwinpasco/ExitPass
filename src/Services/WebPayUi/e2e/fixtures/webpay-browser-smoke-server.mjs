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
      "x-posserver-admin-key": request.headers["x-posserver-admin-key"],
      "x-posserver-admin-permission": request.headers["x-posserver-admin-permission"]
    },
    body
  });
}

function paymentStatusForTicket(ticketReference) {
  if (ticketReference === "WEBPAY-SMOKE-FAILED") {
    return "Failed";
  }

  return "Paid";
}

function parkingStatusForTicket(ticketReference) {
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
    writeJson(response, 409, errorResponse("UNEXPECTED_BROWSER_SMOKE_PAYMENT_SUBMISSION", "Browser smoke must not submit payment.", false, body.correlationId));
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

    if (request.method === "GET" && url.pathname === "/__fixture/state") {
      writeJson(response, 200, { requestLog, receiptAttempts, paymentAttemptIds });
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
