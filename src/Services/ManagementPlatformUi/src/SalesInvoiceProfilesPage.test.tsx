import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { App } from "./App";
import { createUiError } from "./apiClient";
import { futureSalesInvoiceProfilePermissions, managementPlatformOverviewPermission } from "./permissions";
import { SalesInvoiceProfilesPage } from "./SalesInvoiceProfilesPage";
import { controlledSalesInvoicePresentationVersion, controlledSalesInvoiceTemplateVersion, createSalesInvoiceProfileReadClient, resolveSalesInvoiceProfileReadScenario, salesInvoiceProfileReadRoute, type EffectiveReadinessResult, type FiscalIdentityDetail, type FiscalIdentityMutationRequest, type SalesInvoiceHeaderProfile, type SalesInvoiceHeaderProfileMutationRequest, type SalesInvoiceHeaderProfileSummary, type SalesInvoiceProfileClient, type SalesInvoiceProfileUsageResult, type SalesInvoiceProfileValidationResult } from "./salesInvoiceProfiles";
import type { CentralPmsApiClient, ManagementPlatformAuthState, ManagementPlatformSite } from "./types";

const siteA: ManagementPlatformSite = {
  siteId: "71000000-0000-0000-0000-000000000101",
  sitePosServerId: "72000000-0000-0000-0000-000000000101",
  displayName: "Development Site Alpha"
};

const siteB: ManagementPlatformSite = {
  siteId: "71000000-0000-0000-0000-000000000102",
  sitePosServerId: "72000000-0000-0000-0000-000000000102",
  displayName: "Development Site Beta"
};

function authState(permissions = [managementPlatformOverviewPermission, futureSalesInvoiceProfilePermissions.read], sites = [siteA, siteB]): ManagementPlatformAuthState {
  return {
    status: "authenticated",
    principal: {
      authenticated: true,
      subjectRef: "read-user-001",
      displayName: "Read User",
      permissions,
      authorizedSites: sites
    }
  };
}

function profileSummary(site = siteA, id = "profile-001", version = "2026.01"): SalesInvoiceHeaderProfileSummary {
  return {
    salesInvoiceHeaderProfileId: id,
    fiscalIdentityId: "fiscal-001",
    siteId: site.siteId,
    sitePosServerId: site.sitePosServerId ?? "pos-server-001",
    profileVersion: version,
    templateVersion: "digital-sales-invoice-json-v1",
    presentationVersion: "digital-sales-invoice-presentation-json-v1",
    parkingLocationDisplay: site.displayName,
    lifecycleState: "APPROVED",
    effectiveFrom: "2026-01-01T00:00:00Z",
    effectiveTo: "2026-12-31T23:59:59Z",
    updatedAt: "2026-07-20T01:00:00Z",
    fiscalIdentityDisplayName: "Development Parking Services"
  };
}

function profile(id = "profile-001"): SalesInvoiceHeaderProfile {
  return {
    ...profileSummary(siteA, id),
    posSerialNumber: "DEV-POS-001",
    machineIdentificationNumber: "DEV-MIN-001",
    birAccreditationNumber: "DEV-BIR-001",
    birAccreditationIssuedDate: "2026-01-15",
    birAccreditationValidUntil: "2027-01-15",
    ptuNumber: "DEV-PTU-001",
    ptuIssuedDate: "2026-01-20",
    salesInvoiceLegalStatement: "Development legal statement.",
    customerServiceFooter: "Development footer.",
    approvedAt: "2026-02-01T00:00:00Z",
    retiredAt: undefined,
    createdAt: "2026-01-01T00:00:00Z"
  };
}

const fiscalIdentity: FiscalIdentityDetail = {
  fiscalIdentityId: "fiscal-001",
  registeredBusinessName: "Development Parking Services",
  registeredBusinessAddress: "Development Address Block",
  tin: "DEV-TIN-001",
  taxpayerRegistrationPosture: "VAT_REGISTERED",
  lifecycleState: "ACTIVE",
  createdAt: "2026-01-01T00:00:00Z",
  updatedAt: "2026-07-20T01:00:00Z"
};

const completeValidation: SalesInvoiceProfileValidationResult = {
  salesInvoiceHeaderProfileId: "profile-001",
  lifecycleState: "APPROVED",
  isComplete: true,
  missingOrInvalidFieldCodes: [],
  messages: ["Profile configuration is complete."],
  templateVersionPosture: "SUPPORTED",
  presentationVersionPosture: "SUPPORTED",
  effectiveWindowPosture: "VALID",
  overlapPosture: "NONE",
  fiscalIdentityPosture: "VALID",
  validatedAt: "2026-07-20T04:30:00Z",
  correlationId: "corr-validation"
};

function completeDraftValidation(id = "profile-draft-001"): SalesInvoiceProfileValidationResult {
  return {
    ...completeValidation,
    salesInvoiceHeaderProfileId: id,
    lifecycleState: "DRAFT",
    messages: ["Sales Invoice Setup configuration is complete."]
  };
}

function readiness(status: EffectiveReadinessResult["resolutionStatus"] = "READY"): EffectiveReadinessResult {
  return {
    siteId: siteA.siteId,
    sitePosServerId: siteA.sitePosServerId!,
    effectiveAt: "2026-07-20T04:30:00Z",
    resolutionStatus: status,
    effectiveProfileId: status === "NO_EFFECTIVE_PROFILE" ? undefined : "profile-001",
    profileVersion: status === "NO_EFFECTIVE_PROFILE" ? undefined : "2026.01",
    fiscalIdentityId: status === "NO_EFFECTIVE_PROFILE" ? undefined : "fiscal-001",
    lifecycleState: status === "RETIRED" ? "RETIRED" : status === "NO_EFFECTIVE_PROFILE" ? undefined : "APPROVED",
    isComplete: status === "READY",
    enforcementRequired: true,
    missingOrInvalidFieldCodes: status === "READY" ? [] : ["SAFE_CODE"],
    birAccreditationValidityPosture: status === "EXPIRED" ? "EXPIRED" : "VALID",
    ptuCompletenessPosture: status === "INCOMPLETE" ? "INCOMPLETE" : "COMPLETE",
    supportedVersionPosture: status === "UNSUPPORTED_VERSION" ? "UNSUPPORTED" : "SUPPORTED",
    overlapOrAmbiguityPosture: status === "AMBIGUOUS" ? "AMBIGUOUS" : "NONE",
    lastUpdatedAt: "2026-07-20T04:35:00Z",
    correlationId: "corr-readiness"
  };
}

