import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { App } from "./App";
import { createUiError } from "./apiClient";
import { futureSalesInvoiceProfilePermissions, managementPlatformOverviewPermission } from "./permissions";
import { SalesInvoiceProfilesPage } from "./SalesInvoiceProfilesPage";
import { createSalesInvoiceProfileReadClient, resolveSalesInvoiceProfileReadScenario, salesInvoiceProfileReadRoute, type EffectiveReadinessResult, type FiscalIdentityDetail, type SalesInvoiceHeaderProfile, type SalesInvoiceHeaderProfileSummary, type SalesInvoiceProfileReadClient, type SalesInvoiceProfileUsageResult, type SalesInvoiceProfileValidationResult } from "./salesInvoiceProfiles";
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

function makeClient(overrides: Partial<SalesInvoiceProfileReadClient> = {}): SalesInvoiceProfileReadClient {
  return {
    listProfiles: vi.fn(async (site: ManagementPlatformSite) => [profileSummary(site)]),
    getProfile: vi.fn(async (id: string) => profile(id)),
    getFiscalIdentity: vi.fn(async () => fiscalIdentity),
    validateProfile: vi.fn(async () => completeValidation),
    getEffectiveReadiness: vi.fn(async () => readiness()),
    getProfileUsage: vi.fn(async () => usage),
    ...overrides
  };
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
});

describe("Sales Invoice Profile read-only route", () => {
  it("requires the read permission and does not expose mutation controls", async () => {
    const { rerender } = render(<App authState={authState([managementPlatformOverviewPermission])} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient()} />);
    expect(screen.getByRole("alert", { name: "Permission denied" })).toBeInTheDocument();

    rerender(<App authState={authState()} initialPath={salesInvoiceProfileReadRoute} salesInvoiceProfilesClient={makeClient()} />);
    expect(await screen.findByRole("heading", { name: "Read-only profile administration status" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Sales Invoice Profiles read-only status/i })).toBeInTheDocument();
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