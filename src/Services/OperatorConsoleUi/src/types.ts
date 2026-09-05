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
  registrySource?: string;
  policyResolutionBasis: string;
  policyCode?: string;
  policyName?: string;
  legalBasisReference?: string;
  ordinanceReference?: string;
  nationalLawReference?: string;
  verificationStatus?: string;
  policyReadinessClassification?: string;
  requiresManualReview: boolean;
  policyReadinessReason?: string;
  operatorMessage?: string;
  productionAutoApplicationEligible: boolean;
  benefitType?: string;
  discountBaseScope?: string;
  evidenceRequired: boolean;
  requiredEvidenceType?: string;
  effectiveFrom?: string;
  effectiveTo?: string;
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
  statutoryDiscountDecisionCommandId?: string;
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
  decisionReasonCode?: string | null;
  governingPolicy?: StatutoryDiscountGoverningPolicy;
  auditActivity: string[];
}

export interface StatutoryDiscountGoverningPolicyEvidenceRequirement {
  evidenceType: string;
  requirementStatus: string;
  safeRequirementLabel?: string;
  safeRequirementNotes?: string;
}

export interface StatutoryDiscountGoverningPolicy {
  statutoryDiscountPolicyVersionId: string;
  jurisdictionId: string;
  jurisdictionCode: string;
  jurisdictionDisplayName: string;
  policyCode: string;
  policyVersion: string;
  ordinanceNumber?: string;
  ordinanceTitle?: string;
  sourceVerificationStatus: string;
  transactionPublicationStatus: string;
  detailedRuleVerificationStatus: string;
  parkingServiceApplicability: string;
  benefitType: string;
  beneficiaryResidencyScope: string;
  officialSourceAvailable: boolean;
  ordinanceTextAvailable: boolean;
  ordinanceNumberAvailable: boolean;
  effectiveFrom?: string;
  effectiveTo?: string;
  requiredEvidenceTypes: StatutoryDiscountGoverningPolicyEvidenceRequirement[];
  legalApprovabilityReason?: string;
}

export type CanonicalStatutoryReviewStatus =
  | "PENDING_REVIEW"
  | "APPROVED"
  | "REJECTED"
  | "REVIEW_FACTS_UNAVAILABLE";

export interface CanonicalStatutoryReviewFilters {
  status: CanonicalStatutoryReviewStatus | "ALL";
  siteId?: string;
  sourceChannel?: "WEBPAY" | "ASSISTED_PAYMENT_TERMINAL";
  entitlementType?: string;
  submittedFrom?: string;
  submittedTo?: string;
  search?: string;
  page: number;
  pageSize: number;
}

export interface CanonicalStatutoryReviewQueueItem {
  statutoryDiscountDecisionCommandId: string;
  requestReference: string;
  parkingSessionId: string;
  sourceChannel: string;
  siteId?: string;
  siteGroupId?: string;
  ticketReference?: string;
  entitlementType: string;
  commandStatus: string;
  decisionResultStatus: string;
  reviewStatus: CanonicalStatutoryReviewStatus;
  evidenceRequired: boolean;
  evidenceRecorded: boolean;
  submittedAt: string;
}

export interface CanonicalStatutoryReviewQueueResult {
  items: CanonicalStatutoryReviewQueueItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  hasMore: boolean;
  correlationId: string;
}

export interface CanonicalStatutoryReviewEvidenceReference {
  evidenceType: string;
  captureMethod: string;
  referenceNumberMasked?: string;
  verificationStatus?: string;
}

export interface CanonicalStatutoryReviewDetail extends CanonicalStatutoryReviewQueueItem {
  sessionEligibilityStatus: "ELIGIBLE" | "NOT_ELIGIBLE" | "PENDING_REVIEW";
  payableBasisStatus: "NOT_YET_CREATED" | "CREATED";
  statutoryDiscountValidationId?: string;
  plateNumber?: string;
  idDocumentType?: string;
  issuingAuthority?: string;
  expiryDate?: string;
  maskedIdReference?: string;
  evidenceReferences: CanonicalStatutoryReviewEvidenceReference[];
  requesterAttestation: boolean;
  attestationNotes?: string;
  reasonCode?: string;
  originalAmountMinorUnits?: number;
  vatExclusiveAmountMinorUnits?: number;
  vatAmountMinorUnits?: number;
  statutoryDiscountAmountMinorUnits?: number;
  finalPayableAmountMinorUnits?: number;
  currency?: string;
  governingPolicy?: StatutoryDiscountGoverningPolicy;
  reviewerUserId?: string;
  reviewerDecision?: string;
  reviewerReasonCode?: string;
  reviewedAt?: string;
  payableBasisApplicationStatus?: string;
  correlationId: string;
}

