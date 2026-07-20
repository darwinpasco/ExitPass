import { createUiError } from "./apiClient";
import type { CentralPmsApiClient, ManagementPlatformSite, ManagementPlatformUiError } from "./types";

export const salesInvoiceProfileReadRoute = "/management-platform/sales-invoice-profiles";
export const salesInvoiceProfileApiRoutes = {
  profiles: "/v1/management-platform/sales-invoice-header-profiles",
  readiness: "/v1/management-platform/sales-invoice-header-profiles/effective-readiness",
  fiscalIdentities: "/v1/management-platform/fiscal-identities"
} as const;

export const controlledSalesInvoiceTemplateVersion = "digital-sales-invoice-json-v1";
export const controlledSalesInvoicePresentationVersion = "digital-sales-invoice-presentation-json-v1";

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

export interface FiscalIdentityMutationRequest {
  registeredBusinessName: string;
  registeredBusinessAddress: string;
  tin: string;
  taxpayerRegistrationPosture: string;
}

export interface SalesInvoiceHeaderProfileMutationRequest {
  fiscalIdentityId: string;
  siteId: string;
  sitePosServerId: string;
  profileVersion: string;
  templateVersion: string;
  presentationVersion: string;
  posSerialNumber: string;
  machineIdentificationNumber: string;
  parkingLocationDisplay: string;
  birAccreditationNumber: string;
  birAccreditationIssuedDate: string;
  birAccreditationValidUntil: string;
  ptuNumber: string;
  ptuIssuedDate: string;
  salesInvoiceLegalStatement: string;
  customerServiceFooter: string;
  effectiveFrom: string;
  effectiveTo?: string;
}

export interface SalesInvoiceProfileReadClient {
  listProfiles(site: ManagementPlatformSite, signal?: AbortSignal): Promise<SalesInvoiceHeaderProfileSummary[]>;
  getProfile(profileId: string, signal?: AbortSignal): Promise<SalesInvoiceHeaderProfile>;
  getFiscalIdentity(fiscalIdentityId: string, signal?: AbortSignal): Promise<FiscalIdentityDetail>;
  validateProfile(profileId: string, signal?: AbortSignal): Promise<SalesInvoiceProfileValidationResult>;
  getEffectiveReadiness(site: ManagementPlatformSite, effectiveAt: string, signal?: AbortSignal): Promise<EffectiveReadinessResult>;
  getProfileUsage(profileId: string, signal?: AbortSignal): Promise<SalesInvoiceProfileUsageResult>;
}

