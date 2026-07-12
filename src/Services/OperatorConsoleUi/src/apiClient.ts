import type {
  AccessReadinessRequest,
  AccessReadinessResponse,
  AuditReportQuery,
  AuditReportResponse,
  DraftStatus,
  EntitlementType,
  FiscalIssuanceVoidInput,
  FiscalIssuanceVoidResult,
  FiscalIssuanceStatus,
  FiscalVoidActionAuditReportQuery,
  FiscalVoidActionAuditReportResponse,
  FiscalStatusViewAuditReportQuery,
  FiscalStatusViewAuditReportResponse,
  OperatorConsoleApiError,
  OperatorTicketLookupInput,
  OperatorTicketLookupResult,
  ProductionPolicyImportDryRunInput,
  ProductionPolicyImportDryRunResult,
  ProductionPolicyImportReviewDecisionInput,
  ProductionPolicyImportReviewListResult,
  ProductionPolicyImportReviewQuery,
  ProductionPolicyImportReviewResult,
  ProductionPolicyImportReviewSubmitInput,
  StatutoryDiscountDecisionInput,
  StatutoryDiscountDecisionResult,
  StatutoryDiscountDraftCreateInput,
  StatutoryDiscountDraftCreateResult,
  StatutoryDiscountDraftDetail,
  StatutoryDiscountEvidenceCaptureInput,
  StatutoryDiscountEvidenceCaptureResult,
  StatutoryDiscountEvidenceItem,
  StatutoryDiscountEvidenceList,
  StatutoryDiscountPayableBasisApplicationInput,
  StatutoryDiscountPayableBasisApplicationResult,
  StatutoryDiscountPolicyContext,
  StatutoryDiscountQueueItem,
  VendorPaymentAcknowledgmentDetail,
  VendorPaymentAcknowledgmentDiagnostic,
  VendorPaymentAcknowledgmentSearchInput,
  VendorPaymentAcknowledgmentSearchResult,
  VendorPaymentAcknowledgmentStatusBuckets,
  VendorPaymentAcknowledgmentSummary,
  VendorSessionProjectionHealthConfig,
  VendorSessionProjectionHealthLatestRecord,
  VendorSessionProjectionHealthTarget,
  VendorSessionProjectionHealthTargetDetail,
  VendorSessionProjectionHealthTargetsResponse,
  VendorSessionProjectionHealthSummary
} from "./types";

export interface OperatorConsoleApiClient {
  evaluateAccessReadiness(input: AccessReadinessRequest): Promise<AccessReadinessResponse>;
  lookupSessionByTicket(input: OperatorTicketLookupInput): Promise<OperatorTicketLookupResult>;
  getFiscalIssuanceStatus(fiscalIssuanceReferenceId: string): Promise<FiscalIssuanceStatus>;
  lookupFiscalIssuanceStatus(query: string): Promise<FiscalIssuanceStatus>;
  voidFiscalIssuanceReference(input: FiscalIssuanceVoidInput): Promise<FiscalIssuanceVoidResult>;
  listFiscalVoidActionAuditReport(input?: FiscalVoidActionAuditReportQuery): Promise<FiscalVoidActionAuditReportResponse>;
  listFiscalStatusViewAuditReport(input?: FiscalStatusViewAuditReportQuery): Promise<FiscalStatusViewAuditReportResponse>;
  listAuditReport(input?: AuditReportQuery): Promise<AuditReportResponse>;
  createStatutoryDiscountDraft(input: StatutoryDiscountDraftCreateInput): Promise<StatutoryDiscountDraftCreateResult>;
  listStatutoryDiscountDrafts(): Promise<StatutoryDiscountQueueItem[]>;
  getStatutoryDiscountDraft(draftId: string): Promise<StatutoryDiscountDraftDetail>;
  listStatutoryDiscountEvidence(draftId: string): Promise<StatutoryDiscountEvidenceList>;
  captureStatutoryDiscountEvidence(input: StatutoryDiscountEvidenceCaptureInput): Promise<StatutoryDiscountEvidenceCaptureResult>;
  submitStatutoryDiscountDecision(input: StatutoryDiscountDecisionInput): Promise<StatutoryDiscountDecisionResult>;
  applyStatutoryDiscountPayableBasis(
    input: StatutoryDiscountPayableBasisApplicationInput
  ): Promise<StatutoryDiscountPayableBasisApplicationResult>;
  dryRunProductionPolicyImport(input: ProductionPolicyImportDryRunInput): Promise<ProductionPolicyImportDryRunResult>;
  submitProductionPolicyImportReview(input: ProductionPolicyImportReviewSubmitInput): Promise<ProductionPolicyImportReviewResult>;
  decideProductionPolicyImportReview(input: ProductionPolicyImportReviewDecisionInput): Promise<ProductionPolicyImportReviewResult>;
  listProductionPolicyImportReviews(input?: ProductionPolicyImportReviewQuery): Promise<ProductionPolicyImportReviewListResult>;
  getProductionPolicyImportReview(reviewId: string): Promise<ProductionPolicyImportReviewResult>;
  searchVendorPaymentAcknowledgments(input?: VendorPaymentAcknowledgmentSearchInput): Promise<VendorPaymentAcknowledgmentSearchResult>;
  getVendorPaymentAcknowledgment(vendorPaymentAcknowledgmentId: string): Promise<VendorPaymentAcknowledgmentDetail>;
  listVendorSessionProjectionHealthTargets(): Promise<VendorSessionProjectionHealthTargetsResponse>;
  getVendorSessionProjectionHealthTarget(projectionSyncTargetId: string): Promise<VendorSessionProjectionHealthTargetDetail>;
  getVendorSessionProjectionHealthSummary(): Promise<VendorSessionProjectionHealthSummary>;
  canDecideProductionPolicyImportReview?(): boolean;
  canVoidFiscalDocument?(): boolean;
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
  evidenceRequiredSatisfied?: boolean | null;
  evidenceCount?: number | null;
  latestEvidenceStatus?: string | null;
  registrySource?: string | null;
  policyResolutionBasis?: string | null;
  policyCode?: string | null;
  policyName?: string | null;
  verificationStatus?: string | null;
  policyReadinessClassification?: string | null;
  requiresManualReview?: boolean | null;
  policyReadinessReason?: string | null;
  operatorMessage?: string | null;
  requiredEvidenceType?: string | null;
  effectiveFrom?: string | null;
  effectiveTo?: string | null;
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
  requiredEvidenceTypes?: string[] | null;
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
  registrySource?: string | null;
  policyReadinessClassification?: string | null;
  requiresManualReview?: boolean | null;
  policyReadinessReason?: string | null;
  operatorMessage?: string | null;
  requiredEvidenceType?: string | null;
  effectiveFrom?: string | null;
  effectiveTo?: string | null;
  originalTariffSnapshotId?: string | null;
  payableBasisApplicationId?: string | null;
  payableBasisApplicationStatus?: string | null;
  appliedTariffSnapshotId?: string | null;
  vatAmountMinorUnits?: number | null;
  vatExclusiveAmountMinorUnits?: number | null;
  statutoryDiscountAmountMinorUnits?: number | null;
  finalPayableAmountMinorUnits?: number | null;
  activity: string[];
}

