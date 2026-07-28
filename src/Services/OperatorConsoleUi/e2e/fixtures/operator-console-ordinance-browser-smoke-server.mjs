import { createServer } from "node:http";
import { readFile } from "node:fs/promises";
import { extname, join, normalize } from "node:path";
import { fileURLToPath } from "node:url";

const port = Number(process.env.OPERATOR_CONSOLE_ORDINANCE_BROWSER_SMOKE_PORT ?? 5197);
const root = normalize(join(fileURLToPath(new URL(".", import.meta.url)), "..", ".."));
const distRoot = normalize(join(root, "dist"));

const contentTypes = {
  ".css": "text/css; charset=utf-8",
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".png": "image/png",
  ".svg": "image/svg+xml"
};

const scenarioIds = {
  active: "active-authority",
  missing: "missing-authority",
  malformed: "malformed-authority",
  unsupported: "unsupported-effect",
  paranaque: "paranaque-operational"
};

const scenarioParkingSessionIds = {
  [scenarioIds.active]: "20000000-0000-0000-0000-000000000101",
  [scenarioIds.missing]: "20000000-0000-0000-0000-000000000102",
  [scenarioIds.malformed]: "20000000-0000-0000-0000-000000000103",
  [scenarioIds.unsupported]: "20000000-0000-0000-0000-000000000104",
  [scenarioIds.paranaque]: "20000000-0000-0000-0000-000000000105"
};

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

function activeReadiness(correlationId, body = {}) {
  return {
    accessEvaluationId: "91000000-0000-0000-0000-000000000001",
    accessAllowed: true,
    accessDecision: "ALLOWED",
    requestedAction: body.requestedAction ?? "OperatorConsoleStatutoryDiscountDecisionReview",
    readinessStatus: "READY",
    readinessDimensions: [
      { dimension: "OPERATOR", status: "READY", required: true, denialReasonCodes: [] },
      { dimension: "DEVICE", status: "READY", required: true, denialReasonCodes: [] },
      { dimension: "SHIFT", status: "READY", required: true, denialReasonCodes: [] },
      { dimension: "SITE", status: "READY", required: true, denialReasonCodes: [] }
    ],
    denialReasons: [],
    operatorReadiness: {
      operatorUserId: "77000000-0000-0000-0000-000000000010",
      status: "READY",
      ready: true
    },
    deviceReadiness: {
      operatorDeviceBindingId: "77000000-0000-0000-0000-000000000020",
      status: "TRUSTED",
      ready: true
    },
    shiftReadiness: {
      operatorShiftId: "77000000-0000-0000-0000-000000000050",
      status: "ACTIVE",
      ready: true
    },
    siteReadiness: {
      siteId: "77000000-0000-0000-0000-000000000002",
      siteGroupId: "77000000-0000-0000-0000-000000000001",
      status: "READY",
      ready: true
    },
    workflowReadiness: {
      requestedAction: body.requestedAction ?? "OperatorConsoleStatutoryDiscountDecisionReview",
      workflowState: body.workflowState ?? "PENDING_OPERATOR_REVIEW",
      status: "READY",
      ready: true
    },
    auditPersisted: true,
    evaluatedAt: "2026-07-29T09:00:00+08:00",
    correlationId,
    retryable: false,
    nextOperatorAction: "CONTINUE"
  };
}

