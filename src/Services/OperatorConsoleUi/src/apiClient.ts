import type {
  DraftStatus,
  EntitlementType,
  OperatorConsoleApiError,
  StatutoryDiscountDecisionInput,
  StatutoryDiscountDecisionResult,
  StatutoryDiscountDraftDetail,
  StatutoryDiscountPolicyContext,
  StatutoryDiscountQueueItem
} from "./types";

export interface OperatorConsoleApiClient {
  listStatutoryDiscountDrafts(): Promise<StatutoryDiscountQueueItem[]>;
  getStatutoryDiscountDraft(draftId: string): Promise<StatutoryDiscountDraftDetail>;
  submitStatutoryDiscountDecision(input: StatutoryDiscountDecisionInput): Promise<StatutoryDiscountDecisionResult>;
}

interface QueueResponse {
  items: QueueItemDto[];
}

interface QueueItemDto {
  draftId: string;
  parkingSessionId: string;
  ticketReference?: string | null;
  plateNumber?: string | null;
  siteId: string;
  siteName?: string | null;
  entitlementType: string;
  validationStatus: string;
  evidenceRequired: boolean;
  policyResolutionBasis?: string | null;
  policyCode?: string | null;
  policyName?: string | null;
  originalAmountMinorUnits?: number | null;
  payableAmountMinorUnits?: number | null;
  currencyCode?: string | null;
  requestedAt: string;
  requestedByUserId?: string | null;
  blockedReason?: string | null;
}

interface DetailDto extends QueueItemDto {
  siteGroupId: string;
  evidenceCaptured: boolean;
  validatedAt?: string | null;
  validatedByUserId?: string | null;
  decisionReasonCode?: string | null;
  failureReasonCode?: string | null;
  legalBasisReference?: string | null;
  ordinanceReference?: string | null;
  nationalLawReference?: string | null;
  verificationStatus?: string | null;
  benefitType?: string | null;
  freeDurationMinutes?: number | null;
  succeedingHoursDiscountRule?: string | null;
  discountBaseScope?: string | null;
  stackingPolicy?: string | null;
  originalTariffSnapshotId?: string | null;
  payableBasisApplicationId?: string | null;
  payableBasisApplicationStatus?: string | null;
  statutoryDiscountAmountMinorUnits?: number | null;
  activity: string[];
}

interface DecisionResponse {
  accessAllowed: boolean;
  accessDecision: string;
  accessDenialReasons: string[];
  decisionAccepted: boolean;
  decisionPersisted: boolean;
  currentValidationStatus?: string | null;
  ineligibilityReason?: string | null;
  errorCode?: string | null;
}

const defaultOperatorContext = {
  userId: import.meta.env.VITE_OPERATOR_CONSOLE_USER_ID ?? "77000000-0000-0000-0000-000000000010",
  operatorDeviceBindingId: import.meta.env.VITE_OPERATOR_CONSOLE_DEVICE_BINDING_ID ?? null,
  operatorShiftId: import.meta.env.VITE_OPERATOR_CONSOLE_SHIFT_ID ?? null
};

export function createOperatorConsoleApiClient(): OperatorConsoleApiClient {
  return createHttpOperatorConsoleApiClient({
    baseUrl: import.meta.env.VITE_CENTRAL_PMS_BASE_URL ?? ""
  });
}

