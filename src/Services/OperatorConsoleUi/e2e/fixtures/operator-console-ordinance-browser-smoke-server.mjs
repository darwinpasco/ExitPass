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
  seniorRepresentative: "senior-representative-optional",
  pwdRepresentative: "pwd-representative-unspecified",
  residencyRequired: "residency-required",
  driverRequired: "driver-required",
  passengerRequired: "passenger-required",
  missingEvidence: "missing-evidence",
  missing: "missing-authority",
  malformed: "malformed-authority",
  unsupported: "unsupported-effect",
  paranaque: "paranaque-operational",
  approved: "approved-request",
  rejected: "rejected-request",
  evidenceJpeg: "evidence-eligible-jpeg",
  evidencePng: "evidence-eligible-png",
  evidencePdf: "evidence-unsupported-pdf",
  evidenceValidationPending: "evidence-validation-pending",
  evidenceValidationFailed: "evidence-validation-failed",
  evidenceScanPending: "evidence-scan-pending",
  evidenceScannerOutage: "evidence-scanner-outage",
  evidenceMalware: "evidence-malware-detected",
  evidenceNotReviewable: "evidence-not-reviewable",
  evidenceStale: "evidence-stale",
  evidenceReplaced: "evidence-replaced",
  evidenceDeleted: "evidence-deleted",
  evidencePendingDeletion: "evidence-pending-deletion",
  evidenceHold: "evidence-hold-active",
  evidenceReplacementAllowed: "evidence-replacement-allowed",
  evidenceReplacementDenied: "evidence-replacement-denied",
  evidencePermissionDenied: "evidence-permission-denied",
  evidenceMetadataNotFound: "evidence-metadata-not-found",
  evidencePreviewNotFound: "evidence-preview-not-found",
  evidenceStorageOutage: "evidence-preview-storage-outage",
  evidenceCrossSite: "evidence-cross-site-denied",
  evidenceCrossSiteGroup: "evidence-cross-site-group-denied",
  evidenceDecisionSwitch: "evidence-decision-switch"
};

const evidenceScenarioIds = Object.values(scenarioIds).filter((id) => id.startsWith("evidence-"));
const scenarioDecisionIds = new Map(
  evidenceScenarioIds.map((id, index) => [id, `61000000-0000-0000-0000-${String(index + 1).padStart(12, "0")}`])
);

const scenarioParkingSessionIds = {
  [scenarioIds.seniorRepresentative]: "20000000-0000-0000-0000-000000000101",
  [scenarioIds.pwdRepresentative]: "20000000-0000-0000-0000-000000000106",
  [scenarioIds.residencyRequired]: "20000000-0000-0000-0000-000000000107",
  [scenarioIds.driverRequired]: "20000000-0000-0000-0000-000000000108",
  [scenarioIds.passengerRequired]: "20000000-0000-0000-0000-000000000109",
  [scenarioIds.missingEvidence]: "20000000-0000-0000-0000-000000000110",
  [scenarioIds.missing]: "20000000-0000-0000-0000-000000000102",
  [scenarioIds.malformed]: "20000000-0000-0000-0000-000000000103",
  [scenarioIds.unsupported]: "20000000-0000-0000-0000-000000000104",
  [scenarioIds.paranaque]: "20000000-0000-0000-0000-000000000105",
  [scenarioIds.approved]: "20000000-0000-0000-0000-000000000111",
  [scenarioIds.rejected]: "20000000-0000-0000-0000-000000000112"
};

function writeJson(response, statusCode, body, headers = {}) {
  const payload = JSON.stringify(body);
  response.writeHead(statusCode, {
    "Content-Type": "application/json; charset=utf-8",
    "Content-Length": Buffer.byteLength(payload),
    "Cache-Control": "no-store, private",
    "X-Content-Type-Options": "nosniff",
    ...headers
  });
  response.end(payload);
}

