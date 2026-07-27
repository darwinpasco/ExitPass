export type PaymentMethod = "QRPH" | "GCASH" | "MAYA" | "CARD";

export type PaymentIntentRequest = {
  ticketReference?: string;
  plateNumber?: string;
  paymentMethod: PaymentMethod;
  siteGroupId?: string;
  siteId?: string;
  vendorSystemId?: string;
  tariffSnapshotId?: string;
  expectedAmountMinorUnits?: number;
  expectedCurrency?: string;
  statutoryDiscountDecisionCommandId?: string;
  statutoryDiscountPayableBasisApplicationCommandId?: string;
  correlationId?: string;
};

export type ParkingSessionResolveRequest = Omit<PaymentIntentRequest, "paymentMethod">;

export type WebPayHandoff = {
  type?: string;
  handoffUrl?: string | null;
  checkoutUrl?: string | null;
  resumePaymentUrl?: string | null;
  qrCodeUrl?: string | null;
  expiresAt?: string | null;
};

export type ParkingSessionSummary = {
  siteGroupName?: string | null;
  siteName?: string | null;
  ticketReference?: string | null;
  plateNumber?: string | null;
  entryTime?: string | null;
  exitTime?: string | null;
  currentFeeCalculationTime?: string | null;
  durationParked?: string | null;
  tariffName?: string | null;
  totalFeeMinorUnits?: number | null;
  amountMinorUnits?: number | null;
  originalAmountMinorUnits?: number | null;
  couponAdjustmentMinorUnits?: number | null;
  statutoryAdjustmentMinorUnits?: number | null;
  totalAdjustmentMinorUnits?: number | null;
  couponStatus?: string | null;
  statutoryStatus?: string | null;
  statutoryDiscountStatus?: string | null;
  statutoryDiscountValidationStatus?: string | null;
  currency?: string | null;
  sessionStatus?: string | null;
  parkingStatus?: string | null;
  paymentStatus?: string | null;
  feeValidUntil?: string | null;
  tariffExpiresAt?: string | null;
};

export type WebPayExitInstruction = {
  status?: string | null;
  message?: string | null;
  exitBy?: string | null;
  expiresAt?: string | null;
  laneName?: string | null;
};

export type PaymentIntentResponse = {
  paymentAttemptId: string;
  parkingSessionId: string;
  tariffSnapshotId: string;
  siteGroupId?: string | null;
  siteId?: string | null;
  vendorSystemId?: string | null;
  siteGroupName?: string | null;
  amountMinorUnits: number;
  currency: string;
  paymentMethod: PaymentMethod | string;
  selectedProviderCode?: string;
  fallbackProviderCode?: string | null;
  routingReason?: string;
  status: string;
  handoff?: WebPayHandoff | null;
  correlationId: string;
  siteName?: string | null;
  ticketReference?: string | null;
  plateNumber?: string | null;
  entryTime?: string | null;
  exitTime?: string | null;
  currentFeeCalculationTime?: string | null;
  durationParked?: string | null;
  tariffName?: string | null;
  totalFeeMinorUnits?: number | null;
  couponAdjustmentMinorUnits?: number | null;
  statutoryAdjustmentMinorUnits?: number | null;
  totalAdjustmentMinorUnits?: number | null;
  couponStatus?: string | null;
  statutoryStatus?: string | null;
  statutoryDiscountStatus?: string | null;
  statutoryDiscountValidationStatus?: string | null;
  feeValidUntil?: string | null;
  tariffExpiresAt?: string | null;
  parkingStatus?: string | null;
  paymentStatus?: string | null;
  sessionSummary?: ParkingSessionSummary | null;
};

export type ParkingSessionResolveResponse = {
  parkingSessionId: string;
  tariffSnapshotId: string;
  siteGroupId?: string | null;
  siteId?: string | null;
  vendorSystemId?: string | null;
  siteGroupName?: string | null;
  amountMinorUnits: number;
  originalAmountMinorUnits?: number | null;
  couponAdjustmentMinorUnits?: number | null;
  statutoryAdjustmentMinorUnits?: number | null;
  totalAdjustmentMinorUnits?: number | null;
  couponStatus?: string | null;
  statutoryStatus?: string | null;
  statutoryDiscountStatus?: string | null;
  statutoryDiscountValidationStatus?: string | null;
  currency: string;
  correlationId: string;
  siteName?: string | null;
  ticketReference?: string | null;
  plateNumber?: string | null;
  entryTime?: string | null;
  exitTime?: string | null;
  currentFeeCalculationTime?: string | null;
  durationParked?: string | null;
  tariffName?: string | null;
  totalFeeMinorUnits?: number | null;
  parkingStatus?: string | null;
  paymentStatus?: string | null;
  feeValidUntil?: string | null;
  tariffExpiresAt?: string | null;
  sessionSummary?: ParkingSessionSummary | null;
  exitInstruction?: WebPayExitInstruction | null;
  exitAuthorizationStatus?: string | null;
  exitAuthorizationExpiresAt?: string | null;
  exitBy?: string | null;
};