export function createHttpOperatorConsoleApiClient(options: { baseUrl?: string } = {}): OperatorConsoleApiClient {
  const baseUrl = options.baseUrl?.replace(/\/$/, "") ?? "";

  return {
    async listStatutoryDiscountDrafts() {
      const correlationId = newCorrelationId();
      const response = await fetch(
        `${baseUrl}/v1/ops/operator-console/statutory-discounts/drafts?correlationId=${correlationId}`,
        { headers: { "X-Correlation-Id": correlationId } }
      );
      const body = await parseResponse<QueueResponse>(response);
      return body.items.map(toQueueItem);
    },

    async getStatutoryDiscountDraft(draftId) {
      const correlationId = newCorrelationId();
      const response = await fetch(
        `${baseUrl}/v1/ops/operator-console/statutory-discounts/drafts/${encodeURIComponent(draftId)}?correlationId=${correlationId}`,
        { headers: { "X-Correlation-Id": correlationId } }
      );
      return toDraftDetail(await parseResponse<DetailDto>(response));
    },

    async submitStatutoryDiscountDecision(input) {
      const correlationId = newCorrelationId();
      const response = await fetch(
        `${baseUrl}/v1/ops/operator-console/statutory-discounts/${encodeURIComponent(input.draftId)}/decision`,
        {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            "X-Correlation-Id": correlationId
          },
          body: JSON.stringify({
            userId: defaultOperatorContext.userId,
            operatorDeviceBindingId: defaultOperatorContext.operatorDeviceBindingId,
            siteId: input.siteId ?? null,
            siteGroupId: input.siteGroupId ?? null,
            operatorShiftId: defaultOperatorContext.operatorShiftId,
            decision: input.decision,
            decisionReasonCode: input.decision === "REJECT" ? input.reasonCode : null,
            decisionNotes: input.notes ?? null,
            reviewerAttestation: true,
            idempotencyKey: `operator-console-ui-${input.decision.toLowerCase()}-${input.draftId}-${correlationId}`,
            correlationId
          })
        }
      );
      const body = await parseResponse<DecisionResponse>(response);
      return {
        accepted: body.decisionAccepted,
        persisted: body.decisionPersisted,
        currentStatus: body.currentValidationStatus ? mapStatus(body.currentValidationStatus) : undefined,
        errorCode: body.errorCode ?? undefined,
        message: decisionMessage(body)
      };
    }
  };
}