const usage: SalesInvoiceProfileUsageResult = {
  salesInvoiceHeaderProfileId: "profile-001",
  profileVersion: "2026.01",
  fiscalIdentityId: "fiscal-001",
  firstSnapshotAt: "2026-02-01T00:00:00Z",
  latestSnapshotAt: "2026-07-19T00:00:00Z",
  fiscalDocumentCount: 3,
  safeFiscalDocumentIds: ["SAFE-DOC-001", "SAFE-DOC-002"],
  destructiveMutationBlocked: true,
  correlationId: "corr-usage"
};

function draftProfile(id = "profile-draft-001"): SalesInvoiceHeaderProfile {
  return {
    ...profile(id),
    lifecycleState: "DRAFT",
    approvedAt: undefined,
    retiredAt: undefined
  };
}

function profileFromMutation(id: string, request: SalesInvoiceHeaderProfileMutationRequest): SalesInvoiceHeaderProfile {
  return {
    salesInvoiceHeaderProfileId: id,
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
    lifecycleState: "DRAFT",
    createdAt: "2026-07-20T05:00:00Z",
    updatedAt: "2026-07-20T05:00:00Z"
  };
}

function fiscalIdentityFromMutation(id: string, request: FiscalIdentityMutationRequest): FiscalIdentityDetail {
  return {
    fiscalIdentityId: id,
    registeredBusinessName: request.registeredBusinessName,
    registeredBusinessAddress: request.registeredBusinessAddress,
    tin: request.tin,
    taxpayerRegistrationPosture: request.taxpayerRegistrationPosture,
    lifecycleState: "ACTIVE",
    createdAt: "2026-07-20T05:00:00Z",
    updatedAt: "2026-07-20T05:00:00Z"
  };
}

function makeClient(overrides: Partial<SalesInvoiceProfileClient> = {}): SalesInvoiceProfileClient {
  return {
    listProfiles: vi.fn(async (site: ManagementPlatformSite) => [profileSummary(site)]),
    getProfile: vi.fn(async (id: string) => profile(id)),
    getFiscalIdentity: vi.fn(async () => fiscalIdentity),
    validateProfile: vi.fn(async () => completeValidation),
    getEffectiveReadiness: vi.fn(async () => readiness()),
    getProfileUsage: vi.fn(async () => usage),
    createFiscalIdentity: vi.fn(async (request: FiscalIdentityMutationRequest) => fiscalIdentityFromMutation("fiscal-created", request)),
    updateFiscalIdentity: vi.fn(async (id: string, request: FiscalIdentityMutationRequest) => fiscalIdentityFromMutation(id, request)),
    createProfile: vi.fn(async (request: SalesInvoiceHeaderProfileMutationRequest) => profileFromMutation("profile-created", request)),
    updateDraftProfile: vi.fn(async (id: string, request: SalesInvoiceHeaderProfileMutationRequest) => profileFromMutation(id, request)),
    approveProfile: vi.fn(async (id: string) => ({ ...draftProfile(id), lifecycleState: "APPROVED", approvedAt: "2026-07-20T06:00:00Z", updatedAt: "2026-07-20T06:00:00Z" })),
    retireProfile: vi.fn(async (id: string) => ({ ...profile(id), lifecycleState: "RETIRED", retiredAt: "2026-07-20T06:30:00Z", updatedAt: "2026-07-20T06:30:00Z" })),
    ...overrides
  };
}

function fillFiscalIdentityForm(form: HTMLElement) {
  fireEvent.change(within(form).getByLabelText(/Registered business name/i), { target: { value: "Managed Development Parking" } });
  fireEvent.change(within(form).getByLabelText(/Registered business address/i), { target: { value: "Managed Development Address" } });
  fireEvent.change(within(form).getByLabelText(/^TIN/i), { target: { value: "MANAGED-TIN-001" } });
  fireEvent.change(within(form).getByLabelText(/Taxpayer\/VAT registration posture/i), { target: { value: "VAT_REGISTERED" } });
}

function fillProfileForm(form: HTMLElement) {
  fireEvent.change(within(form).getByLabelText(/Registered Business ID/i), { target: { value: "fiscal-001" } });
  fireEvent.change(within(form).getByLabelText(/Setup version/i), { target: { value: "2026.02" } });
  fireEvent.change(within(form).getByLabelText(/POS serial number/i), { target: { value: "DEV-POS-002" } });
  fireEvent.change(within(form).getByLabelText(/Machine Identification Number/i), { target: { value: "DEV-MIN-002" } });
  fireEvent.change(within(form).getByLabelText(/Parking-location display/i), { target: { value: "Managed Development Parking" } });
  fireEvent.change(within(form).getByLabelText(/BIR accreditation number/i), { target: { value: "DEV-BIR-002" } });
  fireEvent.change(within(form).getByLabelText(/BIR accreditation date issued/i), { target: { value: "2026-02-01" } });
  fireEvent.change(within(form).getByLabelText(/BIR accreditation valid until/i), { target: { value: "2027-02-01" } });
  fireEvent.change(within(form).getByLabelText(/PTU number/i), { target: { value: "DEV-PTU-002" } });
  fireEvent.change(within(form).getByLabelText(/PTU date issued/i), { target: { value: "2026-02-02" } });
  fireEvent.change(within(form).getByLabelText(/Sales Invoice legal statement/i), { target: { value: "Managed development legal statement." } });
  fireEvent.change(within(form).getByLabelText(/Customer-service footer/i), { target: { value: "Managed development footer." } });
  fireEvent.change(within(form).getByLabelText(/^Effective from/i), { target: { value: "2026-02-01T08:00" } });
  fireEvent.change(within(form).getByLabelText(/^Effective to/i), { target: { value: "2026-12-31T23:59" } });
}

