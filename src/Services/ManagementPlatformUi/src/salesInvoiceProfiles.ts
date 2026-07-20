import { createUiError } from "./apiClient";
import type { CentralPmsApiClient, ManagementPlatformSite, ManagementPlatformUiError } from "./types";

export const salesInvoiceProfileReadRoute = "/management-platform/sales-invoice-profiles";
export const salesInvoiceProfileApiRoutes = {
  profiles: "/v1/management-platform/sales-invoice-header-profiles",
  readiness: "/v1/management-platform/sales-invoice-header-profiles/effective-readiness",
  fiscalIdentities: "/v1/management-platform/fiscal-identities"
} as const;

export type SalesInvoiceProfileLifecycleState = "DRAFT" | "APPROVED" | "RETIRED" | string;
export type EffectiveReadinessStatus =
  | "READY"
  | "NO_EFFECTIVE_PROFILE"
  | "INCOMPLETE"
  | "EXPIRED"
  | "AMBIGUOUS"
  | "UNSUPPORTED_VERSION"
  | "RETIRED"
  | string;

export interface SalesInvoiceHeaderProfileSummary {
  salesInvoiceHeaderProfileId: string;
  fiscalIdentityId: string;
  siteId: string;
  sitePosServerId: string;
  profileVersion: string;
  templateVersion: string;
  presentationVersion: string;
  parkingLocationDisplay: string;
  lifecycleState: SalesInvoiceProfileLifecycleState;
  effectiveFrom?: string;
  effectiveTo?: string;
  updatedAt?: string;
  fiscalIdentityDisplayName?: string;
}

export interface SalesInvoiceHeaderProfile extends SalesInvoiceHeaderProfileSummary {
  posSerialNumber?: string;
  machineIdentificationNumber?: string;
  birAccreditationNumber?: string;
  birAccreditationIssuedDate?: string;
  birAccreditationValidUntil?: string;
  ptuNumber?: string;
  ptuIssuedDate?: string;
  salesInvoiceLegalStatement?: string;
  customerServiceFooter?: string;
  approvedAt?: string;
  approvedByRef?: string;
  retiredAt?: string;
  createdAt?: string;
}

export interface FiscalIdentityDetail {
  fiscalIdentityId: string;
  registeredBusinessName: string;
  registeredBusinessAddress: string;
  tin: string;
  taxpayerRegistrationPosture: string;
  lifecycleState?: string;
  status?: string;
  createdAt?: string;
  updatedAt?: string;
}

export interface SalesInvoiceProfileValidationResult {
  salesInvoiceHeaderProfileId: string;
  lifecycleState: SalesInvoiceProfileLifecycleState;
  isComplete: boolean;
  missingOrInvalidFieldCodes: string[];
  messages: string[];
  templateVersionPosture: string;
  presentationVersionPosture: string;
  effectiveWindowPosture: string;
  overlapPosture: string;
  fiscalIdentityPosture: string;
  validatedAt: string;
  correlationId?: string;
}

export interface EffectiveReadinessResult {
  siteId: string;
  sitePosServerId: string;
  effectiveAt: string;
  resolutionStatus: EffectiveReadinessStatus;
  effectiveProfileId?: string;
  profileVersion?: string;
  fiscalIdentityId?: string;
  lifecycleState?: SalesInvoiceProfileLifecycleState;
  isComplete: boolean;
  enforcementRequired: boolean;
  missingOrInvalidFieldCodes: string[];
  birAccreditationValidityPosture: string;
  ptuCompletenessPosture: string;
  supportedVersionPosture: string;
  overlapOrAmbiguityPosture: string;
  lastUpdatedAt?: string;
  correlationId?: string;
}

export interface SalesInvoiceProfileUsageResult {
  salesInvoiceHeaderProfileId: string;
  profileVersion: string;
  fiscalIdentityId: string;
  firstSnapshotAt?: string;
  latestSnapshotAt?: string;
  fiscalDocumentCount: number;
  safeFiscalDocumentIds: string[];
  destructiveMutationBlocked: boolean;
  correlationId?: string;
}