export interface CanonicalStatutoryReviewDecisionInput {
  statutoryDiscountDecisionCommandId: string;
  decision: "APPROVE" | "REJECT";
  reasonCode?: string;
  reviewerAttestation: boolean;
  idempotencyKey: string;
}

interface CanonicalStatutoryReviewDecisionResultBase {
  decisionPersisted: boolean;
  decision: string;
  alreadyDecided: boolean;
  decisionChanged: boolean;
  errorCode?: string;
  correlationId: string;
}

export type CanonicalStatutoryReviewDecisionResult = CanonicalStatutoryReviewDecisionResultBase & (
  | { decisionAccepted: true; currentValidationStatus: "APPROVED" | "REJECTED" }
  | { decisionAccepted: false; currentValidationStatus?: "APPROVED" | "REJECTED" }
);

export type LoadState<T> =
  | { status: "idle" | "loading" }
  | { status: "loaded"; data: T }
  | { status: "empty" }
  | { status: "not-found" }
  | { status: "access-denied"; message: string }
  | { status: "error"; message: string };

export interface ShiftAuthorizedSite {
  siteId: string;
  siteGroupId: string;
  siteCode: string;
  siteName: string;
  siteGroupCode: string;
  siteGroupName: string;
}

export interface OperationalShift {
  shiftId: string;
  shiftReference: string;
  operatorUserId: string;
  username: string;
  displayName: string;
  userType: string;
  roles: string[];
  siteId: string;
  siteGroupId: string;
  siteCode: string;
  siteName: string;
  siteGroupCode: string;
  siteGroupName: string;
  deviceId?: string;
  deviceName?: string;
  terminalReference?: string;
  openedAt: string;
  closedAt?: string;
  elapsedSeconds: number;
  status: string;
  cashCustodyStatus: string;
  openingCashMinorUnits?: number;
  cashTransactionCount: number;
  cashCollectedMinorUnits?: number;
  closeType?: string;
  closingActorName?: string;
  closeReason?: string;
  createdAt: string;
  updatedAt: string;
}

export interface ShiftListResult {
  items: OperationalShift[];
  correlationId: string;
}

export interface OperatorConsoleApiError {
  status: "access-denied" | "not-found" | "error";
  message: string;
  errorCode?: string;
}

export interface OperatorTicketLookupInput {
  ticketNumber: string;
  cardNum?: string;
}

export interface OperatorTicketLookupResult {
  sessionFound: boolean;
  accessAllowed?: boolean;
  sessionEligible?: boolean;
  parkingSessionId?: string;
  siteId?: string;
  siteGroupId?: string;
  ticketNumber?: string;
  cardNum?: string;
  plateLicense?: string;
  parkingInTime?: string;
  parkingDurationSeconds?: number;
  feeMinorUnits?: number;
  currencyCode?: string;
  feeRuleType?: string;
  feeRuleIndexCode?: string;
  feeRuleName?: string;
  paymentAttemptStatus?: string;
  paymentStatus?: string;
  paymentConfirmationStatus?: string;
  vendorSystemCode?: string;
  vendorConfirmationCode?: string;
  vendorConfirmationStatus?: string | null;
  vendorConfirmationTimestamp?: string;
  vendorMessage?: string;
  diagnostics?: string[];
  correlationId?: string;
  message?: string;
}

export interface StatutoryDiscountDraftCreateInput {
  parkingSessionId: string;
  ticketReference?: string;
  plateNumber?: string;
  siteId?: string;
  siteGroupId?: string;
  entitlementType: "SENIOR_CITIZEN" | "PWD";
  idDocumentType: string;
  issuingAuthority: string;
  maskedIdReference: string;
  evidenceCaptureRequested: boolean;
  operatorAttestation: boolean;
  attestationNotes?: string;
  reasonCode?: string;
}

