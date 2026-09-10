import { useEffect, useMemo, useState } from "react";
import { createCentralPmsApiClient } from "./apiClient";
import { createDevelopmentAuthState } from "./auth";
import { getManagementPlatformConfig } from "./config";
import { FiscalExceptionReportPage } from "./FiscalExceptionReportPage";
import { FiscalReportingPage } from "./FiscalReportingPage";
import { createFiscalReportingClient, createFiscalReportingFixtureClient, fiscalReportingPermissions, fiscalReportingRoute, type FiscalReportingClient } from "./fiscalReporting";
import {
  createFiscalExceptionReportingClient,
  fiscalExceptionReportPermission,
  fiscalExceptionReportingRoute,
  resolveFiscalExceptionReportScenario,
  type FiscalExceptionReportClient,
  type FiscalReportScope
} from "./fiscalExceptionReporting";
import { resolveManagementPlatformManualScenario, type ManagementPlatformManualScenarioName } from "./manualScenarios";
import { managementPlatformOverviewPermission, futureSalesInvoiceProfilePermissions, hasPermission } from "./permissions";
import { SalesInvoiceProfilesPage } from "./SalesInvoiceProfilesPage";
import { createSalesInvoiceProfileReadClient, resolveSalesInvoiceProfileReadScenario, salesInvoiceProfileReadRoute, type SalesInvoiceProfileClient } from "./salesInvoiceProfiles";
import { useManagementPlatformSiteSelection } from "./siteContext";
import type { ManagementPlatformAuthState, ManagementPlatformConfig, ManagementPlatformUiError } from "./types";

const routes = {
  root: "/management-platform",
  overview: "/management-platform/overview",
  salesInvoiceProfiles: salesInvoiceProfileReadRoute,
  fiscalExceptions: fiscalExceptionReportingRoute,
  fiscalReporting: fiscalReportingRoute
};

interface AppProps {
  authState?: ManagementPlatformAuthState;
  initialPath?: string;
  config?: ManagementPlatformConfig;
  salesInvoiceProfilesClient?: SalesInvoiceProfileClient;
  fiscalExceptionClient?: FiscalExceptionReportClient;
  fiscalReportingClient?: FiscalReportingClient;
  developmentScenariosEnabled?: boolean;
  profileScenariosEnabled?: boolean;
  fiscalScenariosEnabled?: boolean;
}