export interface SalesInvoiceProfileReadClient {
  listProfiles(site: ManagementPlatformSite, signal?: AbortSignal): Promise<SalesInvoiceHeaderProfileSummary[]>;
  getProfile(profileId: string, signal?: AbortSignal): Promise<SalesInvoiceHeaderProfile>;
  getFiscalIdentity(fiscalIdentityId: string, signal?: AbortSignal): Promise<FiscalIdentityDetail>;
  validateProfile(profileId: string, signal?: AbortSignal): Promise<SalesInvoiceProfileValidationResult>;
  getEffectiveReadiness(site: ManagementPlatformSite, effectiveAt: string, signal?: AbortSignal): Promise<EffectiveReadinessResult>;
  getProfileUsage(profileId: string, signal?: AbortSignal): Promise<SalesInvoiceProfileUsageResult>;
}

export type SalesInvoiceProfileReadScenarioName =
  | "profiles"
  | "empty"
  | "incomplete"
  | "ready"
  | "no-effective-profile"
  | "expired"
  | "ambiguous"
  | "unsupported-version"
  | "retired"
  | "unknown-readiness"
  | "usage"
  | "disabled"
  | "forbidden"
  | "unavailable";

export interface SalesInvoiceProfileReadScenario {
  name: SalesInvoiceProfileReadScenarioName;
  client: SalesInvoiceProfileReadClient;
  showIndicator: boolean;
}

export function createSalesInvoiceProfileReadClient(apiClient: CentralPmsApiClient): SalesInvoiceProfileReadClient {
  return {
    listProfiles(site, signal) {
      const query = new URLSearchParams({ siteId: site.siteId, sitePosServerId: site.sitePosServerId ?? "" });
      return apiClient.request<SalesInvoiceHeaderProfileSummary[]>(`${salesInvoiceProfileApiRoutes.profiles}?${query.toString()}`, { signal });
    },
    getProfile(profileId, signal) {
      return apiClient.request<SalesInvoiceHeaderProfile>(`${salesInvoiceProfileApiRoutes.profiles}/${encodeURIComponent(profileId)}`, { signal });
    },
    getFiscalIdentity(fiscalIdentityId, signal) {
      return apiClient.request<FiscalIdentityDetail>(`${salesInvoiceProfileApiRoutes.fiscalIdentities}/${encodeURIComponent(fiscalIdentityId)}`, { signal });
    },
    validateProfile(profileId, signal) {
      return apiClient.request<SalesInvoiceProfileValidationResult>(`${salesInvoiceProfileApiRoutes.profiles}/${encodeURIComponent(profileId)}/validate`, { method: "POST", signal });
    },
    getEffectiveReadiness(site, effectiveAt, signal) {
      const query = new URLSearchParams({ siteId: site.siteId, sitePosServerId: site.sitePosServerId ?? "", effectiveAt });
      return apiClient.request<EffectiveReadinessResult>(`${salesInvoiceProfileApiRoutes.readiness}?${query.toString()}`, { signal });
    },
    getProfileUsage(profileId, signal) {
      return apiClient.request<SalesInvoiceProfileUsageResult>(`${salesInvoiceProfileApiRoutes.profiles}/${encodeURIComponent(profileId)}/usage`, { signal });
    }
  };
}

export function resolveSalesInvoiceProfileReadScenario(isDevelopment: boolean, search: string): SalesInvoiceProfileReadScenario | undefined {
  if (!isDevelopment) {
    return undefined;
  }

  const scenarioName = normalizeProfileScenarioName(new URLSearchParams(search).get("mpProfileScenario"));
  if (!scenarioName) {
    return undefined;
  }

  return {
    name: scenarioName,
    client: createSalesInvoiceProfileScenarioClient(scenarioName),
    showIndicator: true
  };
}

export function readinessStatusText(status: EffectiveReadinessStatus): string {
  switch (status) {
    case "READY":
      return "Ready for Sales Invoice issuance";
    case "NO_EFFECTIVE_PROFILE":
      return "No effective Sales Invoice Header Profile";
    case "INCOMPLETE":
      return "Profile configuration is incomplete";
    case "EXPIRED":
      return "BIR accreditation or effective profile is expired";
    case "AMBIGUOUS":
      return "Multiple effective profiles require correction";
    case "UNSUPPORTED_VERSION":
      return "Template or presentation version is unsupported";
    case "RETIRED":
      return "Profile is retired and unavailable for new issuance";
    default:
      return `Unknown readiness: ${status}`;
  }
}

export function readinessTone(status: EffectiveReadinessStatus): "ready" | "warning" {
  return status === "READY" ? "ready" : "warning";
}

