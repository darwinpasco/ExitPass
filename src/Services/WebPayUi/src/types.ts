export type PaymentMethod = "QRPH" | "CARD" | "GCASH" | "MAYA";

export type PaymentIntentRequest = {
  ticketReference?: string;
  plateNumber?: string;
  paymentMethod: PaymentMethod;
  siteGroupId?: string;
  siteId?: string;
  vendorSystemId?: string;
};

export type WebPayHandoff = {
  type?: string;
  handoffUrl?: string | null;
  checkoutUrl?: string | null;
  resumePaymentUrl?: string | null;
  qrCodeUrl?: string | null;
  expiresAt?: string | null;
};

export type ParkingSessionSummary = {
  siteName?: string | null;
  ticketReference?: string | null;
  plateNumber?: string | null;
  entryTime?: string | null;
  exitTime?: string | null;
  currentFeeCalculationTime?: string | null;
  durationParked?: string | null;
  tariffName?: string | null;
  amountMinorUnits?: number | null;
  currency?: string | null;
  sessionStatus?: string | null;
  feeValidUntil?: string | null;
  tariffExpiresAt?: string | null;
};

export type PaymentIntentResponse = {
  paymentAttemptId: string;
  parkingSessionId: string;
  tariffSnapshotId: string;
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