export function App({
  authState,
  initialPath,
  config,
  salesInvoiceProfilesClient,
  fiscalExceptionClient,
  fiscalReportingClient,
  developmentScenariosEnabled = import.meta.env.DEV,
  profileScenariosEnabled = import.meta.env.DEV,
  fiscalScenariosEnabled = import.meta.env.DEV
}: AppProps) {
  const resolvedConfig = useMemo(() => config ?? getManagementPlatformConfig(), [config]);
  const manualScenario = useMemo(
    () => authState ? undefined : resolveManagementPlatformManualScenario(developmentScenariosEnabled, window.location.search),
    [authState, developmentScenariosEnabled]
  );
  const profileScenario = useMemo(
    () => salesInvoiceProfilesClient ? undefined : resolveSalesInvoiceProfileReadScenario(profileScenariosEnabled, window.location.search),
    [salesInvoiceProfilesClient, profileScenariosEnabled]
  );
  const centralPmsClient = useMemo(
    () => createCentralPmsApiClient({ basePath: resolvedConfig.centralPmsApiBasePath }),
    [resolvedConfig.centralPmsApiBasePath]
  );
  const profileClient = useMemo(
    () => salesInvoiceProfilesClient ?? profileScenario?.client ?? createSalesInvoiceProfileReadClient(centralPmsClient),
    [salesInvoiceProfilesClient, profileScenario?.client, centralPmsClient]
  );
  const fiscalScenario = useMemo(
    () => fiscalExceptionClient ? undefined : resolveFiscalExceptionReportScenario(fiscalScenariosEnabled, window.location.search),
    [fiscalExceptionClient, fiscalScenariosEnabled]
  );
  const fiscalClient = useMemo(
    () => fiscalExceptionClient ?? fiscalScenario?.client ?? createFiscalExceptionReportingClient(centralPmsClient),
    [fiscalExceptionClient, fiscalScenario?.client, centralPmsClient]
  );
  const governedFiscalClient = useMemo(() => fiscalReportingClient ?? (
    import.meta.env.DEV && new URLSearchParams(window.location.search).get("mpFiscalReportingScenario") === "ready"
      ? createFiscalReportingFixtureClient()
      : createFiscalReportingClient(centralPmsClient)
  ), [fiscalReportingClient, centralPmsClient]);
  const state = authState ?? manualScenario?.authState ?? createDevelopmentAuthState();
  const scenarioInitialPath = authState ? undefined : manualScenario?.initialPath;
  const [path, setPath] = useState(initialPath ?? scenarioInitialPath ?? normalizePath(window.location.pathname));
  const [salesInvoiceFormState, setSalesInvoiceFormState] = useState({ hasUnsavedChanges: false, mutationPending: false });
  const scenarioIndicator = manualScenario?.showIndicator
    ? <DevelopmentScenarioIndicator scenarioName={manualScenario.name} />
    : null;

  useEffect(() => {
    document.title = routeTitle(path);
  }, [path]);

  useEffect(() => {
    if (initialPath || scenarioInitialPath) {
      return;
    }

    const handlePopState = () => setPath(normalizePath(window.location.pathname));
    window.addEventListener("popstate", handlePopState);
    return () => window.removeEventListener("popstate", handlePopState);
  }, [initialPath, scenarioInitialPath]);

  function navigate(nextPath: string) {
    setPath(nextPath);
    if (!initialPath && !scenarioInitialPath) {
      window.history.pushState({}, "", nextPath);
    }
  }

  if (state.status === "loading") {
    return <>{scenarioIndicator}<LoadingState message="Loading Management Platform access" /></>;
  }

  if (state.status === "error") {
    return <>{scenarioIndicator}<PageError error={{ kind: "unknown", code: "MANAGEMENT_PLATFORM_AUTH_CONTEXT_UNAVAILABLE", message: state.message ?? "Management Platform access could not be resolved safely.", retryable: false, mutationUncertain: false }} /></>;
  }

  if (state.status === "unauthenticated" || !state.principal?.authenticated) {
    return <>{scenarioIndicator}<AuthenticationRequired /></>;
  }

  const principal = state.principal;
  const siteSelection = useManagementPlatformSiteSelection(principal.authorizedSites);
  const canViewOverview = hasPermission(principal.permissions, managementPlatformOverviewPermission);
  const canReadSalesInvoiceProfiles = hasPermission(principal.permissions, futureSalesInvoiceProfilePermissions.read);
  const canManageSalesInvoiceProfiles = hasPermission(principal.permissions, futureSalesInvoiceProfilePermissions.manage);
  const canApproveSalesInvoiceProfiles = hasPermission(principal.permissions, futureSalesInvoiceProfilePermissions.approve);
  const canViewFiscalExceptions = hasPermission(principal.permissions, fiscalExceptionReportPermission);
  const canViewFiscalReporting = Object.values(fiscalReportingPermissions).some(permission => hasPermission(principal.permissions, permission));
  const fiscalReportScopes: FiscalReportScope[] = principal.authorizedReportScopes?.length
    ? principal.authorizedReportScopes
    : principal.authorizedSites.map((site) => ({ scopeType: "SITE", scopeReference: site.siteId, displayName: site.displayName }));
  const isKnownRoute = path === routes.root || path === routes.overview || path === routes.salesInvoiceProfiles || path === routes.fiscalExceptions || path === routes.fiscalReporting;
  const shellProps = {
    principalName: principal.displayName,
    siteSelection,
    path,
    navigate,
    canViewOverview,
    canReadSalesInvoiceProfiles,
    canViewFiscalExceptions,
    canViewFiscalReporting,
    salesInvoiceFormState,
    environmentName: resolvedConfig.environmentName,
    scenarioIndicator
  };

  if (!isKnownRoute) {
    return <Shell {...shellProps}><NotFound /></Shell>;
  }

  if ((path === routes.root || path === routes.overview) && !canViewOverview) {
    return <Shell {...shellProps}><PermissionDenied /></Shell>;
  }

  if (path === routes.salesInvoiceProfiles && !canReadSalesInvoiceProfiles) {
    return <Shell {...shellProps}><PermissionDenied /></Shell>;
  }

  if (path === routes.fiscalExceptions && !canViewFiscalExceptions) {
    return <Shell {...shellProps}><PermissionDenied /></Shell>;
  }
  if (path === routes.fiscalReporting && !canViewFiscalReporting) return <Shell {...shellProps}><PermissionDenied /></Shell>;

  if (manualScenario?.error) {
    return <Shell {...shellProps}><PageError error={manualScenario.error} /></Shell>;
  }

  if (path === routes.salesInvoiceProfiles) {
    return (
      <Shell {...shellProps}>
        <SalesInvoiceProfilesPage
          currentSite={siteSelection.currentSite}
          client={profileClient}
          developmentScenarioName={profileScenario?.name}
          canManage={canManageSalesInvoiceProfiles}
          canApprove={canApproveSalesInvoiceProfiles}
          onFormStateChange={setSalesInvoiceFormState}
        />
      </Shell>
    );
  }

  if (path === routes.fiscalExceptions) {
    return <Shell {...shellProps}><FiscalExceptionReportPage client={fiscalClient} availableScopes={fiscalReportScopes} /></Shell>;
  }
  if (path === routes.fiscalReporting) return <Shell {...shellProps}><FiscalReportingPage client={governedFiscalClient} sites={principal.authorizedSites} scopes={fiscalReportScopes} permissions={principal.permissions} /></Shell>;

  return (
    <Shell {...shellProps}>
      <OverviewPage subjectRef={principal.subjectRef} currentSiteName={siteSelection.currentSite?.displayName} hasSites={siteSelection.hasSites} />
    </Shell>
  );
}