export function groupValidationCode(code: string): string {
  const normalized = code.toUpperCase();
  if (normalized.includes("FISCAL_IDENTITY") || normalized.includes("TIN") || normalized.includes("BUSINESS")) {
    return "Fiscal Identity";
  }
  if (normalized.includes("SITE")) {
    return "Site and Site POS Server";
  }
  if (normalized.includes("BIR") || normalized.includes("ACCREDITATION")) {
    return "BIR accreditation";
  }
  if (normalized.includes("PTU")) {
    return "PTU";
  }
  if (normalized.includes("EFFECTIVE")) {
    return "Effective dates";
  }
  if (normalized.includes("TEMPLATE") || normalized.includes("PRESENTATION")) {
    return "Template and presentation versions";
  }
  if (normalized.includes("OVERLAP") || normalized.includes("LIFECYCLE")) {
    return "Overlap and lifecycle";
  }

  return "Sales Invoice header";
}

export function isManagementPlatformUiError(error: unknown): error is ManagementPlatformUiError {
  return typeof error === "object" && error !== null && "kind" in error && "message" in error;
}

function createSalesInvoiceProfileScenarioClient(scenario: SalesInvoiceProfileReadScenarioName): SalesInvoiceProfileReadClient {
  return {
    async listProfiles(site, signal) {
      await scenarioDelay(signal);
      throwScenarioErrorIfNeeded(scenario);
      if (scenario === "empty") {
        return [];
      }
      return scenarioProfiles(scenario, site);
    },
    async getProfile(profileId, signal) {
      await scenarioDelay(signal);
      throwScenarioErrorIfNeeded(scenario);
      return scenarioProfile(scenario, profileId);
    },
    async getFiscalIdentity(fiscalIdentityId, signal) {
      await scenarioDelay(signal);
      throwScenarioErrorIfNeeded(scenario);
      return scenarioFiscalIdentity(fiscalIdentityId);
    },
    async validateProfile(profileId, signal) {
      await scenarioDelay(signal);
      throwScenarioErrorIfNeeded(scenario);
      return scenarioValidation(scenario, profileId);
    },
    async getEffectiveReadiness(site, effectiveAt, signal) {
      await scenarioDelay(signal);
      throwScenarioErrorIfNeeded(scenario);
      return scenarioReadiness(scenario, site, effectiveAt);
    },
    async getProfileUsage(profileId, signal) {
      await scenarioDelay(signal);
      throwScenarioErrorIfNeeded(scenario);
      return scenarioUsage(profileId);
    }
  };
}

function normalizeProfileScenarioName(value: string | null): SalesInvoiceProfileReadScenarioName | undefined {
  switch (value) {
    case "profiles":
    case "empty":
    case "incomplete":
    case "ready":
    case "no-effective-profile":
    case "expired":
    case "ambiguous":
    case "unsupported-version":
    case "retired":
    case "unknown-readiness":
    case "usage":
    case "disabled":
    case "forbidden":
    case "unavailable":
      return value;
    default:
      return undefined;
  }
}

function scenarioProfiles(scenario: SalesInvoiceProfileReadScenarioName, site: ManagementPlatformSite): SalesInvoiceHeaderProfileSummary[] {
  const firstProfile = scenarioProfile(scenario, "sip-dev-profile-001");
  return [
    {
      salesInvoiceHeaderProfileId: firstProfile.salesInvoiceHeaderProfileId,
      fiscalIdentityId: firstProfile.fiscalIdentityId,
      siteId: site.siteId,
      sitePosServerId: site.sitePosServerId ?? "dev-pos-server",
      profileVersion: firstProfile.profileVersion,
      templateVersion: firstProfile.templateVersion,
      presentationVersion: firstProfile.presentationVersion,
      parkingLocationDisplay: firstProfile.parkingLocationDisplay,
      lifecycleState: firstProfile.lifecycleState,
      effectiveFrom: firstProfile.effectiveFrom,
      effectiveTo: firstProfile.effectiveTo,
      updatedAt: firstProfile.updatedAt,
      fiscalIdentityDisplayName: "Development Parking Services"
    },
    {
      salesInvoiceHeaderProfileId: "sip-dev-profile-002",
      fiscalIdentityId: "fiscal-dev-identity-001",
      siteId: site.siteId,
      sitePosServerId: site.sitePosServerId ?? "dev-pos-server",
      profileVersion: "2026.02",
      templateVersion: "digital-sales-invoice-json-v1",
      presentationVersion: "digital-sales-invoice-presentation-json-v1",
      parkingLocationDisplay: "Development Overflow Parking",
      lifecycleState: "DRAFT",
      effectiveFrom: "2026-09-01T00:00:00Z",
      effectiveTo: "2027-08-31T23:59:59Z",
      updatedAt: "2026-07-19T07:20:00Z",
      fiscalIdentityDisplayName: "Development Parking Services"
    }
  ];
}

