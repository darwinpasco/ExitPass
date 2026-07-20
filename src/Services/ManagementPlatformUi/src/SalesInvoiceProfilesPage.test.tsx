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
  fireEvent.change(within(form).getByLabelText(/Fiscal Identity ID/i), { target: { value: "fiscal-001" } });
  fireEvent.change(within(form).getByLabelText(/Profile version/i), { target: { value: "2026.02" } });
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
    await client.getEffectiveReadiness(siteA, "2026-07-20T04:30:00Z");
    await client.getProfileUsage("profile-001");
    await client.getFiscalIdentity("fiscal-001");

    expect(calls.map((call) => call.path)).toEqual([
      "/v1/management-platform/sales-invoice-header-profiles?siteId=71000000-0000-0000-0000-000000000101&sitePosServerId=72000000-0000-0000-0000-000000000101",
      "/v1/management-platform/sales-invoice-header-profiles/profile-001",
      "/v1/management-platform/sales-invoice-header-profiles/profile-001/validate",
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
    expect(await screen.findByRole("heading", { name: "Profile administration status" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Sales Invoice Profiles administration status/i })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Create|Edit|Approve|Retire|Delete|New Version/i })).not.toBeInTheDocument();
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
    expect(await screen.findByRole("status", { name: "No profiles" })).toBeInTheDocument();

    rerender(<SalesInvoiceProfilesPage currentSite={siteA} client={makeClient({ listProfiles: vi.fn(async () => { throw createUiError("permission-denied", "SITE_SCOPE_DENIED", "You do not have permission for this Site scope.", "corr-forbidden", 403); }) })} />);
    expect(await screen.findByRole("alert", { name: "Profile list unavailable" })).toHaveTextContent("corr-forbidden");
  });
});