function Shell({ principalName, siteSelection, path, navigate, canViewOverview, canReadSalesInvoiceProfiles, canViewFiscalExceptions, canViewFiscalReporting, salesInvoiceFormState, environmentName, scenarioIndicator, children }: {
  principalName?: string;
  siteSelection: ReturnType<typeof useManagementPlatformSiteSelection>;
  path: string;
  navigate: (path: string) => void;
  canViewOverview: boolean;
  canReadSalesInvoiceProfiles: boolean;
  canViewFiscalExceptions: boolean;
  canViewFiscalReporting: boolean;
  salesInvoiceFormState: { hasUnsavedChanges: boolean; mutationPending: boolean };
  environmentName: string;
  scenarioIndicator?: React.ReactNode;
  children: React.ReactNode;
}) {
  return (
    <main className="appShell" aria-labelledby="app-title">
      <header className="appHeader">
        <div>
          <p className="eyebrow">Management Platform</p>
          <h1 id="app-title">ExitPass Management Platform</h1>
          <p className="headerCopy">Administrative control plane for governed configuration and lifecycle workflows.</p>
        </div>
        <div className="identityPanel" aria-label="Authenticated Management Platform user">
          <span>User</span>
          <strong>{principalName ?? "Authenticated user"}</strong>
          <small>{environmentName}</small>
        </div>
      </header>

      {scenarioIndicator}

      <section className="platformShell">
        <aside className="moduleRail" aria-label="Management Platform navigation">
          <div className="panelHeader">
            <p className="eyebrow">Workspace</p>
            <h2>Navigation</h2>
          </div>
          <nav aria-label="Management Platform routes">
            {canViewOverview && (
              <button className={`navLink ${path === routes.root || path === routes.overview ? "navLinkActive" : ""}`} type="button" onClick={() => navigate(routes.overview)}>
                Overview
              </button>
            )}
            {canReadSalesInvoiceProfiles && (
              <button className={`navLink ${path === routes.salesInvoiceProfiles ? "navLinkActive" : ""}`} type="button" onClick={() => navigate(routes.salesInvoiceProfiles)}>
                Sales Invoice Configuration <span className="navMeta">Sales Invoice Setups</span>
              </button>
            )}
            {canViewFiscalExceptions && (
              <button className={`navLink ${path === routes.fiscalExceptions ? "navLinkActive" : ""}`} type="button" onClick={() => navigate(routes.fiscalExceptions)}>
                Fiscal exceptions <span className="navMeta">Sales Invoice reporting</span>
              </button>
            )}
            {canViewFiscalReporting && <button className={`navLink ${path === routes.fiscalReporting ? "navLinkActive" : ""}`} type="button" onClick={() => navigate(routes.fiscalReporting)}>Fiscal Reporting <span className="navMeta">EJ / X / Z</span></button>}
          </nav>
          <SiteSelector siteSelection={siteSelection} formState={salesInvoiceFormState} />
        </aside>

        <section className="workspace" aria-live="polite">
          {children}
        </section>
      </section>
    </main>
  );
}

function DevelopmentScenarioIndicator({ scenarioName }: { scenarioName: ManagementPlatformManualScenarioName }) {
  return (
    <div className="developmentScenario" role="status" aria-label="Development scenario">
      Development scenario: <strong>{scenarioName}</strong>
    </div>
  );
}

