import type { OperatorSessionSummary, SessionLookupResult, SessionSearchCriteria } from "./types";

export interface StatutoryDiscountOperatorApiClient {
  findSession(criteria: SessionSearchCriteria): Promise<SessionLookupResult>;
}

const placeholderSession: OperatorSessionSummary = {
  parkingSessionReference: "STAT-OP-SESSION-0001",
  vehiclePlate: "ABC 1234",
  entryTime: "May 26, 2026 09:15 AM",
  currentFee: "PHP 120.00",
  payableBasisStatus: "Backend-approved payable basis pending lookup wiring",
  siteDisplayName: "Demo Site Group / Demo Parking Site"
};

export function createStatutoryDiscountOperatorApiClient(): StatutoryDiscountOperatorApiClient {
  return {
    async findSession(criteria) {
      const ticketNumber = criteria.ticketNumber.trim().toUpperCase();
      const plateNumber = criteria.plateNumber.trim().toUpperCase();

      if (ticketNumber === "DEMO-AMBIGUOUS" || plateNumber === "AMBIGUOUS") {
        return { status: "ambiguous session", matches: 2 };
      }

      if (ticketNumber === "DEMO-FOUND" || plateNumber === "ABC 1234") {
        return { status: "session found", session: placeholderSession };
      }

      return { status: "not found" };
    }
  };
}