describe("Sales Invoice Profile Manage-only workflows", () => {
  const managePermissions = [managementPlatformOverviewPermission, futureSalesInvoiceProfilePermissions.read, futureSalesInvoiceProfilePermissions.manage];

  it("shows create controls only to Manage-authorized users and unrelated permissions grant no mutation access", async () => {
    const { rerender } = render(<App authState={authState()} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient()} />);
    expect(await screen.findByRole("heading", { name: "Profile administration status" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Create Fiscal Identity" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Create Draft Profile" })).not.toBeInTheDocument();

    rerender(<App authState={authState([managementPlatformOverviewPermission, futureSalesInvoiceProfilePermissions.read, "unrelated.permission"])} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient()} />);
    expect(screen.queryByRole("button", { name: "Create Fiscal Identity" })).not.toBeInTheDocument();

    rerender(<App authState={authState(managePermissions)} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient()} />);
    expect(await screen.findByRole("button", { name: "Create Fiscal Identity" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Create Draft Profile" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Approve|Retire|Delete|New Version/i })).not.toBeInTheDocument();
  });

  it("creates a Fiscal Identity once with no actor fields and refreshes authoritative detail", async () => {
    const createFiscalIdentity = vi.fn(async (request: FiscalIdentityMutationRequest) => fiscalIdentityFromMutation("fiscal-created", request));
    render(<App authState={authState(managePermissions)} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient({ createFiscalIdentity })} />);

    await userEvent.click(await screen.findByRole("button", { name: "Create Fiscal Identity" }));
    const form = screen.getByRole("form", { name: "Create Fiscal Identity" });
    expect(form).not.toHaveTextContent("createdByRef");
    expect(form).not.toHaveTextContent("updatedByRef");
    await fillFiscalIdentityForm(form);
    await userEvent.click(within(form).getByRole("button", { name: "Create Fiscal Identity" }));

    await waitFor(() => expect(createFiscalIdentity).toHaveBeenCalledTimes(1));
    const submitted = createFiscalIdentity.mock.calls[0][0];
    expect(submitted).toMatchObject({ registeredBusinessName: "Managed Development Parking", tin: "MANAGED-TIN-001" });
    expect(JSON.stringify(submitted)).not.toMatch(/createdByRef|updatedByRef|approvedByRef|retiredByRef/i);
    expect(await screen.findByText(/Fiscal Identity created from authoritative response/i)).toBeInTheDocument();
  });

  it("keeps Fiscal Identity form data on governed conflict and shows timeout uncertainty without retry", async () => {
    const conflict = vi.fn(async () => {
      throw createUiError("conflict", "FISCAL_IDENTITY_IMMUTABLE", "The Fiscal Identity is already used and cannot be changed.", "corr-conflict", 409);
    });
    const { rerender } = render(<App authState={authState(managePermissions)} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient({ createFiscalIdentity: conflict })} />);

    await userEvent.click(await screen.findByRole("button", { name: "Create Fiscal Identity" }));
    const conflictForm = screen.getByRole("form", { name: "Create Fiscal Identity" });
    await fillFiscalIdentityForm(conflictForm);
    await userEvent.click(within(conflictForm).getByRole("button", { name: "Create Fiscal Identity" }));

    expect(await screen.findByRole("alert", { name: "Mutation failed safely" })).toHaveTextContent("corr-conflict");
    expect(within(conflictForm).getByLabelText(/Registered business name/i)).toHaveValue("Managed Development Parking");
    expect(conflict).toHaveBeenCalledTimes(1);

    const timeout = vi.fn(async () => {
      throw createUiError("timeout", "PROFILE_CREATE_TIMEOUT", "The request timed out. Refresh and verify the authoritative state before trying again.", "corr-timeout", 504, true, true);
    });
    rerender(<App authState={authState(managePermissions)} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient({ createProfile: timeout })} />);
    await userEvent.click(await screen.findByRole("button", { name: "Create Draft Profile" }));
    const timeoutForm = screen.getByRole("form", { name: "Create Draft Profile" });
    await fillProfileForm(timeoutForm);
    await userEvent.click(within(timeoutForm).getByRole("button", { name: "Create Draft Profile" }));

    expect(await screen.findByRole("status", { name: "Mutation result uncertain" })).toHaveTextContent("Refresh and verify");
    expect(timeout).toHaveBeenCalledTimes(1);
  });

  it("creates a DRAFT Header Profile with controlled versions, Site scope, and no terminal or actor fields", async () => {
    const createProfile = vi.fn(async (request: SalesInvoiceHeaderProfileMutationRequest) => profileFromMutation("profile-created", request));
    render(<App authState={authState(managePermissions)} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient({ createProfile })} />);

    await userEvent.click(await screen.findByRole("button", { name: "Create Draft Profile" }));
    const form = screen.getByRole("form", { name: "Create Draft Profile" });
    expect(within(form).getByLabelText(/Template version/i)).toHaveValue(controlledSalesInvoiceTemplateVersion);
    expect(within(form).getByLabelText(/Presentation version/i)).toHaveValue(controlledSalesInvoicePresentationVersion);
    expect(within(form).getByLabelText(/Site ID/i)).toHaveValue(siteA.siteId);
    expect(within(form).getByLabelText(/Site POS Server ID/i)).toHaveValue(siteA.sitePosServerId);
    expect(form).not.toHaveTextContent("Terminal ID");
    expect(form).not.toHaveTextContent("approvedByRef");
    expect(form).not.toHaveTextContent("retiredByRef");

    await fillProfileForm(form);
    await userEvent.click(within(form).getByRole("button", { name: "Create Draft Profile" }));

    await waitFor(() => expect(createProfile).toHaveBeenCalledTimes(1));
    const submitted = createProfile.mock.calls[0][0];
    expect(submitted).toMatchObject({
      siteId: siteA.siteId,
      sitePosServerId: siteA.sitePosServerId,
      templateVersion: controlledSalesInvoiceTemplateVersion,
      presentationVersion: controlledSalesInvoicePresentationVersion
    });
    expect(JSON.stringify(submitted)).not.toMatch(/terminalId|createdByRef|updatedByRef|approvedByRef|retiredByRef|lifecycleState/i);
    expect(await screen.findByText(/Draft profile created from authoritative response/i)).toBeInTheDocument();
  });

  it("edits only DRAFT profiles and keeps APPROVED or RETIRED profiles read-only", async () => {
    const updateDraftProfile = vi.fn(async (id: string, request: SalesInvoiceHeaderProfileMutationRequest) => profileFromMutation(id, request));
    const { rerender } = render(<SalesInvoiceProfilesPage currentSite={siteA} client={makeClient({ getProfile: vi.fn(async () => draftProfile()), updateDraftProfile })} canManage />);

    await userEvent.click(await screen.findByRole("button", { name: "2026.01" }));
    await userEvent.click(await screen.findByRole("button", { name: "Edit Draft Profile" }));
    const form = screen.getByRole("form", { name: "Save Draft Changes" });
    await userEvent.clear(within(form).getByLabelText(/Parking-location display/i));
    await userEvent.type(within(form).getByLabelText(/Parking-location display/i), "Updated Development Parking");
    await userEvent.click(within(form).getByRole("button", { name: "Save Draft Changes" }));

    await waitFor(() => expect(updateDraftProfile).toHaveBeenCalledTimes(1));
    expect(updateDraftProfile.mock.calls[0][0]).toBe("profile-draft-001");
    expect(updateDraftProfile.mock.calls[0][1]).toMatchObject({ parkingLocationDisplay: "Updated Development Parking" });

    rerender(<SalesInvoiceProfilesPage currentSite={siteA} client={makeClient({ getProfile: vi.fn(async () => profile()) })} canManage />);
    await userEvent.click(await screen.findByRole("button", { name: "2026.01" }));
    expect(await screen.findByRole("status", { name: "Approved profile is read-only" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Edit Draft Profile" })).not.toBeInTheDocument();

    rerender(<SalesInvoiceProfilesPage currentSite={siteA} client={makeClient({ getProfile: vi.fn(async () => ({ ...profile(), lifecycleState: "RETIRED", retiredAt: "2026-07-20T06:00:00Z" })) })} canManage />);
    await userEvent.click(await screen.findByRole("button", { name: "2026.01" }));
    expect(await screen.findByRole("status", { name: "Retired profile is read-only" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Edit Draft Profile" })).not.toBeInTheDocument();
  });

  it("Cancel sends no request and Site switch with unsaved changes requires confirmation", async () => {
    const createFiscalIdentity = vi.fn(async (request: FiscalIdentityMutationRequest) => fiscalIdentityFromMutation("fiscal-created", request));
    const confirmSpy = vi.spyOn(window, "confirm").mockReturnValue(false);
    try {
      render(<App authState={authState(managePermissions)} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient({ createFiscalIdentity })} />);

      await userEvent.click(await screen.findByRole("button", { name: "Create Fiscal Identity" }));
      const form = screen.getByRole("form", { name: "Create Fiscal Identity" });
      await userEvent.type(within(form).getByLabelText(/Registered business name/i), "Unsaved Development Parking");
      await userEvent.click(within(form).getByRole("button", { name: "Cancel" }));
      expect(createFiscalIdentity).not.toHaveBeenCalled();

      await userEvent.click(screen.getByRole("button", { name: "Create Fiscal Identity" }));
      await userEvent.type(within(screen.getByRole("form", { name: "Create Fiscal Identity" })).getByLabelText(/Registered business name/i), "Unsaved Development Parking");
      await userEvent.selectOptions(screen.getByLabelText("Current Site"), siteB.siteId);
      expect(confirmSpy).toHaveBeenCalled();
      expect(screen.getByLabelText("Current Site")).toHaveValue(siteA.siteId);

      confirmSpy.mockReturnValue(true);
      await userEvent.selectOptions(screen.getByLabelText("Current Site"), siteB.siteId);
      expect(screen.getByLabelText("Current Site")).toHaveValue(siteB.siteId);
      expect(screen.queryByRole("form", { name: "Create Fiscal Identity" })).not.toBeInTheDocument();
    } finally {
      confirmSpy.mockRestore();
    }
  });

  it("prevents duplicate mutation submission while pending", async () => {
    let resolveCreate: (value: SalesInvoiceHeaderProfile) => void = () => undefined;
    const pendingCreate = new Promise<SalesInvoiceHeaderProfile>((resolve) => { resolveCreate = resolve; });
    const createProfile = vi.fn((_request: SalesInvoiceHeaderProfileMutationRequest) => pendingCreate);
    render(<App authState={authState(managePermissions)} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient({ createProfile })} />);

    await userEvent.click(await screen.findByRole("button", { name: "Create Draft Profile" }));
    const form = screen.getByRole("form", { name: "Create Draft Profile" });
    await fillProfileForm(form);
    const submit = within(form).getByRole("button", { name: "Create Draft Profile" });
    await userEvent.dblClick(submit);

    expect(createProfile).toHaveBeenCalledTimes(1);
    expect(submit).toBeDisabled();
    resolveCreate(profileFromMutation("profile-created", createProfile.mock.calls[0][0]));
    expect(await screen.findByText(/Draft profile created from authoritative response/i)).toBeInTheDocument();
  });

  it("development manage scenarios expose Manage permission only in development mode", async () => {
    window.history.pushState({}, "", `${salesInvoiceProfileReadRoute}?mpScenario=authenticated&mpProfileScenario=manage`);
    const { unmount } = render(<App />);
    expect(screen.getByRole("status", { name: "Development profile scenario" })).toHaveTextContent("manage");
    expect(screen.getByRole("button", { name: "Create Fiscal Identity" })).toBeInTheDocument();
    expect(await screen.findByRole("button", { name: "2026.01" })).toBeInTheDocument();
    expect(await screen.findByRole("status", { name: "Effective readiness result" })).toBeInTheDocument();

    unmount();
    window.history.pushState({}, "", `${salesInvoiceProfileReadRoute}?mpScenario=authenticated&mpProfileScenario=manage`);
    render(<App authState={authState()} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient()} developmentScenariosEnabled={false} profileScenariosEnabled={false} />);
    expect(screen.queryByRole("status", { name: "Development profile scenario" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Create Fiscal Identity" })).not.toBeInTheDocument();
    expect(await screen.findByRole("button", { name: "2026.01" })).toBeInTheDocument();
  });
});

describe("Sales Invoice Profile detail, validation, readiness, and usage", () => {
  it("renders profile detail, linked Fiscal Identity, and distinct statutory date fields", async () => {
    render(<SalesInvoiceProfilesPage currentSite={siteA} client={makeClient()} />);

    await userEvent.click(await screen.findByRole("button", { name: "2026.01" }));

    expect(await screen.findByRole("heading", { name: "Profile detail" })).toBeInTheDocument();
    expect(screen.getAllByText("Development Parking Services").length).toBeGreaterThan(0);
    expect(screen.getByText("BIR accreditation issued date")).toBeInTheDocument();
    expect(screen.getByText("BIR accreditation valid-until date")).toBeInTheDocument();
    expect(screen.getByText("PTU issued date")).toBeInTheDocument();
    expect(screen.queryByText("Terminal ID")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Create|Edit|Approve|Retire|Delete|New Version/i })).not.toBeInTheDocument();
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
    expect(screen.getByRole("status", { name: "Validation result" })).toHaveTextContent("Lifecycle: APPROVED");
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
    expect(result).toHaveTextContent("Fiscal Identity");
    expect(result).toHaveTextContent("BIR accreditation");
    expect(result).toHaveTextContent("PTU");
    expect(result).toHaveTextContent("UNKNOWN_FUTURE_CODE");
  });

  it.each([
    ["READY", "Ready for Sales Invoice issuance"],
    ["NO_EFFECTIVE_PROFILE", "No effective Sales Invoice Header Profile"],
    ["INCOMPLETE", "Profile configuration is incomplete"],
    ["EXPIRED", "BIR accreditation or effective profile is expired"],
    ["AMBIGUOUS", "Multiple effective profiles require correction"],
    ["UNSUPPORTED_VERSION", "Template or presentation version is unsupported"],
    ["RETIRED", "Profile is retired and unavailable for new issuance"],
    ["FUTURE_REVIEW_REQUIRED", "Unknown readiness: FUTURE_REVIEW_REQUIRED"]
  ])("renders readiness status %s safely", async (status, text) => {
    render(<SalesInvoiceProfilesPage currentSite={siteA} client={makeClient({ getEffectiveReadiness: vi.fn(async () => readiness(status)) })} />);

    const readinessResult = await screen.findByRole("status", { name: "Effective readiness result" });
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
    const usagePanel = await screen.findByRole("heading", { name: "Immutable usage" });
    expect(usagePanel).toBeInTheDocument();
    expect(screen.getByText("Fiscal-document count")).toBeInTheDocument();
    expect(screen.getByText("SAFE-DOC-001")).toBeInTheDocument();
    expect(document.body).not.toHaveTextContent("receiptPayload");
    expect(document.body).not.toHaveTextContent("snapshotJson");
  });

  it.each([
    ["disabled", "Sales Invoice Profile administration is not enabled for this environment."],
    ["forbidden", "You do not have permission for this Site scope."],
    ["unavailable", "Profile administration is unavailable."]
  ] as const)("development scenario %s renders safe errors", async (scenarioName, message) => {
    const scenario = resolveSalesInvoiceProfileReadScenario(true, `?mpProfileScenario=${scenarioName}`)!;
    render(<SalesInvoiceProfilesPage currentSite={siteA} client={scenario.client} developmentScenarioName={scenario.name} />);

    expect(screen.getByRole("status", { name: "Development profile scenario" })).toHaveTextContent(scenarioName);
    expect(await screen.findByRole("alert", { name: "Profile list unavailable" })).toHaveTextContent(message);
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

    expect(await screen.findByRole("columnheader", { name: "Profile" })).toBeInTheDocument();
    await userEvent.tab();
    await userEvent.tab();
    expect(document.activeElement).toBe(screen.getByRole("button", { name: "2026.01" }));
    await userEvent.keyboard("{Enter}");
    expect(await screen.findByRole("heading", { name: "Registered business" })).toBeInTheDocument();
    expect(within(screen.getByRole("status", { name: "Effective readiness result" })).getByText(/Ready for Sales Invoice issuance/i)).toBeInTheDocument();
  });
});