export interface SalesInvoiceProfileClient extends SalesInvoiceProfileReadClient {
  createFiscalIdentity(request: FiscalIdentityMutationRequest, signal?: AbortSignal): Promise<FiscalIdentityDetail>;
  updateFiscalIdentity(fiscalIdentityId: string, request: FiscalIdentityMutationRequest, signal?: AbortSignal): Promise<FiscalIdentityDetail>;
  createProfile(request: SalesInvoiceHeaderProfileMutationRequest, signal?: AbortSignal): Promise<SalesInvoiceHeaderProfile>;
  updateDraftProfile(profileId: string, request: SalesInvoiceHeaderProfileMutationRequest, signal?: AbortSignal): Promise<SalesInvoiceHeaderProfile>;
  approveProfile(profileId: string, signal?: AbortSignal): Promise<SalesInvoiceHeaderProfile>;
  retireProfile(profileId: string, signal?: AbortSignal): Promise<SalesInvoiceHeaderProfile>;
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
  | "unavailable"
  | "manage"
  | "read-only"
  | "fiscal-identity-create-success"
  | "fiscal-identity-create-conflict"
  | "fiscal-identity-update-success"
  | "fiscal-identity-update-immutable"
  | "profile-create-success"
  | "profile-create-conflict"
  | "profile-create-timeout"
  | "draft-edit-success"
  | "draft-edit-conflict"
  | "approved-read-only"
  | "retired-read-only"
  | "forbidden-manage"
  | "disabled-manage"
  | "unavailable-manage"
  | "approve-user"
  | "manage-without-approve"
  | "approve-draft-complete"
  | "approve-draft-incomplete"
  | "approve-success"
  | "approve-conflict"
  | "approve-timeout"
  | "retire-approved"
  | "retire-success"
  | "retire-conflict"
  | "retire-timeout"
  | "retired-history"
  | "approve-forbidden"
  | "retire-forbidden"
  | "new-version-manage"
  | "new-version-read-only"
  | "new-version-approve-only"
  | "new-version-success"
  | "new-version-duplicate-conflict"
  | "new-version-overlap-conflict"
  | "new-version-timeout"
  | "new-version-site-mismatch"
  | "new-version-source-not-active"
  | "new-version-source-not-found"
  | "new-version-cancel"
  | "new-version-unsaved-site-switch"
  | "new-version-pending-site-switch"
  | "new-version-double-submit"
  | "new-version-source-preserved"
  | "new-version-unknown-status";

export interface SalesInvoiceProfileReadScenario {
  name: SalesInvoiceProfileReadScenarioName;
  client: SalesInvoiceProfileClient;
  showIndicator: boolean;
}

export function createSalesInvoiceProfileReadClient(apiClient: CentralPmsApiClient): SalesInvoiceProfileClient {
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
    },
    createFiscalIdentity(request, signal) {
      return apiClient.request<FiscalIdentityDetail>(salesInvoiceProfileApiRoutes.fiscalIdentities, { method: "POST", body: sanitizeFiscalIdentityRequest(request), signal });
    },
    updateFiscalIdentity(fiscalIdentityId, request, signal) {
      return apiClient.request<FiscalIdentityDetail>(`${salesInvoiceProfileApiRoutes.fiscalIdentities}/${encodeURIComponent(fiscalIdentityId)}`, { method: "PATCH", body: sanitizeFiscalIdentityRequest(request), signal });
    },
    createProfile(request, signal) {
      return apiClient.request<SalesInvoiceHeaderProfile>(salesInvoiceProfileApiRoutes.profiles, { method: "POST", body: sanitizeProfileRequest(request), signal });
    },
    updateDraftProfile(profileId, request, signal) {
      return apiClient.request<SalesInvoiceHeaderProfile>(`${salesInvoiceProfileApiRoutes.profiles}/${encodeURIComponent(profileId)}`, { method: "PATCH", body: sanitizeProfileRequest(request), signal });
    },
    approveProfile(profileId, signal) {
      return apiClient.request<SalesInvoiceHeaderProfile>(`${salesInvoiceProfileApiRoutes.profiles}/${encodeURIComponent(profileId)}/approve`, { method: "POST", signal });
    },
    retireProfile(profileId, signal) {
      return apiClient.request<SalesInvoiceHeaderProfile>(`${salesInvoiceProfileApiRoutes.profiles}/${encodeURIComponent(profileId)}/retire`, { method: "POST", signal });
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
      return "No effective Sales Invoice Setup";
    case "INCOMPLETE":
      return "Setup configuration is incomplete";
    case "EXPIRED":
      return "BIR accreditation or effective profile is expired";
    case "AMBIGUOUS":
      return "Multiple effective profiles require correction";
    case "UNSUPPORTED_VERSION":
      return "Template or presentation version is unsupported";
    case "RETIRED":
      return "Setup is retired and unavailable for new issuance";
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
    return "Registered Business";
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

function createSalesInvoiceProfileScenarioClient(scenario: SalesInvoiceProfileReadScenarioName): SalesInvoiceProfileClient {
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
      if (scenario === "new-version-source-not-found") {
        throw createUiError("not-found", "SALES_INVOICE_SETUP_NOT_FOUND", "The selected Sales Invoice Setup could not be found. Refresh and select an available source setup.", "dev-new-version-source-not-found", 404, false, false);
      }
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
    },
    async createFiscalIdentity(request, signal) {
      await scenarioDelay(signal);
      throwMutationScenarioErrorIfNeeded(scenario, "fiscal-create");
      return {
        ...scenarioFiscalIdentity("fiscal-dev-identity-created"),
        registeredBusinessName: request.registeredBusinessName,
        registeredBusinessAddress: request.registeredBusinessAddress,
        tin: request.tin,
        taxpayerRegistrationPosture: request.taxpayerRegistrationPosture,
        createdAt: "2026-07-20T05:00:00Z",
        updatedAt: "2026-07-20T05:00:00Z"
      };
    },
    async updateFiscalIdentity(fiscalIdentityId, request, signal) {
      await scenarioDelay(signal);
      throwMutationScenarioErrorIfNeeded(scenario, "fiscal-update");
      return {
        ...scenarioFiscalIdentity(fiscalIdentityId),
        registeredBusinessName: request.registeredBusinessName,
        registeredBusinessAddress: request.registeredBusinessAddress,
        tin: request.tin,
        taxpayerRegistrationPosture: request.taxpayerRegistrationPosture,
        updatedAt: "2026-07-20T05:10:00Z"
      };
    },
    async createProfile(request, signal) {
      await scenarioDelay(signal);
      throwMutationScenarioErrorIfNeeded(scenario, "profile-create");
      return profileFromRequest(scenario === "new-version-success" || scenario === "new-version-source-preserved" ? "sip-dev-profile-new-version" : "sip-dev-profile-created", request, "DRAFT");
    },
    async updateDraftProfile(profileId, request, signal) {
      await scenarioDelay(signal);
      throwMutationScenarioErrorIfNeeded(scenario, "profile-update");
      return profileFromRequest(profileId, request, "DRAFT");
    },
    async approveProfile(profileId, signal) {
      await scenarioDelay(signal);
      throwMutationScenarioErrorIfNeeded(scenario, "approve");
      return {
        ...scenarioProfile(scenario, profileId),
        lifecycleState: "APPROVED",
        approvedAt: "2026-07-20T06:00:00Z",
        retiredAt: undefined,
        updatedAt: "2026-07-20T06:00:00Z"
      };
    },
    async retireProfile(profileId, signal) {
      await scenarioDelay(signal);
      throwMutationScenarioErrorIfNeeded(scenario, "retire");
      return {
        ...scenarioProfile(scenario, profileId),
        lifecycleState: "RETIRED",
        retiredAt: "2026-07-20T06:30:00Z",
        updatedAt: "2026-07-20T06:30:00Z"
      };
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
    case "manage":
    case "read-only":
    case "fiscal-identity-create-success":
    case "fiscal-identity-create-conflict":
    case "fiscal-identity-update-success":
    case "fiscal-identity-update-immutable":
    case "profile-create-success":
    case "profile-create-conflict":
    case "profile-create-timeout":
    case "draft-edit-success":
    case "draft-edit-conflict":
    case "approved-read-only":
    case "retired-read-only":
    case "forbidden-manage":
    case "disabled-manage":
    case "unavailable-manage":
    case "approve-user":
    case "manage-without-approve":
    case "approve-draft-complete":
    case "approve-draft-incomplete":
    case "approve-success":
    case "approve-conflict":
    case "approve-timeout":
    case "retire-approved":
    case "retire-success":
    case "retire-conflict":
    case "retire-timeout":
    case "retired-history":
    case "approve-forbidden":
    case "retire-forbidden":
    case "new-version-manage":
    case "new-version-read-only":
    case "new-version-approve-only":
    case "new-version-success":
    case "new-version-duplicate-conflict":
    case "new-version-overlap-conflict":
    case "new-version-timeout":
    case "new-version-site-mismatch":
    case "new-version-source-not-active":
    case "new-version-source-not-found":
    case "new-version-cancel":
    case "new-version-unsaved-site-switch":
    case "new-version-pending-site-switch":
    case "new-version-double-submit":
    case "new-version-source-preserved":
    case "new-version-unknown-status":
      return value;
    default:
      return undefined;
  }
}

function sanitizeFiscalIdentityRequest(request: FiscalIdentityMutationRequest): FiscalIdentityMutationRequest {
  return {
    registeredBusinessName: request.registeredBusinessName.trim(),
    registeredBusinessAddress: request.registeredBusinessAddress.trim(),
    tin: request.tin.trim(),
    taxpayerRegistrationPosture: request.taxpayerRegistrationPosture.trim()
  };
}

function sanitizeProfileRequest(request: SalesInvoiceHeaderProfileMutationRequest): SalesInvoiceHeaderProfileMutationRequest {
  return {
    fiscalIdentityId: request.fiscalIdentityId.trim(),
    siteId: request.siteId.trim(),
    sitePosServerId: request.sitePosServerId.trim(),
    profileVersion: request.profileVersion.trim(),
    templateVersion: request.templateVersion.trim(),
    presentationVersion: request.presentationVersion.trim(),
    posSerialNumber: request.posSerialNumber.trim(),
    machineIdentificationNumber: request.machineIdentificationNumber.trim(),
    parkingLocationDisplay: request.parkingLocationDisplay.trim(),
    birAccreditationNumber: request.birAccreditationNumber.trim(),
    birAccreditationIssuedDate: request.birAccreditationIssuedDate.trim(),
    birAccreditationValidUntil: request.birAccreditationValidUntil.trim(),
    ptuNumber: request.ptuNumber.trim(),
    ptuIssuedDate: request.ptuIssuedDate.trim(),
    salesInvoiceLegalStatement: request.salesInvoiceLegalStatement.trim(),
    customerServiceFooter: request.customerServiceFooter.trim(),
    effectiveFrom: request.effectiveFrom.trim(),
    effectiveTo: request.effectiveTo?.trim() || undefined
  };
}

function scenarioProfiles(scenario: SalesInvoiceProfileReadScenarioName, site: ManagementPlatformSite): SalesInvoiceHeaderProfileSummary[] {
  const firstProfile = scenarioProfile(scenario, "sip-dev-profile-001");
  const useSourceScope = scenario === "new-version-site-mismatch";
  return [
    {
      salesInvoiceHeaderProfileId: firstProfile.salesInvoiceHeaderProfileId,
      fiscalIdentityId: firstProfile.fiscalIdentityId,
      siteId: useSourceScope ? firstProfile.siteId : site.siteId,
      sitePosServerId: useSourceScope ? firstProfile.sitePosServerId : site.sitePosServerId ?? "dev-pos-server",
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
  const retired = scenario === "retired" || scenario === "retired-read-only" || scenario === "retired-history";
  const draft = scenario === "manage" || scenario === "profile-create-success" || scenario === "draft-edit-success" || scenario === "draft-edit-conflict" || scenario === "approve-user" || scenario === "manage-without-approve" || scenario === "approve-draft-complete" || scenario === "approve-draft-incomplete" || scenario === "approve-success" || scenario === "approve-conflict" || scenario === "approve-timeout" || scenario === "approve-forbidden" || scenario === "new-version-source-not-active";
  const siteMismatch = scenario === "new-version-site-mismatch";
  const unknownStatus = scenario === "new-version-unknown-status";
  return {
    salesInvoiceHeaderProfileId: profileId,
    fiscalIdentityId: "fiscal-dev-identity-001",
    siteId: siteMismatch ? "71000000-0000-0000-0000-000000000102" : "71000000-0000-0000-0000-000000000101",
    sitePosServerId: siteMismatch ? "72000000-0000-0000-0000-000000000102" : "72000000-0000-0000-0000-000000000101",
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
    lifecycleState: unknownStatus ? "PENDING_REVIEW" : retired ? "RETIRED" : draft ? "DRAFT" : "APPROVED",
    approvedAt: draft || unknownStatus ? undefined : "2026-01-25T04:00:00Z",
    approvedByRef: draft || unknownStatus ? undefined : "dev-approver-ref",
    retiredAt: retired ? "2026-07-01T00:00:00Z" : undefined,
    createdAt: "2026-01-10T02:00:00Z",
    updatedAt: "2026-07-19T07:10:00Z"
  };
}

function profileFromRequest(
  profileId: string,
  request: SalesInvoiceHeaderProfileMutationRequest,
  lifecycleState: SalesInvoiceProfileLifecycleState
): SalesInvoiceHeaderProfile {
  return {
    salesInvoiceHeaderProfileId: profileId,
    fiscalIdentityId: request.fiscalIdentityId,
    siteId: request.siteId,
    sitePosServerId: request.sitePosServerId,
    profileVersion: request.profileVersion,
    templateVersion: request.templateVersion,
    presentationVersion: request.presentationVersion,
    posSerialNumber: request.posSerialNumber,
    machineIdentificationNumber: request.machineIdentificationNumber,
    parkingLocationDisplay: request.parkingLocationDisplay,
    birAccreditationNumber: request.birAccreditationNumber,
    birAccreditationIssuedDate: request.birAccreditationIssuedDate,
    birAccreditationValidUntil: request.birAccreditationValidUntil,
    ptuNumber: request.ptuNumber,
    ptuIssuedDate: request.ptuIssuedDate,
    salesInvoiceLegalStatement: request.salesInvoiceLegalStatement,
    customerServiceFooter: request.customerServiceFooter,
    effectiveFrom: request.effectiveFrom,
    effectiveTo: request.effectiveTo,
    lifecycleState,
    createdAt: "2026-07-20T05:15:00Z",
    updatedAt: "2026-07-20T05:15:00Z"
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
  const incomplete = scenario === "incomplete" || scenario === "approve-draft-incomplete";
  return {
    salesInvoiceHeaderProfileId: profileId,
    lifecycleState: draftValidationScenario(scenario) ? "DRAFT" : scenario === "retired" ? "RETIRED" : "APPROVED",
    isComplete: !incomplete,
    missingOrInvalidFieldCodes: incomplete
      ? ["FISCAL_IDENTITY_TIN_REQUIRED", "BIR_ACCREDITATION_VALID_UNTIL_REQUIRED", "PTU_ISSUED_DATE_REQUIRED", "EFFECTIVE_WINDOW_OVERLAP", "UNKNOWN_FUTURE_CODE"]
      : [],
    messages: incomplete ? ["Registered Business TIN is required.", "Unknown future validation code was preserved safely."] : ["Setup configuration is complete."],
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
  if (scenario === "disabled" || scenario === "disabled-manage") {
    throw createUiError("feature-disabled", "SALES_INVOICE_PROFILE_ADMINISTRATION_DISABLED", "Sales Invoice Configuration is not enabled for this environment.", "dev-disabled-correlation", 503);
  }
  if (scenario === "forbidden" || scenario === "forbidden-manage") {
    throw createUiError("permission-denied", "SITE_SCOPE_DENIED", "You do not have permission for this Site scope.", "dev-forbidden-correlation", 403);
  }
  if (scenario === "unavailable" || scenario === "unavailable-manage") {
    throw createUiError("integration-unavailable", "PROFILE_ADMINISTRATION_UNAVAILABLE", "Sales Invoice Configuration is unavailable.", "dev-unavailable-correlation", 503, true);
  }
}

function throwMutationScenarioErrorIfNeeded(
  scenario: SalesInvoiceProfileReadScenarioName,
  operation: "fiscal-create" | "fiscal-update" | "profile-create" | "profile-update"
    | "approve" | "retire"
): void {
  throwScenarioErrorIfNeeded(scenario);

  if (scenario === "fiscal-identity-create-conflict" && operation === "fiscal-create") {
    throw createUiError("conflict", "FISCAL_IDENTITY_DUPLICATE_OR_IMMUTABLE", "The Registered Business conflicts with authoritative state.", "dev-fiscal-create-conflict", 409, false, false);
  }
  if (scenario === "fiscal-identity-update-immutable" && operation === "fiscal-update") {
    throw createUiError("conflict", "FISCAL_IDENTITY_IMMUTABLE", "The Registered Business cannot be changed in its current authoritative state.", "dev-fiscal-update-conflict", 409, false, false);
  }
  if (scenario === "profile-create-conflict" && operation === "profile-create") {
    throw createUiError("conflict", "SALES_INVOICE_PROFILE_DUPLICATE_VERSION", "The Draft Sales Invoice Setup conflicts with version or effective-window rules.", "dev-profile-create-conflict", 409, false, false);
  }
  if (scenario === "new-version-duplicate-conflict" && operation === "profile-create") {
    throw createUiError("conflict", "SALES_INVOICE_SETUP_DUPLICATE_VERSION", "The new setup version already exists. Keep the form values, choose an explicit different version, and submit again only after review.", "dev-new-version-duplicate-conflict", 409, false, false);
  }
  if (scenario === "new-version-overlap-conflict" && operation === "profile-create") {
    throw createUiError("conflict", "SALES_INVOICE_SETUP_EFFECTIVE_PERIOD_OVERLAP", "The effective period conflicts with another Active setup. Keep the form values and review the authoritative effective period before submitting again.", "dev-new-version-overlap-conflict", 409, false, false);
  }
  if (scenario === "draft-edit-conflict" && operation === "profile-update") {
    throw createUiError("conflict", "SALES_INVOICE_PROFILE_LIFECYCLE_CONFLICT", "The Draft Sales Invoice Setup can no longer be updated in place.", "dev-draft-edit-conflict", 409, false, false);
  }
  if (scenario === "profile-create-timeout" && operation === "profile-create") {
    throw createUiError("timeout", "SALES_INVOICE_PROFILE_MUTATION_TIMEOUT", "Mutation result is uncertain. Refresh and verify before retrying.", "dev-profile-create-timeout", 504, true, true);
  }
  if (scenario === "new-version-timeout" && operation === "profile-create") {
    throw createUiError("timeout", "SALES_INVOICE_SETUP_NEW_VERSION_TIMEOUT", "Create result is uncertain. Refresh and verify whether the Draft was created before trying again.", "dev-new-version-timeout", 504, true, true);
  }
  if ((scenario === "approve-forbidden" && operation === "approve") || (scenario === "retire-forbidden" && operation === "retire")) {
    throw createUiError("permission-denied", "SALES_INVOICE_SETUP_APPROVE_DENIED", "You do not have permission to change this Sales Invoice Setup status.", "dev-approve-forbidden-correlation", 403);
  }
  if (scenario === "approve-conflict" && operation === "approve") {
    throw createUiError("conflict", "SALES_INVOICE_SETUP_ACTIVATION_CONFLICT", "The Sales Invoice Setup could not be activated because authoritative validation or status changed.", "dev-approve-conflict", 409, false, false);
  }
  if (scenario === "approve-timeout" && operation === "approve") {
    throw createUiError("timeout", "SALES_INVOICE_SETUP_ACTIVATION_TIMEOUT", "Activation result is uncertain. Refresh and verify the authoritative status before trying again.", "dev-approve-timeout", 504, true, true);
  }
  if (scenario === "retire-conflict" && operation === "retire") {
    throw createUiError("conflict", "SALES_INVOICE_SETUP_RETIREMENT_CONFLICT", "The Sales Invoice Setup could not be retired because authoritative status changed.", "dev-retire-conflict", 409, false, false);
  }
  if (scenario === "retire-timeout" && operation === "retire") {
    throw createUiError("timeout", "SALES_INVOICE_SETUP_RETIREMENT_TIMEOUT", "Retirement result is uncertain. Refresh and verify the authoritative status before trying again.", "dev-retire-timeout", 504, true, true);
  }
}

function draftValidationScenario(scenario: SalesInvoiceProfileReadScenarioName): boolean {
  return scenario === "approve-user" ||
    scenario === "manage-without-approve" ||
    scenario === "approve-draft-complete" ||
    scenario === "approve-draft-incomplete" ||
    scenario === "approve-success" ||
    scenario === "approve-conflict" ||
    scenario === "approve-timeout" ||
    scenario === "approve-forbidden";
}

function scenarioDelay(signal?: AbortSignal): Promise<void> {
  if (signal?.aborted) {
    return Promise.reject(new DOMException("cancelled", "AbortError"));
  }

  return new Promise((resolve, reject) => {
    const timeoutId = window.setTimeout(resolve, developmentScenarioDelayMs());
    signal?.addEventListener("abort", () => {
      window.clearTimeout(timeoutId);
      reject(new DOMException("cancelled", "AbortError"));
    }, { once: true });
  });
}

function developmentScenarioDelayMs(): number {
  const parsedDelay = Number(new URLSearchParams(window.location.search).get("mpProfileDelayMs"));
  if (!Number.isFinite(parsedDelay)) {
    return 1;
  }

  return Math.min(Math.max(parsedDelay, 1), 1_000);
}