const fixtureSessionCookie = "operator_console_fixture_session";
const fixtureCsrfToken = "operator-console-fixture-csrf";
const fixturePermissions = [
  "statutory-discounts.session.lookup",
  "statutory-discounts.request.create",
  "statutory-discounts.review.queue.view",
  "statutory-discounts.review.detail.view",
  "statutory-discounts.review.queue.read",
  "statutory-discounts.review.detail.read",
  "statutory-discounts.decision.review",
  "statutory-discounts.decision.approve",
  "statutory-discounts.decision.reject",
  "statutory-discounts.evidence.review.view",
  "operator-console.policy-import-review.submit",
  "operator-console.policy-import-review.view-own",
  "operator-console.policy-import-review.review",
  "operator-console.policy-import-review.approve.legal",
  "operator-console.policy-import-review.approve.ops",
  "operator-console.policy-import-review.approve.qa",
  "operator-console.policy-import-review.approve.db",
  "operator-console.vendor-projection-health.view",
  "fiscal-issuance.status.read",
  "fiscal-issuance.void.audit.read"
];

function fixtureAuthMode(request) {
  const cookies = Object.fromEntries(
    (request.headers.cookie ?? "")
      .split(";")
      .map((part) => part.trim().split("=", 2))
      .filter(([name, value]) => name && value)
  );
  return cookies[fixtureSessionCookie] ?? "authenticated";
}

function sessionResponse() {
  return {
    outcome: "AUTHENTICATED",
    authenticated: true,
    session: {
      sessionReference: "71000000-0000-0000-0000-000000000001",
      userReference: "72000000-0000-0000-0000-000000000001",
      username: "review.operator",
      displayName: "Review Operator",
      audience: "OPERATOR_CONSOLE",
      assurance: "PASSWORD",
      privilegedAccount: false,
      passwordChangeRequired: false,
      mfaRequired: false,
      mfaSatisfied: false,
      authenticatedAt: "2026-08-08T08:00:00+08:00",
      lastSeenAt: "2026-08-08T08:05:00+08:00",
      idleExpiresAt: "2099-08-08T08:35:00+08:00",
      absoluteExpiresAt: "2099-08-08T16:00:00+08:00",
      permissions: fixturePermissions,
      siteReferences: ["73000000-0000-0000-0000-000000000001"],
      siteGroupReferences: ["74000000-0000-0000-0000-000000000001"],
      hasGlobalScope: false,
      deviceServiceIdentityReference: null,
      correlationId: "75000000-0000-0000-0000-000000000001"
    },
    aptSessionToken: null,
    errorCode: null,
    retryable: false,
    correlationId: "75000000-0000-0000-0000-000000000001"
  };
}

function sessionFailure(mode) {
  return {
    outcome: "REJECTED",
    authenticated: false,
    session: null,
    aptSessionToken: null,
    errorCode: mode === "expired" ? "SESSION_EXPIRED" : mode === "revoked" ? "SESSION_REVOKED" : "SESSION_REQUIRED",
    retryable: false,
    correlationId: "75000000-0000-0000-0000-000000000001"
  };
}