function SiteSelector({ siteSelection, formState }: {
  siteSelection: ReturnType<typeof useManagementPlatformSiteSelection>;
  formState: { hasUnsavedChanges: boolean; mutationPending: boolean };
}) {
  if (!siteSelection.hasSites) {
    return <StateMessage title="No authorized Sites" message="No Sites are currently available for your Management Platform permissions." tone="warning" />;
  }

  return (
    <div className="sitePanel">
      <label htmlFor="site-selector">Current Site</label>
      <select
        id="site-selector"
        value={siteSelection.currentSite?.siteId ?? ""}
        disabled={formState.mutationPending}
        onChange={(event) => {
          if (formState.hasUnsavedChanges && !window.confirm("Discard unsaved Sales Invoice Setup changes before switching Site?")) {
            event.currentTarget.value = siteSelection.currentSite?.siteId ?? "";
            return;
          }

          siteSelection.switchSite(event.target.value);
        }}
      >
        {siteSelection.sites.map((site) => (
          <option key={site.siteId} value={site.siteId}>{site.displayName}</option>
        ))}
      </select>
      <p>{siteSelection.currentSite?.sitePosServerId ? `Site POS Server: ${siteSelection.currentSite.sitePosServerId}` : "Site POS Server not selected"}</p>
    </div>
  );
}

function OverviewPage({ subjectRef, currentSiteName, hasSites }: { subjectRef?: string; currentSiteName?: string; hasSites: boolean }) {
  return (
    <section className="panel" aria-labelledby="overview-title">
      <div className="pageTitle">
        <div>
          <p className="eyebrow">Overview</p>
          <h2 id="overview-title">Management Platform foundation</h2>
        </div>
        <span className="statusPill">Foundation ready</span>
      </div>
      <div className="overviewGrid">
        <article>
          <h3>Purpose</h3>
          <p>Administrative modules will appear here as they are enabled for your permissions and Site scope.</p>
        </article>
        <article>
          <h3>Access posture</h3>
          <p>Authenticated as {subjectRef ?? "a governed Management Platform principal"}.</p>
        </article>
        <article>
          <h3>Site context</h3>
          <p>{hasSites ? `Current Site: ${currentSiteName}` : "No authorized Site is available."}</p>
        </article>
      </div>
      <StateMessage title="Administrative modules" message="Sales Invoice Configuration is available to read-authorized users. This shell does not edit fiscal data, issue documents, print receipts, authorize exits, or operate gates." />
    </section>
  );
}

export function AuthenticationRequired() {
  return <StateMessage title="Authentication required" message="Sign in with a Management Platform account to continue." tone="warning" />;
}

export function PermissionDenied() {
  return <StateMessage title="Permission denied" message="Your account is authenticated but does not have permission for this Management Platform route." tone="danger" />;
}

export function NotFound() {
  return <StateMessage title="Management Platform route not found" message="The requested Management Platform route does not exist." />;
}

export function LoadingState({ message }: { message: string }) {
  return <StateMessage title="Loading" message={message} tone="neutral" />;
}

export function PageError({ error }: { error: ManagementPlatformUiError }) {
  return <StateMessage title="Management Platform error" message={`${error.message}${error.correlationId ? ` Support reference: ${error.correlationId}.` : ""}`} tone="danger" />;
}

export function MutationUncertainMessage({ correlationId }: { correlationId?: string }) {
  return <StateMessage title="Result uncertain" message={`Refresh and verify the authoritative state before trying again.${correlationId ? ` Support reference: ${correlationId}.` : ""}`} tone="warning" />;
}

export function FeatureUnavailable() {
  return <StateMessage title="Feature unavailable" message="This administrative feature is not enabled for this environment." tone="warning" />;
}

function StateMessage({ title, message, tone = "neutral" }: { title: string; message: string; tone?: "neutral" | "warning" | "danger" }) {
  return (
    <section className={`stateMessage ${tone}`} role={tone === "danger" ? "alert" : "status"} aria-label={title}>
      <h2>{title}</h2>
      <p>{message}</p>
    </section>
  );
}

function routeTitle(path: string): string {
  if (path === routes.fiscalReporting) return "Fiscal Reporting - ExitPass Management Platform";
  if (path === routes.fiscalExceptions) {
    return "Fiscal Exceptions - ExitPass Management Platform";
  }
  if (path === routes.salesInvoiceProfiles) {
    return "Sales Invoice Configuration - ExitPass Management Platform";
  }

  if (path === routes.root || path === routes.overview) {
    return "Overview - ExitPass Management Platform";
  }

  return "Not Found - ExitPass Management Platform";
}

function normalizePath(path: string): string {
  return path.replace(/\/$/, "") || routes.root;
}