function governingPolicy(overrides = {}) {
  return {
    statutoryDiscountPolicyVersionId: "8a000000-0000-0000-0000-000000000111",
    jurisdictionId: "8a000000-0000-0000-0000-000000000112",
    jurisdictionCode: "PH-137404000",
    jurisdictionDisplayName: "Quezon City",
    policyCode: "QC_PWD_PARKING_2026",
    policyVersion: "v2",
    ordinanceNumber: "QC Ordinance 2026-04",
    ordinanceTitle: "Quezon City PWD Parking Benefit",
    sourceVerificationStatus: "VERIFIED_OFFICIAL",
    transactionPublicationStatus: "ACTIVE_FOR_TRANSACTION_USE",
    detailedRuleVerificationStatus: "VERIFIED",
    parkingServiceApplicability: "COVERED",
    benefitType: "STATUTORY_DISCOUNT_VAT_EXEMPT",
    beneficiaryResidencyScope: "UNRESTRICTED_VALID_ID",
    officialSourceAvailable: true,
    ordinanceTextAvailable: true,
    ordinanceNumberAvailable: true,
    effectiveFrom: "2026-01-01T00:00:00+08:00",
    effectiveTo: "2026-12-31T23:59:59+08:00",
    requiredEvidenceTypes: [
      {
        evidenceType: "PWD_ID",
        requirementStatus: "REQUIRED",
        safeRequirementLabel: "Masked PWD ID reference"
      },
      {
        evidenceType: "BENEFICIARY_PRESENCE",
        requirementStatus: "REQUIRED",
        safeRequirementLabel: "Beneficiary present at review"
      }
    ],
    legalApprovabilityReason:
      "Central PMS froze this active city ordinance policy authority before creating the review item.",
    ...overrides
  };
}

function baseDraft(id, overrides = {}) {
  return {
    draftId: id,
    parkingSessionId: scenarioParkingSessionIds[id] ?? "20000000-0000-0000-0000-000000000199",
    ticketReference: `OC-ORD-${id.toUpperCase()}`,
    plateNumber: "ORD 2026",
    siteId: "77000000-0000-0000-0000-000000000002",
    siteGroupId: "77000000-0000-0000-0000-000000000001",
    siteName: "Deterministic Ordinance Site",
    entitlementType: "PWD",
    validationStatus: "PENDING_OPERATOR_REVIEW",
    evidenceRequired: true,
    evidenceRequiredSatisfied: true,
    evidenceCount: 2,
    latestEvidenceStatus: "VERIFIED_METADATA_ONLY",
    registrySource: "LOCAL_ORDINANCE_POLICY_AUTHORITY",
    policyResolutionBasis: "LOCAL_ORDINANCE",
    policyCode: "QC_PWD_PARKING_2026",
    policyName: "Quezon City PWD Parking Benefit",
    verificationStatus: "VERIFIED_OFFICIAL",
    policyReadinessClassification: "READY_VERIFIED",
    requiresManualReview: false,
    policyReadinessReason: "ACTIVE_LOCAL_ORDINANCE_POLICY_AUTHORITY",
    operatorMessage: "Central PMS resolved a frozen local ordinance authority for this review.",
    requiredEvidenceType: "PWD_ID",
    effectiveFrom: "2026-01-01T00:00:00+08:00",
    effectiveTo: "2026-12-31T23:59:59+08:00",
    originalAmountMinorUnits: 22000,
    payableAmountMinorUnits: 17600,
    currencyCode: "PHP",
    requestedAt: "2026-07-29T09:00:00+08:00",
    requestedByUserId: "77000000-0000-0000-0000-000000000099",
    evidenceCaptured: true,
    requiredEvidenceTypes: ["PWD_ID", "BENEFICIARY_PRESENCE"],
    legalBasisReference: "RA 10754",
    ordinanceReference: "QC Ordinance 2026-04",
    nationalLawReference: "RA 10754",
    benefitType: "STATUTORY_DISCOUNT_VAT_EXEMPT",
    originalTariffSnapshotId: "30000000-0000-0000-0000-000000000001",
    vatAmountMinorUnits: 2357,
    vatExclusiveAmountMinorUnits: 19643,
    statutoryDiscountAmountMinorUnits: 4400,
    finalPayableAmountMinorUnits: 17600,
    governingPolicy: governingPolicy(),
    activity: [
      "Central PMS resolved local ordinance policy authority.",
      "Service-channel review item created with frozen governing policy."
    ],
    ...overrides
  };
}

