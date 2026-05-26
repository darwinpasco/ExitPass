export type PaymentMethod = "QRPH";

export type PaymentIntentRequest = {
  ticketReference?: string;
  plateNumber?: string;
  paymentMethod: PaymentMethod;
  siteGroupId?: string;
  siteId?: string;
  vendorSystemId?: string;
  tariffSnapshotId?: string;
  expectedAmountMinorUnits?: number;
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

export type PayableBasisModifierStatus = "APPROVED" | "PENDING_REVIEW" | "REJECTED" | "EXPIRED" | "FAILED";

export type CouponApplyRequest = {
  parkingSessionId: string;
  tariffSnapshotId: string;
  couponCode: string;
  amountMinorUnits: number;
  currency: string;
  correlationId?: string;
};

export type CouponApplyResponse = {
  status: PayableBasisModifierStatus | string;
  couponCode?: string | null;
  couponApplicationId?: string | null;
  tariffSnapshotId?: string | null;
  originalAmountMinorUnits?: number | null;
  adjustmentMinorUnits?: number | null;
  finalAmountMinorUnits?: number | null;
  currency?: string | null;
  message?: string | null;
  errorCode?: string | null;
  correlationId?: string | null;
};

export type StatutoryDiscountRequest = {
  parkingSessionId: string;
  tariffSnapshotId: string;
  entitlementType: "SENIOR_CITIZEN" | "PWD";
  evidenceReference?: string;
  amountMinorUnits: number;
  currency: string;
  correlationId?: string;
};

export type StatutoryDiscountResponse = {
  status: PayableBasisModifierStatus | string;
  entitlementType?: string | null;
  statutoryDiscountValidationId?: string | null;
  tariffSnapshotId?: string | null;
  originalAmountMinorUnits?: number | null;
  adjustmentMinorUnits?: number | null;
  finalAmountMinorUnits?: number | null;
  currency?: string | null;
  evidenceRequired?: boolean | null;
  message?: string | null;
  errorCode?: string | null;
  correlationId?: string | null;
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
