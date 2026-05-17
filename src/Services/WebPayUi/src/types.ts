export type PaymentMethod = "QRPH" | "CARD" | "GCASH" | "MAYA";

export type PaymentIntentRequest = {
  ticketReference?: string;
  plateNumber?: string;
  paymentMethod: PaymentMethod;
};

export type WebPayHandoff = {
  type?: string;
  handoffUrl?: string | null;
  qrCodeUrl?: string | null;
  expiresAt?: string | null;
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
};

export type ActivePaymentAttemptState = {
  kind: "active-payment-attempt";
  message: string;
  correlationId?: string;
  parkingSessionId?: string;
  paymentAttemptId?: string;
  status?: string;
  handoff?: WebPayHandoff | null;
};
