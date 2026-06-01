import type {
  OperatorConsoleApiError,
  StatutoryDiscountDraftDetail,
  StatutoryDiscountQueueItem
} from "./types";

export interface OperatorConsoleApiClient {
  listStatutoryDiscountDrafts(): Promise<StatutoryDiscountQueueItem[]>;
  getStatutoryDiscountDraft(draftId: string): Promise<StatutoryDiscountDraftDetail>;
}

const seniorNationalPolicy = {
  kind: "national-fallback" as const,
  title: "National fallback policy",
  operatorSummary:
    "Use the national Senior Citizen parking discount policy because no verified local ordinance overrides it for this site.",
  policyResolutionBasis: "NATIONAL_FALLBACK",
  policyCode: "RA9994-SENIOR-PARKING",
  policyName: "Senior Citizen Parking Benefit",
  legalBasisReference: "Republic Act No. 9994",
  nationalLawReference: "RA 9994",
  verificationStatus: "VERIFIED",
  benefitType: "VAT exempt and statutory discount",
  evidenceRequired: false
};

const pwdLocalPolicy = {
  kind: "verified-local" as const,
  title: "Verified local policy",
  operatorSummary:
    "Apply the verified city policy for PWD parking benefits. Local policy is active and linked to this site jurisdiction.",
  policyResolutionBasis: "VERIFIED_LOCAL_POLICY",
  policyCode: "QC-PWD-PARKING-2026",
  policyName: "Quezon City PWD Parking Benefit",
  legalBasisReference: "RA 10754 and verified local ordinance",
  ordinanceReference: "QC Ordinance 2026-04",
  nationalLawReference: "RA 10754",
  verificationStatus: "VERIFIED",
  benefitType: "Local policy discount",
  evidenceRequired: true
};

const blockedLocalPolicy = {
  kind: "blocked-unverified-local" as const,
  title: "Unverified local policy blocked",
  operatorSummary:
    "A local ordinance candidate exists, but it is not verified. The draft must not be approved using this local policy.",
  policyResolutionBasis: "LOCAL_POLICY_BLOCKED",
  policyCode: "LOCAL-UNVERIFIED",
  policyName: "Unverified Local Parking Benefit",
  legalBasisReference: "Local policy verification required",
  ordinanceReference: "Pending verification",
  verificationStatus: "UNVERIFIED",
  benefitType: "Blocked local discount",
  evidenceRequired: true,
  ineligibilityReason: "Local policy is not verified for operator use."
};

const unsupportedEntitlementPolicy = {
  kind: "unsupported-entitlement" as const,
  title: "Unsupported entitlement",
  operatorSummary:
    "The entitlement type is not supported by the statutory discount workflow. Do not approve the draft.",
  policyResolutionBasis: "UNSUPPORTED_ENTITLEMENT",
  verificationStatus: "NOT_APPLICABLE",
  benefitType: "Not supported",
  evidenceRequired: false,
  ineligibilityReason: "Only Senior Citizen and PWD are supported."
};

const missingJurisdictionPolicy = {
  kind: "missing-site-jurisdiction" as const,
  title: "Missing site jurisdiction",
  operatorSummary:
    "The site does not have a resolved jurisdiction. Policy cannot be selected until site setup is corrected.",
  policyResolutionBasis: "SITE_JURISDICTION_MISSING",
  verificationStatus: "NOT_RESOLVED",
  benefitType: "Policy unavailable",
  evidenceRequired: false,
  ineligibilityReason: "Site jurisdiction is missing."
};