function scenarioProfile(scenario: SalesInvoiceProfileReadScenarioName, profileId: string): SalesInvoiceHeaderProfile {
  const retired = scenario === "retired";
  return {
    salesInvoiceHeaderProfileId: profileId,
    fiscalIdentityId: "fiscal-dev-identity-001",
    siteId: "71000000-0000-0000-0000-000000000101",
    sitePosServerId: "72000000-0000-0000-0000-000000000101",
    profileVersion: retired ? "2025.12" : "2026.01",
    templateVersion: "digital-sales-invoice-json-v1",
    presentationVersion: "digital-sales-invoice-presentation-json-v1",
    posSerialNumber: "DEV-POS-SERIAL-001",
    machineIdentificationNumber: "DEV-MIN-001",
    parkingLocationDisplay: "Development North Parking",
    birAccreditationNumber: "DEV-BIR-ACCREDITATION-001",
    birAccreditationIssuedDate: "2026-01-15",
    birAccreditationValidUntil: scenario === "expired" ? "2026-01-31" : "2027-01-31",
    ptuNumber: "DEV-PTU-001",
    ptuIssuedDate: "2026-01-20",
    salesInvoiceLegalStatement: "Development legal statement for manual validation only.",
    customerServiceFooter: "Development customer-service footer.",
    effectiveFrom: "2026-02-01T00:00:00Z",
    effectiveTo: "2027-01-31T23:59:59Z",
    lifecycleState: retired ? "RETIRED" : "APPROVED",
    approvedAt: "2026-01-25T04:00:00Z",
    approvedByRef: "dev-approver-ref",
    retiredAt: retired ? "2026-07-01T00:00:00Z" : undefined,
    createdAt: "2026-01-10T02:00:00Z",
    updatedAt: "2026-07-19T07:10:00Z"
  };
}

function scenarioFiscalIdentity(fiscalIdentityId: string): FiscalIdentityDetail {
  return {
    fiscalIdentityId,
    registeredBusinessName: "Development Parking Services",
    registeredBusinessAddress: "Development Address Block, Example City",
    tin: "DEV-TIN-000-000-001",
    taxpayerRegistrationPosture: "VAT_REGISTERED",
    lifecycleState: "ACTIVE",
    createdAt: "2026-01-08T01:00:00Z",
    updatedAt: "2026-07-18T03:00:00Z"
  };
}

function scenarioValidation(scenario: SalesInvoiceProfileReadScenarioName, profileId: string): SalesInvoiceProfileValidationResult {
  const incomplete = scenario === "incomplete";
  return {
    salesInvoiceHeaderProfileId: profileId,
    lifecycleState: scenario === "retired" ? "RETIRED" : "APPROVED",
    isComplete: !incomplete,
    missingOrInvalidFieldCodes: incomplete
      ? ["FISCAL_IDENTITY_TIN_REQUIRED", "BIR_ACCREDITATION_VALID_UNTIL_REQUIRED", "PTU_ISSUED_DATE_REQUIRED", "EFFECTIVE_WINDOW_OVERLAP", "UNKNOWN_FUTURE_CODE"]
      : [],
    messages: incomplete ? ["Fiscal Identity TIN is required.", "Unknown future validation code was preserved safely."] : ["Profile configuration is complete."],
    templateVersionPosture: "SUPPORTED",
    presentationVersionPosture: "SUPPORTED",
    effectiveWindowPosture: incomplete ? "OVERLAP" : "VALID",
    overlapPosture: incomplete ? "OVERLAP_DETECTED" : "NONE",
    fiscalIdentityPosture: incomplete ? "INCOMPLETE" : "VALID",
    validatedAt: "2026-07-20T04:30:00Z",
    correlationId: "dev-validation-correlation-0001"
  };
}