export type SalesInvoicePresentationRow = {
  key?: string | null;
  label?: string | null;
  displayValue?: string | number | null;
  value?: string | number | null;
  posture?: string | null;
};

export type SalesInvoicePresentationSection = {
  key?: string | null;
  name?: string | null;
  title?: string | null;
  rows?: SalesInvoicePresentationRow[] | null;
};

export type SalesInvoicePresentationDocument = {
  documentTitle?: string | null;
  renderFormat?: string | null;
  presentationVersion?: string | null;
  templateVersion?: string | null;
  sections?: SalesInvoicePresentationSection[] | null;
};

export type SalesInvoiceAuthoritativePresentation = {
  presentation?: SalesInvoicePresentationDocument | null;
  presentationVersion?: string | null;
  templateVersion?: string | null;
  contentType?: string | null;
  fiscalDocumentId?: string | null;
  fiscalDocumentNumber?: string | null;
  [key: string]: unknown;
};

export type WebPayReceiptPresentationResponse = {
  paymentAttemptId: string;
  paymentConfirmationId: string;
  fiscalIssuanceReferenceId: string;
  fiscalIssuanceState: string;
  posFiscalDocumentId: string;
  fiscalDocumentNumber?: string | null;
  fiscalDocumentStatus?: string | null;
  receiptAvailabilityState: string;
  presentationVersion?: string | null;
  templateVersion?: string | null;
  contentType?: string | null;
  authoritativePresentation: SalesInvoiceAuthoritativePresentation;
  voidStatus?: string | null;
  voidReasonCode?: string | null;
  voidedAt?: string | null;
  createdAt: string;
  updatedAt: string;
  correlationId: string;
};

export type ApiError = {
  errorCode?: string;
  message?: string;
  retryable?: boolean;
  correlationId?: string;
  parkingSessionId?: string;
  paymentAttemptId?: string;
  status?: string;
  handoff?: WebPayHandoff | null;
  handoffUrl?: string | null;
  checkoutUrl?: string | null;
  resumePaymentUrl?: string | null;
  statusUrl?: string | null;
  checkStatusUrl?: string | null;
  paymentMethod?: PaymentMethod | string;
  amountMinorUnits?: number | null;
  currency?: string | null;
  siteName?: string | null;
  ticketReference?: string | null;
  plateNumber?: string | null;
};

export type StatutoryDiscountEntitlementType = "SENIOR_CITIZEN" | "PWD";

export type WebPayStatutoryDiscountDecisionRequest = {
  requestReference: string;
  parkingSessionId: string;
  siteId?: string | null;
  siteGroupId?: string | null;
  ticketReference?: string | null;
  plateNumber?: string | null;
  entitlementType: StatutoryDiscountEntitlementType;
  idDocumentType: string;
  issuingAuthority: string;
  expiryDate?: string | null;
  maskedIdReference: string;
  evidenceCaptureRequested: boolean;
  requesterAttestation: boolean;
  attestationNotes?: string | null;
  originalTariffSnapshotId?: string | null;
};

export type WebPayStatutoryDiscountDecisionResponse = {
  statutoryDiscountDecisionCommandId: string;
  requestReference: string;
  statutoryDiscountPayableBasisApplicationCommandId?: string | null;
  statutoryDiscountValidationId?: string | null;
  parkingSessionId: string;
  siteId?: string | null;
  siteGroupId?: string | null;
  entitlementType: string;
  decisionCommandStatus: string;
  decisionResultStatus?: string | null;
  applicationCommandStatus: string;
  applicationResultClassification: string;
  payableBasisReady: boolean;
  payableBasisReadinessStatus: string;
  payableBasisReadinessAction?: string | null;
  originalTariffSnapshotId?: string | null;
  appliedTariffSnapshotId?: string | null;
  originalAmountMinorUnits?: number | null;
  vatExclusiveBasisAmountMinorUnits?: number | null;
  vatAmountMinorUnits?: number | null;
  vatTreatment?: string | null;
  statutoryDiscountAmountMinorUnits?: number | null;
  finalPayableAmountMinorUnits?: number | null;
  currency?: string | null;
  retryable: boolean;
  recoveryClassification: string;
  recoveryAction?: string | null;
  safeErrorCode?: string | null;
  overallResultClassification: string;
  oneShotComplete: boolean;
  correlationId: string;
  createdAt: string;
  decidedAt?: string | null;
  appliedAt?: string | null;
};

export type ActivePaymentAttemptState = {
  kind: "active-payment-attempt";
  message: string;
  correlationId?: string;
  parkingSessionId?: string;
  paymentAttemptId?: string;
  status?: string;
  handoff?: WebPayHandoff | null;
  statusUrl?: string | null;
  checkStatusUrl?: string | null;
  paymentMethod?: PaymentMethod | string;
  amountMinorUnits?: number | null;
  currency?: string | null;
  siteName?: string | null;
  ticketReference?: string | null;
  plateNumber?: string | null;
};