describe("SalesInvoiceProfileReadClient", () => {
  it("uses only relative Central PMS Management Platform profile routes", async () => {
    const calls: Array<{ path: string; method?: string }> = [];
    const apiClient: CentralPmsApiClient = {
      async request(path, options) {
        calls.push({ path, method: options?.method });
        return {} as never;
      }
    };
    const client = createSalesInvoiceProfileReadClient(apiClient);

    await client.listProfiles(siteA);
    await client.getProfile("profile-001");
    await client.validateProfile("profile-001");
    await client.approveProfile("profile-001");
    await client.retireProfile("profile-001");
    await client.getEffectiveReadiness(siteA, "2026-07-20T04:30:00Z");
    await client.getProfileUsage("profile-001");
    await client.getFiscalIdentity("fiscal-001");

    expect(calls.map((call) => call.path)).toEqual([
      "/v1/management-platform/sales-invoice-header-profiles?siteId=71000000-0000-0000-0000-000000000101&sitePosServerId=72000000-0000-0000-0000-000000000101",
      "/v1/management-platform/sales-invoice-header-profiles/profile-001",
      "/v1/management-platform/sales-invoice-header-profiles/profile-001/validate",
      "/v1/management-platform/sales-invoice-header-profiles/profile-001/approve",
      "/v1/management-platform/sales-invoice-header-profiles/profile-001/retire",
      "/v1/management-platform/sales-invoice-header-profiles/effective-readiness?siteId=71000000-0000-0000-0000-000000000101&sitePosServerId=72000000-0000-0000-0000-000000000101&effectiveAt=2026-07-20T04%3A30%3A00Z",
      "/v1/management-platform/sales-invoice-header-profiles/profile-001/usage",
      "/v1/management-platform/fiscal-identities/fiscal-001"
    ]);
    expect(calls[2].method).toBe("POST");
    expect(calls.every((call) => call.path.startsWith("/v1/management-platform"))).toBe(true);
  });

  it("sends manage mutations only to Central PMS with browser-safe DTOs", async () => {
    const calls: Array<{ path: string; method?: string; body?: unknown }> = [];
    const apiClient: CentralPmsApiClient = {
      async request(path, options) {
        calls.push({ path, method: options?.method, body: options?.body });
        return {} as never;
      }
    };
    const client = createSalesInvoiceProfileReadClient(apiClient);

    await client.createFiscalIdentity({
      registeredBusinessName: " Development Parking Services ",
      registeredBusinessAddress: " Development Address ",
      tin: " DEV-TIN ",
      taxpayerRegistrationPosture: "VAT_REGISTERED"
    });
    await client.updateFiscalIdentity("fiscal-001", {
      registeredBusinessName: "Updated Development Parking Services",
      registeredBusinessAddress: "Updated Development Address",
      tin: "DEV-TIN-UPDATED",
      taxpayerRegistrationPosture: "VAT_REGISTERED"
    });
    await client.createProfile({
      fiscalIdentityId: "fiscal-001",
      siteId: siteA.siteId,
      sitePosServerId: siteA.sitePosServerId!,
      profileVersion: "2026.02",
      templateVersion: controlledSalesInvoiceTemplateVersion,
      presentationVersion: controlledSalesInvoicePresentationVersion,
      posSerialNumber: "DEV-POS-002",
      machineIdentificationNumber: "DEV-MIN-002",
      parkingLocationDisplay: "Development Parking",
      birAccreditationNumber: "DEV-BIR-002",
      birAccreditationIssuedDate: "2026-02-01",
      birAccreditationValidUntil: "2027-02-01",
      ptuNumber: "DEV-PTU-002",
      ptuIssuedDate: "2026-02-02",
      salesInvoiceLegalStatement: "Development legal statement.",
      customerServiceFooter: "Development footer.",
      effectiveFrom: "2026-02-01T00:00",
      effectiveTo: "2026-12-31T23:59"
    });
    await client.updateDraftProfile("profile-draft-001", {
      fiscalIdentityId: "fiscal-001",
      siteId: siteA.siteId,
      sitePosServerId: siteA.sitePosServerId!,
      profileVersion: "2026.02",
      templateVersion: controlledSalesInvoiceTemplateVersion,
      presentationVersion: controlledSalesInvoicePresentationVersion,
      posSerialNumber: "DEV-POS-002",
      machineIdentificationNumber: "DEV-MIN-002",
      parkingLocationDisplay: "Development Parking",
      birAccreditationNumber: "DEV-BIR-002",
      birAccreditationIssuedDate: "2026-02-01",
      birAccreditationValidUntil: "2027-02-01",
      ptuNumber: "DEV-PTU-002",
      ptuIssuedDate: "2026-02-02",
      salesInvoiceLegalStatement: "Development legal statement.",
      customerServiceFooter: "Development footer.",
      effectiveFrom: "2026-02-01T00:00",
      effectiveTo: "2026-12-31T23:59"
    });

    expect(calls.map((call) => [call.method, call.path])).toEqual([
      ["POST", "/v1/management-platform/fiscal-identities"],
      ["PATCH", "/v1/management-platform/fiscal-identities/fiscal-001"],
      ["POST", "/v1/management-platform/sales-invoice-header-profiles"],
      ["PATCH", "/v1/management-platform/sales-invoice-header-profiles/profile-draft-001"]
    ]);
    expect(calls.every((call) => call.path.startsWith("/v1/management-platform"))).toBe(true);
    for (const call of calls) {
      expect(JSON.stringify(call.body)).not.toMatch(/createdByRef|updatedByRef|approvedByRef|retiredByRef|terminalId|X-PosServer-Admin-Key|X-PosServer-Admin-Permission/i);
    }
    expect(calls[0].body).toMatchObject({ registeredBusinessName: "Development Parking Services", tin: "DEV-TIN" });
    expect(calls[2].body).toMatchObject({
      siteId: siteA.siteId,
      sitePosServerId: siteA.sitePosServerId,
      templateVersion: controlledSalesInvoiceTemplateVersion,
      presentationVersion: controlledSalesInvoicePresentationVersion
    });
  });
});