export function createMockOperatorConsoleApiClient(
  options: {
    drafts?: StatutoryDiscountDraftDetail[];
    listError?: OperatorConsoleApiError;
    detailError?: OperatorConsoleApiError;
    decisionError?: OperatorConsoleApiError;
    empty?: boolean;
    onDecision?: (input: StatutoryDiscountDecisionInput) => void;
  } = {}
): OperatorConsoleApiClient {
  const drafts = options.drafts ?? mockDrafts;
  return {
    async listStatutoryDiscountDrafts() {
      await delay();
      if (options.listError) {
        throw options.listError;
      }

      return options.empty ? [] : drafts.map(toQueueFromDetail);
    },

    async getStatutoryDiscountDraft(draftId) {
      await delay();
      if (options.detailError) {
        throw options.detailError;
      }

      const draft = drafts.find((item) => item.draftId === draftId);
      if (!draft) {
        throw {
          status: "not-found",
          message: "Statutory discount draft was not found.",
          errorCode: "DRAFT_NOT_FOUND"
        } satisfies OperatorConsoleApiError;
      }

      return draft;
    },

    async submitStatutoryDiscountDecision(input) {
      await delay();
      if (options.decisionError) {
        throw options.decisionError;
      }

      options.onDecision?.(input);
      return {
        accepted: true,
        persisted: true,
        currentStatus: input.decision === "APPROVE" ? "Approved" : "Rejected",
        message: input.decision === "APPROVE" ? "Decision approved." : "Decision rejected."
      };
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

async function parseResponse<T>(response: Response): Promise<T> {
  const text = await response.text();
  const body = text ? JSON.parse(text) : {};
  if (response.ok) {
    return body as T;
  }

  throw {
    status: response.status === 404 ? "not-found" : response.status === 401 || response.status === 403 ? "access-denied" : "error",
    message: body.message ?? body.errorCode ?? "Operator Console request failed.",
    errorCode: body.errorCode
  } satisfies OperatorConsoleApiError;
}

function toQueueItem(item: QueueItemDto): StatutoryDiscountQueueItem {
  const policyContext = toPolicyContext(item);
  return {
    draftId: item.draftId,
    parkingSessionId: item.parkingSessionId,
    ticketReference: item.ticketReference ?? "Not available",
    plateNumber: item.plateNumber ?? "Not available",
    siteId: item.siteId,
    siteName: item.siteName ?? "Site not named",
    entitlementType: mapEntitlement(item.entitlementType),
    status: mapStatus(item.validationStatus),
    requestedAt: item.requestedAt,
    requestedBy: item.requestedByUserId ?? "Unknown operator",
    policyContext,
    originalAmountMinorUnits: item.originalAmountMinorUnits ?? undefined,
    payableAmountMinorUnits: item.payableAmountMinorUnits ?? undefined,
    currencyCode: item.currencyCode ?? undefined
  };
}

function toDraftDetail(item: DetailDto): StatutoryDiscountDraftDetail {
  const queueItem = toQueueItem(item);
  return {
    ...queueItem,
    siteGroupId: item.siteGroupId,
    laneName: "Not available",
    parkingStartedAt: item.requestedAt,
    originalTariffAmount: formatMoney(item.originalAmountMinorUnits, item.currencyCode),
    payableBasisPreview: payableBasisPreview(item),
    currentPaymentStatus: "Read-only in this module",
    maskedIdReference: "Evidence metadata only",
    issuingAuthority: "Not available",
    evidenceCaptured: item.evidenceCaptured,
    statutoryDiscountAmountMinorUnits: item.statutoryDiscountAmountMinorUnits ?? undefined,
    payableBasisApplicationStatus: item.payableBasisApplicationStatus ?? undefined,
    auditActivity: item.activity.length > 0 ? item.activity : ["No activity history is available yet."]
  };
}

function toPolicyContext(item: QueueItemDto | DetailDto): StatutoryDiscountPolicyContext {
  const basis = item.policyResolutionBasis ?? "UNKNOWN";
  const detail = item as DetailDto;
  const kind = policyKind(basis, detail.verificationStatus);
  return {
    kind,
    title: policyTitle(kind),
    operatorSummary: policySummary(kind),
    policyResolutionBasis: basis,
    policyCode: item.policyCode ?? undefined,
    policyName: item.policyName ?? undefined,
    legalBasisReference: detail.legalBasisReference ?? undefined,
    ordinanceReference: detail.ordinanceReference ?? undefined,
    nationalLawReference: detail.nationalLawReference ?? undefined,
    verificationStatus: detail.verificationStatus ?? undefined,
    benefitType: detail.benefitType ?? undefined,
    evidenceRequired: item.evidenceRequired,
    ineligibilityReason: item.blockedReason ?? detail.failureReasonCode ?? undefined
  };
}

function policyKind(basis: string, verificationStatus?: string | null): StatutoryDiscountPolicyContext["kind"] {
  if (basis === "LOCAL_POLICY_BLOCKED" || verificationStatus?.includes("UNVERIFIED")) {
    return "blocked-unverified-local";
  }

  if (basis.includes("LOCAL")) {
    return "verified-local";
  }

  if (basis === "UNSUPPORTED_ENTITLEMENT") {
    return "unsupported-entitlement";
  }

  if (basis === "SITE_JURISDICTION_MISSING") {
    return "missing-site-jurisdiction";
  }

  return "national-fallback";
}

function policyTitle(kind: StatutoryDiscountPolicyContext["kind"]) {
  return {
    "national-fallback": "National fallback policy",
    "verified-local": "Verified local policy",
    "blocked-unverified-local": "Unverified local policy blocked",
    "unsupported-entitlement": "Unsupported entitlement",
    "missing-site-jurisdiction": "Missing site jurisdiction"
  }[kind];
}

function policySummary(kind: StatutoryDiscountPolicyContext["kind"]) {
  return {
    "national-fallback": "Use the stored national statutory policy because no verified local policy overrides it for this draft.",
    "verified-local": "Use the stored verified local policy linked to the site jurisdiction.",
    "blocked-unverified-local": "A local policy is not verified for operator use. Do not approve using that local policy.",
    "unsupported-entitlement": "The entitlement type is not supported by the statutory discount workflow.",
    "missing-site-jurisdiction": "The site does not have a resolved jurisdiction for policy selection."
  }[kind];
}

function payableBasisPreview(item: DetailDto) {
  if (item.payableBasisApplicationStatus) {
    return `${item.payableBasisApplicationStatus} - ${formatMoney(item.payableAmountMinorUnits, item.currencyCode)}`;
  }

  if (item.payableAmountMinorUnits !== undefined && item.payableAmountMinorUnits !== null) {
    return `Preview ${formatMoney(item.payableAmountMinorUnits, item.currencyCode)}`;
  }

  return item.evidenceRequired && !item.evidenceCaptured ? "Evidence upload pending" : "Not available";
}

function mapEntitlement(value: string): EntitlementType {
  if (value === "PWD") {
    return "PWD";
  }

  if (value === "SENIOR_CITIZEN") {
    return "Senior Citizen";
  }

  return "Unsupported";
}

function mapStatus(value: string): DraftStatus {
  const statuses: Record<string, DraftStatus> = {
    REQUESTED: "Requested",
    PENDING_OPERATOR_REVIEW: "Pending Review",
    APPROVED: "Approved",
    REJECTED: "Rejected",
    EXPIRED: "Expired",
    BLOCKED: "Blocked"
  };

  return statuses[value] ?? "Blocked";
}

function decisionMessage(body: DecisionResponse) {
  if (!body.accessAllowed) {
    return `Access denied: ${body.accessDenialReasons.join(", ") || body.accessDecision}`;
  }

  if (!body.decisionAccepted) {
    return body.errorCode ?? body.ineligibilityReason ?? "Decision was not accepted.";
  }

  return body.decisionPersisted ? "Decision saved." : "Decision accepted but not persisted.";
}

function formatMoney(minorUnits?: number | null, currencyCode?: string | null) {
  if (minorUnits === undefined || minorUnits === null) {
    return "Not available";
  }

  return `${currencyCode ?? "PHP"} ${(minorUnits / 100).toFixed(2)}`;
}

function toQueueFromDetail(draft: StatutoryDiscountDraftDetail): StatutoryDiscountQueueItem {
  return {
    draftId: draft.draftId,
    parkingSessionId: draft.parkingSessionId,
    ticketReference: draft.ticketReference,
    plateNumber: draft.plateNumber,
    siteId: draft.siteId,
    siteGroupId: draft.siteGroupId,
    siteName: draft.siteName,
    entitlementType: draft.entitlementType,
    status: draft.status,
    requestedAt: draft.requestedAt,
    requestedBy: draft.requestedBy,
    policyContext: draft.policyContext,
    originalAmountMinorUnits: draft.originalAmountMinorUnits,
    payableAmountMinorUnits: draft.payableAmountMinorUnits,
    currencyCode: draft.currencyCode
  };
}

function newCorrelationId() {
  return globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

function delay() {
  return new Promise((resolve) => window.setTimeout(resolve, 30));
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

const seniorNationalPolicy = {
  kind: "national-fallback" as const,
  title: "National fallback policy",
  operatorSummary:
    "Use the national Senior Citizen parking discount policy because no verified local ordinance overrides it for this site.",
  policyResolutionBasis: "NATIONAL_LAW_FALLBACK",
  policyCode: "PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK",
  policyName: "Senior Citizen Parking Benefit",
  legalBasisReference: "Republic Act No. 9994",
  nationalLawReference: "RA 9994",
  verificationStatus: "VERIFIED_OFFICIAL",
  benefitType: "STATUTORY_DISCOUNT_VAT_EXEMPT",
  evidenceRequired: false
};

const pwdLocalPolicy = {
  kind: "verified-local" as const,
  title: "Verified local policy",
  operatorSummary:
    "Apply the verified city policy for PWD parking benefits. Local policy is active and linked to this site jurisdiction.",
  policyResolutionBasis: "LOCAL_ORDINANCE_APPLIED",
  policyCode: "QC-PWD-PARKING-2026",
  policyName: "Quezon City PWD Parking Benefit",
  legalBasisReference: "RA 10754 and verified local ordinance",
  ordinanceReference: "QC Ordinance 2026-04",
  nationalLawReference: "RA 10754",
  verificationStatus: "VERIFIED_OFFICIAL",
  benefitType: "FREE_DURATION",
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
  verificationStatus: "LEAD_UNVERIFIED",
  benefitType: "FREE_DURATION",
  evidenceRequired: true,
  ineligibilityReason: "Local policy is not verified for operator use."
};

const mockDrafts: StatutoryDiscountDraftDetail[] = [
  {
    draftId: "47000000-0000-0000-0000-000000000008",
    parkingSessionId: "25000000-0000-0000-0000-000000000001",
    ticketReference: "STAT-OP-SESSION-0001",
    plateNumber: "ABC 1234",
    siteId: "77000000-0000-0000-0000-000000000002",
    siteGroupId: "77000000-0000-0000-0000-000000000001",
    siteName: "Terminal Parking / North Exit",
    laneName: "North Exit Lane 2",
    entitlementType: "Senior Citizen",
    status: "Requested",
    requestedAt: "2026-06-01T08:15:00+08:00",
    requestedBy: "operator.shift-a",
    parkingStartedAt: "2026-06-01T06:55:00+08:00",
    originalTariffAmount: "PHP 180.00",
    payableBasisPreview: "Preview PHP 144.00",
    currentPaymentStatus: "Read-only in this module",
    maskedIdReference: "Evidence metadata only",
    issuingAuthority: "OSCA",
    evidenceCaptured: false,
    originalAmountMinorUnits: 18000,
    payableAmountMinorUnits: 14400,
    currencyCode: "PHP",
    policyContext: seniorNationalPolicy,
    auditActivity: ["Draft created after access evaluation.", "Policy resolved through national fallback."]
  },
  {
    draftId: "47000000-0000-0000-0000-000000000009",
    parkingSessionId: "25000000-0000-0000-0000-000000000002",
    ticketReference: "STAT-OP-SESSION-0002",
    plateNumber: "PWD 2048",
    siteId: "77000000-0000-0000-0000-000000000002",
    siteGroupId: "77000000-0000-0000-0000-000000000001",
    siteName: "City Center Parking / Basement",
    laneName: "Basement Exit Lane 1",
    entitlementType: "PWD",
    status: "Requested",
    requestedAt: "2026-06-01T08:42:00+08:00",
    requestedBy: "operator.shift-a",
    parkingStartedAt: "2026-06-01T07:10:00+08:00",
    originalTariffAmount: "PHP 220.00",
    payableBasisPreview: "Evidence upload pending",
    currentPaymentStatus: "Read-only in this module",
    maskedIdReference: "Evidence metadata only",
    issuingAuthority: "PDAO",
    evidenceCaptured: false,
    originalAmountMinorUnits: 22000,
    currencyCode: "PHP",
    policyContext: pwdLocalPolicy,
    auditActivity: ["Draft created after access evaluation.", "Evidence required flag is active."]
  },
  {
    draftId: "47000000-0000-0000-0000-000000000010",
    parkingSessionId: "25000000-0000-0000-0000-000000000003",
    ticketReference: "STAT-OP-SESSION-0003",
    plateNumber: "LOC 8841",
    siteId: "77000000-0000-0000-0000-000000000002",
    siteGroupId: "77000000-0000-0000-0000-000000000001",
    siteName: "Riverside Parking / Exit",
    laneName: "Riverside Exit Lane",
    entitlementType: "Senior Citizen",
    status: "Blocked",
    requestedAt: "2026-06-01T09:05:00+08:00",
    requestedBy: "operator.shift-b",
    parkingStartedAt: "2026-06-01T08:01:00+08:00",
    originalTariffAmount: "PHP 90.00",
    payableBasisPreview: "Blocked",
    currentPaymentStatus: "Read-only in this module",
    maskedIdReference: "Evidence metadata only",
    issuingAuthority: "OSCA",
    evidenceCaptured: false,
    originalAmountMinorUnits: 9000,
    currencyCode: "PHP",
    policyContext: blockedLocalPolicy,
    auditActivity: ["Unverified local policy detected.", "Decision blocked pending policy verification."]
  }
];
