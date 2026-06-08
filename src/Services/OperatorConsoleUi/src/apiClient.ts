import type {
  AccessReadinessRequest,
  AccessReadinessResponse,
  AuditReportQuery,
  AuditReportResponse,
  DraftStatus,
  EntitlementType,
  OperatorConsoleApiError,
  StatutoryDiscountDecisionInput,
  StatutoryDiscountDecisionResult,
  StatutoryDiscountDraftDetail,
  StatutoryDiscountEvidenceCaptureInput,
  StatutoryDiscountEvidenceCaptureResult,
  StatutoryDiscountEvidenceItem,
  StatutoryDiscountEvidenceList,
  StatutoryDiscountPayableBasisApplicationInput,
  StatutoryDiscountPayableBasisApplicationResult,
  StatutoryDiscountPolicyContext,
  StatutoryDiscountQueueItem
} from "./types";

export interface OperatorConsoleApiClient {
  evaluateAccessReadiness(input: AccessReadinessRequest): Promise<AccessReadinessResponse>;
  listAuditReport(input?: AuditReportQuery): Promise<AuditReportResponse>;
  listStatutoryDiscountDrafts(): Promise<StatutoryDiscountQueueItem[]>;
  getStatutoryDiscountDraft(draftId: string): Promise<StatutoryDiscountDraftDetail>;
  listStatutoryDiscountEvidence(draftId: string): Promise<StatutoryDiscountEvidenceList>;
  captureStatutoryDiscountEvidence(input: StatutoryDiscountEvidenceCaptureInput): Promise<StatutoryDiscountEvidenceCaptureResult>;
  submitStatutoryDiscountDecision(input: StatutoryDiscountDecisionInput): Promise<StatutoryDiscountDecisionResult>;
  applyStatutoryDiscountPayableBasis(
    input: StatutoryDiscountPayableBasisApplicationInput
  ): Promise<StatutoryDiscountPayableBasisApplicationResult>;
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

export interface OperatorConsoleOperatorContext {
  userId: string;
  operatorDeviceBindingId: string;
  operatorShiftId: string;
}

const defaultOperatorContext: OperatorConsoleOperatorContext = {
  userId: localFallback(import.meta.env.VITE_OPERATOR_CONSOLE_USER_ID, "77000000-0000-0000-0000-000000000010"),
  operatorDeviceBindingId: localFallback(
    import.meta.env.VITE_OPERATOR_CONSOLE_DEVICE_BINDING_ID,
    "77000000-0000-0000-0000-000000000030"
  ),
  operatorShiftId: localFallback(import.meta.env.VITE_OPERATOR_CONSOLE_SHIFT_ID, "77000000-0000-0000-0000-000000000050")
};

export function createOperatorConsoleApiClient(): OperatorConsoleApiClient {
  return createHttpOperatorConsoleApiClient({
    baseUrl: import.meta.env.VITE_CENTRAL_PMS_BASE_URL ?? ""
  });
}

export function createHttpOperatorConsoleApiClient(options: { baseUrl?: string } = {}): OperatorConsoleApiClient {
  const baseUrl = options.baseUrl?.replace(/\/$/, "") ?? "";

  return {
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

      return parseResponse<AuditReportResponse>(response);
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
    }
  };
}

function operatorConsoleHeaders(correlationId: string, options: { json?: boolean } = {}) {
  return {
    ...(options.json ? { "Content-Type": "application/json" } : {}),
    "X-Correlation-Id": correlationId,
    "X-Operator-User-Id": defaultOperatorContext.userId,
    "X-Operator-Device-Binding-Id": defaultOperatorContext.operatorDeviceBindingId,
    "X-Operator-Shift-Id": defaultOperatorContext.operatorShiftId
  };
}

function localFallback(value: string | undefined, fallback: string) {
  return value && value.trim().length > 0 ? value : fallback;
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
    !hasConfiguredValue(import.meta.env.VITE_OPERATOR_CONSOLE_SHIFT_ID)
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
    empty?: boolean;
    onDecision?: (input: StatutoryDiscountDecisionInput) => void;
    onEvidenceCapture?: (input: StatutoryDiscountEvidenceCaptureInput) => void;
    onPayableBasisApply?: (input: StatutoryDiscountPayableBasisApplicationInput) => void;
  } = {}
): OperatorConsoleApiClient {
  const drafts = (options.drafts ?? mockDrafts).map((draft) => ({ ...draft }));
  const evidence = new Map<string, StatutoryDiscountEvidenceItem[]>();
  return {
    async evaluateAccessReadiness(input) {
      await delay();
      return options.readiness ?? mockAccessReadiness(input);
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
    policyCode: draft.policyContext.policyCode,
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