function setFixtureSession(response, mode) {
  response.setHeader(
    "Set-Cookie",
    `${fixtureSessionCookie}=${mode}; HttpOnly; SameSite=Strict; Path=/`
  );
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
        requirementStatus: "OPTIONAL",
        safeRequirementLabel: "Representative transaction allowed"
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
    statutoryDiscountDecisionCommandId: scenarioDecisionIds.get(id),
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
  [
    scenarioIds.seniorRepresentative,
    baseDraft(scenarioIds.seniorRepresentative, {
      entitlementType: "SENIOR_CITIZEN",
      policyName: "Senior Citizen representative parking discount",
      requiredEvidenceType: "SENIOR_CITIZEN_ID",
      requiredEvidenceTypes: ["SENIOR_CITIZEN_ID"],
      governingPolicy: governingPolicy({
        policyCode: "QC_SC_REPRESENTATIVE_2026",
        ordinanceTitle: "Quezon City Senior Citizen Parking Benefit",
        benefitType: "STATUTORY_DISCOUNT_VAT_EXEMPT",
        beneficiaryResidencyScope: "UNRESTRICTED_VALID_ID",
        requiredEvidenceTypes: [
          {
            evidenceType: "SENIOR_CITIZEN_ID",
            requirementStatus: "REQUIRED",
            safeRequirementLabel: "Masked statutory ID reference"
          },
          {
            evidenceType: "BENEFICIARY_PRESENCE",
            requirementStatus: "OPTIONAL",
            safeRequirementLabel: "Representative transaction allowed"
          }
        ]
      })
    })
  ],
  [
    scenarioIds.pwdRepresentative,
    baseDraft(scenarioIds.pwdRepresentative, {
      entitlementType: "PWD",
      policyName: "PWD representative parking discount",
      requiredEvidenceType: "PWD_ID",
      requiredEvidenceTypes: ["PWD_ID"],
      governingPolicy: governingPolicy({
        requiredEvidenceTypes: [
          {
            evidenceType: "PWD_ID",
            requirementStatus: "REQUIRED",
            safeRequirementLabel: "Masked PWD ID reference"
          },
          {
            evidenceType: "BENEFICIARY_PRESENCE",
            requirementStatus: "UNSPECIFIED",
            safeRequirementLabel: "Representative transaction allowed"
          }
        ]
      })
    })
  ],
  [
    scenarioIds.residencyRequired,
    baseDraft(scenarioIds.residencyRequired, {
      entitlementType: "SENIOR_CITIZEN",
      evidenceRequiredSatisfied: false,
      latestEvidenceStatus: "MISSING",
      policyName: "Residency-required Senior Citizen parking privilege",
      requiredEvidenceType: "SENIOR_CITIZEN_ID",
      requiredEvidenceTypes: ["SENIOR_CITIZEN_ID", "RESIDENCY_EVIDENCE"],
      governingPolicy: governingPolicy({
        policyCode: "QC_SC_RESIDENT_ONLY_2026",
        beneficiaryResidencyScope: "RESIDENT_ONLY",
        requiredEvidenceTypes: [
          {
            evidenceType: "SENIOR_CITIZEN_ID",
            requirementStatus: "REQUIRED",
            safeRequirementLabel: "Masked statutory ID reference"
          },
          {
            evidenceType: "RESIDENCY_EVIDENCE",
            requirementStatus: "REQUIRED",
            safeRequirementLabel: "Proof of residency"
          }
        ]
      })
    })
  ],
  [
    scenarioIds.driverRequired,
    baseDraft(scenarioIds.driverRequired, {
      evidenceRequiredSatisfied: false,
      latestEvidenceStatus: "MISSING",
      requiredEvidenceTypes: ["PWD_ID", "BENEFICIARY_DRIVER"],
      governingPolicy: governingPolicy({
        requiredEvidenceTypes: [
          {
            evidenceType: "PWD_ID",
            requirementStatus: "REQUIRED",
            safeRequirementLabel: "Masked PWD ID reference"
          },
          {
            evidenceType: "BENEFICIARY_DRIVER",
            requirementStatus: "REQUIRED",
            safeRequirementLabel: "Beneficiary is the driver"
          }
        ]
      })
    })
  ],
  [
    scenarioIds.passengerRequired,
    baseDraft(scenarioIds.passengerRequired, {
      evidenceRequiredSatisfied: false,
      latestEvidenceStatus: "MISSING",
      requiredEvidenceTypes: ["PWD_ID", "BENEFICIARY_PASSENGER"],
      governingPolicy: governingPolicy({
        requiredEvidenceTypes: [
          {
            evidenceType: "PWD_ID",
            requirementStatus: "REQUIRED",
            safeRequirementLabel: "Masked PWD ID reference"
          },
          {
            evidenceType: "BENEFICIARY_PASSENGER",
            requirementStatus: "REQUIRED",
            safeRequirementLabel: "Beneficiary is a passenger"
          }
        ]
      })
    })
  ],
  [
    scenarioIds.missingEvidence,
    baseDraft(scenarioIds.missingEvidence, {
      evidenceRequiredSatisfied: false,
      latestEvidenceStatus: "MISSING"
    })
  ],
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
  ],
  [
    scenarioIds.approved,
    baseDraft(scenarioIds.approved, {
      validationStatus: "APPROVED",
      evidenceRequiredSatisfied: true,
      latestEvidenceStatus: "VERIFIED_METADATA_ONLY",
      activity: ["Decision approved by reviewer."]
    })
  ],
  [
    scenarioIds.rejected,
    baseDraft(scenarioIds.rejected, {
      validationStatus: "REJECTED",
      evidenceRequiredSatisfied: true,
      latestEvidenceStatus: "VERIFIED_METADATA_ONLY",
      decisionReasonCode: "ID_NOT_VALID",
      activity: ["Decision rejected by reviewer."]
    })
  ],
  ...evidenceScenarioIds.map((id) => [
    id,
    baseDraft(id, {
      ticketReference: `OC-EVIDENCE-${id.replace("evidence-", "").toUpperCase()}`,
      plateNumber: "SAFE 005",
      siteName: "Synthetic Evidence Review Site",
      policyName: "Synthetic statutory evidence review fixture",
      requiredEvidenceTypes: ["PWD_ID"],
      evidenceCount: 1,
      activity: ["Synthetic evidence review fixture created for browser validation."]
    })
  ])
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

