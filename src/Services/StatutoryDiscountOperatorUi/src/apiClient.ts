import type { OperatorSessionSummary, SessionSearchCriteria } from "./types";

export interface StatutoryDiscountOperatorApiClient {
  findSession(criteria: SessionSearchCriteria): Promise<OperatorSessionSummary | null>;
}

export function createStatutoryDiscountOperatorApiClient(): StatutoryDiscountOperatorApiClient {
  return {
    async findSession() {
      return null;
    }
  };
}