describe("Sales Invoice Profile read-only route", () => {
  it("requires the read permission and does not expose mutation controls", async () => {
    const { rerender } = render(<App authState={authState([managementPlatformOverviewPermission])} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient()} />);
    expect(screen.getByRole("alert", { name: "Permission denied" })).toBeInTheDocument();

    rerender(<App authState={authState()} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient()} />);
    expect(await screen.findByRole("heading", { name: "Sales Invoice Setups" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Sales Invoice Configuration Sales Invoice Setups/i })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Create|Edit|Activate|Retire|Delete|New Version/i })).not.toBeInTheDocument();
  });

  it("sends selected Site and Site POS Server to the list and readiness calls", async () => {
    const client = makeClient();
    render(<App authState={authState()} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={client} />);

    await screen.findByRole("button", { name: "2026.01" });

    expect(client.listProfiles).toHaveBeenCalledWith(expect.objectContaining({ siteId: siteA.siteId, sitePosServerId: siteA.sitePosServerId }), expect.any(AbortSignal));
    expect(client.getEffectiveReadiness).toHaveBeenCalledWith(expect.objectContaining({ siteId: siteA.siteId, sitePosServerId: siteA.sitePosServerId }), expect.any(String), expect.any(AbortSignal));
  });

  it("clears old Site data when Site selection changes", async () => {
    const client = makeClient({
      listProfiles: vi.fn(async (site: ManagementPlatformSite) => [profileSummary(site, `profile-${site.siteId.slice(-3)}`, site.displayName.includes("Beta") ? "BETA-2026" : "ALPHA-2026")])
    });
    render(<App authState={authState()} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={client} />);

    expect(await screen.findByRole("button", { name: "ALPHA-2026" })).toBeInTheDocument();
    await userEvent.selectOptions(screen.getByLabelText("Current Site"), siteB.siteId);

    await waitFor(() => expect(screen.queryByRole("button", { name: "ALPHA-2026" })).not.toBeInTheDocument());
    expect(await screen.findByRole("button", { name: "BETA-2026" })).toBeInTheDocument();
  });

  it("renders empty and forbidden Site-scope postures safely", async () => {
    const { rerender } = render(<SalesInvoiceProfilesPage currentSite={siteA} client={makeClient({ listProfiles: vi.fn(async () => []) })} />);
    expect(await screen.findByRole("status", { name: "No Sales Invoice Setups" })).toBeInTheDocument();

    rerender(<SalesInvoiceProfilesPage currentSite={siteA} client={makeClient({ listProfiles: vi.fn(async () => { throw createUiError("permission-denied", "SITE_SCOPE_DENIED", "You do not have permission for this Site scope.", "corr-forbidden", 403); }) })} />);
    expect(await screen.findByRole("alert", { name: "Sales Invoice Setups unavailable" })).toHaveTextContent("corr-forbidden");
  });
});