function reviewEvidence(draft) {
  const scenario = draft.draftId;
  const item = reviewEvidenceItem(scenario);
  return {
    statutoryDiscountDecisionCommandId: draft.statutoryDiscountDecisionCommandId,
    evidenceSetReference: "62000000-0000-0000-0000-000000000001",
    sourceChannel: "WEBPAY",
    decisionResultStatus: draft.validationStatus,
    reviewStatus: "PENDING",
    evidenceRequired: true,
    evidenceRecorded: true,
    setStatus: scenario === scenarioIds.evidenceStale ? "STALE" : "CURRENT",
    retentionStatus: "ACTIVE",
    deletionStatus:
      scenario === scenarioIds.evidenceDeleted
        ? "DELETED"
        : scenario === scenarioIds.evidencePendingDeletion
          ? "PENDING_DELETION"
          : "NONE",
    holdActive: scenario === scenarioIds.evidenceHold,
    replacementPosture:
      scenario === scenarioIds.evidenceReplaced
        ? "REPLACED"
        : scenario === scenarioIds.evidenceReplacementAllowed
          ? "REPLACEMENT_ALLOWED"
          : scenario === scenarioIds.evidenceReplacementDenied
            ? "REPLACEMENT_PROHIBITED"
            : "CURRENT",
    items: [item],
    correlationId: "63000000-0000-0000-0000-000000000001"
  };
}

function canonicalReviewItem(draft) {
  return {
    statutoryDiscountDecisionCommandId: draft.statutoryDiscountDecisionCommandId,
    requestReference: `STAT-${draft.statutoryDiscountDecisionCommandId.slice(0, 8)}`,
    parkingSessionId: draft.parkingSessionId,
    sourceChannel: "WEBPAY",
    siteId: draft.siteId,
    siteGroupId: draft.siteGroupId,
    ticketReference: draft.ticketReference,
    entitlementType: draft.entitlementType === "PWD" ? "PWD" : "SENIOR_CITIZEN",
    commandStatus: "AWAITING_REVIEW",
    decisionResultStatus: "NOT_DECIDED",
    reviewStatus: "PENDING_REVIEW",
    evidenceRequired: true,
    evidenceRecorded: true,
    submittedAt: draft.requestedAt
  };
}

function canonicalReviewDetail(draft) {
  return {
    ...canonicalReviewItem(draft),
    evidenceReferences: [{
      evidenceType: draft.entitlementType === "PWD" ? "PWD_ID" : "SENIOR_CITIZEN_ID",
      captureMethod: "UPLOAD",
      referenceNumberMasked: "***1234",
      verificationStatus: "RECORDED"
    }],
    requesterAttestation: true,
    sessionEligibilityStatus: "PENDING_REVIEW",
    payableBasisStatus: "NOT_YET_CREATED",
    payableBasisApplicationStatus: null,
    correlationId: "65000000-0000-0000-0000-000000000001"
  };
}