function scenarioReadiness(scenario: SalesInvoiceProfileReadScenarioName, site: ManagementPlatformSite, effectiveAt: string): EffectiveReadinessResult {
  const status = scenarioReadinessStatus(scenario);
  return {
    siteId: site.siteId,
    sitePosServerId: site.sitePosServerId ?? "dev-pos-server",
    effectiveAt,
    resolutionStatus: status,
    effectiveProfileId: status === "NO_EFFECTIVE_PROFILE" ? undefined : "sip-dev-profile-001",
    profileVersion: status === "NO_EFFECTIVE_PROFILE" ? undefined : "2026.01",
    fiscalIdentityId: status === "NO_EFFECTIVE_PROFILE" ? undefined : "fiscal-dev-identity-001",
    lifecycleState: status === "RETIRED" ? "RETIRED" : status === "NO_EFFECTIVE_PROFILE" ? undefined : "APPROVED",
    isComplete: status === "READY",
    enforcementRequired: true,
    missingOrInvalidFieldCodes: status === "READY" ? [] : ["DEV_READINESS_REVIEW_REQUIRED"],
    birAccreditationValidityPosture: status === "EXPIRED" ? "EXPIRED" : "VALID",
    ptuCompletenessPosture: status === "INCOMPLETE" ? "INCOMPLETE" : "COMPLETE",
    supportedVersionPosture: status === "UNSUPPORTED_VERSION" ? "UNSUPPORTED" : "SUPPORTED",
    overlapOrAmbiguityPosture: status === "AMBIGUOUS" ? "AMBIGUOUS" : "NONE",
    lastUpdatedAt: "2026-07-20T04:35:00Z",
    correlationId: "dev-readiness-correlation-0001"
  };
}

function scenarioReadinessStatus(scenario: SalesInvoiceProfileReadScenarioName): EffectiveReadinessStatus {
  switch (scenario) {
    case "no-effective-profile":
      return "NO_EFFECTIVE_PROFILE";
    case "incomplete":
      return "INCOMPLETE";
    case "expired":
      return "EXPIRED";
    case "ambiguous":
      return "AMBIGUOUS";
    case "unsupported-version":
      return "UNSUPPORTED_VERSION";
    case "retired":
      return "RETIRED";
    case "unknown-readiness":
      return "FUTURE_REVIEW_REQUIRED";
    default:
      return "READY";
  }
}

function scenarioUsage(profileId: string): SalesInvoiceProfileUsageResult {
  return {
    salesInvoiceHeaderProfileId: profileId,
    profileVersion: "2026.01",
    fiscalIdentityId: "fiscal-dev-identity-001",
    firstSnapshotAt: "2026-02-02T08:00:00Z",
    latestSnapshotAt: "2026-07-15T10:30:00Z",
    fiscalDocumentCount: 42,
    safeFiscalDocumentIds: ["DEV-FISCAL-DOC-0001", "DEV-FISCAL-DOC-0002"],
    destructiveMutationBlocked: true,
    correlationId: "dev-usage-correlation-0001"
  };
}

function throwScenarioErrorIfNeeded(scenario: SalesInvoiceProfileReadScenarioName): void {
  if (scenario === "disabled") {
    throw createUiError("feature-disabled", "SALES_INVOICE_PROFILE_ADMINISTRATION_DISABLED", "Sales Invoice Profile administration is not enabled for this environment.", "dev-disabled-correlation", 503);
  }
  if (scenario === "forbidden") {
    throw createUiError("permission-denied", "SITE_SCOPE_DENIED", "You do not have permission for this Site scope.", "dev-forbidden-correlation", 403);
  }
  if (scenario === "unavailable") {
    throw createUiError("integration-unavailable", "PROFILE_ADMINISTRATION_UNAVAILABLE", "Profile administration is unavailable.", "dev-unavailable-correlation", 503, true);
  }
}

function scenarioDelay(signal?: AbortSignal): Promise<void> {
  if (signal?.aborted) {
    return Promise.reject(new DOMException("cancelled", "AbortError"));
  }

  return new Promise((resolve, reject) => {
    const timeoutId = window.setTimeout(resolve, 1);
    signal?.addEventListener("abort", () => {
      window.clearTimeout(timeoutId);
      reject(new DOMException("cancelled", "AbortError"));
    }, { once: true });
  });
}