describe("Sales Invoice Profile Manage-only workflows", () => {
  const managePermissions = [managementPlatformOverviewPermission, futureSalesInvoiceProfilePermissions.read, futureSalesInvoiceProfilePermissions.manage];

  it("shows create controls only to Manage-authorized users and unrelated permissions grant no mutation access", async () => {
    const { rerender } = render(<App authState={authState()} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient()} />);
    expect(await screen.findByRole("heading", { name: "Sales Invoice Setups" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Create Registered Business" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Create Draft Sales Invoice Setup" })).not.toBeInTheDocument();

    rerender(<App authState={authState([managementPlatformOverviewPermission, futureSalesInvoiceProfilePermissions.read, "unrelated.permission"])} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient()} />);
    expect(screen.queryByRole("button", { name: "Create Registered Business" })).not.toBeInTheDocument();

    rerender(<App authState={authState(managePermissions)} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient()} />);
    expect(await screen.findByRole("button", { name: "Create Registered Business" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Create Draft Sales Invoice Setup" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Activate|Retire|Delete|New Version/i })).not.toBeInTheDocument();
  });

  it("creates a Fiscal Identity once with no actor fields and refreshes authoritative detail", async () => {
    const createFiscalIdentity = vi.fn(async (request: FiscalIdentityMutationRequest) => fiscalIdentityFromMutation("fiscal-created", request));
    render(<App authState={authState(managePermissions)} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient({ createFiscalIdentity })} />);

    await userEvent.click(await screen.findByRole("button", { name: "Create Registered Business" }));
    const form = screen.getByRole("form", { name: "Create Registered Business" });
    expect(form).not.toHaveTextContent("createdByRef");
    expect(form).not.toHaveTextContent("updatedByRef");
    await fillFiscalIdentityForm(form);
    await userEvent.click(within(form).getByRole("button", { name: "Create Registered Business" }));

    await waitFor(() => expect(createFiscalIdentity).toHaveBeenCalledTimes(1));
    const submitted = createFiscalIdentity.mock.calls[0][0];
    expect(submitted).toMatchObject({ registeredBusinessName: "Managed Development Parking", tin: "MANAGED-TIN-001" });
    expect(JSON.stringify(submitted)).not.toMatch(/createdByRef|updatedByRef|approvedByRef|retiredByRef/i);
    expect(await screen.findByRole("status", { name: "Registered business created" })).toBeInTheDocument();
  });

  it("keeps Fiscal Identity form data on governed conflict and shows timeout uncertainty without retry", async () => {
    const conflict = vi.fn(async () => {
      throw createUiError("conflict", "FISCAL_IDENTITY_IMMUTABLE", "The Registered Business is already used and cannot be changed.", "corr-conflict", 409);
    });
    const { rerender } = render(<App authState={authState(managePermissions)} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient({ createFiscalIdentity: conflict })} />);

    await userEvent.click(await screen.findByRole("button", { name: "Create Registered Business" }));
    const conflictForm = screen.getByRole("form", { name: "Create Registered Business" });
    await fillFiscalIdentityForm(conflictForm);
    await userEvent.click(within(conflictForm).getByRole("button", { name: "Create Registered Business" }));

    expect(await screen.findByRole("alert", { name: "Changes failed safely" })).toHaveTextContent("corr-conflict");
    expect(within(conflictForm).getByLabelText(/Registered business name/i)).toHaveValue("Managed Development Parking");
    expect(conflict).toHaveBeenCalledTimes(1);

    const timeout = vi.fn(async () => {
      throw createUiError("timeout", "PROFILE_CREATE_TIMEOUT", "The request timed out. Refresh and verify the authoritative state before trying again.", "corr-timeout", 504, true, true);
    });
    rerender(<App authState={authState(managePermissions)} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient({ createProfile: timeout })} />);
    await userEvent.click(await screen.findByRole("button", { name: "Create Draft Sales Invoice Setup" }));
    const timeoutForm = screen.getByRole("form", { name: "Create Draft Sales Invoice Setup" });
    await fillProfileForm(timeoutForm);
    await userEvent.click(within(timeoutForm).getByRole("button", { name: "Create Draft Sales Invoice Setup" }));

    expect(await screen.findByRole("status", { name: "Result uncertain" })).toHaveTextContent("Refresh and verify");
    expect(timeout).toHaveBeenCalledTimes(1);
  });

  it("creates a DRAFT Header Profile with controlled versions, Site scope, and no terminal or actor fields", async () => {
    const createProfile = vi.fn(async (request: SalesInvoiceHeaderProfileMutationRequest) => profileFromMutation("profile-created", request));
    render(<App authState={authState(managePermissions)} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient({ createProfile })} />);

    await userEvent.click(await screen.findByRole("button", { name: "Create Draft Sales Invoice Setup" }));
    const form = screen.getByRole("form", { name: "Create Draft Sales Invoice Setup" });
    expect(within(form).getByLabelText(/Template version/i)).toHaveValue(controlledSalesInvoiceTemplateVersion);
    expect(within(form).getByLabelText(/Presentation version/i)).toHaveValue(controlledSalesInvoicePresentationVersion);
    expect(within(form).getByLabelText(/Site ID/i)).toHaveValue(siteA.siteId);
    expect(within(form).getByLabelText(/Site POS Server ID/i)).toHaveValue(siteA.sitePosServerId);
    expect(form).not.toHaveTextContent("Terminal ID");
    expect(form).not.toHaveTextContent("approvedByRef");
    expect(form).not.toHaveTextContent("retiredByRef");

    await fillProfileForm(form);
    await userEvent.click(within(form).getByRole("button", { name: "Create Draft Sales Invoice Setup" }));

    await waitFor(() => expect(createProfile).toHaveBeenCalledTimes(1));
    const submitted = createProfile.mock.calls[0][0];
    expect(submitted).toMatchObject({
      siteId: siteA.siteId,
      sitePosServerId: siteA.sitePosServerId,
      templateVersion: controlledSalesInvoiceTemplateVersion,
      presentationVersion: controlledSalesInvoicePresentationVersion
    });
    expect(JSON.stringify(submitted)).not.toMatch(/terminalId|createdByRef|updatedByRef|approvedByRef|retiredByRef|lifecycleState/i);
    expect(await screen.findByRole("status", { name: "Draft Sales Invoice Setup created" })).toBeInTheDocument();
  });

  it("edits only DRAFT profiles and keeps APPROVED or RETIRED profiles read-only", async () => {
    const updateDraftProfile = vi.fn(async (id: string, request: SalesInvoiceHeaderProfileMutationRequest) => profileFromMutation(id, request));
    const { rerender } = render(<SalesInvoiceProfilesPage currentSite={siteA} client={makeClient({ getProfile: vi.fn(async () => draftProfile()), updateDraftProfile })} canManage />);

    await userEvent.click(await screen.findByRole("button", { name: "2026.01" }));
    await userEvent.click(await screen.findByRole("button", { name: "Edit Draft Sales Invoice Setup" }));
    const form = screen.getByRole("form", { name: "Save Draft Changes" });
    await userEvent.clear(within(form).getByLabelText(/Parking-location display/i));
    await userEvent.type(within(form).getByLabelText(/Parking-location display/i), "Updated Development Parking");
    await userEvent.click(within(form).getByRole("button", { name: "Save Draft Changes" }));

    await waitFor(() => expect(updateDraftProfile).toHaveBeenCalledTimes(1));
    expect(updateDraftProfile.mock.calls[0][0]).toBe("profile-draft-001");
    expect(updateDraftProfile.mock.calls[0][1]).toMatchObject({ parkingLocationDisplay: "Updated Development Parking" });

    rerender(<SalesInvoiceProfilesPage currentSite={siteA} client={makeClient({ getProfile: vi.fn(async () => profile()) })} canManage />);
    await userEvent.click(await screen.findByRole("button", { name: "2026.01" }));
    expect(await screen.findByRole("status", { name: "Active setup is read-only" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Edit Draft Sales Invoice Setup" })).not.toBeInTheDocument();

    rerender(<SalesInvoiceProfilesPage currentSite={siteA} client={makeClient({ getProfile: vi.fn(async () => ({ ...profile(), lifecycleState: "RETIRED", retiredAt: "2026-07-20T06:00:00Z" })) })} canManage />);
    await userEvent.click(await screen.findByRole("button", { name: "2026.01" }));
    expect(await screen.findByRole("status", { name: "Retired setup is read-only" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Edit Draft Sales Invoice Setup" })).not.toBeInTheDocument();
  });

  it("Cancel sends no request and Site switch with unsaved changes requires confirmation", async () => {
    const createFiscalIdentity = vi.fn(async (request: FiscalIdentityMutationRequest) => fiscalIdentityFromMutation("fiscal-created", request));
    const confirmSpy = vi.spyOn(window, "confirm").mockReturnValue(false);
    try {
      render(<App authState={authState(managePermissions)} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient({ createFiscalIdentity })} />);

      await userEvent.click(await screen.findByRole("button", { name: "Create Registered Business" }));
      const form = screen.getByRole("form", { name: "Create Registered Business" });
      await userEvent.type(within(form).getByLabelText(/Registered business name/i), "Unsaved Development Parking");
      await userEvent.click(within(form).getByRole("button", { name: "Cancel" }));
      expect(createFiscalIdentity).not.toHaveBeenCalled();

      await userEvent.click(screen.getByRole("button", { name: "Create Registered Business" }));
      await userEvent.type(within(screen.getByRole("form", { name: "Create Registered Business" })).getByLabelText(/Registered business name/i), "Unsaved Development Parking");
      await userEvent.selectOptions(screen.getByLabelText("Current Site"), siteB.siteId);
      expect(confirmSpy).toHaveBeenCalled();
      expect(screen.getByLabelText("Current Site")).toHaveValue(siteA.siteId);

      confirmSpy.mockReturnValue(true);
      await userEvent.selectOptions(screen.getByLabelText("Current Site"), siteB.siteId);
      expect(screen.getByLabelText("Current Site")).toHaveValue(siteB.siteId);
      expect(screen.queryByRole("form", { name: "Create Registered Business" })).not.toBeInTheDocument();
    } finally {
      confirmSpy.mockRestore();
    }
  });

  it("prevents duplicate mutation submission while pending", async () => {
    let resolveCreate: (value: SalesInvoiceHeaderProfile) => void = () => undefined;
    const pendingCreate = new Promise<SalesInvoiceHeaderProfile>((resolve) => { resolveCreate = resolve; });
    const createProfile = vi.fn((_request: SalesInvoiceHeaderProfileMutationRequest) => pendingCreate);
    render(<App authState={authState(managePermissions)} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient({ createProfile })} />);

    await userEvent.click(await screen.findByRole("button", { name: "Create Draft Sales Invoice Setup" }));
    const form = screen.getByRole("form", { name: "Create Draft Sales Invoice Setup" });
    await fillProfileForm(form);
    const submit = within(form).getByRole("button", { name: "Create Draft Sales Invoice Setup" });
    await userEvent.dblClick(submit);

    expect(createProfile).toHaveBeenCalledTimes(1);
    expect(submit).toBeDisabled();
    resolveCreate(profileFromMutation("profile-created", createProfile.mock.calls[0][0]));
    expect(await screen.findByRole("status", { name: "Draft Sales Invoice Setup created" })).toBeInTheDocument();
  });

  it("development manage scenarios expose Manage permission only in development mode", async () => {
    window.history.pushState({}, "", `${salesInvoiceProfileReadRoute}?mpScenario=authenticated&mpProfileScenario=manage`);
    const { unmount } = render(<App />);
    expect(screen.getByRole("status", { name: "Development profile scenario" })).toHaveTextContent("manage");
    expect(screen.getByRole("button", { name: "Create Registered Business" })).toBeInTheDocument();
    expect(await screen.findByRole("button", { name: "2026.01" })).toBeInTheDocument();
    expect(await screen.findByRole("status", { name: "Sales Invoice readiness result" })).toBeInTheDocument();

    unmount();
    window.history.pushState({}, "", `${salesInvoiceProfileReadRoute}?mpScenario=authenticated&mpProfileScenario=manage`);
    render(<App authState={authState()} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient()} developmentScenariosEnabled={false} profileScenariosEnabled={false} />);
    expect(screen.queryByRole("status", { name: "Development profile scenario" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Create Registered Business" })).not.toBeInTheDocument();
    expect(await screen.findByRole("button", { name: "2026.01" })).toBeInTheDocument();
  });
});

describe("Sales Invoice Setup activation and retirement workflows", () => {
  const approvePermissions = [managementPlatformOverviewPermission, futureSalesInvoiceProfilePermissions.read, futureSalesInvoiceProfilePermissions.approve];

  it("keeps approve authority separate from read and manage permissions", async () => {
    const { rerender } = render(<App authState={authState()} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient({ getProfile: vi.fn(async () => draftProfile()) })} />);
    await userEvent.click(await screen.findByRole("button", { name: "2026.01" }));
    expect(screen.queryByRole("button", { name: "Activate Sales Invoice Setup" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Retire Sales Invoice Setup" })).not.toBeInTheDocument();

    rerender(<App authState={authState([managementPlatformOverviewPermission, futureSalesInvoiceProfilePermissions.read, futureSalesInvoiceProfilePermissions.manage])} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient({ getProfile: vi.fn(async () => draftProfile()) })} />);
    await userEvent.click(await screen.findByRole("button", { name: "2026.01" }));
    expect(screen.getByRole("button", { name: "Edit Draft Sales Invoice Setup" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Activate Sales Invoice Setup" })).not.toBeInTheDocument();

    rerender(<App authState={authState(approvePermissions)} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient({ getProfile: vi.fn(async () => draftProfile()) })} />);
    await userEvent.click(await screen.findByRole("button", { name: "2026.01" }));
    expect(screen.queryByRole("button", { name: "Create Registered Business" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Edit Draft Sales Invoice Setup" })).not.toBeInTheDocument();
    expect(screen.getByRole("status", { name: "Validation required before activation" })).toBeInTheDocument();
  });

  it("requires complete authoritative validation before activating a Draft setup", async () => {
    const { rerender } = render(<App authState={authState(approvePermissions)} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient({ getProfile: vi.fn(async () => draftProfile()), validateProfile: vi.fn(async () => completeDraftValidation()) })} />);
    await userEvent.click(await screen.findByRole("button", { name: "2026.01" }));
    expect(screen.queryByRole("button", { name: "Activate Sales Invoice Setup" })).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Validate configuration" }));
    expect(await screen.findByRole("button", { name: "Activate Sales Invoice Setup" })).toBeInTheDocument();

    const incomplete = { ...completeDraftValidation(), isComplete: false, missingOrInvalidFieldCodes: ["BIR_ACCREDITATION_VALID_UNTIL_REQUIRED"] };
    rerender(<App authState={authState(approvePermissions)} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient({ getProfile: vi.fn(async () => draftProfile()), validateProfile: vi.fn(async () => incomplete) })} />);
    await userEvent.click(await screen.findByRole("button", { name: "2026.01" }));
    await userEvent.click(screen.getByRole("button", { name: "Validate configuration" }));
    expect(await screen.findByRole("status", { name: "Validation result" })).toHaveTextContent("Configuration completeness: Incomplete");
    expect(screen.queryByRole("button", { name: "Activate Sales Invoice Setup" })).not.toBeInTheDocument();
  });

  it("activates a complete Draft once, sends no actor field, and refreshes Active status", async () => {
    let resolveActivation: (value: SalesInvoiceHeaderProfile) => void = () => undefined;
    const pendingActivation = new Promise<SalesInvoiceHeaderProfile>((resolve) => { resolveActivation = resolve; });
    const approveProfile = vi.fn((_profileId: string) => pendingActivation);
    render(<App authState={authState(approvePermissions)} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient({ getProfile: vi.fn(async () => draftProfile()), validateProfile: vi.fn(async () => completeDraftValidation()), approveProfile })} />);

    await userEvent.click(await screen.findByRole("button", { name: "2026.01" }));
    await userEvent.click(screen.getByRole("button", { name: "Validate configuration" }));
    await userEvent.click(await screen.findByRole("button", { name: "Activate Sales Invoice Setup" }));

    const dialog = screen.getByRole("dialog", { name: "Activate Sales Invoice Setup?" });
    expect(dialog).toHaveTextContent("Setup version");
    expect(dialog).toHaveTextContent("Registered Business");
    expect(dialog).not.toHaveTextContent(/approvedByRef|actor ID|POS Server API key/i);

    await userEvent.dblClick(within(dialog).getByRole("button", { name: "Activate Sales Invoice Setup" }));
    expect(approveProfile).toHaveBeenCalledTimes(1);
    expect(approveProfile).toHaveBeenCalledWith("profile-draft-001", expect.any(AbortSignal));
    resolveActivation({ ...draftProfile("profile-draft-001"), lifecycleState: "APPROVED", approvedAt: "2026-07-20T06:00:00Z", updatedAt: "2026-07-20T06:00:00Z" });

    expect(await screen.findByRole("status", { name: "Sales Invoice Setup activated" })).toBeInTheDocument();
    expect((await screen.findAllByText("Active")).length).toBeGreaterThan(0);
    expect(document.body).not.toHaveTextContent("Mutation accepted");
  });

  it("keeps Draft on activation conflict and shows uncertain guidance on timeout without retry", async () => {
    const approveConflict = vi.fn(async () => {
      throw createUiError("conflict", "SALES_INVOICE_SETUP_ACTIVATION_CONFLICT", "The Sales Invoice Setup could not be activated.", "corr-activate-conflict", 409);
    });
    const { rerender } = render(<App authState={authState(approvePermissions)} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient({ getProfile: vi.fn(async () => draftProfile()), validateProfile: vi.fn(async () => completeDraftValidation()), approveProfile: approveConflict })} />);

    await userEvent.click(await screen.findByRole("button", { name: "2026.01" }));
    await userEvent.click(screen.getByRole("button", { name: "Validate configuration" }));
    await userEvent.click(await screen.findByRole("button", { name: "Activate Sales Invoice Setup" }));
    await userEvent.click(screen.getByRole("dialog", { name: "Activate Sales Invoice Setup?" }).querySelector("button")!);

    expect(await screen.findByRole("alert", { name: "Status change failed safely" })).toHaveTextContent("corr-activate-conflict");
    expect(screen.getAllByText("Draft").length).toBeGreaterThan(0);
    expect(approveConflict).toHaveBeenCalledTimes(1);

    const approveTimeout = vi.fn(async () => {
      throw createUiError("timeout", "SALES_INVOICE_SETUP_ACTIVATION_TIMEOUT", "Activation result is uncertain. Refresh and verify the authoritative status before trying again.", "corr-activate-timeout", 504, true, true);
    });
    rerender(<App authState={authState(approvePermissions)} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient({ getProfile: vi.fn(async () => draftProfile()), validateProfile: vi.fn(async () => completeDraftValidation()), approveProfile: approveTimeout })} />);
    await userEvent.click(await screen.findByRole("button", { name: "2026.01" }));
    await userEvent.click(screen.getByRole("button", { name: "Validate configuration" }));
    await userEvent.click(await screen.findByRole("button", { name: "Activate Sales Invoice Setup" }));
    await userEvent.click(screen.getByRole("dialog", { name: "Activate Sales Invoice Setup?" }).querySelector("button")!);

    expect(await screen.findByRole("status", { name: "Result uncertain" })).toHaveTextContent("Refresh and verify");
    expect(approveTimeout).toHaveBeenCalledTimes(1);
  });

  it("retires an Active setup once, preserves issuance history, and handles conflicts safely", async () => {
    const retireProfile = vi.fn(async (id: string) => ({ ...profile(id), lifecycleState: "RETIRED", retiredAt: "2026-07-20T06:30:00Z", updatedAt: "2026-07-20T06:30:00Z" }));
    const { rerender } = render(<App authState={authState(approvePermissions)} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient({ retireProfile })} />);

    await userEvent.click(await screen.findByRole("button", { name: "2026.01" }));
    await userEvent.click(await screen.findByRole("button", { name: "Retire Sales Invoice Setup" }));
    const dialog = screen.getByRole("dialog", { name: "Retire Sales Invoice Setup?" });
    expect(dialog).toHaveTextContent("Historical Sales Invoices");
    expect(dialog).not.toHaveTextContent(/retiredByRef|actor ID/i);
    expect(within(dialog).queryByRole("button", { name: /^Delete$/i })).not.toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole("button", { name: "Retire Sales Invoice Setup" }));

    await waitFor(() => expect(retireProfile).toHaveBeenCalledTimes(1));
    expect(await screen.findByRole("status", { name: "Sales Invoice Setup retired" })).toBeInTheDocument();
    expect(await screen.findByRole("heading", { name: "Issuance history" })).toBeInTheDocument();

    const retireConflict = vi.fn(async () => {
      throw createUiError("conflict", "SALES_INVOICE_SETUP_RETIREMENT_CONFLICT", "The Sales Invoice Setup could not be retired.", "corr-retire-conflict", 409);
    });
    rerender(<App authState={authState(approvePermissions)} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient({ retireProfile: retireConflict })} />);
    await userEvent.click(await screen.findByRole("button", { name: "2026.01" }));
    await userEvent.click(await screen.findByRole("button", { name: "Retire Sales Invoice Setup" }));
    await userEvent.click(screen.getByRole("dialog", { name: "Retire Sales Invoice Setup?" }).querySelector("button")!);

    expect(await screen.findByRole("alert", { name: "Status change failed safely" })).toHaveTextContent("corr-retire-conflict");
    expect(screen.getAllByText("Active").length).toBeGreaterThan(0);
    expect(retireConflict).toHaveBeenCalledTimes(1);
  });
});

describe("Sales Invoice Profile detail, validation, readiness, and usage", () => {
  it("renders setup detail, linked Registered Business, and distinct statutory date fields", async () => {
    render(<SalesInvoiceProfilesPage currentSite={siteA} client={makeClient()} />);

    await userEvent.click(await screen.findByRole("button", { name: "2026.01" }));

    expect(await screen.findByRole("heading", { name: "Sales Invoice Setup details" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Effective period and status history" })).toBeInTheDocument();
    expect(screen.queryByText("Effective and lifecycle metadata")).not.toBeInTheDocument();
    expect(screen.getAllByText("Development Parking Services").length).toBeGreaterThan(0);
    expect(screen.getByText("BIR accreditation issued date")).toBeInTheDocument();
    expect(screen.getByText("BIR accreditation valid-until date")).toBeInTheDocument();
    expect(screen.getByText("PTU issued date")).toBeInTheDocument();
    expect(screen.queryByText("Terminal ID")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Create|Edit|Activate|Retire|Delete|New Version/i })).not.toBeInTheDocument();
  });

  it("validates once while pending, displays Complete, and does not mutate lifecycle", async () => {
    let resolveValidation: (value: SalesInvoiceProfileValidationResult) => void = () => undefined;
    const pendingValidation = new Promise<SalesInvoiceProfileValidationResult>((resolve) => { resolveValidation = resolve; });
    const validateProfile = vi.fn(() => pendingValidation);
    render(<SalesInvoiceProfilesPage currentSite={siteA} client={makeClient({ validateProfile })} />);

    await userEvent.click(await screen.findByRole("button", { name: "2026.01" }));
    const validateButton = await screen.findByRole("button", { name: "Validate configuration" });
    await userEvent.dblClick(validateButton);

    expect(validateProfile).toHaveBeenCalledTimes(1);
    resolveValidation(completeValidation);
    expect(await screen.findByRole("status", { name: "Validation result" })).toHaveTextContent("Configuration completeness: Complete");
    expect(screen.getByRole("status", { name: "Validation result" })).toHaveTextContent("Status: Active");
  });

  it("displays Incomplete validation with grouped safe and unknown codes", async () => {
    const incompleteValidation = {
      ...completeValidation,
      isComplete: false,
      missingOrInvalidFieldCodes: ["FISCAL_IDENTITY_TIN_REQUIRED", "BIR_ACCREDITATION_VALID_UNTIL_REQUIRED", "PTU_ISSUED_DATE_REQUIRED", "EFFECTIVE_WINDOW_OVERLAP", "UNKNOWN_FUTURE_CODE"],
      messages: ["Unknown future validation code was preserved safely."]
    };
    render(<SalesInvoiceProfilesPage currentSite={siteA} client={makeClient({ validateProfile: vi.fn(async () => incompleteValidation) })} />);

    await userEvent.click(await screen.findByRole("button", { name: "2026.01" }));
    await userEvent.click(await screen.findByRole("button", { name: "Validate configuration" }));

    const result = await screen.findByRole("status", { name: "Validation result" });
    expect(result).toHaveTextContent("Configuration completeness: Incomplete");
    expect(result).toHaveTextContent("Registered Business");
    expect(result).toHaveTextContent("BIR accreditation");
    expect(result).toHaveTextContent("PTU");
    expect(result).toHaveTextContent("UNKNOWN_FUTURE_CODE");
  });

  it.each([
    ["READY", "Ready for Sales Invoice issuance"],
    ["NO_EFFECTIVE_PROFILE", "No effective Sales Invoice Setup"],
    ["INCOMPLETE", "Setup configuration is incomplete"],
    ["EXPIRED", "BIR accreditation or effective profile is expired"],
    ["AMBIGUOUS", "Multiple effective profiles require correction"],
    ["UNSUPPORTED_VERSION", "Template or presentation version is unsupported"],
    ["RETIRED", "Setup is retired and unavailable for new issuance"],
    ["FUTURE_REVIEW_REQUIRED", "Unknown readiness: FUTURE_REVIEW_REQUIRED"]
  ])("renders readiness status %s safely", async (status, text) => {
    render(<SalesInvoiceProfilesPage currentSite={siteA} client={makeClient({ getEffectiveReadiness: vi.fn(async () => readiness(status)) })} />);

    const readinessResult = await screen.findByRole("status", { name: "Sales Invoice readiness result" });
    expect(readinessResult).toHaveTextContent(text);
    if (status !== "READY") {
      expect(readinessResult).not.toHaveTextContent("Ready for Sales Invoice issuance");
    }
  });

  it("maps effectiveAt changes and usage aggregates without receipt payloads", async () => {
    const getEffectiveReadiness = vi.fn(async () => readiness());
    render(<SalesInvoiceProfilesPage currentSite={siteA} client={makeClient({ getEffectiveReadiness })} />);

    await userEvent.clear(screen.getByLabelText(/Effective at/i));
    await userEvent.type(screen.getByLabelText(/Effective at/i), "2026-08-01T10:15");
    await userEvent.click(await screen.findByRole("button", { name: "2026.01" }));

    await waitFor(() => expect(getEffectiveReadiness).toHaveBeenCalledWith(siteA, expect.stringContaining("2026"), expect.any(AbortSignal)));
    const usagePanel = await screen.findByRole("heading", { name: "Issuance history" });
    expect(usagePanel).toBeInTheDocument();
    expect(screen.getByText("Fiscal-document count")).toBeInTheDocument();
    expect(screen.getByText("SAFE-DOC-001")).toBeInTheDocument();
    expect(document.body).not.toHaveTextContent("receiptPayload");
    expect(document.body).not.toHaveTextContent("snapshotJson");
  });

  it.each([
    ["disabled", "Sales Invoice Configuration is not enabled for this environment."],
    ["forbidden", "You do not have permission for this Site scope."],
    ["unavailable", "Sales Invoice Configuration is unavailable."]
  ] as const)("development scenario %s renders safe errors", async (scenarioName, message) => {
    const scenario = resolveSalesInvoiceProfileReadScenario(true, `?mpProfileScenario=${scenarioName}`)!;
    render(<SalesInvoiceProfilesPage currentSite={siteA} client={scenario.client} developmentScenarioName={scenario.name} />);

    expect(screen.getByRole("status", { name: "Development profile scenario" })).toHaveTextContent(scenarioName);
    expect(await screen.findByRole("alert", { name: "Sales Invoice Setups unavailable" })).toHaveTextContent(message);
    expect(document.body).not.toHaveTextContent("stack trace");
    expect(document.body).not.toHaveTextContent("https://");
    expect(document.body).not.toHaveTextContent("token");
  });

  it("production mode ignores mpProfileScenario", () => {
    expect(resolveSalesInvoiceProfileReadScenario(false, "?mpProfileScenario=profiles")).toBeUndefined();
  });
});

describe("Sales Invoice Profile browser boundary", () => {
  it("does not use browser storage or direct downstream administration tokens", () => {
    const source = `${SalesInvoiceProfilesPage.toString()} ${createSalesInvoiceProfileReadClient.toString()}`;

    expect(source).not.toContain("localStorage");
    expect(source).not.toContain("sessionStorage");
    expect(source).not.toContain("IndexedDB");
    expect(source).not.toContain("X-PosServer-Admin-Key");
    expect(source).not.toContain("X-PosServer-Admin-Permission");
    expect(source).not.toContain("/v1/admin/");
  });

  it("uses accessible table headers, keyboard-selectable rows, and semantic section headings", async () => {
    render(<SalesInvoiceProfilesPage currentSite={siteA} client={makeClient()} />);

    expect(await screen.findByRole("columnheader", { name: "Setup version" })).toBeInTheDocument();
    await userEvent.tab();
    await userEvent.tab();
    expect(document.activeElement).toBe(screen.getByRole("button", { name: "2026.01" }));
    await userEvent.keyboard("{Enter}");
    expect(await screen.findByRole("heading", { name: "Registered Business" })).toBeInTheDocument();
    expect(within(screen.getByRole("status", { name: "Sales Invoice readiness result" })).getByText(/Ready for Sales Invoice issuance/i)).toBeInTheDocument();
  });
});