function reviewEvidenceItem(scenario) {
  const item = {
    evidenceItemReference: "64000000-0000-0000-0000-000000000001",
    documentType: "PWD_ID",
    itemRole: "PRIMARY_IDENTITY_DOCUMENT",
    declaredContentType: "image/png",
    authoritativeContentType: scenario === scenarioIds.evidenceJpeg ? "image/jpeg" : "image/png",
    contentLength: scenario === scenarioIds.evidenceJpeg
      ? syntheticJpegPreviewBytes.length
      : syntheticPngPreviewBytes.length,
    uploadStatus: "FINALIZED",
    validationStatus: "VALID",
    scanStatus: "CLEAN",
    reviewabilityStatus: "REVIEWABLE",
    bindingStatus: "BOUND",
    retentionStatus: "ACTIVE",
    deletionStatus: "NONE",
    holdActive: scenario === scenarioIds.evidenceHold,
    uploadedAt: "2026-08-01T08:00:00+08:00",
    finalizedAt: "2026-08-01T08:01:00+08:00",
    validatedAt: "2026-08-01T08:02:00+08:00",
    scannedAt: "2026-08-01T08:03:00+08:00",
    reviewableAt: "2026-08-01T08:04:00+08:00",
    previewPermitted: true,
    previewDenialReason: null
  };

  const denied = (previewDenialReason, overrides = {}) => ({
    ...item,
    previewPermitted: false,
    previewDenialReason,
    ...overrides
  });

  switch (scenario) {
    case scenarioIds.evidencePdf:
      return denied("STATUTORY_EVIDENCE_PREVIEW_UNSUPPORTED_MEDIA", {
        declaredContentType: "application/pdf",
        authoritativeContentType: "application/pdf"
      });
    case scenarioIds.evidenceValidationPending:
      return denied("STATUTORY_EVIDENCE_VALIDATION_PENDING", { validationStatus: "PENDING" });
    case scenarioIds.evidenceValidationFailed:
      return denied("STATUTORY_EVIDENCE_VALIDATION_FAILED", { validationStatus: "FAILED" });
    case scenarioIds.evidenceScanPending:
      return denied("STATUTORY_EVIDENCE_SCAN_PENDING", { scanStatus: "PENDING" });
    case scenarioIds.evidenceScannerOutage:
      return denied("STATUTORY_EVIDENCE_SCANNER_UNAVAILABLE", { scanStatus: "UNAVAILABLE" });
    case scenarioIds.evidenceMalware:
      return denied("STATUTORY_EVIDENCE_MALWARE_DETECTED", { scanStatus: "MALWARE_DETECTED" });
    case scenarioIds.evidenceNotReviewable:
      return denied("STATUTORY_EVIDENCE_NOT_REVIEWABLE", { reviewabilityStatus: "NOT_REVIEWABLE" });
    case scenarioIds.evidenceStale:
      return denied("STATUTORY_EVIDENCE_STALE", { bindingStatus: "STALE" });
    case scenarioIds.evidenceReplaced:
      return denied("REPLACED", { bindingStatus: "REPLACED" });
    case scenarioIds.evidenceDeleted:
      return denied("DELETED", { deletionStatus: "DELETED" });
    case scenarioIds.evidencePendingDeletion:
      return denied("STATUTORY_EVIDENCE_DELETION_IN_PROGRESS", { deletionStatus: "PENDING_DELETION" });
    default:
      return item;
  }
}

const syntheticJpegPreviewBytes = await readFile(
  join(root, "e2e", "fixtures", "assets", "synthetic-evidence-landscape.jpg")
);
const syntheticPngPreviewBytes = await readFile(
  join(root, "e2e", "fixtures", "assets", "synthetic-evidence-portrait.png")
);

