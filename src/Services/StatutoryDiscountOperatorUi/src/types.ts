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

export interface OperatorSessionSummary {
  parkingSessionReference: string;
  vehiclePlate: string;
  entryTime: string;
  currentFee: string;
  payableBasisStatus: string;
  siteDisplayName: string;
}

export interface StatutoryDiscountReview {
  entitlementType: EntitlementType;
  validationStatus: StatutoryDiscountValidationStatus;
  operatorInstruction: string;
}