interface DraftCreateResponseDto {
  accessAllowed: boolean;
  accessDecision: string;
  accessDenialReasons: string[];
  draftAccepted: boolean;
  draftPersisted: boolean;
  draftId?: string | null;
  parkingSessionId?: string | null;
  entitlementType?: string | null;
  validationStatus?: string | null;
  evidenceCaptureRequired: boolean;
  evidenceRequired: boolean;
  evidenceReferenceCreated: boolean;
  reusedExistingDraft: boolean;
  ineligibilityReason?: string | null;
  errorCode?: string | null;
  policyReadinessClassification?: string | null;
  operatorMessage?: string | null;
  correlationId?: string | null;
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

interface EvidenceListDto {
  draftId: string;
  evidenceRequired: boolean;
  evidenceRequiredSatisfied: boolean;
  requiredEvidenceTypes: string[];
  evidenceCount: number;
  latestEvidenceStatus?: string | null;
  items: EvidenceItemDto[];
}

interface EvidenceItemDto {
  evidenceId: string;
  draftId: string;
  evidenceType: string;
  captureMethod: string;
  storageReference?: string | null;
  capturedByUserId?: string | null;
  capturedAt: string;
  redactionStatus: string;
  verificationStatus: string;
  correlationId?: string | null;
}

interface EvidenceCaptureResponseDto {
  evidenceId: string;
  draftId: string;
  evidenceType: string;
  captureMethod: string;
  verificationStatus: string;
  evidenceRequiredSatisfied: boolean;
  currentDraftStatus: string;
  accessAllowed: boolean;
  errorCode?: string | null;
}

interface PayableBasisApplicationResponseDto {
  accessAllowed: boolean;
  accessDecision: string;
  accessDenialReasons: string[];
  applicationAccepted: boolean;
  applicationPersisted: boolean;
  payableBasisApplicationId?: string | null;
  statutoryDiscountValidationId?: string | null;
  parkingSessionId?: string | null;
  originalTariffSnapshotId?: string | null;
  appliedTariffSnapshotId?: string | null;
  applicationStatus?: string | null;
  alreadyApplied: boolean;
  grossAmountMinorUnits?: number | null;
  vatAmountMinorUnits?: number | null;
  vatExclusiveAmountMinorUnits?: number | null;
  statutoryDiscountAmountMinorUnits?: number | null;
  finalPayableAmountMinorUnits?: number | null;
  currencyCode?: string | null;
  ineligibilityReason?: string | null;
  errorCode?: string | null;
}

interface AuditReportItemDto {
  statutoryDiscountValidationId: string;
  draftId: string;
  parkingSessionId: string;
  ticketReference?: string | null;
  plateNumber?: string | null;
  siteId: string;
  siteGroupId: string;
  entitlementType: string;
  validationStatus: string;
  evidenceRequired: boolean;
  evidenceCaptured: boolean;
  evidenceRequiredSatisfied: boolean;
  evidenceCount: number;
  latestEvidenceStatus?: string | null;
  payableBasisApplicationStatus?: string | null;
  originalAmountMinorUnits?: number | null;
  statutoryDiscountAmountMinorUnits?: number | null;
  finalPayableAmountMinorUnits?: number | null;
  currencyCode?: string | null;
  requestedByUserId?: string | null;
  validatedByUserId?: string | null;
  requestedAt: string;
  validatedAt?: string | null;
  correlationId?: string | null;
  registrySource?: string | null;
  policyCode?: string | null;
  verificationStatus?: string | null;
  policyReadinessClassification?: string | null;
  requiresManualReview?: boolean | null;
  policyReadinessReason?: string | null;
  operatorMessage?: string | null;
  ordinanceReference?: string | null;
  legalBasisReference?: string | null;
  appliedTariffSnapshotId?: string | null;
  accessEvaluationSummary?: string | null;
}

interface AuditReportResponseDto {
  items: AuditReportItemDto[];
  totalCount: number;
  limit: number;
  offset: number;
  correlationId: string;
}

interface FiscalStatusViewAuditReportItemDto {
  actionLogEntryId: string;
  actionTimestamp: string;
  actionCode: string;
  resultClass: string;
  operatorUserId: string;
  operatorDisplayName?: string | null;
  operatorUsername?: string | null;
  siteId?: string | null;
  siteName?: string | null;
  siteGroupId?: string | null;
  siteGroupName?: string | null;
  fiscalIssuanceReferenceId: string;
  fiscalDocumentNumber?: string | null;
  ticketNumber?: string | null;
  correlationId: string;
  safeDenialOrErrorPosture?: string | null;
  sourceModule?: string | null;
}

interface FiscalStatusViewAuditReportResponseDto {
  items: FiscalStatusViewAuditReportItemDto[];
  totalCount: number;
  limit: number;
  offset: number;
  correlationId: string;
}

interface FiscalVoidActionAuditReportItemDto {
  actionLogEntryId: string;
  actionTimestamp: string;
  actionCode: string;
  resultClass: string;
  operatorUserId: string;
  operatorDisplayName?: string | null;
  operatorUsername?: string | null;
  siteId?: string | null;
  siteName?: string | null;
  siteGroupId?: string | null;
  siteGroupName?: string | null;
  fiscalIssuanceReferenceId: string;
  fiscalDocumentNumber?: string | null;
  ticketNumber?: string | null;
  posServerFiscalDocumentId?: string | null;
  reasonCode?: string | null;
  reasonText?: string | null;
  correlationId: string;
  operatorActionRequestId?: string | null;
  posServerResultClassification?: string | null;
  safeDenialOrErrorPosture?: string | null;
  sourceModule?: string | null;
  paymentFinalityChanged?: boolean | null;
  exitAuthorizationIssued?: boolean | null;
  gateBehaviorTriggered?: boolean | null;
  refundOrReversalCreated?: boolean | null;
  hikCentralCalled?: boolean | null;
  paymentProviderCalled?: boolean | null;
  renderingGenerated?: boolean | null;
  replacementFiscalDocumentCreated?: boolean | null;
  newFiscalNumberAllocated?: boolean | null;
  fiscalSequenceChangedByCentralPms?: boolean | null;
}

interface FiscalVoidActionAuditReportResponseDto {
  items: FiscalVoidActionAuditReportItemDto[];
  totalCount: number;
  limit: number;
  offset: number;
  correlationId: string;
}

interface OperatorTicketLookupResponseDto {
  accessAllowed?: boolean | null;
  accessDecision?: string | null;
  accessDenialReasons?: string[] | null;
  accessPersisted?: boolean | null;
  sessionFound?: boolean | null;
  sessionEligible?: boolean | null;
  ineligibilityReason?: string | null;
  parkingSessionId?: string | null;
  ticketReference?: string | null;
  ticketNumber?: string | null;
  cardNum?: string | null;
  plateLicense?: string | null;
  plateNumber?: string | null;
  siteId?: string | null;
  siteGroupId?: string | null;
  sessionStatus?: string | null;
  entryTime?: string | null;
  currentPayableAmountMinorUnits?: number | null;
  parkingInTime?: string | null;
  parkingDurationSeconds?: number | null;
  feeMinorUnits?: number | null;
  currencyCode?: string | null;
  feeRuleType?: string | null;
  feeRuleIndexCode?: string | null;
  feeRuleName?: string | null;
  paymentAttemptStatus?: string | null;
  paymentStatus?: string | null;
  paymentConfirmationStatus?: string | null;
  discountStatus?: string | null;
  exitAuthorizationStatus?: string | null;
  alerts?: string[] | null;
  vendorSystemCode?: string | null;
  vendorConfirmationCode?: string | null;
  vendorConfirmationStatus?: string | null;
  vendorConfirmationTimestamp?: string | null;
  vendorMessage?: string | null;
  diagnostics?: string[] | null;
  correlationId?: string | null;
  message?: string | null;
  errorCode?: string | null;
}

interface VendorPaymentAcknowledgmentSearchResponseDto {
  items: VendorPaymentAcknowledgmentDto[];
  statusBuckets: VendorPaymentAcknowledgmentStatusBucketsDto;
  pageIndex: number;
  pageSize: number;
  hasMore: boolean;
}

interface VendorPaymentAcknowledgmentDetailResponseDto extends VendorPaymentAcknowledgmentDto {
  diagnostics?: VendorPaymentAcknowledgmentDiagnosticDto[] | null;
}

interface VendorPaymentAcknowledgmentDto {
  vendorPaymentAcknowledgmentId: string;
  paymentAttemptId: string;
  paymentConfirmationId: string;
  parkingSessionId?: string | null;
  vendorSystemCode: string;
  vendorSessionRef?: string | null;
  ticketNumber?: string | null;
  cardNum?: string | null;
  acknowledgmentStatus: string;
  statusBucket?: string | null;
  vendorCode?: string | null;
  vendorMessage?: string | null;
  requestFeeMinorUnits?: number | null;
  requestCurrencyCode?: string | null;
  confirmedFeeMinorUnits?: number | null;
  vendorConfirmedAt?: string | null;
  attemptCount: number;
  lastAttemptedAt?: string | null;
  nextRetryAt?: string | null;
  correlationId?: string | null;
  createdAt: string;
  updatedAt: string;
}

interface VendorPaymentAcknowledgmentStatusBucketsDto {
  pending: number;
  retryPending: number;
  failed: number;
  confirmed: number;
  skippedDisabled: number;
  cancelled: number;
}

interface VendorPaymentAcknowledgmentDiagnosticDto {
  code: string;
  message: string;
  source: string;
  retryable: boolean;
  correlationId?: string | null;
}

export interface OperatorConsoleOperatorContext {
  userId: string;
  operatorDeviceBindingId: string;
  operatorShiftId: string;
  siteId: string;
  siteGroupId: string;
}

const defaultOperatorContext: OperatorConsoleOperatorContext = {
  userId: localFallback(import.meta.env.VITE_OPERATOR_CONSOLE_USER_ID, "77000000-0000-0000-0000-000000000010"),
  operatorDeviceBindingId: localFallback(
    import.meta.env.VITE_OPERATOR_CONSOLE_DEVICE_BINDING_ID,
    "77000000-0000-0000-0000-000000000030"
  ),
  operatorShiftId: localFallback(import.meta.env.VITE_OPERATOR_CONSOLE_SHIFT_ID, "77000000-0000-0000-0000-000000000050"),
  siteId: localFallback(import.meta.env.VITE_OPERATOR_CONSOLE_SITE_ID, "77000000-0000-0000-0000-000000000002"),
  siteGroupId: localFallback(import.meta.env.VITE_OPERATOR_CONSOLE_SITE_GROUP_ID, "77000000-0000-0000-0000-000000000001")
};

const reviewDecisionPermissions = new Set([
  "operator-console.policy-import-review.manage",
  "operator-console.policy-import-review.review",
  "operator-console.policy-import-review.approve.legal",
  "operator-console.policy-import-review.approve.ops",
  "operator-console.policy-import-review.approve.qa",
  "operator-console.policy-import-review.approve.db"
]);

const fiscalVoidPermissions = new Set(["fiscal-issuance.void.command", "reconciliation.manage"]);

const defaultOperatorPermissions = localFallback(
  import.meta.env.VITE_OPERATOR_CONSOLE_PERMISSIONS,
  [
    "operator-console.policy-import-review.submit",
    "operator-console.policy-import-review.view-own",
    "operator-console.policy-import-review.review",
    "operator-console.policy-import-review.approve.legal",
    "operator-console.policy-import-review.approve.ops",
    "operator-console.policy-import-review.approve.qa",
    "operator-console.policy-import-review.approve.db",
    "operator-console.vendor-projection-health.view",
    "fiscal-issuance.status.read",
    "fiscal-issuance.void.command",
    "fiscal-issuance.void.audit.read"
  ].join(",")
);

export function createOperatorConsoleApiClient(): OperatorConsoleApiClient {
  return createHttpOperatorConsoleApiClient({
    baseUrl: import.meta.env.VITE_CENTRAL_PMS_BASE_URL ?? ""
  });
}

export function createHttpOperatorConsoleApiClient(options: { baseUrl?: string } = {}): OperatorConsoleApiClient {
  const baseUrl = options.baseUrl?.replace(/\/$/, "") ?? "";
  const permissions = parsePermissions(defaultOperatorPermissions);

  return {
    canDecideProductionPolicyImportReview() {
      return permissions.some((permission) => reviewDecisionPermissions.has(permission));
    },

    canVoidFiscalDocument() {
      return permissions.some((permission) => fiscalVoidPermissions.has(permission));
    },

    async evaluateAccessReadiness(input) {
      const correlationId = input.correlationId ?? newCorrelationId();
      const response = await fetch(`${baseUrl}/v1/ops/operator-console/access/readiness/evaluate`, {
        method: "POST",
        headers: operatorConsoleHeaders(correlationId, { json: true }),
        body: JSON.stringify({
          operatorUserId: input.operatorUserId ?? defaultOperatorContext.userId,
          operatorDeviceBindingId: input.operatorDeviceBindingId ?? defaultOperatorContext.operatorDeviceBindingId,
          operatorShiftId: input.operatorShiftId ?? defaultOperatorContext.operatorShiftId,
          siteId: input.siteId ?? null,
          siteGroupId: input.siteGroupId ?? null,
          requestedAction: input.requestedAction,
          targetEntityType: input.targetEntityType ?? null,
          targetEntityId: input.targetEntityId ?? null,
          workflowState: input.workflowState ?? null,
          correlationId,
          idempotencyKey: input.idempotencyKey ?? null,
          clientContext: input.clientContext ?? null,
          devModeContext: input.devModeContext ?? defaultDevModeContext()
        })
      });

      return parseResponse<AccessReadinessResponse>(response);
    },

    async lookupSessionByTicket(input) {
      const correlationId = newCorrelationId();
      const response = await fetch(`${baseUrl}/v1/ops/operator-console/sessions/lookup`, {
        method: "POST",
        headers: operatorConsoleHeaders(correlationId, { json: true }),
        body: JSON.stringify({
          userId: defaultOperatorContext.userId,
          operatorDeviceBindingId: defaultOperatorContext.operatorDeviceBindingId,
          siteId: defaultOperatorContext.siteId,
          siteGroupId: defaultOperatorContext.siteGroupId,
          operatorShiftId: defaultOperatorContext.operatorShiftId,
          parkingSessionId: null,
          ticketReference: input.ticketNumber,
          plateNumber: null,
          lookupMode: "TICKET_REFERENCE",
          idempotencyKey: `operator-console-ui-session-lookup-${input.ticketNumber}-${correlationId}`,
          correlationId
        })
      });

      return parseTicketLookupResponse(response);
    },

    async getFiscalIssuanceStatus(fiscalIssuanceReferenceId) {
      const correlationId = newCorrelationId();
      const response = await fetch(
        `${baseUrl}/v1/ops/operator-console/fiscal-issuance/references/${encodeURIComponent(fiscalIssuanceReferenceId)}`,
        { headers: operatorConsoleHeaders(correlationId) }
      );

      return parseResponse<FiscalIssuanceStatus>(response);
    },

    async lookupFiscalIssuanceStatus(query) {
      const correlationId = newCorrelationId();
      const search = new URLSearchParams();
      search.set("query", query);
      const response = await fetch(
        `${baseUrl}/v1/ops/operator-console/fiscal-issuance/lookup?${search.toString()}`,
        { headers: operatorConsoleHeaders(correlationId) }
      );

      return parseResponse<FiscalIssuanceStatus>(response);
    },

    async voidFiscalIssuanceReference(input) {
      const correlationId = input.correlationId ?? newCorrelationId();
      const response = await fetch(
        `${baseUrl}/v1/ops/operator-console/fiscal-issuance/references/${encodeURIComponent(input.fiscalIssuanceReferenceId)}/void`,
        {
          method: "POST",
          headers: operatorConsoleHeaders(correlationId, { json: true }),
          body: JSON.stringify({
            operatorActionRequestId: input.operatorActionRequestId,
            reasonCode: input.reasonCode,
            reasonText: input.reasonText,
            confirmationText: input.confirmationText,
            correlationId
          })
        }
      );

      return parseCommandResponse<FiscalIssuanceVoidResult>(response);
    },

    async listFiscalVoidActionAuditReport(input = {}) {
      const requestCorrelationId = newCorrelationId();
      const search = new URLSearchParams();
      addQuery(search, "from", input.from);
      addQuery(search, "to", input.to);
      addQuery(search, "siteId", input.siteId);
      addQuery(search, "siteGroupId", input.siteGroupId);
      addQuery(search, "operatorUserId", input.operatorUserId);
      addQuery(search, "fiscalIssuanceReferenceId", input.fiscalIssuanceReferenceId);
      addQuery(search, "fiscalDocumentNumber", input.fiscalDocumentNumber);
      addQuery(search, "resultClass", input.resultClass);
      addQuery(search, "correlationId", input.correlationId);
      addQuery(search, "limit", input.limit?.toString());
      addQuery(search, "offset", input.offset?.toString());

      const query = search.toString();
      const response = await fetch(
        `${baseUrl}/v1/ops/operator-console/audit/fiscal-void-actions${query ? `?${query}` : ""}`,
        { headers: operatorConsoleHeaders(requestCorrelationId) }
      );

      return toFiscalVoidActionAuditReport(await parseResponse<FiscalVoidActionAuditReportResponseDto>(response));
    },

    async listFiscalStatusViewAuditReport(input = {}) {
      const requestCorrelationId = newCorrelationId();
      const search = new URLSearchParams();
      addQuery(search, "from", input.from);
      addQuery(search, "to", input.to);
      addQuery(search, "siteId", input.siteId);
      addQuery(search, "siteGroupId", input.siteGroupId);
      addQuery(search, "operatorUserId", input.operatorUserId);
      addQuery(search, "fiscalIssuanceReferenceId", input.fiscalIssuanceReferenceId);
      addQuery(search, "resultClass", input.resultClass);
      addQuery(search, "correlationId", input.correlationId);
      addQuery(search, "limit", input.limit?.toString());
      addQuery(search, "offset", input.offset?.toString());

      const query = search.toString();
      const response = await fetch(
        `${baseUrl}/v1/ops/operator-console/audit/fiscal-status-views${query ? `?${query}` : ""}`,
        { headers: operatorConsoleHeaders(requestCorrelationId) }
      );

      return toFiscalStatusViewAuditReport(await parseResponse<FiscalStatusViewAuditReportResponseDto>(response));
    },

    async listAuditReport(input = {}) {
      const correlationId = newCorrelationId();
      const search = new URLSearchParams({ correlationId });
      addQuery(search, "siteId", input.siteId);
      addQuery(search, "parkingSessionId", input.parkingSessionId);
      addQuery(search, "validationStatus", input.validationStatus);
      addQuery(search, "from", input.from);
      addQuery(search, "to", input.to);
      addQuery(search, "limit", input.limit?.toString());
      addQuery(search, "offset", input.offset?.toString());

      const response = await fetch(`${baseUrl}/v1/ops/operator-console/audit/statutory-discounts?${search}`, {
        headers: operatorConsoleHeaders(correlationId)
      });

      return toAuditReport(await parseResponse<AuditReportResponseDto>(response));
    },

    async createStatutoryDiscountDraft(input) {
      const correlationId = newCorrelationId();
      const response = await fetch(`${baseUrl}/v1/ops/operator-console/statutory-discounts/draft`, {
        method: "POST",
        headers: operatorConsoleHeaders(correlationId, { json: true }),
        body: JSON.stringify({
          userId: defaultOperatorContext.userId,
          operatorDeviceBindingId: defaultOperatorContext.operatorDeviceBindingId,
          siteId: input.siteId ?? defaultOperatorContext.siteId,
          siteGroupId: input.siteGroupId ?? defaultOperatorContext.siteGroupId,
          operatorShiftId: defaultOperatorContext.operatorShiftId,
          parkingSessionId: input.parkingSessionId,
          ticketReference: input.ticketReference ?? null,
          plateNumber: input.plateNumber ?? null,
          entitlementType: input.entitlementType,
          idDocumentType: input.idDocumentType,
          issuingAuthority: input.issuingAuthority,
          expiryDate: null,
          maskedIdReference: input.maskedIdReference,
          entitlementFingerprint: null,
          evidenceCaptureRequested: input.evidenceCaptureRequested,
          evidenceAccessIntent: "METADATA_ONLY",
          operatorAttestation: input.operatorAttestation,
          attestationNotes: input.attestationNotes ?? null,
          reasonCode: input.reasonCode ?? "OPERATOR_UAT_SMOKE",
          idempotencyKey: `operator-console-ui-statutory-discount-draft-${input.parkingSessionId}-${input.entitlementType}`,
          correlationId
        })
      });
      return toDraftCreateResult(await parseResponse<DraftCreateResponseDto>(response));
    },

    async listStatutoryDiscountDrafts() {
      const correlationId = newCorrelationId();
      const response = await fetch(
        `${baseUrl}/v1/ops/operator-console/statutory-discounts/drafts?correlationId=${correlationId}`,
        { headers: operatorConsoleHeaders(correlationId) }
      );
      const body = await parseResponse<QueueResponse>(response);
      return body.items.map(toQueueItem);
    },

    async getStatutoryDiscountDraft(draftId) {
      const correlationId = newCorrelationId();
      const response = await fetch(
        `${baseUrl}/v1/ops/operator-console/statutory-discounts/drafts/${encodeURIComponent(draftId)}?correlationId=${correlationId}`,
        { headers: operatorConsoleHeaders(correlationId) }
      );
      return toDraftDetail(await parseResponse<DetailDto>(response));
    },

    async listStatutoryDiscountEvidence(draftId) {
      const correlationId = newCorrelationId();
      const response = await fetch(
        `${baseUrl}/v1/ops/operator-console/statutory-discounts/${encodeURIComponent(draftId)}/evidence?correlationId=${correlationId}`,
        { headers: operatorConsoleHeaders(correlationId) }
      );
      return toEvidenceList(await parseResponse<EvidenceListDto>(response));
    },

    async captureStatutoryDiscountEvidence(input) {
      const correlationId = newCorrelationId();
      const response = await fetch(
        `${baseUrl}/v1/ops/operator-console/statutory-discounts/${encodeURIComponent(input.draftId)}/evidence`,
        {
          method: "POST",
          headers: operatorConsoleHeaders(correlationId, { json: true }),
          body: JSON.stringify({
            userId: defaultOperatorContext.userId,
            operatorDeviceBindingId: defaultOperatorContext.operatorDeviceBindingId,
            siteId: input.siteId ?? null,
            siteGroupId: input.siteGroupId ?? null,
            operatorShiftId: defaultOperatorContext.operatorShiftId,
            evidenceType: input.evidenceType,
            captureMethod: input.captureMethod,
            fileName: input.fileName ?? null,
            contentType: input.contentType ?? null,
            sizeBytes: input.sizeBytes ?? null,
            storageReference: null,
            referenceNumber: input.referenceNumber ?? null,
            notes: input.notes ?? null,
            operatorConfirmation: input.operatorConfirmation,
            idempotencyKey: `operator-console-ui-evidence-${input.draftId}-${correlationId}`,
            correlationId
          })
        }
      );
      const body = await parseResponse<EvidenceCaptureResponseDto>(response);
      return {
        evidenceId: body.evidenceId,
        draftId: body.draftId,
        evidenceType: body.evidenceType,
        captureMethod: body.captureMethod,
        verificationStatus: body.verificationStatus,
        evidenceRequiredSatisfied: body.evidenceRequiredSatisfied,
        currentDraftStatus: mapStatus(body.currentDraftStatus),
        message: body.accessAllowed ? "Evidence metadata captured." : body.errorCode ?? "Evidence capture was not allowed."
      };
    },

    async submitStatutoryDiscountDecision(input) {
      const correlationId = newCorrelationId();
      const response = await fetch(
        `${baseUrl}/v1/ops/operator-console/statutory-discounts/${encodeURIComponent(input.draftId)}/decision`,
        {
          method: "POST",
          headers: operatorConsoleHeaders(correlationId, { json: true }),
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
    },

    async applyStatutoryDiscountPayableBasis(input) {
      const correlationId = newCorrelationId();
      const response = await fetch(
        `${baseUrl}/v1/ops/operator-console/statutory-discounts/${encodeURIComponent(input.draftId)}/apply-payable-basis`,
        {
          method: "POST",
          headers: operatorConsoleHeaders(correlationId, { json: true }),
          body: JSON.stringify({
            userId: defaultOperatorContext.userId,
            operatorDeviceBindingId: defaultOperatorContext.operatorDeviceBindingId,
            siteId: input.siteId ?? null,
            siteGroupId: input.siteGroupId ?? null,
            operatorShiftId: defaultOperatorContext.operatorShiftId,
            originalTariffSnapshotId: input.originalTariffSnapshotId ?? null,
            idempotencyKey: `operator-console-ui-apply-payable-basis-${input.draftId}-${correlationId}`,
            correlationId
          })
        }
      );
      const body = await parseResponse<PayableBasisApplicationResponseDto>(response);
      return toPayableBasisResult(body);
    },

    async dryRunProductionPolicyImport(input) {
      const correlationId = newCorrelationId();
      const response = await fetch(
        `${baseUrl}/v1/ops/operator-console/statutory-discounts/policies/import/dry-run`,
        {
          method: "POST",
          headers: operatorConsoleHeaders(correlationId, { json: true }),
          body: JSON.stringify({
            csvContent: input.csvContent,
            fileName: input.fileName ?? null,
            submittedByOperatorId: defaultOperatorContext.userId,
            correlationId
          })
        }
      );

      return parseResponse<ProductionPolicyImportDryRunResult>(response);
    },

    async submitProductionPolicyImportReview(input) {
      const correlationId = newCorrelationId();
      const response = await fetch(
        `${baseUrl}/v1/ops/operator-console/statutory-discounts/policies/import/reviews`,
        {
          method: "POST",
          headers: operatorConsoleHeaders(correlationId, { json: true }),
          body: JSON.stringify({
            dryRunResult: input.dryRunResult,
            fileName: input.fileName ?? null,
            submittedByOperatorId: defaultOperatorContext.userId,
            correlationId
          })
        }
      );

      return parseResponse<ProductionPolicyImportReviewResult>(response);
    },

    async decideProductionPolicyImportReview(input) {
      const correlationId = newCorrelationId();
      const response = await fetch(
        `${baseUrl}/v1/ops/operator-console/statutory-discounts/policies/import/reviews/${encodeURIComponent(input.reviewId)}/decision`,
        {
          method: "POST",
          headers: operatorConsoleHeaders(correlationId, { json: true }),
          body: JSON.stringify({
            action: input.action,
            reason: input.reason ?? null,
            reviewerOperatorId: defaultOperatorContext.userId,
            correlationId
          })
        }
      );

      return parseResponse<ProductionPolicyImportReviewResult>(response);
    },

    async listProductionPolicyImportReviews(input = {}) {
      const correlationId = newCorrelationId();
      const search = new URLSearchParams({ correlationId });
      addQuery(search, "status", input.status);
      addQuery(search, "makerOperatorId", input.makerOperatorId);
      addQuery(search, "reviewerOperatorId", input.reviewerOperatorId);
      addQuery(search, "reviewerRole", input.reviewerRole);
      addQuery(search, "createdFrom", input.createdFrom);
      addQuery(search, "createdTo", input.createdTo);
      addQuery(search, "limit", input.limit?.toString());
      addQuery(search, "offset", input.offset?.toString());

      const response = await fetch(
        `${baseUrl}/v1/ops/operator-console/statutory-discounts/policies/import/reviews?${search}`,
        { headers: operatorConsoleHeaders(correlationId) }
      );

      return parseResponse<ProductionPolicyImportReviewListResult>(response);
    },

    async getProductionPolicyImportReview(reviewId) {
      const correlationId = newCorrelationId();
      const response = await fetch(
        `${baseUrl}/v1/ops/operator-console/statutory-discounts/policies/import/reviews/${encodeURIComponent(reviewId)}?correlationId=${correlationId}`,
        { headers: operatorConsoleHeaders(correlationId) }
      );

      return parseResponse<ProductionPolicyImportReviewResult>(response);
    },

    async searchVendorPaymentAcknowledgments(input = {}) {
      const correlationId = newCorrelationId();
      const response = await fetch(`${baseUrl}/v1/ops/vendor-payment-acknowledgments/search`, {
        method: "POST",
        headers: operatorConsoleHeaders(correlationId, { json: true }),
        body: JSON.stringify({
          acknowledgmentStatus: blankToNull(input.acknowledgmentStatus),
          vendorSystemCode: blankToNull(input.vendorSystemCode),
          paymentAttemptId: blankToNull(input.paymentAttemptId),
          paymentConfirmationId: blankToNull(input.paymentConfirmationId),
          parkingSessionId: blankToNull(input.parkingSessionId),
          ticketNumber: blankToNull(input.ticketNumber),
          cardNum: blankToNull(input.cardNum),
          correlationId: blankToNull(input.correlationId),
          createdFrom: blankToNull(input.createdFrom),
          createdTo: blankToNull(input.createdTo),
          lastAttemptedFrom: blankToNull(input.lastAttemptedFrom),
          lastAttemptedTo: blankToNull(input.lastAttemptedTo),
          nextRetryDueOnly: input.nextRetryDueOnly ?? false,
          pageIndex: input.pageIndex ?? 0,
          pageSize: input.pageSize ?? 25
        })
      });

      return toVendorPaymentAcknowledgmentSearchResult(
        await parseResponse<VendorPaymentAcknowledgmentSearchResponseDto>(response)
      );
    },

    async getVendorPaymentAcknowledgment(vendorPaymentAcknowledgmentId) {
      const correlationId = newCorrelationId();
      const response = await fetch(
        `${baseUrl}/v1/ops/vendor-payment-acknowledgments/${encodeURIComponent(vendorPaymentAcknowledgmentId)}`,
        { headers: operatorConsoleHeaders(correlationId) }
      );

      return toVendorPaymentAcknowledgmentDetail(
        await parseResponse<VendorPaymentAcknowledgmentDetailResponseDto>(response)
      );
    },

    async listVendorSessionProjectionHealthTargets() {
      const correlationId = newCorrelationId();
      const response = await fetch(`${baseUrl}/v1/ops/vendor-session-projections/targets`, {
        headers: operatorConsoleHeaders(correlationId)
      });

      return parseResponse<VendorSessionProjectionHealthTargetsResponse>(response);
    },

    async getVendorSessionProjectionHealthTarget(projectionSyncTargetId) {
      const correlationId = newCorrelationId();
      const response = await fetch(
        `${baseUrl}/v1/ops/vendor-session-projections/targets/${encodeURIComponent(projectionSyncTargetId)}`,
        { headers: operatorConsoleHeaders(correlationId) }
      );

      return parseResponse<VendorSessionProjectionHealthTargetDetail>(response);
    },

    async getVendorSessionProjectionHealthSummary() {
      const correlationId = newCorrelationId();
      const response = await fetch(`${baseUrl}/v1/ops/vendor-session-projections/summary`, {
        headers: operatorConsoleHeaders(correlationId)
      });

      return parseResponse<VendorSessionProjectionHealthSummary>(response);
    }
  };
}

function operatorConsoleHeaders(correlationId: string, options: { json?: boolean } = {}) {
  return {
    ...(options.json ? { "Content-Type": "application/json" } : {}),
    "X-Correlation-Id": correlationId,
    "X-Operator-User-Id": defaultOperatorContext.userId,
    "X-ExitPass-User-Id": defaultOperatorContext.userId,
    "X-ExitPass-Permissions": defaultOperatorPermissions,
    "X-Operator-Device-Binding-Id": defaultOperatorContext.operatorDeviceBindingId,
    "X-Operator-Shift-Id": defaultOperatorContext.operatorShiftId,
    ...(defaultOperatorContext.siteId ? { "X-Site-Id": defaultOperatorContext.siteId } : {}),
    ...(defaultOperatorContext.siteGroupId ? { "X-Site-Group-Id": defaultOperatorContext.siteGroupId } : {})
  };
}

function parsePermissions(value: string) {
  return value
    .split(/[,\s]+/)
    .map((permission) => permission.trim().toLowerCase())
    .filter((permission) => permission.length > 0);
}

function localFallback(value: string | undefined, fallback: string) {
  return value && value.trim().length > 0 ? value : fallback;
}

function blankToNull(value: string | undefined) {
  return value && value.trim().length > 0 ? value.trim() : null;
}

export function getDefaultOperatorConsoleContext() {
  return { ...defaultOperatorContext };
}

export function defaultDevModeContext() {
  return {
    usesLocalDevFallbackContext: isUsingLocalFallbackContext(),
    environmentName: import.meta.env.MODE ?? "Development"
  };
}

function isUsingLocalFallbackContext() {
  return (
    !hasConfiguredValue(import.meta.env.VITE_OPERATOR_CONSOLE_USER_ID) ||
    !hasConfiguredValue(import.meta.env.VITE_OPERATOR_CONSOLE_DEVICE_BINDING_ID) ||
    !hasConfiguredValue(import.meta.env.VITE_OPERATOR_CONSOLE_SHIFT_ID) ||
    !hasConfiguredValue(import.meta.env.VITE_OPERATOR_CONSOLE_SITE_ID)
  );
}

function hasConfiguredValue(value: string | undefined) {
  return value !== undefined && value.trim().length > 0;
}

export function createMockOperatorConsoleApiClient(
  options: {
    drafts?: StatutoryDiscountDraftDetail[];
    readiness?: AccessReadinessResponse;
    listError?: OperatorConsoleApiError;
    detailError?: OperatorConsoleApiError;
    decisionError?: OperatorConsoleApiError;
    evidenceError?: OperatorConsoleApiError;
    ticketLookupError?: OperatorConsoleApiError;
    ticketLookupResults?: OperatorTicketLookupResult[];
    fiscalStatusError?: OperatorConsoleApiError;
    fiscalStatuses?: FiscalIssuanceStatus[];
    fiscalVoidError?: OperatorConsoleApiError;
    fiscalVoidResult?: FiscalIssuanceVoidResult;
    fiscalVoidAuthorized?: boolean;
    fiscalVoidActionAuditReportError?: OperatorConsoleApiError;
    fiscalVoidActionAuditReport?: FiscalVoidActionAuditReportResponse;
    fiscalStatusViewAuditReportError?: OperatorConsoleApiError;
    fiscalStatusViewAuditReport?: FiscalStatusViewAuditReportResponse;
    empty?: boolean;
    onTicketLookup?: (input: OperatorTicketLookupInput) => void;
    onDraftCreate?: (input: StatutoryDiscountDraftCreateInput) => void;
    onFiscalStatusLookup?: (query: string) => void;
    onFiscalVoid?: (input: FiscalIssuanceVoidInput) => void;
    onFiscalVoidActionAuditReport?: (input: FiscalVoidActionAuditReportQuery) => void;
    onFiscalStatusViewAuditReport?: (input: FiscalStatusViewAuditReportQuery) => void;
    onDecision?: (input: StatutoryDiscountDecisionInput) => void;
    onEvidenceCapture?: (input: StatutoryDiscountEvidenceCaptureInput) => void;
    onPayableBasisApply?: (input: StatutoryDiscountPayableBasisApplicationInput) => void;
    onProductionPolicyDryRun?: (input: ProductionPolicyImportDryRunInput) => void;
    onProductionPolicyReviewSubmit?: (input: ProductionPolicyImportReviewSubmitInput) => void;
    onProductionPolicyReviewDecision?: (input: ProductionPolicyImportReviewDecisionInput) => void;
    onVendorPaymentAcknowledgmentSearch?: (input: VendorPaymentAcknowledgmentSearchInput) => void;
    onVendorPaymentAcknowledgmentDetail?: (vendorPaymentAcknowledgmentId: string) => void;
    vendorPaymentAcknowledgments?: VendorPaymentAcknowledgmentDetail[];
    vendorSessionProjectionHealthTargets?: VendorSessionProjectionHealthTarget[];
    vendorSessionProjectionHealthConfig?: VendorSessionProjectionHealthConfig;
    onVendorSessionProjectionHealthTargets?: () => void;
    onVendorSessionProjectionHealthTargetDetail?: (projectionSyncTargetId: string) => void;
    onVendorSessionProjectionHealthSummary?: () => void;
    productionPolicyReviewDecisionAuthorized?: boolean;
  } = {}
): OperatorConsoleApiClient {
  const drafts = (options.drafts ?? mockDrafts).map((draft) => ({ ...draft }));
  const vendorPaymentAcknowledgments = (options.vendorPaymentAcknowledgments ?? mockVendorPaymentAcknowledgments()).map((item) => ({
    ...item,
    diagnostics: item.diagnostics.map((diagnostic) => ({ ...diagnostic }))
  }));
  const projectionHealthConfig = options.vendorSessionProjectionHealthConfig ?? mockVendorSessionProjectionHealthConfig();
  const projectionHealthTargets = (options.vendorSessionProjectionHealthTargets ?? mockVendorSessionProjectionHealthTargets()).map((item) => ({
    ...item
  }));
  const fiscalStatuses = (options.fiscalStatuses ?? mockFiscalIssuanceStatuses()).map((item) => ({ ...item }));
  const evidence = new Map<string, StatutoryDiscountEvidenceItem[]>();
  let productionPolicyReview: ProductionPolicyImportReviewResult | null = null;
  return {
    canDecideProductionPolicyImportReview() {
      return options.productionPolicyReviewDecisionAuthorized ?? true;
    },

    canVoidFiscalDocument() {
      return options.fiscalVoidAuthorized ?? true;
    },

    async evaluateAccessReadiness(input) {
      await delay();
      return options.readiness ?? mockAccessReadiness(input);
    },

    async lookupSessionByTicket(input) {
      await delay();
      options.onTicketLookup?.(input);
      if (options.ticketLookupError) {
        throw options.ticketLookupError;
      }

      const results = options.ticketLookupResults ?? mockTicketLookupResults;
      const match = results.find((item) => item.ticketNumber === input.ticketNumber || item.cardNum === input.ticketNumber);
      if (match) {
        return { ...match, ticketNumber: match.ticketNumber ?? input.ticketNumber };
      }

      return {
        sessionFound: false,
        ticketNumber: input.ticketNumber,
        correlationId: newCorrelationId(),
        message: "Ticket not found."
      };
    },

    async createStatutoryDiscountDraft(input) {
      await delay();
      options.onDraftCreate?.(input);
      const existing = drafts.find(
        (draft) => draft.parkingSessionId === input.parkingSessionId && draft.entitlementType === input.entitlementType
      );
      if (existing) {
        return {
          accepted: true,
          persisted: true,
          draftId: existing.draftId,
          parkingSessionId: existing.parkingSessionId,
          entitlementType: existing.entitlementType,
          validationStatus: existing.status,
          evidenceCaptureRequired: existing.policyContext.evidenceRequired,
          evidenceRequired: existing.policyContext.evidenceRequired,
          evidenceReferenceCreated: false,
          reusedExistingDraft: true,
          message: "Existing statutory discount draft opened."
        };
      }

      const created = {
        ...mockDrafts[0],
        draftId: "47000000-0000-0000-0000-000000000099",
        parkingSessionId: input.parkingSessionId,
        ticketReference: input.ticketReference ?? "UAT-TICKET",
        plateNumber: input.plateNumber ?? "Unknown",
        siteId: input.siteId ?? defaultOperatorContext.siteId,
        siteGroupId: input.siteGroupId ?? defaultOperatorContext.siteGroupId,
        entitlementType: input.entitlementType === "PWD" ? "PWD" : "Senior Citizen",
        status: "Requested" as DraftStatus,
        maskedIdReference: input.maskedIdReference,
        issuingAuthority: input.issuingAuthority,
        evidenceCaptured: false,
        evidenceRequiredSatisfied: false,
        evidenceCount: 0
      };
      drafts.unshift(created);
      return {
        accepted: true,
        persisted: true,
        draftId: created.draftId,
        parkingSessionId: created.parkingSessionId,
        entitlementType: created.entitlementType,
        validationStatus: created.status,
        evidenceCaptureRequired: created.policyContext.evidenceRequired,
        evidenceRequired: created.policyContext.evidenceRequired,
        evidenceReferenceCreated: false,
        reusedExistingDraft: false,
        message: "Statutory discount draft created."
      };
    },

    async getFiscalIssuanceStatus(fiscalIssuanceReferenceId) {
      await delay();
      options.onFiscalStatusLookup?.(fiscalIssuanceReferenceId);
      if (options.fiscalStatusError) {
        throw options.fiscalStatusError;
      }

      const match = fiscalStatuses.find((item) => item.fiscalIssuanceReferenceId === fiscalIssuanceReferenceId);
      if (!match) {
        throw {
          status: "not-found",
          message: "Fiscal issuance reference was not found.",
          errorCode: "FISCAL_ISSUANCE_REFERENCE_NOT_FOUND"
        } satisfies OperatorConsoleApiError;
      }

      return { ...match };
    },

    async lookupFiscalIssuanceStatus(query) {
      await delay();
      const trimmed = query.trim();
      options.onFiscalStatusLookup?.(trimmed);
      if (options.fiscalStatusError) {
        throw options.fiscalStatusError;
      }

      const match = fiscalStatuses.find((item) =>
        item.fiscalIssuanceReferenceId === trimmed || item.fiscalDocumentNumber === trimmed
      );
      if (!match) {
        throw {
          status: "not-found",
          message: "Fiscal status lookup did not match a fiscal issuance reference.",
          errorCode: "FISCAL_ISSUANCE_LOOKUP_NOT_FOUND"
        } satisfies OperatorConsoleApiError;
      }

      return { ...match };
    },

    async voidFiscalIssuanceReference(input) {
      await delay();
      options.onFiscalVoid?.(input);
      if (options.fiscalVoidError) {
        throw options.fiscalVoidError;
      }

      const result = options.fiscalVoidResult ?? mockFiscalVoidResult(input.fiscalIssuanceReferenceId);
      const match = fiscalStatuses.find((item) => item.fiscalIssuanceReferenceId === input.fiscalIssuanceReferenceId);
      if (match && ["pos_server_void_recorded", "pos_server_void_idempotent_replay", "pos_server_already_voided"].includes(result.status)) {
        match.posServerFiscalDocumentReadStatus = "AVAILABLE";
        match.posServerFiscalDocumentStatusCodeKey = "voided";
        match.posServerVoidStatus = "recorded";
        match.posServerVoidReasonCode = result.voidReasonCode ?? input.reasonCode;
        match.posServerVoidedAt = result.voidedAt ?? new Date("2026-07-10T00:00:00Z").toISOString();
      }

      return { ...result };
    },

    async listFiscalVoidActionAuditReport(input = {}) {
      await delay();
      options.onFiscalVoidActionAuditReport?.(input);
      if (options.fiscalVoidActionAuditReportError) {
        throw options.fiscalVoidActionAuditReportError;
      }

      const report = options.fiscalVoidActionAuditReport ?? mockFiscalVoidActionAuditReport();
      return {
        ...report,
        items: options.empty ? [] : report.items.map((item) => ({ ...item })),
        totalCount: options.empty ? 0 : report.totalCount,
        limit: input.limit ?? report.limit,
        offset: input.offset ?? report.offset
      };
    },

    async listFiscalStatusViewAuditReport(input = {}) {
      await delay();
      options.onFiscalStatusViewAuditReport?.(input);
      if (options.fiscalStatusViewAuditReportError) {
        throw options.fiscalStatusViewAuditReportError;
      }

      const report = options.fiscalStatusViewAuditReport ?? mockFiscalStatusViewAuditReport();
      return {
        ...report,
        items: options.empty ? [] : report.items.map((item) => ({ ...item })),
        totalCount: options.empty ? 0 : report.totalCount,
        limit: input.limit ?? report.limit,
        offset: input.offset ?? report.offset
      };
    },

    async listAuditReport() {
      await delay();
      return {
        items: options.empty ? [] : drafts.map(toAuditReportItem),
        totalCount: options.empty ? 0 : drafts.length,
        limit: 25,
        offset: 0,
        correlationId: newCorrelationId()
      };
    },

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

    async listStatutoryDiscountEvidence(draftId) {
      await delay();
      if (options.evidenceError) {
        throw options.evidenceError;
      }

      const draft = drafts.find((item) => item.draftId === draftId);
      if (!draft) {
        throw {
          status: "not-found",
          message: "Statutory discount draft was not found.",
          errorCode: "DRAFT_NOT_FOUND"
        } satisfies OperatorConsoleApiError;
      }

      const items = evidence.get(draftId) ?? [];
      return {
        draftId,
        evidenceRequired: draft.policyContext.evidenceRequired,
        evidenceRequiredSatisfied: draft.evidenceRequiredSatisfied,
        requiredEvidenceTypes: draft.requiredEvidenceTypes,
        evidenceCount: items.length,
        latestEvidenceStatus: items[0]?.verificationStatus,
        items
      };
    },

    async captureStatutoryDiscountEvidence(input) {
      await delay();
      if (options.evidenceError) {
        throw options.evidenceError;
      }

      options.onEvidenceCapture?.(input);
      const item: StatutoryDiscountEvidenceItem = {
        evidenceId: `mock-evidence-${Date.now()}`,
        draftId: input.draftId,
        evidenceType: input.evidenceType,
        captureMethod: input.captureMethod,
        storageReference: input.captureMethod === "MANUAL_REFERENCE" ? "manual-reference:****1234" : "operator-confirmed",
        capturedByUserId: defaultOperatorContext.userId,
        capturedAt: new Date().toISOString(),
        redactionStatus: "NOT_REDACTED",
        verificationStatus: "CAPTURED"
      };
      evidence.set(input.draftId, [item, ...(evidence.get(input.draftId) ?? [])]);

      const draft = drafts.find((item) => item.draftId === input.draftId);
      if (draft) {
        draft.evidenceCaptured = true;
        draft.evidenceRequiredSatisfied = true;
        draft.evidenceCount = evidence.get(input.draftId)?.length ?? 1;
        draft.latestEvidenceStatus = "CAPTURED";
      }

      return {
        evidenceId: item.evidenceId,
        draftId: input.draftId,
        evidenceType: input.evidenceType,
        captureMethod: input.captureMethod,
        verificationStatus: "CAPTURED",
        evidenceRequiredSatisfied: true,
        currentDraftStatus: draft?.status ?? "Requested",
        message: "Evidence metadata captured."
      };
    },

    async submitStatutoryDiscountDecision(input) {
      await delay();
      if (options.decisionError) {
        throw options.decisionError;
      }

      options.onDecision?.(input);
      const draft = drafts.find((item) => item.draftId === input.draftId);
      if (draft) {
        draft.status = input.decision === "APPROVE" ? "Approved" : "Rejected";
      }
      return {
        accepted: true,
        persisted: true,
        currentStatus: input.decision === "APPROVE" ? "Approved" : "Rejected",
        message: input.decision === "APPROVE" ? "Decision approved." : "Decision rejected."
      };
    },

    async applyStatutoryDiscountPayableBasis(input) {
      await delay();
      options.onPayableBasisApply?.(input);
      const draft = drafts.find((item) => item.draftId === input.draftId);
      if (draft) {
        draft.payableBasisApplicationStatus = "APPLIED";
        draft.payableBasisApplicationId = "4a000000-0000-0000-0000-000000000001";
        draft.appliedTariffSnapshotId = "4b000000-0000-0000-0000-00000000000a";
        draft.statutoryDiscountAmountMinorUnits = draft.statutoryDiscountAmountMinorUnits ?? 3600;
        draft.finalPayableAmountMinorUnits = draft.finalPayableAmountMinorUnits ?? draft.payableAmountMinorUnits;
        draft.payableBasisPreview = `APPLIED - ${formatMoney(draft.finalPayableAmountMinorUnits, draft.currencyCode)}`;
      }

      return {
        accepted: true,
        persisted: true,
        alreadyApplied: false,
        applicationStatus: "APPLIED",
        payableBasisApplicationId: draft?.payableBasisApplicationId,
        statutoryDiscountValidationId: input.draftId,
        parkingSessionId: draft?.parkingSessionId,
        originalTariffSnapshotId: input.originalTariffSnapshotId ?? draft?.originalTariffSnapshotId,
        appliedTariffSnapshotId: draft?.appliedTariffSnapshotId,
        grossAmountMinorUnits: draft?.originalAmountMinorUnits,
        vatAmountMinorUnits: draft?.vatAmountMinorUnits,
        vatExclusiveAmountMinorUnits: draft?.vatExclusiveAmountMinorUnits,
        statutoryDiscountAmountMinorUnits: draft?.statutoryDiscountAmountMinorUnits,
        finalPayableAmountMinorUnits: draft?.finalPayableAmountMinorUnits ?? draft?.payableAmountMinorUnits,
        currencyCode: draft?.currencyCode,
        message: "Payable basis applied."
      };
    },

    async dryRunProductionPolicyImport(input) {
      await delay();
      options.onProductionPolicyDryRun?.(input);
      const rowCount = Math.max(input.csvContent.split(/\r?\n/).filter((line) => line.trim()).length - 1, 0);
      return {
        imported: false,
        importedRowCount: 0,
        dryRunOnly: true,
        message: "Dry run completed. No policies were imported.",
        summary: {
          totalRows: rowCount,
          passCount: rowCount > 0 ? rowCount : 0,
          warnCount: 0,
          failCount: 0,
          importableCount: rowCount,
          manualReviewCount: 0,
          notImportableCount: 0,
          dryRunOnlyCount: 0,
          duplicateCount: 0
        },
        rows: rowCount > 0
          ? [
              {
                rowNumber: 2,
                policyCode: "PH_VALID_SC_IMPORT_001",
                entitlementType: "SENIOR_CITIZEN",
                decision: "IMPORTABLE_AFTER_APPROVAL",
                findings: []
              }
            ]
          : [],
        correlationId: newCorrelationId()
      };
    },

    async submitProductionPolicyImportReview(input) {
      await delay();
      options.onProductionPolicyReviewSubmit?.(input);
      productionPolicyReview = {
        imported: false,
        productionPolicyActivationBlocked: true,
        message: "Review submission created. No policies were imported.",
        submission: {
          reviewId: "99000000-0000-0000-0000-000000000001",
          makerOperatorId: defaultOperatorContext.userId,
          fileName: input.fileName,
          status: "LEGAL_REVIEW_PENDING",
          dryRunSummary: input.dryRunResult.summary,
          reviewerDecisions: [],
          history: [
            {
              action: "SUBMIT_FOR_REVIEW",
              status: "LEGAL_REVIEW_PENDING",
              actorOperatorId: defaultOperatorContext.userId,
              occurredAt: new Date().toISOString(),
              correlationId: input.dryRunResult.correlationId
            }
          ],
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString()
        },
        findings: [],
        correlationId: input.dryRunResult.correlationId
      };

      return productionPolicyReview;
    },

    async decideProductionPolicyImportReview(input) {
      await delay();
      options.onProductionPolicyReviewDecision?.(input);
      const current = productionPolicyReview ?? mockProductionPolicyReview();
      const approved = input.action.startsWith("APPROVE");
      const status = approved ? "APPROVED_FOR_DB_REPO_ALIGNMENT" : input.action === "REJECT" ? "REJECTED" : "SUBMITTED_FOR_REVIEW";
      const reviewerRole = input.action.startsWith("APPROVE") ? input.action.replace("APPROVE_", "") : undefined;
      productionPolicyReview = {
        ...current,
        message: "Review decision recorded. No policies were imported or activated.",
        submission: {
          ...current.submission,
          status,
          reviewerDecisions: approved
            ? [
                ...current.submission.reviewerDecisions,
                {
                  reviewerRole: reviewerRole ?? "LEGAL",
                  action: input.action,
                  reviewerOperatorId: defaultOperatorContext.userId,
                  reason: input.reason,
                  decidedAt: new Date().toISOString(),
                  correlationId: current.correlationId
                }
              ]
            : current.submission.reviewerDecisions,
          history: [
            ...current.submission.history,
            {
              action: input.action,
              status,
              actorOperatorId: defaultOperatorContext.userId,
              reviewerRole,
              reason: input.reason,
              occurredAt: new Date().toISOString(),
              correlationId: current.correlationId
            }
          ],
          updatedAt: new Date().toISOString()
        }
      };

      return productionPolicyReview;
    },

    async listProductionPolicyImportReviews(input = {}) {
      await delay();
      const reviews = options.empty
        ? []
        : [productionPolicyReview ?? mockProductionPolicyReview()];
      const filtered = reviews.filter((review) => {
        const matchesStatus = !input.status || review.submission.status === input.status;
        const matchesMaker = !input.makerOperatorId || review.submission.makerOperatorId === input.makerOperatorId;
        const matchesReviewer = !input.reviewerOperatorId ||
          review.submission.reviewerDecisions.some((decision) => decision.reviewerOperatorId === input.reviewerOperatorId);
        const matchesRole = !input.reviewerRole ||
          review.submission.reviewerDecisions.some((decision) => decision.reviewerRole === input.reviewerRole);
        return matchesStatus && matchesMaker && matchesReviewer && matchesRole;
      });
      const offset = input.offset ?? 0;
      const limit = input.limit ?? 50;

      return {
        imported: false,
        productionPolicyActivationBlocked: true,
        items: filtered.slice(offset, offset + limit).map((review) => ({
          imported: false,
          productionPolicyActivationBlocked: true,
          submission: review.submission,
          findings: review.findings
        })),
        totalCount: filtered.length,
        limit,
        offset,
        correlationId: newCorrelationId()
      };
    },

    async getProductionPolicyImportReview(reviewId) {
      await delay();
      const review = productionPolicyReview ?? mockProductionPolicyReview();
      if (!review || review.submission.reviewId !== reviewId) {
        throw {
          status: "not-found",
          message: "Review submission was not found.",
          errorCode: "OPERATOR_CONSOLE_POLICY_IMPORT_REVIEW_NOT_FOUND"
        } satisfies OperatorConsoleApiError;
      }

      return {
        ...review,
        message: "Review submission loaded. No policies were imported or activated."
      };
    },

    async searchVendorPaymentAcknowledgments(input = {}) {
      await delay();
      options.onVendorPaymentAcknowledgmentSearch?.(input);
      const pageIndex = input.pageIndex ?? 0;
      const pageSize = input.pageSize ?? 25;
      const filtered = vendorPaymentAcknowledgments.filter((item) => {
        const matchesStatus = !input.acknowledgmentStatus || item.acknowledgmentStatus === input.acknowledgmentStatus;
        const matchesVendor = !input.vendorSystemCode ||
          item.vendorSystemCode.toUpperCase().includes(input.vendorSystemCode.toUpperCase());
        const matchesTicket = !input.ticketNumber ||
          item.ticketNumber?.toUpperCase().includes(input.ticketNumber.toUpperCase());
        const matchesCard = !input.cardNum ||
          item.cardNum?.toUpperCase().includes(input.cardNum.toUpperCase());
        const matchesRetryDue = !input.nextRetryDueOnly ||
          (item.acknowledgmentStatus === "RETRY_PENDING" &&
            (!item.nextRetryAt || new Date(item.nextRetryAt).getTime() <= Date.now()));
        return matchesStatus && matchesVendor && matchesTicket && matchesCard && matchesRetryDue;
      });
      const offset = pageIndex * pageSize;

      return {
        items: filtered.slice(offset, offset + pageSize).map(({ diagnostics: _diagnostics, ...item }) => ({ ...item })),
        statusBuckets: countVendorPaymentAcknowledgmentBuckets(filtered),
        pageIndex,
        pageSize,
        hasMore: offset + pageSize < filtered.length
      };
    },

    async getVendorPaymentAcknowledgment(vendorPaymentAcknowledgmentId) {
      await delay();
      options.onVendorPaymentAcknowledgmentDetail?.(vendorPaymentAcknowledgmentId);
      const detail = vendorPaymentAcknowledgments.find(
        (item) => item.vendorPaymentAcknowledgmentId === vendorPaymentAcknowledgmentId
      );
      if (!detail) {
        throw {
          status: "not-found",
          message: "Vendor payment acknowledgment was not found.",
          errorCode: "VENDOR_PAYMENT_ACKNOWLEDGMENT_NOT_FOUND"
        } satisfies OperatorConsoleApiError;
      }

      return {
        ...detail,
        diagnostics: detail.diagnostics.map((diagnostic) => ({ ...diagnostic }))
      };
    },

    async listVendorSessionProjectionHealthTargets() {
      await delay();
      options.onVendorSessionProjectionHealthTargets?.();
      return {
        targets: projectionHealthTargets.map((target) => ({ ...target })),
        config: { ...projectionHealthConfig }
      };
    },

    async getVendorSessionProjectionHealthTarget(projectionSyncTargetId) {
      await delay();
      options.onVendorSessionProjectionHealthTargetDetail?.(projectionSyncTargetId);
      const target = projectionHealthTargets.find((item) => item.projectionSyncTargetId === projectionSyncTargetId);
      if (!target) {
        throw {
          status: "not-found",
          message: "Vendor session projection sync target was not found.",
          errorCode: "VENDOR_SESSION_PROJECTION_TARGET_NOT_FOUND"
        } satisfies OperatorConsoleApiError;
      }

      return {
        target: { ...target },
        latestProjectedRecords: mockVendorSessionProjectionLatestRecords(target.projectionSyncTargetId),
        config: { ...projectionHealthConfig }
      };
    },

    async getVendorSessionProjectionHealthSummary() {
      await delay();
      options.onVendorSessionProjectionHealthSummary?.();
      return buildVendorSessionProjectionHealthSummary(projectionHealthTargets, projectionHealthConfig);
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

async function parseCommandResponse<T extends { status?: string }>(response: Response): Promise<T> {
  const text = await response.text();
  const body = text ? JSON.parse(text) : {};
  if (response.ok || typeof body.status === "string") {
    return body as T;
  }

  throw {
    status: response.status === 404 ? "not-found" : response.status === 401 || response.status === 403 ? "access-denied" : "error",
    message: body.message ?? body.errorCode ?? "Operator Console request failed.",
    errorCode: body.errorCode
  } satisfies OperatorConsoleApiError;
}

async function parseTicketLookupResponse(response: Response): Promise<OperatorTicketLookupResult> {
  const text = await response.text();
  const body = (text ? JSON.parse(text) : {}) as OperatorTicketLookupResponseDto;
  if (response.status === 404) {
    return {
      sessionFound: false,
      correlationId: body.correlationId ?? undefined,
      message: body.message ?? body.errorCode ?? "Ticket not found."
    };
  }

  if (response.ok) {
    return toTicketLookupResult(body);
  }

  throw {
    status: response.status === 401 || response.status === 403 ? "access-denied" : "error",
    message: body.message ?? body.errorCode ?? "Operator Console ticket lookup failed.",
    errorCode: body.errorCode ?? undefined
  } satisfies OperatorConsoleApiError;
}

function toTicketLookupResult(body: OperatorTicketLookupResponseDto): OperatorTicketLookupResult {
  return {
    sessionFound: body.sessionFound ?? true,
    accessAllowed: body.accessAllowed ?? undefined,
    sessionEligible: body.sessionEligible ?? undefined,
    parkingSessionId: body.parkingSessionId ?? undefined,
    siteId: body.siteId ?? undefined,
    siteGroupId: body.siteGroupId ?? undefined,
    ticketNumber: body.ticketReference ?? body.ticketNumber ?? undefined,
    cardNum: body.cardNum ?? body.ticketReference ?? body.ticketNumber ?? undefined,
    plateLicense: body.plateNumber ?? body.plateLicense ?? undefined,
    parkingInTime: body.entryTime ?? body.parkingInTime ?? undefined,
    parkingDurationSeconds: body.parkingDurationSeconds ?? undefined,
    feeMinorUnits: body.currentPayableAmountMinorUnits ?? body.feeMinorUnits ?? undefined,
    currencyCode: body.currencyCode ?? undefined,
    feeRuleType: body.feeRuleType ?? undefined,
    feeRuleIndexCode: body.feeRuleIndexCode ?? undefined,
    feeRuleName: body.feeRuleName ?? undefined,
    paymentAttemptStatus: body.paymentAttemptStatus ?? undefined,
    paymentStatus: body.paymentStatus ?? undefined,
    paymentConfirmationStatus: body.paymentConfirmationStatus ?? undefined,
    vendorSystemCode: body.vendorSystemCode ?? undefined,
    vendorConfirmationCode: body.vendorConfirmationCode ?? undefined,
    vendorConfirmationStatus: body.vendorConfirmationStatus ?? undefined,
    vendorConfirmationTimestamp: body.vendorConfirmationTimestamp ?? undefined,
    vendorMessage: body.vendorMessage ?? undefined,
    diagnostics: body.diagnostics ?? body.alerts ?? undefined,
    correlationId: body.correlationId ?? undefined,
    message: body.message ?? body.ineligibilityReason ?? body.errorCode ?? undefined
  };
}

function toDraftCreateResult(body: DraftCreateResponseDto): StatutoryDiscountDraftCreateResult {
  return {
    accepted: body.draftAccepted,
    persisted: body.draftPersisted,
    draftId: body.draftId ?? undefined,
    parkingSessionId: body.parkingSessionId ?? undefined,
    entitlementType: body.entitlementType ?? undefined,
    validationStatus: body.validationStatus ? mapStatus(body.validationStatus) : undefined,
    evidenceCaptureRequired: body.evidenceCaptureRequired,
    evidenceRequired: body.evidenceRequired,
    evidenceReferenceCreated: body.evidenceReferenceCreated,
    reusedExistingDraft: body.reusedExistingDraft,
    errorCode: body.errorCode ?? undefined,
    message: body.draftAccepted
      ? body.reusedExistingDraft
        ? "Existing statutory discount draft opened."
        : "Statutory discount draft created."
      : body.operatorMessage ?? body.ineligibilityReason ?? body.errorCode ?? "Statutory discount draft was not accepted."
  };
}

function toVendorPaymentAcknowledgmentSearchResult(
  body: VendorPaymentAcknowledgmentSearchResponseDto
): VendorPaymentAcknowledgmentSearchResult {
  return {
    items: body.items.map(toVendorPaymentAcknowledgmentSummary),
    statusBuckets: {
      pending: body.statusBuckets.pending,
      retryPending: body.statusBuckets.retryPending,
      failed: body.statusBuckets.failed,
      confirmed: body.statusBuckets.confirmed,
      skippedDisabled: body.statusBuckets.skippedDisabled,
      cancelled: body.statusBuckets.cancelled
    },
    pageIndex: body.pageIndex,
    pageSize: body.pageSize,
    hasMore: body.hasMore
  };
}

function toVendorPaymentAcknowledgmentDetail(
  body: VendorPaymentAcknowledgmentDetailResponseDto
): VendorPaymentAcknowledgmentDetail {
  return {
    ...toVendorPaymentAcknowledgmentSummary(body),
    diagnostics: (body.diagnostics ?? []).map(toVendorPaymentAcknowledgmentDiagnostic)
  };
}

function toVendorPaymentAcknowledgmentSummary(dto: VendorPaymentAcknowledgmentDto): VendorPaymentAcknowledgmentSummary {
  return {
    vendorPaymentAcknowledgmentId: dto.vendorPaymentAcknowledgmentId,
    paymentAttemptId: dto.paymentAttemptId,
    paymentConfirmationId: dto.paymentConfirmationId,
    parkingSessionId: dto.parkingSessionId ?? undefined,
    vendorSystemCode: dto.vendorSystemCode,
    vendorSessionRef: dto.vendorSessionRef ?? undefined,
    ticketNumber: dto.ticketNumber ?? undefined,
    cardNum: dto.cardNum ?? undefined,
    acknowledgmentStatus: dto.acknowledgmentStatus,
    statusBucket: dto.statusBucket ?? undefined,
    vendorCode: dto.vendorCode ?? undefined,
    vendorMessage: dto.vendorMessage ?? undefined,
    requestFeeMinorUnits: dto.requestFeeMinorUnits ?? undefined,
    requestCurrencyCode: dto.requestCurrencyCode ?? undefined,
    confirmedFeeMinorUnits: dto.confirmedFeeMinorUnits ?? undefined,
    vendorConfirmedAt: dto.vendorConfirmedAt ?? undefined,
    attemptCount: dto.attemptCount,
    lastAttemptedAt: dto.lastAttemptedAt ?? undefined,
    nextRetryAt: dto.nextRetryAt ?? undefined,
    correlationId: dto.correlationId ?? undefined,
    createdAt: dto.createdAt,
    updatedAt: dto.updatedAt
  };
}

function toVendorPaymentAcknowledgmentDiagnostic(
  dto: VendorPaymentAcknowledgmentDiagnosticDto
): VendorPaymentAcknowledgmentDiagnostic {
  return {
    code: dto.code,
    message: dto.message,
    source: dto.source,
    retryable: dto.retryable,
    correlationId: dto.correlationId ?? undefined
  };
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
    evidenceRequiredSatisfied: item.evidenceRequiredSatisfied ?? false,
    evidenceCount: item.evidenceCount ?? 0,
    latestEvidenceStatus: item.latestEvidenceStatus ?? undefined,
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
    evidenceRequiredSatisfied: item.evidenceRequiredSatisfied ?? item.evidenceCaptured,
    evidenceCount: item.evidenceCount ?? 0,
    latestEvidenceStatus: item.latestEvidenceStatus ?? undefined,
    requiredEvidenceTypes: item.requiredEvidenceTypes ?? [],
    originalTariffSnapshotId: item.originalTariffSnapshotId ?? undefined,
    payableBasisApplicationId: item.payableBasisApplicationId ?? undefined,
    appliedTariffSnapshotId: item.appliedTariffSnapshotId ?? undefined,
    vatAmountMinorUnits: item.vatAmountMinorUnits ?? undefined,
    vatExclusiveAmountMinorUnits: item.vatExclusiveAmountMinorUnits ?? undefined,
    statutoryDiscountAmountMinorUnits: item.statutoryDiscountAmountMinorUnits ?? undefined,
    finalPayableAmountMinorUnits: item.finalPayableAmountMinorUnits ?? item.payableAmountMinorUnits ?? undefined,
    payableBasisApplicationStatus: item.payableBasisApplicationStatus ?? undefined,
    auditActivity: item.activity.length > 0 ? item.activity : ["No activity history is available yet."]
  };
}

function toPayableBasisResult(body: PayableBasisApplicationResponseDto): StatutoryDiscountPayableBasisApplicationResult {
  return {
    accepted: body.applicationAccepted,
    persisted: body.applicationPersisted,
    alreadyApplied: body.alreadyApplied,
    applicationStatus: body.applicationStatus ?? undefined,
    payableBasisApplicationId: body.payableBasisApplicationId ?? undefined,
    statutoryDiscountValidationId: body.statutoryDiscountValidationId ?? undefined,
    parkingSessionId: body.parkingSessionId ?? undefined,
    originalTariffSnapshotId: body.originalTariffSnapshotId ?? undefined,
    appliedTariffSnapshotId: body.appliedTariffSnapshotId ?? undefined,
    grossAmountMinorUnits: body.grossAmountMinorUnits ?? undefined,
    vatAmountMinorUnits: body.vatAmountMinorUnits ?? undefined,
    vatExclusiveAmountMinorUnits: body.vatExclusiveAmountMinorUnits ?? undefined,
    statutoryDiscountAmountMinorUnits: body.statutoryDiscountAmountMinorUnits ?? undefined,
    finalPayableAmountMinorUnits: body.finalPayableAmountMinorUnits ?? undefined,
    currencyCode: body.currencyCode ?? undefined,
    errorCode: body.errorCode ?? undefined,
    message: payableBasisMessage(body)
  };
}

function toEvidenceList(dto: EvidenceListDto): StatutoryDiscountEvidenceList {
  return {
    draftId: dto.draftId,
    evidenceRequired: dto.evidenceRequired,
    evidenceRequiredSatisfied: dto.evidenceRequiredSatisfied,
    requiredEvidenceTypes: dto.requiredEvidenceTypes,
    evidenceCount: dto.evidenceCount,
    latestEvidenceStatus: dto.latestEvidenceStatus ?? undefined,
    items: dto.items.map(toEvidenceItem)
  };
}

function toEvidenceItem(dto: EvidenceItemDto): StatutoryDiscountEvidenceItem {
  return {
    evidenceId: dto.evidenceId,
    draftId: dto.draftId,
    evidenceType: dto.evidenceType,
    captureMethod: dto.captureMethod,
    storageReference: dto.storageReference ?? undefined,
    capturedByUserId: dto.capturedByUserId ?? undefined,
    capturedAt: dto.capturedAt,
    redactionStatus: dto.redactionStatus,
    verificationStatus: dto.verificationStatus,
    correlationId: dto.correlationId ?? undefined
  };
}

function addQuery(search: URLSearchParams, key: string, value?: string) {
  if (value && value.trim().length > 0) {
    search.set(key, value.trim());
  }
}

function toPolicyContext(item: QueueItemDto | DetailDto): StatutoryDiscountPolicyContext {
  const basis = item.policyResolutionBasis ?? "UNKNOWN";
  const detail = item as DetailDto;
  const verificationStatus = detail.verificationStatus ?? item.verificationStatus ?? undefined;
  const readinessClassification =
    detail.policyReadinessClassification ?? item.policyReadinessClassification ?? inferPolicyReadiness(item, verificationStatus);
  const requiresManualReview =
    detail.requiresManualReview ?? item.requiresManualReview ?? readinessClassification !== "READY_VERIFIED";
  const kind = policyKind(basis, verificationStatus, readinessClassification);
  const operatorMessage = detail.operatorMessage ?? item.operatorMessage ?? policyReadinessMessage(readinessClassification);
  return {
    kind,
    title: policyTitle(kind, readinessClassification),
    operatorSummary: policySummary(kind, readinessClassification),
    registrySource: detail.registrySource ?? item.registrySource ?? inferRegistrySource(basis),
    policyResolutionBasis: basis,
    policyCode: item.policyCode ?? undefined,
    policyName: item.policyName ?? undefined,
    legalBasisReference: detail.legalBasisReference ?? undefined,
    ordinanceReference: detail.ordinanceReference ?? undefined,
    nationalLawReference: detail.nationalLawReference ?? undefined,
    verificationStatus,
    policyReadinessClassification: readinessClassification,
    requiresManualReview,
    policyReadinessReason: detail.policyReadinessReason ?? item.policyReadinessReason ?? readinessClassification,
    operatorMessage,
    productionAutoApplicationEligible: readinessClassification === "READY_VERIFIED" && !requiresManualReview,
    benefitType: detail.benefitType ?? undefined,
    discountBaseScope: detail.discountBaseScope ?? undefined,
    evidenceRequired: item.evidenceRequired,
    requiredEvidenceType: detail.requiredEvidenceType ?? item.requiredEvidenceType ?? undefined,
    effectiveFrom: detail.effectiveFrom ?? item.effectiveFrom ?? undefined,
    effectiveTo: detail.effectiveTo ?? item.effectiveTo ?? undefined,
    ineligibilityReason: item.blockedReason ?? detail.failureReasonCode ?? undefined
  };
}

function policyKind(
  basis: string,
  verificationStatus?: string | null,
  readinessClassification?: string
): StatutoryDiscountPolicyContext["kind"] {
  if (
    readinessClassification === "SANDBOX_ONLY" ||
    readinessClassification === "CONFIGURED_BUT_UNVERIFIED" ||
    basis === "LOCAL_POLICY_BLOCKED" ||
    verificationStatus?.includes("UNVERIFIED")
  ) {
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

function policyTitle(kind: StatutoryDiscountPolicyContext["kind"], readinessClassification?: string) {
  if (readinessClassification === "READY_VERIFIED") {
    return "Production-ready verified policy";
  }

  if (readinessClassification === "READY_WITH_MANUAL_REVIEW") {
    return "Manual-review policy";
  }

  if (readinessClassification === "SANDBOX_ONLY") {
    return "Sandbox/test policy warning";
  }

  return {
    "national-fallback": "National fallback policy",
    "verified-local": "Verified local policy",
    "blocked-unverified-local": "Unverified local policy blocked",
    "unsupported-entitlement": "Unsupported entitlement",
    "missing-site-jurisdiction": "Missing site jurisdiction"
  }[kind];
}

function policySummary(kind: StatutoryDiscountPolicyContext["kind"], readinessClassification?: string) {
  if (readinessClassification === "READY_VERIFIED") {
    return "The policy is verified for production readiness review. Payment approval and payable-basis application remain separate controlled steps.";
  }

  if (readinessClassification === "READY_WITH_MANUAL_REVIEW") {
    return "The policy requires manual review before any production automatic application decision.";
  }

  if (readinessClassification === "SANDBOX_ONLY") {
    return "Sandbox/test policies are visible for validation only and are not production-ready.";
  }

  return {
    "national-fallback": "Use the stored national statutory policy because no verified local policy overrides it for this draft.",
    "verified-local": "Use the stored verified local policy linked to the site jurisdiction.",
    "blocked-unverified-local": "A local policy is not verified for operator use. Do not approve using that local policy.",
    "unsupported-entitlement": "The entitlement type is not supported by the statutory discount workflow.",
    "missing-site-jurisdiction": "The site does not have a resolved jurisdiction for policy selection."
  }[kind];
}

function inferPolicyReadiness(
  item: {
    policyCode?: string | null;
    policyName?: string | null;
    policyResolutionBasis?: string | null;
    legalBasisReference?: string | null;
    ordinanceReference?: string | null;
    nationalLawReference?: string | null;
  },
  verificationStatus?: string | null
) {
  const markerText = [
    item.policyCode,
    item.policyName,
    item.policyResolutionBasis,
    (item as DetailDto).legalBasisReference,
    (item as DetailDto).ordinanceReference,
    (item as DetailDto).nationalLawReference
  ]
    .filter(Boolean)
    .join(" ")
    .toUpperCase();

  if (/\b(SANDBOX|TEST|DEV|E2E)\b/.test(markerText) || markerText.includes("TEST_") || markerText.includes("SANDBOX_")) {
    return "SANDBOX_ONLY";
  }

  if (!item.policyCode) {
    return "MISSING_REQUIRED_POLICY";
  }

  if (verificationStatus === "ACTIVE_APPROVED" || verificationStatus === "VERIFIED_OFFICIAL") {
    return "READY_VERIFIED";
  }

  if (verificationStatus === "APPROVED_FOR_PILOT") {
    return "READY_WITH_MANUAL_REVIEW";
  }

  if (verificationStatus === "LEAD_UNVERIFIED" || verificationStatus === "VERIFIED_SECONDARY" || verificationStatus === "PROPOSED_ONLY") {
    return "CONFIGURED_BUT_UNVERIFIED";
  }

  if (verificationStatus === "REJECTED") {
    return "NOT_READY";
  }

  return "NOT_READY";
}

function inferRegistrySource(policyResolutionBasis?: string | null) {
  return policyResolutionBasis?.startsWith("DEDICATED_")
    ? "DEDICATED_REGISTRY"
    : "COMPATIBILITY_POLICY_REFERENCES";
}

function policyReadinessMessage(classification: string) {
  const messages: Record<string, string> = {
    READY_VERIFIED: "Policy is verified. Policy readiness is not payment approval.",
    READY_WITH_MANUAL_REVIEW: "Manual review is required before automatic production application.",
    CONFIGURED_BUT_UNVERIFIED: "Policy is configured but is not verified for production auto-application.",
    MISSING_REQUIRED_POLICY: "Required production policy is missing.",
    MISSING_SITE_MAPPING: "Policy scope or site mapping is missing.",
    MISSING_EVIDENCE_RULE: "Evidence rule is missing or inconsistent.",
    EXPIRED_OR_INACTIVE: "Policy is expired or inactive.",
    SANDBOX_ONLY: "Sandbox/test policies are not production-ready.",
    NOT_READY: "Policy is not production-ready."
  };

  return messages[classification] ?? "Policy readiness could not be confirmed.";
}

function payableBasisPreview(item: DetailDto) {
  if (item.payableBasisApplicationStatus) {
    return `${item.payableBasisApplicationStatus} - ${formatMoney(item.payableAmountMinorUnits, item.currencyCode)}`;
  }

  if (item.payableAmountMinorUnits !== undefined && item.payableAmountMinorUnits !== null) {
    return `Preview ${formatMoney(item.payableAmountMinorUnits, item.currencyCode)}`;
  }

  return item.evidenceRequired && !item.evidenceCaptured ? "Evidence metadata pending" : "Not available";
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
    CANCELLED: "Cancelled",
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

function payableBasisMessage(body: PayableBasisApplicationResponseDto) {
  if (!body.accessAllowed) {
    return `Access denied: ${body.accessDenialReasons.join(", ") || body.accessDecision}`;
  }

  if (!body.applicationAccepted) {
    return body.errorCode ?? body.ineligibilityReason ?? "Payable basis was not applied.";
  }

  if (body.alreadyApplied) {
    return "Payable basis has already been applied.";
  }

  return body.applicationPersisted ? "Payable basis applied." : "Payable basis accepted but not persisted.";
}

function mockAccessReadiness(input: AccessReadinessRequest): AccessReadinessResponse {
  const correlationId = input.correlationId ?? newCorrelationId();
  const requestedAction = input.requestedAction;
  return {
    accessEvaluationId: undefined,
    accessAllowed: true,
    accessDecision: "ALLOWED",
    requestedAction,
    readinessStatus: "READY",
    readinessDimensions: [
      { dimension: "OPERATOR", status: "READY", required: true, denialReasonCodes: [] },
      { dimension: "DEVICE", status: "READY", required: true, denialReasonCodes: [] },
      { dimension: "SHIFT", status: "READY", required: true, denialReasonCodes: [] },
      { dimension: "SITE", status: "READY", required: true, denialReasonCodes: [] },
      { dimension: "WORKFLOW", status: "READY", required: true, denialReasonCodes: [] }
    ],
    denialReasons: [],
    operatorReadiness: {
      operatorUserId: input.operatorUserId ?? defaultOperatorContext.userId,
      status: "READY",
      ready: true
    },
    deviceReadiness: {
      operatorDeviceBindingId: input.operatorDeviceBindingId ?? defaultOperatorContext.operatorDeviceBindingId,
      status: "READY",
      ready: true
    },
    shiftReadiness: {
      operatorShiftId: input.operatorShiftId ?? defaultOperatorContext.operatorShiftId,
      status: "READY",
      ready: true
    },
    siteReadiness: {
      siteId: input.siteId,
      siteGroupId: input.siteGroupId,
      status: "READY",
      ready: true
    },
    workflowReadiness: {
      requestedAction,
      workflowState: input.workflowState,
      status: "READY",
      ready: true
    },
    auditPersisted: false,
    evaluatedAt: new Date().toISOString(),
    correlationId,
    retryable: false,
    nextOperatorAction: undefined
  };
}

function mockVendorPaymentAcknowledgments(): VendorPaymentAcknowledgmentDetail[] {
  const now = "2026-06-18T09:15:00+08:00";
  return [
    {
      vendorPaymentAcknowledgmentId: "88000000-0000-0000-0000-000000000001",
      paymentAttemptId: "28000000-0000-0000-0000-000000000001",
      paymentConfirmationId: "38000000-0000-0000-0000-000000000001",
      parkingSessionId: "25000000-0000-0000-0000-000000000001",
      vendorSystemCode: "HIKCENTRAL",
      vendorSessionRef: "HC-SESSION-001",
      ticketNumber: "VENDOR-TICKET-001",
      cardNum: "VENDOR-CARD-001",
      acknowledgmentStatus: "RETRY_PENDING",
      statusBucket: "retry_pending",
      vendorCode: "TEMPORARY_FAILURE",
      vendorMessage: "Vendor acknowledgment retry is pending.",
      requestFeeMinorUnits: 12000,
      requestCurrencyCode: "PHP",
      confirmedFeeMinorUnits: undefined,
      vendorConfirmedAt: undefined,
      attemptCount: 2,
      lastAttemptedAt: "2026-06-18T09:10:00+08:00",
      nextRetryAt: "2026-06-18T09:12:00+08:00",
      correlationId: "88000000-0000-0000-0000-000000000099",
      createdAt: "2026-06-18T09:00:00+08:00",
      updatedAt: now,
      diagnostics: [
        {
          code: "VENDOR_PAYMENT_ACKNOWLEDGMENT_RETRY_DUE",
          message: "Retry-pending acknowledgment is due for dispatcher pickup.",
          source: "central-pms.vendor-payment-acknowledgments",
          retryable: true,
          correlationId: "88000000-0000-0000-0000-000000000099"
        }
      ]
    },
    {
      vendorPaymentAcknowledgmentId: "88000000-0000-0000-0000-000000000002",
      paymentAttemptId: "28000000-0000-0000-0000-000000000002",
      paymentConfirmationId: "38000000-0000-0000-0000-000000000002",
      parkingSessionId: "25000000-0000-0000-0000-000000000002",
      vendorSystemCode: "HIKCENTRAL",
      vendorSessionRef: "HC-SESSION-002",
      ticketNumber: "VENDOR-TICKET-002",
      cardNum: "VENDOR-CARD-002",
      acknowledgmentStatus: "CONFIRMED",
      statusBucket: "confirmed",
      vendorCode: "0",
      vendorMessage: "Accepted",
      requestFeeMinorUnits: 9000,
      requestCurrencyCode: "PHP",
      confirmedFeeMinorUnits: 9000,
      vendorConfirmedAt: "2026-06-18T08:50:00+08:00",
      attemptCount: 1,
      lastAttemptedAt: "2026-06-18T08:50:00+08:00",
      nextRetryAt: undefined,
      correlationId: "88000000-0000-0000-0000-000000000098",
      createdAt: "2026-06-18T08:45:00+08:00",
      updatedAt: "2026-06-18T08:50:00+08:00",
      diagnostics: [
        {
          code: "VENDOR_PAYMENT_ACKNOWLEDGMENT_STATUS_BUCKET",
          message: "Status bucket: confirmed.",
          source: "central-pms.vendor-payment-acknowledgments",
          retryable: false,
          correlationId: "88000000-0000-0000-0000-000000000098"
        }
      ]
    }
  ];
}

function mockVendorSessionProjectionHealthConfig(): VendorSessionProjectionHealthConfig {
  return {
    schedulerEnabled: true,
    degradedResolveFallbackEnabled: true,
    maxProjectionAgeMinutes: 1440,
    maxParallelSiteJobs: 4,
    schedulerScanIntervalSeconds: 60
  };
}

function mockVendorSessionProjectionHealthTargets(): VendorSessionProjectionHealthTarget[] {
  return [
    {
      projectionSyncTargetId: "abe7da56-1198-4d51-901f-87e8fb7cd40d",
      siteId: "c9000000-0000-0000-0000-000000000001",
      siteGroupId: "ce000000-0000-0000-0000-000000000001",
      vendorSystemId: "31bde78a-5dfc-45c3-a1f3-e48abaf90927",
      parkingLotIndexCode: "1",
      parkingLotName: "TEST SITE",
      enabledFlag: true,
      healthStatus: "HEALTHY",
      lastAttemptAt: "2026-06-22T02:05:00Z",
      lastSuccessAt: "2026-06-22T02:05:00Z",
      lastFailureAt: null,
      failureCount: 0,
      lastErrorCode: null,
      lastErrorMessage: null,
      pollIntervalSeconds: 60,
      lookbackWindowMinutes: 10080,
      pageSize: 50,
      latestProjectionLastRefreshedAt: "2026-06-22T02:05:00Z",
      freshnessAgeSeconds: 120,
      isStale: false,
      totalProjectionCount: 19,
      activeProjectionCount: 12,
      exitedProjectionCount: 7,
      cardNumProjectionCount: 15,
      plateLicenseProjectionCount: 2
    },
    {
      projectionSyncTargetId: "abe7da56-1198-4d51-901f-87e8fb7cd41d",
      siteId: "c9000000-0000-0000-0000-000000000002",
      siteGroupId: "ce000000-0000-0000-0000-000000000001",
      vendorSystemId: "31bde78a-5dfc-45c3-a1f3-e48abaf90927",
      parkingLotIndexCode: "2",
      parkingLotName: "STALE SITE",
      enabledFlag: true,
      healthStatus: "FAILING",
      lastAttemptAt: "2026-06-22T01:30:00Z",
      lastSuccessAt: "2026-06-20T01:30:00Z",
      lastFailureAt: "2026-06-22T01:30:00Z",
      failureCount: 3,
      lastErrorCode: "HIKCENTRAL_UNAVAILABLE",
      lastErrorMessage: "HikCentral connection timed out.",
      pollIntervalSeconds: 60,
      lookbackWindowMinutes: 1440,
      pageSize: 50,
      latestProjectionLastRefreshedAt: "2026-06-20T01:30:00Z",
      freshnessAgeSeconds: 172800,
      isStale: true,
      totalProjectionCount: 5,
      activeProjectionCount: 4,
      exitedProjectionCount: 1,
      cardNumProjectionCount: 3,
      plateLicenseProjectionCount: 1
    },
    {
      projectionSyncTargetId: "abe7da56-1198-4d51-901f-87e8fb7cd42d",
      siteId: "c9000000-0000-0000-0000-000000000003",
      siteGroupId: "ce000000-0000-0000-0000-000000000001",
      vendorSystemId: "31bde78a-5dfc-45c3-a1f3-e48abaf90927",
      parkingLotIndexCode: "3",
      parkingLotName: "DISABLED SITE",
      enabledFlag: false,
      healthStatus: "DISABLED",
      lastAttemptAt: null,
      lastSuccessAt: null,
      lastFailureAt: null,
      failureCount: 0,
      lastErrorCode: null,
      lastErrorMessage: null,
      pollIntervalSeconds: 300,
      lookbackWindowMinutes: 1440,
      pageSize: 25,
      latestProjectionLastRefreshedAt: null,
      freshnessAgeSeconds: null,
      isStale: false,
      totalProjectionCount: 0,
      activeProjectionCount: 0,
      exitedProjectionCount: 0,
      cardNumProjectionCount: 0,
      plateLicenseProjectionCount: 0
    }
  ];
}

function mockVendorSessionProjectionLatestRecords(
  projectionSyncTargetId: string
): VendorSessionProjectionHealthLatestRecord[] {
  if (projectionSyncTargetId !== "abe7da56-1198-4d51-901f-87e8fb7cd40d") {
    return [];
  }

  return [
    {
      vendorSessionProjectionId: "b1000000-0000-0000-0000-000000000001",
      vendorRecordGuid: "5BF30C478FE44C0D8432E549AF9FE0F7",
      cardNum: "3519278781100",
      plateLicense: null,
      enterTime: "2026-06-16T09:30:04Z",
      exitTime: null,
      projectionStatus: "ACTIVE",
      lastRefreshedAt: "2026-06-22T02:05:00Z",
      sourceEventAt: "2026-06-16T09:30:04Z",
      correlationId: "b2000000-0000-0000-0000-000000000001"
    }
  ];
}

function buildVendorSessionProjectionHealthSummary(
  targets: VendorSessionProjectionHealthTarget[],
  config: VendorSessionProjectionHealthConfig
): VendorSessionProjectionHealthSummary {
  return {
    totalTargets: targets.length,
    enabledTargets: targets.filter((target) => target.enabledFlag).length,
    disabledTargets: targets.filter((target) => !target.enabledFlag).length,
    healthyTargets: targets.filter((target) => target.healthStatus === "HEALTHY").length,
    degradedTargets: targets.filter((target) => target.healthStatus === "DEGRADED").length,
    failingTargets: targets.filter((target) => target.healthStatus === "FAILING").length,
    unknownTargets: targets.filter((target) => target.healthStatus === "UNKNOWN").length,
    staleTargets: targets.filter((target) => target.isStale).length,
    targetsWithLastFailure: targets.filter((target) => Boolean(target.lastFailureAt)).length,
    latestSuccessfulProjectionSyncAt: targets
      .map((target) => target.lastSuccessAt)
      .filter((value): value is string => Boolean(value))
      .sort()
      .at(-1) ?? null,
    totalActiveProjections: targets.reduce((sum, target) => sum + target.activeProjectionCount, 0),
    totalExitedProjections: targets.reduce((sum, target) => sum + target.exitedProjectionCount, 0),
    config: { ...config }
  };
}

function countVendorPaymentAcknowledgmentBuckets(
  items: VendorPaymentAcknowledgmentSummary[]
): VendorPaymentAcknowledgmentStatusBuckets {
  return items.reduce<VendorPaymentAcknowledgmentStatusBuckets>(
    (counts, item) => {
      switch (item.acknowledgmentStatus) {
        case "PENDING":
          counts.pending += 1;
          break;
        case "RETRY_PENDING":
          counts.retryPending += 1;
          break;
        case "FAILED":
          counts.failed += 1;
          break;
        case "CONFIRMED":
          counts.confirmed += 1;
          break;
        case "SKIPPED_DISABLED":
          counts.skippedDisabled += 1;
          break;
        case "CANCELLED":
          counts.cancelled += 1;
          break;
      }

      return counts;
    },
    { pending: 0, retryPending: 0, failed: 0, confirmed: 0, skippedDisabled: 0, cancelled: 0 }
  );
}

function mockProductionPolicyReview(): ProductionPolicyImportReviewResult {
  const now = new Date().toISOString();
  return {
    imported: false,
    productionPolicyActivationBlocked: true,
    message: "Review submission created. No policies were imported.",
    submission: {
      reviewId: "99000000-0000-0000-0000-000000000001",
      makerOperatorId: defaultOperatorContext.userId,
      fileName: "candidate.csv",
      status: "LEGAL_REVIEW_PENDING",
      dryRunSummary: {
        totalRows: 1,
        passCount: 1,
        warnCount: 0,
        failCount: 0,
        importableCount: 1,
        manualReviewCount: 0,
        notImportableCount: 0,
        dryRunOnlyCount: 0,
        duplicateCount: 0
      },
      reviewerDecisions: [],
      history: [
        {
          action: "SUBMIT_FOR_REVIEW",
          status: "LEGAL_REVIEW_PENDING",
          actorOperatorId: defaultOperatorContext.userId,
          occurredAt: now,
          correlationId: "99000000-0000-0000-0000-000000000099"
        }
      ],
      createdAt: now,
      updatedAt: now
    },
    findings: [],
    correlationId: "99000000-0000-0000-0000-000000000099"
  };
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
    evidenceRequiredSatisfied: draft.evidenceRequiredSatisfied,
    evidenceCount: draft.evidenceCount,
    latestEvidenceStatus: draft.latestEvidenceStatus,
    originalAmountMinorUnits: draft.originalAmountMinorUnits,
    payableAmountMinorUnits: draft.payableAmountMinorUnits,
    currencyCode: draft.currencyCode
  };
}

function toAuditReport(dto: AuditReportResponseDto): AuditReportResponse {
  return {
    items: dto.items.map((item) => {
      const verificationStatus = item.verificationStatus ?? undefined;
      const policyReadinessClassification =
        item.policyReadinessClassification ?? inferPolicyReadiness(item, verificationStatus);
      return {
        statutoryDiscountValidationId: item.statutoryDiscountValidationId,
        draftId: item.draftId,
        parkingSessionId: item.parkingSessionId,
        ticketReference: item.ticketReference ?? undefined,
        plateNumber: item.plateNumber ?? undefined,
        siteId: item.siteId,
        siteGroupId: item.siteGroupId,
        entitlementType: item.entitlementType,
        validationStatus: item.validationStatus,
        evidenceRequired: item.evidenceRequired,
        evidenceCaptured: item.evidenceCaptured,
        evidenceRequiredSatisfied: item.evidenceRequiredSatisfied,
        evidenceCount: item.evidenceCount,
        latestEvidenceStatus: item.latestEvidenceStatus ?? undefined,
        payableBasisApplicationStatus: item.payableBasisApplicationStatus ?? undefined,
        originalAmountMinorUnits: item.originalAmountMinorUnits ?? undefined,
        statutoryDiscountAmountMinorUnits: item.statutoryDiscountAmountMinorUnits ?? undefined,
        finalPayableAmountMinorUnits: item.finalPayableAmountMinorUnits ?? undefined,
        currencyCode: item.currencyCode ?? undefined,
        requestedByUserId: item.requestedByUserId ?? undefined,
        validatedByUserId: item.validatedByUserId ?? undefined,
        requestedAt: item.requestedAt,
        validatedAt: item.validatedAt ?? undefined,
        correlationId: item.correlationId ?? undefined,
        registrySource: item.registrySource ?? inferRegistrySource(undefined),
        policyCode: item.policyCode ?? undefined,
        verificationStatus,
        policyReadinessClassification,
        requiresManualReview: item.requiresManualReview ?? policyReadinessClassification !== "READY_VERIFIED",
        policyReadinessReason: item.policyReadinessReason ?? policyReadinessClassification,
        operatorMessage: item.operatorMessage ?? policyReadinessMessage(policyReadinessClassification),
        ordinanceReference: item.ordinanceReference ?? undefined,
        legalBasisReference: item.legalBasisReference ?? undefined,
        appliedTariffSnapshotId: item.appliedTariffSnapshotId ?? undefined,
        accessEvaluationSummary: item.accessEvaluationSummary ?? undefined
      };
    }),
    totalCount: dto.totalCount,
    limit: dto.limit,
    offset: dto.offset,
    correlationId: dto.correlationId
  };
}

function toFiscalStatusViewAuditReport(dto: FiscalStatusViewAuditReportResponseDto): FiscalStatusViewAuditReportResponse {
  return {
    items: dto.items.map((item) => ({
      actionLogEntryId: item.actionLogEntryId,
      actionTimestamp: item.actionTimestamp,
      actionCode: item.actionCode,
      resultClass: item.resultClass,
      operatorUserId: item.operatorUserId,
      operatorDisplayName: item.operatorDisplayName ?? undefined,
      operatorUsername: item.operatorUsername ?? undefined,
      siteId: item.siteId ?? undefined,
      siteName: item.siteName ?? undefined,
      siteGroupId: item.siteGroupId ?? undefined,
      siteGroupName: item.siteGroupName ?? undefined,
      fiscalIssuanceReferenceId: item.fiscalIssuanceReferenceId,
      fiscalDocumentNumber: item.fiscalDocumentNumber ?? undefined,
      ticketNumber: item.ticketNumber ?? undefined,
      correlationId: item.correlationId,
      safeDenialOrErrorPosture: item.safeDenialOrErrorPosture ?? undefined,
      sourceModule: item.sourceModule ?? undefined
    })),
    totalCount: dto.totalCount,
    limit: dto.limit,
    offset: dto.offset,
    correlationId: dto.correlationId
  };
}

function toFiscalVoidActionAuditReport(dto: FiscalVoidActionAuditReportResponseDto): FiscalVoidActionAuditReportResponse {
  return {
    items: dto.items.map((item) => ({
      actionLogEntryId: item.actionLogEntryId,
      actionTimestamp: item.actionTimestamp,
      actionCode: item.actionCode,
      resultClass: item.resultClass,
      operatorUserId: item.operatorUserId,
      operatorDisplayName: item.operatorDisplayName ?? undefined,
      operatorUsername: item.operatorUsername ?? undefined,
      siteId: item.siteId ?? undefined,
      siteName: item.siteName ?? undefined,
      siteGroupId: item.siteGroupId ?? undefined,
      siteGroupName: item.siteGroupName ?? undefined,
      fiscalIssuanceReferenceId: item.fiscalIssuanceReferenceId,
      fiscalDocumentNumber: item.fiscalDocumentNumber ?? undefined,
      ticketNumber: item.ticketNumber ?? undefined,
      posServerFiscalDocumentId: item.posServerFiscalDocumentId ?? undefined,
      reasonCode: item.reasonCode ?? undefined,
      reasonText: item.reasonText ?? undefined,
      correlationId: item.correlationId,
      operatorActionRequestId: item.operatorActionRequestId ?? undefined,
      posServerResultClassification: item.posServerResultClassification ?? undefined,
      safeDenialOrErrorPosture: item.safeDenialOrErrorPosture ?? undefined,
      sourceModule: item.sourceModule ?? undefined,
      paymentFinalityChanged: item.paymentFinalityChanged ?? undefined,
      exitAuthorizationIssued: item.exitAuthorizationIssued ?? undefined,
      gateBehaviorTriggered: item.gateBehaviorTriggered ?? undefined,
      refundOrReversalCreated: item.refundOrReversalCreated ?? undefined,
      hikCentralCalled: item.hikCentralCalled ?? undefined,
      paymentProviderCalled: item.paymentProviderCalled ?? undefined,
      renderingGenerated: item.renderingGenerated ?? undefined,
      replacementFiscalDocumentCreated: item.replacementFiscalDocumentCreated ?? undefined,
      newFiscalNumberAllocated: item.newFiscalNumberAllocated ?? undefined,
      fiscalSequenceChangedByCentralPms: item.fiscalSequenceChangedByCentralPms ?? undefined
    })),
    totalCount: dto.totalCount,
    limit: dto.limit,
    offset: dto.offset,
    correlationId: dto.correlationId
  };
}

function toAuditReportItem(draft: StatutoryDiscountDraftDetail) {
  return {
    statutoryDiscountValidationId: draft.draftId,
    draftId: draft.draftId,
    parkingSessionId: draft.parkingSessionId,
    ticketReference: draft.ticketReference,
    plateNumber: draft.plateNumber,
    siteId: draft.siteId,
    siteGroupId: draft.siteGroupId ?? "Not available",
    entitlementType: draft.entitlementType,
    validationStatus: draft.status,
    evidenceRequired: draft.policyContext.evidenceRequired,
    evidenceCaptured: draft.evidenceCaptured,
    evidenceRequiredSatisfied: draft.evidenceRequiredSatisfied,
    evidenceCount: draft.evidenceCount,
    latestEvidenceStatus: draft.latestEvidenceStatus,
    payableBasisApplicationStatus: draft.payableBasisApplicationStatus,
    originalAmountMinorUnits: draft.originalAmountMinorUnits,
    statutoryDiscountAmountMinorUnits: draft.statutoryDiscountAmountMinorUnits,
    finalPayableAmountMinorUnits: draft.finalPayableAmountMinorUnits ?? draft.payableAmountMinorUnits,
    currencyCode: draft.currencyCode,
    requestedByUserId: draft.requestedBy,
    validatedByUserId: undefined,
    requestedAt: draft.requestedAt,
    validatedAt: undefined,
    correlationId: undefined,
    registrySource: draft.policyContext.registrySource,
    policyCode: draft.policyContext.policyCode,
    verificationStatus: draft.policyContext.verificationStatus,
    policyReadinessClassification: draft.policyContext.policyReadinessClassification,
    requiresManualReview: draft.policyContext.requiresManualReview,
    policyReadinessReason: draft.policyContext.policyReadinessReason,
    operatorMessage: draft.policyContext.operatorMessage,
    ordinanceReference: draft.policyContext.ordinanceReference,
    legalBasisReference: draft.policyContext.legalBasisReference,
    appliedTariffSnapshotId: draft.appliedTariffSnapshotId,
    accessEvaluationSummary: "SESSION_LOOKUP / SUCCESS"
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
  registrySource: "COMPATIBILITY_POLICY_REFERENCES",
  policyResolutionBasis: "NATIONAL_LAW_FALLBACK",
  policyCode: "PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK",
  policyName: "Senior Citizen Parking Benefit",
  legalBasisReference: "Republic Act No. 9994",
  nationalLawReference: "RA 9994",
  verificationStatus: "VERIFIED_OFFICIAL",
  policyReadinessClassification: "READY_VERIFIED",
  requiresManualReview: false,
  policyReadinessReason: "READY_VERIFIED",
  operatorMessage: "Policy is verified. Policy readiness is not payment approval.",
  productionAutoApplicationEligible: true,
  benefitType: "STATUTORY_DISCOUNT_VAT_EXEMPT",
  discountBaseScope: "VAT_EXCLUSIVE",
  evidenceRequired: false
};

const pwdLocalPolicy = {
  kind: "verified-local" as const,
  title: "Verified local policy",
  operatorSummary:
    "Apply the verified city policy for PWD parking benefits. Local policy is active and linked to this site jurisdiction.",
  registrySource: "DEDICATED_REGISTRY",
  policyResolutionBasis: "LOCAL_ORDINANCE_APPLIED",
  policyCode: "QC-PWD-PARKING-2026",
  policyName: "Quezon City PWD Parking Benefit",
  legalBasisReference: "RA 10754 and verified local ordinance",
  ordinanceReference: "QC Ordinance 2026-04",
  nationalLawReference: "RA 10754",
  verificationStatus: "APPROVED_FOR_PILOT",
  policyReadinessClassification: "READY_WITH_MANUAL_REVIEW",
  requiresManualReview: true,
  policyReadinessReason: "READY_WITH_MANUAL_REVIEW",
  operatorMessage: "Manual review is required before automatic production application.",
  productionAutoApplicationEligible: false,
  benefitType: "FREE_DURATION",
  discountBaseScope: "VAT_EXCLUSIVE",
  evidenceRequired: true,
  requiredEvidenceType: "PWD_ID"
};

const blockedLocalPolicy = {
  kind: "blocked-unverified-local" as const,
  title: "Unverified local policy blocked",
  operatorSummary:
    "A local ordinance candidate exists, but it is not verified. The draft must not be approved using this local policy.",
  registrySource: "DEDICATED_REGISTRY",
  policyResolutionBasis: "LOCAL_POLICY_BLOCKED",
  policyCode: "LOCAL-UNVERIFIED",
  policyName: "Unverified Local Parking Benefit",
  legalBasisReference: "Local policy verification required",
  ordinanceReference: "Pending verification",
  verificationStatus: "LEAD_UNVERIFIED",
  policyReadinessClassification: "CONFIGURED_BUT_UNVERIFIED",
  requiresManualReview: true,
  policyReadinessReason: "CONFIGURED_BUT_UNVERIFIED",
  operatorMessage: "Policy is configured but is not verified for production auto-application.",
  productionAutoApplicationEligible: false,
  benefitType: "FREE_DURATION",
  discountBaseScope: "VAT_EXCLUSIVE",
  evidenceRequired: true,
  requiredEvidenceType: "SENIOR_CITIZEN_ID",
  ineligibilityReason: "Local policy is not verified for operator use."
};

function mockFiscalIssuanceStatuses(): FiscalIssuanceStatus[] {
  return [
    {
      fiscalIssuanceReferenceId: "5f000000-0000-0000-0000-000000000001",
      fiscalIssuanceState: "FISCAL_ISSUANCE_RECORDED",
      resultClassification: "NEWLY_CREATED",
      fiscalIssuanceEvidenceStatus: "FISCAL_DOCUMENT_NUMBER_ASSIGNED",
      fiscalNumberAssignmentState: "ASSIGNED",
      upstreamFinalityReference: "CPS-POS-UAT:CPS-POS-UAT-20260703-DEV-ATC-001:newly_created:001",
      paymentConfirmationId: "5f000000-0000-0000-0000-000000000009",
      paymentAttemptId: "5f000000-0000-0000-0000-000000000010",
      parkingSessionId: "5f000000-0000-0000-0000-000000000011",
      siteId: "5f000000-0000-0000-0000-000000000005",
      sitePosServerId: "5f000000-0000-0000-0000-000000000012",
      sitePosServerRef: "DEV-POS-SERVER-ATC-001",
      fiscalDocumentTypeCodeId: "5f000000-0000-0000-0000-000000000013",
      fiscalDocumentTypeCodeKey: "sales_invoice",
      posServerFiscalDocumentId: "5f000000-0000-0000-0000-000000000014",
      fiscalDocumentNumber: "SI-00000001-UAT",
      fiscalIdentityId: "5f000000-0000-0000-0000-000000000015",
      fiscalSequencePolicyId: "5f000000-0000-0000-0000-000000000016",
      fiscalSequenceValue: 1,
      fiscalSeries: "UAT-SI",
      fiscalNumberPrefixText: "SI-",
      fiscalNumberSuffixText: "-UAT",
      fiscalNumberAssignedAt: "2026-07-08T08:00:00Z",
      fiscalNumberAssignedByRef: "pos-server",
      semanticRequestHashValue: "hash-value",
      semanticRequestHashVersion: "sha256:v1",
      semanticRequestHashStatus: "AVAILABLE",
      semanticRequestHashAlgorithm: "SHA-256",
      semanticRequestHashSourceFactCount: 24,
      firstRecordedAt: "2026-07-08T08:00:00Z",
      lastUpdatedAt: "2026-07-08T08:00:00Z",
      correlationId: "5f000000-0000-0000-0000-000000000008"
    }
  ];
}

function mockFiscalVoidResult(fiscalIssuanceReferenceId: string): FiscalIssuanceVoidResult {
  return {
    accessAllowed: true,
    accessDecision: "ALLOWED",
    accessDenialReasons: [],
    accessPersisted: true,
    accepted: true,
    status: "pos_server_void_recorded",
    httpStatusCode: 200,
    errors: [],
    fiscalIssuanceReferenceId,
    posServerFiscalDocumentId: "5f000000-0000-0000-0000-000000000014",
    fiscalDocumentNumber: "SI-00000001-UAT",
    fiscalSequenceValue: 1,
    fiscalDocumentStatusPosture: "voided",
    voidStatus: "recorded",
    voidReasonCode: "operator_error",
    voidedAt: "2026-07-10T00:00:00Z",
    posServerResultClassification: "newly_voided",
    correlationId: newCorrelationId(),
    errorPosture: undefined,
    newFiscalNumberAllocated: false,
    paymentFinalityChanged: false,
    exitAuthorizationIssued: false,
    gateBehaviorTriggered: false,
    refundOrReversalCreated: false,
    hikCentralCalled: false,
    paymentProviderCalled: false,
    renderingGenerated: false,
    replacementFiscalDocumentCreated: false,
    fiscalSequenceChangedByCentralPms: false,
    idempotentReplay: false
  };
}

function mockFiscalVoidActionAuditReport(): FiscalVoidActionAuditReportResponse {
  return {
    items: [
      {
        actionLogEntryId: "6c000000-0000-0000-0000-000000000001",
        actionTimestamp: "2026-07-10T06:00:00Z",
        actionCode: "VOID_FISCAL_DOCUMENT",
        resultClass: "SUCCEEDED",
        operatorUserId: defaultOperatorContext.userId,
        siteId: defaultOperatorContext.siteId,
        siteGroupId: defaultOperatorContext.siteGroupId,
        fiscalIssuanceReferenceId: "7f4a7d36-2e6e-4f2c-aad6-2d98e8e1b501",
        fiscalDocumentNumber: "SI-OCVOID-0001-UAT",
        posServerFiscalDocumentId: "3cddbc8e-28f8-49d2-93cf-b4a28a947501",
        reasonCode: "operator_error",
        reasonText: undefined,
        correlationId: "b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df",
        operatorActionRequestId: undefined,
        posServerResultClassification: undefined,
        sourceModule: "operator-console-fiscal-issuance-status",
        paymentFinalityChanged: false,
        exitAuthorizationIssued: false,
        gateBehaviorTriggered: false,
        refundOrReversalCreated: false,
        hikCentralCalled: false,
        paymentProviderCalled: false,
        renderingGenerated: false,
        replacementFiscalDocumentCreated: false,
        newFiscalNumberAllocated: false,
        fiscalSequenceChangedByCentralPms: false
      }
    ],
    totalCount: 1,
    limit: 25,
    offset: 0,
    correlationId: "6c000000-0000-0000-0000-000000000099"
  };
}

function mockFiscalStatusViewAuditReport(): FiscalStatusViewAuditReportResponse {
  return {
    items: [
      {
        actionLogEntryId: "6b000000-0000-0000-0000-000000000001",
        actionTimestamp: "2026-07-08T01:30:00Z",
        actionCode: "VIEW_FISCAL_ISSUANCE_STATUS",
        resultClass: "SUCCEEDED",
        operatorUserId: defaultOperatorContext.userId,
        siteId: "77000000-0000-0000-0000-000000000002",
        siteGroupId: "77000000-0000-0000-0000-000000000001",
        fiscalIssuanceReferenceId: "5f000000-0000-0000-0000-000000000001",
        correlationId: "6b000000-0000-0000-0000-000000000099",
        sourceModule: "operator-console-fiscal-issuance-status"
      },
      {
        actionLogEntryId: "6b000000-0000-0000-0000-000000000002",
        actionTimestamp: "2026-07-08T01:20:00Z",
        actionCode: "VIEW_FISCAL_ISSUANCE_STATUS",
        resultClass: "DENIED",
        operatorUserId: defaultOperatorContext.userId,
        siteId: "77000000-0000-0000-0000-000000000002",
        siteGroupId: "77000000-0000-0000-0000-000000000001",
        fiscalIssuanceReferenceId: "5f000000-0000-0000-0000-000000000002",
        correlationId: "6b000000-0000-0000-0000-000000000098",
        safeDenialOrErrorPosture: "Operator Console fiscal issuance status access was denied.",
        sourceModule: "operator-console-fiscal-issuance-status"
      },
      {
        actionLogEntryId: "6b000000-0000-0000-0000-000000000003",
        actionTimestamp: "2026-07-08T01:10:00Z",
        actionCode: "VIEW_FISCAL_ISSUANCE_STATUS",
        resultClass: "NOT_FOUND",
        operatorUserId: defaultOperatorContext.userId,
        siteId: "77000000-0000-0000-0000-000000000002",
        siteGroupId: "77000000-0000-0000-0000-000000000001",
        fiscalIssuanceReferenceId: "5f000000-0000-0000-0000-000000000003",
        correlationId: "6b000000-0000-0000-0000-000000000097",
        safeDenialOrErrorPosture: "Fiscal issuance reference was not found.",
        sourceModule: "operator-console-fiscal-issuance-status"
      },
      {
        actionLogEntryId: "6b000000-0000-0000-0000-000000000004",
        actionTimestamp: "2026-07-08T01:00:00Z",
        actionCode: "VIEW_FISCAL_ISSUANCE_STATUS",
        resultClass: "FAILED_SAFELY",
        operatorUserId: defaultOperatorContext.userId,
        siteId: "77000000-0000-0000-0000-000000000002",
        siteGroupId: "77000000-0000-0000-0000-000000000001",
        fiscalIssuanceReferenceId: "5f000000-0000-0000-0000-000000000004",
        correlationId: "6b000000-0000-0000-0000-000000000096",
        safeDenialOrErrorPosture: "Fiscal status view failed safely.",
        sourceModule: "operator-console-fiscal-issuance-status"
      }
    ],
    totalCount: 4,
    limit: 25,
    offset: 0,
    correlationId: "6b000000-0000-0000-0000-000000000100"
  };
}

const mockTicketLookupResults: OperatorTicketLookupResult[] = [
  {
    sessionFound: true,
    ticketNumber: "STAT-OP-SESSION-0001",
    cardNum: "STAT-OP-SESSION-0001",
    plateLicense: "Unknown",
    parkingInTime: "2026-06-01T06:55:00+08:00",
    parkingDurationSeconds: 4800,
    feeMinorUnits: 18000,
    currencyCode: "PHP",
    feeRuleType: "STANDARD",
    feeRuleIndexCode: "STD-001",
    feeRuleName: "Standard Parking Fee",
    paymentAttemptStatus: "CONFIRMED",
    paymentStatus: "CONFIRMED",
    paymentConfirmationStatus: "RECORDED",
    vendorSystemCode: "HIKCENTRAL",
    vendorConfirmationCode: "CONFIRMED",
    vendorConfirmationStatus: "CONFIRMED",
    vendorConfirmationTimestamp: "2026-06-01T08:20:00+08:00",
    vendorMessage: "Payment confirmation accepted by vendor.",
    diagnostics: ["summary-only"],
    correlationId: "mock-ticket-lookup-confirmed"
  },
  {
    sessionFound: true,
    ticketNumber: "STAT-OP-SESSION-0002",
    cardNum: "STAT-OP-SESSION-0002",
    plateLicense: "PWD 2048",
    parkingInTime: "2026-06-01T07:10:00+08:00",
    parkingDurationSeconds: 5520,
    feeMinorUnits: 22000,
    currencyCode: "PHP",
    feeRuleType: "STANDARD",
    feeRuleIndexCode: "STD-002",
    feeRuleName: "Standard Parking Fee",
    paymentAttemptStatus: "REQUESTED",
    paymentStatus: "UNPAID",
    paymentConfirmationStatus: "NONE",
    vendorSystemCode: "HIKCENTRAL",
    vendorConfirmationCode: undefined,
    vendorConfirmationStatus: null,
    vendorMessage: "No vendor confirmation is available.",
    correlationId: "mock-ticket-lookup-unpaid"
  },
  {
    sessionFound: true,
    ticketNumber: "STAT-OP-SESSION-PENDING",
    cardNum: "STAT-OP-SESSION-PENDING",
    plateLicense: "ABC 5678",
    parkingInTime: "2026-06-01T08:00:00+08:00",
    parkingDurationSeconds: 2700,
    feeMinorUnits: 9000,
    currencyCode: "PHP",
    feeRuleType: "STANDARD",
    feeRuleIndexCode: "STD-003",
    feeRuleName: "Standard Parking Fee",
    paymentAttemptStatus: "CONFIRMED",
    paymentStatus: "CONFIRMED",
    paymentConfirmationStatus: "RECORDED",
    vendorSystemCode: "HIKCENTRAL",
    vendorConfirmationCode: "PENDING",
    vendorConfirmationStatus: "PENDING",
    vendorMessage: "Vendor confirmation is still pending.",
    correlationId: "mock-ticket-lookup-vendor-pending"
  },
  {
    sessionFound: true,
    ticketNumber: "STAT-OP-SESSION-VENDOR-FAILED",
    cardNum: "STAT-OP-SESSION-VENDOR-FAILED",
    plateLicense: "VND 5000",
    parkingInTime: "2026-06-01T08:10:00+08:00",
    parkingDurationSeconds: 2100,
    feeMinorUnits: 12000,
    currencyCode: "PHP",
    feeRuleType: "STANDARD",
    feeRuleIndexCode: "STD-004",
    feeRuleName: "Standard Parking Fee",
    paymentAttemptStatus: "CONFIRMED",
    paymentStatus: "CONFIRMED",
    paymentConfirmationStatus: "RECORDED",
    vendorSystemCode: "HIKCENTRAL",
    vendorConfirmationCode: "FAILED",
    vendorConfirmationStatus: "FAILED",
    vendorMessage: "Vendor confirmation failed.",
    correlationId: "mock-ticket-lookup-vendor-failed"
  }
];

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
    evidenceRequiredSatisfied: false,
    evidenceCount: 0,
    latestEvidenceStatus: undefined,
    requiredEvidenceTypes: [],
    originalTariffSnapshotId: "23100000-0000-0000-0000-000000000004",
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
    payableBasisPreview: "Evidence metadata pending",
    currentPaymentStatus: "Read-only in this module",
    maskedIdReference: "Evidence metadata only",
    issuingAuthority: "PDAO",
    evidenceCaptured: false,
    evidenceRequiredSatisfied: false,
    evidenceCount: 0,
    latestEvidenceStatus: undefined,
    requiredEvidenceTypes: ["PWD_ID"],
    originalTariffSnapshotId: "23100000-0000-0000-0000-000000000014",
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
    evidenceRequiredSatisfied: false,
    evidenceCount: 0,
    latestEvidenceStatus: undefined,
    requiredEvidenceTypes: ["SENIOR_CITIZEN_ID"],
    originalTariffSnapshotId: "23100000-0000-0000-0000-000000000024",
    originalAmountMinorUnits: 9000,
    currencyCode: "PHP",
    policyContext: blockedLocalPolicy,
    auditActivity: ["Unverified local policy detected.", "Decision blocked pending policy verification."]
  }
];
