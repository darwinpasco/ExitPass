export type PaymentMethod = "QRPH";

export type PaymentIntentRequest = {
  ticketReference?: string;
  plateNumber?: string;
  paymentMethod: PaymentMethod;
  siteGroupId?: string;
  siteId?: string;
  vendorSystemId?: string;
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
  currency?: string | null;
  sessionStatus?: string | null;
  parkingStatus?: string | null;
  paymentStatus?: string | null;
  feeValidUntil?: string | null;
  tariffExpiresAt?: string | null;
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