function writePreview(response, contentType) {
  const content = contentType === "image/jpeg" ? syntheticJpegPreviewBytes : syntheticPngPreviewBytes;
  response.writeHead(200, {
    "Content-Type": contentType,
    "Content-Length": content.length,
    "Cache-Control": "no-store, private, max-age=0",
    Pragma: "no-cache",
    "X-Content-Type-Options": "nosniff",
    "Content-Disposition": "inline",
    "Referrer-Policy": "no-referrer"
  });
  response.end(content);
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

const previewAttempts = new Map();

const server = createServer(async (request, response) => {
  try {
    const url = new URL(request.url ?? "/", `http://${request.headers.host}`);

    if (url.searchParams.has("auth")) {
      const mode = url.searchParams.get("auth") ?? "logged-out";
      setFixtureSession(response, mode);
      response.writeHead(302, { Location: url.pathname });
      response.end();
      return;
    }

    if (url.pathname === "/__fixture/health") {
      writeJson(response, 200, { status: "READY", scenarios: Object.values(scenarioIds) });
      return;
    }

    if (url.pathname === "/__fixture/scenarios") {
      writeJson(response, 200, { scenarios: Object.values(scenarioIds) });
      return;
    }

    if (url.pathname === "/v1/human-authentication/session" && request.method === "GET") {
      const mode = fixtureAuthMode(request);
      writeJson(
        response,
        mode === "authenticated" ? 200 : 401,
        mode === "authenticated" ? sessionResponse() : sessionFailure(mode),
        { "X-CSRF-Token": fixtureCsrfToken }
      );
      return;
    }

    if (url.pathname === "/v1/human-authentication/login" && request.method === "POST") {
      const body = await readJson(request);
      if (body.audience !== "OPERATOR_CONSOLE") {
        writeJson(response, 400, sessionFailure("logged-out"));
        return;
      }
      if (body.username === "throttled.operator") {
        writeJson(response, 429, { ...sessionFailure("logged-out"), errorCode: "AUTHENTICATION_THROTTLED", retryable: true });
        return;
      }
      if (body.username !== "review.operator" || body.password !== "operator-password") {
        writeJson(response, 401, { ...sessionFailure("logged-out"), errorCode: "INVALID_CREDENTIALS" });
        return;
      }
      setFixtureSession(response, "authenticated");
      writeJson(response, 200, sessionResponse(), { "X-CSRF-Token": fixtureCsrfToken });
      return;
    }

    if (url.pathname === "/v1/human-authentication/logout" && request.method === "POST") {
      if (request.headers["x-csrf-token"] !== fixtureCsrfToken) {
        writeJson(response, 400, { ...sessionFailure("logged-out"), errorCode: "CSRF_VALIDATION_FAILED" });
        return;
      }
      setFixtureSession(response, "logged-out");
      writeJson(response, 200, {
        ...sessionFailure("logged-out"),
        outcome: "LOGGED_OUT",
        errorCode: null
      });
      return;
    }

    if (url.pathname.startsWith("/v1/ops/operator-console/") && fixtureAuthMode(request) !== "authenticated") {
      writeJson(response, 401, sessionFailure(fixtureAuthMode(request)));
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

    if (url.pathname === "/v1/ops/operator-console/statutory-discounts/reviews" && request.method === "GET") {
      const requestedOrigin = url.searchParams.get("sourceChannel");
      const requestedSearch = url.searchParams.get("search")?.trim().toUpperCase();
      const candidates = Array.from(drafts.values()).filter((draft) => draft.statutoryDiscountDecisionCommandId);
      const items = candidates
        .map(canonicalReviewItem)
        .filter((item) => (!requestedOrigin || item.sourceChannel === requestedOrigin) &&
          (!requestedSearch || JSON.stringify(item).toUpperCase().includes(requestedSearch)));
      writeJson(response, 200, {
        items: items.slice(0, 25), totalCount: items.length, page: 1, pageSize: 25,
        hasMore: items.length > 25, correlationId: "65000000-0000-0000-0000-000000000002"
      });
      return;
    }

    const canonicalDecisionMatch = url.pathname.match(
      /^\/v1\/ops\/operator-console\/statutory-discounts\/reviews\/([^/]+)\/decision$/
    );
    if (canonicalDecisionMatch && request.method === "POST") {
      const decisionId = decodeURIComponent(canonicalDecisionMatch[1]);
      const draft = Array.from(drafts.values()).find((item) => item.statutoryDiscountDecisionCommandId === decisionId);
      const body = await readJson(request);
      if (!draft) {
        writeJson(response, 404, { errorCode: "NOT_FOUND", message: "Fixture review not found." });
        return;
      }
      writeJson(response, 200, {
        decisionAccepted: true,
        decisionPersisted: true,
        currentValidationStatus: body.decision === "REJECT" ? "REJECTED" : "APPROVED",
        decision: body.decision,
        alreadyDecided: false,
        decisionChanged: true,
        correlationId: "65000000-0000-0000-0000-000000000003"
      });
      return;
    }

    const canonicalDetailMatch = url.pathname.match(
      /^\/v1\/ops\/operator-console\/statutory-discounts\/reviews\/([^/]+)$/
    );
    if (canonicalDetailMatch && request.method === "GET") {
      const decisionId = decodeURIComponent(canonicalDetailMatch[1]);
      const draft = Array.from(drafts.values()).find((item) => item.statutoryDiscountDecisionCommandId === decisionId);
      if (!draft) {
        writeJson(response, 404, { errorCode: "NOT_FOUND", message: "Fixture review not found." });
        return;
      }
      writeJson(response, 200, canonicalReviewDetail(draft));
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

    const reviewPreviewMatch = url.pathname.match(
      /^\/v1\/ops\/operator-console\/statutory-discounts\/reviews\/([^/]+)\/evidence\/([^/]+)\/preview$/
    );
    if (reviewPreviewMatch && request.method === "GET") {
      const decisionId = decodeURIComponent(reviewPreviewMatch[1]);
      const draft = Array.from(drafts.values()).find((item) => item.statutoryDiscountDecisionCommandId === decisionId);
      if (!draft || draft.draftId === scenarioIds.evidencePreviewNotFound) {
        writeJson(response, 404, { errorCode: "NOT_FOUND", detail: "Synthetic object key must not be rendered." });
        return;
      }
      if (draft.draftId === scenarioIds.evidenceCrossSite || draft.draftId === scenarioIds.evidenceCrossSiteGroup) {
        writeJson(response, 404, { errorCode: "NOT_FOUND", detail: "Scope detail must not be rendered." });
        return;
      }
      if (draft.draftId === scenarioIds.evidenceStorageOutage) {
        const attempts = previewAttempts.get(decisionId) ?? 0;
        previewAttempts.set(decisionId, attempts + 1);
        if (attempts === 0) {
          writeJson(response, 503, {
            errorCode: "OPERATOR_CONSOLE_EVIDENCE_PREVIEW_STORAGE_UNAVAILABLE",
            detail: "Synthetic provider endpoint and bucket must not be rendered."
          });
          return;
        }
      }
      if (draft.draftId === scenarioIds.evidenceDecisionSwitch) {
        await new Promise((resolve) => setTimeout(resolve, 700));
      }

      writePreview(response, draft.draftId === scenarioIds.evidenceJpeg ? "image/jpeg" : "image/png");
      return;
    }

    const reviewMetadataMatch = url.pathname.match(
      /^\/v1\/ops\/operator-console\/statutory-discounts\/reviews\/([^/]+)\/evidence$/
    );
    if (reviewMetadataMatch && request.method === "GET") {
      const decisionId = decodeURIComponent(reviewMetadataMatch[1]);
      const draft = Array.from(drafts.values()).find((item) => item.statutoryDiscountDecisionCommandId === decisionId);
      if (!draft || draft.draftId === scenarioIds.evidenceMetadataNotFound) {
        writeJson(response, 404, { errorCode: "NOT_FOUND", detail: "Synthetic database row must not be rendered." });
        return;
      }
      if (draft.draftId === scenarioIds.evidencePermissionDenied) {
        writeJson(response, 403, {
          errorCode: "OPERATOR_CONSOLE_STATUTORY_EVIDENCE_REVIEW_FORBIDDEN",
          detail: "Synthetic permission diagnostic must not be rendered."
        });
        return;
      }
      if (draft.draftId === scenarioIds.evidenceCrossSite || draft.draftId === scenarioIds.evidenceCrossSiteGroup) {
        writeJson(response, 404, { errorCode: "NOT_FOUND", detail: "Synthetic scope diagnostic must not be rendered." });
        return;
      }

      writeJson(response, 200, reviewEvidence(draft));
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