const drafts = new Map([
  [scenarioIds.active, baseDraft(scenarioIds.active)],
  [
    scenarioIds.missing,
    baseDraft(scenarioIds.missing, {
      policyName: "Missing frozen policy authority fixture",
      governingPolicy: null,
      activity: ["Fixture intentionally omits frozen policy authority for fail-closed browser proof."]
    })
  ],
  [
    scenarioIds.malformed,
    baseDraft(scenarioIds.malformed, {
      policyName: "Malformed frozen policy authority fixture",
      governingPolicy: {
        ...governingPolicy(),
        policyCode: null
      },
      activity: ["Fixture intentionally returns incomplete governing-policy DTO fields for fail-closed proof."]
    })
  ],
  [
    scenarioIds.unsupported,
    baseDraft(scenarioIds.unsupported, {
      policyName: "Full parking-fee exemption policy fixture",
      governingPolicy: governingPolicy({
        policyCode: "QC_FULL_FEE_EXEMPTION_UNSUPPORTED",
        ordinanceTitle: "Quezon City Full Parking-Fee Exemption",
        benefitType: "FULL_FEE_EXEMPTION",
        legalApprovabilityReason:
          "Central PMS resolved an ordinance, but the current Operator Console review flow cannot approve this benefit effect."
      }),
      activity: ["Fixture exposes unsupported benefit effect without mapping it to a percentage discount."]
    })
  ],
  [
    scenarioIds.paranaque,
    baseDraft(scenarioIds.paranaque, {
      siteName: "Paranaque Controlled Policy Site",
      entitlementType: "SENIOR_CITIZEN",
      policyCode: "PARANAQUE_SC_OPERATIONAL",
      policyName: "Paranaque resident Senior Citizen free parking operational authority",
      ordinanceReference: null,
      verificationStatus: "VERIFIED_ACTIVE_OPERATIONAL",
      requiredEvidenceType: "SENIOR_CITIZEN_ID",
      requiredEvidenceTypes: ["SENIOR_CITIZEN_ID", "RESIDENCY_EVIDENCE"],
      governingPolicy: governingPolicy({
        statutoryDiscountPolicyVersionId: "8a000000-0000-0000-0000-000000000211",
        jurisdictionId: "8a000000-0000-0000-0000-000000000212",
        jurisdictionCode: "PH-137604000",
        jurisdictionDisplayName: "Para\u00f1aque City",
        policyCode: "PARANAQUE_SC_OPERATIONAL",
        policyVersion: "v1",
        ordinanceNumber: null,
        ordinanceTitle: null,
        sourceVerificationStatus: "VERIFIED_ACTIVE_OPERATIONAL",
        detailedRuleVerificationStatus: "PARTIALLY_VERIFIED",
        beneficiaryResidencyScope: "RESIDENT_ONLY",
        officialSourceAvailable: false,
        ordinanceTextAvailable: false,
        ordinanceNumberAvailable: false,
        requiredEvidenceTypes: [
          {
            evidenceType: "SENIOR_CITIZEN_ID",
            requirementStatus: "REQUIRED",
            safeRequirementLabel: "Masked statutory ID reference"
          },
          {
            evidenceType: "RESIDENCY_EVIDENCE",
            requirementStatus: "REQUIRED",
            safeRequirementLabel: "Paranaque residency evidence"
          }
        ],
        legalApprovabilityReason:
          "Central PMS resolved verified active operational Paranaque resident benefit authority; online ordinance text is unavailable."
      }),
      activity: ["Paranaque verified active operational policy displayed without inventing ordinance number."]
    })
  ]
]);

function evidenceList(draft) {
  return {
    draftId: draft.draftId,
    evidenceRequired: draft.evidenceRequired,
    evidenceRequiredSatisfied: draft.evidenceRequiredSatisfied,
    requiredEvidenceTypes: draft.requiredEvidenceTypes,
    evidenceCount: draft.evidenceCount,
    latestEvidenceStatus: draft.latestEvidenceStatus,
    items: draft.requiredEvidenceTypes.map((evidenceType, index) => ({
      evidenceId: `79000000-0000-0000-0000-${String(index + 1).padStart(12, "0")}`,
      draftId: draft.draftId,
      evidenceType,
      captureMethod: "METADATA_ONLY",
      storageReference: null,
      capturedByUserId: "77000000-0000-0000-0000-000000000010",
      capturedAt: "2026-07-29T09:05:00+08:00",
      redactionStatus: "REFERENCE_ONLY",
      verificationStatus: "VERIFIED_METADATA_ONLY",
      correlationId: "92000000-0000-0000-0000-000000000001"
    }))
  };
}