export interface StatutoryDiscountDraftCreateResult {
  accepted: boolean;
  persisted: boolean;
  draftId?: string;
  parkingSessionId?: string;
  entitlementType?: string;
  validationStatus?: DraftStatus;
  evidenceCaptureRequired: boolean;
  evidenceRequired: boolean;
  evidenceReferenceCreated: boolean;
  reusedExistingDraft: boolean;
  errorCode?: string;
  message: string;
}

export interface FiscalIssuanceStatus {
  fiscalIssuanceReferenceId: string;
  fiscalIssuanceState: string;
  resultClassification?: string;
  fiscalIssuanceEvidenceStatus?: string;
  fiscalNumberAssignmentState: string;
  upstreamFinalityReference: string;
  paymentConfirmationId: string;
  paymentAttemptId: string;
  parkingSessionId: string;
  siteId?: string;
  sitePosServerId?: string;
  sitePosServerRef?: string;
  fiscalDocumentTypeCodeId?: string;
  fiscalDocumentTypeCodeKey?: string;
  posServerFiscalDocumentId?: string;
  fiscalDocumentNumber?: string;
  fiscalIdentityId?: string;
  fiscalSequencePolicyId?: string;
  fiscalSequenceValue?: number;
  fiscalSeries?: string;
  fiscalNumberPrefixText?: string;
  fiscalNumberSuffixText?: string;
  fiscalNumberAssignedAt?: string;
  fiscalNumberAssignedByRef?: string;
  semanticRequestHashValue?: string;
  semanticRequestHashVersion?: string;
  semanticRequestHashStatus?: string;
  semanticRequestHashAlgorithm?: string;
  semanticRequestHashSourceFactCount?: number;
  latestErrorCode?: string;
  latestErrorPosture?: string;
  latestExceptionReason?: string;
  firstRecordedAt: string;
  lastUpdatedAt: string;
  correlationId?: string;
  posServerFiscalDocumentReadStatus?: string;
  posServerFiscalDocumentStatusCodeKey?: string;
  posServerVoidStatus?: string;
  posServerVoidReasonCode?: string;
  posServerVoidedAt?: string;
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

export interface AuditReportQuery {
  siteId?: string;
  parkingSessionId?: string;
  validationStatus?: string;
  from?: string;
  to?: string;
  limit?: number;
  offset?: number;
}

export interface AuditReportResponse {
  items: AuditReportItem[];
  totalCount: number;
  limit: number;
  offset: number;
  correlationId: string;
}

export interface AuditReportItem {
  statutoryDiscountValidationId: string;
  draftId: string;
  parkingSessionId: string;
  ticketReference?: string;
  plateNumber?: string;
  siteId: string;
  siteGroupId: string;
  entitlementType: string;
  validationStatus: string;
  evidenceRequired: boolean;
  evidenceCaptured: boolean;
  evidenceRequiredSatisfied: boolean;
  evidenceCount: number;
  latestEvidenceStatus?: string;
  payableBasisApplicationStatus?: string;
  originalAmountMinorUnits?: number;
  statutoryDiscountAmountMinorUnits?: number;
  finalPayableAmountMinorUnits?: number;
  currencyCode?: string;
  requestedByUserId?: string;
  validatedByUserId?: string;
  requestedAt: string;
  validatedAt?: string;
  correlationId?: string;
  registrySource?: string;
  policyCode?: string;
  verificationStatus?: string;
  policyReadinessClassification?: string;
  requiresManualReview?: boolean;
  policyReadinessReason?: string;
  operatorMessage?: string;
  ordinanceReference?: string;
  legalBasisReference?: string;
  appliedTariffSnapshotId?: string;
  accessEvaluationSummary?: string;
}

export type FiscalStatusViewAuditResultClass = "SUCCEEDED" | "DENIED" | "NOT_FOUND" | "FAILED_SAFELY";
export type FiscalVoidActionAuditResultClass =
  | "SUCCEEDED"
  | "DENIED"
  | "NOT_FOUND"
  | "CONFLICT"
  | "REJECTED"
  | "ALREADY_VOIDED"
  | "FAILED_SAFELY";

export interface FiscalStatusViewAuditReportQuery {
  from?: string;
  to?: string;
  siteId?: string;
  siteGroupId?: string;
  operatorUserId?: string;
  fiscalIssuanceReferenceId?: string;
  resultClass?: FiscalStatusViewAuditResultClass | string;
  correlationId?: string;
  limit?: number;
  offset?: number;
}

export interface FiscalStatusViewAuditReportResponse {
  items: FiscalStatusViewAuditReportItem[];
  totalCount: number;
  limit: number;
  offset: number;
  correlationId: string;
}

export interface FiscalStatusViewAuditReportItem {
  actionLogEntryId: string;
  actionTimestamp: string;
  actionCode: string;
  resultClass: FiscalStatusViewAuditResultClass | string;
  operatorUserId: string;
  operatorDisplayName?: string;
  operatorUsername?: string;
  siteId?: string;
  siteName?: string;
  siteGroupId?: string;
  siteGroupName?: string;
  fiscalIssuanceReferenceId: string;
  fiscalDocumentNumber?: string;
  ticketNumber?: string;
  correlationId: string;
  safeDenialOrErrorPosture?: string;
  sourceModule?: string;
}

export interface FiscalVoidActionAuditReportQuery {
  from?: string;
  to?: string;
  siteId?: string;
  siteGroupId?: string;
  operatorUserId?: string;
  fiscalIssuanceReferenceId?: string;
  fiscalDocumentNumber?: string;
  resultClass?: FiscalVoidActionAuditResultClass | string;
  correlationId?: string;
  limit?: number;
  offset?: number;
}

export interface FiscalVoidActionAuditReportResponse {
  items: FiscalVoidActionAuditReportItem[];
  totalCount: number;
  limit: number;
  offset: number;
  correlationId: string;
}

export interface FiscalVoidActionAuditReportItem {
  actionLogEntryId: string;
  actionTimestamp: string;
  actionCode: string;
  resultClass: FiscalVoidActionAuditResultClass | string;
  operatorUserId: string;
  operatorDisplayName?: string;
  operatorUsername?: string;
  siteId?: string;
  siteName?: string;
  siteGroupId?: string;
  siteGroupName?: string;
  fiscalIssuanceReferenceId: string;
  fiscalDocumentNumber?: string;
  ticketNumber?: string;
  posServerFiscalDocumentId?: string;
  reasonCode?: string;
  reasonText?: string;
  correlationId: string;
  operatorActionRequestId?: string;
  posServerResultClassification?: string;
  safeDenialOrErrorPosture?: string;
  sourceModule?: string;
  paymentFinalityChanged?: boolean;
  exitAuthorizationIssued?: boolean;
  gateBehaviorTriggered?: boolean;
  refundOrReversalCreated?: boolean;
  hikCentralCalled?: boolean;
  paymentProviderCalled?: boolean;
  renderingGenerated?: boolean;
  replacementFiscalDocumentCreated?: boolean;
  newFiscalNumberAllocated?: boolean;
  fiscalSequenceChangedByCentralPms?: boolean;
}

export type VendorPaymentAcknowledgmentStatus =
  | "PENDING"
  | "RETRY_PENDING"
  | "FAILED"
  | "CONFIRMED"
  | "SKIPPED_DISABLED"
  | "CANCELLED";

export interface VendorPaymentAcknowledgmentSearchInput {
  acknowledgmentStatus?: VendorPaymentAcknowledgmentStatus | string;
  vendorSystemCode?: string;
  paymentAttemptId?: string;
  paymentConfirmationId?: string;
  parkingSessionId?: string;
  ticketNumber?: string;
  cardNum?: string;
  correlationId?: string;
  createdFrom?: string;
  createdTo?: string;
  lastAttemptedFrom?: string;
  lastAttemptedTo?: string;
  nextRetryDueOnly?: boolean;
  pageIndex?: number;
  pageSize?: number;
}

export interface VendorPaymentAcknowledgmentStatusBuckets {
  pending: number;
  retryPending: number;
  failed: number;
  confirmed: number;
  skippedDisabled: number;
  cancelled: number;
}

export interface VendorPaymentAcknowledgmentSummary {
  vendorPaymentAcknowledgmentId: string;
  paymentAttemptId: string;
  paymentConfirmationId: string;
  parkingSessionId?: string;
  vendorSystemCode: string;
  vendorSessionRef?: string;
  ticketNumber?: string;
  cardNum?: string;
  acknowledgmentStatus: VendorPaymentAcknowledgmentStatus | string;
  statusBucket?: string;
  vendorCode?: string;
  vendorMessage?: string;
  requestFeeMinorUnits?: number;
  requestCurrencyCode?: string;
  confirmedFeeMinorUnits?: number;
  vendorConfirmedAt?: string;
  attemptCount: number;
  lastAttemptedAt?: string;
  nextRetryAt?: string;
  correlationId?: string;
  createdAt: string;
  updatedAt: string;
}

export interface VendorPaymentAcknowledgmentDiagnostic {
  code: string;
  message: string;
  source: string;
  retryable: boolean;
  correlationId?: string;
}

export interface VendorPaymentAcknowledgmentDetail extends VendorPaymentAcknowledgmentSummary {
  diagnostics: VendorPaymentAcknowledgmentDiagnostic[];
}

export interface VendorPaymentAcknowledgmentSearchResult {
  items: VendorPaymentAcknowledgmentSummary[];
  statusBuckets: VendorPaymentAcknowledgmentStatusBuckets;
  pageIndex: number;
  pageSize: number;
  hasMore: boolean;
}

export interface VendorSessionProjectionHealthConfig {
  schedulerEnabled: boolean;
  degradedResolveFallbackEnabled: boolean;
  maxProjectionAgeMinutes: number;
  maxParallelSiteJobs: number;
  schedulerScanIntervalSeconds: number;
}

export interface VendorSessionProjectionHealthTarget {
  projectionSyncTargetId: string;
  siteId: string;
  siteGroupId: string;
  vendorSystemId: string;
  parkingLotIndexCode: string;
  parkingLotName?: string | null;
  enabledFlag: boolean;
  healthStatus: string;
  lastAttemptAt?: string | null;
  lastSuccessAt?: string | null;
  lastFailureAt?: string | null;
  failureCount: number;
  lastErrorCode?: string | null;
  lastErrorMessage?: string | null;
  pollIntervalSeconds: number;
  lookbackWindowMinutes: number;
  pageSize: number;
  latestProjectionLastRefreshedAt?: string | null;
  freshnessAgeSeconds?: number | null;
  isStale: boolean;
  totalProjectionCount: number;
  activeProjectionCount: number;
  exitedProjectionCount: number;
  cardNumProjectionCount: number;
  plateLicenseProjectionCount: number;
}

export interface VendorSessionProjectionHealthTargetsResponse {
  targets: VendorSessionProjectionHealthTarget[];
  config: VendorSessionProjectionHealthConfig;
}

export interface VendorSessionProjectionHealthLatestRecord {
  vendorSessionProjectionId: string;
  vendorRecordGuid?: string | null;
  cardNum?: string | null;
  plateLicense?: string | null;
  enterTime?: string | null;
  exitTime?: string | null;
  projectionStatus: string;
  lastRefreshedAt: string;
  sourceEventAt?: string | null;
  correlationId?: string | null;
}

export interface VendorSessionProjectionHealthTargetDetail {
  target: VendorSessionProjectionHealthTarget;
  latestProjectedRecords: VendorSessionProjectionHealthLatestRecord[];
  config: VendorSessionProjectionHealthConfig;
}

export interface VendorSessionProjectionHealthSummary {
  totalTargets: number;
  enabledTargets: number;
  disabledTargets: number;
  healthyTargets: number;
  degradedTargets: number;
  failingTargets: number;
  unknownTargets: number;
  staleTargets: number;
  targetsWithLastFailure: number;
  latestSuccessfulProjectionSyncAt?: string | null;
  totalActiveProjections: number;
  totalExitedProjections: number;
  config: VendorSessionProjectionHealthConfig;
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

export interface StatutoryEvidenceReviewItem {
  evidenceItemReference: string;
  documentType: string;
  itemRole: string;
  declaredContentType?: string;
  authoritativeContentType?: string;
  contentLength?: number;
  uploadStatus: string;
  validationStatus: string;
  scanStatus: string;
  reviewabilityStatus: string;
  bindingStatus: string;
  retentionStatus: string;
  deletionStatus: string;
  holdActive: boolean;
  uploadedAt?: string;
  finalizedAt?: string;
  validatedAt?: string;
  scannedAt?: string;
  reviewableAt?: string;
  previewPermitted: boolean;
  previewDenialReason?: string;
}

export interface StatutoryEvidenceReview {
  statutoryDiscountDecisionCommandId: string;
  evidenceSetReference?: string;
  sourceChannel: string;
  decisionResultStatus: string;
  reviewStatus: string;
  evidenceRequired: boolean;
  evidenceRecorded: boolean;
  setStatus?: string;
  retentionStatus?: string;
  deletionStatus?: string;
  holdActive: boolean;
  replacementPosture: string;
  items: StatutoryEvidenceReviewItem[];
}

export interface StatutoryEvidencePreview {
  blob: Blob;
  contentType: "image/jpeg" | "image/png";
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

export interface ProductionPolicyImportDryRunInput {
  csvContent: string;
  fileName?: string;
}

export interface ProductionPolicyImportDryRunSummary {
  totalRows: number;
  passCount: number;
  warnCount: number;
  failCount: number;
  importableCount: number;
  manualReviewCount: number;
  notImportableCount: number;
  dryRunOnlyCount: number;
  duplicateCount: number;
}

export interface ProductionPolicyImportDryRunFinding {
  severity: string;
  code: string;
  message: string;
  fieldName?: string;
}

export interface ProductionPolicyImportDryRunRow {
  rowNumber: number;
  policyCode?: string;
  entitlementType?: string;
  decision: string;
  findings: ProductionPolicyImportDryRunFinding[];
}

export interface ProductionPolicyImportDryRunResult {
  imported: false;
  importedRowCount: 0;
  dryRunOnly: true;
  message: string;
  summary: ProductionPolicyImportDryRunSummary;
  rows: ProductionPolicyImportDryRunRow[];
  correlationId: string;
}

export interface ProductionPolicyImportReviewSubmitInput {
  dryRunResult: ProductionPolicyImportDryRunResult;
  fileName?: string;
}

export type ProductionPolicyImportReviewDecisionAction =
  | "APPROVE_LEGAL"
  | "APPROVE_OPS"
  | "APPROVE_QA"
  | "APPROVE_DB"
  | "REJECT"
  | "REQUEST_CHANGES"
  | "ESCALATE";

export interface ProductionPolicyImportReviewDecisionInput {
  reviewId: string;
  action: ProductionPolicyImportReviewDecisionAction;
  reason?: string;
}

export interface ProductionPolicyImportReviewQuery {
  status?: string;
  makerOperatorId?: string;
  reviewerOperatorId?: string;
  reviewerRole?: string;
  createdFrom?: string;
  createdTo?: string;
  limit?: number;
  offset?: number;
}

export interface ProductionPolicyImportReviewDecision {
  reviewerRole: string;
  action: string;
  reviewerOperatorId: string;
  reason?: string;
  decidedAt: string;
  correlationId: string;
}

export interface ProductionPolicyImportReviewHistoryEntry {
  action: string;
  status: string;
  actorOperatorId: string;
  reviewerRole?: string;
  reason?: string;
  occurredAt: string;
  correlationId: string;
}

export interface ProductionPolicyImportReviewFinding {
  severity: string;
  message: string;
  fieldName?: string;
}

export interface ProductionPolicyImportReviewSubmission {
  reviewId: string;
  makerOperatorId: string;
  fileName?: string;
  status: string;
  dryRunSummary: ProductionPolicyImportDryRunSummary;
  reviewerDecisions: ProductionPolicyImportReviewDecision[];
  history: ProductionPolicyImportReviewHistoryEntry[];
  createdAt: string;
  updatedAt: string;
}

export interface ProductionPolicyImportReviewResult {
  imported: false;
  productionPolicyActivationBlocked: true;
  message: string;
  submission: ProductionPolicyImportReviewSubmission;
  findings: ProductionPolicyImportReviewFinding[];
  correlationId: string;
}

export interface ProductionPolicyImportReviewQueueItem {
  imported: false;
  productionPolicyActivationBlocked: true;
  submission: ProductionPolicyImportReviewSubmission;
  findings: ProductionPolicyImportReviewFinding[];
}

export interface ProductionPolicyImportReviewListResult {
  imported: false;
  productionPolicyActivationBlocked: true;
  items: ProductionPolicyImportReviewQueueItem[];
  totalCount: number;
  limit: number;
  offset: number;
  correlationId: string;
}