const mockDrafts: StatutoryDiscountDraftDetail[] = [
  {
    draftId: "47000000-0000-0000-0000-000000000008",
    parkingSessionId: "25000000-0000-0000-0000-000000000001",
    ticketReference: "STAT-OP-SESSION-0001",
    plateNumber: "ABC 1234",
    siteName: "Terminal Parking / North Exit",
    laneName: "North Exit Lane 2",
    entitlementType: "Senior Citizen",
    status: "Pending Review",
    requestedAt: "2026-06-01T08:15:00+08:00",
    requestedBy: "operator.shift-a",
    parkingStartedAt: "2026-06-01T06:55:00+08:00",
    originalTariffAmount: "PHP 180.00",
    payableBasisPreview: "Pending backend payable-basis application",
    currentPaymentStatus: "Unpaid",
    maskedIdReference: "SC-****-1934",
    issuingAuthority: "OSCA",
    policyContext: seniorNationalPolicy,
    auditActivity: [
      "Draft created after access evaluation.",
      "Policy resolved through national fallback.",
      "Awaiting operator decision."
    ]
  },
  {
    draftId: "47000000-0000-0000-0000-000000000009",
    parkingSessionId: "25000000-0000-0000-0000-000000000002",
    ticketReference: "STAT-OP-SESSION-0002",
    plateNumber: "PWD 2048",
    siteName: "City Center Parking / Basement",
    laneName: "Basement Exit Lane 1",
    entitlementType: "PWD",
    status: "Pending Review",
    requestedAt: "2026-06-01T08:42:00+08:00",
    requestedBy: "operator.shift-a",
    parkingStartedAt: "2026-06-01T07:10:00+08:00",
    originalTariffAmount: "PHP 220.00",
    payableBasisPreview: "Evidence required before decision",
    currentPaymentStatus: "Unpaid",
    maskedIdReference: "PWD-****-8721",
    issuingAuthority: "PDAO",
    policyContext: pwdLocalPolicy,
    auditActivity: [
      "Draft created after access evaluation.",
      "Verified local policy resolved.",
      "Evidence required flag is active."
    ]
  },
  {
    draftId: "47000000-0000-0000-0000-000000000010",
    parkingSessionId: "25000000-0000-0000-0000-000000000003",
    ticketReference: "STAT-OP-SESSION-0003",
    plateNumber: "LOC 8841",
    siteName: "Riverside Parking / Exit",
    laneName: "Riverside Exit Lane",
    entitlementType: "Senior Citizen",
    status: "Blocked",
    requestedAt: "2026-06-01T09:05:00+08:00",
    requestedBy: "operator.shift-b",
    parkingStartedAt: "2026-06-01T08:01:00+08:00",
    originalTariffAmount: "PHP 90.00",
    payableBasisPreview: "Blocked",
    currentPaymentStatus: "Unpaid",
    maskedIdReference: "SC-****-5510",
    issuingAuthority: "OSCA",
    policyContext: blockedLocalPolicy,
    auditActivity: [
      "Draft created after access evaluation.",
      "Unverified local policy detected.",
      "Decision blocked pending policy verification."
    ]
  },
  {
    draftId: "47000000-0000-0000-0000-000000000011",
    parkingSessionId: "25000000-0000-0000-0000-000000000004",
    ticketReference: "STAT-OP-SESSION-0004",
    plateNumber: "UNK 2026",
    siteName: "Terminal Parking / North Exit",
    laneName: "North Exit Lane 1",
    entitlementType: "Unsupported",
    status: "Blocked",
    requestedAt: "2026-06-01T09:22:00+08:00",
    requestedBy: "operator.shift-b",
    parkingStartedAt: "2026-06-01T08:40:00+08:00",
    originalTariffAmount: "PHP 60.00",
    payableBasisPreview: "Unsupported entitlement",
    currentPaymentStatus: "Unpaid",
    maskedIdReference: "ENT-****-0000",
    issuingAuthority: "Unknown",
    policyContext: unsupportedEntitlementPolicy,
    auditActivity: ["Unsupported entitlement submitted.", "Draft blocked before decision."]
  },
  {
    draftId: "47000000-0000-0000-0000-000000000012",
    parkingSessionId: "25000000-0000-0000-0000-000000000005",
    ticketReference: "STAT-OP-SESSION-0005",
    plateNumber: "JUR 404",
    siteName: "Unmapped Site / Exit",
    laneName: "Exit Lane",
    entitlementType: "PWD",
    status: "Blocked",
    requestedAt: "2026-06-01T09:41:00+08:00",
    requestedBy: "operator.shift-c",
    parkingStartedAt: "2026-06-01T09:00:00+08:00",
    originalTariffAmount: "PHP 120.00",
    payableBasisPreview: "Policy unavailable",
    currentPaymentStatus: "Unpaid",
    maskedIdReference: "PWD-****-4412",
    issuingAuthority: "PDAO",
    policyContext: missingJurisdictionPolicy,
    auditActivity: ["Site jurisdiction missing.", "Draft blocked before policy selection."]
  }
];

function toQueueItem(draft: StatutoryDiscountDraftDetail): StatutoryDiscountQueueItem {
  return {
    draftId: draft.draftId,
    parkingSessionId: draft.parkingSessionId,
    ticketReference: draft.ticketReference,
    plateNumber: draft.plateNumber,
    siteName: draft.siteName,
    entitlementType: draft.entitlementType,
    status: draft.status,
    requestedAt: draft.requestedAt,
    requestedBy: draft.requestedBy,
    policyContext: draft.policyContext
  };
}

function delay() {
  return new Promise((resolve) => window.setTimeout(resolve, 80));
}

export function createOperatorConsoleApiClient(): OperatorConsoleApiClient {
  return createMockOperatorConsoleApiClient();
}

export function createMockOperatorConsoleApiClient(
  options: {
    listError?: OperatorConsoleApiError;
    detailError?: OperatorConsoleApiError;
    empty?: boolean;
  } = {}
): OperatorConsoleApiClient {
  return {
    async listStatutoryDiscountDrafts() {
      await delay();
      if (options.listError) {
        throw options.listError;
      }

      return options.empty ? [] : mockDrafts.map(toQueueItem);
    },

    async getStatutoryDiscountDraft(draftId) {
      await delay();
      if (options.detailError) {
        throw options.detailError;
      }

      const draft = mockDrafts.find((item) => item.draftId === draftId);
      if (!draft) {
        throw {
          status: "not-found",
          message: "Statutory discount draft was not found.",
          errorCode: "DRAFT_NOT_FOUND"
        } satisfies OperatorConsoleApiError;
      }

      return draft;
    }
  };
}

export function mapApiError(error: unknown): OperatorConsoleApiError {
  if (isApiError(error)) {
    return error;
  }

  return {
    status: "error",
    message: "Operator Console statutory discount data could not be loaded."
  };
}

function isApiError(error: unknown): error is OperatorConsoleApiError {
  return (
    typeof error === "object" &&
    error !== null &&
    "status" in error &&
    "message" in error &&
    (error as { status?: unknown }).status !== undefined
  );
}