async function serveStatic(pathname, response) {
  const requestedPath = pathname === "/" ? "/index.html" : pathname;
  const candidate = normalize(join(distRoot, requestedPath));
  const safePath = candidate.startsWith(distRoot) ? candidate : join(distRoot, "index.html");
  const ext = extname(safePath);

  try {
    const content = await readFile(safePath);
    response.writeHead(200, { "Content-Type": contentTypes[ext] ?? "application/octet-stream" });
    response.end(content);
  } catch {
    const index = await readFile(join(distRoot, "index.html"));
    response.writeHead(200, { "Content-Type": contentTypes[".html"] });
    response.end(index);
  }
}

const server = createServer(async (request, response) => {
  try {
    const url = new URL(request.url ?? "/", `http://${request.headers.host}`);

    if (url.pathname === "/__fixture/health") {
      writeJson(response, 200, { status: "READY", scenarios: Object.values(scenarioIds) });
      return;
    }

    if (url.pathname === "/__fixture/scenarios") {
      writeJson(response, 200, { scenarios: Object.values(scenarioIds) });
      return;
    }

    if (url.pathname === "/v1/ops/operator-console/access/readiness/evaluate" && request.method === "POST") {
      const body = await readJson(request);
      writeJson(response, 200, activeReadiness(request.headers["x-correlation-id"] ?? body.correlationId ?? "", body));
      return;
    }

    if (url.pathname === "/v1/ops/operator-console/statutory-discounts/drafts" && request.method === "GET") {
      writeJson(response, 200, { items: Array.from(drafts.values()) });
      return;
    }

    const detailMatch = url.pathname.match(/^\/v1\/ops\/operator-console\/statutory-discounts\/drafts\/([^/]+)$/);
    if (detailMatch && request.method === "GET") {
      const draft = drafts.get(decodeURIComponent(detailMatch[1]));
      if (!draft) {
        writeJson(response, 404, { errorCode: "NOT_FOUND", message: "Fixture draft not found." });
        return;
      }

      writeJson(response, 200, draft);
      return;
    }

    const evidenceMatch = url.pathname.match(/^\/v1\/ops\/operator-console\/statutory-discounts\/([^/]+)\/evidence$/);
    if (evidenceMatch && request.method === "GET") {
      const draft = drafts.get(decodeURIComponent(evidenceMatch[1]));
      if (!draft) {
        writeJson(response, 404, { errorCode: "NOT_FOUND", message: "Fixture draft not found." });
        return;
      }

      writeJson(response, 200, evidenceList(draft));
      return;
    }

    const decisionMatch = url.pathname.match(/^\/v1\/ops\/operator-console\/statutory-discounts\/([^/]+)\/decision$/);
    if (decisionMatch && request.method === "POST") {
      const draft = drafts.get(decodeURIComponent(decisionMatch[1]));
      const body = await readJson(request);
      if (!draft) {
        writeJson(response, 404, { errorCode: "NOT_FOUND", message: "Fixture draft not found." });
        return;
      }

      writeJson(response, 200, {
        accessAllowed: true,
        accessDecision: "ALLOWED",
        accessDenialReasons: [],
        decisionAccepted: true,
        decisionPersisted: true,
        currentValidationStatus: body.decision === "REJECT" ? "REJECTED" : "APPROVED",
        ineligibilityReason: null,
        errorCode: null,
        correlationId: request.headers["x-correlation-id"] ?? body.correlationId ?? ""
      });
      return;
    }

    await serveStatic(url.pathname, response);
  } catch {
    writeJson(response, 500, {
      errorCode: "FIXTURE_ERROR",
      message: "Operator Console ordinance browser-smoke fixture failed."
    });
  }
});

server.listen(port, "127.0.0.1", () => {
  console.log(`Operator Console ordinance browser-smoke fixture listening on http://127.0.0.1:${port}`);
});
