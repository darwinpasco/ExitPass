import { createDevelopmentPrincipal } from "./auth";
import { futureSalesInvoiceProfilePermissions, managementPlatformOverviewPermission } from "./permissions";
import type { ManagementPlatformAuthState, ManagementPlatformSite, ManagementPlatformUiError } from "./types";

export type ManagementPlatformManualScenarioName =
  | "authenticated"
  | "unauthenticated"
  | "permission-denied"
  | "multi-site"
  | "no-sites"
  | "unavailable"
  | "not-found";

export interface ManagementPlatformManualScenario {
  name: ManagementPlatformManualScenarioName;
  authState: ManagementPlatformAuthState;
  initialPath?: string;
  error?: ManagementPlatformUiError;
  showIndicator: boolean;
}

const oneSite: ManagementPlatformSite = {
  siteId: "71000000-0000-0000-0000-000000000101",
  sitePosServerId: "72000000-0000-0000-0000-000000000101",
  displayName: "Development Site Alpha"
};

const secondSite: ManagementPlatformSite = {
  siteId: "71000000-0000-0000-0000-000000000102",
  sitePosServerId: "72000000-0000-0000-0000-000000000102",
  displayName: "Development Site Beta"
};

const defaultDevelopmentPermissions = [managementPlatformOverviewPermission, futureSalesInvoiceProfilePermissions.read];

export function resolveManagementPlatformManualScenario(
  isDevelopment: boolean,
  search: string
): ManagementPlatformManualScenario {
  if (!isDevelopment) {
    return authenticatedScenario(false);
  }

  const scenarioName = normalizeScenarioName(new URLSearchParams(search).get("mpScenario"));

  switch (scenarioName) {
    case "unauthenticated":
      return {
        name: scenarioName,
        authState: { status: "unauthenticated" },
        showIndicator: true
      };
    case "permission-denied":
      return {
        name: scenarioName,
        authState: {
          status: "authenticated",
          principal: createDevelopmentPrincipal({
            displayName: "Development Permission Denied User",
            permissions: [],
            authorizedSites: [oneSite]
          })
        },
        showIndicator: true
      };
    case "multi-site":
      return {
        name: scenarioName,
        authState: {
          status: "authenticated",
          principal: createDevelopmentPrincipal({
            displayName: "Development Multi Site User",
            permissions: defaultDevelopmentPermissions,
            authorizedSites: [oneSite, secondSite]
          })
        },
        showIndicator: true
      };
    case "no-sites":
      return {
        name: scenarioName,
        authState: {
          status: "authenticated",
          principal: createDevelopmentPrincipal({
            displayName: "Development No Site User",
            permissions: defaultDevelopmentPermissions,
            authorizedSites: []
          })
        },
        showIndicator: true
      };
    case "unavailable":
      return {
        name: scenarioName,
        authState: {
          status: "authenticated",
          principal: createDevelopmentPrincipal({
            displayName: "Development Unavailable User",
            permissions: defaultDevelopmentPermissions,
            authorizedSites: [oneSite]
          })
        },
        error: {
          kind: "integration-unavailable",
          code: "MANAGEMENT_PLATFORM_DEVELOPMENT_UNAVAILABLE",
          message: "Management Platform is unavailable in this development scenario.",
          correlationId: "dev-scenario-correlation-0001",
          retryable: false,
          mutationUncertain: false
        },
        showIndicator: true
      };
    case "not-found":
      return {
        ...authenticatedScenario(true),
        name: scenarioName,
        initialPath: "/management-platform/development-not-found"
      };
    case "authenticated":
    default:
      return authenticatedScenario(true);
  }
}

function authenticatedScenario(showIndicator: boolean): ManagementPlatformManualScenario {
  return {
    name: "authenticated",
    authState: {
      status: "authenticated",
      principal: createDevelopmentPrincipal({
        permissions: defaultDevelopmentPermissions,
        authorizedSites: [oneSite]
      })
    },
    showIndicator
  };
}

function normalizeScenarioName(value: string | null): ManagementPlatformManualScenarioName {
  switch (value) {
    case "unauthenticated":
    case "permission-denied":
    case "multi-site":
    case "no-sites":
    case "unavailable":
    case "not-found":
      return value;
    default:
      return "authenticated";
  }
}