export type EntitlementType = "Senior Citizen" | "PWD";

export type StatutoryDiscountValidationStatus =
  | "no request"
  | "pending operator review"
  | "approved"
  | "rejected"
  | "expired";

export interface SessionSearchCriteria {
  ticketNumber: string;
  plateNumber: string;
}

export type SessionLookupStatus =
  | "not searched"
  | "searching"
  | "session found"
  | "not found"
  | "ambiguous session";

export type OperatorConsoleModuleStatus = "available" | "first module" | "planned";

export interface OperatorConsoleModule {
  name: string;
  status: OperatorConsoleModuleStatus;
  description: string;
}

export interface OperatorSessionSummary {
  parkingSessionReference: string;
  vehiclePlate: string;
  entryTime: string;
  currentFee: string;
  paymentStatus: string;
  payableBasisStatus: string;
  siteDisplayName: string;
}

export type SessionLookupResult =
  | { status: "session found"; session: OperatorSessionSummary }
  | { status: "not found" }
  | { status: "ambiguous session"; matches: number };

export interface StatutoryDiscountReview {
  entitlementType: EntitlementType;
  validationStatus: StatutoryDiscountValidationStatus;
  operatorInstruction: string;
}
