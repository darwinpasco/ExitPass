export type EntitlementType = "Senior Citizen" | "PWD" | "Unsupported";

export type DraftStatus =
  | "Pending Review"
  | "Requested"
  | "Approved"
  | "Rejected"
  | "Blocked"
  | "Cancelled"
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
  evidenceRequiredSatisfied: boolean;
  evidenceCount: number;
  latestEvidenceStatus?: string;
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
  evidenceRequiredSatisfied: boolean;
  evidenceCount: number;
  latestEvidenceStatus?: string;
  requiredEvidenceTypes: string[];
  originalTariffSnapshotId?: string;
  payableBasisApplicationId?: string;
  appliedTariffSnapshotId?: string;
  vatAmountMinorUnits?: number;
  vatExclusiveAmountMinorUnits?: number;
  statutoryDiscountAmountMinorUnits?: number;
  finalPayableAmountMinorUnits?: number;
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

export interface AccessReadinessClientContext {
  uiModule?: string;
  screenState?: string;
}

export interface AccessReadinessDevModeContext {
  usesLocalDevFallbackContext: boolean;
  environmentName?: string;
}

export interface AccessReadinessRequest {
  operatorUserId?: string;
  operatorDeviceBindingId?: string;
  operatorShiftId?: string;
  siteId?: string;
  siteGroupId?: string;
  requestedAction: string;
  targetEntityType?: string;
  targetEntityId?: string;
  workflowState?: string;
  correlationId?: string;
  idempotencyKey?: string;
  clientContext?: AccessReadinessClientContext;
  devModeContext?: AccessReadinessDevModeContext;
}

export interface AccessReadinessDimension {
  dimension: string;
  status: string;
  required: boolean;
  denialReasonCodes: string[];
}

export interface AccessReadinessDenialReason {
  code: string;
  severity: string;
  retryable: boolean;
  uxMessageCategory: string;
}

export interface OperatorReadiness {
  operatorUserId?: string;
  status: string;
  ready: boolean;
}

export interface DeviceReadiness {
  operatorDeviceBindingId?: string;
  status: string;
  ready: boolean;
}

export interface ShiftReadiness {
  operatorShiftId?: string;
  status: string;
  ready: boolean;
}

export interface SiteReadiness {
  siteId?: string;
  siteGroupId?: string;
  status: string;
  ready: boolean;
}

export interface WorkflowReadiness {
  requestedAction: string;
  workflowState?: string;
  status: string;
  ready: boolean;
}

export interface AccessReadinessResponse {
  accessEvaluationId?: string;
  accessAllowed: boolean;
  accessDecision: string;
  requestedAction: string;
  readinessStatus: string;
  readinessDimensions: AccessReadinessDimension[];
  denialReasons: AccessReadinessDenialReason[];
  operatorReadiness: OperatorReadiness;
  deviceReadiness: DeviceReadiness;
  shiftReadiness: ShiftReadiness;
  siteReadiness: SiteReadiness;
  workflowReadiness: WorkflowReadiness;
  auditPersisted: boolean;
  evaluatedAt: string;
  correlationId: string;
  retryable: boolean;
  nextOperatorAction?: string;
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

export interface StatutoryDiscountPayableBasisApplicationInput {
  draftId: string;
  siteId?: string;
  siteGroupId?: string;
  originalTariffSnapshotId?: string;
}

export interface StatutoryDiscountPayableBasisApplicationResult {
  accepted: boolean;
  persisted: boolean;
  alreadyApplied: boolean;
  applicationStatus?: string;
  payableBasisApplicationId?: string;
  statutoryDiscountValidationId?: string;
  parkingSessionId?: string;
  originalTariffSnapshotId?: string;
  appliedTariffSnapshotId?: string;
  grossAmountMinorUnits?: number;
  vatAmountMinorUnits?: number;
  vatExclusiveAmountMinorUnits?: number;
  statutoryDiscountAmountMinorUnits?: number;
  finalPayableAmountMinorUnits?: number;
  currencyCode?: string;
  errorCode?: string;
  message: string;
}

export type EvidenceType = "SENIOR_CITIZEN_ID" | "PWD_ID" | "OTHER_SUPPORTING_DOCUMENT";

export type EvidenceCaptureMethod = "UPLOAD" | "MANUAL_REFERENCE" | "OPERATOR_CONFIRMED";

export interface StatutoryDiscountEvidenceItem {
  evidenceId: string;
  draftId: string;
  evidenceType: EvidenceType | string;
  captureMethod: EvidenceCaptureMethod | string;
  storageReference?: string;
  capturedByUserId?: string;
  capturedAt: string;
  redactionStatus: string;
  verificationStatus: string;
  correlationId?: string;
}

export interface StatutoryDiscountEvidenceList {
  draftId: string;
  evidenceRequired: boolean;
  evidenceRequiredSatisfied: boolean;
  requiredEvidenceTypes: string[];
  evidenceCount: number;
  latestEvidenceStatus?: string;
  items: StatutoryDiscountEvidenceItem[];
}

export interface StatutoryDiscountEvidenceCaptureInput {
  draftId: string;
  siteId?: string;
  siteGroupId?: string;
  evidenceType: EvidenceType;
  captureMethod: EvidenceCaptureMethod;
  fileName?: string;
  contentType?: string;
  sizeBytes?: number;
  referenceNumber?: string;
  notes?: string;
  operatorConfirmation: boolean;
}

export interface StatutoryDiscountEvidenceCaptureResult {
  evidenceId: string;
  draftId: string;
  evidenceType: string;
  captureMethod: string;
  verificationStatus: string;
  evidenceRequiredSatisfied: boolean;
  currentDraftStatus: DraftStatus;
  message: string;
}
