export type EntitlementType = "Senior Citizen" | "PWD" | "Unsupported";

export type DraftStatus =
  | "Pending Review"
  | "Requested"
  | "Approved"
  | "Rejected"
  | "Blocked"
  | "Expired";

export type PolicyContextKind =
  | "national-fallback"
  | "verified-local"
  | "blocked-unverified-local"
  | "unsupported-entitlement"
  | "missing-site-jurisdiction";

export interface StatutoryDiscountPolicyContext {
  kind: PolicyContextKind;
  title: string;
  operatorSummary: string;
  policyResolutionBasis: string;
  policyCode?: string;
  policyName?: string;
  legalBasisReference?: string;
  ordinanceReference?: string;
  nationalLawReference?: string;
  verificationStatus?: string;
  benefitType?: string;
  evidenceRequired: boolean;
  ineligibilityReason?: string;
}

export interface StatutoryDiscountQueueItem {
  draftId: string;
  parkingSessionId: string;
  ticketReference: string;
  plateNumber: string;
  siteId: string;
  siteGroupId?: string;
  siteName: string;
  entitlementType: EntitlementType;
  status: DraftStatus;
  requestedAt: string;
  requestedBy: string;
  policyContext: StatutoryDiscountPolicyContext;
  originalAmountMinorUnits?: number;
  payableAmountMinorUnits?: number;
  currencyCode?: string;
}

export interface StatutoryDiscountDraftDetail extends StatutoryDiscountQueueItem {
  laneName: string;
  parkingStartedAt: string;
  originalTariffAmount: string;
  payableBasisPreview: string;
  currentPaymentStatus: string;
  maskedIdReference: string;
  issuingAuthority: string;
  evidenceCaptured: boolean;
  statutoryDiscountAmountMinorUnits?: number;
  payableBasisApplicationStatus?: string;
  auditActivity: string[];
}

export type LoadState<T> =
  | { status: "idle" | "loading" }
  | { status: "loaded"; data: T }
  | { status: "empty" }
  | { status: "not-found" }
  | { status: "access-denied"; message: string }
  | { status: "error"; message: string };

export interface OperatorConsoleApiError {
  status: "access-denied" | "not-found" | "error";
  message: string;
  errorCode?: string;
}

export interface StatutoryDiscountDecisionInput {
  draftId: string;
  siteId?: string;
  siteGroupId?: string;
  decision: "APPROVE" | "REJECT";
  reasonCode?: string;
  notes?: string;
}

export interface StatutoryDiscountDecisionResult {
  accepted: boolean;
  persisted: boolean;
  currentStatus?: DraftStatus;
  errorCode?: string;
  message: string;
}
